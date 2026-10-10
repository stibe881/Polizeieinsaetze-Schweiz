using SwissPoliceSim.Player;
using SwissPoliceSim.Util;
using UnityEngine;

namespace SwissPoliceSim.Vehicle
{
    /// <summary>Headlights, blue light bar (alternating), indicators/hazards and a procedurally generated two-tone siren.</summary>
    public sealed class VehicleLights : MonoBehaviour
    {
        public Light[] headlights;
        public Renderer[] blueLeft, blueRight;
        public Light[] blueLights;
        public Renderer[] indicatorsLeft, indicatorsRight;
        public AudioSource sirenSource;

        public bool HeadlightsOn { get; private set; }
        public bool BlueOn { get; private set; }
        public bool SirenOn { get; private set; }
        public bool IndicatorL { get; private set; }
        public bool IndicatorR { get; private set; }
        public bool HazardOn { get; private set; }

        static readonly Color BlueOff = new Color(0.05f, 0.08f, 0.2f), BlueOnC = new Color(0.1f, 0.3f, 1f);
        static readonly Color AmberOff = new Color(0.25f, 0.15f, 0.02f), AmberOn = new Color(1f, 0.6f, 0.05f);

        public void HandleInput()
        {
            if (GameInput.Headlights()) { HeadlightsOn = !HeadlightsOn; foreach (var l in headlights) l.enabled = HeadlightsOn; }
            if (GameInput.BlueLights()) BlueOn = !BlueOn;
            if (GameInput.Siren()) { SirenOn = !SirenOn; if (SirenOn) { BlueOn = true; sirenSource.Play(); } else sirenSource.Stop(); }
            if (GameInput.IndicatorLeft()) { IndicatorL = !IndicatorL; IndicatorR = false; HazardOn = false; }
            if (GameInput.IndicatorRight()) { IndicatorR = !IndicatorR; IndicatorL = false; HazardOn = false; }
            if (GameInput.Hazard()) { HazardOn = !HazardOn; IndicatorL = IndicatorR = false; }
        }

        void Update()
        {
            bool blink = Mathf.Repeat(Time.time, 0.7f) < 0.35f;   // 1.4 Hz indicators
            SetColor(indicatorsLeft, (IndicatorL || HazardOn) && blink ? AmberOn : AmberOff);
            SetColor(indicatorsRight, (IndicatorR || HazardOn) && blink ? AmberOn : AmberOff);

            bool phase = Mathf.Repeat(Time.time, 0.25f) < 0.125f;  // 4 Hz alternating flash
            SetColor(blueLeft, BlueOn && phase ? BlueOnC : BlueOff);
            SetColor(blueRight, BlueOn && !phase ? BlueOnC : BlueOff);
            if (blueLights != null)
                for (int i = 0; i < blueLights.Length; i++)
                    blueLights[i].enabled = BlueOn && ((i % 2 == 0) == phase);
        }

        static void SetColor(Renderer[] rs, Color c)
        {
            if (rs == null) return;
            foreach (var r in rs) r.material.color = c;
        }

        /// <summary>Two-tone horn (alternating 440/587 Hz, ~0.9 s per cycle) as a looping clip. Placeholder for licensed audio.</summary>
        public static AudioClip MakeSirenClip()
        {
            const int rate = 44100; const float period = 0.9f;
            int n = (int)(rate * period * 2);
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = Mathf.Repeat(t, period) < period * 0.5f ? 440f : 587f;
                phase += 2 * System.Math.PI * f / rate;
                data[i] = (float)System.Math.Sin(phase) * 0.35f + (float)System.Math.Sin(phase * 2) * 0.1f;
            }
            var clip = AudioClip.Create("SirenTwoTone", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
