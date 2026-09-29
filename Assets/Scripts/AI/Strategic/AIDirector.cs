using System.Collections.Generic;
using UnityEngine;

public class AIDirector : MonoBehaviour
{
    public static AIDirector Instance { get; private set; }

    public bool aiEnabled = true;
    public float thinkInterval = 3f;
    public float factionDiscoveryInterval = 5f;
    public List<Ship> buildableShips = new List<Ship>();
    public AIPersonality defaultPersonality;

    public float GalacticTime { get; private set; }
    public GalacticWorld World { get; } = new GalacticWorld();
    public readonly List<FactionBrain> brains = new List<FactionBrain>();

    private float nextDiscoveryAt;
    private int nextBrainIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (GetComponent<AIDebugOverlay>() == null)
        {
            gameObject.AddComponent<AIDebugOverlay>();
        }

        GalacticEvents.PlanetCaptured += HandlePlanetCaptured;
        GalacticEvents.BattleResolved += HandleBattleResolved;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        GalacticEvents.PlanetCaptured -= HandlePlanetCaptured;
        GalacticEvents.BattleResolved -= HandleBattleResolved;
        Instance = null;
    }

    public static bool MapIsActive => GalacticMapManager.Instance != null && GalacticState.Instance != null;

    private void Update()
    {
        if (!MapIsActive) return;

        World.EnsurePlanets();
        if (!aiEnabled || BattlePrompt.IsOpen) return;

        GalacticTime += Time.deltaTime;

        if (GalacticTime >= nextDiscoveryAt || brains.Count == 0)
        {
            nextDiscoveryAt = GalacticTime + factionDiscoveryInterval;
            World.Refresh();
            DiscoverFactions();
        }

        if (brains.Count == 0) return;

        for (int attempt = 0; attempt < brains.Count; attempt++)
        {
            nextBrainIndex = (nextBrainIndex + 1) % brains.Count;
            FactionBrain brain = brains[nextBrainIndex];
            if (brain.nextThinkAt > GalacticTime) continue;

            World.Refresh();
            brain.Think(World, GalacticTime);
            brain.nextThinkAt = GalacticTime + thinkInterval;
            break;
        }
    }

    public FactionBrain BrainFor(Faction faction)
    {
        foreach (FactionBrain brain in brains)
        {
            if (brain.faction == faction) return brain;
        }
        return null;
    }

    private void DiscoverFactions()
    {
        foreach (Planet planet in World.planets)
        {
            TryAddBrain(planet.owner);
        }

        foreach (GalacticFleet fleet in World.fleets)
        {
            TryAddBrain(fleet.faction);
        }
    }

    private void TryAddBrain(Faction faction)
    {
        if (faction == null || faction.isPlayerFaction || faction.isNeutral || BrainFor(faction) != null) return;

        AIPersonality personality = faction.personality != null ? faction.personality
            : defaultPersonality != null ? defaultPersonality
            : AIPersonality.CreateVaried(StableHash(faction.factionName));

        FactionBrain brain = new FactionBrain(faction, personality)
        {
            nextThinkAt = GalacticTime + thinkInterval * (brains.Count + 1) / 8f
        };
        brains.Add(brain);
    }

    private static int StableHash(string text)
    {
        int hash = 17;
        if (text == null) return hash;

        foreach (char c in text)
        {
            hash = hash * 31 + c;
        }
        return hash;
    }

    private void HandlePlanetCaptured(Planet planet, Faction previousOwner, Faction newOwner)
    {
        foreach (FactionBrain brain in brains)
        {
            brain.OnPlanetCaptured(planet, previousOwner, newOwner);
        }
    }

    private void HandleBattleResolved(BattleReport report)
    {
        foreach (FactionBrain brain in brains)
        {
            brain.OnBattleResolved(report);
        }
    }
}
