using Ironfront.Economy;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>Occasional sparkles over an ore field so resources read at a glance. Stops once depleted.</summary>
    [RequireComponent(typeof(OreDeposit))]
    public class OreDepositFx : MonoBehaviour
    {
        [SerializeField] private float radius = 1.6f;
        [SerializeField] private float interval = 0.35f;

        private OreDeposit _deposit;
        private Renderer _visibility;
        private float _timer;

        private void Awake()
        {
            _deposit = GetComponent<OreDeposit>();
            var crystals = transform.Find("Crystals");
            _visibility = crystals != null ? crystals.GetComponentInChildren<Renderer>() : null;
            _timer = Random.value * interval;
        }

        private void Update()
        {
            if (_deposit.IsDepleted) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = interval * Random.Range(0.6f, 1.4f);
            if (_visibility != null && !_visibility.isVisible) return;
            float scale = _deposit.InitialAmount > 0 ? Mathf.Lerp(0.5f, 1f, (float)_deposit.Remaining / _deposit.InitialAmount) : 1f;
            var d = Random.insideUnitCircle * radius * scale;
            FxLibrary.Glint(transform.position + new Vector3(d.x, Random.Range(0.4f, 1.8f) * scale, d.y), Random.Range(0.3f, 0.6f));
        }
    }
}
