using System.Collections.Generic;
using Ironfront.Core.Audio;
using Ironfront.Visuals;
using UnityEngine;

namespace Ironfront.Audio
{
    /// <summary>
    /// The scene's audio player. One-shots play through a small pool of 2D sources; positional sounds are
    /// attenuated and panned from their position relative to the camera view (an RTS camera hears what it looks
    /// at), and a <see cref="VoiceLimiter"/> caps overlapping copies of the same sound. Also runs the ambience
    /// loop, an aggregated engine loop driven by how many vehicles are moving on screen, and occasional distant
    /// battle rumble. Volumes come from a shared <see cref="AudioMix"/> persisted in PlayerPrefs.
    ///
    /// Static <see cref="Play"/> is safe to call from anywhere; it does nothing when no GameAudio is in the scene
    /// (tests, tools).
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        private const int PoolSize = 24;
        private static readonly string[] PrefKeys = { "ironfront.audio.effects", "ironfront.audio.interface", "ironfront.audio.voice", "ironfront.audio.ambience" };
        private const string MasterKey = "ironfront.audio.master";

        [SerializeField] private SoundBank bank;
        [SerializeField] private bool ambience = true;

        private static GameAudio s_current;
        private static AudioMix s_mix;

        private readonly List<AudioSource> _pool = new List<AudioSource>(PoolSize);
        private readonly VoiceLimiter _limiter = new VoiceLimiter();
        private AudioSource _ambience, _engine;
        private int _next;
        private float _engineTimer, _engineTarget, _enginePitch = 1f, _rumbleTimer = 6f;
        private Camera _camera;

        /// <summary>Total one-shots started (diagnostics / tests).</summary>
        public static int Played { get; private set; }
        /// <summary>One-shots refused by the voice limiter (diagnostics / tests).</summary>
        public static int Limited { get; private set; }
        public static GameAudio Current => s_current;
        public SoundBank Bank => bank;

        /// <summary>Shared volume settings (loaded from PlayerPrefs on first use).</summary>
        public static AudioMix Mix
        {
            get
            {
                if (s_mix != null) return s_mix;
                s_mix = new AudioMix();
                s_mix.Master = PlayerPrefs.GetFloat(MasterKey, s_mix.Master);
                for (int i = 0; i < AudioMix.CategoryCount; i++) s_mix.Set(i, PlayerPrefs.GetFloat(PrefKeys[i], s_mix.Get(i)));
                s_mix.Changed += SaveMix;
                return s_mix;
            }
        }

        private static void SaveMix()
        {
            PlayerPrefs.SetFloat(MasterKey, s_mix.Master);
            for (int i = 0; i < AudioMix.CategoryCount; i++) PlayerPrefs.SetFloat(PrefKeys[i], s_mix.Get(i));
        }

        private void Awake()
        {
            s_current = this;
            for (int i = 0; i < PoolSize; i++) _pool.Add(NewSource("Voice" + i, false));
            if (bank != null && ambience)
            {
                if (bank.AmbienceLoop != null) _ambience = Loop("Ambience", bank.AmbienceLoop);
                if (bank.EngineLoop != null) _engine = Loop("Engines", bank.EngineLoop);
            }
            Mix.Changed += ApplyLoopVolumes;
            ApplyLoopVolumes();
        }

        private void OnDestroy()
        {
            if (s_current == this) s_current = null;
            if (s_mix != null) s_mix.Changed -= ApplyLoopVolumes;
        }

