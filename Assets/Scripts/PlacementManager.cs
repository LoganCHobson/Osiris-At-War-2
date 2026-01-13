using UnityEngine;

public class PlacementManager : MonoBehaviour
{
    public bool placementMode = false;

    public GameObject unitList;


    private GameObject shipToPlace;
    private Vector3 posToPlace;

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



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if(placementMode == false) 
        {
            Debug.Log("Not in placement mode");
            return;
        }

        if(Input.GetMouseButtonDown(1))
        {
            placementMode = false;

            ResetPlacement();
            //Dont place unit
        }
        if(Input.GetMouseButtonUp(0))
        {
           
            Instantiate(shipToPlace, posToPlace, Quaternion.identity);
            Debug.Log("Placed Unit");

             placementMode = false;
        }
        



    }


    public void EnablePlacement(GameObject obj) //Set via button
    {
        
        placementMode = true;
        
        shipToPlace = obj.GetComponentInParent<IconShipRef>().ship.prefab;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            posToPlace = hit.point;
            Debug.Log("Placing unit at: " + posToPlace);
        }
        else
        {
            posToPlace = Vector3.zero;
            Debug.LogError("Could not place unit, no raycast hit");
        }

       
    }

    void ResetPlacement()
    {
       
    }
}
