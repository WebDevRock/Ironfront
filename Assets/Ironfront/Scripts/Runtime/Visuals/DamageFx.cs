using Ironfront.Combat;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Damage states: below half health the object trails smoke; below a quarter it also burns and sparks.
    /// Reads <see cref="Health"/> only through its events and current value. Idle (no Update work) while healthy.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class DamageFx : MonoBehaviour
    {
        [SerializeField] private float smokeSize = 0.8f;
        [Tooltip("Emission points; the aim point is used when empty.")]
        [SerializeField] private Vector3[] localPoints = new Vector3[0];
        [SerializeField] private float smokeRate = 3f;

        private Health _health;
        private float _timer;
        private Renderer _visibility;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _visibility = GetComponentInChildren<MeshRenderer>();
            // Subscribed for the component's whole life; damage below half health switches Update on.
            if (_health != null) _health.Damaged += OnDamaged;
            enabled = false;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
        }

        private void OnDamaged(float amount)
        {
            if (_health != null && _health.Max > 0f && _health.Current / _health.Max < 0.5f) enabled = true;
        }

        private void Update()
        {
            if (_health == null || !_health.IsAlive) return;
            float ratio = _health.Max > 0f ? _health.Current / _health.Max : 1f;
            if (ratio >= 0.5f)
            {
                enabled = false; // repaired / healthy again
                return;
            }
            if (_visibility != null && !_visibility.isVisible) return;

            bool critical = ratio < 0.25f;
            _timer -= Time.deltaTime * smokeRate * (critical ? 1.6f : 1f);
            if (_timer > 0f) return;
            _timer = 1f;

            Vector3 p = Point();
            float grey = critical ? 0.1f : 0.22f;
            FxLibrary.Smoke(p, new Vector3(0f, 1.1f, 0f) - transform.forward * 0.2f, smokeSize, grey, 0.75f, 2.2f);
            if (critical) FxLibrary.Flame(p, smokeSize * 0.6f);
        }

        private Vector3 Point()
        {
            if (localPoints.Length > 0) return transform.TransformPoint(localPoints[Random.Range(0, localPoints.Length)]);
            return transform.position + Vector3.up * _health.AimHeight;
        }
    }
}
