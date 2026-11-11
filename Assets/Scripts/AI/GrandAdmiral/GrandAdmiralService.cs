using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osiris.GrandAdmiral;
using UnityEngine;
using UnityEngine.Networking;

// Talks to the Grand Admiral server, quietly, in the background. The player never waits on it:
//   * a few seconds after launch (then every few hours), asks for a newer model and installs it if
//     it verifies (takes effect from the next battle);
//   * after a Grand Admiral battle, uploads the battle record if "Share Grand Admiral battles" is on.
//     Records wait in an outbox on disk, so nothing is lost offline or if the game closes;
//   * registers anonymously the first time it uploads (no account, no personal data).
// Every failure just means "try again later". Nothing here can stall or break the game.
public class GrandAdmiralService : MonoBehaviour
{
    private const float FirstCheckDelay = 5f;
    private const float CheckInterval = 6 * 3600f;
    private const int Timeout = 15;
    private const int MaxOutbox = 25;
    private const string CredentialsFile = "install.json";

    private static GrandAdmiralService instance;
    private bool flushing;
    private bool blocked;

    [Serializable] private class Challenge { public string challenge; public int bits; }
    [Serializable] private class RegisterRequest { public string challenge; public string nonce; }
    [Serializable] private class Credentials { public string install_id; public string token; public string server; }
    [Serializable] private class ArenaResult { public int version; public string opponent; public int games; public int wins; public int draws; }

    public static bool Busy => instance != null && instance.flushing;

