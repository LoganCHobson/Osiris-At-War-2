using System.Collections.Generic;
using UnityEngine;

// Every asset a saved battle replay may refer to (models, materials, textures, sounds, effect prefabs), each with a
// key that stays the same between builds of the game (its asset id). Made before every build (and from
// Osiris > Dev > Build Replay Asset List); loaded only when a replay is saved or opened.
public class ReplayAssets : ScriptableObject
{
    public string[] keys = new string[0];
    public Object[] objects = new Object[0];

    private static ReplayAssets loaded;
    private static Dictionary<Object, string> keyOf;
    private static Dictionary<string, Object> objectOf;
    private static Dictionary<string, GameObject> prefabNamed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { loaded = null; keyOf = null; objectOf = null; prefabNamed = null; }

    private static bool Ready()
    {
        if (keyOf != null) return true;
        loaded = Resources.Load<ReplayAssets>("ReplayAssets");
        keyOf = new Dictionary<Object, string>(ReferenceComparer.Instance);
        objectOf = new Dictionary<string, Object>();
        prefabNamed = new Dictionary<string, GameObject>();
        if (loaded == null) { Debug.LogWarning("ReplayAssets: no asset list in Resources - saved replays will lack models."); return false; }
        for (int i = 0; i < loaded.keys.Length && i < loaded.objects.Length; i++)
        {
            Object o = loaded.objects[i];
            if (o == null) continue;
            keyOf[o] = loaded.keys[i];
            objectOf[loaded.keys[i]] = o;
            if (o is GameObject go && !prefabNamed.ContainsKey(go.name)) prefabNamed[go.name] = go;
        }
        return true;
    }

    // Loads the list (main thread) so lookups can then be made from workers.
    public static void Prepare() => Ready();

    public static string KeyOf(Object o)
    {
        if ((object)o == null) return null;
        Ready();
        return keyOf.TryGetValue(o, out string key) ? key : null;
    }

    public static T Find<T>(string key) where T : Object
    {
        if (string.IsNullOrEmpty(key)) return null;
        Ready();
        return objectOf.TryGetValue(key, out Object o) ? o as T : null;
    }

    public static GameObject PrefabNamed(string name)
    {
        Ready();
        return prefabNamed.TryGetValue(name, out GameObject go) ? go : null;
    }
}
