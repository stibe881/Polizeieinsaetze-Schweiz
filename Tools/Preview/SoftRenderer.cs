using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Textures;

/// <summary>
/// Minimal CPU rasteriser used ONLY to preview the generated world and vehicles (shadow map, texture mips, fog, alpha).
/// It is not the Unity renderer; it shows the same geometry and textures.
/// </summary>
public sealed class Mat
{
    public Image[] Mips;                 // null = flat colour
    public Vector3 Base = Vector3.One;
    public float UvScale = 1f;
    public bool UseVertexColor = true;
    public float Alpha = 1f;
    public Vector3 Emissive;
    public float Specular;               // 0..1
    public float Gloss = 32;
    public bool DoubleSided;
    public bool Unlit;
    public bool CutoutAlpha;             // alpha from the texture, discard < .5 (decals)
    public bool Sky;                     // reflect sky colour (glass)

    public static Image[] MakeMips(Image im)
    {
        var l = new List<Image> { im };
        while (l[l.Count - 1].W > 4) l.Add(l[l.Count - 1].Half());
        return l.ToArray();
    }
}

public sealed class SoftRenderer
{
    public readonly int W, H, SS, PW, PH;
    readonly float[] _col, _depth;
    public Vector3 CamPos, CamTarget, CamUp = Vector3.UnitY;
    public float FovY = 55f, Near = 0.2f, Far = 6000f;
    public Vector3 SunDir = Vector3.Normalize(new Vector3(-0.45f, -0.75f, -0.35f));   // direction the light travels
    public Vector3 SunColor = new Vector3(1.0f, 0.93f, 0.80f) * 3.2f;
    public Vector3 SkyZenith = new Vector3(0.22f, 0.42f, 0.78f), SkyHorizon = new Vector3(0.72f, 0.82f, 0.92f);
    public Vector3 AmbientSky = new Vector3(0.30f, 0.40f, 0.60f), AmbientGround = new Vector3(0.16f, 0.15f, 0.13f);
    public float FogDensity = 1f / 5200f;
    public bool Shadows = true;

    struct Draw { public MeshData M; public Matrix4x4 T; public Mat[] Mats; }
    readonly List<Draw> _draws = new List<Draw>();
    float[] _smap; int _sres = 4096; Matrix4x4 _lightVP; Vector3 _sceneCentre; float _sceneRadius;

    public SoftRenderer(int w, int h, int ss)
    {
        W = w; H = h; SS = ss; PW = w * ss; PH = h * ss;
        _col = new float[PW * PH * 3]; _depth = new float[PW * PH];
    }

    public void Add(MeshData m, Matrix4x4 t, params Mat[] mats) { if (m.VertexCount > 0) _draws.Add(new Draw { M = m, T = t, Mats = mats }); }

    // ------------------------------------------------------------------ render
    public void Render(Vector3 sceneCentre, float sceneRadius)
    {
        _sceneCentre = sceneCentre; _sceneRadius = sceneRadius;
        var view = Matrix4x4.CreateLookAt(CamPos, CamTarget, CamUp);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(FovY * (float)Math.PI / 180f, (float)PW / PH, Near, Far);
        // sky
        Parallel.For(0, PH, y =>
        {
            for (int x = 0; x < PW; x++)
            {
                var d = RayDir(x + 0.5f, y + 0.5f, view, proj);
                var c = Sky(d);
                int i = (y * PW + x) * 3; _col[i] = c.X; _col[i + 1] = c.Y; _col[i + 2] = c.Z; _depth[y * PW + x] = float.MaxValue;
            }
        });
        if (Shadows) BuildShadowMap();
        var vp = view * proj;
        // opaque pass then transparent pass
        for (int pass = 0; pass < 2; pass++)
            foreach (var d in _draws)
                for (int s = 0; s < d.M.Indices.Length && s < d.Mats.Length; s++)
                {
                    var mat = d.Mats[s]; if (mat == null) continue;
                    bool transp = mat.Alpha < 0.999f;
                    if ((pass == 0) == transp) continue;
                    RasterSubmesh(d, s, mat, view, vp);
                }
    }

