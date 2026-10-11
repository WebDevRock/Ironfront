using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Movement feedback for a vehicle, derived from how its transform actually moves (works for NavMesh units
    /// and scripted movement alike): hull pitch on acceleration and roll in turns, scrolling track treads, dust
    /// from the tracks/wheels and exhaust smoke. Visual only; the gameplay transform is never modified.
    /// </summary>
    public class VehicleMotionFx : MonoBehaviour
    {
        private static readonly List<VehicleMotionFx> s_all = new List<VehicleMotionFx>();
        /// <summary>Active vehicles (engine audio aggregation).</summary>
        public static IReadOnlyList<VehicleMotionFx> All => s_all;

        [SerializeField] private Transform hull;
        [SerializeField] private Renderer tracks;
        [Tooltip("Tread texture repeats per metre travelled.")]
        [SerializeField] private float treadScroll = 1f;
        [SerializeField] private Transform[] dustPoints = new Transform[0];
        [SerializeField] private Transform[] exhaustPoints = new Transform[0];
        [SerializeField] private float maxPitch = 3f;
        [SerializeField] private float maxRoll = 2.5f;
        [SerializeField] private float dustSize = 1f;
        [Tooltip("Optional spinning parts about their local x (e.g. a harvester's wheels).")]
        [SerializeField] private Transform[] wheels = new Transform[0];
        [SerializeField] private float wheelRadius = 0.3f;

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        // Treads: one shared "moving" copy of each track material is scrolled once per frame, and a vehicle swaps
        // its tracks to it while moving. (The first version scrolled a per-renderer property block every frame;
        // the M006 benchmark measured +4.4 ms render-thread time at 200 moving tanks, because every update
        // re-uploads renderer data and drops the renderer out of SRP batching.)
        private static readonly System.Collections.Generic.Dictionary<Material, Material> s_moving =
            new System.Collections.Generic.Dictionary<Material, Material>();
        private static int s_scrolledFrame = -1;
        private Material _staticTracks;
        private bool _treadsMoving;
        private Vector3 _lastPos;
        private float _lastYaw;
        private float _speed, _accel, _yawRate;
        private float _dustTimer, _exhaustTimer;
        private int _dustIndex;
        private Quaternion _hullRest = Quaternion.identity;
        private Renderer _visibility;

        /// <summary>Smoothed ground speed in m/s.</summary>
        public float Speed => _speed;
        public bool IsVisible => _visibility == null || _visibility.isVisible;

        private void Awake()
        {
            if (hull != null) _hullRest = hull.localRotation;
            if (tracks != null) _staticTracks = tracks.sharedMaterial;
            _visibility = tracks != null ? tracks : GetComponentInChildren<Renderer>();
        }

        private void OnEnable()
        {
            _lastPos = transform.position;
            _lastYaw = transform.eulerAngles.y;
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        private void OnDisable() => s_all.Remove(this);

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 pos = transform.position;
            Vector3 delta = pos - _lastPos;
            delta.y = 0f;
            _lastPos = pos;
            float signed = Vector3.Dot(delta, transform.forward) / dt;
            float yaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
            _lastYaw = yaw;

            float prev = _speed;
            _speed = Mathf.Lerp(_speed, Mathf.Abs(signed), 1f - Mathf.Exp(-10f * dt));
            _accel = Mathf.Lerp(_accel, (_speed - prev) / dt, 1f - Mathf.Exp(-6f * dt));
            _yawRate = Mathf.Lerp(_yawRate, yawRate, 1f - Mathf.Exp(-8f * dt));

            if (hull != null && VisualFeatures.HullTilt)
            {
                float pitch = Mathf.Clamp(-_accel * 0.6f, -maxPitch, maxPitch);
                float roll = Mathf.Clamp(_yawRate * _speed * -0.004f, -maxRoll, maxRoll);
                hull.localRotation = _hullRest * Quaternion.Euler(pitch, 0f, roll);
            }

            if (tracks != null && _staticTracks != null && VisualFeatures.TreadScroll)
            {
                bool moving = _speed > 0.4f || Mathf.Abs(_yawRate) > 20f;
                if (moving != _treadsMoving)
                {
                    _treadsMoving = moving;
                    tracks.sharedMaterial = moving ? MovingTracks(_staticTracks) : _staticTracks;
                }
                if (moving) ScrollShared(dt * treadScroll * 5f);
            }

            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null) wheels[i].Rotate(signed * dt / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg, 0f, 0f, Space.Self);

            if (!IsVisible) return;
            if (_speed > 1f && dustPoints.Length > 0)
            {
                // About 3 puffs per second at full speed, alternating tracks (was 17/s: the M006 benchmark showed
                // movement dust dominating particle cost in 200-unit battles).
                _dustTimer -= dt * Mathf.Min(_speed, 8f) * 0.4f;
                if (_dustTimer <= 0f)
                {
                    _dustTimer = 1f;
                    var p = dustPoints[_dustIndex++ % dustPoints.Length];
                    if (p != null) FxLibrary.Dust(p.position, -transform.forward * 0.6f + Vector3.up * 0.4f, dustSize * 1.3f, 0.32f);
                }
            }
            if (exhaustPoints.Length > 0 && (_accel > 0.5f || _speed > 0.5f))
            {
                _exhaustTimer -= dt * (Mathf.Max(0f, _accel) * 0.6f + _speed * 0.08f);
                if (_exhaustTimer <= 0f)
                {
                    _exhaustTimer = 1f;
                    foreach (var p in exhaustPoints)
                        if (p != null) FxLibrary.Smoke(p.position, Vector3.up * 0.6f - transform.forward * 0.3f, 0.35f, 0.25f, 0.35f, 1.4f);
                }
            }
        }

        private static Material MovingTracks(Material source)
        {
            if (!s_moving.TryGetValue(source, out var m) || m == null)
            {
                m = new Material(source) { name = source.name + " (moving)" };
                s_moving[source] = m;
            }
            return m;
        }

        /// <summary>Advances every shared moving-tread material once per frame (about 5 m/s of apparent travel).</summary>
        private static void ScrollShared(float delta)
        {
            if (s_scrolledFrame == Time.frameCount) return;
            s_scrolledFrame = Time.frameCount;
            foreach (var m in s_moving.Values)
            {
                if (m == null) continue;
                var st = m.GetVector(BaseMapSt);
                st.w = Mathf.Repeat(st.w - delta, 1f);
                m.SetVector(BaseMapSt, st);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_all.Clear();
            s_moving.Clear();
            s_scrolledFrame = -1;
        }
    }
}
