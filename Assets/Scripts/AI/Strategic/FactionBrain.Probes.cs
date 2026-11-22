using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// AI empires use spy probes (SpyProbes.cs) the way a player would: when they're about to fight somewhere they
// can't see, or know little about the enemy worlds on their border, they buy a probe at a shipyard and send
// it there. What the probe sees reaches the brain through GalacticVisibility, like a fleet's view, so
// campaigns plan on fresh intel. The player never sees an enemy probe.
public partial class FactionBrain
{
    public const int MaxProbes = 2;            // owned + in production
    public const float StaleIntelSeconds = 90f; // older than this is worth a probe
    public const int ProbeScoutDepth = 2;       // jumps beyond our border worth scouting without a campaign

    public string ProbeIntent { get; private set; } = "";
    public int ProbesOrdered { get; private set; }
    public int ProbesSent { get; private set; }

    // How long since we last saw this planet (never: a long time).
    private float IntelAge(Planet planet)
    {
        return planet != null && intel.TryGetValue(planet.planetName, out PlanetIntel entry) ? now - entry.seenAt : 600f;
    }

    // The planet a probe would help most: a campaign objective we can't see, else the hostile world near our
    // border we know least about. taken: planets already watched by, or with a probe on its way from, us.
    private Planet ProbeTarget(HashSet<string> taken, out bool forCampaign)
    {
        forCampaign = false;
        Planet best = null;
        float bestScore = 0f;

        foreach (Campaign campaign in campaigns)
        {
            if (campaign.suspended) continue;
            Planet objective = SpyProbes.PlanetNamed(campaign.objectivePlanet);
            if (objective == null || visible.Contains(objective) || taken.Contains(objective.planetName)) continue;
            float score = IntelAge(objective);
            if (score > bestScore) { bestScore = score; best = objective; }
        }
        if (best != null)
        {
            forCampaign = true;
            return best;
        }

        Dictionary<Planet, int> near = JumpsFromOwned(ProbeScoutDepth);
        foreach (KeyValuePair<Planet, int> entry in near)
        {
            Planet planet = entry.Key;
            if (planet.owner == null || planet.owner == faction || planet.owner.isNeutral) continue;
            if (visible.Contains(planet) || taken.Contains(planet.planetName)) continue;
            float age = IntelAge(planet);
            if (age < StaleIntelSeconds) continue;
            float score = Mathf.Min(age, 600f) / 60f + world.PlanetValue(planet) + Grievance(planet.owner) / GrievanceScale * 3f - entry.Value;
            if (score > bestScore) { bestScore = score; best = planet; }
        }
        return best;
    }

    // Planets within this many jumps of one we own, with their distance.
    private Dictionary<Planet, int> JumpsFromOwned(int maxJumps)
    {
        Dictionary<Planet, int> depth = new Dictionary<Planet, int>();
        Queue<Planet> queue = new Queue<Planet>();
        foreach (Planet planet in owned) { depth[planet] = 0; queue.Enqueue(planet); }
        while (queue.Count > 0)
        {
            Planet planet = queue.Dequeue();
            if (depth[planet] >= maxJumps) continue;
            foreach (Planet next in planet.connections)
            {
                if (next == null || depth.ContainsKey(next)) continue;
                depth[next] = depth[planet] + 1;
                queue.Enqueue(next);
            }
        }
        return depth;
    }

    private HashSet<string> ProbeClaims()
    {
        string id = SaveRegistry.Id(faction);
        HashSet<string> taken = new HashSet<string>();
        foreach (SpyProbes.Probe probe in SpyProbes.All)
        {
            if (probe.faction != id) continue;
            taken.Add(probe.Parked ? probe.planet : probe.route[probe.route.Count - 1]);
        }
        return taken;
    }

    // Economy option (FactionBrain.Economy.cs): a probe at the shipyard nearest the planet we'd send it to.
    private void AddProbeOption()
    {
        if (SpyProbes.CountFor(faction) >= MaxProbes) return;
        List<Planet> yards = owned.Where(p => p.hasCapitalShipyard).ToList();
        if (yards.Count == 0) return;

        Planet target = ProbeTarget(ProbeClaims(), out bool forCampaign);
        if (target == null) return;

        Planet yard = yards.OrderBy(p => (p.transform.position - target.transform.position).sqrMagnitude).First();
        economyOptions.Add(new EconomyOption
        {
            label = $"Spy Probe @ {yard.planetName} for {target.planetName}",
            cost = SpyProbes.Cost,
            score = forCampaign ? 2.2f : 0.9f,
            execute = () =>
            {
                if (yard.owner != faction || !yard.hasCapitalShipyard) return false;
                SpyProbes.Enqueue(yard);
                ProbesOrdered++;
                Log($"Ordered a spy probe at {yard.planetName} to scout {target.planetName}");
                return true;
            }
        });
    }

    // Sends each idle probe (parked at home, its clock not started) where it's most needed.
    private void DispatchProbes()
    {
        string id = SaveRegistry.Id(faction);
        HashSet<string> taken = ProbeClaims();
        ProbeIntent = "";
        foreach (SpyProbes.Probe probe in SpyProbes.All)
        {
            if (probe.faction != id || !probe.Parked || probe.lifeLeft >= 0f) continue;
            Planet here = SpyProbes.PlanetNamed(probe.planet);
            if (here == null || here.owner != faction) continue;

            taken.Remove(probe.planet);
            Planet target = ProbeTarget(taken, out bool forCampaign);
            if (target == null || !SpyProbes.Send(probe, target))
            {
                taken.Add(probe.planet);
                continue;
            }
            taken.Add(target.planetName);
            ProbesSent++;
            ProbeIntent = $"Probe > {target.planetName}";
            Log($"Sent a spy probe from {here.planetName} to {target.planetName}{(forCampaign ? " ahead of the campaign" : "")}");
        }
    }
}
