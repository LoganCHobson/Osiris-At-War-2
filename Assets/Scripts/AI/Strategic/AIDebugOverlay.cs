using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class AIDebugOverlay : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F3;
    public KeyCode cycleFactionKey = KeyCode.F4;
    public bool visible;

    public static AIDebugOverlay Instance { get; private set; }

    public Faction FocusedFaction
    {
        get
        {
            AIDirector director = AIDirector.Instance;
            return director != null && focusIndex >= 0 && focusIndex < director.brains.Count ? director.brains[focusIndex].faction : null;
        }
    }

    private int focusIndex = -1;

    private void Awake()
    {
        Instance = this;
    }
    private Vector2 scroll;
    private GUIStyle panelStyle;
    private GUIStyle labelStyle;
    private GUIStyle worldLabelStyle;
    private readonly StringBuilder builder = new StringBuilder();

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
        }

        if (visible && Input.GetKeyDown(cycleFactionKey) && AIDirector.Instance != null)
        {
            int count = AIDirector.Instance.brains.Count;
            focusIndex = count == 0 ? -1 : (focusIndex + 2) % (count + 1) - 1;
        }
    }

    private void OnGUI()
    {
        if (!visible || AIDirector.Instance == null) return;

        EnsureStyles();

        if (!AIDirector.MapIsActive)
        {
            DrawBattlePanel();
            return;
        }

        AIDirector director = AIDirector.Instance;
        FactionBrain focus = focusIndex >= 0 && focusIndex < director.brains.Count ? director.brains[focusIndex] : null;

        DrawWorld(director, focus);
        DrawPanel(director, focus);
    }

    private void EnsureStyles()
    {
        if (panelStyle != null) return;

        panelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6) };
        panelStyle.normal.background = MakeTexture(new Color(0f, 0f, 0f, 0.75f));

        labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, wordWrap = true };
        labelStyle.normal.textColor = Color.white;

        worldLabelStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontSize = 11 };
    }

    private void DrawPanel(AIDirector director, FactionBrain focus)
    {
        builder.Clear();
        builder.AppendLine($"<b>AI DEBUG</b>  [{toggleKey}] hide  [{cycleFactionKey}] focus: {(focus != null ? Colored(focus.faction) : "All")}");
        builder.AppendLine($"Galactic time {director.GalacticTime:0}s");
        builder.AppendLine();

        if (focus != null)
        {
            AppendDetail(focus);
        }
        else
        {
            foreach (FactionBrain brain in director.brains)
            {
                AppendSummary(brain);
            }
        }

        Rect area = new Rect(10f, 10f, 400f, Screen.height - 20f);
        GUILayout.BeginArea(area, panelStyle);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label(builder.ToString(), labelStyle);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawBattlePanel()
    {
        AIBattleCommander[] commanders = FindObjectsByType<AIBattleCommander>(FindObjectsSortMode.None);
        if (commanders.Length == 0) return;

        builder.Clear();
        builder.AppendLine($"<b>AI BATTLE</b>  [{toggleKey}] hide");

        foreach (AIBattleCommander commander in commanders)
        {
            builder.AppendLine($"<b>{Colored(commander.Faction)}</b> ({(commander.IsAttackerSide ? "attacker" : "defender")})");
            builder.AppendLine($"  stance {commander.CurrentStance}  force ratio {commander.ForceRatio:0.00}");
            builder.AppendLine($"  on field {commander.FieldCount}  reserve {commander.ReserveCount}");
        }

        GUILayout.BeginArea(new Rect(10f, 10f, 320f, 30f + commanders.Length * 60f), panelStyle);
        GUILayout.Label(builder.ToString(), labelStyle);
        GUILayout.EndArea();
    }

    private void AppendSummary(FactionBrain brain)
    {
        builder.AppendLine($"<b>{Colored(brain.faction)}</b>{(brain.IsEliminated ? " <color=#ff6666>ELIMINATED</color>" : "")}");
        builder.AppendLine($"  ${Currency(brain)}  +{brain.IncomePerMinute:0}/min  power {brain.TotalPower:0}  planets {brain.owned.Count}  fleets {brain.myFleets.Count}");
        builder.AppendLine($"  threat {brain.TotalThreat:0}  primary threat: {(brain.PrimaryThreat != null ? Colored(brain.PrimaryThreat) : "none")}");

        foreach (FactionBrain.Campaign campaign in brain.campaigns)
        {
            builder.AppendLine($"  campaign vs {Colored(campaign.target)} -> {campaign.objectivePlanet} {(campaign.suspended ? "<color=#ffaa00>(suspended)</color>" : "")} staged {campaign.stagedPower:0}/{campaign.requiredPower:0}");
        }

        builder.AppendLine($"  economy: {brain.EconomyIntent}");
        if (brain.log.Count > 0)
        {
            builder.AppendLine($"  <color=#aaaaaa>{brain.log[0]}</color>");
        }
        builder.AppendLine();
    }

    private void AppendDetail(FactionBrain brain)
    {
        AIPersonality p = brain.personality;
        AppendSummary(brain);

        builder.AppendLine("<b>Personality</b>");
        builder.AppendLine($"  {p.name}: aggr {p.aggression:0.00}  def {p.defensiveness:0.00}  exp {p.expansion:0.00}  econ {p.economy:0.00}  venge {p.vengefulness:0.00}");
        builder.AppendLine();

        builder.AppendLine("<b>Grievances</b>");
        List<KeyValuePair<Faction, float>> grudges = new List<KeyValuePair<Faction, float>>(brain.grievance);
        grudges.Sort((a, b) => b.Value.CompareTo(a.Value));
        foreach (KeyValuePair<Faction, float> entry in grudges)
        {
            builder.AppendLine($"  {Colored(entry.Key)}: {entry.Value:0}");
        }
        builder.AppendLine();

        builder.AppendLine("<b>Target scores</b>");
        foreach (KeyValuePair<Faction, float> entry in brain.targetScores)
        {
            builder.AppendLine($"  {Colored(entry.Key)}: {entry.Value:0.00}");
        }
        builder.AppendLine();

        builder.AppendLine("<b>Campaigns</b>");
        foreach (FactionBrain.Campaign campaign in brain.campaigns)
        {
            builder.AppendLine($"  vs {Colored(campaign.target)}  score {campaign.score:0.00}  success {campaign.Success:0.00}");
            builder.AppendLine($"    objective {campaign.objectivePlanet}  staging {campaign.stagingPlanet}");
            builder.AppendLine($"    lost {campaign.powerLost:0}  destroyed {campaign.enemyPowerDestroyed:0}  taken {campaign.planetsTaken}");
        }
        builder.AppendLine();

        builder.AppendLine($"<b>Military tasks</b>  rally: {brain.RallyPlanet ?? "-"}{(brain.CampaignsSuspended ? "  <color=#ffaa00>offensives paused</color>" : "")}");
        foreach (FactionBrain.MilitaryTask task in brain.activeTasks)
        {
            string color = task.assigned >= task.required ? "#88ff88" : "#ffcc66";
            string lingering = task.lastNeededAt < AIDirector.Instance.GalacticTime - 0.5f ? " <color=#888888>(holding)</color>" : "";
            builder.AppendLine($"  <color={color}>{task.kind} {task.planetName}  {task.assigned:0}/{task.required:0}</color>  p{task.priority:0}{(task.critical ? " CRIT" : "")}{lingering}");
        }
        builder.AppendLine();

        if (brain.enemyComposition.Count > 0)
        {
            builder.AppendLine("<b>Enemy composition seen</b>");
            foreach (KeyValuePair<FleetRole, float> entry in brain.enemyComposition)
            {
                builder.AppendLine($"  {entry.Key}: {entry.Value:0}");
            }
            builder.AppendLine();
        }

        builder.AppendLine("<b>Desired composition</b>");
        foreach (KeyValuePair<FleetRole, float> entry in brain.DesiredComposition())
        {
            builder.AppendLine($"  {entry.Key}: {entry.Value:P0}");
        }
        builder.AppendLine();

        builder.AppendLine($"<b>Fleet orders</b>  (required power {brain.RequiredPower:0})");
        foreach (FactionBrain.FleetOrder order in brain.orders.Values)
        {
            builder.AppendLine($"  {order.kind} -> {order.planetName}  <color=#888888>{order.fleetName}</color>");
        }
        builder.AppendLine();

        builder.AppendLine("<b>Log</b>");
        foreach (string entry in brain.log)
        {
            builder.AppendLine($"  {entry}");
        }
    }

    private void DrawWorld(AIDirector director, FactionBrain focus)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        director.World.EnsurePlanets();

        foreach (FactionBrain brain in director.brains)
        {
            if (focus != null && brain != focus) continue;

            foreach (FactionBrain.FleetOrder order in brain.orders.Values)
            {
                GalacticFleet fleet = director.World.FindFleet(order.fleetName);
                Planet planet = director.World.FindPlanet(order.planetName);
                if (fleet == null || planet == null) continue;

                if (!ToScreen(cam, fleet.transform.position, out Vector2 from) || !ToScreen(cam, planet.transform.position, out Vector2 to)) continue;

                Color color = brain.faction.color;
                float width = order.kind == FactionBrain.OrderKind.Attack ? 4f : 2f;
                if (from != to)
                {
                    DrawLine(from, to, color, width);
                }

                string holding = fleet.IsHolding ? " HOLD" : "";
                DrawWorldLabel(from + new Vector2(0f, -18f), $"{order.kind}{holding} {Strength.Of(fleet):0}", color);
            }

            foreach (FactionBrain.Campaign campaign in brain.campaigns)
            {
                Planet objective = director.World.FindPlanet(campaign.objectivePlanet);
                if (objective != null && ToScreen(cam, objective.transform.position, out Vector2 point))
                {
                    DrawWorldLabel(point + new Vector2(0f, 22f), $"TARGET of {brain.faction.factionName}", brain.faction.color);
                }
            }
        }

        foreach (Planet planet in director.World.planets)
        {
            if (!ToScreen(cam, planet.transform.position, out Vector2 point)) continue;

            builder.Clear();
            if (director.World.IsChokepoint(planet))
            {
                builder.Append($"CHOKE {director.World.ChokeScore(planet):0.00}");
            }

            if (focus != null)
            {
                if (!focus.visible.Contains(planet))
                {
                    builder.Append(builder.Length > 0 ? "  " : "").Append("<color=#777777>fog</color>");
                }

                float threat = focus.ThreatAt(planet);
                if (threat > 0f)
                {
                    builder.Append(builder.Length > 0 ? "  " : "").Append($"<color=#ff5555>threat {threat:0}</color>");
                }

                float seen = focus.EstimatedFleetPower(planet);
                if (seen > 0f && !focus.ownedSet.Contains(planet))
                {
                    builder.Append(builder.Length > 0 ? "  " : "").Append($"<color=#ffaa55>seen {seen:0}</color>");
                }
            }

            if (builder.Length > 0)
            {
                DrawWorldLabel(point + new Vector2(0f, 36f), builder.ToString(), Color.white);
            }
        }
    }

    private void DrawWorldLabel(Vector2 center, string text, Color color)
    {
        GUIContent content = new GUIContent(text);
        Vector2 size = worldLabelStyle.CalcSize(content);
        Rect rect = new Rect(center.x - size.x * 0.5f - 3f, center.y - size.y * 0.5f, size.x + 6f, size.y);

        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.Label(rect, content, worldLabelStyle);
        GUI.color = previous;
    }

    private static void DrawLine(Vector2 from, Vector2 to, Color color, float width)
    {
        Matrix4x4 matrix = GUI.matrix;
        Color previous = GUI.color;

        float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
        float length = (to - from).magnitude;

        GUIUtility.RotateAroundPivot(angle, from);
        GUI.color = color;
        GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, length, width), Texture2D.whiteTexture);

        GUI.matrix = matrix;
        GUI.color = previous;
    }

    private static bool ToScreen(Camera cam, Vector3 world, out Vector2 screen)
    {
        Vector3 point = cam.WorldToScreenPoint(world);
        screen = new Vector2(point.x, Screen.height - point.y);
        return point.z > 0f;
    }

    private static string Colored(Faction faction)
    {
        if (faction == null) return "Unclaimed";
        return $"<color=#{ColorUtility.ToHtmlStringRGB(faction.color)}>{faction.factionName}</color>";
    }

    private static int Currency(FactionBrain brain)
    {
        return GalacticState.Instance != null ? GalacticState.Instance.GetCurrency(brain.faction) : 0;
    }

    private static Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
