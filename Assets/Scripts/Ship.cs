using UnityEngine;

[System.Serializable]
public class Ship : MonoBehaviour
{
    public GameObject icon;

    public GameObject prefab;

    public int cost;
    public float buildTime = 20f;
}
