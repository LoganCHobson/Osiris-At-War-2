using UnityEngine;

[CreateAssetMenu(fileName = "AIPersonality", menuName = "Osiris/AI Personality")]
public class AIPersonality : ScriptableObject
{
    [Header("Temperament")]
    [Range(0.1f, 2f)] public float aggression = 1f;
    [Range(0.1f, 2f)] public float defensiveness = 1f;
    [Range(0.1f, 2f)] public float expansion = 1f;
    [Range(0.1f, 2f)] public float economy = 1f;
    [Range(0f, 2f)] public float vengefulness = 1f;

    [Header("Operations")]
    public float attackPowerMargin = 1.5f;
    public float defensePowerMargin = 1.2f;
    public int maxCampaigns = 1;
    public float campaignStartThreshold = 0.35f;
    public float campaignSwitchMargin = 0.35f;
    public float campaignGiveUpRatio = 2.5f;
    public float minCampaignDuration = 60f;
    public int maxConcurrentExpansions = 2;
    public float objectiveSwitchMargin = 0.5f;

    [Header("Fleet Discipline")]
    public int minDetachmentShips = 3;
    public int strikeMinShips = 4;
    public int responseRadiusHops = 3;
    public float commitmentTime = 45f;
    public float taskMemory = 30f;
    [Range(0.1f, 1f)] public float maxDefenseShare = 0.5f;
    public float emergencyPriorityRatio = 2.5f;
    public float criticalPlanetValue = 4f;

    [Header("Memory")]
    public float grudgeHalfLife = 180f;
    public float intelMemory = 90f;

    [Header("Economy")]
    public int shipsQueuedPerShipyard = 2;
    public float planetsPerShipyard = 5f;
    public int reserveShips = 2;
    public float saveUpWindow = 45f;
    public float counterWeight = 0.5f;

    [Header("Battle Stations")]
    [Range(0f, 1f)] public float battleStationShare = 0.2f;
    public float minStationSiteScore = 1.2f;
    public float stationPaybackMinutes = 10f;
    public float stationRelocateDelay = 180f;

    [Header("Battle")]
    public float pressAdvantageRatio = 1.25f;
    public float fallBackRatio = 0.7f;
    public float reinforcementDelay = 2.5f;
    public float retreatRatio = 0.35f;
    [Range(0.1f, 1f)] public float defendedRetreatFactor = 0.6f;

    [Header("Playbook")]
    public bool canFocusFire = true;
    public bool canManageStance = true;
    public bool canPickDropZones = true;
    public bool canSwitchCampaigns = true;
    public bool canGarrisonChokepoints = true;
    public bool canSplitFleets = true;
    public bool canBuildBattleStations = true;
    public bool canRetreat = true;

    public static AIPersonality CreateVaried(int seed)
    {
        AIPersonality personality = CreateInstance<AIPersonality>();
        System.Random random = new System.Random(seed);

        personality.aggression = Vary(random, 1f);
        personality.defensiveness = Vary(random, 1f);
        personality.expansion = Vary(random, 1f);
        personality.economy = Vary(random, 1f);
        personality.vengefulness = Vary(random, 1f);
        personality.attackPowerMargin = Vary(random, 1.5f, 0.15f);
        personality.name = $"Generated ({seed})";
        return personality;
    }

    private static float Vary(System.Random random, float baseValue, float spread = 0.3f)
    {
        float t = (float)random.NextDouble() * 2f - 1f;
        return baseValue * (1f + t * spread);
    }
}
