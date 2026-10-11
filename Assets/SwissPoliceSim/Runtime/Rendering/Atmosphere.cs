using System;
using SwissPoliceSim.Audio;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Geo;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SwissPoliceSim.Rendering
{
    /// <summary>
    /// Sun, moon, sky, ambient light, fog, post-processing (bloom, ACES, exposure, vignette) and weather (rain, snow, fog, thunderstorms) driven by the game clock
    /// and the regional <see cref="WeatherModel"/>. All Unity objects are created in code, no scene setup required.
    /// </summary>
    public sealed class Atmosphere : MonoBehaviour
    {
        public Camera cam; public Transform viewer;
        public Func<DateTime> localTime = () => new DateTime(2026, 10, 12, 12, 0, 0);
        public int weatherSeed = 1; public bool weatherEnabled = true;
        /// <summary>Optional overrides for testing/debug: -1 = use model.</summary>
        public float forceRain = -1f, forceFog = -1f;

        public WeatherSample Weather { get; private set; } = Clear();
        static WeatherSample Clear() => new WeatherSample { TemperatureC = 14f, Visibility = 20000f, RoadFriction = 1f, WindMs = 2f };
        public float Daylight { get; private set; } = 1f; public double SunElevation { get; private set; }

        Light _sun, _moon; Material _sky; Volume _volume; Bloom _bloom; ColorAdjustments _color; Vignette _vignette; Tonemapping _tone;
        ParticleSystem _rain, _snow; AudioSource _rainSrc, _windSrc, _oneShot; float _nextSky, _nextWeather, _wet, _snowCover, _flash, _nextThunder = -1;
        double _lat = 46.95, _lon = 7.44, _alt = 540; bool _inited;

        void Start() { Init(); }

        void Init()
        {
            if (_inited) return; _inited = true;
            var sunGo = new GameObject("Sun"); sunGo.transform.SetParent(transform, false); _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional; _sun.shadows = LightShadows.Soft; _sun.shadowStrength = 0.95f; _sun.shadowBias = 0.05f; _sun.shadowNormalBias = 0.4f; _sun.color = Color.white; _sun.intensity = 1.4f;
            var moonGo = new GameObject("Moon"); moonGo.transform.SetParent(transform, false); _moon = moonGo.AddComponent<Light>();
            _moon.type = LightType.Directional; _moon.shadows = LightShadows.None; _moon.color = new Color(0.55f, 0.65f, 1f); _moon.intensity = 0f;
            RenderSettings.sun = _sun; RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.reflectionIntensity = 1f;
            var sh = Shader.Find("Skybox/Procedural");
            if (sh != null) { _sky = new Material(sh) { name = "SkyProc" }; RenderSettings.skybox = _sky; }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.00008f;
            if (cam != null) { cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true; cam.farClipPlane = 60000f; cam.nearClipPlane = 0.2f; cam.depth = 0; }
            SetupPost();
            SetupWeatherObjects();
            QualitySettings.shadowDistance = 320f; QualitySettings.shadowCascades = 4; QualitySettings.lodBias = 1.6f; QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        }

        void SetupPost()
        {
            var go = new GameObject("PostProcessing"); go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>(); _volume.isGlobal = true; _volume.priority = 1; _volume.weight = 1;
            var p = ScriptableObject.CreateInstance<VolumeProfile>(); _volume.sharedProfile = p; _volume.profile = p;
            _tone = p.Add<Tonemapping>(true); _tone.mode.Override(TonemappingMode.ACES);
            _bloom = p.Add<Bloom>(true); _bloom.threshold.Override(1.0f); _bloom.intensity.Override(0.35f); _bloom.scatter.Override(0.65f); _bloom.highQualityFiltering.Override(true);
            _color = p.Add<ColorAdjustments>(true); _color.postExposure.Override(0.0f); _color.contrast.Override(8f); _color.saturation.Override(6f);
            _vignette = p.Add<Vignette>(true); _vignette.intensity.Override(0.22f); _vignette.smoothness.Override(0.45f);
            if (cam != null)
            {
                var ud = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                ud.renderPostProcessing = true; ud.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; ud.antialiasingQuality = AntialiasingQuality.High; ud.dithering = true;
            }
        }

        void SetupWeatherObjects()
        {
            _rain = MakeParticles("Rain", 0.02f, new Color(0.8f, 0.85f, 0.95f, 0.30f), true); _snow = MakeParticles("Snow", 0.05f, new Color(1, 1, 1, 0.85f), false);
            _rainSrc = gameObject.AddComponent<AudioSource>(); _rainSrc.clip = ProceduralAudio.Rain(); _rainSrc.loop = true; _rainSrc.volume = 0; _rainSrc.spatialBlend = 0; _rainSrc.Play();
            _windSrc = gameObject.AddComponent<AudioSource>(); _windSrc.clip = ProceduralAudio.Wind(); _windSrc.loop = true; _windSrc.volume = 0; _windSrc.spatialBlend = 0; _windSrc.Play();
            _oneShot = gameObject.AddComponent<AudioSource>(); _oneShot.spatialBlend = 0;
        }

        ParticleSystem MakeParticles(string name, float size, Color col, bool rain)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.startLifetime = rain ? 1.1f : 7f; main.startSpeed = 0f; main.startSize = size; main.startColor = col; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = rain ? 6000 : 5000; main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 0;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(rain ? 28 : 34, 0.5f, rain ? 28 : 34);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(rain ? -1.5f : -0.4f, rain ? 1.5f : 0.4f); vel.y = new ParticleSystem.MinMaxCurve(rain ? -19f : -1.6f, rain ? -15f : -0.9f); vel.z = new ParticleSystem.MinMaxCurve(rain ? -1.5f : -0.4f, rain ? 1.5f : 0.4f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = rain ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard; if (rain) { r.velocityScale = 0.04f; r.lengthScale = 2.2f; }
            var shader = Shader.Find("Sprites/Default"); if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) r.sharedMaterial = new Material(shader) { name = name + "Mat", color = Color.white };
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        void Update()
        {
            if (!_inited) Init();
            float dt = Time.deltaTime;
            Shader.SetGlobalFloat("_SpsTime", (float)(Time.timeAsDouble % 3600.0));
            if (Time.unscaledTime >= _nextSky) { _nextSky = Time.unscaledTime + 0.25f; UpdateSun(); }
            if (Time.unscaledTime >= _nextWeather) { _nextWeather = Time.unscaledTime + 1f; UpdateWeather(); }
            UpdateFlash(dt); FollowCamera();
        }

        void UpdateSun()
        {
            var t = localTime();
            if (viewer != null) { var p = World.WorldOrigin.ToLv95(viewer.position); var w = SwissCoordinateConverter.ToWgs84(p); _lat = w.Lat; _lon = w.Lon; _alt = p.H; }
            SunModel.Position(t, _lat, _lon, out double el, out double az); SunElevation = el;
            float day = SunModel.Daylight(el); Daylight = day;
            float ca = (float)Math.Cos(el * Math.PI / 180), sa = (float)Math.Sin(el * Math.PI / 180); float azr = (float)(az * Math.PI / 180);
            var toSun = new Vector3((float)Math.Sin(azr) * ca, sa, (float)Math.Cos(azr) * ca);
            _sun.transform.rotation = Quaternion.LookRotation(-toSun);
            float low = Mathf.Clamp01(1f - (float)(el / 25.0));                                          // warmer near the horizon
            _sun.color = Color.Lerp(new Color(1f, 0.96f, 0.9f), new Color(1f, 0.62f, 0.35f), Mathf.Clamp01(low * 0.9f));
            float overcast = Weather.CloudCover; float sunI = Mathf.Clamp01((float)(el + 2) / 12f) * Mathf.Lerp(1.55f, 0.55f, overcast * 0.85f);
            _sun.intensity = sunI; _sun.enabled = el > -3;
            // moon opposite the sun at night
            _moon.transform.rotation = Quaternion.LookRotation(toSun * 0.9f + new Vector3(0.2f, 0, 0.1f)); _moon.intensity = Mathf.Clamp01(1f - day) * 0.22f * (1f - overcast * 0.5f); _moon.enabled = day < 0.6f;
            if (_sky != null)
            {
                _sky.SetFloat("_Exposure", Mathf.Lerp(0.08f, 1.15f, Mathf.Clamp01(day * day * 1.3f)) * Mathf.Lerp(1f, 0.6f, overcast)); _sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.9f, 1.4f, low) + overcast * 0.6f);
                _sky.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.42f, 0.42f, 0.46f), overcast)); _sky.SetColor("_GroundColor", Color.Lerp(new Color(0.37f, 0.35f, 0.33f), new Color(0.12f, 0.12f, 0.14f), 1f - day));
                _sky.SetFloat("_SunSize", Mathf.Lerp(0.05f, 0.0f, overcast)); RenderSettings.skybox = _sky;
            }
            RenderSettings.ambientIntensity = Mathf.Lerp(0.18f, 1.0f, day) * Mathf.Lerp(1f, 0.8f, overcast);
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.25f, 1f, day);
            if (Time.frameCount % 8 == 0) DynamicGI.UpdateEnvironment();
            Shader.SetGlobalFloat("_SpsNight", 1f - day);
            // exposure & post
            if (_color != null) { _color.postExposure.value = Mathf.Lerp(0.9f, 0f, day); _color.saturation.value = Mathf.Lerp(-6f, 8f, day) - Weather.FogDensity * 14f; }
            if (_bloom != null) { _bloom.threshold.value = Mathf.Lerp(0.55f, 1.0f, day); _bloom.intensity.value = Mathf.Lerp(0.8f, 0.3f, day) + _flash * 3f; }
            // fog
            float vis = Mathf.Max(60f, Weather.Visibility); float dens = Mathf.Clamp(2.2f / vis, 0.00006f, 0.02f);
            if (forceFog >= 0) dens = Mathf.Lerp(0.00008f, 0.012f, forceFog);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, dens, 0.2f);
            var fogDay = Color.Lerp(new Color(0.70f, 0.78f, 0.88f), new Color(0.72f, 0.74f, 0.78f), overcast); var fogNight = new Color(0.02f, 0.03f, 0.06f);
            RenderSettings.fogColor = Color.Lerp(fogNight, fogDay, day) * (1f + _flash);
        }

        void UpdateWeather()
        {
            if (!weatherEnabled) { Weather = Clear(); ApplyWeatherVisuals(); return; }
            var t = localTime(); var p = viewer != null ? World.WorldOrigin.ToLv95(viewer.position) : new Lv95(2600000, 1200000, 540);
            var w = WeatherModel.Sample(p.E, p.N, p.H, t, weatherSeed);
            if (forceRain >= 0) { w.PrecipitationMmH = forceRain * 8f; w.IsRain = forceRain > 0 && w.TemperatureC > 1.5f; w.IsSnow = forceRain > 0 && !w.IsRain; w.CloudCover = Mathf.Max(w.CloudCover, forceRain > 0 ? 0.9f : 0f); }
            Weather = w; ApplyWeatherVisuals();
        }

        void ApplyWeatherVisuals()
        {
            var w = Weather; float rainRate = (w.IsRain || w.IsSleet) ? Mathf.Clamp(w.PrecipitationMmH / 8f, 0.08f, 1f) : 0f; float snowRate = w.IsSnow ? Mathf.Clamp(w.PrecipitationMmH / 5f, 0.1f, 1f) : 0f;
            if (_rain != null) { var em = _rain.emission; em.rateOverTime = 3600f * rainRate; if (rainRate > 0 && !_rain.isPlaying) _rain.Play(); else if (rainRate <= 0 && _rain.isPlaying) _rain.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            if (_snow != null) { var em = _snow.emission; em.rateOverTime = 2600f * snowRate; if (snowRate > 0 && !_snow.isPlaying) _snow.Play(); else if (snowRate <= 0 && _snow.isPlaying) _snow.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            if (_rainSrc != null) _rainSrc.volume = Mathf.Lerp(_rainSrc.volume, rainRate * 0.55f, 0.3f);
            if (_windSrc != null) _windSrc.volume = Mathf.Lerp(_windSrc.volume, Mathf.Clamp01((w.WindMs - 3f) / 14f) * 0.4f, 0.3f);
            float wetTarget = (w.IsRain || w.IsSleet) ? 1f : (_wet > 0.2f ? _wet - 0.1f : 0f); _wet = Mathf.MoveTowards(_wet, wetTarget, 0.25f);
            _snowCover = Mathf.MoveTowards(_snowCover, w.IsSnow && w.TemperatureC < 1.5f ? 1f : (w.TemperatureC > 3f ? 0f : _snowCover), 0.05f);
            MaterialLibrary.SetWetness(_wet); MaterialLibrary.SetSnowCover(_snowCover * 0.6f);
            // thunder
            if (w.Thunder && _nextThunder < 0) _nextThunder = Time.unscaledTime + UnityEngine.Random.Range(6f, 22f);
            if (!w.Thunder) _nextThunder = -1;
            if (_nextThunder > 0 && Time.unscaledTime >= _nextThunder) { _flash = 1f; _nextThunder = Time.unscaledTime + UnityEngine.Random.Range(14f, 50f); if (_oneShot != null) _oneShot.PlayOneShot(ProceduralAudio.Thunder(), 0.9f); }
        }

        void UpdateFlash(float dt) { if (_flash > 0) { _flash = Mathf.MoveTowards(_flash, 0, dt * 2.2f); _moon.intensity += _flash * 1.4f; } }

        void FollowCamera()
        {
            var anchor = cam != null ? cam.transform : viewer; if (anchor == null) return;
            if (_rain != null) _rain.transform.position = anchor.position + Vector3.up * 12f;
            if (_snow != null) _snow.transform.position = anchor.position + Vector3.up * 9f;
        }

        public float Wetness => _wet;
    }
}
