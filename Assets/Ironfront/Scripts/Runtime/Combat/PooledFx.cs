using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>Marks an object owned by <see cref="FxPool"/> and remembers which pool it returns to.</summary>
    [DisallowMultipleComponent]
    public sealed class PooledFx : MonoBehaviour
    {
        public PrimitiveType Type;
        public Material Material;
    }
}
