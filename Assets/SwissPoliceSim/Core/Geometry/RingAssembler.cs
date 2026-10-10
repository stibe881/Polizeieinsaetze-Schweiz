using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Geometry
{
    /// <summary>Joins OSM way segments (lists of node ids) end-to-end into closed rings (multipolygon assembly).</summary>
    public static class RingAssembler
    {
        /// <summary>Returns closed rings as node-id lists (first == last). Open leftovers are reported in <paramref name="unclosed"/>.</summary>
        public static List<List<long>> Assemble(IList<List<long>> ways, out int unclosed)
        {
            var rings = new List<List<long>>();
            var pool = new List<List<long>>();
            foreach (var w in ways) if (w.Count >= 2) pool.Add(new List<long>(w));
            unclosed = 0;
            while (pool.Count > 0)
            {
                var cur = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1);
                bool progress = true;
                while (cur[0] != cur[cur.Count - 1] && progress)
                {
                    progress = false;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        var w = pool[i];
                        long head = cur[0], tail = cur[cur.Count - 1];
                        if (w[0] == tail) { cur.AddRange(w.GetRange(1, w.Count - 1)); }
                        else if (w[w.Count - 1] == tail) { var r = new List<long>(w); r.Reverse(); cur.AddRange(r.GetRange(1, r.Count - 1)); }
                        else if (w[w.Count - 1] == head) { var r = new List<long>(w.GetRange(0, w.Count - 1)); r.AddRange(cur); cur = r; }
                        else if (w[0] == head) { var r = new List<long>(w); r.Reverse(); r.RemoveAt(r.Count - 1); r.AddRange(cur); cur = r; }
                        else continue;
                        pool.RemoveAt(i); progress = true; break;
                    }
                }
                if (cur.Count >= 4 && cur[0] == cur[cur.Count - 1]) rings.Add(cur); else unclosed++;
            }
            return rings;
        }
    }
}
