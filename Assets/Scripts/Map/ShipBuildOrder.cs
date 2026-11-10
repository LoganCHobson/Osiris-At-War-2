[System.Serializable]
public class ShipBuildOrder
{
    public Ship ship;
    public float remainingTime;
    // Production orders: when built, the ship is sent on to this planet (by name) instead of staying at the yard.
    public string deliverTo;
}
