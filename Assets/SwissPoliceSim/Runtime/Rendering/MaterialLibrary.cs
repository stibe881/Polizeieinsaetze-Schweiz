using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Textures;
using UnityEngine;

namespace SwissPoliceSim.Rendering
{
    /// <summary>
    /// All materials of the game, created in code from the procedural textures of the Core library (same textures as the offline preview renderer).
    /// Uses the custom URP shaders in Resources/Shaders; falls back to URP/Lit (or Standard) if they are missing so the game still renders.
    /// </summary>
    public static class MaterialLibrary
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        static Shader _lit, _terrain, _water;
        public static bool UsingFallback { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Cache.Clear(); Textures.Clear(); _lit = _terrain = _water = null; }

        static Shader Find(string name, string fallbackA = "Universal Render Pipeline/Lit", string fallbackB = "Standard")
        {
            var s = Shader.Find(name);
            if (s != null) return s;
            UsingFallback = true;
            s = Shader.Find(fallbackA); if (s != null) return s;
            s = Shader.Find(fallbackB); if (s != null) return s;
            return Shader.Find("Sprites/Default");
        }
        static Shader Lit => _lit != null ? _lit : (_lit = Find("SPS/Lit"));
        static Shader TerrainShader => _terrain != null ? _terrain : (_terrain = Find("SPS/Terrain"));
        static Shader WaterShader => _water != null ? _water : (_water = Find("SPS/Water"));

        // ------------------------------------------------------------------ textures
        public static Texture2D ToTexture(Image im, bool srgb = true, bool mips = true, TextureWrapMode wrap = TextureWrapMode.Repeat, int aniso = 8)
        {
            var t = new Texture2D(im.W, im.H, TextureFormat.RGBA32, mips, !srgb) { wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = aniso };
            t.SetPixelData(im.Px, 0); t.Apply(mips, false);
            return t;
        }

        public static Texture2D Tex(string key, Func<Image> make, bool srgb = true, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            if (!Textures.TryGetValue(key, out var t) || t == null) { t = ToTexture(make(), srgb, true, wrap); t.name = key; Textures[key] = t; }
            return t;
        }

