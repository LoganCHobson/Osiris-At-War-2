using System.Collections.Generic;
using UnityEngine;

// What a hero's presence does for every ship on their side of a battle (while their flagship lives).
// Values are fractions: 0.15 = 15% better.
public enum HeroBuff
{
    Accuracy,         // Tighter shot spread.
    Range,            // Longer gun range.
    Speed,            // Faster ships.
    AbilityCooldown,  // Abilities come back sooner.
    Damage,           // Harder-hitting guns.
    ShieldRegen,      // Shields recharge faster (Avanti).
    HullRepair,       // Damaged hardpoints slowly patched (fraction of max health per minute).
    FighterLaunch,    // Hangars launch replacement squadrons sooner.
    Resolve,          // The fleet holds on longer before breaking (lower retreat threshold).
    Reinforcement     // Reinforcements arrive sooner.
}

// A named commander with a permanent flagship. Heroes join their faction at the start of a campaign,
// ride in a fleet like any ship, and buff their whole side in battle. If the flagship is destroyed the
// hero is gone for the rest of the campaign.
public class HeroDefinition
{
    public string id;
    public string name;
    public string title;
    public string factionName;   // Faction.factionName the hero serves.
    public string shipClass;     // Ship prefab name (SaveRegistry id) the flagship is built on.
    public string flagshipName;  // Permanent name of the flagship.
    public string biography;
    public float powerMultiplier = 1.35f; // The flagship counts for this much more than its class.
    public Color portraitTint = Color.white;
    public readonly Dictionary<HeroBuff, float> buffs = new Dictionary<HeroBuff, float>();

    private Sprite portrait;
    private bool portraitLoaded;

    // Resources/Heroes/<id> (a sprite or texture). Drop real art there to replace the placeholder.
    public Sprite Portrait
    {
        get
        {
            if (portraitLoaded) return portrait;
            portraitLoaded = true;
            portrait = Resources.Load<Sprite>("Heroes/" + id);
            if (portrait == null)
            {
                Texture2D texture = Resources.Load<Texture2D>("Heroes/" + id);
                if (texture != null) portrait = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
            return portrait;
        }
    }

    public float Buff(HeroBuff buff) => buffs.TryGetValue(buff, out float value) ? value : 0f;

    public string BuffSummary()
    {
        List<string> parts = new List<string>();
        foreach (KeyValuePair<HeroBuff, float> buff in buffs) parts.Add($"+{buff.Value * 100f:0}% {Describe(buff.Key)}");
        return string.Join("\n", parts);
    }

    public static string Describe(HeroBuff buff)
    {
        switch (buff)
        {
            case HeroBuff.AbilityCooldown: return "ability recharge";
            case HeroBuff.ShieldRegen: return "shield recharge";
            case HeroBuff.HullRepair: return "hull repair / min";
            case HeroBuff.FighterLaunch: return "squadron launch rate";
            case HeroBuff.Resolve: return "resolve";
            case HeroBuff.Reinforcement: return "reinforcement speed";
            default: return buff.ToString().ToLowerInvariant();
        }
    }

    private HeroDefinition With(HeroBuff buff, float value)
    {
        buffs[buff] = value;
        return this;
    }

    // ---------------- The roster of heroes ----------------

    private static List<HeroDefinition> all;

    public static IReadOnlyList<HeroDefinition> All => all ??= Build();

    public static HeroDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (HeroDefinition hero in All) if (hero.id == id) return hero;
        return null;
    }

