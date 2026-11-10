using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// What's left when a turret hardpoint is destroyed. The prefabs used to just switch the turret's model off and
// play a fire placed below the mount (inside the hull on most ships, and on the wrong side of upside-down
// ventral mounts). Now the turret is wrecked in one of several ways, and the blast, fire and smoke sit on the
// mount itself, pouring outward from the hull:
//   Torn barrels - the barrels are ripped off and tumble away; the housing is left blackened and burning.
//   Split open   - the housing is blown apart into two blackened halves peeled back from the mount, barrels
//                  slumped, fire inside.
//   Blown off    - the whole turret is thrown off its mount, leaving a burning crater.
//   Slumped      - gutted where it stands: blackened, barrels drooping, smoke and sparks.
// Fitted to every turret by GameManager.ApplyAllegiance.
[DisallowMultipleComponent]
public class HardpointWreck : MonoBehaviour
{
    private enum Fate { TornBarrels, SplitOpen, BlownOff, Slumped }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private const float Charred = 0.16f;

    private HardpointHealth health;
    private TurretController turret;
    private HullDamage hull;
    private ParticleSystem ownFire;
    private ParticleSystem ownBlast;
    private bool wrecked;

    public static void Fit(GameObject ship)
    {
        foreach (TurretController gun in ship.GetComponentsInChildren<TurretController>(true))
        {
            if (gun.GetComponent<HardpointHealth>() != null && gun.GetComponent<HardpointWreck>() == null) gun.gameObject.AddComponent<HardpointWreck>();
        }
    }

    private void Awake()
    {
        health = GetComponent<HardpointHealth>();
        turret = GetComponent<TurretController>();
        hull = GetComponentInParent<HullDamage>();

        // Take over from the prefab's own death wiring: no more hiding the model, and the fire/blast are
        // replayed from the right place by Wreck(). Sound and switching the gun off stay as they were.
        UnityEvent die = health.die;
        for (int i = 0; i < die.GetPersistentEventCount(); i++)
        {
            Object target = die.GetPersistentTarget(i);
            string method = die.GetPersistentMethodName(i);
            if (target is GameObject && method == "SetActive") die.SetPersistentListenerState(i, UnityEventCallState.Off);
            else if (target is ParticleSystem system && method == "Play")
            {
                die.SetPersistentListenerState(i, UnityEventCallState.Off);
                if (system.main.loop) ownFire = system;
                else ownBlast = system;
            }
        }
        die.AddListener(Wreck);
    }

    private void Wreck()
    {
        if (wrecked) return;
        wrecked = true;

        Transform mountBase = turret != null && turret.turretBase != null ? turret.turretBase : transform;
        Vector3 outward = transform.up; // Ventral mounts are upside down, so this points away from the hull either way.

        List<MeshRenderer> housing = new List<MeshRenderer>();
        List<MeshRenderer> barrels = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in mountBase.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (turret != null && turret.turretBarrel != null && renderer.transform.IsChildOf(turret.turretBarrel)) barrels.Add(renderer);
            else housing.Add(renderer);
        }

        Bounds bounds = new Bounds(mountBase.position, Vector3.zero);
        foreach (MeshRenderer renderer in housing) bounds.Encapsulate(renderer.bounds);
        foreach (MeshRenderer renderer in barrels) bounds.Encapsulate(renderer.bounds);
        float size = Mathf.Max(0.3f, bounds.extents.magnitude);
        Vector3 centre = bounds.center;

        Fate fate = PickFate(housing.Count > 0, barrels.Count > 0, size);

        // The blast, on the turret itself.
        if (ownBlast != null)
        {
            ownBlast.transform.position = centre;
            ownBlast.transform.rotation = Quaternion.FromToRotation(Vector3.up, outward);
            ownBlast.Play(true);
        }
        DestructionProfile profile = hull != null ? hull.profile : null;
        if (profile != null)
        {
            DestructionFx.Explode(profile.smallExplosion, centre, size * 0.9f, profile.explosionLifetime);
            DestructionFx.Flash(centre, new Color(1f, 0.6f, 0.3f), 3f, size * 8f, 0.35f);
        }

