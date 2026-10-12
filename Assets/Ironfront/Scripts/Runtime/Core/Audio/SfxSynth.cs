using System;
using System.Collections.Generic;

namespace Ironfront.Core.Audio
{
    /// <summary>
    /// Procedural sound-effect synthesiser for Ironfront (Milestone 006). Every game sound is generated from a
    /// small recipe of noise, oscillators, filters and envelopes, so the project ships no third-party audio and
    /// all sounds are original, deterministic (seeded) and free of licensing concerns.
    ///
    /// Output: mono float samples in [-1, 1] at <see cref="SampleRate"/>, peak-normalised to the recipe's level.
    /// Pure C#; the editor writes the results to WAV files.
    /// </summary>
    public static class SfxSynth
    {
        public const int SampleRate = 44100;

        /// <summary>Names of every recipe, for tooling and tests.</summary>
        public static readonly string[] Recipes =
        {
            "light_cannon_a", "light_cannon_b", "heavy_cannon_a", "heavy_cannon_b",
            "impact_a", "impact_b", "impact_c",
            "explosion_small_a", "explosion_small_b", "explosion_large", "structure_destroyed",
            "ack_select_a", "ack_select_b", "ack_select_c", "ack_move_a", "ack_move_b", "ack_attack_a", "ack_attack_b",
            "place_structure", "construction_complete", "unit_ready", "delivery",
            "ui_click", "ui_hover", "ui_error", "alert", "victory", "defeat",
            "ambience_wind_loop", "engine_loop", "distant_battle_a", "distant_battle_b", "distant_battle_c",
            "machine_gun_a", "machine_gun_b", "rocket_launch_a", "rocket_launch_b", "research_complete", "repair_work_a", "repair_work_b", "repair_complete",
        };

        /// <summary>True for recipes designed to loop seamlessly.</summary>
        public static bool IsLoop(string name) => name.EndsWith("_loop", StringComparison.Ordinal);

        public static float[] Generate(string name)
        {
            switch (name)
            {
                case "light_cannon_a": return Cannon(0.65f, 115f, 1.0f, 11);
                case "light_cannon_b": return Cannon(0.65f, 125f, 0.95f, 12);
                case "heavy_cannon_a": return Cannon(1.1f, 72f, 1.6f, 21);
                case "heavy_cannon_b": return Cannon(1.1f, 66f, 1.7f, 22);
                case "impact_a": return Impact(31, 1.0f);
                case "impact_b": return Impact(32, 1.15f);
                case "impact_c": return Impact(33, 0.9f);
                case "explosion_small_a": return Explosion(1.5f, 1.0f, 41);
                case "explosion_small_b": return Explosion(1.5f, 1.1f, 42);
                case "explosion_large": return Explosion(2.4f, 1.8f, 43);
                case "structure_destroyed": return StructureCollapse(51);
                case "ack_select_a": return Radio(new[] { 880f, 1320f }, 0.07f, 61);
                case "ack_select_b": return Radio(new[] { 990f, 1480f }, 0.07f, 62);
                case "ack_select_c": return Radio(new[] { 784f, 1175f }, 0.07f, 63);
                case "ack_move_a": return Radio(new[] { 740f, 988f, 1318f }, 0.055f, 64);
                case "ack_move_b": return Radio(new[] { 660f, 880f, 1175f }, 0.055f, 65);
                case "ack_attack_a": return Radio(new[] { 1318f, 880f, 880f }, 0.06f, 66, saw: true);
                case "ack_attack_b": return Radio(new[] { 1175f, 784f, 784f }, 0.06f, 67, saw: true);
                case "place_structure": return Place(71);
                case "construction_complete": return Chime(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 1.1f, 0.8f);
                case "unit_ready": return UnitReady(81);
                case "delivery": return Delivery(91);
                case "ui_click": return Blip(1650f, 0.05f, 0.012f, 0.55f, 101);
                case "ui_hover": return Blip(2400f, 0.03f, 0.006f, 0.25f, 102);
                case "ui_error": return Buzz(103);
                case "alert": return Klaxon();
                case "victory": return Fanfare(new[] { 261.63f, 329.63f, 392f, 523.25f }, major: true);
                case "defeat": return Fanfare(new[] { 440f, 392f, 349.23f, 220f }, major: false);
                case "ambience_wind_loop": return WindLoop(12f, 111);
                case "engine_loop": return EngineLoop(2f, 121);
                case "distant_battle_a": return DistantBattle(131);
                case "distant_battle_b": return DistantBattle(132);
                case "distant_battle_c": return DistantBattle(133);
                case "machine_gun_a": return MachineGun(141, 3);
                case "machine_gun_b": return MachineGun(142, 2);
                case "rocket_launch_a": return RocketLaunch(151);
                case "rocket_launch_b": return RocketLaunch(152);
                case "research_complete": return Chime(new[] { 587.33f, 783.99f, 987.77f, 1174.66f, 1567.98f }, 0.09f, 1.2f, 0.75f);
                case "repair_work_a": return RepairWork(161);
                case "repair_work_b": return RepairWork(162);
                case "repair_complete": return RepairComplete(171);
                default: throw new ArgumentException("Unknown recipe " + name);
            }
        }

