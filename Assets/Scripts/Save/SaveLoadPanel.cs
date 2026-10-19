using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Save / Load window, used by the pause menu (save and load) and the main menu (load).
public class SaveLoadPanel : MonoBehaviour
{
    public enum Mode { Save, Load }

    private Mode mode;
    private bool confirmLoad;
    private System.Action onClose;
    private RectTransform list;
    private RectTransform confirm;

    public static bool IsOpen { get; private set; }

    public static SaveLoadPanel Open(Transform canvas, Mode mode, bool confirmLoad, System.Action onClose)
    {
        RectTransform root = UIKit.Fill(mode == Mode.Save ? "SaveGame" : "LoadGame", canvas);
        SaveLoadPanel panel = root.gameObject.AddComponent<SaveLoadPanel>();
        panel.mode = mode;
        panel.confirmLoad = confirmLoad;
        panel.onClose = onClose;
        panel.Build(root);
        IsOpen = true;
        return panel;
    }

    public void Close()
    {
        IsOpen = false;
        onClose?.Invoke();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        IsOpen = false;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (confirm != null) CloseConfirm();
        else Close();
    }

    private void Build(RectTransform root)
    {
        UIKit.Image(root, UIKit.Dim, true);

        RectTransform window = UIKit.Rect("Window", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 760f));
        UIKit.Image(window, UIKit.PanelColor, true);

        RectTransform header = UIKit.Rect("Header", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 90f));
        TMP_Text title = UIKit.Text(header, mode == Mode.Save ? "SAVE GAME" : "LOAD GAME", 44f, UIKit.TextColor);
        title.rectTransform.offsetMin = new Vector2(40f, 0f);
        title.characterSpacing = 14f;
        title.fontStyle = FontStyles.Bold;

        RectTransform rule = UIKit.Rect("Rule", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(-80f, 2f));
        UIKit.Image(rule, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.5f));

        // Scrolling list of saves.
        RectTransform viewport = UIKit.Rect("Viewport", window, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(40f, 100f);
        viewport.offsetMax = new Vector2(-40f, -110f);
        UIKit.Image(viewport, new Color(0f, 0f, 0f, 0.01f), true);
        viewport.gameObject.AddComponent<RectMask2D>();

        list = UIKit.Rect("List", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        UIKit.List(list, 8f);
        ContentSizeFitter fit = list.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = list;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        RectTransform footer = UIKit.Rect("Footer", window, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(260f, 60f));
        Button back = UIKit.MenuButton(footer, "BACK", 32f, new Vector2(260f, 60f), Close);
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = backRect.anchorMax = backRect.pivot = Vector2.zero;

        Refresh();
    }

    private void Refresh()
    {
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);

        if (mode == Mode.Save)
        {
            bool canSave = SaveSystem.CanSave(out string reason);
            Button create = UIKit.MenuButton(list, canSave ? "+  NEW SAVE" : reason, 30f, new Vector2(900f, 64f), () => SaveTo(SaveSystem.NewSlot(), null), canSave);
            create.gameObject.name = "NewSave";
        }

        List<SaveSummary> saves = SaveSystem.List();
        if (saves.Count == 0)
        {
            RectTransform empty = UIKit.Rect("Empty", list, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(900f, 60f));
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            UIKit.Text(empty, "No saved campaigns yet.", 26f, UIKit.MutedColor, TextAlignmentOptions.Center);
        }

        foreach (SaveSummary save in saves) AddRow(save);
    }

    private void AddRow(SaveSummary save)
    {
        RectTransform row = UIKit.Rect(save.slot, list, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(900f, 84f));
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
        Image background = UIKit.Image(row, UIKit.RowColor, true);

        string label = save.IsQuicksave ? "Quicksave" : save.IsAutosave ? "Autosave" : save.data.displayName;
        TMP_Text name = UIKit.Text(row, $"<b>{label}</b>", 30f, UIKit.TextColor, TextAlignmentOptions.TopLeft);
        name.rectTransform.offsetMin = new Vector2(24f, 0f);
        name.rectTransform.offsetMax = new Vector2(-80f, -10f);

        string details = $"{(DifficultyLevel)save.data.difficulty}   ·   {save.data.playerPlanets} planets   ·   {save.data.playerShips} ships   ·   ${save.data.playerCredits:N0}   ·   {save.savedAt:MMM d, h:mm tt}";
        TMP_Text info = UIKit.Text(row, details, 20f, UIKit.MutedColor, TextAlignmentOptions.BottomLeft);
        info.rectTransform.offsetMin = new Vector2(24f, 10f);
        info.rectTransform.offsetMax = new Vector2(-80f, 0f);

        Button pick = row.gameObject.AddComponent<Button>();
        ColorBlock colors = pick.colors;
        colors.highlightedColor = new Color(1.6f, 1.5f, 1.2f, 1f);
        pick.colors = colors;
        pick.targetGraphic = background;
        pick.onClick.AddListener(() =>
        {
            if (mode == Mode.Save)
            {
                if (SaveSystem.CanSave(out _)) Confirm($"Overwrite \"{label}\"?", () => SaveTo(save.slot, save.IsAutosave || save.IsQuicksave ? null : save.data.displayName));
            }
            else if (confirmLoad)
            {
                Confirm($"Load \"{label}\"?\nUnsaved progress will be lost.", () => LoadFrom(save.slot));
            }
            else
            {
                LoadFrom(save.slot);
            }
        });

        RectTransform delete = UIKit.Rect("Delete", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(52f, 52f));
        Image deleteBackground = UIKit.Image(delete, new Color(1f, 0.3f, 0.25f, 0.12f), true);
        UIKit.Text(delete, "X", 26f, new Color(1f, 0.45f, 0.4f), TextAlignmentOptions.Center);
        Button deleteButton = delete.gameObject.AddComponent<Button>();
        deleteButton.targetGraphic = deleteBackground;
        deleteButton.onClick.AddListener(() => Confirm($"Delete \"{label}\"?", () =>
        {
            SaveSystem.Delete(save.slot);
            CloseConfirm();
            Refresh();
        }));
    }

    private void SaveTo(string slot, string displayName)
    {
        CloseConfirm();
        if (SaveSystem.Save(slot, displayName, out string error))
        {
            Toast.Show("Game saved");
            Close();
        }
        else
        {
            Toast.Show(error);
        }
    }

    private void LoadFrom(string slot)
    {
        CloseConfirm();
        if (!SaveSystem.Load(slot, out string error)) Toast.Show(error);
    }

    private void Confirm(string question, UnityAction yes)
    {
        CloseConfirm();
        confirm = UIKit.Fill("Confirm", transform);
        UIKit.Image(confirm, UIKit.Dim, true);

        RectTransform window = UIKit.Rect("Window", confirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 300f));
        UIKit.Image(window, UIKit.PanelColor, true);

        RectTransform textArea = UIKit.Rect("Question", window, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(-60f, 140f));
        TMP_Text text = UIKit.Text(textArea, question, 30f, UIKit.TextColor, TextAlignmentOptions.Center);
        text.textWrappingMode = TextWrappingModes.Normal;

        RectTransform buttons = UIKit.Rect("Buttons", window, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(-120f, 64f));
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
    }
}
