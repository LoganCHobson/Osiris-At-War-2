using UnityEngine;

// The clock for presentation effects (the cinematic camera, FTL jumps). Normally real time, so they run at
// the same pace whatever the game speed. Frame-by-frame recordings switch it to game time; otherwise the
// slow capture lets real time race ahead and these effects play too fast or get skipped on film.
public static class PresentationClock
{
    public static bool FollowGameTime;

    public static float Now => FollowGameTime ? Time.time : Time.unscaledTime;
    public static float Delta => FollowGameTime ? Time.deltaTime : Time.unscaledDeltaTime;
}
