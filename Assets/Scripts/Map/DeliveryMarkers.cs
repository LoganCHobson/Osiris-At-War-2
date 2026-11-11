using System.Collections.Generic;
using TMPro;
using UnityEngine;

// A label over every planet the player's build orders are delivering to: "INCOMING n SHIPS", counting ships
// still queued at the yards, finished ones gathering there, and convoys on their way. Same orange as the
// BUILD ORDER / CONVOY tags on the fleets themselves (FleetMapIcon).
public class DeliveryMarkers : MonoBehaviour
{
    public Color color = new Color(1f, 0.7f, 0.25f);
    public float height = 2.6f;
    public float refreshInterval = 0.5f;

    private readonly Dictionary<Planet, TextMeshPro> labels = new Dictionary<Planet, TextMeshPro>();
    private readonly Dictionary<string, int> incoming = new Dictionary<string, int>();
    private float nextRefreshAt;
    private Camera cam;

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (Time.unscaledTime >= nextRefreshAt)
        {
            nextRefreshAt = Time.unscaledTime + refreshInterval;
            Count();
        }

        foreach (KeyValuePair<Planet, TextMeshPro> entry in labels)
        {
            if (entry.Key == null || !entry.Value.enabled) continue;
            entry.Value.transform.position = entry.Key.transform.position + Vector3.up * height;
            if (cam != null) entry.Value.transform.rotation = cam.transform.rotation; // Face the camera.
        }
    }

    private void Count()
    {
        incoming.Clear();
        foreach (Planet yard in Planet.All)
        {
            if (yard == null || yard.owner == null || !yard.owner.isPlayerFaction) continue;
            foreach (ShipBuildOrder order in yard.shipBuildQueue)
            {
                if (order != null && !string.IsNullOrEmpty(order.deliverTo)) Add(order.deliverTo, 1);
            }
        }
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (fleet == null || fleet.faction == null || !fleet.faction.isPlayerFaction) continue;
            if (!string.IsNullOrEmpty(fleet.pendingDelivery)) Add(fleet.pendingDelivery, fleet.ships.Count);
            else if (!string.IsNullOrEmpty(fleet.deliveringTo)) Add(fleet.deliveringTo, fleet.ships.Count);
        }

        foreach (Planet planet in Planet.All)
        {
            if (planet == null) continue;
            incoming.TryGetValue(planet.planetName, out int ships);
            TextMeshPro label = Label(planet, ships > 0);
            if (label == null) continue;
            label.enabled = ships > 0;
            if (ships > 0) label.text = $"INCOMING  {ships} SHIP{(ships == 1 ? "" : "S")}";
        }
    }

    private void Add(string planet, int ships)
    {
        incoming.TryGetValue(planet, out int count);
        incoming[planet] = count + ships;
    }

    private TextMeshPro Label(Planet planet, bool create)
    {
        if (labels.TryGetValue(planet, out TextMeshPro label) || !create) return label;

        GameObject go = new GameObject("DeliveryMarker");
        go.transform.SetParent(transform, false);
        label = go.AddComponent<TextMeshPro>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 6f;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(12f, 1.5f);
        label.sortingOrder = 105;
        labels[planet] = label;
        return label;
    }
}
