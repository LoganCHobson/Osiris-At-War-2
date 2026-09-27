using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class GalacticMapManager : MonoBehaviour
{
    [Header("Raycasting")]
    public LayerMask fleetLayer;
    public LayerMask planetLayer;

    [Header("Drag order preview")]
    public LineRenderer orderLinePreview;
    public Color validOrderColor = Color.cyan;
    public Color invalidOrderColor = Color.red;

    [Header("Hyperspace lanes")]
    public Color laneColor = Color.cyan;
    public float laneWidth = 0.15f;

    private Camera cam;
    private readonly List<GalacticFleet> selectedFleets = new List<GalacticFleet>();
    private bool isDragging;
    private Vector3 dragOrigin;

    private void Start()
    {
        cam = Camera.main;
        DrawHyperspaceLanes();

        if (orderLinePreview != null)
        {
            orderLinePreview.positionCount = 0;
        }
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            HandlePress();
        }
        else if (isDragging && Input.GetMouseButton(0))
        {
            HandleDrag();
        }
        else if (isDragging && Input.GetMouseButtonUp(0))
        {
            HandleRelease();
        }
        else if (Input.GetMouseButtonDown(1))
        {
            DeselectAll();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            FleetPanel.Instance?.Hide();
        }
    }

    private void HandlePress()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, fleetLayer))
        {
            GalacticFleet fleet = hit.collider.GetComponentInParent<GalacticFleet>();
            if (fleet == null) return;

            if (Input.GetKey(KeyCode.LeftShift))
            {
                if (!selectedFleets.Contains(fleet))
                {
                    selectedFleets.Add(fleet);
                }
            }
            else if (!selectedFleets.Contains(fleet))
            {
                DeselectAll();
                selectedFleets.Add(fleet);
            }

            isDragging = true;
            dragOrigin = fleet.transform.position;
            FleetPanel.Instance?.Hide();
            return;
        }

        if (Physics.Raycast(ray, out RaycastHit planetHit, Mathf.Infinity, planetLayer))
        {
            Planet planet = planetHit.collider.GetComponentInParent<Planet>();
            if (planet != null && FleetPanel.Instance != null)
            {
                FleetPanel.Instance.Show(planet);
            }
            return;
        }

        FleetPanel.Instance?.Hide();
    }

    private void HandleDrag()
    {
        if (orderLinePreview == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 dragPoint;
        Planet hoveredPlanet = null;

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, planetLayer))
        {
            hoveredPlanet = hit.collider.GetComponentInParent<Planet>();
            dragPoint = hit.collider.transform.position;
        }
        else
        {
            Plane mapPlane = new Plane(Vector3.up, dragOrigin);
            dragPoint = mapPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : dragOrigin;
        }

        bool valid = hoveredPlanet != null && CanAnySelectedFleetReach(hoveredPlanet);

        orderLinePreview.positionCount = 2;
        orderLinePreview.SetPosition(0, dragOrigin);
        orderLinePreview.SetPosition(1, dragPoint);

        Color lineColor = valid ? validOrderColor : invalidOrderColor;
        orderLinePreview.startColor = lineColor;
        orderLinePreview.endColor = lineColor;
    }

    private bool CanAnySelectedFleetReach(Planet planet)
    {
        foreach (GalacticFleet fleet in selectedFleets)
        {
            if (fleet.currentPlanet == null) continue;
            if (fleet.currentPlanet == planet) return true;
            if (GalacticPathfinder.FindPath(fleet.currentPlanet, planet, false).Count >= 2) return true;
        }
        return false;
    }

    private void HandleRelease()
    {
        isDragging = false;

        if (orderLinePreview != null)
        {
            orderLinePreview.positionCount = 0;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, planetLayer))
        {
            return;
        }

        Planet targetPlanet = hit.collider.GetComponentInParent<Planet>();
        if (targetPlanet == null) return;

        foreach (GalacticFleet fleet in selectedFleets)
        {
            fleet.TrySetDestination(targetPlanet);
        }
    }

    private void DeselectAll()
    {
        selectedFleets.Clear();
        FleetPanel.Instance?.Hide();
    }

    private void DrawHyperspaceLanes()
    {
        Planet[] planets = FindObjectsByType<Planet>(FindObjectsSortMode.None);
        HashSet<(Planet, Planet)> drawn = new HashSet<(Planet, Planet)>();

        foreach (Planet planet in planets)
        {
            foreach (Planet connection in planet.connections)
            {
                if (drawn.Contains((connection, planet))) continue;
                drawn.Add((planet, connection));

                GameObject laneObj = new GameObject($"Lane_{planet.planetName}-{connection.planetName}");
                laneObj.transform.SetParent(transform);

                LineRenderer line = laneObj.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.SetPosition(0, planet.transform.position);
                line.SetPosition(1, connection.transform.position);
                line.startWidth = laneWidth;
                line.endWidth = laneWidth;
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = laneColor;
                line.endColor = laneColor;
            }
        }
    }
}
