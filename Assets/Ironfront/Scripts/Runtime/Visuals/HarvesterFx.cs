using Ironfront.Core.Harvesting;
using Ironfront.Economy;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Harvesting presentation driven by the harvester's state: the cutter drum spins and throws up ore dust and
    /// sparkle while mining, the warning beacon flashes while working, and ore glints pour off the hopper while
    /// unloading at a refinery.
    /// </summary>
    [RequireComponent(typeof(Harvester))]
    public class HarvesterFx : MonoBehaviour
    {
        [SerializeField] private Transform drum;
        [SerializeField] private Transform beacon;
        [SerializeField] private Transform scoopPoint;
        [SerializeField] private Transform cargo;
        [SerializeField] private float drumSpeed = 540f;

        private Harvester _harvester;
        private float _timer;
        private float _drumSpeed;
        private Renderer _visibility;

        private void Awake()
        {
            _harvester = GetComponent<Harvester>();
            _visibility = GetComponentInChildren<MeshRenderer>();
        }

        private void Update()
        {
            if (_harvester == null || _harvester.Machine == null) return;
            var state = _harvester.State;
            bool mining = state == HarvesterState.Harvesting;
            bool unloading = state == HarvesterState.Unloading;
            float dt = Time.deltaTime;

            _drumSpeed = Mathf.MoveTowards(_drumSpeed, mining ? drumSpeed : 0f, drumSpeed * dt * 1.5f);
            if (drum != null && _drumSpeed > 0f) drum.Rotate(_drumSpeed * dt, 0f, 0f, Space.Self);

            if (beacon != null)
            {
                bool on = (mining || unloading) && Mathf.Repeat(Time.time, 0.8f) < 0.4f;
                if (beacon.gameObject.activeSelf != on) beacon.gameObject.SetActive(on);
            }

            if (!(mining || unloading) || (_visibility != null && !_visibility.isVisible)) return;
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = 0.12f;
            if (mining && scoopPoint != null)
            {
                FxLibrary.Dust(scoopPoint.position, transform.forward * 0.8f + Vector3.up * 0.8f + Random.insideUnitSphere * 0.4f, 0.8f, 0.4f);
                if (Random.value < 0.6f) FxLibrary.Glint(scoopPoint.position + Random.insideUnitSphere * 0.5f + Vector3.up * 0.3f, 0.45f);
            }
            if (unloading && cargo != null && cargo.gameObject.activeInHierarchy)
                FxLibrary.Glint(cargo.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.5f, 0.5f);
        }
    }
}
