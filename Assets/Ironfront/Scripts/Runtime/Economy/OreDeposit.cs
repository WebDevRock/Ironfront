using System;
using System.Collections.Generic;
using Ironfront.Core.Harvesting;
using UnityEngine;
using UnityEngine.AI;

namespace Ironfront.Economy
{
    /// <summary>
    /// A finite Ore field. The visual shrinks as it is mined; when empty it disappears, stops blocking
    /// navigation and raises <see cref="AnyDepleted"/>.
    /// </summary>
    public class OreDeposit : MonoBehaviour, IOreSource
    {
        private static readonly List<OreDeposit> s_All = new List<OreDeposit>();
        public static IReadOnlyList<OreDeposit> All => s_All;

        public static event Action<OreDeposit> AnyDepleted;

        [SerializeField, Min(1)] private int initialAmount = 5000;
        [Tooltip("Distance from the centre within which a harvester can mine.")]
        [SerializeField, Min(0.5f)] private float harvestRadius = 2.5f;
        [SerializeField] private Transform visual;
        [SerializeField, Range(0.05f, 1f)] private float minVisualScale = 0.35f;

        private int _remaining;
        private Vector3 _baseScale = Vector3.one;

        public int InitialAmount => initialAmount;
        public int Remaining => _remaining;
        public bool IsDepleted => _remaining <= 0;
        public float HarvestRadius => harvestRadius;

        /// <summary>Raised with the remaining amount after each extraction.</summary>
        public event Action<int> AmountChanged;

        private void Awake()
        {
            _remaining = initialAmount;
            if (visual != null) _baseScale = visual.localScale;
        }

        private void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        private void OnDisable() => s_All.Remove(this);

        public int Extract(int max)
        {
            if (max <= 0 || IsDepleted) return 0;
            int taken = Math.Min(max, _remaining);
            _remaining -= taken;
            UpdateVisual();
            AmountChanged?.Invoke(_remaining);
            if (IsDepleted) OnDepleted();
            return taken;
        }

        /// <summary>Overrides the remaining amount (tools and tests). Zero depletes the deposit.</summary>
        public void SetRemaining(int amount)
        {
            _remaining = Math.Max(0, amount);
            UpdateVisual();
            AmountChanged?.Invoke(_remaining);
            if (IsDepleted) OnDepleted();
        }

        private void UpdateVisual()
        {
            if (visual == null) return;
            float t = initialAmount > 0 ? (float)_remaining / initialAmount : 0f;
            visual.localScale = _baseScale * Mathf.Lerp(minVisualScale, 1f, t);
        }

        private void OnDepleted()
        {
            if (visual != null) visual.gameObject.SetActive(false);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            var obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle != null) obstacle.enabled = false;

            Debug.Log($"[Ironfront] {name} depleted.", this);
            AnyDepleted?.Invoke(this);
        }

        /// <summary>Nearest deposit that still has Ore, or null.</summary>
        public static OreDeposit FindNearest(Vector3 position)
        {
            OreDeposit best = null;
            float bestSqr = float.MaxValue;
            foreach (var d in s_All)
            {
                if (d.IsDepleted) continue;
                float sqr = (d.transform.position - position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = d; }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            AnyDepleted = null;
        }
    }
}
