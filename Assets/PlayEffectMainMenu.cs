using UnityEngine;

public class PlayEffectMainMenu : MonoBehaviour
{
    public AudioSource woosh1;
    public AudioSource woosh2;
    public AudioSource woosh3;

    public Animator anim; 
    public void Woosh(string value)
    {
        if(value == "1")
        {
            woosh1.Play();
        }
        if (value == "2")
        {
            woosh2.Play();
        }
        if (value == "3")
        {
            woosh3.Play();
        }

    }

    public void FadeIn()
    {
        anim.Play("FadeIn");
    }
}