        // ------------------------------------------------------------------ factory
        public static Material Make(string key, Texture tex, Color tint, float smoothness = 0.15f, float metallic = 0f, bool vertexColor = true, bool cutout = false, bool doubleSided = false, Color? emission = null, bool signalLens = false)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = new Material(Lit) { name = key, enableInstancing = true };
            SetBase(m, tex, tint);
            Set(m, "_UseVertexColor", vertexColor ? 1f : 0f); Set(m, "_Smoothness", smoothness); Set(m, "_Glossiness", smoothness); Set(m, "_Metallic", metallic);
            Set(m, "_Cull", doubleSided ? 0f : 2f); Set(m, "_SignalLens", signalLens ? 1f : 0f);
            if (cutout) { Set(m, "_AlphaClip", 1f); Set(m, "_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); }
            Cache[key] = m; return m;
        }

        static void SetBase(Material m, Texture tex, Color tint)
        {
            if (tex != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex); }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint); if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
        }
        static void Set(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }

        public static Material Instance(Material shared, string suffix) { var m = new Material(shared) { name = shared.name + suffix }; return m; }


        // ------------------------------------------------------------------ photo PBR sets (ambientCG, CC0) – mapped in world space, see SPS_Lit "_Pbr"
        static Texture2D Res(string name) => Resources.Load<Texture2D>("PBR/" + name);
        public static bool HasPbr(string set) => Res(set + "_Color") != null;

        /// <summary>Material with a photographic PBR set (albedo, normal, roughness, AO) triplanar-mapped in world space; falls back to <paramref name="fallback"/> if the set is not in the project.</summary>
        public static Material MakePbr(string key, string set, float repeatsPerMetre, float boost, Func<Material> fallback, float smoothScale = 1f, bool vertexColor = true, bool doubleSided = false, float normalStrength = 1f)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var col = Res(set + "_Color"); if (col == null) return fallback();
            var m = Make(key, col, Color.white, 0.2f, 0f, vertexColor, false, doubleSided);
            Set(m, "_Pbr", 1f); Set(m, "_TriScale", repeatsPerMetre); Set(m, "_PbrBoost", boost); Set(m, "_SmoothScale", smoothScale); Set(m, "_NormalStrength", normalStrength);
            var n = Res(set + "_Normal"); var r = Res(set + "_Rough"); var ao = Res(set + "_AO");
            if (n != null && m.HasProperty("_NormalMap")) m.SetTexture("_NormalMap", n); if (r != null && m.HasProperty("_RoughMap")) m.SetTexture("_RoughMap", r); if (ao != null && m.HasProperty("_AOMap")) m.SetTexture("_AOMap", ao);
            return m;
        }

        // ------------------------------------------------------------------ world materials
        public static Material[] Terrain()
        {
            var m = Get("terrain", () =>
            {
                var mat = new Material(TerrainShader) { name = "terrain" };
                if (mat.HasProperty("_GrassMap"))
                {
                    mat.SetTexture("_GrassMap", Res("Grass004_Color") ?? Tex("grass", () => ProceduralTextures.Grass())); mat.SetTexture("_RockMap", Res("Rock030_Color") ?? Tex("rock", () => ProceduralTextures.Rock())); mat.SetTexture("_PavedMap", Res("Ground037_Color") ?? Tex("urbanground", () => ProceduralTextures.Pavement()));
                    var gn = Res("Grass004_Normal"); var rn = Res("Rock030_Normal"); var pn = Res("Ground037_Normal"); if (gn != null) mat.SetTexture("_GrassN", gn); if (rn != null) mat.SetTexture("_RockN", rn); if (pn != null) mat.SetTexture("_PavedN", pn);
                }
                else SetBase(mat, Res("Grass004_Color") ?? Tex("grass", () => ProceduralTextures.Grass()), Color.white);
                return mat;
            });
            return new[] { m };
        }

        static Material Get(string key, Func<Material> make) { if (!Cache.TryGetValue(key, out var m) || m == null) { m = make(); Cache[key] = m; } return m; }

        /// <summary>Road mesh submeshes: 0 motorway, 1 main, 2 local asphalt, 3 sidewalk/kerb, 4 white markings, 5 yellow/other markings.</summary>
        public static Material[] Roads()
        {
            var asphalt = Tex("asphalt", () => ProceduralTextures.Asphalt());
            return new[]
            {
                MakePbr("road0", "Asphalt031", 0.25f, 1.5f, () => Make("road0", asphalt, Color.white, 0.22f)), MakePbr("road1", "Asphalt004", 0.25f, 1.6f, () => Make("road1", asphalt, Color.white, 0.2f)), MakePbr("road2", "Asphalt004", 0.25f, 1.5f, () => Make("road2", asphalt, Color.white, 0.18f)),
                MakePbr("sidewalk", "PavingStones070", 0.5f, 1.3f, () => Make("sidewalk", Tex("pavement", () => ProceduralTextures.Pavement()), Color.white, 0.08f)),
                Make("mark_white", null, Color.white, 0.3f), Make("mark_yellow", null, Color.white, 0.3f)
            };
        }

        /// <summary>Building submeshes: 0..2 old facade textures (far LOD), 3 pitched roof, 4 flat roof, 5 plaster, 6 window atlas, 7 trim, 8 shutters, 9 railing.</summary>
        public static Material[] Buildings()
        {
            return new[]
            {
                Make("facade0", Tex("facade0", () => ProceduralTextures.FacadeResidential()), Color.white, 0.05f), Make("facade1", Tex("facade1", () => ProceduralTextures.FacadeCommercial()), Color.white, 0.08f), Make("facade2", Tex("facade2", () => ProceduralTextures.FacadeIndustrial()), Color.white, 0.1f),
                MakePbr("roof_tiles", "RoofingTiles014A", 0.5f, 1.5f, () => Make("roof_tiles", Tex("rooftiles", () => ProceduralTextures.RoofTiles()), Color.white, 0.12f, 0f, true, false, true), 1f, true, true), MakePbr("roof_flat", "Gravel022", 0.5f, 1.4f, () => Make("roof_flat", Tex("roofflat", () => ProceduralTextures.FlatRoof()), Color.white, 0.05f, 0f, true, false, true), 1f, true, true),
                MakePbr("plaster", "Plaster003", 0.4f, 1.7f, () => Make("plaster", Tex("plaster", () => ProceduralTextures.PlasterWall()), Color.white, 0.04f)), Make("windows", Tex("windows", () => ProceduralTextures.WindowAtlas(), true, TextureWrapMode.Clamp), Color.white, 0.88f, 0.1f),
                MakePbr("trim", "Concrete025", 0.5f, 1.6f, () => Make("trim", Tex("stone", () => ProceduralTextures.Stone()), Color.white, 0.06f, 0f, true, false, true), 1f, true, true), Make("shutters", Tex("shutter", () => ProceduralTextures.ShutterSlats()), Color.white, 0.12f, 0f, true, false, true),
                Make("railing", Tex("railing", () => ProceduralTextures.Railing()), Color.white, 0.4f, 0.6f, true, true, true)
            };
        }

        /// <summary>Furniture: 0 painted metal/concrete/signs, 1 signal lenses (animated), 2 sign faces.</summary>
        public static Material[] Furniture() => new[] { MakePbr("furniture", "Metal032", 0.5f, 1.2f, () => Make("furniture", null, Color.white, 0.35f, 0.3f)), Make("signal_lens", null, Color.white, 0.8f, 0f, true, false, false, null, true), Make("signface", null, Color.white, 0.4f, 0f, true, false, true) };

        public static Material[] Rails() => new[] { MakePbr("ballast", "Gravel022", 0.5f, 1.2f, () => Make("ballast", Tex("gravel", () => ProceduralTextures.Gravel()), Color.white, 0.05f)), Make("rails", null, Color.white, 0.6f, 0.8f), MakePbr("sleepers", "Concrete025", 0.5f, 1.4f, () => Make("sleepers", Tex("stone", () => ProceduralTextures.Stone()), Color.white, 0.05f)), Make("masts", null, Color.white, 0.4f, 0.6f) };

        public static Material Water()
        {
            return Get("water", () =>
            {
                var m = new Material(WaterShader) { name = "water" };
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", Tex("waternormal", () => ProceduralTexturesAudioGlue.WaterNormal(), false));
                else SetBase(m, Tex("water", () => ProceduralTextures.Water()), new Color(1, 1, 1, 0.8f));
                return m;
            });
        }

        public static Material[] Trees() => new[] { Make("trunk", null, Color.white, 0.05f), Make("foliage", Tex("foliage", () => ProceduralTextures.FoliageAtlas(), true, TextureWrapMode.Clamp), Color.white, 0.12f, 0f, true, true, true) };

        public static Material[] Humans()
        {
            return new[] { Make("human_body", null, Color.white, 0.08f), Make("human_leather", null, Color.white, 0.45f), Make("human_patch", Tex("uniformpatch", () => ProceduralTextures.UniformPatch(), true, TextureWrapMode.Clamp), Color.white, 0.2f, 0f, false) };
        }

        /// <summary>Car body submeshes (CarMesher.Sub*), lamp slots are returned as per-car instances so they can glow independently.</summary>
        public static Material[] CarBody(string plate, byte[] paintRgb, bool police)
        {
            var decal = Tex("decalatlas", () => ProceduralTextures.DecalAtlas(), true, TextureWrapMode.Clamp);
            var plateTex = Tex("plate_" + plate, () => ProceduralTextures.Plate(plate), true, TextureWrapMode.Clamp);
            var arr = new Material[CarMesher.Submeshes];
            arr[CarMesher.SubPaint] = Make("car_paint", null, Color.white, 0.82f, 0.25f);
            arr[CarMesher.SubGlass] = Make("car_glass", null, new Color(0.04f, 0.06f, 0.08f, 1f), 0.95f, 0.1f, false);
            arr[CarMesher.SubTrim] = Make("car_trim", null, Color.white, 0.25f);
            arr[CarMesher.SubChrome] = Make("car_chrome", null, Color.white, 0.9f, 1f);
            arr[CarMesher.SubInterior] = Make("car_interior", null, Color.white, 0.1f, 0f, true, false, true);
            arr[CarMesher.SubPlate] = Make("plate_" + plate, plateTex, Color.white, 0.3f, 0f, false, false, true);
            arr[CarMesher.SubDecal] = Make("car_decal", decal, Color.white, 0.4f, 0f, false, true, true);
            arr[CarMesher.SubLampFront] = Instance(Make("lamp_front", null, Color.white, 0.9f), "_i");
            arr[CarMesher.SubLampRear] = Instance(Make("lamp_rear", null, Color.white, 0.8f), "_i");
            arr[CarMesher.SubIndicator] = Instance(Make("lamp_ind", null, Color.white, 0.7f), "_i");
            arr[CarMesher.SubBlueL] = Instance(Make("lamp_blue", null, Color.white, 0.7f), "_i");
            arr[CarMesher.SubBlueR] = Instance(Make("lamp_blue", null, Color.white, 0.7f), "_i");
            return arr;
        }

        public static Material[] Wheel() => new[] { Make("tyre", null, Color.white, 0.12f), Make("rim", null, Color.white, 0.85f, 1f) };

        // ------------------------------------------------------------------ weather
        public static void SetWetness(float wet)
        {
            foreach (var m in Cache.Values) if (m != null && m.HasProperty("_Wet")) m.SetFloat("_Wet", wet);
        }

        public static void SetSnowCover(float snow)
        {
            foreach (var m in Cache.Values) if (m != null && m.HasProperty("_SnowCover")) m.SetFloat("_SnowCover", snow);
        }

        public static void SetEmission(Material m, Color c) { if (m == null) return; m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c); }
    }

    /// <summary>Water normal map generated procedurally (height noise → normals).</summary>
    public static class ProceduralTexturesAudioGlue
    {
        public static Image WaterNormal(int n = 256)
        {
            var h = new float[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float u = x / (float)n, v = y / (float)n; h[y * n + x] = ProceduralTextures.Fbm(u, v, 6, 4, 301) * 0.7f + ProceduralTextures.Fbm(u + 0.4f, v + 0.1f, 14, 3, 302) * 0.3f; }
            var im = new Image(n, n);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = h[y * n + (x + 1) % n] - h[y * n + (x + n - 1) % n], dy = h[((y + 1) % n) * n + x] - h[((y + n - 1) % n) * n + x];
                float nx = -dx * 6f, ny = -dy * 6f, nz = 1f; float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                im.Set(x, y, nx / l * 0.5f + 0.5f, ny / l * 0.5f + 0.5f, nz / l * 0.5f + 0.5f, 1f);
            }
            return im;
        }
    }
}
