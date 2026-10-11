using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Traffic;
using SwissServer.Sim;

namespace SwissServer.Net
{
    /// <summary>Model messages (cars, humans, trees) keyed by a string the client asks for. Layout: [5][key][meta json][n meshes][mesh…].</summary>
    public static class ModelWire
    {
        public static string PedKey(Pedestrian p) => $"human:ped:{(p.Female ? 1 : 0)}{(p.Child ? 1 : 0)}{(p.Elderly ? 1 : 0)}:{p.Shirt}:{p.Trousers}:{p.Skin}:{p.Hair}:{(int)Math.Round(p.Height * 100)}:{p.Seed % 1000}";
        public static string ActorKey(SActor a)
        {
            var c = a.Person; bool female = c.Gender == SwissPoliceSim.Core.Npc.Gender.Female; var r = new Random(c.Seed);
            byte shirt = (byte)r.Next(12), trousers = (byte)r.Next(8), skin = (byte)r.Next(6), hair = (byte)r.Next(7); float h = a.Child ? 1.28f : (female ? 1.62f : 1.76f) + (float)r.NextDouble() * 0.14f;
            return $"human:ped:{(female ? 1 : 0)}{(a.Child ? 1 : 0)}{(c.Age > 68 ? 1 : 0)}:{shirt}:{trousers}:{skin}:{hair}:{(int)Math.Round(h * 100)}:{c.Seed % 1000}";
        }

        public static byte[] Build(string key)
        {
            var p = key.Split(':');
            try
            {
                if (p[0] == "car") return Car(key, p);
                if (p[0] == "human") return Human(key, p);
                if (p[0] == "tree") { var m = TreeMesher.Build(byte.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4])); return Wrap(key, new { kind = "tree" }, new[] { m }); }
            }
            catch (Exception ex) { Console.WriteLine("model " + key + ": " + ex.Message); }
            return null;
        }

        static byte[] Wrap(string key, object meta, IList<MeshData> meshes)
        {
            var b = new Bin(); b.U8(Msg.Model).Str(key).Str(JsonSerializer.Serialize(meta, Hub.Json)).U8(meshes.Count); foreach (var m in meshes) b.Mesh(m); return b.ToArray();
        }

        static byte[] Car(string key, string[] p)
        {
            CarLivery liv; BodyKind kind;
            if (p[1] == "ems") { kind = BodyKind.Van; liv = new CarLivery { Body = new byte[] { 238, 238, 232 }, Stripe = new byte[] { 220, 40, 30 }, LightBar = true, Plate = "BE 144144" }; }
            else if (p[1] == "tow") { kind = BodyKind.Van; liv = new CarLivery { Body = new byte[] { 230, 190, 40 }, LightBar = false, Plate = "BE 55 118" }; }
            else if (p[1] == "police") { kind = BodyKind.Wagon; liv = CarLivery.PatrolCar(); }
            else { kind = (BodyKind)int.Parse(p[1]); var c = Game.VehicleColour(int.Parse(p[2])); liv = CarLivery.Civilian(c[0], c[1], c[2], "BE 000000"); }
            var m = CarMesher.Build(kind, liv);
            return Wrap(key, new { kind = "car", wheels = m.WheelCentres, wheelRadius = m.WheelRadius, wheelWidth = m.WheelWidth, length = m.Length, width = m.Width, height = m.Height, driverEye = m.DriverEye, hoodCam = m.HoodCam, submeshes = new { paint = CarMesher.SubPaint, glass = CarMesher.SubGlass, trim = CarMesher.SubTrim, lampFront = CarMesher.SubLampFront, lampRear = CarMesher.SubLampRear, indicator = CarMesher.SubIndicator, blueL = CarMesher.SubBlueL, blueR = CarMesher.SubBlueR, plate = CarMesher.SubPlate, decal = CarMesher.SubDecal, interior = CarMesher.SubInterior, chrome = CarMesher.SubChrome } }, new[] { m.Body, m.Wheel });
        }

        static byte[] Human(string key, string[] p)
        {
            HumanSpec spec;
            if (p[1] == "officer") spec = HumanSpec.Officer(p[2] == "f", 7, p.Length > 3 && p[3] == "1");
            else
            {
                string f = p[2]; spec = HumanSpec.FromPed(f[0] == '1', f[1] == '1', f[2] == '1', byte.Parse(p[3]), byte.Parse(p[4]), byte.Parse(p[5]), byte.Parse(p[6]), int.Parse(p[7]) / 100f, int.Parse(p[8]));
                if (!(f[0] == '1' && false)) spec.Shoes = new byte[] { 40, 40, 44 };
            }
            var model = HumanMesher.Build(spec, 0);
            var parts = model.Parts.Select((pt, i) => new { name = pt.Name, parent = pt.Parent, pivot = pt.Pivot }).ToArray();
            return Wrap(key, new { kind = "human", height = model.Height, hip = model.HipHeight, parts, submeshes = new { body = HumanMesher.SubBody, leather = HumanMesher.SubLeather, patch = HumanMesher.SubPatch } }, model.Parts.Select(pt => pt.Mesh).ToList());
        }
    }
}
