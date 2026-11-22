using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Spy probes: small unmanned scouts, built at a shipyard and flown along the hyperlanes to any planet,
// where they park in the planet's own spot (below the planet, inside the fleet ring - the spot ground units
// will use later). They never fight, never start a battle and pass through enemy systems untouched.
//
// Parked over a planet, a probe shows its owner everything about it: the planet and its neighbours as if
// a fleet sat there (GalacticVisibility), and the build queue in the planet panel (PlanetBuildPanel). The
// enemy never knows: other factions' probes are never drawn and nothing in the game reacts to them. Once a
// probe first parks over a planet that isn't its owner's, its clock starts; when it runs out the probe
// goes dark and is gone.
//
// The probes and their build orders are plain data in GalacticState, so they survive battles (scene loads)
// and go into saves; SpyProbeLayer draws and flies them on the galactic map.
public static class SpyProbes
{
    public const int Cost = 150;
    public const float BuildTime = 30f;         // galactic seconds
    public const float Lifetime = 240f;         // galactic seconds, from first parking over someone else's planet
    public const float SpeedMultiplier = 2.5f;  // times a fleet's travel speed

    [System.Serializable]
    public class Probe
    {
        public string faction;                          // SaveRegistry.Id
        public string planet;                           // where it is parked, or the last planet it passed
        public List<string> route = new List<string>(); // planets still to reach, in order
        public float x, y, z;                           // position on the map
        public float lifeLeft = -1f;                    // < 0: not yet started

        public bool Parked => route.Count == 0;
        public Vector3 Position { get => new Vector3(x, y, z); set { x = value.x; y = value.y; z = value.z; } }
    }

    [System.Serializable]
    public class Order
    {
        public string faction;
        public string planet;
        public float remaining;
    }

    public static List<Probe> All => GalacticState.Instance != null ? GalacticState.Instance.probes : Empty;
    public static List<Order> Orders => GalacticState.Instance != null ? GalacticState.Instance.probeOrders : EmptyOrders;
    private static readonly List<Probe> Empty = new List<Probe>();
    private static readonly List<Order> EmptyOrders = new List<Order>();

    public static Faction Player => SaveRegistry.Factions.FirstOrDefault(f => f != null && f.isPlayerFaction);

    // A probe of this faction parked over this planet.
    public static bool Watching(Faction viewer, Planet planet)
    {
        if (viewer == null || planet == null) return false;
        string id = SaveRegistry.Id(viewer);
        return All.Exists(p => p.faction == id && p.Parked && p.planet == planet.planetName);
    }

    public static IEnumerable<string> WatchedPlanets(Faction viewer)
    {
        string id = SaveRegistry.Id(viewer);
        return All.Where(p => p.faction == id && p.Parked).Select(p => p.planet);
    }

    // ---- building ----

    public static string WhyNot(Planet planet)
    {
        if (planet == null || planet.owner == null || !planet.owner.isPlayerFaction) return "Not your planet";
        if (!planet.hasCapitalShipyard) return "Needs a capital shipyard";
        if (GalacticState.Instance == null || GalacticState.Instance.GetCurrency(planet.owner) < Cost) return "Not enough credits";
        return null;
    }

    public static bool Queue(Planet planet)
    {
        if (WhyNot(planet) != null || !GalacticState.Instance.TrySpend(planet.owner, Cost)) return false;
        Enqueue(planet);
        return true;
    }

    // An order already paid for (the AI's economy pays as it chooses).
    public static void Enqueue(Planet planet)
    {
        Orders.Add(new Order { faction = SaveRegistry.Id(planet.owner), planet = planet.planetName, remaining = BuildTime });
    }

    public static int CountFor(Faction faction)
    {
        string id = SaveRegistry.Id(faction);
        return All.Count(p => p.faction == id) + Orders.Count(o => o.faction == id);
    }

    public static Planet PlanetNamed(string name) => name == null ? null : Planet.All.Find(p => p != null && p.planetName == name);

