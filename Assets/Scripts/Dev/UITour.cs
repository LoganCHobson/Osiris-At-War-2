using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Dev tool: walks through the menus (main menu, settings, difficulty picker, galactic map, pause menu,
// battle) and screenshots each, so UI layout can be checked without clicking through by hand.
// Started by UITourRunner (Osiris > Run UI Tour); quits when done.
public class UITour : MonoBehaviour
{
    public string outputDir;

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(outputDir);

        yield return Wait(4f);
        yield return Shot("1_main_menu");

        MainMenuController menu = FindFirstObjectByType<MainMenuController>();
        Call(menu, "OpenOptions");
        yield return Wait(1f);
        yield return Shot("2_settings_audio");
        Click("DISPLAY");
        yield return Wait(0.5f);
        yield return Shot("3_settings_display");
        Click("CONTROLS");
        yield return Wait(0.5f);
        yield return Shot("4_settings_controls");
        FindFirstObjectByType<SettingsPanel>()?.Close();
        yield return Wait(1f);

        Call(menu, "ChooseDifficulty");
        yield return Wait(1f);
        yield return Shot("5_difficulty");

        SceneManager.LoadScene("GalacticMap");
        yield return Wait(4f);
        yield return Shot("6_galactic_map");

        PauseMenu pause = FindFirstObjectByType<PauseMenu>();
        pause?.Open();
        yield return Wait(0.5f);
        yield return Shot("7_pause_menu");
        pause?.Close();

        SceneManager.LoadScene("BattleScene");
        yield return Wait(5f);
        yield return Shot("8_battle");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }

    private IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(outputDir, name + ".png"), image.EncodeToPNG());
        Destroy(image);
        Debug.Log($"UITOUR {name}");
    }

    private static void Call(object target, string method)
    {
        target?.GetType().GetMethod(method, Any)?.Invoke(target, null);
    }

    // Presses the first UI button whose label matches.
    private static void Click(string label)
    {
        foreach (TMPro.TMP_Text text in FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None))
        {
            if (text.text != label) continue;
            UnityEngine.UI.Button button = text.GetComponentInParent<UnityEngine.UI.Button>();
            if (button == null) continue;
            button.onClick.Invoke();
            return;
        }
    }
}
