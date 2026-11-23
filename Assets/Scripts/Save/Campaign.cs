using System;
using System.Globalization;

// Which campaign is being played: an id made when it starts and carried in every save of it (and reloaded with
// them), so its battle replays can be filed together whichever save they were fought from.
public static class Campaign
{
    private static string id, started;

    public static string Id
    {
        get
        {
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 12);
                started = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            }
            return id;
        }
    }

    public static string Started { get { _ = Id; return started; } }

    public static void Resume(string campaignId, string startedAt)
    {
        id = campaignId;
        started = string.IsNullOrEmpty(startedAt) ? DateTime.Now.ToString("o", CultureInfo.InvariantCulture) : startedAt;
    }

    public static void Clear() { id = null; started = null; }
}
