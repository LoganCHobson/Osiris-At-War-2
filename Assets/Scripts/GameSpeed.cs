using UnityEngine;

public static class GameSpeed
{
    private static float baseFixedDeltaTime;

    public static float Speed { get; private set; } = 1f;
    public static bool Suspended { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        baseFixedDeltaTime = Time.fixedDeltaTime;
        Speed = 1f;
        Suspended = false;
    }

    public static void SetSpeed(float speed)
    {
        Speed = Mathf.Max(0f, speed);
        Apply();
    }

    public static void SetSuspended(bool suspended)
    {
        Suspended = suspended;
        Apply();
    }

    public static void Reset()
    {
        Speed = 1f;
        Suspended = false;
        Apply();
    }

    private static void Apply()
    {
        if (baseFixedDeltaTime <= 0f)
        {
            baseFixedDeltaTime = Time.fixedDeltaTime;
        }

        float scale = Suspended ? 0f : Speed;
        Time.timeScale = scale;
        Time.fixedDeltaTime = baseFixedDeltaTime * Mathf.Max(1f, scale);
    }
}
