using System;
using Ironfront.Core.Audio;
using NUnit.Framework;

namespace Ironfront.Tests
{
    public class AudioRulesTests
    {
        // ---------- VoiceLimiter ----------

        [Test]
        public void VoiceLimiter_CapsSimultaneousVoices_UntilTheyFinish()
        {
            var v = new VoiceLimiter();
            Assert.IsTrue(v.TryStart(1, 0f, 1f, maxVoices: 2, minInterval: 0f));
            Assert.IsTrue(v.TryStart(1, 0.1f, 1f, 2, 0f));
            Assert.IsFalse(v.TryStart(1, 0.2f, 1f, 2, 0f), "third overlapping voice refused");
            Assert.AreEqual(2, v.Active(1, 0.5f));
            Assert.IsTrue(v.TryStart(1, 1.05f, 1f, 2, 0f), "first voice ended at 1.0, so a slot is free");
        }

        [Test]
        public void VoiceLimiter_EnforcesMinimumIntervalPerSound()
        {
            var v = new VoiceLimiter();
            Assert.IsTrue(v.TryStart(3, 0f, 0.1f, 8, 0.25f));
            Assert.IsFalse(v.TryStart(3, 0.2f, 0.1f, 8, 0.25f));
            Assert.IsTrue(v.TryStart(3, 0.26f, 0.1f, 8, 0.25f));
        }

        [Test]
        public void VoiceLimiter_TracksSoundsIndependently()
        {
            var v = new VoiceLimiter();
            Assert.IsTrue(v.TryStart(1, 0f, 5f, 1, 0f));
            Assert.IsFalse(v.TryStart(1, 0f, 5f, 1, 0f));
            Assert.IsTrue(v.TryStart(2, 0f, 5f, 1, 0f), "another sound id is unaffected");
            Assert.IsFalse(v.TryStart(4, 0f, 1f, 0, 0f), "zero voices means never");
        }

        // ---------- AudioMix ----------

        [Test]
        public void AudioMix_EffectiveVolumeIsMasterTimesCategory_Clamped()
        {
            var mix = new AudioMix { Master = 0.5f };
            mix.Set(0, 0.8f);
            Assert.AreEqual(0.4f, mix.Effective(0), 1e-5f);
            mix.Set(1, 3f);
            Assert.AreEqual(1f, mix.Get(1));
            mix.Master = -1f;
            Assert.AreEqual(0f, mix.Effective(0));
            mix.Set(2, float.NaN);
            Assert.AreEqual(0f, mix.Get(2));
        }

        [Test]
        public void AudioMix_RaisesChanged_AndSliderCurveIsMonotonic()
        {
            var mix = new AudioMix();
            int changes = 0;
            mix.Changed += () => changes++;
            mix.Master = 0.3f;
            mix.Set(3, 0.2f);
            Assert.AreEqual(2, changes);
            Assert.AreEqual(0f, AudioMix.SliderToGain(0f));
            Assert.AreEqual(1f, AudioMix.SliderToGain(1f), 1e-6f);
            Assert.Less(AudioMix.SliderToGain(0.4f), AudioMix.SliderToGain(0.6f));
        }

        // ---------- SfxSynth ----------

        [Test]
        public void Synth_EveryRecipe_IsNonEmptyFiniteAndWithinFullScale()
        {
            foreach (var name in SfxSynth.Recipes)
            {
                var s = SfxSynth.Generate(name);
                Assert.Greater(s.Length, SfxSynth.SampleRate / 50, name + " has audible length");
                float peak = 0f;
                foreach (float x in s)
                {
                    Assert.IsFalse(float.IsNaN(x) || float.IsInfinity(x), name + " finite");
                    peak = Math.Max(peak, Math.Abs(x));
                }
                Assert.LessOrEqual(peak, 1f, name + " does not clip");
                Assert.Greater(peak, 0.1f, name + " is not silent");
            }
        }

        [Test]
        public void Synth_IsDeterministic()
        {
            CollectionAssert.AreEqual(SfxSynth.Generate("explosion_small_a"), SfxSynth.Generate("explosion_small_a"));
            CollectionAssert.AreNotEqual(SfxSynth.Generate("explosion_small_a"), SfxSynth.Generate("explosion_small_b"));
        }

        [Test]
        public void Synth_LoopsJoinWithoutAClick()
        {
            foreach (var name in SfxSynth.Recipes)
            {
                if (!SfxSynth.IsLoop(name)) continue;
                var s = SfxSynth.Generate(name);
                float rms = 0f;
                for (int i = 1; i < s.Length; i++) rms += (s[i] - s[i - 1]) * (s[i] - s[i - 1]);
                rms = (float)Math.Sqrt(rms / (s.Length - 1));
                float seam = Math.Abs(s[0] - s[s.Length - 1]);
                Assert.Less(seam, rms * 6f + 0.01f, $"{name}: the wrap-around step ({seam:0.0000}) is like any other sample step (rms {rms:0.0000})");
            }
        }

        [Test]
        public void Synth_LoopifyCrossfadesTailIntoHead()
        {
            var raw = new float[150];
            for (int i = 0; i < raw.Length; i++) raw[i] = i < 100 ? 0f : 1f; // tail differs from head
            var loop = SfxSynth.Loopify(raw, 100, 50);
            Assert.AreEqual(100, loop.Length);
            Assert.AreEqual(1f, loop[0], 1e-5f, "loop starts on the tail value, continuing from the end");
            Assert.AreEqual(0f, loop[99], 1e-5f);
        }

        [Test]
        public void Wav_HeaderDescribes16BitMonoPcm()
        {
            var wav = SfxSynth.ToWav(new[] { 0f, 1f, -1f, 0.5f });
            Assert.AreEqual(44 + 8, wav.Length);
            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
            Assert.AreEqual(1, BitConverter.ToInt16(wav, 22), "mono");
            Assert.AreEqual(SfxSynth.SampleRate, BitConverter.ToInt32(wav, 24));
            Assert.AreEqual(16, BitConverter.ToInt16(wav, 34));
            Assert.AreEqual(32767, BitConverter.ToInt16(wav, 46));
            Assert.AreEqual(-32767, BitConverter.ToInt16(wav, 48));
        }
    }
}
