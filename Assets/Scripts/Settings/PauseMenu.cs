using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Esc menu on the galactic map and in battle: Resume, Settings, Main Menu, Quit. Freezes the game while
// open. Esc only opens it when nothing else wants the key (an open panel, an armed command, the
// cinematic camera, a followed ship, a battle prompt) - those still get first claim on Esc.
[DefaultExecutionOrder(-1000)]
public class PauseMenu : MonoBehaviour
{
    private Canvas canvas;
    private RectTransform main;
    private RectTransform confirm;
    private bool open;

    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, _) => AttachTo(scene);
        AttachTo(SceneManager.GetActiveScene());
    }

    private static void AttachTo(Scene scene)
    {
        if (scene.name != "GalacticMap" && scene.name != "BattleScene") return;
        if (FindFirstObjectByType<PauseMenu>() != null) return;
        new GameObject("PauseMenu").AddComponent<PauseMenu>();
    }

    private void Start()
    {
        canvas = UIKit.Canvas("PauseCanvas", 900, transform);
        BuildMain();
        canvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (IsOpen) GameSpeed.SetMenuPaused(false);
        IsOpen = false;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape) || SettingsPanel.IsOpen || SaveLoadPanel.IsOpen) return;

        if (open)
        {
            if (confirm != null) CloseConfirm();
            else Close();
            return;
        }

        if (!EscapeIsBusy()) Open();
    }

    // Something else already uses Esc right now - let it.
    private static bool EscapeIsBusy()
    {
        if (CinematicCamera.Active || BattlePrompt.IsOpen) return true;
        if (FleetPanel.Instance != null && FleetPanel.Instance.panelRoot != null && FleetPanel.Instance.panelRoot.activeSelf) return true;
        if (PlanetBuildPanel.Instance != null && PlanetBuildPanel.Instance.IsOpen) return true;
        if (RTSCameraController.instance != null && RTSCameraController.instance.followTransform != null) return true;

        PlayerSpaceManager space = FindFirstObjectByType<PlayerSpaceManager>();
        return space != null && space.Mode != PlayerSpaceManager.CommandMode.None;
    }

    public void Open()
    {
        open = true;
        IsOpen = true;
        canvas.gameObject.SetActive(true);
        main.gameObject.SetActive(true);
        GameSpeed.SetMenuPaused(true);
    }

    public void Close()
    {
        open = false;
        IsOpen = false;
        CloseConfirm();
        canvas.gameObject.SetActive(false);
        GameSpeed.SetMenuPaused(false);
    }

    private void BuildMain()
    {
        RectTransform root = UIKit.Fill("Pause", canvas.transform);
        UIKit.Image(root, UIKit.Dim, true);

        bool onGalacticMap = SceneManager.GetActiveScene().name == "GalacticMap";
        main = UIKit.Rect("Window", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, onGalacticMap ? 660f : 590f));
        UIKit.Image(main, UIKit.PanelColor, true);

        RectTransform header = UIKit.Rect("Header", main, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));
        TMP_Text title = UIKit.Text(header, "PAUSED", 46f, UIKit.TextColor, TextAlignmentOptions.Center);
        title.characterSpacing = 18f;
        title.fontStyle = FontStyles.Bold;

        RectTransform rule = UIKit.Rect("Rule", main, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(300f, 2f));
        UIKit.Image(rule, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.6f));

        RectTransform list = UIKit.Rect("Buttons", main, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        list.offsetMin = new Vector2(70f, 40f);
        list.offsetMax = new Vector2(-70f, -130f);
        UIKit.List(list, 8f);

        UIKit.MenuButton(list, "RESUME", 34f, new Vector2(380f, 62f), Close);
        if (onGalacticMap) UIKit.MenuButton(list, "SAVE GAME", 34f, new Vector2(380f, 62f), () => OpenSaves(SaveLoadPanel.Mode.Save));
        UIKit.MenuButton(list, "LOAD GAME", 34f, new Vector2(380f, 62f), () => OpenSaves(SaveLoadPanel.Mode.Load));
        UIKit.MenuButton(list, "SETTINGS", 34f, new Vector2(380f, 62f), OpenSettings);
        UIKit.MenuButton(list, "MAIN MENU", 34f, new Vector2(380f, 62f), () => Confirm("Leave to the main menu?\nUnsaved progress will be lost.", ToMainMenu));
        UIKit.MenuButton(list, "QUIT GAME", 34f, new Vector2(380f, 62f), () => Confirm("Quit to desktop?\nUnsaved progress will be lost.", QuitGame));
    }

    private void OpenSaves(SaveLoadPanel.Mode mode)
    {
        main.gameObject.SetActive(false);
        SaveLoadPanel.Open(canvas.transform, mode, true, () =>
        {
            if (main != null && open) main.gameObject.SetActive(true);
        });
    }

    private void OpenSettings()
    {
        main.gameObject.SetActive(false);
        SettingsPanel.Open(canvas.transform, () =>
        {
            if (main != null) main.gameObject.SetActive(true);
        });
    }

    private void Confirm(string question, UnityEngine.Events.UnityAction yes)
    {
        main.gameObject.SetActive(false);

        confirm = UIKit.Rect("Confirm", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 300f));
        UIKit.Image(confirm, UIKit.PanelColor, true);

        RectTransform textArea = UIKit.Rect("Question", confirm, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(-60f, 140f));
        TMP_Text text = UIKit.Text(textArea, question, 30f, UIKit.TextColor, TextAlignmentOptions.Center);
        text.textWrappingMode = TextWrappingModes.Normal;

        RectTransform buttons = UIKit.Rect("Buttons", confirm, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(-120f, 64f));
        HorizontalLayoutGroup row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 40f;
        row.childControlWidth = true;
        row.childForceExpandWidth = true;
        UIKit.MenuButton(buttons, "YES", 32f, new Vector2(200f, 60f), yes);
        UIKit.MenuButton(buttons, "NO", 32f, new Vector2(200f, 60f), CloseConfirm);
    }

    private void CloseConfirm()
    {
        if (confirm == null) return;
        Destroy(confirm.gameObject);
        confirm = null;
        if (open && main != null) main.gameObject.SetActive(true);
    }

    // Leaving the campaign: drop everything that survives scene loads so a new campaign starts clean.
    private void ToMainMenu()
    {
        Close();
        SaveSystem.ReturnToMainMenu();
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
