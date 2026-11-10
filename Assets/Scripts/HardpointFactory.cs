using UnityEngine;
using UnityEngine.UI;

// Adds a hardpoint to a ship at runtime (missile tubes, etc.): a node with a box collider for shots to hit,
// a HardpointHealth worth an average hardpoint of that ship, and a copy of the ship's own hardpoint marker so
// it can be seen and clicked like the rest. Must be called before the ship totals its health (its Start).
public static class HardpointFactory
{
    public static HardpointHealth Create(GameObject ship, string name, Vector3 localPosition, Vector3 localSize)
    {
        HardpointHealth template = null;
        float total = 0f;
        int count = 0;
        foreach (HardpointHealth hardpoint in ship.GetComponentsInChildren<HardpointHealth>(true))
        {
            total += hardpoint.maxHealth;
            count++;
            if (template == null || (template.healthVisual == null && hardpoint.healthVisual != null)) template = hardpoint;
        }

        GameObject node = new GameObject(name);
        node.layer = template != null ? template.gameObject.layer : ship.layer;
        node.transform.SetParent(ship.transform, false);
        node.transform.localPosition = localPosition;

        BoxCollider collider = node.AddComponent<BoxCollider>();
        collider.size = localSize;

        HardpointHealth health = node.AddComponent<HardpointHealth>();
        health.maxHealth = count > 0 ? total / count : 100f;
        health.currentHealth = health.maxHealth;
        health.die = new UnityEngine.Events.UnityEvent();
        health.dealDamage = new UnityEngine.Events.UnityEvent();

        if (template != null && template.healthVisual != null)
        {
            Canvas canvas = template.healthVisual.GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                Image[] originals = canvas.GetComponentsInChildren<Image>(true);
                int index = System.Array.IndexOf(originals, template.healthVisual);
                GameObject copy = Object.Instantiate(canvas.gameObject, node.transform);
                copy.transform.localPosition = canvas.transform.localPosition;
                Image[] copies = copy.GetComponentsInChildren<Image>(true);
                if (index >= 0 && index < copies.Length) health.healthVisual = copies[index];
            }
        }

        if (ship.TryGetComponent(out HardpointManager manager) && !manager.hardpoints.Contains(health)) manager.hardpoints.Add(health);
        return health;
    }
}
