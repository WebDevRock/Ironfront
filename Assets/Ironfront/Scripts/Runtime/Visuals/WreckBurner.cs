using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>Burns (fire and smoke) for a while, then sinks the wreck into the ground and removes it.</summary>
    public class WreckBurner : MonoBehaviour
    {
        [SerializeField] private float burnSeconds = 7f;
        [SerializeField] private float sinkSeconds = 3f;
        [SerializeField] private float sinkDepth = 1.4f;

        private float _age, _timer;
        private Vector3 _start;

        private void Start() => _start = transform.position;

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age < burnSeconds)
            {
                _timer -= Time.deltaTime * Mathf.Lerp(4f, 1.5f, _age / burnSeconds);
                if (_timer <= 0f)
                {
                    _timer = 1f;
                    var p = transform.position + Vector3.up * 0.6f + Random.insideUnitSphere * 0.4f;
                    FxLibrary.Smoke(p, Vector3.up * 1.2f, 0.9f, 0.12f, 0.8f, 2.8f);
                    if (_age < burnSeconds * 0.6f) FxLibrary.Flame(p, 0.6f);
                }
                return;
            }
            float t = (_age - burnSeconds) / sinkSeconds;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            transform.position = _start + Vector3.down * sinkDepth * t * t;
        }
    }
}