    // Sends a parked probe along the hyperlanes to this planet's spot. False if there's no route.
    public static bool Send(Probe probe, Planet target)
    {
        if (probe == null || target == null || !probe.Parked) return false;
        Planet from = PlanetNamed(probe.planet);
        if (from == null || from == target) return false;
        List<Planet> route = GalacticPathfinder.FindPath(from, target, SaveRegistry.FindFaction(probe.faction));
        if (route.Count < 2) return false;
        probe.route = route.Skip(1).Select(p => p.planetName).ToList();
        return true;
    }

    // Seconds until the next probe at this planet is ready, or -1 if none is being built.
    public static float BuildingAt(Planet planet)
    {
        float best = -1f;
        foreach (Order order in Orders)
        {
            if (order.planet == planet.planetName && (best < 0f || order.remaining < best)) best = order.remaining;
        }
        return best;
    }

    public static int QueuedAt(Planet planet) => Orders.Count(o => o.planet == planet.planetName);
}

// Draws the player's probes on the galactic map, flies every faction's probes, finishes build orders, and
// handles dragging a probe to another planet's spot. GalacticMapManager adds it to the map and asks it first
// whether a mouse press was on a probe.
public class SpyProbeLayer : MonoBehaviour
{
    public static SpyProbeLayer Instance { get; private set; }

    private const float PickRadiusPixels = 26f;
    private const float SnapRadius = 1.4f;
    private const float IconSize = 0.75f;
    private const float ReferenceCameraDistance = 16f;
    private const float MaxZoomScale = 2.5f;
    private const float FleetSpeed = 5f; // GalacticFleet.travelSpeed

    private class Marker
    {
        public Transform root;
        public SpriteRenderer body;
        public SpriteRenderer eye;
        public SpriteRenderer ring;
        public TMPro.TextMeshPro timer;
    }

    private readonly Dictionary<SpyProbes.Probe, Marker> markers = new Dictionary<SpyProbes.Probe, Marker>();
    private readonly List<SpriteRenderer> spotMarkers = new List<SpriteRenderer>();
    private Dictionary<string, Planet> planets;
    private Camera cam;
    private LineRenderer dragLine;

    private SpyProbes.Probe dragging;
    private Planet dropPlanet;

    private static Sprite diamondSprite, eyeSprite, ringSprite;
    private static Material spriteMaterial;

    public bool IsDragging => dragging != null;

