using Ironfront.Audio;
using Ironfront.Core.Construction;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Presentation of a Mobile Command Vehicle (Milestone 010). While deploying, the outriggers swing down, the command
    /// module rises and the mast extends in step with the progress, the beacon flashes and hydraulics hiss; a cancelled
    /// or failed deployment folds everything back. When the vehicle becomes a Forward Command Centre a dust ring and the
    /// "base established" chime mark it. Reads <see cref="MobileCommandVehicle"/>; never changes it.
    /// </summary>
    public class McvFx : MonoBehaviour
    {
        [SerializeField] private MobileCommandVehicle vehicle;
        [SerializeField] private Transform module;
        [SerializeField] private Transform mast;
        [SerializeField] private Transform beacon;
        [SerializeField] private Transform[] outriggers = new Transform[0];

        private Vector3 _moduleRest, _mastScale = Vector3.one;
        private Quaternion[] _outriggerRest;
        private float _shown;
        private float _dustTimer;
        private DeployState _last = DeployState.Mobile;

        private void Awake()
        {
            if (vehicle == null) vehicle = GetComponent<MobileCommandVehicle>();
            if (module != null) _moduleRest = module.localPosition;
            if (mast != null) _mastScale = mast.localScale;
            _outriggerRest = new Quaternion[outriggers.Length];
            for (int i = 0; i < outriggers.Length; i++) if (outriggers[i] != null) _outriggerRest[i] = outriggers[i].localRotation;
        }

        private void Update()
        {
            if (vehicle == null) return;
            var state = vehicle.State;
            if (state != _last)
            {
                if (state == DeployState.Deploying && Vision.VisionSystem.PlayerCanSee(transform.position))
                    GameAudio.Play(SoundId.DeployStart, transform.position);
                _last = state;
            }
            float target = state == DeployState.Deploying ? vehicle.Progress01 : 0f;
            // Follow the progress while deploying; fold back quickly otherwise.
            _shown = state == DeployState.Deploying ? target : Mathf.MoveTowards(_shown, 0f, Time.deltaTime * 1.5f);
            Apply(_shown);

            if (state == DeployState.Deploying && Vision.VisionSystem.PlayerCanSee(transform.position))
            {
                _dustTimer -= Time.deltaTime;
                if (_dustTimer <= 0f)
                {
                    _dustTimer = Random.Range(0.25f, 0.45f);
                    var p = transform.TransformPoint(new Vector3(Random.Range(-1.6f, 1.6f), 0.1f, Random.Range(-2.6f, 2.6f)));
                    FxLibrary.Dust(p, Vector3.up * 0.8f + Random.insideUnitSphere * 0.4f, 1.2f);
                }
                if (beacon != null) beacon.gameObject.SetActive(Mathf.Repeat(Time.time * 3f, 1f) < 0.6f);
            }
            else if (beacon != null && !beacon.gameObject.activeSelf) beacon.gameObject.SetActive(true);
        }

        private void Apply(float t)
        {
            float legs = Mathf.Clamp01(t / 0.35f);
            float rise = Mathf.Clamp01((t - 0.3f) / 0.5f);
            float mastT = Mathf.Clamp01((t - 0.6f) / 0.4f);
            for (int i = 0; i < outriggers.Length; i++)
            {
                if (outriggers[i] == null) continue;
                float side = outriggers[i].localPosition.x < 0f ? 1f : -1f;
                outriggers[i].localRotation = _outriggerRest[i] * Quaternion.Euler(0f, 0f, side * -55f * legs);
            }
            if (module != null) module.localPosition = _moduleRest + Vector3.up * 0.8f * rise;
            if (mast != null) mast.localScale = new Vector3(_mastScale.x, _mastScale.y * (1f + 1.2f * mastT), _mastScale.z);
        }

        private void OnEnable() => MobileCommandVehicle.AnyDeployed += OnDeployed;
        private void OnDisable() => MobileCommandVehicle.AnyDeployed -= OnDeployed;

        private void OnDeployed(MobileCommandVehicle v, Buildings.Building centre)
        {
            if (v != vehicle || centre == null) return;
            if (!Vision.VisionSystem.PlayerCanSee(centre.transform.position)) return;
            FxLibrary.DustRing(centre.transform.position, centre.Definition != null ? centre.Definition.FootprintSize : new Vector2(9f, 9f));
            if (centre.FactionId == Match.MatchController.Current?.PlayerFactionId)
                GameAudio.Play(SoundId.BaseEstablished, centre.transform.position);
        }
    }
}
