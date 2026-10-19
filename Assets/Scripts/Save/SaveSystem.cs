using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Saving and loading campaigns. Saves are JSON files in <persistentDataPath>/Saves. You can only save on
// the galactic map (not mid-battle, and not while a battle is about to start).
//
// Loading works in two steps around the galactic map's own startup: PrepareLoad fills GalacticState
// before it rebuilds the planets and fleets, then FinishLoad restores the AI empires, clock and economy.
public static class SaveSystem
{
    public const string QuicksaveSlot = "quicksave";
    public const string AutosavePrefix = "autosave_";
    public const int AutosaveSlots = 3;
    private const string GalacticScene = "GalacticMap";
    private const string MainMenuScene = "MainMenu";

    private static SaveData pendingLoad;

    public static bool IsLoading => pendingLoad != null;
    public static string Folder => Path.Combine(Application.persistentDataPath, "Saves");

    // ---------------- Saving ----------------

    public static bool CanSave(out string reason)
    {
        reason = null;
        if (SceneManager.GetActiveScene().name != GalacticScene || GalacticMapManager.Instance == null || GalacticState.Instance == null)
        {
            reason = "You can only save on the galactic map.";
        }
        else if (BattlePrompt.IsOpen || GalacticFleet.All.Exists(fleet => fleet != null && fleet.AwaitingBattle))
        {
            reason = "Can't save while a battle is about to start.";
        }
        return reason == null;
    }

