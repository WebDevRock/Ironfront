using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ironfront.Combat
{
    /// <summary>
    /// Pool for short-lived combat objects (shells, muzzle flashes, impacts, explosions, order markers).
    /// Objects are deactivated and reused instead of being created and destroyed every shot, which removed
    /// the per-shot GameObject churn and most combat GC allocation (see Milestone 005 benchmark).
    /// Pools belong to the active scene and are cleared when it unloads.
    /// </summary>
    public static class FxPool
    {
        private static readonly Dictionary<(PrimitiveType, Material), Stack<GameObject>> s_free =
            new Dictionary<(PrimitiveType, Material), Stack<GameObject>>();
        private static Transform s_root;
        private static bool s_hooked;

        /// <summary>Objects created since load (diagnostics).</summary>
        public static int Created { get; private set; }
        /// <summary>Times an existing object was reused (diagnostics).</summary>
        public static int Reused { get; private set; }

        public static GameObject Get(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            Hook();
            var key = (type, material);
            GameObject go = null;
            if (s_free.TryGetValue(key, out var stack))
            {
                while (stack.Count > 0 && go == null) go = stack.Pop(); // skip any destroyed with their scene
            }

            if (go != null)
            {
                Reused++;
            }
            else
            {
                go = CombatFx.Primitive(type, name, position, scale, material);
                var tag = go.AddComponent<PooledFx>();
                tag.Type = type;
                tag.Material = material;
                Created++;
            }

            go.transform.SetParent(Root, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = scale;
            go.SetActive(true);
            return go;
        }

        /// <summary>Returns an object obtained from <see cref="Get"/>. Non-pooled objects are destroyed.</summary>
        public static void Release(GameObject go)
        {
            if (go == null) return;
            var tag = go.GetComponent<PooledFx>();
            if (tag == null)
            {
                Object.Destroy(go);
                return;
            }
            go.SetActive(false);
            var key = (tag.Type, tag.Material);
            if (!s_free.TryGetValue(key, out var stack)) s_free[key] = stack = new Stack<GameObject>();
            stack.Push(go);
        }

        private static Transform Root
        {
            get
            {
                if (s_root == null) s_root = new GameObject("FxPool").transform;
                return s_root;
            }
        }

        private static void Hook()
        {
            if (s_hooked) return;
            s_hooked = true;
            SceneManager.sceneUnloaded += _ => Clear();
        }

        private static void Clear()
        {
            s_free.Clear();
            s_root = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clear();
            s_hooked = false;
            Created = 0;
            Reused = 0;
        }
    }
}
