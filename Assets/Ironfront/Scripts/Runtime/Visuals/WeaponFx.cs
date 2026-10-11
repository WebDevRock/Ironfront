using Ironfront.Audio;
using Ironfront.Combat;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Presentation of a unit's shots: barrel recoil, muzzle flash/smoke and the firing sound. Driven only by
    /// <see cref="CombatUnit.Fired"/>; never touches cooldowns, damage or targeting.
    /// </summary>
    public class WeaponFx : MonoBehaviour
    {
        [SerializeField] private Transform barrel;
        [SerializeField] private Transform muzzle;
        [SerializeField] private float recoilDistance = 0.18f;
        [SerializeField] private float recoilReturn = 0.35f;
        [SerializeField] private float flashScale = 1f;
        [SerializeField] private SoundId fireSound = SoundId.LightCannon;

        private IFiringWeapon _unit;
        private Vector3 _barrelRest;
        private float _recoil; // 1 = fully back, decays to 0

        private void Awake()
        {
            _unit = GetComponent<IFiringWeapon>();
            if (barrel != null) _barrelRest = barrel.localPosition;
        }

        private void OnEnable()
        {
            if (_unit != null) _unit.Fired += OnFired;
        }

        private void OnDisable()
        {
            if (_unit != null) _unit.Fired -= OnFired;
        }

        private void OnFired(Health target)
        {
            var m = muzzle != null ? muzzle : _unit?.Muzzle;
            if (m != null && !Ironfront.Vision.VisionSystem.PlayerCanSee(m.position)) { _recoil = 1f; return; }
            Vector3 pos = m != null ? m.position : transform.position + Vector3.up;
            Vector3 fwd = m != null ? m.forward : transform.forward;
            CombatFx.MuzzleFlash(pos, fwd, flashScale);
            GameAudio.Play(fireSound, pos);
            _recoil = 1f;
        }

        private void LateUpdate()
        {
            if (barrel == null || _recoil <= 0f) return;
            _recoil = Mathf.Max(0f, _recoil - Time.deltaTime / Mathf.Max(0.05f, recoilReturn));
            float e = _recoil * _recoil; // fast kick, eased return
            barrel.localPosition = _barrelRest + Vector3.back * recoilDistance * e;
        }
    }
}