    public static bool Save(string slot, string displayName, out string error)
    {
        error = null;
        if (!CanSave(out error)) return false;

        try
        {
            SaveData data = Capture();
            data.displayName = string.IsNullOrEmpty(displayName) ? Describe(data) : displayName;
            Directory.CreateDirectory(Folder);

            string path = PathFor(slot);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path); // Write-then-swap so a crash mid-save can't wreck the old file.
            return true;
        }
        catch (System.Exception exception)
        {
            error = "Save failed: " + exception.Message;
            Debug.LogException(exception);
            return false;
        }
    }

    public static bool Quicksave(out string error) => Save(QuicksaveSlot, "Quicksave", out error);

    // Rotates through the autosave slots, overwriting the oldest.
    public static bool Autosave(out string error)
    {
        string oldest = null;
        System.DateTime oldestTime = System.DateTime.MaxValue;
        for (int i = 1; i <= AutosaveSlots; i++)
        {
            string slot = AutosavePrefix + i;
            if (!File.Exists(PathFor(slot)))
            {
                oldest = slot;
                break;
            }
            System.DateTime written = File.GetLastWriteTime(PathFor(slot));
            if (written < oldestTime)
            {
                oldestTime = written;
                oldest = slot;
            }
        }
        return Save(oldest, "Autosave", out error);
    }

    // A fresh slot id for "New Save".
    public static string NewSlot()
    {
        return "save_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    }

    // ---------------- Listing ----------------

    public static List<SaveSummary> List()
    {
        List<SaveSummary> saves = new List<SaveSummary>();
        if (!Directory.Exists(Folder)) return saves;

        foreach (string path in Directory.GetFiles(Folder, "*.json"))
        {
            SaveData data = Read(path);
            if (data == null) continue;

            System.DateTime.TryParse(data.savedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out System.DateTime savedAt);
            saves.Add(new SaveSummary
            {
                slot = Path.GetFileNameWithoutExtension(path),
                path = path,
                data = data,
                savedAt = savedAt == default ? File.GetLastWriteTime(path) : savedAt
            });
        }
        return saves.OrderByDescending(save => save.savedAt).ToList();
    }

    public static SaveSummary Latest() => List().FirstOrDefault();

    public static bool HasQuicksave => File.Exists(PathFor(QuicksaveSlot));

    public static void Delete(string slot)
    {
        string path = PathFor(slot);
        if (File.Exists(path)) File.Delete(path);
    }

    // ---------------- Loading ----------------

    // Starts loading a save: clears the current campaign and opens the galactic map, which picks it up.
    public static bool Load(string slot, out string error)
    {
        error = null;
        SaveData data = Read(PathFor(slot));
        if (data == null)
        {
            error = "That save couldn't be read.";
            return false;
        }
        if (data.version > SaveData.CurrentVersion)
        {
            error = "That save is from a newer version of the game.";
            return false;
        }

        pendingLoad = data;
        ClearCampaign();
        GameSpeed.Reset();
        SceneManager.LoadScene(GalacticScene);
        return true;
    }

    // Drops everything that outlives a scene so the next campaign (new or loaded) starts clean.
    public static void ClearCampaign()
    {
        if (GalacticState.Instance != null) Object.Destroy(GalacticState.Instance.gameObject);
        if (BattleContext.Instance != null) Object.Destroy(BattleContext.Instance.gameObject);
        if (AIDirector.Instance != null) Object.Destroy(AIDirector.Instance.gameObject);
    }

    public static void ReturnToMainMenu()
    {
        pendingLoad = null;
        ClearCampaign();
        GameSpeed.Reset();
        SceneManager.LoadScene(MainMenuScene);
    }

    // Step 1 (before GalacticState rebuilds the map): hand it the saved galaxy.
    public static void PrepareLoad(GalacticState state)
    {
        if (pendingLoad == null || state == null) return;
        SaveData data = pendingLoad;

        if (data.mapSignature != MapSignature())
        {
            Debug.LogWarning("Loading a save made on a different galaxy layout - planets that no longer exist are skipped.");
        }

        Difficulty.Current = (DifficultyLevel)data.difficulty;

        state.planetSnapshots.Clear();
        foreach (PlanetSave planet in data.planets)
        {
            state.planetSnapshots.Add(new GalacticState.PlanetSnapshot
            {
                planetName = planet.name,
                owner = SaveRegistry.FindFaction(planet.owner),
                hasTaxOffice = planet.hasTaxOffice,
                hasCapitalShipyard = planet.hasCapitalShipyard,
                hasBattleStation = planet.hasBattleStation,
                shipBuildQueue = planet.buildQueue
                    .Select(order => new ShipBuildOrder { ship = SaveRegistry.FindShip(order.ship), remainingTime = order.remainingTime })
                    .Where(order => order.ship != null)
                    .ToList()
            });
        }

        state.fleetSnapshots.Clear();
        foreach (FleetSave fleet in data.fleets)
        {
            List<Ship> roster = fleet.roster.Select(SaveRegistry.FindShip).Where(ship => ship != null).ToList();
            if (roster.Count == 0) continue;

            state.fleetSnapshots.Add(new GalacticState.FleetSnapshot
            {
                fleetName = fleet.name,
                faction = SaveRegistry.FindFaction(fleet.faction),
                planetName = fleet.planet,
                roster = roster,
                travel = fleet.travel
            });
        }

        state.treasuries.Clear();
        foreach (TreasurySave treasury in data.treasuries)
        {
            Faction faction = SaveRegistry.FindFaction(treasury.faction);
            if (faction != null) state.treasuries.Add(new GalacticState.TreasurySnapshot { faction = faction, currency = treasury.currency });
        }

        state.playerIntel.Clear();
        foreach (IntelSave saved in data.playerIntel)
        {
            PlanetIntel intel = new PlanetIntel
            {
                seenAt = saved.seenAt,
                owner = SaveRegistry.FindFaction(saved.owner),
                hasTaxOffice = saved.hasTaxOffice,
                hasCapitalShipyard = saved.hasCapitalShipyard,
                hasBattleStation = saved.hasBattleStation
            };
            foreach (SightingSave sighting in saved.fleets)
            {
                intel.fleets.Add(new PlanetIntel.FleetSighting { faction = SaveRegistry.FindFaction(sighting.faction), ships = sighting.ships, power = sighting.power });
            }
            state.playerIntel[saved.planet] = intel;
        }

        state.initialized = true;
    }

    // Step 2 (after the map is rebuilt): the AI empires' memories, the clock and the economy.
    public static void FinishLoad()
    {
        if (pendingLoad == null) return;
        SaveData data = pendingLoad;
        pendingLoad = null;

        AIDirector director = AIDirector.Instance;
        if (director != null)
        {
            director.RestoreClock(data.galacticTime);
            foreach (BrainSave saved in data.brains)
            {
                FactionBrain brain = director.EnsureBrain(SaveRegistry.FindFaction(saved.faction));
                if (brain != null) RestoreBrain(brain, saved);
            }
        }

        if (EconomyManager.Instance != null) EconomyManager.Instance.TaxTimer = data.taxTimer;
    }

    // ---------------- Capture ----------------

    private static SaveData Capture()
    {
        GalacticState state = GalacticState.Instance;
        state.CaptureFromScene(); // Planets, fleets (with travel) and treasuries as they are right now.

        Faction player = FindPlayerFaction();
        SaveData data = new SaveData
        {
            savedAt = System.DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            mapSignature = MapSignature(),
            playerFaction = SaveRegistry.Id(player),
            difficulty = (int)Difficulty.Current,
            galacticTime = AIDirector.Instance != null ? AIDirector.Instance.GalacticTime : 0f,
            taxTimer = EconomyManager.Instance != null ? EconomyManager.Instance.TaxTimer : 0f,
            playerCredits = state.GetCurrency(player)
        };

        foreach (GalacticState.PlanetSnapshot planet in state.planetSnapshots)
        {
            SaveRegistry.Remember(planet.owner);
            data.planets.Add(new PlanetSave
            {
                name = planet.planetName,
                owner = SaveRegistry.Id(planet.owner),
                hasTaxOffice = planet.hasTaxOffice,
                hasCapitalShipyard = planet.hasCapitalShipyard,
                hasBattleStation = planet.hasBattleStation,
                buildQueue = planet.shipBuildQueue
                    .Where(order => order.ship != null)
                    .Select(order => new BuildSave { ship = SaveRegistry.Id(order.ship), remainingTime = order.remainingTime })
                    .ToList()
            });
            if (planet.owner == player && player != null) data.playerPlanets++;
        }

        foreach (GalacticState.FleetSnapshot fleet in state.fleetSnapshots)
        {
            SaveRegistry.Remember(fleet.faction);
            foreach (Ship ship in fleet.roster) SaveRegistry.Remember(ship);
            data.fleets.Add(new FleetSave
            {
                name = fleet.fleetName,
                faction = SaveRegistry.Id(fleet.faction),
                planet = fleet.planetName,
                roster = fleet.roster.Where(ship => ship != null).Select(SaveRegistry.Id).ToList(),
                travel = fleet.travel
            });
            if (fleet.faction == player && player != null) data.playerShips += fleet.roster.Count;
        }

        foreach (GalacticState.TreasurySnapshot treasury in state.treasuries)
        {
            data.treasuries.Add(new TreasurySave { faction = SaveRegistry.Id(treasury.faction), currency = treasury.currency });
        }

        foreach (KeyValuePair<string, PlanetIntel> entry in state.playerIntel)
        {
            IntelSave intel = new IntelSave
            {
                planet = entry.Key,
                seenAt = entry.Value.seenAt,
                owner = SaveRegistry.Id(entry.Value.owner),
                hasTaxOffice = entry.Value.hasTaxOffice,
                hasCapitalShipyard = entry.Value.hasCapitalShipyard,
                hasBattleStation = entry.Value.hasBattleStation
            };
            foreach (PlanetIntel.FleetSighting sighting in entry.Value.fleets)
            {
                intel.fleets.Add(new SightingSave { faction = SaveRegistry.Id(sighting.faction), ships = sighting.ships, power = sighting.power });
            }
            data.playerIntel.Add(intel);
        }

        if (AIDirector.Instance != null)
        {
            foreach (FactionBrain brain in AIDirector.Instance.brains) data.brains.Add(CaptureBrain(brain));
        }

        return data;
    }

    private static BrainSave CaptureBrain(FactionBrain brain)
    {
        BrainSave saved = new BrainSave
        {
            faction = SaveRegistry.Id(brain.faction),
            nextThinkAt = brain.nextThinkAt,
            log = new List<string>(brain.log)
        };

        foreach (KeyValuePair<Faction, float> grievance in brain.grievance)
        {
            saved.grievances.Add(new GrievanceSave { faction = SaveRegistry.Id(grievance.Key), amount = grievance.Value });
        }
        foreach (KeyValuePair<string, FactionBrain.PlanetIntel> intel in brain.intel)
        {
            saved.intel.Add(new BrainIntelSave { planet = intel.Key, power = intel.Value.power, faction = SaveRegistry.Id(intel.Value.faction), seenAt = intel.Value.seenAt });
        }
        foreach (FactionBrain.Campaign campaign in brain.campaigns)
        {
            saved.campaigns.Add(new CampaignSave
            {
                target = SaveRegistry.Id(campaign.target),
                objectivePlanet = campaign.objectivePlanet,
                stagingPlanet = campaign.stagingPlanet,
                startedAt = campaign.startedAt,
                powerLost = campaign.powerLost,
                enemyPowerDestroyed = campaign.enemyPowerDestroyed,
                planetsTaken = campaign.planetsTaken,
                score = campaign.score,
                requiredPower = campaign.requiredPower,
                stagedPower = campaign.stagedPower,
                suspended = campaign.suspended
            });
        }
        foreach (KeyValuePair<string, FactionBrain.FleetOrder> order in brain.orders)
        {
            saved.orders.Add(new OrderSave { fleet = order.Key, kind = (int)order.Value.kind, planet = order.Value.planetName });
        }
        return saved;
    }

    private static void RestoreBrain(FactionBrain brain, BrainSave saved)
    {
        brain.nextThinkAt = saved.nextThinkAt;

        brain.grievance.Clear();
        foreach (GrievanceSave grievance in saved.grievances)
        {
            Faction faction = SaveRegistry.FindFaction(grievance.faction);
            if (faction != null) brain.grievance[faction] = grievance.amount;
        }

        brain.intel.Clear();
        foreach (BrainIntelSave intel in saved.intel)
        {
            brain.intel[intel.planet] = new FactionBrain.PlanetIntel { power = intel.power, faction = SaveRegistry.FindFaction(intel.faction), seenAt = intel.seenAt };
        }

        brain.campaigns.Clear();
        foreach (CampaignSave campaign in saved.campaigns)
        {
            Faction target = SaveRegistry.FindFaction(campaign.target);
            if (target == null) continue;
            brain.campaigns.Add(new FactionBrain.Campaign
            {
                target = target,
                objectivePlanet = campaign.objectivePlanet,
                stagingPlanet = campaign.stagingPlanet,
                startedAt = campaign.startedAt,
                powerLost = campaign.powerLost,
                enemyPowerDestroyed = campaign.enemyPowerDestroyed,
                planetsTaken = campaign.planetsTaken,
                score = campaign.score,
                requiredPower = campaign.requiredPower,
                stagedPower = campaign.stagedPower,
                suspended = campaign.suspended
            });
        }

        brain.orders.Clear();
        foreach (OrderSave order in saved.orders)
        {
            brain.orders[order.fleet] = new FactionBrain.FleetOrder { fleetName = order.fleet, kind = (FactionBrain.OrderKind)order.kind, planetName = order.planet };
        }

        brain.log.Clear();
        brain.log.AddRange(saved.log);
    }

    // ---------------- Helpers ----------------

    private static string PathFor(string slot)
    {
        foreach (char bad in Path.GetInvalidFileNameChars()) slot = slot.Replace(bad, '_');
        return Path.Combine(Folder, slot + ".json");
    }

    private static SaveData Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(path)) : null;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"Couldn't read save '{path}': {exception.Message}");
            return null;
        }
    }

    // Planet names identify the galaxy layout a save belongs to.
    private static string MapSignature()
    {
        List<string> names = Planet.All.Select(planet => planet.planetName).OrderBy(name => name).ToList();
        return $"{names.Count}:{PlanetArchetypes.StableHash(string.Join("|", names))}";
    }

    private static Faction FindPlayerFaction()
    {
        foreach (Planet planet in Planet.All)
        {
            if (Strength.IsPlayer(planet.owner)) return planet.owner;
        }
        foreach (GalacticFleet fleet in GalacticFleet.All)
        {
            if (Strength.IsPlayer(fleet.faction)) return fleet.faction;
        }
        return null;
    }

    private static string Describe(SaveData data)
    {
        return $"{data.playerPlanets} planets, {data.playerShips} ships";
    }
}
