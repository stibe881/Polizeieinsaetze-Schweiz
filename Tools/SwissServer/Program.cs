using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Meshing;
using SwissServer.Net;
using SwissServer.Sim;

namespace SwissServer
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            string world = Arg(args, "--world") ?? FindUp("Assets/StreamingAssets/SwissWorld"), law = Arg(args, "--law") ?? FindUp("Assets/StreamingAssets/Law"); int port = int.Parse(Arg(args, "--port") ?? "47800");
            if (world == null || !Directory.Exists(world)) { Console.Error.WriteLine("World data not found. Use --world <dir> (the folder with manifest.json and tiles/)."); return 2; }
            Console.WriteLine($"Loading world from {world} …"); var store = WorldStore.Load(world); Console.WriteLine($"{store.TileCount} tiles, {store.Macro.Count} macro blocks, {store.Pois?.All.Count ?? 0} POIs.");
            if (args.Contains("--scan")) return Scan(store);
            var hub = new Hub(store, law);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = FindUp("Tools/SwissServer/wwwroot") ?? Path.Combine(AppContext.BaseDirectory, "wwwroot") });
            builder.Logging.SetMinimumLevel(LogLevel.Warning); builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var app = builder.Build(); app.UseWebSockets(); app.UseDefaultFiles(); app.UseStaticFiles();
            app.Map("/ws", async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync(); var conn = hub.Add(ws); var pump = conn.PumpAsync(ctx.RequestAborted); var buf = new byte[1 << 16];
                try
                {
                    while (ws.State == WebSocketState.Open)
                    {
                        using var ms = new MemoryStream(); WebSocketReceiveResult r;
                        do { r = await ws.ReceiveAsync(buf, ctx.RequestAborted); if (r.MessageType == WebSocketMessageType.Close) break; ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                        if (r.MessageType == WebSocketMessageType.Close) break; if (r.MessageType != WebSocketMessageType.Text) continue;
                        try { using var doc = JsonDocument.Parse(ms.ToArray()); hub.Enqueue(conn, doc.RootElement.Clone()); } catch (JsonException) { }
                    }
                }
                catch (Exception) { }
                finally { hub.Remove(conn); }
            });
            app.MapGet("/map/tile/{x:int}/{y:int}.png", (int x, int y) =>
            {
                var id = new TileId(x, y); var c = store.Content(id); if (c == null) return Results.NotFound();
                var t = TileMeshBuilder.Build(store.Source.TryRead(id, out var bytes) ? bytes : null, false); var px = MapRasterizer.RenderTile(t, 512); return Results.File(Png.EncodeRgba(512, 512, px), "image/png");
            });
            app.MapGet("/map/macro/{x:int}/{y:int}.png", (int x, int y) =>
            {
                if (!store.Macro.TryGetValue(WorldStore.Key(x, y), out var b)) return Results.NotFound(); return Results.File(Png.EncodeRgba(256, 256, MapRasterizer.RenderMacro(b, 256)), "image/png");
            });
            app.MapGet("/api/status", () => Results.Json(new { world = store.Manifest.name, tiles = store.TileCount, playing = hub.Game.Playing }));
            var cts = new CancellationTokenSource(); Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
            var loop = hub.RunAsync(cts.Token); Console.WriteLine($"Swiss Police Simulator server on http://127.0.0.1:{port}  (WebSocket /ws, web UI /)");
            try { await app.RunAsync(cts.Token); } catch (OperationCanceledException) { }
            return 0;
        }

        /// <summary>Diagnose: baut jedes Tile in jeder Qualitaet und zaehlt nicht-endliche Vertexwerte je Mesh-Art.</summary>
        static int Scan(WorldStore w)
        {
            int bad = 0;
            foreach (var id in w.Source.Tiles)
            {
                if (!w.Source.TryRead(id, out var bytes)) continue;
                foreach (int q in new[] { 1, 2, 3 })
                {
                    var c = TileMeshBuilder.Build(bytes, true, q == 1, double.NaN, double.NaN, q == 1 ? TileWire.DetailRadius : 0);
                    int stride = q == 1 ? 1 : q == 2 ? 2 : 4; MeshData terrain = null;
                    if (c.Terrain != null) { int ms = (c.Terrain.Resolution - 1) / 32; stride = Math.Min(stride, Math.Max(1, ms)); terrain = TerrainMesher.Build(c.Terrain, c.LandUse, stride); }
                    var meshes = new (string, MeshData)[] { ("Terrain", terrain), ("Roads", c.RoadMesh), ("BuildingsFar", c.BuildingMesh), ("BuildingsOutside", c.BuildingMeshOutside), ("BuildingsDetail", c.BuildingMeshDetail), ("Water", c.WaterMesh), ("Furniture", c.FurnitureMesh), ("Rail", c.RailMesh) };
                    foreach (var (name, m) in meshes)
                    {
                        if (m == null) continue; int n = 0; foreach (var f in m.Positions) if (!float.IsFinite(f) || Math.Abs(f) > 1e5f) n++;
                        int nn = 0; foreach (var f in m.Normals) if (!float.IsFinite(f)) nn++;
                        if (n > 0 || nn > 0) { bad++; Console.WriteLine($"tile {id.X}/{id.Y} q{q} {name}: {n} bad positions, {nn} bad normals of {m.VertexCount} vertices"); }
                    }
                }
            }
            Console.WriteLine(bad == 0 ? "scan: no bad vertices" : $"scan: {bad} bad meshes"); return bad == 0 ? 0 : 1;
        }

        static string Arg(string[] a, string k) { for (int i = 0; i + 1 < a.Length; i++) if (a[i] == k) return a[i + 1]; return null; }
        static string FindUp(string rel) { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null) { var p = Path.Combine(d.FullName, rel); if (Directory.Exists(p)) return p; d = d.Parent; } var cwd = Path.Combine(Directory.GetCurrentDirectory(), rel); return Directory.Exists(cwd) ? cwd : null; }
    }
}
