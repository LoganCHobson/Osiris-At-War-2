using UnityEngine;

public static class MapText
{
    public const string Good = "#7CFC7C";
    public const string Warning = "#FFB040";
    public const string Bad = "#FF6060";
    public const string Muted = "#AAAAAA";
    public const string Site = "#FF9A00";

    public static string Colored(Faction faction, string fallback = "Unclaimed")
    {
        if (faction == null) return fallback;
        return $"<color=#{ColorUtility.ToHtmlStringRGB(faction.color)}>{faction.factionName}</color>";
    }

    public static string Tint(string text, string hex)
    {
        return $"<color={hex}>{text}</color>";
    }

    public static string Plural(int count, string word)
    {
        return count == 1 ? $"{count} {word}" : $"{count} {word}s";
    }

    public static string RealTime(float gameSeconds)
    {
        float speed = GameSpeed.Speed > 0f ? GameSpeed.Speed : 1f;
        return Duration(gameSeconds / speed);
    }

    public static string Duration(float seconds)
    {
        int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return total < 60 ? $"{total}s" : $"{total / 60}m {total % 60:00}s";
    }
}
