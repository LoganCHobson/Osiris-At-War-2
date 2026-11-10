using System.Collections.Generic;
using UnityEngine;

// Every rebindable hotkey in one place (Settings > Controls > Key Bindings). Saved in PlayerPrefs.
// Fixed and not listed: Escape, the number keys (game speed / control groups) and the Shift/Ctrl/Alt
// modifiers, which change what other keys and clicks do.
public enum KeyAction
{
    CameraForward, CameraBack, CameraLeft, CameraRight, RotateLeft, RotateRight,
    Guard, PriorityMove, Ability, Stance,
    Climb, Dive, ToPlane, RollLeft, RollRight, PitchUp, PitchDown, Level, VerticalFormation, SideView,
    Pause, Production, Help, Quicksave, Quickload, CinematicCamera, Overview
}

public static class Keybinds
{
    private const string Prefix = "osiris.key.";

    public static readonly Dictionary<KeyAction, KeyCode> Defaults = new Dictionary<KeyAction, KeyCode>
    {
        { KeyAction.CameraForward, KeyCode.W }, { KeyAction.CameraBack, KeyCode.S },
        { KeyAction.CameraLeft, KeyCode.A }, { KeyAction.CameraRight, KeyCode.D },
        { KeyAction.RotateLeft, KeyCode.Q }, { KeyAction.RotateRight, KeyCode.E },
        { KeyAction.Guard, KeyCode.G }, { KeyAction.PriorityMove, KeyCode.F },
        { KeyAction.Ability, KeyCode.B }, { KeyAction.Stance, KeyCode.Y },
        { KeyAction.Climb, KeyCode.PageUp }, { KeyAction.Dive, KeyCode.PageDown }, { KeyAction.ToPlane, KeyCode.End },
        { KeyAction.RollLeft, KeyCode.Z }, { KeyAction.RollRight, KeyCode.X },
        { KeyAction.PitchUp, KeyCode.R }, { KeyAction.PitchDown, KeyCode.T },
        { KeyAction.Level, KeyCode.Home }, { KeyAction.VerticalFormation, KeyCode.L }, { KeyAction.SideView, KeyCode.H },
        { KeyAction.Pause, KeyCode.Space }, { KeyAction.Production, KeyCode.P }, { KeyAction.Help, KeyCode.F1 },
        { KeyAction.Quicksave, KeyCode.F5 }, { KeyAction.Quickload, KeyCode.F9 },
        { KeyAction.CinematicCamera, KeyCode.C }, { KeyAction.Overview, KeyCode.V },
    };

    public static readonly HashSet<KeyCode> Reserved = new HashSet<KeyCode>
    {
        KeyCode.Escape, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
        KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2,
        KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
        KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
    };

    private static Dictionary<KeyAction, KeyCode> current;

    private static Dictionary<KeyAction, KeyCode> Current
    {
        get
        {
            if (current != null) return current;
            current = new Dictionary<KeyAction, KeyCode>();
            foreach (KeyValuePair<KeyAction, KeyCode> entry in Defaults)
            {
                int saved = PlayerPrefs.GetInt(Prefix + entry.Key, (int)entry.Value);
                current[entry.Key] = (KeyCode)saved;
            }
            return current;
        }
    }

    public static KeyCode Get(KeyAction action) => Current.TryGetValue(action, out KeyCode key) ? key : KeyCode.None;
    public static bool Down(KeyAction action) => Input.GetKeyDown(Get(action));
    public static bool Held(KeyAction action) => Input.GetKey(Get(action));

    // Binds a key; if another action had it, that action takes this one's old key (a swap), so nothing is
    // left unbound or doubly bound. Returns the action that was swapped, if any.
    public static KeyAction? Set(KeyAction action, KeyCode key)
    {
        if (Reserved.Contains(key)) return null;
        KeyCode old = Get(action);
        KeyAction? swapped = null;
        foreach (KeyValuePair<KeyAction, KeyCode> entry in new List<KeyValuePair<KeyAction, KeyCode>>(Current))
        {
            if (entry.Key != action && entry.Value == key)
            {
                Current[entry.Key] = old;
                PlayerPrefs.SetInt(Prefix + entry.Key, (int)old);
                swapped = entry.Key;
            }
        }
        Current[action] = key;
        PlayerPrefs.SetInt(Prefix + action, (int)key);
        PlayerPrefs.Save();
        return swapped;
    }

    public static void ResetAll()
    {
        foreach (KeyValuePair<KeyAction, KeyCode> entry in Defaults)
        {
            PlayerPrefs.DeleteKey(Prefix + entry.Key);
        }
        PlayerPrefs.Save();
        current = null;
    }

    public static string Label(KeyAction action)
    {
        switch (action)
        {
            case KeyAction.CameraForward: return "Camera forward";
            case KeyAction.CameraBack: return "Camera back";
            case KeyAction.CameraLeft: return "Camera left";
            case KeyAction.CameraRight: return "Camera right";
            case KeyAction.RotateLeft: return "Rotate camera left";
            case KeyAction.RotateRight: return "Rotate camera right";
            case KeyAction.PriorityMove: return "Priority move";
            case KeyAction.Ability: return "Use ability";
            case KeyAction.Stance: return "Cycle stance";
            case KeyAction.ToPlane: return "Back to the plane";
            case KeyAction.RollLeft: return "Roll left";
            case KeyAction.RollRight: return "Roll right";
            case KeyAction.PitchUp: return "Pitch nose up";
            case KeyAction.PitchDown: return "Pitch nose down";
            case KeyAction.Level: return "Level out / auto-angle";
            case KeyAction.VerticalFormation: return "Vertical formation";
            case KeyAction.SideView: return "Side view";
            case KeyAction.Pause: return "Pause / resume";
            case KeyAction.Help: return "Controls help";
            case KeyAction.CinematicCamera: return "Cinematic camera";
            case KeyAction.Overview: return "Cinematic overview";
            default: return action.ToString();
        }
    }

    public static string KeyName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.PageUp: return "Page Up";
            case KeyCode.PageDown: return "Page Down";
            case KeyCode.None: return "-";
            default: return key.ToString();
        }
    }
}
