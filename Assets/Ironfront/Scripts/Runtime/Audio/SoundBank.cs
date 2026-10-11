using System;
using UnityEngine;

namespace Ironfront.Audio
{
    /// <summary>Clips and playback rules for every <see cref="SoundId"/>, plus the ambient loops.</summary>
    [CreateAssetMenu(menuName = "Ironfront/Sound Bank", fileName = "SoundBank")]
    public class SoundBank : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public SoundId id;
            public AudioClip[] clips = new AudioClip[0];
            public SoundCategory category = SoundCategory.Effects;
            [Range(0f, 1.5f)] public float volume = 1f;
            [Range(0f, 0.3f)] public float pitchJitter = 0.05f;
            [Tooltip("Maximum simultaneous voices of this sound.")]
            public int maxVoices = 4;
            [Tooltip("Minimum seconds between two starts.")]
            public float minInterval = 0.04f;
            [Tooltip("Attenuated and panned by on-screen position; otherwise played flat (UI, voice).")]
            public bool positional = true;
        }

        [SerializeField] private Entry[] entries = new Entry[0];
        [SerializeField] private AudioClip ambienceLoop;
        [SerializeField] private AudioClip engineLoop;
        [SerializeField] private AudioClip[] distantBattle = new AudioClip[0];

        public Entry[] Entries => entries;
        public AudioClip AmbienceLoop => ambienceLoop;
        public AudioClip EngineLoop => engineLoop;
        public AudioClip[] DistantBattle => distantBattle;

        public Entry Find(SoundId id)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null && entries[i].id == id) return entries[i];
            return null;
        }
    }
}
