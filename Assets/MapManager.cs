using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    public List<PlanetManager> planets = new List<PlanetManager>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        PlanetManager[] foundPlanets = GameObject.FindObjectsByType<PlanetManager>(FindObjectsSortMode.None);
        planets = foundPlanets.ToList();

        ToggleAllPlanetsFleetSlots();
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void ToggleAllPlanetsFleetSlots()
    {
        foreach (PlanetManager planet in planets)
        {
            if(planet.fleetSlot1.transform.childCount == 0)
            {
                planet.fleetSlot1.SetActive(!planet.fleetSlot1.activeInHierarchy);
            }
            
            if(planet.fleetSlot2.transform.childCount == 0)
            {
                planet.fleetSlot2.SetActive(!planet.fleetSlot2.activeInHierarchy);
            }
            
            if(planet.fleetSlot3.transform.childCount == 0)
            {
                planet.fleetSlot3.SetActive(!planet.fleetSlot3.activeInHierarchy);
            }
            
        }
    }
}
