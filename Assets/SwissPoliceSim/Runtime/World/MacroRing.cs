using System.Collections.Generic;
using SwissPoliceSim.Common;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.Rendering;
using UnityEngine;

namespace SwissPoliceSim.World
{
    /// <summary>Horizon terrain: 8 km macro blocks within <see cref="radiusM"/> of the viewer, built lazily from the resident macro data and kept below the real tiles.</summary>
    public sealed class MacroRing : MonoBehaviour
    {
        public Transform viewer; public float radiusM = 36000f;
        readonly Dictionary<long, GameObject> _blocks = new Dictionary<long, GameObject>();
        float _next;

        void Update()
        {
            if (viewer == null || !WorldData.Ready || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1.0f;
            var p = WorldOrigin.ToLv95(viewer.position); int cbx = (int)System.Math.Floor(p.E / MacroBlock.BlockSize), cby = (int)System.Math.Floor(p.N / MacroBlock.BlockSize);
            int r = (int)System.Math.Ceiling(radiusM / MacroBlock.BlockSize);
            var want = new HashSet<long>();
            for (int bx = cbx - r; bx <= cbx + r; bx++)
                for (int by = cby - r; by <= cby + r; by++)
                {
                    var b = WorldData.MacroAt(bx, by); if (b == null) continue; long key = ((long)bx << 32) ^ (uint)by; want.Add(key);
                    if (!_blocks.ContainsKey(key)) _blocks[key] = Create(b);
                }
            var drop = new List<long>(); foreach (var kv in _blocks) if (!want.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var k in drop) { Destroy(_blocks[k].GetComponent<MeshFilter>().sharedMesh); Destroy(_blocks[k]); _blocks.Remove(k); }
        }

        GameObject Create(MacroBlock b)
        {
            var go = new GameObject($"Macro {b.BX},{b.BY}"); go.transform.position = WorldOrigin.ToUnity(b.MinE, b.MinN, 0);
            go.AddComponent<MeshFilter>().sharedMesh = MeshConv.ToMesh(TerrainMesher.BuildMacro(b), go.name, true);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = MaterialLibrary.Terrain(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