    public static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("GrandAdmiralService");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GrandAdmiralService>();
    }

    private static string Outbox
    {
        get
        {
            string folder = Path.Combine(GrandAdmiral.Folder, "outbox");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static void Enqueue(GARecord record)
    {
        try
        {
            string[] queued = Directory.GetFiles(Outbox, "*.json");
            if (queued.Length >= MaxOutbox)
            {
                Array.Sort(queued, (a, b) => File.GetCreationTimeUtc(a).CompareTo(File.GetCreationTimeUtc(b)));
                for (int i = 0; i <= queued.Length - MaxOutbox; i++) File.Delete(queued[i]);
            }
            File.WriteAllText(Path.Combine(Outbox, record.BattleId + ".json"), record.ToJson());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GrandAdmiral] Couldn't queue battle: {e.Message}");
            return;
        }
        Ensure();
        instance.Flush();
    }

    public void Flush()
    {
        if (!flushing && !blocked) StartCoroutine(FlushOutbox());
    }

    // Self-play: report win rates to the server (accepted only from self-play installs).
    public static IEnumerator ReportArena(int version, string opponent, int games, int wins, int draws)
    {
        Ensure();
        string body = JsonUtility.ToJson(new ArenaResult { version = version, opponent = opponent, games = games, wins = wins, draws = draws });
        yield return instance.SignedPost("/v1/arena", Encoding.UTF8.GetBytes(body), _ => { });
    }

    // Self-play: make sure we're running the newest released model before the first battle.
    public static IEnumerator CheckNow()
    {
        Ensure();
        yield return instance.CheckForModel();
    }

    // Self-play: play whatever the server says to - the newest trial model if there is one (a candidate
    // waiting for self-play to prove it), else the newest release. Never saved, so the next run asks again.
    public static IEnumerator SelfPlayModel()
    {
        Ensure();
        long code = 0;
        byte[] bundle = null;
        yield return instance.SignedPost("/v1/selfplay/model", Encoding.UTF8.GetBytes("{}"), c => code = c, data => bundle = data);
        if (code == 200 && bundle != null && bundle.Length <= GABundle.MaxModelBytes + 1024 && GrandAdmiral.UseForSelfPlay(bundle))
        {
            Debug.Log($"[GrandAdmiral] Self-play is playing model v{GrandAdmiral.Model.Version}");
        }
        else
        {
            Debug.LogWarning($"[GrandAdmiral] No self-play model from the server ({code}); checking for a release instead");
            yield return instance.CheckForModel();
        }
    }

    // Self-play: wait until queued uploads are done (or have failed for now).
    public static IEnumerator WaitForUploads(float timeout = 120f)
    {
        float until = Time.realtimeSinceStartup + timeout;
        Ensure();
        instance.Flush();
        yield return null;
        while (Busy && Time.realtimeSinceStartup < until) yield return null;
    }

    private IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(FirstCheckDelay);
        while (true)
        {
            yield return CheckForModel();
            Flush();
            yield return new WaitForSecondsRealtime(CheckInterval);
        }
    }

    private IEnumerator CheckForModel()
    {
        int have = GrandAdmiral.Model != null ? GrandAdmiral.Model.Version : 0;
        using (UnityWebRequest request = UnityWebRequest.Get($"{GrandAdmiral.Server}/v1/model/latest?have={have}"))
        {
            request.timeout = Timeout;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200) yield break;
            byte[] bundle = request.downloadHandler.data;
            if (bundle == null || bundle.Length > GABundle.MaxModelBytes + 1024) yield break;
            if (GrandAdmiral.Install(bundle)) Debug.Log($"[GrandAdmiral] Installed model v{GrandAdmiral.Model.Version}");
        }
    }

    private IEnumerator FlushOutbox()
    {
        flushing = true;
        try
        {
            string[] files = Directory.GetFiles(Outbox, "*.json");
            Array.Sort(files, (a, b) => File.GetCreationTimeUtc(a).CompareTo(File.GetCreationTimeUtc(b)));
            foreach (string file in files)
            {
                if (!GameSettings.ShareBattleData) break; // Turned off since it was queued: send nothing.
                byte[] body;
                try { body = File.ReadAllBytes(file); }
                catch { continue; }

                long code = 0;
                yield return SignedPost("/v1/battles", body, c => code = c);
                if (code == 200 || code == 202 || code == 413 || code == 422)
                {
                    TryDelete(file); // Delivered, or the server will never take it: either way, done.
                }
                else if (code == 403)
                {
                    blocked = true;
                    break;
                }
                else
                {
                    break; // Offline, rate limited or server trouble: try again later.
                }
            }
        }
        finally
        {
            flushing = false;
        }
    }

    private IEnumerator SignedPost(string path, byte[] body, Action<long> done, Action<byte[]> response = null)
    {
        Credentials credentials = null;
        yield return EnsureRegistered(c => credentials = c);
        if (credentials == null) { done(0); yield break; }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (UnityWebRequest request = new UnityWebRequest(GrandAdmiral.Server + path, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" };
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Timeout;
                request.SetRequestHeader("X-GA-Install", credentials.install_id);
                request.SetRequestHeader("X-GA-Time", now.ToString(System.Globalization.CultureInfo.InvariantCulture));
                request.SetRequestHeader("X-GA-Signature", GAAuth.Sign(credentials.token, "POST", path, now, body));
                yield return request.SendWebRequest();

                if (request.responseCode == 401 && attempt == 0)
                {
                    // Credentials no longer valid (e.g. the server's key was rotated): register again once.
                    TryDelete(Path.Combine(GrandAdmiral.Folder, CredentialsFile));
                    credentials = null;
                    yield return EnsureRegistered(c => credentials = c);
                    if (credentials == null) { done(0); yield break; }
                    continue;
                }
                if (request.result == UnityWebRequest.Result.Success) response?.Invoke(request.downloadHandler.data);
                done(request.result == UnityWebRequest.Result.ConnectionError ? 0 : request.responseCode);
                yield break;
            }
        }
        done(0);
    }

    private IEnumerator EnsureRegistered(Action<Credentials> done)
    {
        Credentials saved = LoadCredentials();
        if (saved != null) { done(saved); yield break; }

        Challenge challenge;
        using (UnityWebRequest request = UnityWebRequest.Get(GrandAdmiral.Server + "/v1/challenge"))
        {
            request.timeout = Timeout;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { done(null); yield break; }
            challenge = JsonUtility.FromJson<Challenge>(request.downloadHandler.text);
        }
        if (challenge == null || string.IsNullOrEmpty(challenge.challenge) || challenge.bits < 0 || challenge.bits > GAAuth.MaxBits)
        {
            done(null);
            yield break;
        }

        // Proof of work on a worker thread, so the game keeps its frame rate.
        CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task<string> work = Task.Run(() => GAAuth.SolveChallenge(challenge.challenge, challenge.bits, cancel.Token));
        while (!work.IsCompleted) yield return null;
        if (work.IsFaulted || work.IsCanceled) { done(null); yield break; }

        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new RegisterRequest { challenge = challenge.challenge, nonce = work.Result }));
        using (UnityWebRequest request = new UnityWebRequest(GrandAdmiral.Server + "/v1/register", UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" };
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Timeout;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { done(null); yield break; }
            Credentials credentials = JsonUtility.FromJson<Credentials>(request.downloadHandler.text);
            if (credentials == null || string.IsNullOrEmpty(credentials.install_id) || string.IsNullOrEmpty(credentials.token)) { done(null); yield break; }
            credentials.server = GrandAdmiral.Server;
            GrandAdmiral.WriteFileAtomic(CredentialsFile, Encoding.UTF8.GetBytes(JsonUtility.ToJson(credentials)));
            done(credentials);
        }
    }

    private static Credentials LoadCredentials()
    {
        // In-cluster self-play runs with a fixed identity the trainer trusts (-gaCredentials <file>,
        // mounted from a Kubernetes Secret). Players never use this.
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-gaCredentials") continue;
            try
            {
                Credentials fixedIdentity = JsonUtility.FromJson<Credentials>(File.ReadAllText(args[i + 1]));
                if (fixedIdentity != null && !string.IsNullOrEmpty(fixedIdentity.token)) return fixedIdentity;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GrandAdmiral] Couldn't read -gaCredentials: {e.Message}");
            }
            return null;
        }

        byte[] data = GrandAdmiral.ReadFile(CredentialsFile);
        if (data == null) return null;
        try
        {
            Credentials credentials = JsonUtility.FromJson<Credentials>(Encoding.UTF8.GetString(data));
            // Credentials belong to one server; pointing the game elsewhere (e.g. a dev server) registers afresh.
            return credentials != null && credentials.server == GrandAdmiral.Server && !string.IsNullOrEmpty(credentials.token) ? credentials : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* next time */ }
    }
}
