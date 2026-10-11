using System;
using System.Collections.Generic;
using System.Linq;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Core.Traffic;
using SwissServer.Sim;

namespace SwissServer.Net
{
    /// <summary>Builds the periodic state JSON and the binary entity snapshot.</summary>
    public static class Snapshotter
    {
        public static int PropsVersion(Game g) { int h = g.Props.Count; foreach (var p in g.Props) h = h * 31 + p.Id; return h; }
        public static int ActorsVersion(Game g) { int h = g.Actors.Count; foreach (var a in g.Actors) h = h * 31 + a.Id; return h; }
        public static string PropModel(SProp p)
        {
            if (p.Kind == "ems") return "car:ems"; if (p.Kind == "tow") return "car:tow";
            if (p.Kind == "car") { int idx = 0; for (int i = 0; i < Game.Palette.Length; i++) if (Game.Palette[i][0] == p.Colour[0] && Game.Palette[i][1] == p.Colour[1] && Game.Palette[i][2] == p.Colour[2]) idx = i; return $"car:{Math.Max(0, Math.Min(4, p.Body))}:{idx}"; }
            return "prop:" + p.Kind;
        }

        public static byte[] Snapshot(Game g)
        {
            var b = new Bin(); b.U8(Msg.Snapshot).F64(g.Traffic != null ? g.Traffic.Time : 0);
            var cars = g.Traffic != null ? g.Traffic.Vehicles : new List<TrafficVehicle>(); b.I32(cars.Count);
            foreach (var v in cars)
            {
                int flags = (v.BrakeLights ? 1 : 0) | (v.Hazard ? 2 : 0) | (v.Indicator < 0 ? 4 : 0) | (v.Indicator > 0 ? 8 : 0) | (v.Mode == DriverMode.Stopped ? 32 : 0) | (v.Mode == DriverMode.Crashed ? 64 : 0);
                b.I32(v.Id).F64(v.E).F64(v.N).F32(g.HeightAt(v.E, v.N)).F32((float)(Math.Atan2(v.DirE, v.DirN) * 180 / Math.PI)).F32(v.V).U16(flags);
            }
            var peds = g.Peds != null ? g.Peds.Peds : new List<Pedestrian>(); b.I32(peds.Count);
            foreach (var p in peds) b.I32(p.Id).F64(p.E).F64(p.N).F32(g.HeightAt(p.E, p.N)).F32((float)(Math.Atan2(p.DirE, p.DirN) * 180 / Math.PI)).U8((int)p.State).F32(p.State == PedState.Walking || p.State == PedState.Crossing ? p.Speed : 0f);
            b.I32(g.Actors.Count); foreach (var a in g.Actors) b.I32(a.Id).F64(a.E).F64(a.N).F32(a.H).F32(a.Fleeing ? (float)(Math.Atan2(a.DirE, a.DirN) * 180 / Math.PI) : a.Yaw).U8(a.Pose).U8((a.Escaped ? 1 : 0) | (a.Cuffed ? 2 : 0) | (a.Fleeing ? 4 : 0));
            return b.ToArray();
        }