    Vector3 RayDir(float px, float py, Matrix4x4 view, Matrix4x4 proj)
    {
        float nx = (px / PW) * 2 - 1, ny = 1 - (py / PH) * 2;
        float tan = (float)Math.Tan(FovY * Math.PI / 360), asp = (float)PW / PH;
        var dirView = new Vector3(nx * tan * asp, ny * tan, -1);
        Matrix4x4.Invert(view, out var inv);
        return Vector3.Normalize(Vector3.TransformNormal(dirView, inv));
    }

    Vector3 Sky(Vector3 d)
    {
        float t = Math.Max(0, d.Y); t = (float)Math.Pow(t, 0.55f);
        var c = Vector3.Lerp(SkyHorizon, SkyZenith, t);
        float sun = Math.Max(0, Vector3.Dot(d, -SunDir));
        c += new Vector3(1f, 0.9f, 0.7f) * (float)(Math.Pow(sun, 600) * 4 + Math.Pow(sun, 12) * 0.15);
        return c;
    }

    void BuildShadowMap()
    {
        var L = Vector3.Normalize(SunDir);
        var up = Math.Abs(L.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        var eye = _sceneCentre - L * _sceneRadius * 2;
        var lv = Matrix4x4.CreateLookAt(eye, _sceneCentre, up);
        var lp = Matrix4x4.CreateOrthographic(_sceneRadius * 2, _sceneRadius * 2, 1, _sceneRadius * 4);
        _lightVP = lv * lp;
        _smap = new float[_sres * _sres];
        Array.Fill(_smap, float.MaxValue);
        foreach (var d in _draws)
            for (int s = 0; s < d.M.Indices.Length && s < d.Mats.Length; s++)
            {
                var mat = d.Mats[s]; if (mat == null || mat.Alpha < 0.5f || mat.Unlit && mat.Sky) continue;
                var idx = d.M.Indices[s]; var pos = d.M.Positions;
                var mvp = d.T * _lightVP;
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    Vector4 a = Proj(mvp, pos, idx[i]), b = Proj(mvp, pos, idx[i + 1]), c = Proj(mvp, pos, idx[i + 2]);
                    if (a.W <= 0 || b.W <= 0 || c.W <= 0) continue;
                    ShadowTri(a, b, c);
                }
            }
    }

    static Vector4 Proj(Matrix4x4 m, List<float> p, int i)
    {
        float x = p[i * 3], y = p[i * 3 + 1], z = p[i * 3 + 2];
        return new Vector4(x * m.M11 + y * m.M21 + z * m.M31 + m.M41, x * m.M12 + y * m.M22 + z * m.M32 + m.M42, x * m.M13 + y * m.M23 + z * m.M33 + m.M43, x * m.M14 + y * m.M24 + z * m.M34 + m.M44);
    }