    private static List<HeroDefinition> Build()
    {
        return new List<HeroDefinition>
        {
            // ---- Titans of Osiris ----
            new HeroDefinition
            {
                id = "eric_von_wolfgang", name = "Eric Von Wolfgang", title = "Admiral",
                factionName = "Titans of Osiris", shipClass = "Human_Battleship", flagshipName = "UNS Chimera",
                biography = "The pride of the navy. A black-haired, bearded veteran of every major engagement since the Titans crossed into Osiris, Von Wolfgang drills his crews until they shoot straighter, fly faster and fight longer than anyone else's.",
                powerMultiplier = 1.5f, portraitTint = new Color(0.35f, 0.75f, 1f)
            }.With(HeroBuff.Accuracy, 0.15f).With(HeroBuff.Range, 0.10f).With(HeroBuff.Speed, 0.10f).With(HeroBuff.AbilityCooldown, 0.20f),

            // ---- Avanti Empire ----
            new HeroDefinition
            {
                id = "varethis_kaal", name = "Varethis Kaal", title = "Fleet Lord",
                factionName = "Avanti Empire", shipClass = "Avanti_Capital", flagshipName = "AIS Throne of Ashes",
                biography = "Last Fleet Lord of the old throne. Kaal treats every retreat as treason and every lost ship as a stain on the Emperor's name. His gunners are drilled in the ancient imperial doctrine: strike first, from further away, and never miss twice.",
                portraitTint = new Color(0.85f, 0.7f, 0.3f)
            }.With(HeroBuff.Range, 0.15f).With(HeroBuff.Accuracy, 0.10f).With(HeroBuff.Resolve, 0.30f),

            // ---- Severed Hand ----
            new HeroDefinition
            {
                id = "ossirak_tel", name = "Ossirak Tel", title = "Marshal",
                factionName = "Severed Hand", shipClass = "Avanti_Cruiser", flagshipName = "SHV Unbroken Oath",
                biography = "Former commander of the Imperial Guard who turned his officers against the throne. Cold, methodical and utterly disciplined - his fleets execute every manoeuvre on the second.",
                portraitTint = new Color(0.8f, 0.8f, 0.85f)
            }.With(HeroBuff.AbilityCooldown, 0.25f).With(HeroBuff.Accuracy, 0.10f).With(HeroBuff.Reinforcement, 0.25f),

            // ---- Kesh Dominion ----
            new HeroDefinition
            {
                id = "hazrun_vesh", name = "Hazrun Vesh", title = "Warlord-Priest",
                factionName = "Kesh Dominion", shipClass = "Avanti_Carrier", flagshipName = "KDS Hymn of Chains",
                biography = "Prophet-king of the Kesh, who sends slave-crewed squadrons to die in waves and calls it worship. His shrine-carrier sings the old hymns over the comm channels as it launches.",
                portraitTint = new Color(0.75f, 0.35f, 0.9f)
            }.With(HeroBuff.FighterLaunch, 0.35f).With(HeroBuff.HullRepair, 0.04f),

            // ---- Vorrak Clans ----
            new HeroDefinition
            {
                id = "dravik_sorn", name = "Dravik Sorn", title = "Raid-Chief",
                factionName = "Vorrak Clans", shipClass = "Avanti_Destroyer", flagshipName = "VCR Bloodwake",
                biography = "The fastest blade among the clans. Sorn hits, burns and is gone before a defence can form - and has never once stayed to hold what he took.",
                portraitTint = new Color(1f, 0.45f, 0.3f)
            }.With(HeroBuff.Speed, 0.25f).With(HeroBuff.Damage, 0.10f),

            // ---- Thessary Concord ----
            new HeroDefinition
            {
                id = "ilvarra_quen", name = "Ilvarra Quen", title = "Magnate",
                factionName = "Thessary Concord", shipClass = "Avanti_Cruiser", flagshipName = "TCM Gilded Tithe",
                biography = "Banker, broker and warlord. Quen's mercenary contracts keep her fleets topped up and her shields the best that money can buy.",
                portraitTint = new Color(0.4f, 0.9f, 0.7f)
            }.With(HeroBuff.ShieldRegen, 0.40f).With(HeroBuff.Reinforcement, 0.30f),

            // ---- Ashcrown Legion ----
            new HeroDefinition
            {
                id = "kethrax_vol", name = "Kethrax Vol", title = "Pyre-Marshal",
                factionName = "Ashcrown Legion", shipClass = "Avanti_Capital", flagshipName = "ALP Cinderfall",
                biography = "Zealot of the burning crown. Vol would sooner cinder a world than surrender it, and his crews would sooner die than be taken. They fight to the last ship.",
                portraitTint = new Color(1f, 0.55f, 0.15f)
            }.With(HeroBuff.Damage, 0.20f).With(HeroBuff.Resolve, 0.60f),
        };
    }
}