        // ---------------- Building blocks ----------------

        private sealed class Noise
        {
            private uint _state;
            public Noise(int seed) { _state = (uint)seed * 2654435761u + 1u; }
            public float Next()
            {
                _state ^= _state << 13; _state ^= _state >> 17; _state ^= _state << 5;
                return (_state & 0xFFFFFF) / 8388608f - 1f;
            }
        }

        /// <summary>One-pole low-pass state.</summary>
        private struct LowPass
        {
            private float _y;
            public float Run(float x, float cutoff)
            {
                float a = 1f - (float)Math.Exp(-2.0 * Math.PI * Math.Max(10f, cutoff) / SampleRate);
                _y += a * (x - _y);
                return _y;
            }
        }

        /// <summary>One-pole high-pass state.</summary>
        private struct HighPass
        {
            private LowPass _lp;
            public float Run(float x, float cutoff) => x - _lp.Run(x, cutoff);
        }

        private static int Len(float seconds) => (int)(seconds * SampleRate);
        private static float T(int i) => (float)i / SampleRate;
        private static float Env(float t, float attack, float decay) => t < attack ? t / attack : (float)Math.Exp(-(t - attack) / decay);
        private static float Sin(double phase) => (float)Math.Sin(phase);
        private static float Drive(float x, float k) => (float)Math.Tanh(x * k) / (float)Math.Tanh(k);

        private static float[] Normalize(float[] s, float peak)
        {
            float max = 0f;
            for (int i = 0; i < s.Length; i++) max = Math.Max(max, Math.Abs(s[i]));
            if (max < 1e-6f) return s;
            float g = peak / max;
            for (int i = 0; i < s.Length; i++) s[i] *= g;
            return s;
        }

        /// <summary>Short fades at both ends remove clicks.</summary>
        private static float[] Fade(float[] s, float inSeconds = 0.002f, float outSeconds = 0.02f)
        {
            int fi = Math.Min(s.Length, Len(inSeconds)), fo = Math.Min(s.Length, Len(outSeconds));
            for (int i = 0; i < fi; i++) s[i] *= (float)i / fi;
            for (int i = 0; i < fo; i++) s[s.Length - 1 - i] *= (float)i / fo;
            return s;
        }

        private static void Echo(float[] s, float delaySeconds, float feedback, int taps)
        {
            int d = Len(delaySeconds);
            for (int t = 1; t <= taps; t++)
            {
                float g = (float)Math.Pow(feedback, t);
                int off = d * t;
                for (int i = s.Length - 1; i >= off; i--) s[i] += s[i - off] * g * 0.5f;
            }
        }

        private static void Mix(float[] into, float[] src, int offset, float gain)
        {
            for (int i = 0; i < src.Length && i + offset < into.Length; i++)
                if (i + offset >= 0) into[i + offset] += src[i] * gain;
        }

        // ---------------- Weapons and impacts ----------------

