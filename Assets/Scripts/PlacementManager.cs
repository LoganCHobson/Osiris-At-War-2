using UnityEngine;

public class PlacementManager : MonoBehaviour
{
    public static PlacementManager Instance;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public bool TryPlace(Ship ship, Vector2 screenPosition, GameObject sourceIcon)
    {
        if (ship == null || ship.prefab == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            return false;
        }

        Instantiate(ship.prefab, hit.point, Quaternion.identity);
        Debug.Log("Placed unit at: " + hit.point);

        Destroy(sourceIcon);
        return true;
    }
}
