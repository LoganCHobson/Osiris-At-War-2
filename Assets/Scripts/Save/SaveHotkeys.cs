using UnityEngine;
using UnityEngine.SceneManagement;

// On the galactic map: F5 quicksaves, F9 quickloads, and the campaign autosaves every few minutes and
// after each battle. Added automatically when the galactic map loads.
public class SaveHotkeys : MonoBehaviour
{
    public KeyCode quicksaveKey = KeyCode.F5;
    public KeyCode quickloadKey = KeyCode.F9;
    [Tooltip("Minutes of play between autosaves.")]
    public float autosaveMinutes = 5f;

    private static bool autosaveRequested;
    private float nextAutosaveAt;

    // Asks for an autosave as soon as saving is allowed (e.g. right after a battle's results are in).
    public static void RequestAutosave() => autosaveRequested = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "GalacticMap" || FindFirstObjectByType<SaveHotkeys>() != null) return;
        new GameObject("SaveHotkeys").AddComponent<SaveHotkeys>();
    }

    private void Start()
    {
        nextAutosaveAt = Time.unscaledTime + autosaveMinutes * 60f;
    }

    private void Update()
    {
        bool menuOpen = GameSpeed.MenuPaused || SaveLoadPanel.IsOpen || SettingsPanel.IsOpen;

        if (!menuOpen && Input.GetKeyDown(quicksaveKey))
        {
            Toast.Show(SaveSystem.Quicksave(out string error) ? "Quicksaved" : error);
        }
        else if (!menuOpen && Input.GetKeyDown(quickloadKey))
        {
            if (!SaveSystem.HasQuicksave) Toast.Show("No quicksave yet");
            else if (!SaveSystem.Load(SaveSystem.QuicksaveSlot, out string error)) Toast.Show(error);
            return;
        }

        bool due = autosaveRequested || Time.unscaledTime >= nextAutosaveAt;
        if (!due || !SaveSystem.CanSave(out _)) return;

        autosaveRequested = false;
        nextAutosaveAt = Time.unscaledTime + autosaveMinutes * 60f;
        if (SaveSystem.Autosave(out string autosaveError)) Toast.Show("Autosaved");
        else Debug.LogWarning(autosaveError);
    }
}
