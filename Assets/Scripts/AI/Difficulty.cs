using UnityEngine;

public enum DifficultyLevel
{
    Cadet = 0,
    Captain = 1,
    Admiral = 2
    // Grand Admiral (machine-learned) comes later.
}

// How well the AI plays. Purely behaviour - every difficulty has the same ships, stats, build times and
// income. Harder AIs think more often, focus fire and position better, use priority moves and drop zones
// smartly, retreat at the right moment, and run a wider strategic playbook.
[System.Serializable]
public class DifficultyProfile
{
    public DifficultyLevel level;

    [Header("Battle")]
    [Tooltip("Seconds between battle decisions (reaction time).")]
    public float decisionInterval = 0.75f;
    [Tooltip("Multiplier on how long it waits before committing reinforcements.")]
    public float reinforcementDelayScale = 1f;
    public bool focusFire = true;
    [Tooltip("Chance per decision that a ship fixates on the nearest enemy instead of the best target.")]
    [Range(0f, 1f)] public float targetMistakeChance;
    public bool manageStance = true;
    public bool pickDropZones = true;
    [Tooltip("Fields only what the fight needs and keeps the rest in reserve (undeployed ships escape for free). Off = throws everything in.")]
    public bool holdReserves = true;
    [Tooltip("0 = never priority-moves through asteroids, 1 = only to escape, 2 = full judgement.")]
    [Range(0, 2)] public int priorityMoveSkill = 2;
    [Tooltip("Multiplier on the force ratio at which it decides to retreat (lower = holds on too long).")]
    public float retreatJudgement = 1f;

    [Header("Galactic map")]
    [Tooltip("Multiplier on time between strategic decisions.")]
    public float thinkIntervalScale = 1f;
    [Tooltip("Multiplier on the power margin it wants before attacking (below 1 = reckless).")]
    public float attackMarginScale = 1f;
    public bool switchCampaigns = true;
    public bool garrisonChokepoints = true;
    public bool splitFleets = true;
    public bool buildBattleStations = true;

    public static DifficultyProfile For(DifficultyLevel level)
    {
        switch (level)
        {
            case DifficultyLevel.Cadet:
                return new DifficultyProfile
                {
                    level = level,
                    decisionInterval = 2.2f,
                    reinforcementDelayScale = 2.5f,
                    focusFire = false,
                    targetMistakeChance = 0.35f,
                    manageStance = false,
                    pickDropZones = false,
                    holdReserves = false,
                    priorityMoveSkill = 0,
                    retreatJudgement = 0.5f,
                    thinkIntervalScale = 2.5f,
                    attackMarginScale = 0.8f,
                    switchCampaigns = false,
                    garrisonChokepoints = false,
                    splitFleets = false,
                    buildBattleStations = false
                };

            case DifficultyLevel.Admiral:
                return new DifficultyProfile
                {
                    level = level,
                    decisionInterval = 0.5f,
                    reinforcementDelayScale = 0.7f,
                    focusFire = true,
                    targetMistakeChance = 0f,
                    manageStance = true,
                    pickDropZones = true,
                    priorityMoveSkill = 2,
                    retreatJudgement = 1f,
                    thinkIntervalScale = 0.75f,
                    attackMarginScale = 1.1f,
                    switchCampaigns = true,
                    garrisonChokepoints = true,
                    splitFleets = true,
                    buildBattleStations = true
                };

            default:
                return new DifficultyProfile
                {
                    level = DifficultyLevel.Captain,
                    decisionInterval = 1.1f,
                    reinforcementDelayScale = 1.4f,
                    focusFire = true,
                    targetMistakeChance = 0.15f,
                    manageStance = true,
                    pickDropZones = true,
                    priorityMoveSkill = 1,
                    retreatJudgement = 0.8f,
                    thinkIntervalScale = 1.4f,
                    attackMarginScale = 1f,
                    switchCampaigns = true,
                    garrisonChokepoints = true,
                    splitFleets = false,
                    buildBattleStations = true
                };
        }
    }

    // A copy of the faction's personality with this difficulty's playbook applied (assets stay untouched).
    public AIPersonality Apply(AIPersonality source)
    {
        AIPersonality tuned = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<AIPersonality>();
        tuned.name = $"{(source != null ? source.name : "Default")} ({level})";

        tuned.canFocusFire &= focusFire;
        tuned.canManageStance &= manageStance;
        tuned.canPickDropZones &= pickDropZones;
        tuned.reinforcementDelay *= reinforcementDelayScale;
        tuned.retreatRatio *= retreatJudgement;

        tuned.attackPowerMargin *= attackMarginScale;
        tuned.canSwitchCampaigns &= switchCampaigns;
        tuned.canGarrisonChokepoints &= garrisonChokepoints;
        tuned.canSplitFleets &= splitFleets;
        tuned.canBuildBattleStations &= buildBattleStations;
        return tuned;
    }
}

// The difficulty chosen for the current campaign (remembered between sessions).
public static class Difficulty
{
    private const string PrefKey = "osiris.difficulty";
    private static DifficultyProfile profile;

    public static DifficultyLevel Current
    {
        get => (DifficultyLevel)PlayerPrefs.GetInt(PrefKey, (int)DifficultyLevel.Captain);
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
            profile = null;
        }
    }

    public static DifficultyProfile Profile => profile != null && profile.level == Current ? profile : (profile = DifficultyProfile.For(Current));

    public static string Describe(DifficultyLevel level)
    {
        switch (level)
        {
            case DifficultyLevel.Cadet: return "A slow, predictable enemy that makes mistakes. Learn the ropes.";
            case DifficultyLevel.Admiral: return "A sharp commander: fast reactions, focused fire, smart retreats and a full strategic playbook.";
            default: return "A capable enemy that fights sensibly but can be outmanoeuvred.";
        }
    }
}