    private void Awake()
    {
        Instance = this;
        BuildSharedAssets();
        dragLine = new GameObject("ProbeOrderLine").AddComponent<LineRenderer>();
        dragLine.transform.SetParent(transform, false);
        dragLine.sharedMaterial = spriteMaterial;
        dragLine.widthMultiplier = 0.12f;
        dragLine.positionCount = 0;
        dragLine.sortingOrder = 90;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private Planet PlanetNamed(string name)
    {
        if (planets == null || planets.Count == 0 || planets.Values.Any(p => p == null))
        {
            planets = new Dictionary<string, Planet>();
            foreach (Planet planet in Planet.All) if (planet != null) planets[planet.planetName] = planet;
        }
        return name != null && planets.TryGetValue(name, out Planet found) ? found : null;
    }

    // ---- simulation ----

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            FinishOrders(dt);
            Fly(dt);
        }
        UpdateDrag();
    }

    private void FinishOrders(float dt)
    {
        List<SpyProbes.Order> orders = SpyProbes.Orders;
        for (int i = orders.Count - 1; i >= 0; i--)
        {
            SpyProbes.Order order = orders[i];
            order.remaining -= dt;
            if (order.remaining > 0f) continue;
            orders.RemoveAt(i);

            Planet planet = PlanetNamed(order.planet);
            if (planet == null) continue;
            SpyProbes.All.Add(new SpyProbes.Probe { faction = order.faction, planet = planet.planetName, Position = SpotFor(planet, null) });
            if (SaveRegistry.FindFaction(order.faction) is Faction f && f.isPlayerFaction)
                Toast.Show($"Spy probe ready at {planet.planetName} - drag it to any planet's spot");
        }
    }

    private void Fly(float dt)
    {
        List<SpyProbes.Probe> probes = SpyProbes.All;
        for (int i = probes.Count - 1; i >= 0; i--)
        {
            SpyProbes.Probe probe = probes[i];

            if (!probe.Parked)
            {
                Planet next = PlanetNamed(probe.route[0]);
                if (next == null) { probe.route.Clear(); continue; }
                Vector3 target = probe.route.Count == 1 ? SpotFor(next, probe) : next.transform.position;
                probe.Position = Vector3.MoveTowards(probe.Position, target, FleetSpeed * SpyProbes.SpeedMultiplier * dt);
                if ((probe.Position - target).sqrMagnitude < 0.0001f)
                {
                    probe.planet = next.planetName;
                    probe.route.RemoveAt(0);
                }
            }

            Planet here = PlanetNamed(probe.planet);
            bool overEnemy = probe.Parked && here != null && SaveRegistry.Id(here.owner) != probe.faction;
            if (probe.lifeLeft < 0f && overEnemy) probe.lifeLeft = SpyProbes.Lifetime;
            if (probe.lifeLeft < 0f) continue;

            probe.lifeLeft -= dt;
            if (probe.lifeLeft > 0f) continue;
            probes.RemoveAt(i);
            if (SaveRegistry.FindFaction(probe.faction) is Faction f && f.isPlayerFaction)
                Toast.Show($"Spy probe over {probe.planet} has gone dark");
        }
    }

    // Where a probe parks at this planet: the planet's spot, fanned out a little if several share it.
    private static Vector3 SpotFor(Planet planet, SpyProbes.Probe probe)
    {
        int index = 0;
        foreach (SpyProbes.Probe other in SpyProbes.All)
        {
            if (other == probe) break;
            if (other.Parked && other.planet == planet.planetName && other.faction == (probe != null ? probe.faction : other.faction)) index++;
        }
        return planet.PlanetSpotPosition + Vector3.right * (index % 2 == 0 ? 1f : -1f) * ((index + 1) / 2) * 0.55f;
    }

    // ---- input ----

    // Called by GalacticMapManager on a left press over the map. Starts dragging the player's probe under the
    // cursor, if there is one, and says so (the map then leaves the press alone).
    public bool TryBeginDrag(Vector3 mousePosition)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;
        string player = SaveRegistry.Id(SpyProbes.Player);
        SpyProbes.Probe best = null;
        float bestDistance = PickRadiusPixels;
        foreach (SpyProbes.Probe probe in SpyProbes.All)
        {
            if (probe.faction != player) continue;
            Vector3 screen = cam.WorldToScreenPoint(probe.Position + Vector3.up * 0.4f);
            if (screen.z < 0f) continue;
            float distance = Vector2.Distance(screen, mousePosition);
            if (distance < bestDistance) { bestDistance = distance; best = probe; }
        }
        if (best == null) return false;
        if (!best.Parked)
        {
            Toast.Show("That probe is still in flight");
            return true;
        }
        dragging = best;
        dropPlanet = null;
        return true;
    }

    private void UpdateDrag()
    {
        if (dragging == null)
        {
            dragLine.positionCount = 0;
            ShowSpots(false, null);
            return;
        }
        if (!SpyProbes.All.Contains(dragging)) { dragging = null; return; }

        Vector3 point;
        if (debugCursor.HasValue)
        {
            point = debugCursor.Value;
        }
        else
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(Vector3.up, dragging.Position);
            point = plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : dragging.Position;
        }

        // Only a planet's own spot takes a probe (not the fleet spots around it).
        dropPlanet = null;
        float best = SnapRadius * SnapRadius;
        foreach (Planet planet in Planet.All)
        {
            if (planet == null) continue;
            Vector3 offset = planet.PlanetSpotPosition - point;
            offset.y = 0f;
            if (offset.sqrMagnitude < best) { best = offset.sqrMagnitude; dropPlanet = planet; }
        }
        Planet from = PlanetNamed(dragging.planet);
        if (dropPlanet == from) dropPlanet = null;
        List<Planet> route = dropPlanet != null ? GalacticPathfinder.FindPath(from, dropPlanet, SpyProbes.Player) : null;
        bool valid = route != null && route.Count >= 2;

        Vector3 lift = Vector3.up * 0.1f;
        if (valid)
        {
            dragLine.positionCount = route.Count;
            dragLine.SetPosition(0, dragging.Position + lift);
            for (int i = 1; i < route.Count - 1; i++) dragLine.SetPosition(i, route[i].transform.position + lift);
            dragLine.SetPosition(route.Count - 1, dropPlanet.PlanetSpotPosition + lift);
        }
        else
        {
            dragLine.positionCount = 2;
            dragLine.SetPosition(0, dragging.Position + lift);
            dragLine.SetPosition(1, point + lift);
        }
        Color color = valid ? new Color(0.55f, 0.9f, 1f, 0.8f) : new Color(1f, 1f, 1f, 0.35f);
        dragLine.startColor = dragLine.endColor = color;
        ShowSpots(true, valid ? dropPlanet : null);
        PlanetTooltip.Instance?.SetRouteInfo(valid
            ? $"<b>Spy probe</b>  {MapText.Plural(route.Count - 1, "jump")}  |  ETA {MapText.RealTime(Eta(route))}\n{MapText.Tint("Unseen by the enemy - no battles on the way", MapText.Good)}"
            : MapText.Tint("Drop on a planet's own spot (below the planet)", MapText.Muted));

        if (debugCursor.HasValue && !debugRelease) return; // A dev tour holding the drag for a screenshot.
        if (debugRelease || Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
        {
            debugCursor = null;
            debugRelease = false;
            if (valid) SpyProbes.Send(dragging, dropPlanet);
            dragging = null;
            dropPlanet = null;
            PlanetTooltip.Instance?.SetRouteInfo(null);
        }
        else if (Input.GetMouseButtonDown(1))
        {
            dragging = null;
            PlanetTooltip.Instance?.SetRouteInfo(null);
        }
    }

    // Dev tours (ProbeTour): hold a drag of this probe with the cursor at a world point, then release it.
    private Vector3? debugCursor;
    private bool debugRelease;

    public void DebugHoldDrag(SpyProbes.Probe probe, Vector3 worldPoint)
    {
        if (cam == null) cam = Camera.main;
        dragging = probe;
        debugCursor = worldPoint;
    }

    public void DebugRelease() => debugRelease = true;

    private float Eta(List<Planet> route)
    {
        float distance = Vector3.Distance(dragging.Position, route[1].transform.position);
        for (int i = 2; i < route.Count; i++) distance += Vector3.Distance(route[i - 1].transform.position, route[i].transform.position);
        return distance / (FleetSpeed * SpyProbes.SpeedMultiplier);
    }

    private void ShowSpots(bool show, Planet highlighted)
    {
        if (!show)
        {
            foreach (SpriteRenderer spot in spotMarkers) spot.enabled = false;
            return;
        }
        int i = 0;
        foreach (Planet planet in Planet.All)
        {
            if (planet == null) continue;
            if (i >= spotMarkers.Count)
            {
                SpriteRenderer created = MakeSprite("ProbeSpot", ringSprite, transform, 95);
                created.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                spotMarkers.Add(created);
            }
            SpriteRenderer spot = spotMarkers[i++];
            spot.enabled = true;
            spot.transform.position = planet.PlanetSpotPosition + Vector3.up * 0.05f;
            bool lit = planet == highlighted;
            spot.transform.localScale = Vector3.one * (lit ? 1.25f : 0.9f);
            spot.color = lit ? new Color(0.55f, 0.9f, 1f, 1f) : new Color(0.55f, 0.9f, 1f, 0.35f);
        }
        for (; i < spotMarkers.Count; i++) spotMarkers[i].enabled = false;
    }

    // ---- drawing (the player's probes only: the enemy never sees one) ----

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        Faction player = SpyProbes.Player;
        string playerId = SaveRegistry.Id(player);

        foreach (SpyProbes.Probe probe in SpyProbes.All)
        {
            if (probe.faction != playerId) continue;
            if (!markers.TryGetValue(probe, out Marker marker)) markers[probe] = marker = MakeMarker(player);
            Place(marker, probe);
        }

        List<SpyProbes.Probe> gone = markers.Keys.Where(p => !SpyProbes.All.Contains(p) || p.faction != playerId).ToList();
        foreach (SpyProbes.Probe probe in gone)
        {
            Destroy(markers[probe].root.gameObject);
            markers.Remove(probe);
        }
    }

    private void Place(Marker marker, SpyProbes.Probe probe)
    {
        marker.root.position = probe.Position + Vector3.up * 0.4f;
        float zoom = 1f;
        if (cam != null)
        {
            marker.root.rotation = cam.transform.rotation;
            zoom = Mathf.Clamp(Vector3.Distance(cam.transform.position, marker.root.position) / ReferenceCameraDistance, 1f, MaxZoomScale);
        }
        marker.root.localScale = Vector3.one * IconSize * zoom;

        bool selected = probe == dragging;
        bool counting = probe.lifeLeft >= 0f;
        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f);
        marker.ring.enabled = selected || counting;
        marker.ring.color = selected ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.55f, 0.9f, 1f, 0.55f * pulse);
        marker.eye.color = Color.Lerp(new Color(0.55f, 0.9f, 1f), Color.white, counting ? pulse * 0.5f : 0f);
        marker.timer.enabled = counting;
        if (counting) marker.timer.text = MapText.RealTime(Mathf.Max(0f, probe.lifeLeft));
    }

    private Marker MakeMarker(Faction faction)
    {
        Marker marker = new Marker { root = new GameObject("SpyProbe").transform };
        marker.root.SetParent(transform, false);
        Color tint = faction != null ? Color.Lerp(faction.color, Color.white, 0.3f) : Color.white;
        marker.ring = MakeSprite("Ring", ringSprite, marker.root, 101);
        marker.ring.transform.localScale = Vector3.one * 1.35f;
        marker.body = MakeSprite("Body", diamondSprite, marker.root, 102);
        marker.body.color = new Color(0.05f, 0.07f, 0.11f, 0.92f);
        SpriteRenderer edge = MakeSprite("Edge", ringSprite, marker.root, 103);
        edge.transform.localScale = Vector3.one * 0.95f;
        edge.color = tint;
        marker.eye = MakeSprite("Eye", eyeSprite, marker.root, 104);
        marker.eye.transform.localScale = Vector3.one * 0.5f;

        GameObject timer = new GameObject("Timer");
        timer.transform.SetParent(marker.root, false);
        timer.transform.localPosition = new Vector3(0f, -0.85f, -0.02f);
        marker.timer = timer.AddComponent<TMPro.TextMeshPro>();
        marker.timer.alignment = TMPro.TextAlignmentOptions.Center;
        marker.timer.fontSize = 3.4f;
        marker.timer.fontStyle = TMPro.FontStyles.Bold;
        marker.timer.color = new Color(0.55f, 0.9f, 1f);
        marker.timer.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        marker.timer.rectTransform.sizeDelta = new Vector2(3f, 0.6f);
        marker.timer.sortingOrder = 105;
        return marker;
    }

    private static SpriteRenderer MakeSprite(string name, Sprite sprite, Transform parent, int order)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = spriteMaterial;
        renderer.sortingOrder = order;
        return renderer;
    }

    // ---- procedural sprites ----

    private static void BuildSharedAssets()
    {
        if (spriteMaterial == null) spriteMaterial = new Material(Shader.Find("Sprites/Default"));
        const int size = 96;
        if (diamondSprite == null) diamondSprite = MakeSprite(size, (x, y) => Coverage((Mathf.Abs(x) + Mathf.Abs(y) - 0.9f) * 0.7f, size));
        if (ringSprite == null) ringSprite = MakeSprite(size, (x, y) => Coverage(Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 0.88f) - 0.06f, size));
        // An eye: an almond with a ring cut out round a solid pupil.
        if (eyeSprite == null) eyeSprite = MakeSprite(size, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float almond = Coverage((Mathf.Sqrt(x * x / 0.81f + y * y / 0.25f) - 1f) * 0.45f, size);
            float hole = Coverage(r - 0.32f, size);
            float pupil = Coverage(r - 0.17f, size);
            return Mathf.Max(almond * (1f - hole), pupil);
        });
    }

    private static float Coverage(float distance, int size) => Mathf.Clamp01(0.5f - distance * size * 0.5f);

    private static Sprite MakeSprite(int size, System.Func<float, float, float> alphaAt)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SpyProbe" };
        Color32[] pixels = new Color32[size * size];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float x = (px + 0.5f) / size * 2f - 1f;
                float y = (py + 0.5f) / size * 2f - 1f;
                pixels[py * size + px] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alphaAt(x, y)) * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size); // One map unit across, like FleetMapIcon's.
    }
}