        private AudioSource NewSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            return s;
        }

        private AudioSource Loop(string name, AudioClip clip)
        {
            var s = NewSource(name, true);
            s.clip = clip;
            s.volume = 0f;
            s.Play();
            return s;
        }

        private void ApplyLoopVolumes()
        {
            if (_ambience != null) _ambience.volume = 0.5f * AudioMix.SliderToGain(Mix.Effective((int)SoundCategory.Ambience));
        }

        // ---------- One-shots ----------

        /// <summary>Plays <paramref name="id"/> at a world position (positional sounds) — no-op without a GameAudio.</summary>
        public static void Play(SoundId id, Vector3 position)
        {
            if (s_current != null) s_current.PlayInternal(id, position, true);
        }

        /// <summary>Plays <paramref name="id"/> without positioning (interface, acknowledgements, stingers).</summary>
        public static void PlayFlat(SoundId id)
        {
            if (s_current != null) s_current.PlayInternal(id, Vector3.zero, false);
        }

        private void PlayInternal(SoundId id, Vector3 position, bool hasPosition)
        {
            if (bank == null) return;
            var e = bank.Find(id);
            if (e == null || e.clips.Length == 0) return;
            var clip = e.clips[Random.Range(0, e.clips.Length)];
            if (clip == null) return;

            float gain = e.volume * AudioMix.SliderToGain(Mix.Effective((int)e.category));
            float pan = 0f;
            if (hasPosition && e.positional)
            {
                if (!Attenuate(position, out float att, out pan)) return;
                gain *= att;
            }
            if (gain <= 0.001f) return;
            if (!_limiter.TryStart((int)id, Time.unscaledTime, clip.length, e.maxVoices, e.minInterval))
            {
                Limited++;
                return;
            }

            var s = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            s.Stop();
            s.clip = clip;
            s.volume = gain;
            s.pitch = 1f + Random.Range(-e.pitchJitter, e.pitchJitter);
            s.panStereo = pan;
            s.Play();
            Played++;
        }

        /// <summary>
        /// On-screen sounds play at full level (slightly quieter towards the edges); off-screen ones fade out over
        /// about half a screen. Pan follows horizontal screen position.
        /// </summary>
        private bool Attenuate(Vector3 position, out float gain, out float pan)
        {
            gain = 1f;
            pan = 0f;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return true;
            Vector3 vp = _camera.WorldToViewportPoint(position);
            if (vp.z < 0f) return false;
            float dx = Mathf.Max(0f, Mathf.Abs(vp.x - 0.5f) - 0.5f), dy = Mathf.Max(0f, Mathf.Abs(vp.y - 0.5f) - 0.5f);
            float outside = Mathf.Sqrt(dx * dx + dy * dy);
            gain = Mathf.Clamp01(1f - outside * 1.8f);
            float centre = Mathf.Abs(vp.x - 0.5f) + Mathf.Abs(vp.y - 0.5f);
            gain *= Mathf.Lerp(1f, 0.75f, Mathf.Clamp01(centre));
            // Zoomed far out, everything is a little quieter.
            float dist = vp.z;
            gain *= Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(25f, 70f, dist));
            pan = Mathf.Clamp((vp.x - 0.5f) * 1.2f, -0.75f, 0.75f);
            return gain > 0.01f;
        }

        // ---------- Loops ----------

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_engine != null)
            {
                _engineTimer -= dt;
                if (_engineTimer <= 0f)
                {
                    _engineTimer = 0.25f;
                    float load = 0f, speedSum = 0f;
                    int moving = 0;
                    var all = VehicleMotionFx.All;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var v = all[i];
                        if (v == null || !v.IsVisible || v.Speed < 0.5f) continue;
                        moving++;
                        speedSum += v.Speed;
                        load += Mathf.Clamp01(v.Speed / 6f);
                    }
                    _engineTarget = 0.45f * (1f - Mathf.Exp(-load / 4f)) * AudioMix.SliderToGain(Mix.Effective((int)SoundCategory.Effects));
                    _enginePitch = moving > 0 ? Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(speedSum / moving / 8f)) : 0.85f;
                }
                _engine.volume = Mathf.MoveTowards(_engine.volume, _engineTarget, dt * 0.8f);
                _engine.pitch = Mathf.MoveTowards(_engine.pitch, _enginePitch, dt * 0.5f);
            }

            if (ambience && bank != null && bank.DistantBattle.Length > 0)
            {
                _rumbleTimer -= dt;
                if (_rumbleTimer <= 0f)
                {
                    _rumbleTimer = Random.Range(9f, 22f);
                    var clip = bank.DistantBattle[Random.Range(0, bank.DistantBattle.Length)];
                    var s = _pool[_next];
                    _next = (_next + 1) % _pool.Count;
                    s.Stop();
                    s.clip = clip;
                    s.volume = Random.Range(0.12f, 0.25f) * AudioMix.SliderToGain(Mix.Effective((int)SoundCategory.Ambience));
                    s.pitch = Random.Range(0.85f, 1.05f);
                    s.panStereo = Random.Range(-0.8f, 0.8f);
                    s.Play();
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
            Played = 0;
            Limited = 0;
        }
    }
}
