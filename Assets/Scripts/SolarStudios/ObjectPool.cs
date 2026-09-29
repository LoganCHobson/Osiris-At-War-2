
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SolarStudios //Logans Library
{
    public class ObjectPool : MonoBehaviour
    {
        private static readonly Dictionary<GameObject, ObjectPool> poolsByPrefab = new Dictionary<GameObject, ObjectPool>();

        public GameObject prefab;
        public int poolSize = 0;
        public bool canGrow = false;
        public List<GameObject> objectPool = new List<GameObject>();
        [Header("Events")]
        public UnityEvent onSpawn;
        public UnityEvent onRecycle;
        public UnityEvent onRecycleAll;
        public UnityEvent onInitalize;

        private readonly Stack<GameObject> available = new Stack<GameObject>();
        private bool warnedFull;

        public static ObjectPool GetPoolFor(GameObject forPrefab)
        {
            if (forPrefab == null) return null;
            return poolsByPrefab.TryGetValue(forPrefab, out ObjectPool pool) && pool != null ? pool : null;
        }

        void Awake()
        {
            if (prefab != null)
            {
                poolsByPrefab[prefab] = this;
            }
        }

        void OnDestroy()
        {
            if (prefab != null && poolsByPrefab.TryGetValue(prefab, out ObjectPool pool) && pool == this)
            {
                poolsByPrefab.Remove(prefab);
            }
        }

        void Start()
        {
            InitializePool();
        }

        public void InitializePool()
        {
            onInitalize.Invoke();
            for (int i = 0; i < poolSize; i++)
            {
                available.Push(CreateInstance());
            }
        }

        private GameObject CreateInstance()
        {
            GameObject obj = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            obj.SetActive(false);
            objectPool.Add(obj);
            return obj;
        }

        public GameObject Spawn(Vector3 position, Quaternion rotation = default)
        {
            onSpawn.Invoke();

            GameObject obj = null;
            while (obj == null && available.Count > 0)
            {
                obj = available.Pop();
            }

            if (obj == null)
            {
                if (!canGrow)
                {
                    if (!warnedFull)
                    {
                        warnedFull = true;
                        Debug.LogWarning($"Object pool for '{(prefab != null ? prefab.name : name)}' reached capacity ({poolSize}). Raise Pool Size or enable Can Grow.");
                    }
                    return null;
                }

                obj = CreateInstance();
            }

            obj.transform.position = position;
            obj.transform.rotation = rotation;
            obj.SetActive(true);
            return obj;
        }

        public void Recycle(GameObject obj, float delay = 0f)
        {
            if (obj == null) return;

            if (delay <= 0f)
            {
                Deactivate(obj);
            }
            else
            {
                StartCoroutine(DeactivateObjectDelayed(obj, delay));
            }

            onRecycle.Invoke();
        }

        private void Deactivate(GameObject obj)
        {
            if (obj == null || !obj.activeSelf) return;

            obj.SetActive(false);
            available.Push(obj);
        }

        private IEnumerator DeactivateObjectDelayed(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            Deactivate(obj);
        }

        public void RecycleAll(float delay = 0f)
        {
            foreach (GameObject obj in objectPool)
            {
                if (obj == null || !obj.activeInHierarchy) continue;

                if (delay <= 0f)
                {
                    Deactivate(obj);
                }
                else
                {
                    StartCoroutine(DeactivateObjectDelayed(obj, delay));
                }
            }
            onRecycleAll.Invoke();
        }

        public bool IsEmpty()
        {
            return available.Count == 0;
        }
    }

}
