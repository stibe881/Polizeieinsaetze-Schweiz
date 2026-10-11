// Minimal stand-ins for the URP / Core-RP types used by the runtime, ONLY for the offline compile check (real types come from the Unity packages).
using System;
using UnityEngine;

namespace UnityEngine.Rendering
{
    public abstract class VolumeParameter { public bool overrideState; }
    public class VolumeParameter<T> : VolumeParameter { public T value; public VolumeParameter() { } public VolumeParameter(T v, bool o = false) { value = v; overrideState = o; } public void Override(T v) { value = v; overrideState = true; } }
    public class FloatParameter : VolumeParameter<float> { public FloatParameter(float v, bool o = false) : base(v, o) { } }
    public class ClampedFloatParameter : VolumeParameter<float> { public ClampedFloatParameter(float v, float min, float max, bool o = false) : base(v, o) { } }
    public class MinFloatParameter : VolumeParameter<float> { public MinFloatParameter(float v, float min, bool o = false) : base(v, o) { } }
    public class BoolParameter : VolumeParameter<bool> { public BoolParameter(bool v, bool o = false) : base(v, o) { } }
    public class ColorParameter : VolumeParameter<Color> { public ColorParameter(Color v, bool hdr = false, bool alpha = false, bool eye = false, bool o = false) : base(v, o) { } }
    public class Vector2Parameter : VolumeParameter<Vector2> { public Vector2Parameter(Vector2 v, bool o = false) : base(v, o) { } }
    public class VolumeComponent : ScriptableObject { public bool active = true; }
    public class VolumeProfile : ScriptableObject
    {
        public T Add<T>(bool overrides = false) where T : VolumeComponent { return ScriptableObject.CreateInstance<T>(); }
        public bool TryGet<T>(out T c) where T : VolumeComponent { c = null; return false; }
    }
    public class Volume : MonoBehaviour { public bool isGlobal; public VolumeProfile profile; public VolumeProfile sharedProfile; public float weight = 1; public float priority; }
}

namespace UnityEngine.Rendering.Universal
{
    public enum TonemappingMode { None, Neutral, ACES }
    public sealed class TonemappingModeParameter : VolumeParameter<TonemappingMode> { public TonemappingModeParameter(TonemappingMode v, bool o = false) : base(v, o) { } }
    public sealed class Tonemapping : VolumeComponent { public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None); }
    public sealed class Bloom : VolumeComponent
    {
        public MinFloatParameter threshold = new MinFloatParameter(0.9f, 0f); public MinFloatParameter intensity = new MinFloatParameter(0f, 0f); public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0, 1);
        public ColorParameter tint = new ColorParameter(Color.white); public BoolParameter highQualityFiltering = new BoolParameter(false);
    }
    public sealed class ColorAdjustments : VolumeComponent
    {
        public FloatParameter postExposure = new FloatParameter(0f); public ClampedFloatParameter contrast = new ClampedFloatParameter(0f, -100, 100); public ClampedFloatParameter saturation = new ClampedFloatParameter(0f, -100, 100);
        public ColorParameter colorFilter = new ColorParameter(Color.white);
    }
    public sealed class Vignette : VolumeComponent
    {
        public ColorParameter color = new ColorParameter(Color.black); public Vector2Parameter center = new Vector2Parameter(new Vector2(0.5f, 0.5f)); public ClampedFloatParameter intensity = new ClampedFloatParameter(0, 0, 1); public ClampedFloatParameter smoothness = new ClampedFloatParameter(0.2f, 0.01f, 1);
    }
    public sealed class MotionBlur : VolumeComponent { public ClampedFloatParameter intensity = new ClampedFloatParameter(0, 0, 1); }
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }
    public enum AntialiasingQuality { Low, Medium, High }
    public class UniversalAdditionalCameraData : MonoBehaviour
    {
        public bool renderPostProcessing; public AntialiasingMode antialiasing; public AntialiasingQuality antialiasingQuality; public bool renderShadows = true; public bool stopNaN; public bool dithering;
    }
}
