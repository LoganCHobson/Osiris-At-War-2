using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Win and lose conditions, checked on the galactic map. Victory: the player holds every system in the galaxy.
// Defeat: the player has lost every system, or has no ships left and no capital shipyard to build more.
// Shows a Victory or Defeat screen and freezes the game. After a victory the player can keep playing.
public class GameOutcome : MonoBehaviour
{
    private const float CheckInterval = 0.5f;

    private static GalacticState victoryShownFor; // The campaign whose victory was already shown (a new or loaded one starts fresh).

    private Faction player;
    private Canvas canvas;
    private RectTransform window;
    private float nextCheckAt;
    private int startFrame;

    public static bool IsShowing { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "GalacticMap") return;
        if (FindFirstObjectByType<GameOutcome>() != null) return;
        new GameObject("GameOutcome").AddComponent<GameOutcome>();
    }

    private void Start()
    {
        startFrame = Time.frameCount;
    }

    private void OnDestroy()
    {
        if (IsShowing) GameSpeed.SetMenuPaused(false);
        IsShowing = false;
    }

    private void Update()
    {
        if (IsShowing || Time.frameCount < startFrame + 2) return; // Let the map restore the galaxy and any battle result first.
        if (Time.unscaledTime < nextCheckAt) return;
        nextCheckAt = Time.unscaledTime + CheckInterval;

        player ??= FindPlayerFaction();
        if (player == null) return;

        int planets = 0, ownedPlanets = 0, shipyards = 0, ships = 0;
        foreach (Planet planet in Planet.All)
        {
            if (planet == null) continue;
            planets++;
            if (planet.owner != player) continue;
            ownedPlanets++;
            if (planet.hasCapitalShipyard) shipyards++;
        }
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet != null && fleet.faction == player) ships += fleet.ships.Count;
        }

        if (ownedPlanets == 0)
        {
            Show(false, "Every system under your banner has fallen.");
        }
        else if (ships == 0 && shipyards == 0)
        {
            Show(false, "Your fleets are destroyed and no shipyard remains to build more.");
        }
        else if (ownedPlanets == planets && victoryShownFor != GalacticState.Instance)
        {
            victoryShownFor = GalacticState.Instance;
            Show(true, "Every system in the galaxy answers to " + player.factionName + ".");
        }
    }

    // From the map, or from the campaign's treasuries once the player has nothing left on it.
    private static Faction FindPlayerFaction()
    {
        foreach (Planet planet in Planet.All)
        {
            if (planet != null && Strength.IsPlayer(planet.owner)) return planet.owner;
        }
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet != null && Strength.IsPlayer(fleet.faction)) return fleet.faction;
        }
        if (GalacticState.Instance != null)
        {
            foreach (GalacticState.TreasurySnapshot treasury in GalacticState.Instance.treasuries)
            {
                if (Strength.IsPlayer(treasury.faction)) return treasury.faction;
            }
        }
        return null;
    }

    private void Show(bool victory, string reason)
    {
        IsShowing = true;
        GameSpeed.SetMenuPaused(true);
        if (PauseMenu.IsOpen) FindFirstObjectByType<PauseMenu>()?.Close();
        FleetPanel.Instance?.Hide();
        PlanetBuildPanel.Instance?.Hide();

        canvas = UIKit.Canvas("OutcomeCanvas", 950, transform);
        RectTransform root = UIKit.Fill("Outcome", canvas.transform);
        UIKit.Image(root, UIKit.Dim, true);

        window = UIKit.Rect("Window", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 440f));
        UIKit.Image(window, UIKit.PanelColor, true);

        Color accent = victory ? UIKit.Accent : new Color(0.9f, 0.32f, 0.28f);

        RectTransform header = UIKit.Rect("Header", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(0f, 110f));
        TMP_Text title = UIKit.Text(header, victory ? "VICTORY" : "DEFEAT", 64f, accent, TextAlignmentOptions.Center);
        title.characterSpacing = 22f;
        title.fontStyle = FontStyles.Bold;

        RectTransform rule = UIKit.Rect("Rule", window, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -135f), new Vector2(340f, 2f));
        UIKit.Image(rule, new Color(accent.r, accent.g, accent.b, 0.6f));

        RectTransform textArea = UIKit.Rect("Reason", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -155f), new Vector2(-80f, 90f));
        TMP_Text text = UIKit.Text(textArea, reason, 28f, UIKit.TextColor, TextAlignmentOptions.Center);
        text.textWrappingMode = TextWrappingModes.Normal;

        RectTransform list = UIKit.Rect("Buttons", window, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(380f, 140f));
        UIKit.List(list, 10f);
        if (victory) UIKit.MenuButton(list, "KEEP PLAYING", 32f, new Vector2(380f, 62f), Hide);
        else UIKit.MenuButton(list, "LOAD GAME", 32f, new Vector2(380f, 62f), OpenLoad);
        UIKit.MenuButton(list, "MAIN MENU", 32f, new Vector2(380f, 62f), SaveSystem.ReturnToMainMenu);
    }

    private void Hide()
    {
        IsShowing = false;
        GameSpeed.SetMenuPaused(false);
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void OpenLoad()
    {
        window.gameObject.SetActive(false);
        SaveLoadPanel.Open(canvas.transform, SaveLoadPanel.Mode.Load, false, () =>
        {
            if (window != null) window.gameObject.SetActive(true);
        });
    }
}
