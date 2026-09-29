using UnityEngine;

public class PlayerSideOnly : MonoBehaviour
{
    public GameObject[] objects;
    public Component[] components;

    public void Strip()
    {
        foreach (GameObject target in objects)
        {
            if (target != null) target.SetActive(false);
        }

        foreach (Component target in components)
        {
            if (target is Behaviour behaviour) behaviour.enabled = false;
            else if (target is Renderer renderer) renderer.enabled = false;
            else if (target is Collider collider) collider.enabled = false;
        }
    }
}
