using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwissPoliceSim.Audio
{
    /// <summary>
    /// All sounds are synthesised at start-up (no audio assets in the repository): engine loop, Swiss two-tone horn ("Wechselklanghorn"), horn, indicator tick,
    /// rain, wind, thunder, crash, radio squelch/beeps, door and footsteps. Quality is "game prototype"; a sound designer can replace any clip by assigning the same key.
    /// </summary>
    public static class ProceduralAudio
    {
        const int SR = 44100;
        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        public static readonly Dictionary<string, AudioClip> Overrides = new Dictionary<string, AudioClip>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Cache.Clear(); Overrides.Clear(); }

        static AudioClip Get(string key, Func<AudioClip> make) { if (Overrides.TryGetValue(key, out var o) && o != null) return o; if (!Cache.TryGetValue(key, out var c) || c == null) { c = make(); Cache[key] = c; } return c; }

        static AudioClip Clip(string name, float[] data, bool loopSafe = false)
        {
            var c = AudioClip.Create(name, data.Length, 1, SR, false); c.SetData(data, 0); return c;
        }

        static float Saw(double ph) => (float)(2.0 * (ph - Math.Floor(ph + 0.5)));

        public static AudioClip Engine() => Get("engine", () =>
        {
            int n = SR * 2; var d = new float[n]; var rnd = new System.Random(1);
            double[] amp = { 1.0, 0.7, 0.55, 0.35, 0.25, 0.15 }; int[] mult = { 1, 2, 3, 4, 6, 8 };
            double f0 = 50;                                    // exactly 100 cycles in 2 s → seamless loop
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR; double s = 0;
                for (int h = 0; h < amp.Length; h++) s += amp[h] * Math.Sin(2 * Math.PI * f0 * mult[h] * t + h * 0.7);
                double pulse = 0.65 + 0.35 * Math.Sin(2 * Math.PI * f0 * 0.5 * t);        // firing pulses
                d[i] = (float)(s * pulse * 0.16);
            }
            double lp = 0; for (int i = 0; i < n; i++) { lp += (rnd.NextDouble() * 2 - 1 - lp) * 0.05; d[i] += (float)lp * 0.05f; }
            return Clip("engine", d, true);
        });

        /// <summary>Swiss emergency two-tone: a fourth apart, ≈ 0.6 s per tone.</summary>
        public static AudioClip Siren() => Get("siren", () =>
        {
            int tone = (int)(SR * 0.62); int n = tone * 2; var d = new float[n]; double fa = 392, fb = 523; double ph = 0;
            for (int i = 0; i < n; i++)
            {
                bool a = i < tone; double f = a ? fa : fb; ph += f / SR;
                double edge = Math.Min(1.0, Math.Min((i % tone) / 400.0, (tone - (i % tone)) / 800.0));
                double s = Saw(ph) * 0.5 + Math.Sin(2 * Math.PI * ph) * 0.6 + Math.Sin(4 * Math.PI * ph) * 0.2;
                d[i] = (float)(s * edge * 0.42);
            }
            return Clip("siren", d, true);
        });

        public static AudioClip Horn() => Get("horn", () =>
        {
            int n = SR; var d = new float[n]; for (int i = 0; i < n; i++) { double t = i / (double)SR; d[i] = (float)((Math.Sin(2 * Math.PI * 420 * t) + 0.8 * Math.Sin(2 * Math.PI * 530 * t) + 0.25 * Math.Sin(2 * Math.PI * 840 * t)) * 0.25); }
            return Clip("horn", d, true);
        });

        public static AudioClip IndicatorTick() => Get("tick", () =>
        {
            int n = (int)(SR * 0.06); var d = new float[n]; for (int i = 0; i < n; i++) { double t = i / (double)SR; d[i] = (float)(Math.Sin(2 * Math.PI * 1800 * t) * Math.Exp(-t * 90) * 0.4); }
            return Clip("tick", d);
        });

        static float[] Noise(int n, double lpCoef, int seed, double gain)
        {
            var d = new float[n]; var rnd = new System.Random(seed); double lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++) { lp += (rnd.NextDouble() * 2 - 1 - lp) * lpCoef; lp2 += (lp - lp2) * lpCoef * 2; d[i] = (float)(lp2 * gain); }
            return d;
        }

        public static AudioClip Rain() => Get("rain", () => { var d = Noise(SR * 3, 0.55, 3, 1.1); Crossfade(d, SR / 4); return Clip("rain", d, true); });
        public static AudioClip Wind() => Get("wind", () => { var d = Noise(SR * 4, 0.01, 4, 7.0); for (int i = 0; i < d.Length; i++) d[i] *= (float)(0.6 + 0.4 * Math.Sin(2 * Math.PI * i / (double)d.Length * 3)); Crossfade(d, SR / 3); return Clip("wind", d, true); });

        static void Crossfade(float[] d, int len)
        {
            for (int i = 0; i < len; i++) { float t = i / (float)len; d[i] = d[i] * t + d[d.Length - len + i] * (1 - t); }
        }

        public static AudioClip Thunder() => Get("thunder", () =>
        {
            int n = SR * 5; var d = Noise(n, 0.02, 5, 14.0); for (int i = 0; i < n; i++) { double t = i / (double)SR; d[i] *= (float)(Math.Exp(-t * 0.9) * Math.Min(1.0, t * 6)); }
            return Clip("thunder", d);
        });

        public static AudioClip Crash() => Get("crash", () =>
        {
            int n = (int)(SR * 1.2); var d = Noise(n, 0.35, 6, 3.0); for (int i = 0; i < n; i++) { double t = i / (double)SR; d[i] = (float)Math.Tanh(d[i] * 2.0 * Math.Exp(-t * 5)) * 0.8f + (float)(Math.Sin(2 * Math.PI * 70 * t) * Math.Exp(-t * 8) * 0.5); }
            return Clip("crash", d);
        });

        public static AudioClip RadioBeep() => Get("radiobeep", () =>
        {
            int n = (int)(SR * 0.22); var d = new float[n]; var rnd = new System.Random(8);
            for (int i = 0; i < n; i++) { double t = i / (double)SR; double env = Math.Min(1, t * 80) * Math.Min(1, (0.22 - t) * 40); d[i] = (float)((Math.Sin(2 * Math.PI * 1150 * t) * 0.4 + (rnd.NextDouble() * 2 - 1) * 0.12) * env); }
            return Clip("radiobeep", d);
        });

        public static AudioClip RadioSquelch() => Get("squelch", () => { int n = (int)(SR * 0.3); var d = Noise(n, 0.7, 9, 0.6); for (int i = 0; i < n; i++) d[i] *= (float)Math.Exp(-i / (double)SR * 9); return Clip("squelch", d); });

        public static AudioClip DoorClose() => Get("door", () =>
        {
            int n = (int)(SR * 0.35); var d = Noise(n, 0.12, 10, 5.0); for (int i = 0; i < n; i++) { double t = i / (double)SR; d[i] = (float)(d[i] * Math.Exp(-t * 18) + Math.Sin(2 * Math.PI * 95 * t) * Math.Exp(-t * 20) * 0.5); }
            return Clip("door", d);
        });

        public static AudioClip Step(int variant) => Get("step" + (variant & 3), () =>
        {
            int n = (int)(SR * 0.12); var d = Noise(n, 0.18 + 0.03 * (variant & 3), 20 + variant, 6.0); for (int i = 0; i < n; i++) d[i] *= (float)Math.Exp(-i / (double)SR * 38);
            return Clip("step" + (variant & 3), d);
        });

        public static AudioClip Beep(float freq = 880f, float seconds = 0.12f) => Get($"beep{freq}_{seconds}", () =>
        {
            int n = (int)(SR * seconds); var d = new float[n]; for (int i = 0; i < n; i++) { double t = i / (double)SR; d[i] = (float)(Math.Sin(2 * Math.PI * freq * t) * 0.25 * Math.Min(1, t * 200) * Math.Min(1, (seconds - t) * 100)); }
            return Clip("beep", d);
        });
    }
}