        /// <summary>Gun report: sharp crack, a pitched thump and a rumbling low tail.</summary>
        private static float[] Cannon(float seconds, float thumpHz, float weight, int seed)
        {
            var n = new Noise(seed);
            var s = new float[Len(seconds)];
            var crackLp = new LowPass(); var tailLp = new LowPass(); var tailLp2 = new LowPass();
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float noise = n.Next();
                float crack = crackLp.Run(noise, 5200f) * Env(t, 0.0008f, 0.018f * weight);
                float f = thumpHz * (0.55f + 0.45f * (float)Math.Exp(-t / 0.05f));
                phase += 2 * Math.PI * f / SampleRate;
                float thump = Sin(phase) * Env(t, 0.002f, 0.09f * weight) * 1.3f;
                float tail = tailLp2.Run(tailLp.Run(noise, 900f / weight), 500f / weight) * Env(t, 0.004f, 0.22f * weight) * 3.5f;
                s[i] = Drive(crack * 1.6f + thump + tail, 1.8f);
            }
            if (weight > 1.2f) Echo(s, 0.11f, 0.35f, 2);
            return Fade(Normalize(s, 0.95f), 0.0005f, 0.08f);
        }

        /// <summary>Shell striking armour: clank of inharmonic partials plus a dull thud.</summary>
        private static float[] Impact(int seed, float pitch)
        {
            var n = new Noise(seed);
            var s = new float[Len(0.4f)];
            var hp = new HighPass(); var lp = new LowPass();
            float[] partials = { 1180f * pitch, 1870f * pitch, 2710f * pitch, 3390f * pitch };
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float noise = n.Next();
                float clank = 0f;
                for (int p = 0; p < partials.Length; p++)
                    clank += Sin(2 * Math.PI * partials[p] * t) * Env(t, 0.0005f, 0.05f / (1 + p * 0.4f));
                float hiss = hp.Run(noise, 2500f) * Env(t, 0.0005f, 0.03f);
                float thud = lp.Run(noise, 300f) * Env(t, 0.002f, 0.08f) * 4f;
                s[i] = Drive(clank * 0.35f + hiss * 0.8f + thud, 1.5f);
            }
            return Fade(Normalize(s, 0.85f));
        }

        /// <summary>Explosion: noise burst with a closing low-pass, sub-bass thump and crackling debris tail.</summary>
        private static float[] Explosion(float seconds, float size, int seed)
        {
            var n = new Noise(seed);
            var crackle = new Noise(seed + 7);
            var s = new float[Len(seconds)];
            var lp1 = new LowPass(); var lp2 = new LowPass();
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float noise = n.Next();
                float cutoff = 180f + 2600f * (float)Math.Exp(-t / (0.12f * size));
                float body = lp2.Run(lp1.Run(noise, cutoff), cutoff * 0.8f) * Env(t, 0.003f, 0.35f * size) * 3f;
                float f = 58f / (float)Math.Sqrt(size) * (0.5f + 0.5f * (float)Math.Exp(-t / 0.15f));
                phase += 2 * Math.PI * f / SampleRate;
                float sub = Sin(phase) * Env(t, 0.004f, 0.25f * size) * 1.2f;
                float c = crackle.Next();
                float debris = (Math.Abs(c) > 0.995f ? c : 0f) * Env(t, 0.15f, 0.4f * size) * (t > 0.12f ? 1f : 0f) * 2.5f;
                s[i] = Drive(body + sub + debris, 2.2f);
            }
            if (size > 1.4f) Echo(s, 0.16f, 0.4f, 3);
            return Fade(Normalize(s, 0.98f), 0.001f, 0.25f);
        }

        private static float[] StructureCollapse(int seed)
        {
            var s = new float[Len(3.2f)];
            var a = Explosion(2.0f, 1.6f, seed);
            var b = Explosion(1.6f, 1.2f, seed + 1);
            var c = Explosion(2.2f, 1.9f, seed + 2);
            Mix(s, a, 0, 0.8f);
            Mix(s, b, Len(0.35f), 0.6f);
            Mix(s, c, Len(0.8f), 0.9f);
            // Rumble of falling masonry.
            var n = new Noise(seed + 3);
            var lp = new LowPass(); var lp2 = new LowPass();
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                s[i] += lp2.Run(lp.Run(n.Next(), 160f), 120f) * Env(t, 0.6f, 1.0f) * 5f;
            }
            return Fade(Normalize(s, 0.98f), 0.001f, 0.4f);
        }

        /// <summary>Short machine-gun burst: rapid cracks with a little low-end body.</summary>
        private static float[] MachineGun(int seed, int rounds)
        {
            var s = new float[Len(0.09f * rounds + 0.2f)];
            for (int r = 0; r < rounds; r++)
            {
                var shot = Cannon(0.18f, 210f, 0.35f, seed * 10 + r);
                Mix(s, shot, Len(0.09f * r), 0.9f - r * 0.1f);
            }
            return Fade(Normalize(s, 0.85f), 0.0005f, 0.05f);
        }

        /// <summary>Rocket launch: a roaring, slowly filtered noise "whoosh" over an ignition thump.</summary>
        private static float[] RocketLaunch(int seed)
        {
            var n = new Noise(seed);
            var s = new float[Len(1.1f)];
            var lp = new LowPass(); var hp = new HighPass();
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float cutoff = 900f + 2600f * (float)Math.Exp(-t / 0.25f);
                float roar = lp.Run(hp.Run(n.Next(), 120f), cutoff) * Env(t, 0.03f, 0.45f) * 2.2f;
                phase += 2 * Math.PI * (80f + 40f * (float)Math.Exp(-t / 0.08f)) / SampleRate;
                float thump = Sin(phase) * Env(t, 0.002f, 0.06f);
                s[i] = Drive(roar + thump, 1.6f);
            }
            return Fade(Normalize(s, 0.85f), 0.001f, 0.15f);
        }

        // ---------------- Unit acknowledgements (radio) ----------------

        /// <summary>
        /// Field-radio acknowledgement: squelch burst, then a short sequence of band-limited, bit-crushed tones.
        /// Ironfront's units "speak" in radio chirps rather than voice lines (no voice acting is used).
        /// </summary>
        private static float[] Radio(float[] tones, float toneSeconds, int seed, bool saw = false)
        {
            var n = new Noise(seed);
            float squelch = 0.035f;
            var s = new float[Len(squelch + tones.Length * toneSeconds + 0.06f)];
            var hp = new HighPass(); var lp = new LowPass();
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float v = 0f;
                if (t < squelch) v = n.Next() * 0.35f * (1f - t / squelch);
                else
                {
                    int k = (int)((t - squelch) / toneSeconds);
                    if (k < tones.Length)
                    {
                        float local = t - squelch - k * toneSeconds;
                        phase += 2 * Math.PI * tones[k] / SampleRate;
                        float osc = saw ? (float)(phase / Math.PI % 2.0 - 1.0) : (Sin(phase) > 0 ? 0.7f : -0.7f) * 0.6f + Sin(phase) * 0.4f;
                        float env = Math.Min(1f, local / 0.004f) * Math.Min(1f, (toneSeconds - local) / 0.008f);
                        v = osc * env * 0.6f + n.Next() * 0.04f;
                    }
                    else v = n.Next() * 0.12f * Math.Max(0f, 1f - (t - squelch - tones.Length * toneSeconds) / 0.05f);
                }
                v = lp.Run(hp.Run(v, 420f), 3400f);
                s[i] = (float)Math.Round(v * 24f) / 24f; // crude bit-crush for the radio texture
            }
            return Fade(Normalize(s, 0.6f));
        }

        // ---------------- Base and economy ----------------

        private static float[] Place(int seed)
        {
            var n = new Noise(seed);
            var s = new float[Len(0.7f)];
            var lp = new LowPass(); var hp = new HighPass();
            double phase = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                phase += 2 * Math.PI * (90f - 40f * Math.Min(1f, t / 0.1f)) / SampleRate;
                float thunk = Sin(phase) * Env(t, 0.002f, 0.08f) + lp.Run(n.Next(), 400f) * Env(t, 0.002f, 0.05f) * 3f;
                float hiss = hp.Run(n.Next(), 3000f) * (t > 0.12f ? Env(t - 0.12f, 0.05f, 0.18f) : 0f) * 0.4f;
                s[i] = Drive(thunk + hiss, 1.4f);
            }
            return Fade(Normalize(s, 0.8f));
        }

        /// <summary>Bell-like arpeggio (two partials per note).</summary>
        private static float[] Chime(float[] notes, float step, float ring, float peak)
        {
            var s = new float[Len(step * notes.Length + ring)];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = Len(step * k);
                for (int i = start; i < s.Length; i++)
                {
                    float t = T(i - start);
                    float e = Env(t, 0.004f, ring * 0.35f);
                    s[i] += (Sin(2 * Math.PI * notes[k] * t) + 0.35f * Sin(2 * Math.PI * notes[k] * 2.01f * t) + 0.12f * Sin(2 * Math.PI * notes[k] * 3.98f * t)) * e;
                }
            }
            return Fade(Normalize(s, peak), 0.002f, 0.1f);
        }

        private static float[] UnitReady(int seed)
        {
            var radio = Radio(new[] { 1046.5f }, 0.03f, seed);
            var chime = Chime(new[] { 659.25f, 987.77f }, 0.09f, 0.5f, 0.7f);
            var s = new float[radio.Length + chime.Length];
            Mix(s, radio, 0, 0.6f);
            Mix(s, chime, radio.Length - Len(0.03f), 1f);
            return Fade(Normalize(s, 0.7f));
        }

        private static float[] Delivery(int seed)
        {
            var n = new Noise(seed);
            var s = new float[Len(0.6f)];
            // Ore tumbling into the hopper: a cascade of small clinks, then a two-note register chime.
            for (int c = 0; c < 9; c++)
            {
                int start = Len(0.025f * c + Math.Abs(n.Next()) * 0.02f);
                float f = 2400f + n.Next() * 800f;
                for (int i = start; i < Math.Min(s.Length, start + Len(0.08f)); i++)
                {
                    float t = T(i - start);
                    s[i] += Sin(2 * Math.PI * f * t) * Env(t, 0.0005f, 0.015f) * 0.4f;
                }
            }
            var ding = Chime(new[] { 1318.5f, 1760f }, 0.07f, 0.35f, 0.5f);
            Mix(s, ding, Len(0.2f), 1f);
            return Fade(Normalize(s, 0.6f));
        }

        /// <summary>
        /// Milestone 009 repair work: an arc-welder crackle (band-limited noise with random bursts) over a few ratchet
        /// clicks from a power wrench.
        /// </summary>
        private static float[] RepairWork(int seed)
        {
            var n = new Noise(seed);
            var s = new float[Len(0.75f)];
            var lp = new LowPass(); var hp = new HighPass();
            float burst = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                if (i % 220 == 0) burst = Math.Abs(n.Next()) > 0.35f ? 1f : 0.25f;
                float crackle = hp.Run(lp.Run(n.Next(), 5200f), 1400f) * burst * Env(t, 0.01f, 0.5f) * 1.6f;
                s[i] = crackle;
            }
            for (int c = 0; c < 4; c++)
            {
                int start = Len(0.12f + c * 0.09f);
                float f = 1800f + n.Next() * 300f;
                for (int i = start; i < Math.Min(s.Length, start + Len(0.03f)); i++)
                {
                    float t = T(i - start);
                    s[i] += (Sin(2 * Math.PI * f * t) + n.Next() * 0.6f) * Env(t, 0.0003f, 0.008f) * 0.8f;
                }
            }
            return Fade(Normalize(s, 0.6f), 0.002f, 0.1f);
        }

        /// <summary>Milestone 009: repair finished — a short pneumatic hiss into a rising two-note chime.</summary>
        private static float[] RepairComplete(int seed)
        {
            var n = new Noise(seed);
            var s = new float[Len(0.8f)];
            var hp = new HighPass();
            for (int i = 0; i < Len(0.25f); i++)
            {
                float t = T(i);
                s[i] = hp.Run(n.Next(), 2500f) * Env(t, 0.005f, 0.12f) * 0.5f;
            }
            Mix(s, Chime(new[] { 880f, 1318.5f }, 0.08f, 0.45f, 0.55f), Len(0.12f), 1f);
            return Fade(Normalize(s, 0.6f));
        }

        // ---------------- Interface ----------------

        private static float[] Blip(float hz, float seconds, float decay, float peak, int seed)
        {
            var n = new Noise(seed);
            var s = new float[Len(seconds)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                s[i] = (Sin(2 * Math.PI * hz * t) * 0.8f + n.Next() * 0.2f * Env(t, 0.0002f, 0.002f)) * Env(t, 0.0008f, decay);
            }
            return Fade(Normalize(s, peak), 0.0005f, 0.008f);
        }

        private static float[] Buzz(int seed)
        {
            var s = new float[Len(0.32f)];
            var lp = new LowPass();
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                bool on = t < 0.11f || (t > 0.15f && t < 0.26f);
                float sq = Sin(2 * Math.PI * 175f * t) > 0 ? 1f : -1f;
                s[i] = on ? lp.Run(sq, 1400f) * 0.5f : lp.Run(0f, 1400f);
            }
            return Fade(Normalize(s, 0.55f));
        }

        private static float[] Klaxon()
        {
            var s = new float[Len(1.0f)];
            var lp = new LowPass();
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                int beat = (int)(t / 0.22f);
                float local = t - beat * 0.22f;
                float f = beat % 2 == 0 ? 660f : 523f;
                float on = beat < 4 && local < 0.18f ? 1f : 0f;
                float sq = Sin(2 * Math.PI * f * t) > 0 ? 1f : -1f;
                s[i] = lp.Run(sq * on, 2200f) * 0.6f;
            }
            return Fade(Normalize(s, 0.6f));
        }

        /// <summary>Short brassy stinger: filtered saw chord built up note by note.</summary>
        private static float[] Fanfare(float[] notes, bool major)
        {
            float step = major ? 0.16f : 0.3f;
            var s = new float[Len(step * notes.Length + 1.6f)];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = Len(step * k);
                var lp = new LowPass();
                double phase = 0;
                for (int i = start; i < s.Length; i++)
                {
                    float t = T(i - start);
                    float vib = 1f + 0.004f * Sin(2 * Math.PI * 5.5 * t);
                    phase += 2 * Math.PI * notes[k] * vib / SampleRate;
                    float saw = (float)(phase / Math.PI % 2.0 - 1.0);
                    float e = Math.Min(1f, t / 0.04f) * (float)Math.Exp(-t / (major ? 1.4f : 1.1f));
                    s[i] += lp.Run(saw, major ? 2200f : 1200f) * e * 0.5f;
                }
            }
            Echo(s, 0.18f, 0.3f, 2);
            return Fade(Normalize(s, 0.7f), 0.005f, 0.3f);
        }

        // ---------------- Loops ----------------

        /// <summary>Wind over open steppe; made seamless by cross-fading the tail into the head.</summary>
        private static float[] WindLoop(float seconds, int seed)
        {
            float fade = 1.5f;
            var n = new Noise(seed);
            var raw = new float[Len(seconds + fade)];
            var lp1 = new LowPass(); var lp2 = new LowPass(); var hp = new HighPass();
            for (int i = 0; i < raw.Length; i++)
            {
                float t = T(i);
                float gust = 0.55f + 0.25f * Sin(2 * Math.PI * t / seconds) + 0.15f * Sin(2 * Math.PI * 3 * t / seconds + 1.3) + 0.1f * Sin(2 * Math.PI * 7 * t / seconds + 0.4);
                float cutoff = 350f + 500f * gust;
                float w = hp.Run(lp2.Run(lp1.Run(n.Next(), cutoff), cutoff * 1.2f), 60f);
                raw[i] = w * gust * 4f;
            }
            return Normalize(Loopify(raw, Len(seconds), Len(fade)), 0.5f);
        }

        /// <summary>Diesel engine idle-to-cruise rumble at 38 Hz firing rate (integer cycles per loop).</summary>
        private static float[] EngineLoop(float seconds, int seed)
        {
            float fade = 0.4f;
            var n = new Noise(seed);
            var raw = new float[Len(seconds + fade)];
            var lp = new LowPass(); var lp2 = new LowPass();
            const float f0 = 38f;
            for (int i = 0; i < raw.Length; i++)
            {
                float t = T(i);
                double ph = 2 * Math.PI * f0 * t;
                float pulse = (float)Math.Pow(Math.Max(0, Math.Sin(ph)), 6);
                float harmonics = Sin(ph) * 0.5f + Sin(ph * 2) * 0.3f + Sin(ph * 3 + 0.5) * 0.15f + Sin(ph * 0.5) * 0.25f;
                float grit = lp.Run(n.Next(), 700f) * (0.4f + pulse);
                raw[i] = lp2.Run(harmonics * 0.6f + pulse * 0.5f + grit * 1.2f, 900f);
            }
            return Normalize(Loopify(raw, Len(seconds), Len(fade)), 0.7f);
        }

        /// <summary>
        /// Turns length+fade samples into a seamless loop of length samples: the extra tail is cross-faded
        /// (equal power) over the beginning, so the last sample flows into the first.
        /// </summary>
        public static float[] Loopify(float[] raw, int length, int fade)
        {
            var s = new float[length];
            Array.Copy(raw, s, length);
            for (int i = 0; i < fade && length + i < raw.Length; i++)
            {
                float x = (float)i / fade;
                float a = (float)Math.Sqrt(x), b = (float)Math.Sqrt(1f - x);
                s[i] = s[i] * a + raw[length + i] * b;
            }
            return s;
        }

        private static float[] DistantBattle(int seed)
        {
            var rng = new Noise(seed);
            var s = new float[Len(3.5f)];
            int booms = 2 + (int)(Math.Abs(rng.Next()) * 3);
            for (int b = 0; b < booms; b++)
            {
                var e = Explosion(1.6f, 1.3f + Math.Abs(rng.Next()) * 0.5f, seed * 10 + b);
                Mix(s, e, Len(Math.Abs(rng.Next()) * 1.8f), 0.6f + Math.Abs(rng.Next()) * 0.4f);
            }
            for (int g = 0; g < 6; g++)
            {
                var c = Cannon(0.5f, 90f, 1f, seed * 20 + g);
                Mix(s, c, Len(Math.Abs(rng.Next()) * 2.8f), 0.3f);
            }
            // Distance: heavy low-pass.
            var lp1 = new LowPass(); var lp2 = new LowPass();
            for (int i = 0; i < s.Length; i++) s[i] = lp2.Run(lp1.Run(s[i], 450f), 350f);
            return Fade(Normalize(s, 0.7f), 0.05f, 0.5f);
        }

        // ---------------- WAV encoding ----------------

        /// <summary>Encodes mono samples as a 16-bit PCM WAV file.</summary>
        public static byte[] ToWav(float[] samples)
        {
            int dataBytes = samples.Length * 2;
            var bytes = new List<byte>(44 + dataBytes);
            void Str(string v) { foreach (char ch in v) bytes.Add((byte)ch); }
            void I32(int v) { bytes.Add((byte)v); bytes.Add((byte)(v >> 8)); bytes.Add((byte)(v >> 16)); bytes.Add((byte)(v >> 24)); }
            void I16(short v) { bytes.Add((byte)v); bytes.Add((byte)(v >> 8)); }
            Str("RIFF"); I32(36 + dataBytes); Str("WAVE");
            Str("fmt "); I32(16); I16(1); I16(1); I32(SampleRate); I32(SampleRate * 2); I16(2); I16(16);
            Str("data"); I32(dataBytes);
            foreach (float f in samples)
            {
                float c = f < -1f ? -1f : f > 1f ? 1f : f;
                I16((short)Math.Round(c * 32767f));
            }
            return bytes.ToArray();
        }
    }
}
