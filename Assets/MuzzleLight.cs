using UnityEngine;

public class MuzzleLight : MonoBehaviour
{
    public ParticleSystem part;
    private Light lightComp;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        lightComp = GetComponent<Light>();
    }

    // Update is called once per frame
    void Update()
    {
        if(part.isPlaying)
        {
            lightComp.enabled = true;
        }
        else
        {
            lightComp.enabled = false;
        }
    }
}
