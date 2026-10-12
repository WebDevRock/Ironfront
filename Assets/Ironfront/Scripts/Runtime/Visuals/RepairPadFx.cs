using Ironfront.Audio;
using Ironfront.Buildings;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Presentation of a Repair Pad (Milestone 009); reads <see cref="RepairPad"/> state, never changes it.
    /// Status lamp and bay ring: green available, amber occupied, red unpowered, pulsing blue while assembling (and the
    /// gantry rises as assembly progresses). While a vehicle is repaired the trolley follows it, the welding arms sweep,
    /// sparks fly from the arm tips and weld/ratchet sounds play; a chime marks each finished repair.
    /// </summary>
    public class RepairPadFx : MonoBehaviour
    {
        [SerializeField] private RepairPad pad;
        [SerializeField] private Transform gantry;
        [SerializeField] private Transform trolley;
        [SerializeField] private Transform[] arms = new Transform[0];
        [SerializeField] private Transform[] sparkPoints = new Transform[0];
        [SerializeField] private Renderer lamp;
        [SerializeField] private Renderer bayRing;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Available = new Color(0.25f, 1f, 0.35f);
        private static readonly Color Occupied = new Color(1f, 0.65f, 0.12f);
        private static readonly Color Unpowered = new Color(1f, 0.15f, 0.1f);
        private static readonly Color Assembling = new Color(0.3f, 0.6f, 1f);

        private MaterialPropertyBlock _block;
        private Vector3 _gantryScale = Vector3.one;
        private float _sparkTimer, _soundTimer;
        private float _trolleyX;

        private void Awake()
        {
            if (pad == null) pad = GetComponent<RepairPad>();
            _block = new MaterialPropertyBlock();
            if (gantry != null) _gantryScale = gantry.localScale;
        }

        private void OnEnable() => RepairPad.AnyRepairCompleted += OnCompleted;
        private void OnDisable() => RepairPad.AnyRepairCompleted -= OnCompleted;

        private void OnCompleted(RepairPad p, Units.RepairClient client)
        {
            if (p == pad && Vision.VisionSystem.PlayerCanSee(pad.BayPoint)) GameAudio.Play(SoundId.RepairComplete, pad.BayPoint);
        }

        private void Update()
        {
            if (pad == null) return;
            var state = pad.State;
            bool busy = pad.Queue.Active != Core.Repair.RepairPadQueue.None;
            Color c = !pad.IsAssembled ? Assembling * (0.55f + 0.45f * Mathf.Sin(Time.time * 4f))
                : state == RepairPad.Status.NoPower ? Unpowered
                : state == RepairPad.Status.NoCredits ? Occupied * (0.5f + 0.5f * Mathf.Sin(Time.time * 6f))
                : busy ? Occupied : Available;
            Tint(lamp, c, 3f);
            // The pad used by the player's selected vehicles pulses brightly.
            Tint(bayRing, c, pad.Highlighted ? 2.4f + 1.6f * Mathf.Sin(Time.unscaledTime * 5f) : 1.6f);

            // Assembly: the gantry rises from 20 % to full height.
            if (gantry != null)
            {
                float k = Mathf.Lerp(0.2f, 1f, pad.Assembly01);
                gantry.localScale = new Vector3(_gantryScale.x, _gantryScale.y * k, _gantryScale.z);
            }

            bool repairing = pad.IsRepairingNow;
            var client = pad.ActiveClient;
            if (trolley != null && client != null)
            {
                float target = Mathf.Clamp(transform.InverseTransformPoint(client.transform.position).x, -2.4f, 2.4f);
                _trolleyX = Mathf.MoveTowards(_trolleyX, target, Time.deltaTime * 2f);
                trolley.localPosition = new Vector3(_trolleyX, trolley.localPosition.y, trolley.localPosition.z);
            }
            for (int i = 0; i < arms.Length; i++)
            {
                if (arms[i] == null) continue;
                float angle = repairing ? Mathf.Sin(Time.time * (2.2f + i * 0.7f) + i) * 28f : 0f;
                arms[i].localRotation = Quaternion.Slerp(arms[i].localRotation, Quaternion.Euler(angle, 0f, angle * 0.4f), Time.deltaTime * 6f);
            }

            if (!repairing || !Vision.VisionSystem.PlayerCanSee(pad.BayPoint)) return;
            _sparkTimer -= Time.deltaTime;
            if (_sparkTimer <= 0f)
            {
                _sparkTimer = Random.Range(0.08f, 0.2f);
                var tip = sparkPoints.Length > 0 ? sparkPoints[Random.Range(0, sparkPoints.Length)] : null;
                if (tip != null) FxLibrary.RepairSparks(tip.position);
            }
            _soundTimer -= Time.deltaTime;
            if (_soundTimer <= 0f)
            {
                _soundTimer = Random.Range(1.1f, 1.6f);
                GameAudio.Play(SoundId.RepairWork, pad.BayPoint);
            }
        }

        private void Tint(Renderer r, Color c, float intensity)
        {
            if (r == null) return;
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c * 0.6f);
            _block.SetColor(EmissionId, c * intensity);
            r.SetPropertyBlock(_block);
        }
    }
}
