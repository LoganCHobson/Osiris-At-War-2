using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    public List<SpaceUnit> allFriendlyUnits = new List<SpaceUnit>();
    public List<SpaceUnit> allEnemyUnits = new List<SpaceUnit>();
    public List<AstroidMiner> allAstroidMiners = new List<AstroidMiner>();
    public float enemyCash = 0f;
    public float playerCash = 0f;    
    
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

    

    public void EndGame()
    {

    }
}
