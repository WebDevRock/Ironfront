using Ironfront.Audio;
using Ironfront.Core.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>Volume sliders (master, effects, interface, unit voices, ambience) bound to <see cref="GameAudio.Mix"/>.</summary>
    public class AudioOptionsPanel : MonoBehaviour
    {
        [SerializeField] private Slider master;
        [SerializeField] private Slider effects;
        [SerializeField] private Slider interfaceVolume;
        [SerializeField] private Slider voice;
        [SerializeField] private Slider ambience;

        private bool _binding;

        private void OnEnable()
        {
            _binding = true;
            var mix = GameAudio.Mix;
            Set(master, mix.Master);
            Set(effects, mix.Get((int)SoundCategory.Effects));
            Set(interfaceVolume, mix.Get((int)SoundCategory.Interface));
            Set(voice, mix.Get((int)SoundCategory.Voice));
            Set(ambience, mix.Get((int)SoundCategory.Ambience));
            _binding = false;
        }

        private void Awake()
        {
            Hook(master, v => GameAudio.Mix.Master = v);
            Hook(effects, v => GameAudio.Mix.Set((int)SoundCategory.Effects, v));
            Hook(interfaceVolume, v => GameAudio.Mix.Set((int)SoundCategory.Interface, v));
            Hook(voice, v => GameAudio.Mix.Set((int)SoundCategory.Voice, v));
            Hook(ambience, v => GameAudio.Mix.Set((int)SoundCategory.Ambience, v));
        }

        private static void Set(Slider s, float v)
        {
            if (s != null) s.SetValueWithoutNotify(v);
        }

        private void Hook(Slider s, System.Action<float> apply)
        {
            if (s == null) return;
            s.onValueChanged.AddListener(v =>
            {
                if (_binding) return;
                apply(v);
                GameAudio.PlayFlat(SoundId.UiHover);
            });
        }
    }
}
