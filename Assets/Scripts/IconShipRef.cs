using UnityEngine;
using UnityEngine.UI;

public class IconShipRef : MonoBehaviour
{
    public Ship ship;

    public void Start()
    {
        GetComponentInChildren<Button>().onClick.AddListener(() => PlacementManager.Instance.EnablePlacement(gameObject));
    }
}
