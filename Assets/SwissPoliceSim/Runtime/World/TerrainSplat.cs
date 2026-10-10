using SwissPoliceSim.Core.Terrain;
using UnityEngine;

namespace SwissPoliceSim.World
{
    /// <summary>Computes the 3-layer alpha map (grass / rock / snow) from slope and altitude. Pure math, thread safe.</summary>
    public static class TerrainSplat
    {
        public const int Layers = 3;

        public static float[,,] Compute(TerrainTileData d, int res)
        {
            var a = new float[res, res, Layers];
            float step = 1000f / res;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float px = (x + 0.5f) * step, pz = (y + 0.5f) * step;
                    float h = d.SampleLocal(px, pz);
                    float dx = d.SampleLocal(px + step, pz) - d.SampleLocal(px - step, pz);
                    float dz = d.SampleLocal(px, pz + step) - d.SampleLocal(px, pz - step);
                    float slopeDeg = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * step)) * Mathf.Rad2Deg;

                    float snow = Mathf.Clamp01((h - 2300f) / 200f);
                    float rock = Mathf.Clamp01((slopeDeg - 28f) / 12f);
                    float grass = Mathf.Clamp01(1f - rock) * (1f - snow);
                    rock *= (1f - snow);
                    float sum = grass + rock + snow;
                    if (sum < 1e-4f) { grass = 1; sum = 1; }
                    a[y, x, 0] = grass / sum; a[y, x, 1] = rock / sum; a[y, x, 2] = snow / sum;
                }
            return a;
        }
    }
}
