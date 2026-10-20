using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The fleets parked at a planet, one lane per fleet slot, in a band just above the build menu. Drag ships
// between lanes to reorganise; an empty lane takes them as a new fleet. Built at runtime in the shared
// UIKit style; the old scene-built panel is hidden on start.
public class FleetPanel : MonoBehaviour
{
    public static FleetPanel Instance;

    [Header("Old scene panel (hidden; the panel now builds itself)")]
    public GameObject panelRoot;

    [Tooltip("Spawned when ships are dragged into an empty lane, or a finished build needs a new fleet.")]
    public GameObject newFleetPrefab;

    private const float BandHeight = 250f;
    private const float BandBottom = 16f + 336f + 10f; // Sits on top of the build menu.

    public bool IsOpen => band != null && band.gameObject.activeSelf;

    private readonly List<FleetRowUI> spawnedRows = new List<FleetRowUI>();
    private Planet currentPlanet;
    private RectTransform band;
    private RectTransform lanes;

    private void Awake()
    {
        Instance = this;
        if (panelRoot != null) panelRoot.SetActive(false);
        Build();
    }

    public void Show(Planet planet)
    {
        currentPlanet = planet;
        Refresh();
    }

    public void Hide()
    {
        currentPlanet = null;
        Clear();
        band.gameObject.SetActive(false);
    }

    public void Refresh()
    {
        Clear();
        if (currentPlanet == null)
        {
            band.gameObject.SetActive(false);
            return;
        }

        bool mine = currentPlanet.owner != null && currentPlanet.owner.isPlayerFaction;
        bool anyFleet = false;
        for (int slot = 0; slot < Planet.FleetSlotCount; slot++)
        {
            GalacticFleet fleet = currentPlanet.GetFleetInSlot(slot);
            if (fleet != null && !fleet.IsRevealed) fleet = null;
            anyFleet |= fleet != null && fleet.roster.Count > 0;

            if (slot > 0) Divider();
            spawnedRows.Add(FleetRowUI.Create(lanes, fleet, currentPlanet, slot, this, mine));
        }

        // Nothing to see at someone else's planet with no fleets in sight.
        band.gameObject.SetActive(mine || anyFleet);
    }

    private void Clear()
    {
        foreach (Transform child in lanes) Destroy(child.gameObject);
        spawnedRows.Clear();
    }

    public GalacticFleet CreateFleetInSlot(Planet planet, int slot)
    {
        if (newFleetPrefab == null)
        {
            Debug.LogWarning("FleetPanel.newFleetPrefab is not assigned - can't create a new fleet from an empty lane.");
            return null;
        }

        GameObject obj = Instantiate(newFleetPrefab, planet.GetSlotPosition(slot), Quaternion.identity);
        obj.name = $"Fleet_{planet.planetName}_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}";
        GalacticFleet fleet = obj.GetComponent<GalacticFleet>();
        if (fleet == null)
        {
            Destroy(obj);
            return null;
        }

        fleet.roster.Clear(); // The template prefab may carry stale/unresolved roster entries - a freshly split fleet always starts empty.
        fleet.currentPlanet = planet;
        planet.ForceClaimSlot(fleet, slot);
        return fleet;
    }

    // ---------------- Layout ----------------

    private void Build()
    {
        // Its own scene root: this script sits inside the old panel, which is hidden. Under the build menu
        // (sort order), whose ship stat tooltips pop up over this band.
        Canvas canvas = UIKit.Canvas("FleetPanelCanvas", 39);

        band = UIKit.Rect("Fleets", canvas.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, BandBottom), new Vector2(-40f, BandHeight));
        UIKit.Image(band, UIKit.PanelColor, true);
        RectTransform rule = UIKit.Rect("Rule", band, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f));
        UIKit.Image(rule, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.55f));

        lanes = UIKit.Fill("Lanes", band);
        HorizontalLayoutGroup layout = lanes.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        band.gameObject.SetActive(false);
    }

    private void Divider()
    {
        RectTransform holder = UIKit.Rect("Divider", lanes, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        holder.gameObject.AddComponent<LayoutElement>().preferredWidth = 1f;
        RectTransform line = UIKit.Rect("Line", holder, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, -32f));
        UIKit.Image(line, new Color(1f, 1f, 1f, 0.08f));
    }
}