    void ShadowTri(Vector4 a, Vector4 b, Vector4 c)
    {
        float ax = (a.X / a.W * 0.5f + 0.5f) * _sres, ay = (1 - (a.Y / a.W * 0.5f + 0.5f)) * _sres, az = a.Z / a.W;
        float bx = (b.X / b.W * 0.5f + 0.5f) * _sres, by = (1 - (b.Y / b.W * 0.5f + 0.5f)) * _sres, bz = b.Z / b.W;
        float cx = (c.X / c.W * 0.5f + 0.5f) * _sres, cy = (1 - (c.Y / c.W * 0.5f + 0.5f)) * _sres, cz = c.Z / c.W;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), maxX = Math.Min(_sres - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), maxY = Math.Min(_sres - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
        float den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy); if (Math.Abs(den) < 1e-9f) return;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den, w1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den, w2 = 1 - w0 - w1;
                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;
                float z = w0 * az + w1 * bz + w2 * cz; int i = y * _sres + x;
                if (z < _smap[i]) _smap[i] = z;
            }
    }

    float ShadowAt(Vector3 wp, float bias)
    {
        var p = Vector4.Transform(new Vector4(wp, 1), _lightVP);
        float sx = (p.X / p.W * 0.5f + 0.5f) * _sres, sy = (1 - (p.Y / p.W * 0.5f + 0.5f)) * _sres, sz = p.Z / p.W - bias;
        float lit = 0; int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = (int)sx + dx, y = (int)sy + dy; n++;
                if ((uint)x >= (uint)_sres || (uint)y >= (uint)_sres) { lit += 1; continue; }
                lit += sz <= _smap[y * _sres + x] ? 1 : 0;
            }
        return lit / n;
    }

    // ------------------------------------------------------------------ raster
    struct V { public Vector4 Clip; public Vector3 World, Normal, Col; public Vector2 Uv; }

    void RasterSubmesh(Draw d, int s, Mat mat, Matrix4x4 view, Matrix4x4 vp)
    {
        var idx = d.M.Indices[s]; var m = d.M;
        var mvp = d.T * vp;
        Matrix4x4.Invert(d.T, out var inv); var nm = Matrix4x4.Transpose(inv);
        var tris = new List<(V, V, V)>(idx.Count / 3);
        var camPos = CamPos;
        for (int i = 0; i + 2 < idx.Count; i += 3)
        {
            var a = MakeV(m, idx[i], d.T, nm, mvp); var b = MakeV(m, idx[i + 1], d.T, nm, mvp); var c = MakeV(m, idx[i + 2], d.T, nm, mvp);
            // back-face culling using the Unity convention (front = geometric normal toward the viewer)
            if (!mat.DoubleSided)
            {
                var gn = Vector3.Cross(b.World - a.World, c.World - a.World);
                if (Vector3.Dot(gn, camPos - a.World) <= 0) continue;
            }
            ClipAndEmit(a, b, c, tris);
        }
        var list = tris;
        // bin into horizontal stripes for parallel raster (each stripe owns its rows → no data races)
        int stripes = 64; int rowsPer = (PH + stripes - 1) / stripes;
        var bins = new List<(V, V, V, int, int)>[stripes]; for (int i = 0; i < stripes; i++) bins[i] = new List<(V, V, V, int, int)>();
        foreach (var (a, b, c) in list)
        {
            float ay = (1 - (a.Clip.Y / a.Clip.W * 0.5f + 0.5f)) * PH, by = (1 - (b.Clip.Y / b.Clip.W * 0.5f + 0.5f)) * PH, cy = (1 - (c.Clip.Y / c.Clip.W * 0.5f + 0.5f)) * PH;
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), y1 = Math.Min(PH - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            if (y1 < y0) continue;
            for (int st = y0 / rowsPer; st <= y1 / rowsPer && st < stripes; st++) bins[st].Add((a, b, c, y0, y1));
        }
        Parallel.For(0, stripes, st =>
        {
            int ys = st * rowsPer, ye = Math.Min(PH - 1, ys + rowsPer - 1);
            foreach (var (a, b, c, _, _) in bins[st]) RasterTri(a, b, c, mat, ys, ye);
        });
    }

    V MakeV(MeshData m, int i, Matrix4x4 T, Matrix4x4 nm, Matrix4x4 mvp)
    {
        var p = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
        var n = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
        return new V
        {
            Clip = Vector4.Transform(new Vector4(p, 1), mvp), World = Vector3.Transform(p, T), Normal = Vector3.Normalize(Vector3.TransformNormal(n, nm)),
            Uv = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]), Col = new Vector3(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2]) / 255f
        };
    }

    static V Lerp(V a, V b, float t) => new V { Clip = Vector4.Lerp(a.Clip, b.Clip, t), World = Vector3.Lerp(a.World, b.World, t), Normal = Vector3.Lerp(a.Normal, b.Normal, t), Col = Vector3.Lerp(a.Col, b.Col, t), Uv = Vector2.Lerp(a.Uv, b.Uv, t) };

    void ClipAndEmit(V a, V b, V c, List<(V, V, V)> o)
    {
        float n = Near;
        bool ia = a.Clip.W > n, ib = b.Clip.W > n, ic = c.Clip.W > n;
        int cnt = (ia ? 1 : 0) + (ib ? 1 : 0) + (ic ? 1 : 0);
        if (cnt == 0) return;
        if (cnt == 3) { o.Add((a, b, c)); return; }
        var vs = new[] { a, b, c }; var inn = new[] { ia, ib, ic };
        var poly = new List<V>();
        for (int i = 0; i < 3; i++)
        {
            var p = vs[i]; var q = vs[(i + 1) % 3]; bool pi = inn[i], qi = inn[(i + 1) % 3];
            if (pi) poly.Add(p);
            if (pi != qi) { float t = (n - p.Clip.W) / (q.Clip.W - p.Clip.W); poly.Add(Lerp(p, q, t)); }
        }
        for (int i = 1; i + 1 < poly.Count; i++) o.Add((poly[0], poly[i], poly[i + 1]));
    }

    void RasterTri(V a, V b, V c, Mat mat, int ys, int ye)
    {
        float iwa = 1 / a.Clip.W, iwb = 1 / b.Clip.W, iwc = 1 / c.Clip.W;
        float ax = (a.Clip.X * iwa * 0.5f + 0.5f) * PW, ay = (1 - (a.Clip.Y * iwa * 0.5f + 0.5f)) * PH;
        float bx = (b.Clip.X * iwb * 0.5f + 0.5f) * PW, by = (1 - (b.Clip.Y * iwb * 0.5f + 0.5f)) * PH;
        float cx = (c.Clip.X * iwc * 0.5f + 0.5f) * PW, cy = (1 - (c.Clip.Y * iwc * 0.5f + 0.5f)) * PH;
        float az = a.Clip.Z * iwa, bz = b.Clip.Z * iwb, cz = c.Clip.Z * iwc;
        float den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy); if (Math.Abs(den) < 1e-7f) return;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), maxX = Math.Min(PW - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
        int minY = Math.Max(ys, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), maxY = Math.Min(ye, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
        if (maxX < minX || maxY < minY) return;

        // texture LOD from the ratio of uv area (in texels) to screen area
        int lod = 0;
        if (mat.Mips != null)
        {
            float uvArea = Math.Abs((b.Uv.X - a.Uv.X) * (c.Uv.Y - a.Uv.Y) - (c.Uv.X - a.Uv.X) * (b.Uv.Y - a.Uv.Y)) * mat.UvScale * mat.UvScale;
            float scrArea = Math.Abs(den) * 0.5f;
            // perspective: use the average of the corner inverse-w to estimate the area ratio more correctly
            float texels = uvArea * mat.Mips[0].W * mat.Mips[0].H;
            if (scrArea > 1e-6f && texels > 0) lod = (int)Math.Max(0, Math.Min(mat.Mips.Length - 1, 0.5f * Math.Log(texels / scrArea, 2) + 0.15f));
        }

        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den, w1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den, w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = 1f / (w0 * iwa + w1 * iwb + w2 * iwc);   // linear view depth (precise at distance)
                int di = y * PW + x;
                if (z >= _depth[di]) continue;
                float p0 = w0 * iwa, p1 = w1 * iwb, p2 = w2 * iwc, ps = p0 + p1 + p2; p0 /= ps; p1 /= ps; p2 /= ps;
                var uv = a.Uv * p0 + b.Uv * p1 + c.Uv * p2;
                var vc = a.Col * p0 + b.Col * p1 + c.Col * p2;
                var wp = a.World * p0 + b.World * p1 + c.World * p2;
                var nrm = Vector3.Normalize(a.Normal * p0 + b.Normal * p1 + c.Normal * p2);
                if (mat.DoubleSided && Vector3.Dot(nrm, CamPos - wp) < 0) nrm = -nrm;

                Vector3 albedo = Lin(mat.Base); float alpha = mat.Alpha;
                if (mat.UseVertexColor) albedo *= Lin(vc);
                if (mat.Mips != null)
                {
                    SampleTex(mat.Mips[lod], uv.X * mat.UvScale, uv.Y * mat.UvScale, out float tr, out float tg, out float tb, out float ta);
                    if (mat.CutoutAlpha) { if (ta < 0.5f) continue; albedo = Lin(new Vector3(tr, tg, tb)) * (mat.UseVertexColor ? Lin(vc) : Vector3.One) * Lin(mat.Base); alpha = 1; }
                    else { albedo *= Lin(new Vector3(tr, tg, tb)); if (mat.Alpha >= 0.999f) alpha = 1; else alpha *= ta; }
                }
                Vector3 col;
                var toCam = CamPos - wp; float dist = toCam.Length(); var V = toCam / Math.Max(1e-5f, dist);
                if (mat.Unlit) col = albedo + mat.Emissive;
                else
                {
                    float ndl = Math.Max(0, Vector3.Dot(nrm, -SunDir));
                    float sh = 1;
                    if (Shadows && ndl > 0) sh = ShadowAt(wp + nrm * 0.15f, 0.0015f);
                    var amb = Vector3.Lerp(AmbientGround, AmbientSky, nrm.Y * 0.5f + 0.5f);
                    col = albedo * (amb + SunColor * ndl * sh);
                    if (mat.Specular > 0)
                    {
                        var h = Vector3.Normalize(V - SunDir);
                        float sp = (float)Math.Pow(Math.Max(0, Vector3.Dot(nrm, h)), mat.Gloss) * mat.Specular * sh;
                        col += SunColor * sp * 0.5f;
                        if (mat.Sky) { var r = Vector3.Reflect(-V, nrm); float fres = 0.04f + 0.96f * (float)Math.Pow(1 - Math.Max(0, Vector3.Dot(nrm, V)), 4); col = Vector3.Lerp(col, Sky(r), Math.Min(1f, fres * 1.2f + 0.12f)); }
                    }
                    col += mat.Emissive;
                }
                float fog = 1 - (float)Math.Exp(-dist * FogDensity);
                var fogCol = Vector3.Lerp(SkyHorizon, SkyZenith, 0.15f);
                col = Vector3.Lerp(col, fogCol, fog);
                int ci = di * 3;
                if (alpha >= 0.999f) { _col[ci] = col.X; _col[ci + 1] = col.Y; _col[ci + 2] = col.Z; _depth[di] = z; }
                else { _col[ci] = _col[ci] * (1 - alpha) + col.X * alpha; _col[ci + 1] = _col[ci + 1] * (1 - alpha) + col.Y * alpha; _col[ci + 2] = _col[ci + 2] * (1 - alpha) + col.Z * alpha; }
            }
    }

    static Vector3 Lin(Vector3 c) => new Vector3(Lin1(c.X), Lin1(c.Y), Lin1(c.Z));
    static float Lin1(float v) => v <= 0 ? 0 : v * v * (0.2f * v + 0.8f);    // ≈ v^2.2 (sRGB texture → linear)

    static void SampleTex(Image t, float u, float v, out float r, out float g, out float b, out float a)
    {
        float fx = u * t.W - 0.5f, fy = v * t.H - 0.5f; int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy); float tx = fx - x0, ty = fy - y0;
        t.Get(x0, y0, out float r00, out float g00, out float b00, out float a00); t.Get(x0 + 1, y0, out float r10, out float g10, out float b10, out float a10);
        t.Get(x0, y0 + 1, out float r01, out float g01, out float b01, out float a01); t.Get(x0 + 1, y0 + 1, out float r11, out float g11, out float b11, out float a11);
        r = Bil(r00, r10, r01, r11, tx, ty); g = Bil(g00, g10, g01, g11, tx, ty); b = Bil(b00, b10, b01, b11, tx, ty); a = Bil(a00, a10, a01, a11, tx, ty);
    }
    static float Bil(float a, float b, float c, float d, float tx, float ty) => (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;

    // ------------------------------------------------------------------ output
    public bool PostFx = true; public float AoRadius = 1.4f, AoStrength = 0.85f, BloomStrength = 0.22f, Exposure = 1f;

    /// <summary>Screen-space ambient occlusion + bloom on the linear HDR buffer (stand-ins for the URP SSAO / Bloom of the real game).</summary>
    void PostProcess()
    {
        float focal = PH * 0.5f / (float)Math.Tan(FovY * Math.PI / 360);
        var ao = new float[PW * PH];
        var pts = new List<Vector2>(); var rnd = new Random(5);
        for (int i = 0; i < 14; i++) { double a = i * 2.39996 + 0.3; double r = Math.Sqrt((i + 0.5) / 14.0); pts.Add(new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r))); }
        Parallel.For(0, PH, y =>
        {
            for (int x = 0; x < PW; x++)
            {
                int di = y * PW + x; float z = _depth[di]; if (z >= 1e8f || z > 400f) { ao[di] = 1; continue; }
                float rad = AoRadius / z * focal; if (rad < 1.5f) { ao[di] = 1; continue; }
                rad = Math.Min(rad, PH * 0.12f);
                float ang = ((x * 7 + y * 13) % 17) * 0.37f, ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
                float occ = 0;
                foreach (var p in pts)
                {
                    float ox = (p.X * ca - p.Y * sa) * rad, oy = (p.X * sa + p.Y * ca) * rad;
                    int sx = x + (int)ox, sy = y + (int)oy; if ((uint)sx >= (uint)PW || (uint)sy >= (uint)PH) continue;
                    float sz = _depth[sy * PW + sx]; float diff = z - sz;
                    if (diff > 0.12f && diff < AoRadius * 2.2f) occ += 1f - Math.Min(1f, diff / (AoRadius * 2.2f)) * 0.5f;
                }
                ao[di] = 1f - AoStrength * Math.Min(1f, occ / pts.Count * 1.9f);
            }
        });
        // light blur of the AO to hide the noise
        var ao2 = new float[PW * PH];
        Parallel.For(0, PH, y => { for (int x = 0; x < PW; x++) { float sum = 0; int n = 0; for (int dy = -2; dy <= 2; dy += 2) for (int dx = -2; dx <= 2; dx += 2) { int sx = x + dx, sy = y + dy; if ((uint)sx >= (uint)PW || (uint)sy >= (uint)PH) continue; sum += ao[sy * PW + sx]; n++; } ao2[y * PW + x] = sum / n; } });
        Parallel.For(0, PH, y => { for (int x = 0; x < PW; x++) { int di = y * PW + x; float k = ao2[di]; int ci = di * 3; _col[ci] *= k; _col[ci + 1] *= k; _col[ci + 2] *= k; } });

        // bloom: bright pass → separable box blur at 1/4 resolution → add
        int bw = Math.Max(1, PW / 4), bh = Math.Max(1, PH / 4); var bl = new float[bw * bh * 3];
        Parallel.For(0, bh, y => { for (int x = 0; x < bw; x++) { float r = 0, g = 0, b = 0; for (int dy = 0; dy < 4; dy++) for (int dx = 0; dx < 4; dx++) { int i = ((y * 4 + dy) * PW + x * 4 + dx) * 3; float lr = _col[i], lg = _col[i + 1], lb = _col[i + 2]; float lum = 0.3f * lr + 0.59f * lg + 0.11f * lb; float k = Math.Max(0, lum - 1.3f) / Math.Max(lum, 1e-4f); r += lr * k; g += lg * k; b += lb * k; } int o = (y * bw + x) * 3; bl[o] = r / 16; bl[o + 1] = g / 16; bl[o + 2] = b / 16; } });
        int R = Math.Max(2, bw / 50); var tmp = new float[bl.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            Parallel.For(0, bh, y => { for (int x = 0; x < bw; x++) { float r = 0, g = 0, b = 0; int n = 0; for (int d = -R; d <= R; d++) { int sx = Math.Min(bw - 1, Math.Max(0, x + d)); int i = (y * bw + sx) * 3; r += bl[i]; g += bl[i + 1]; b += bl[i + 2]; n++; } int o = (y * bw + x) * 3; tmp[o] = r / n; tmp[o + 1] = g / n; tmp[o + 2] = b / n; } });
            Parallel.For(0, bh, y => { for (int x = 0; x < bw; x++) { float r = 0, g = 0, b = 0; int n = 0; for (int d = -R; d <= R; d++) { int sy = Math.Min(bh - 1, Math.Max(0, y + d)); int i = (sy * bw + x) * 3; r += tmp[i]; g += tmp[i + 1]; b += tmp[i + 2]; n++; } int o = (y * bw + x) * 3; bl[o] = r / n; bl[o + 1] = g / n; bl[o + 2] = b / n; } });
        }
        Parallel.For(0, PH, y => { for (int x = 0; x < PW; x++) { int o = (Math.Min(bh - 1, y / 4) * bw + Math.Min(bw - 1, x / 4)) * 3; int ci = (y * PW + x) * 3; _col[ci] += bl[o] * BloomStrength; _col[ci + 1] += bl[o + 1] * BloomStrength; _col[ci + 2] += bl[o + 2] * BloomStrength; } });
    }

    public void SavePng(string path, float exposure = 1f)
    {
        if (PostFx) PostProcess();
        exposure *= Exposure;
        var rgb = new byte[W * H * 3];
        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                float r = 0, g = 0, b = 0;
                for (int sy = 0; sy < SS; sy++) for (int sx = 0; sx < SS; sx++) { int i = ((y * SS + sy) * PW + x * SS + sx) * 3; r += _col[i]; g += _col[i + 1]; b += _col[i + 2]; }
                float k = 1f / (SS * SS);
                rgb[(y * W + x) * 3] = Tone(r * k * exposure); rgb[(y * W + x) * 3 + 1] = Tone(g * k * exposure); rgb[(y * W + x) * 3 + 2] = Tone(b * k * exposure);
            }
        });
        PngWriter.Write(path, W, H, rgb);
    }

    static byte Tone(float v)
    {
        // ACES filmic (Narkowicz) then sRGB
        v = Math.Max(0, v) * 0.85f;
        v = (v * (2.51f * v + 0.03f)) / (v * (2.43f * v + 0.59f) + 0.14f);
        v = Math.Max(0, Math.Min(1, v));
        v = v <= 0.0031308f ? v * 12.92f : 1.055f * (float)Math.Pow(v, 1 / 2.4f) - 0.055f;
        return (byte)Math.Min(255, v * 255 + 0.5f);
    }
}

public static class PngWriter
{
    public static void Write(string path, int w, int h, byte[] rgb)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13]; Be(ihdr, 0, w); Be(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true))
        {
            var row = new byte[w * 3 + 1];
            for (int y = 0; y < h; y++) { row[0] = 0; Buffer.BlockCopy(rgb, y * w * 3, row, 1, w * 3); z.Write(row, 0, row.Length); }
        }
        Chunk(fs, "IDAT", raw.ToArray()); Chunk(fs, "IEND", new byte[0]);
    }
    static void Be(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint crc = Crc(t, data); var c = new byte[4]; Be(c, 0, (int)crc); s.Write(c);
    }
    static uint[] _table;
    static uint Crc(byte[] a, byte[] b)
    {
        if (_table == null) { _table = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; _table[n] = c; } }
        uint crc = 0xFFFFFFFF;
        foreach (var x in a) crc = _table[(crc ^ x) & 0xFF] ^ (crc >> 8);
        foreach (var x in b) crc = _table[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
