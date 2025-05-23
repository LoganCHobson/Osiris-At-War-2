using SolarStudios;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class PathVisualization : MonoBehaviour
{
    private PlayerUnitStateMachine stateMachine;
    private PlayerUnitMoveState moveState;
    private NavMeshAgent agent;
    private LineRenderer lineRenderer;

    private List<Vector3> pathPoints = new List<Vector3>();

    void Start()
    {
        stateMachine = GetComponentInChildren<PlayerUnitStateMachine>();
        moveState = GetComponentInChildren<PlayerUnitMoveState>();
        agent = GetComponent<NavMeshAgent>();
        lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.positionCount = 0;
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = Color.green;
        lineRenderer.endColor = Color.green;

        
    }

    void Update()
    {
        if (agent.hasPath || (Object)stateMachine.currentState == moveState)
        {
            NavMeshPath path = agent.path;
            if (path != null && path.corners.Length > 0)
            {
                pathPoints.Clear();
                pathPoints.AddRange(path.corners);

                lineRenderer.positionCount = pathPoints.Count;
                lineRenderer.SetPositions(pathPoints.ToArray());
            }
        }
    }
}
