using System;
using System.Collections.Generic;
using SwissPoliceSim.Common;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwissPoliceSim.World
{
    /// <summary>
    /// Draws the trees of all loaded tiles with GPU instancing. Per refresh (5 Hz) the trees around the viewer are sorted into LOD buckets
    /// (leaf-card crown &lt; 130 m, light crown &lt; 420 m, lumps &lt; 1100 m); positions are computed from the double-precision LV95 coordinates so the floating origin is transparent.
    /// </summary>
    public sealed class TreeRenderer : MonoBehaviour
    {
        public TileStreamer streamer; public Transform viewer;
        public float lod0 = 130f, lod1 = 420f, lod2 = 1100f; public int maxPerBucket = 1023;

        sealed class Bucket { public Mesh Mesh; public Matrix4x4[] M = new Matrix4x4[1023]; public int Count; }
        readonly Dictionary<(byte kind, int variant, int lod, int ratio), Bucket> _buckets = new Dictionary<(byte, int, int, int), Bucket>();
        readonly Dictionary<(byte, int, int, int), Mesh> _meshes = new Dictionary<(byte, int, int, int), Mesh>();
        readonly List<Bucket> _active = new List<Bucket>(); readonly List<(Bucket b, Matrix4x4[] m, int n)> _overflow = new List<(Bucket, Matrix4x4[], int)>();
        Material[] _mats; float _next; Material _trunkInst, _foliageInst;

        Mesh MeshFor(byte kind, int variant, int lod, int ratio)
        {
            var key = (kind, variant, lod, ratio);
            if (!_meshes.TryGetValue(key, out var m)) { m = MeshConv.ToMesh(TreeMesher.Build(kind, variant, lod, ratio), $"tree{kind}_{variant}_{lod}_{ratio}"); _meshes[key] = m; }
            return m;
        }

        void Update()
        {
            if (streamer == null || viewer == null) return;
            if (_mats == null) _mats = MaterialLibrary.Trees();
            if (Time.unscaledTime >= _next) { _next = Time.unscaledTime + 0.2f; Refresh(); }
            foreach (var b in _active) Draw(b);
            foreach (var o in _overflow) DrawRaw(o.b.Mesh, o.m, o.n);
        }

        void Refresh()
        {
            foreach (var b in _buckets.Values) b.Count = 0; _active.Clear(); _overflow.Clear();
            var vp = viewer.position; var origin = WorldOrigin.Frame;
            double ve = origin.OriginE + vp.x, vn = origin.OriginN + vp.z;
            foreach (var t in streamer.Loaded())
            {
                // cheap tile rejection: distance from viewer to tile square
                double cx = t.OriginE + 500, cy = t.OriginN + 500; double dd = Math.Max(Math.Abs(cx - ve), Math.Abs(cy - vn)) - 500; if (dd > lod2) continue;
                var terrain = t.Content.Terrain;
                foreach (var tr in t.Content.Trees)
                {
                    double dx = tr.E - ve, dz = tr.N - vn; double d2 = dx * dx + dz * dz; if (d2 > lod2 * lod2) continue;
                    float d = (float)Math.Sqrt(d2); int lod = d < lod0 ? 0 : d < lod1 ? 1 : 2;
                    if (lod == 2 && ((int)(tr.E * 3 + tr.N * 7) & 1) == 0) continue;                                 // thin the far lumps
                    int variant = (int)((long)(tr.E * 7 + tr.N * 13) & 3) % 3; int ratio = TreeMesher.RatioBucket(tr.Radius, tr.Height);
                    var key = (tr.Kind, variant, lod, ratio);
                    if (!_buckets.TryGetValue(key, out var b)) { b = new Bucket { Mesh = MeshFor(tr.Kind, variant, lod, ratio) }; _buckets[key] = b; }
                    float gy = terrain != null ? terrain.SampleLocal(tr.E - t.OriginE, tr.N - t.OriginN) : 0f;
                    var pos = new Vector3((float)(tr.E - origin.OriginE), gy - 0.15f, (float)(tr.N - origin.OriginN));
                    float yaw = (float)(((tr.E * 31 + tr.N * 17) % 360.0));
                    if (b.Count == 0) _active.Add(b);
                    if (b.Count < b.M.Length) b.M[b.Count++] = Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one * tr.Height);
                }
            }
        }

        void Draw(Bucket b) { if (b.Count > 0) DrawRaw(b.Mesh, b.M, b.Count); }

        void DrawRaw(Mesh m, Matrix4x4[] mats, int n)
        {
            for (int s = 0; s < m.subMeshCount; s++)
                Graphics.DrawMeshInstanced(m, s, _mats[Math.Min(s, _mats.Length - 1)], mats, n, null, s == 1 ? ShadowCastingMode.On : ShadowCastingMode.On, true);
        }
    }
}
