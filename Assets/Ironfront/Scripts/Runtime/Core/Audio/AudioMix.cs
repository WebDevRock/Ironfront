using System;

namespace Ironfront.Core.Audio
{
    /// <summary>
    /// Volume settings: a master level and one level per category (effects, interface, voice, ambience), each in
    /// [0, 1]. Effective volume = master × category. Pure data; persistence is handled by the caller.
    /// </summary>
    public sealed class AudioMix
    {
        public const int CategoryCount = 4;
        private readonly float[] _category = { 0.9f, 0.8f, 0.8f, 0.6f };
        private float _master = 0.8f;

        public event Action Changed;

        public float Master
        {
            get => _master;
            set { _master = Clamp(value); Changed?.Invoke(); }
        }

        public float Get(int category) => category >= 0 && category < CategoryCount ? _category[category] : 1f;

        public void Set(int category, float value)
        {
            if (category < 0 || category >= CategoryCount) return;
            _category[category] = Clamp(value);
            Changed?.Invoke();
        }

        /// <summary>Master × category, the gain to apply to a sound of that category.</summary>
        public float Effective(int category) => _master * Get(category);

        /// <summary>Converts a linear [0,1] slider to a perceptually even gain (approximately -40 dB at 0.01).</summary>
        public static float SliderToGain(float slider) => slider <= 0f ? 0f : slider * slider;

        private static float Clamp(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