        public static object State(Game g)
        {
            var c = g.Career; var inc = g.Active ?? g.Offered; var sun = SunInfo(g); var w = WeatherModel.Sample(g.PE, g.PN, g.PH, g.Now, g.Seed);
            var nav = g.Nav;
            return new
            {
                t = "state", paused = g.Paused, llm = new { enabled = g.Llm.Enabled, ready = g.Llm.Ready, status = g.Llm.Status }, player = new { e = g.PE, n = g.PN, h = g.PH, speed = g.PSpeedKmh, inVehicle = g.PInVehicle, yaw = g.PYaw, vE = g.VE, vN = g.VN, vYaw = g.VYaw, fuel = g.PFuel, damage = g.PDamage }, time = g.Now.ToString("s"), clock = g.Now.ToString("ddd dd.MM. HH:mm"), hour = g.Hour, timeScale = g.Clock.TimeScale,
                corps = g.Corps?.Name, rank = c != null && g.Corps != null ? c.RankName(g.Corps) : "", place = g.PlaceName, street = g.StreetName, limit = g.SpeedLimitKmh, urban = g.UrbanHere,
                jurisdiction = new { own = g.Jurisdiction.InOwnTerritory, unknown = g.Jurisdiction.Kind == JurisdictionKind.Unknown, text = g.Corps != null ? g.Jurisdiction.Describe(g.Corps) : "" },
                sun = new { el = sun.el, az = sun.az, daylight = SunModel.Daylight(sun.el) },
                weather = new { temp = w.TemperatureC, rain = w.IsRain ? w.PrecipitationMmH : 0f, snow = w.IsSnow ? w.PrecipitationMmH : 0f, cloud = w.CloudCover, fog = w.FogDensity, vis = w.Visibility, wind = w.WindMs, thunder = w.Thunder, friction = w.RoadFriction },
                career = c == null ? null : new { xp = c.Xp, nextXp = c.RankIndex + 1 < CareerState.XpForRank.Length ? CareerState.XpForRank[c.RankIndex + 1] : c.Xp, rankIndex = c.RankIndex, legality = c.Legality, prof = c.Professionalism, deesc = c.DeEscalation, reporting = c.Reporting, community = c.Community, complaints = c.ComplaintsOpen, investigations = c.InvestigationsOpen, disciplinary = c.DisciplinaryCount, fines = c.FinesIssued, arrests = c.ArrestsMade, suspended = c.Suspended, dismissed = c.Dismissed, suspendedLeft = c.Suspended ? Math.Max(0, c.SuspendedUntil - g.Clock.ElapsedGameSeconds) : 0, canPromote = c.CanPromote, overall = c.Overall },
                offered = g.Offered == null ? null : new { id = g.Offered.Id, type = g.Offered.TypeLabel, urgency = g.Offered.Urgency, text = g.Offered.RadioText, address = g.Offered.Address, muni = g.Offered.Municipality, left = g.OfferSecondsLeft },
                active = g.Active == null ? null : new { id = g.Active.Id, type = g.Active.TypeLabel, state = g.Active.State.ToString(), urgency = g.Active.Urgency, address = g.Active.Address, muni = g.Active.Municipality, e = g.Active.E, n = g.Active.N, ems = g.Active.AmbulanceCalled, fire = g.Active.FireCalled, backup = g.Active.BackupCalled, emsEnRoute = g.EmsEnRoute, towEnRoute = g.TowEnRoute, roles = g.Active.Scenario.Roles.Select((r, i) => new { i, role = r.Role, name = r.Person != null ? r.Person.FullName : "–" }).ToArray() },
                nav = new { active = nav.Route != null, computing = nav.Computing, dest = nav.DestinationName, remaining = nav.RemainingM, total = nav.TotalM, eta = nav.EtaSeconds, off = nav.OffRouteM, next = nav.Next != null ? new { text = nav.Next.Text, dist = nav.DistToNext, kind = nav.Next.Kind.ToString() } : null },
                radar = new { on = g.RadarActive, last = g.LastMeasure == null ? null : new { plate = g.LastMeasure.Plate, kmh = g.LastMeasure.Kmh, limit = g.LastMeasure.Limit, violation = g.LastMeasure.Fine != null && g.LastMeasure.Fine.IsViolation, text = g.LastMeasure.Fine?.Text, age = g.Clock.ElapsedGameSeconds - g.LastMeasure.Time } },
                pursuit = new { active = g.Pursuit.Active, risk = g.RiskScore, kmh = g.Pursuit.TargetKmh, heli = g.Pursuit.Helicopter, roadblockReady = g.RoadblockReady },
                encounter = g.EncounterJson(), stopped = g.StoppedVehicle != null ? (int?)g.StoppedVehicle.Id : null
            };
        }

        static (double el, double az) SunInfo(Game g) { var p = g.PLv95; var w = SwissCoordinateConverter.ToWgs84(p); SunModel.Position(g.Now, w.Lat, w.Lon, out double el, out double az); return (el, az); }

        public static object Career(Game g)
        {
            var c = g.Career; return new { t = "career", rank = c.RankName(g.Corps), corps = g.Corps.Name, history = g.ConsequenceLog.AsEnumerable().Reverse().Take(80).ToArray(), cases = g.Cases.Count };
        }

        public static object[] Search(WorldStore w, string text)
        {
            if (w.Pois == null || string.IsNullOrWhiteSpace(text) || text.Length < 2) return new object[0];
            return w.Pois.Search(text, 30).Select(p => (object)new { name = p.Name, kind = p.Kind.ToString(), e = p.E, n = p.N }).ToArray();
        }

        public static object[] PoisNear(WorldStore w, double e, double n, double r)
        {
            if (w.Pois == null) return new object[0]; return w.Pois.Within(e, n, Math.Min(r, 6000)).Take(400).Select(p => (object)new { name = p.Name, kind = p.Kind.ToString(), e = p.E, n = p.N }).ToArray();
        }
    }
}
