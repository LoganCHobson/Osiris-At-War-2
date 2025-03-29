using SolarStudios;
using UnityEngine;

public class StationController : MonoBehaviour
{

    private SpaceUnit unit;
    public GameObject prefab;
    public float cost = 100;

    public Transform spawnpoint;

    
    private void Start()
    {
        unit = GetComponentInParent<SpaceUnit>();
    }

    private void Update()
    {
        GameManager.Instance.playerCash += Time.deltaTime; //Stations should have passive income as well to prevent soft lock.
    }
    public void MakeShip()
    {
        GameObject temp = Instantiate(prefab, spawnpoint);
        if(gameObject.layer == 8) //Just for when the AI does it. FOr now.
        {
            GameManager.Instance.enemyCash -= cost;
        }
       
    }
}
