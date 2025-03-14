using UnityEngine;

public class MuzzleLight : MonoBehaviour
{
    public ParticleSystem part;
    private Light light;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        light = GetComponent<Light>();
    }

    // Update is called once per frame
    void Update()
    {
        if(part.isPlaying)
        {
            light.enabled = true;
        }
        else
        {
            light.enabled = false;
        }
    }
}