        switch (fate)
        {
            case Fate.TornBarrels:
                Throw(barrels, outward + Random.insideUnitSphere * 0.6f, size, 6f, 2.2f);
                Char(housing);
                Burn(centre + outward * size * 0.15f, outward, 1f);
                break;

            case Fate.SplitOpen:
                SplitHousing(housing, mountBase, outward, size);
                Droop(barrels);
                Char(barrels);
                Burn(mountBase.position + outward * size * 0.1f, outward, 1.25f);
                break;

            case Fate.BlownOff:
                List<MeshRenderer> all = new List<MeshRenderer>(housing);
                all.AddRange(barrels);
                Throw(all, outward * 1.6f + Random.insideUnitSphere * 0.4f, size, 4.5f, 1.4f);
                Crater(mountBase.position, outward, housing);
                Burn(mountBase.position + outward * size * 0.05f, outward, 1.4f);
                break;

            default:
                Char(housing);
                Char(barrels);
                Droop(barrels);
                Burn(centre, outward, 0.8f);
                Sparks(centre, outward, size, profile);
                break;
        }
    }

    // Bigger guns come apart more violently; anything without separate barrels can't lose them.
    private static Fate PickFate(bool hasHousing, bool hasBarrels, float size)
    {
        if (!hasHousing) return Fate.BlownOff;
        float roll = Random.value;
        if (!hasBarrels) return roll < 0.45f ? Fate.BlownOff : roll < 0.75f ? Fate.SplitOpen : Fate.Slumped;
        return roll < 0.3f ? Fate.TornBarrels : roll < 0.55f ? Fate.SplitOpen : roll < 0.8f ? Fate.BlownOff : Fate.Slumped;
    }

    // ---------------- The fates ----------------

    // Copies the renderers into a single tumbling piece of debris (burning, blackened) and hides the originals.
    private void Throw(List<MeshRenderer> renderers, Vector3 direction, float size, float speed, float life)
    {
        if (renderers.Count == 0) return;
        GameObject debris = new GameObject(name + "_Debris");
        debris.transform.position = renderers[0].bounds.center;
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer.name.Contains("LOD") && !renderer.name.Contains("LOD0")) { renderer.enabled = false; continue; }
            GameObject piece = new GameObject(renderer.name);
            piece.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            piece.transform.localScale = renderer.transform.lossyScale;
            piece.transform.SetParent(debris.transform, true);
            piece.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer copy = piece.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            Darken(copy, 0.35f);
            renderer.enabled = false;
        }

        Vector3 shipVelocity = Vector3.zero;
        UnityEngine.AI.NavMeshAgent agent = GetComponentInParent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) shipVelocity = agent.velocity;

        DestructionProfile profile = hull != null ? hull.profile : null;
        if (profile != null)
        {
            WreckChunk chunk = debris.AddComponent<WreckChunk>();
            chunk.Launch(profile, shipVelocity + direction.normalized * speed * Mathf.Sqrt(size), Random.insideUnitSphere * Random.Range(120f, 320f), size * 0.4f, false, Random.Range(3f, 6f) * life, false);
            if (ownFire != null) DestructionFx.Attach(ownFire, debris.transform, debris.transform.position, 0.6f);
        }
        else
        {
            Destroy(debris, 4f);
        }
    }

    // The housing torn in two along a rough vertical cut, each half peeled back off the mount.
    private void SplitHousing(List<MeshRenderer> housing, Transform mountBase, Vector3 outward, float size)
    {
        Vector3 cutNormal = Quaternion.AngleAxis(Random.Range(-30f, 30f), outward) * mountBase.right;
        Material interior = hull != null && hull.profile != null ? hull.profile.interiorMaterial : null;

        foreach (MeshRenderer renderer in housing)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) { Darken(renderer, Charred); continue; }

            MeshSlicer.MeshData data = MeshSlicer.MeshData.Read(filter.sharedMesh);
            Vector3 localNormal = renderer.transform.InverseTransformDirection(cutNormal).normalized;
            Vector3 localPoint = renderer.transform.InverseTransformPoint(mountBase.position + Random.insideUnitSphere * size * 0.1f);
            MeshSlicer.Result[] halves = MeshSlicer.Slice(data, new[] { new Plane(localNormal, localPoint) }, interior != null, filter.sharedMesh.name, renderer.transform.InverseTransformDirection(outward));
            if (halves.Length != 2 || halves[0].mesh == null || halves[1].mesh == null) { Darken(renderer, Charred); continue; }

            for (int side = 0; side < 2; side++)
            {
                GameObject half = new GameObject(renderer.name + (side == 0 ? "_Left" : "_Right"));
                half.layer = renderer.gameObject.layer;
                half.transform.SetParent(renderer.transform.parent, false);
                half.transform.localPosition = renderer.transform.localPosition;
                half.transform.localRotation = renderer.transform.localRotation;
                half.transform.localScale = renderer.transform.localScale;
                half.AddComponent<MeshFilter>().sharedMesh = halves[side].mesh;
                MeshRenderer copy = half.AddComponent<MeshRenderer>();
                Material[] materials = renderer.sharedMaterials;
                if (halves[side].capped && interior != null)
                {
                    System.Array.Resize(ref materials, halves[side].mesh.subMeshCount);
                    materials[materials.Length - 1] = interior;
                }
                copy.sharedMaterials = materials;
                Darken(copy, Charred);

                // Peel the half outward, hinged at its foot on the mount.
                Vector3 away = side == 0 ? -cutNormal : cutNormal;
                Vector3 hinge = mountBase.position + away * size * 0.35f;
                Vector3 axis = Vector3.Cross(outward, away).normalized;
                half.transform.RotateAround(hinge, axis, Random.Range(18f, 38f));
                half.transform.position += away * size * Random.Range(0.05f, 0.15f);
            }
            renderer.enabled = false;
        }
    }

    // Barrels sag on their wrecked mounting.
    private void Droop(List<MeshRenderer> barrels)
    {
        if (turret == null || turret.turretBarrel == null || barrels.Count == 0) return;
        turret.turretBarrel.localRotation *= Quaternion.Euler(Random.Range(12f, 24f), Random.Range(-8f, 8f), Random.Range(-6f, 6f));
        Char(barrels);
    }

    // A scorched pit where the turret stood.
    // A blackened pit where the turret stood, cut from the hull's own surface (so it follows the deck and
    // can't hang off the edge), sized to the turret's base - not its barrels.
    private void Crater(Vector3 point, Vector3 outward, List<MeshRenderer> housing)
    {
        HullScarring scarring = GetComponentInParent<HullScarring>();
        if (scarring == null) return;
        // The footprint on the deck: the housing's half-width across the two axes that aren't "outward".
        int up = Mathf.Abs(outward.x) > Mathf.Abs(outward.y) ? (Mathf.Abs(outward.x) > Mathf.Abs(outward.z) ? 0 : 2) : (Mathf.Abs(outward.y) > Mathf.Abs(outward.z) ? 1 : 2);
        float radius = 0f;
        foreach (MeshRenderer renderer in housing)
        {
            Vector3 extents = renderer.bounds.extents;
            for (int axis = 0; axis < 3; axis++) if (axis != up) radius = Mathf.Max(radius, extents[axis]);
        }
        if (radius <= 0f) return;
        scarring.Crater(point, outward, radius * 0.9f);
    }

    // The mount's fire, moved onto the wreck and pointed out of the hull.
    private void Burn(Vector3 point, Vector3 outward, float scale)
    {
        if (ownFire == null) return;
        ownFire.transform.position = point;
        ownFire.transform.rotation = Quaternion.FromToRotation(Vector3.up, outward);
        ownFire.transform.localScale *= scale;
        ownFire.Play(true);
    }

    private void Sparks(Vector3 point, Vector3 outward, float size, DestructionProfile profile)
    {
        if (profile == null || profile.breachVent == null) return;
        GameObject vent = DestructionFx.Attach(profile.breachVent, transform, point, size * 0.25f);
        if (vent != null) vent.transform.rotation = Quaternion.FromToRotation(Vector3.up, outward);
    }

    private void Char(List<MeshRenderer> renderers)
    {
        foreach (MeshRenderer renderer in renderers) Darken(renderer, Charred);
    }

    private void Darken(Renderer renderer, float brightness)
    {
        if (renderer == null) return;
        hull?.ExcludeFromScorch(renderer); // The hull's own scorching mustn't brighten it back up.
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null) continue;
            renderer.GetPropertyBlock(block, i);
            int property = material.HasProperty(BaseColorId) ? BaseColorId : material.HasProperty(ColorId) ? ColorId : -1;
            if (property >= 0)
            {
                Color original = material.GetColor(property);
                block.SetColor(property, new Color(original.r * brightness, original.g * brightness * 0.95f, original.b * brightness * 0.9f, original.a));
            }
            if (material.HasProperty(EmissionId)) block.SetColor(EmissionId, Color.black);
            renderer.SetPropertyBlock(block, i);
        }
    }

    private static Vector3 Inverse(Vector3 v) => new Vector3(1f / v.x, 1f / v.y, 1f / v.z);
}
