using UnityEngine;

namespace Ironfront.Match
{
    /// <summary>
    /// Instantiates a prefab for a faction so that the owner is set before Awake/OnEnable run. Instantiating
    /// normally and assigning the faction afterwards lets OnEnable handlers (registries, HUD bindings) briefly
    /// see the prefab's default owner.
    /// </summary>
    public static class FactionSpawner
    {
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, int factionId)
        {
            var holder = new GameObject("FactionSpawnHolder");
            holder.SetActive(false);
            var go = Object.Instantiate(prefab, position, rotation, holder.transform);
            var member = go.GetComponent<FactionMember>();
            if (member != null) member.FactionId = factionId;
            go.transform.SetParent(null, true); // becomes active here: Awake/OnEnable see the right faction
            Object.Destroy(holder);
            return go;
        }
    }
}
