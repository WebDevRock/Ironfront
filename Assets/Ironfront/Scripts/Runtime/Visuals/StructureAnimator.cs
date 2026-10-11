using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Ambient life for structures: rotating radar dishes, blinking beacons, chimney smoke and cooling-tower steam.
    /// Purely decorative; skips particle work while the structure is off screen.
    /// </summary>
    public class StructureAnimator : MonoBehaviour
    {
        [SerializeField] private Transform[] spinners = new Transform[0];
        [SerializeField] private float spinSpeed = 60f;
        [SerializeField] private Transform[] blinkers = new Transform[0];
        [SerializeField] private float blinkPeriod = 1.4f;
        [SerializeField] private Transform[] smokePoints = new Transform[0];
        [SerializeField] private Transform[] steamPoints = new Transform[0];
        [SerializeField] private float emitInterval = 0.45f;

        private float _timer;
        private float _phase;
        private Renderer _visibility;

        private void Awake()
        {
            _visibility = GetComponentInChildren<MeshRenderer>();
            _phase = Random.value * blinkPeriod;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < spinners.Length; i++)
                if (spinners[i] != null) spinners[i].Rotate(0f, spinSpeed * dt, 0f, Space.Self);

            if (blinkers.Length > 0)
            {
                bool on = Mathf.Repeat(Time.time + _phase, blinkPeriod) < blinkPeriod * 0.35f;
                for (int i = 0; i < blinkers.Length; i++)
                    if (blinkers[i] != null && blinkers[i].gameObject.activeSelf != on) blinkers[i].gameObject.SetActive(on);
            }

            if (smokePoints.Length + steamPoints.Length == 0) return;
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = emitInterval * Random.Range(0.8f, 1.2f);
            if (_visibility != null && !_visibility.isVisible) return;
            foreach (var p in smokePoints)
                if (p != null) FxLibrary.Smoke(p.position, new Vector3(0.3f, 1.0f, 0.1f), 0.6f, 0.38f, 0.45f, 3f);
            foreach (var p in steamPoints)
                if (p != null) FxLibrary.Steam(p.position, 1.1f);
        }
    }
}
