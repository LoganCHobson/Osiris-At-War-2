using UnityEngine;

public class PlanetManager : MonoBehaviour
{
    public GameObject fleetSlot1;
    public GameObject fleetSlot2;
    public GameObject fleetSlot3;
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void ToggleFleetSlot(GameObject obj)
    { 
        obj.SetActive(!obj.activeInHierarchy); 
    }
}
