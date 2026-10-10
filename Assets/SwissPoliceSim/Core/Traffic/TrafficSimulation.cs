using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Traffic
{
    public enum DriverMode : byte { Driving, YieldToEmergency, PulloverRequested, Pulling, Stopped, Fleeing, Crashed, Parked }

    public enum ViolationType : byte
    {
        None, Speeding, RedLight, StopSign, FailureToYield, Tailgating, DangerousDriving, NoSeatbelt, PhoneUse, ImpairedDriving, DefectiveVehicle, WrongWay
    }

    public sealed class TrafficVehicle
    {
        public int Id;
        public int Link; public float S, V, A;
        public float Lat, LatTarget;               // lateral offset to the right of the link centre line (m)
        public int Lane;
        public float Length = 4.5f, Width = 1.8f;
        public int BodyKind;                       // maps to CarMesher.BodyKind (0 wagon,1 hatch,2 sedan,3 suv,4 van) ; 10 truck, 11 bus, 12 motorcycle
        public int ColorIndex; public string Plate = "";
        public float V0Factor = 1f, Accel = 1.6f, Decel = 2.2f, THeadway = 1.4f, MinGap = 2f;
        public int NextLink = -1; public int RouteHops;
        public readonly List<int> Plan = new List<int>();   // upcoming links after the current one (lookahead for signals / yields on short links)
        public DriverMode Mode;
        public bool External;                      // e.g. the player's car: not simulated, only observed
        public float WaitTime, TimeInMode, StoppedTime;
        public sbyte Indicator;                    // -1 left, 0 none, +1 right
        public bool Hazard, BrakeLights;
        public Driver Driver = new Driver();
        // violation bookkeeping
        public float SpeedingTime, TailgateTime; public ViolationType LastViolation; public float LastViolationTime = -999;
        public float EmergencyLastSeen = -999;
        public string Why = "";                   // debug: what currently limits the vehicle
        public double E, N; public float DirE, DirN;       // world pose (updated every step)
        public float SpeedKmh => V * 3.6f;
    }

    /// <summary>Personality + intentional/accidental violations of one driver (data that the police check later reveals).</summary>
    public sealed class Driver
    {
        public string Name = "", FirstName = "", Birthdate = "", Address = "", Language = "de";
        public int Age = 40; public bool Speeder, RunsRedLights, NoSeatbelt, PhoneUser, Impaired, NoLicense, LicenseSuspended, UninsuredOrExpired, VehicleDefect, Wanted;
        public float BloodAlcohol;                 // per mille (‰)
        public float Nervousness, Aggression, Honesty = 0.7f;
        public string Occupation = "";
    }

    public struct TrafficEvent
    {
        public ViolationType Type; public int VehicleId; public double E, N; public float Time;
        public float Value, Limit;                 // e.g. measured speed / limit (km/h)
        public int Link;
    }

    public sealed class TrafficParams
    {
        public int MaxVehicles = 260; public float SpawnRadius = 650, DespawnRadius = 900, MinSpawnDistance = 160;
        public float DensityFactor = 1f; public int Seed = 7;
        public float SpeedTolerance(float limitKmh) => limitKmh <= 100 ? 3f : limitKmh * 0.03f;     // Swiss measurement tolerance (Messtoleranz) as used for radar enforcement
    }

    /// <summary>Microscopic traffic simulation on the link network (IDM car following, signals, priorities, emergency vehicles).</summary>
    public sealed class TrafficSimulation
    {
        public readonly TrafficNetwork Net; public readonly TrafficParams P;
        public readonly List<TrafficVehicle> Vehicles = new List<TrafficVehicle>();
        public float Time;
        public event Action<TrafficEvent> Violation;
        public event Action<TrafficVehicle, TrafficVehicle> Crash;
        readonly Random _rnd; int _nextId = 1;
        readonly List<List<TrafficVehicle>> _byLink;
        readonly List<float> _tmp = new List<float>();
        readonly List<TrafficVehicle> _remove = new List<TrafficVehicle>();
        public event Action<TrafficVehicle> Despawned;

        // emergency vehicle (player) state for the "Rettungsgasse"
        public bool SirenActive; public double SirenE, SirenN; public float SirenDirE, SirenDirN;

        public TrafficSimulation(TrafficNetwork net, TrafficParams p = null)
        {
            Net = net; P = p ?? new TrafficParams(); _rnd = new Random(P.Seed);
            _byLink = new List<List<TrafficVehicle>>(); for (int i = 0; i < net.Links.Count; i++) _byLink.Add(new List<TrafficVehicle>());
        }

        public Random Rng => _rnd;

        // ------------------------------------------------------------------ spawning
        public TrafficVehicle SpawnAt(int link, float s, float speedFactor = 1f, int lane = 0)
        {
            var l = Net.Links[link];
            var v = new TrafficVehicle { Id = _nextId++, Link = link, S = Math.Min(s, l.Length - 1), Lane = Math.Min(lane, l.Lanes - 1) };
            RandomizeDriver(v);
            v.V0Factor *= speedFactor;
            v.V = Math.Min(l.LimitMs * v.V0Factor, l.SpeedLimitAt(v.S)) * 0.9f;
            v.Lat = v.LatTarget = LaneOffset(l, v.Lane);
            v.NextLink = ChooseNext(v, l);
            Vehicles.Add(v); Pose(v);
            return v;
        }

        void RandomizeDriver(TrafficVehicle v)
        {
            double r = _rnd.NextDouble();
            var d = v.Driver;
            d.Speeder = r < 0.07; d.RunsRedLights = _rnd.NextDouble() < 0.012; d.NoSeatbelt = _rnd.NextDouble() < 0.025; d.PhoneUser = _rnd.NextDouble() < 0.03;
            d.Impaired = _rnd.NextDouble() < 0.004; d.BloodAlcohol = d.Impaired ? 0.5f + (float)_rnd.NextDouble() * 1.5f : (_rnd.NextDouble() < 0.02 ? 0.1f + (float)_rnd.NextDouble() * 0.3f : 0f);
            d.NoLicense = _rnd.NextDouble() < 0.004; d.LicenseSuspended = !d.NoLicense && _rnd.NextDouble() < 0.003; d.UninsuredOrExpired = _rnd.NextDouble() < 0.01; d.VehicleDefect = _rnd.NextDouble() < 0.03;
            d.Wanted = _rnd.NextDouble() < 0.002;
            d.Nervousness = (float)_rnd.NextDouble(); d.Aggression = d.Speeder ? 0.6f + (float)_rnd.NextDouble() * 0.4f : (float)_rnd.NextDouble() * 0.6f; d.Honesty = 0.35f + (float)_rnd.NextDouble() * 0.6f;
            v.V0Factor = d.Speeder ? 1.22f + (float)_rnd.NextDouble() * 0.28f : (r < 0.2 ? 0.88f + (float)_rnd.NextDouble() * 0.06f : 0.96f + (float)_rnd.NextDouble() * 0.12f);
            v.THeadway = d.Aggression > 0.7f ? 0.9f : 1.3f + (float)_rnd.NextDouble() * 0.6f;
            v.Accel = 1.2f + (float)_rnd.NextDouble() * 1.2f; v.Decel = 2.0f + (float)_rnd.NextDouble() * 0.8f;
            double k = _rnd.NextDouble();
            v.BodyKind = k < 0.30 ? 1 : k < 0.52 ? 0 : k < 0.72 ? 2 : k < 0.92 ? 3 : 4;
            v.Length = v.BodyKind == 1 ? 4.05f : v.BodyKind == 2 ? 4.75f : v.BodyKind == 3 ? 4.65f : v.BodyKind == 4 ? 5.2f : 4.7f;
            v.ColorIndex = _rnd.Next(12);
            v.Plate = Plates.Random(_rnd);
        }

        /// <summary>Lateral offset of a lane centre to the right of the link's centre line (lane 0 = rightmost lane).</summary>
        float LaneOffset(TLink l, int lane)
        {
            if (!l.OneWay) return l.LaneWidth * (l.Lanes - 0.5f - lane);     // two-way: the centre line separates the directions
            return ((l.Lanes - 1) * 0.5f - lane) * l.LaneWidth;
        }

        /// <summary>Makes sure v.Plan holds the next three links (first = v.NextLink).</summary>
        void EnsurePlan(TrafficVehicle v)
        {
            if (v.NextLink >= 0 && (v.Plan.Count == 0 || v.Plan[0] != v.NextLink)) { v.Plan.Clear(); v.Plan.Add(v.NextLink); }
            while (v.Plan.Count > 0 && v.Plan.Count < 3)
            {
                var last = Net.Links[v.Plan[v.Plan.Count - 1]]; int n = ChooseNext(v, last);
                if (n < 0) break; v.Plan.Add(n);
            }
        }

        int ChooseNext(TrafficVehicle v, TLink l)
        {
            if (l.Out.Count == 0) return -1;
            if (l.Out.Count == 1) return l.Out[0];
            double total = 0; var w = new double[l.Out.Count];
            for (int i = 0; i < l.Out.Count; i++)
            {
                var o = Net.Links[l.Out[i]];
                double x = 1;
                if (o.WayId == l.WayId) x *= 3.0;
                if (o.Class == l.Class) x *= 1.8;
                if (o.Priority >= l.Priority) x *= 1.4;
                if (o.Class == RoadClassGuard.Service) x *= 0.3;
                // prefer roads that keep going
                if (o.Out.Count == 0) x *= 0.15;
                w[i] = x; total += x;
            }
            double r = _rnd.NextDouble() * total;
            for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0) return l.Out[i]; }
            return l.Out[0];
        }

        static class RoadClassGuard { public const SwissPoliceSim.Core.Roads.RoadClass Service = SwissPoliceSim.Core.Roads.RoadClass.Service; }

        /// <summary>Maintains roughly <see cref="TrafficParams.MaxVehicles"/> × density vehicles around observers, spawning outside their sight.</summary>
        public void Maintain(IList<(double e, double n)> observers, float hourOfDay)
        {
            float demand = TrafficDemand.Factor(hourOfDay) * P.DensityFactor;
            int target = (int)(P.MaxVehicles * demand);
            // despawn
            for (int i = Vehicles.Count - 1; i >= 0; i--)
            {
                var v = Vehicles[i]; if (v.External || v.Mode == DriverMode.Stopped || v.Mode == DriverMode.Pulling || v.Mode == DriverMode.PulloverRequested || v.Mode == DriverMode.Fleeing) continue;
                double dmin = double.MaxValue; foreach (var o in observers) dmin = Math.Min(dmin, Math.Sqrt((v.E - o.e) * (v.E - o.e) + (v.N - o.n) * (v.N - o.n)));
                // deadlocked / long-stuck vehicles are removed once nobody can see them (gridlock cleanup that real traffic solves by itself)
                if (v.WaitTime > 60f && v.Mode == DriverMode.Driving && dmin > 120f) { Remove(i); continue; }
                if (dmin > P.DespawnRadius || (Vehicles.Count > target + 20 && dmin > P.MinSpawnDistance * 1.5f && v.Mode == DriverMode.Driving)) Remove(i);
            }
            int guard = 0;
            while (Vehicles.Count < target && guard++ < 25)
            {
                var o = observers[_rnd.Next(observers.Count)];
                double ang = _rnd.NextDouble() * Math.PI * 2, rad = P.MinSpawnDistance + _rnd.NextDouble() * (P.SpawnRadius - P.MinSpawnDistance);
                int link = Net.Nearest(o.e + Math.Cos(ang) * rad, o.n + Math.Sin(ang) * rad, 120, out float s);
                if (link < 0) continue;
                var l = Net.Links[link];
                if (l.Tunnel && l.Length < 30) continue;
                if (!AcceptSpawn(l, hourOfDay)) continue;
                // keep clear of other vehicles and of observers' sight
                if (!SpaceFree(link, s, 14)) continue;
                bool close = false; l.PointAt(s, out double pe, out double pn, out _, out _);
                foreach (var ob in observers) if ((pe - ob.e) * (pe - ob.e) + (pn - ob.n) * (pn - ob.n) < P.MinSpawnDistance * P.MinSpawnDistance) close = true;
                if (close) continue;
                SpawnAt(link, s, 1f, _rnd.Next(l.Lanes));
            }
        }

        bool AcceptSpawn(TLink l, float hour)
        {
            double w;
            switch (l.Class)
            {
                case Roads.RoadClass.Motorway: case Roads.RoadClass.Trunk: w = 0.9; break;
                case Roads.RoadClass.Primary: case Roads.RoadClass.Secondary: w = 0.8; break;
                case Roads.RoadClass.Tertiary: w = 0.55; break;
                case Roads.RoadClass.Residential: case Roads.RoadClass.Unclassified: w = 0.35; break;
                case Roads.RoadClass.Service: case Roads.RoadClass.LivingStreet: w = 0.08; break;
                default: w = 0.4; break;
            }
            return _rnd.NextDouble() < w;
        }

        public bool SpaceFree(int link, float s, float clearance)
        {
            foreach (var o in _byLink[link]) if (Math.Abs(o.S - s) < clearance + o.Length) return false;
            return true;
        }

        void Remove(int index) { Vehicles.RemoveAt(index); }
        public void RemoveVehicle(TrafficVehicle v) { Vehicles.Remove(v); }

        // ------------------------------------------------------------------ simulation step
        public void Step(float dt)
        {
            Time += dt;
            RebuildLists();

            foreach (var v in Vehicles)
            {
                if (v.External) { Pose(v); continue; }
                v.TimeInMode += dt;
                if (v.Mode == DriverMode.Crashed && v.TimeInMode > 40f) { _remove.Add(v); continue; }   // wreck is cleared (towed away)
                UpdateMode(v, dt);
                float a = Acceleration(v);
                v.A = a;
                float nv = Math.Max(0, v.V + a * dt);
                float ds = (v.V + nv) * 0.5f * dt;
                v.V = nv; v.S += ds;
                v.BrakeLights = a < -0.8f || (v.V < 0.3f);
                if (v.V < 0.3f) { v.WaitTime += dt; v.StoppedTime += dt; } else { v.WaitTime = 0; v.StoppedTime = 0; }
                // lateral smoothing
                float dl = v.LatTarget - v.Lat; float maxStep = 1.1f * dt; v.Lat += Math.Max(-maxStep, Math.Min(maxStep, dl));
                Advance(v);
                Pose(v);
                CheckViolations(v, dt);
            }
            DetectCrashes();
            if (_remove.Count > 0) { foreach (var r in _remove) { Vehicles.Remove(r); Despawned?.Invoke(r); } _remove.Clear(); }
        }

        void Advance(TrafficVehicle v)
        {
            var l = Net.Links[v.Link];
            int guard = 0;
            while (v.S >= l.Length && guard++ < 4)
            {
                int nl = v.NextLink;
                if (nl < 0 || nl >= Net.Links.Count)
                {
                    if (v.Mode == DriverMode.Driving || v.Mode == DriverMode.YieldToEmergency) _remove.Add(v);   // dead end / map edge: the driver leaves the simulated area
                    v.S = l.Length; v.V = 0; return;
                }
                // crossing the node: record signal state for red-light detection
                if (l.SignalAtEnd && v.Mode != DriverMode.Crashed)
                {
                    var st = SignalModel.State(Time, l.SignalOffset, l.SignalGroup);
                    if (SignalModel.MustStop(st) && SignalModel.MustStop(SignalModel.State(Time - 1.0, l.SignalOffset, l.SignalGroup)) && v.V > 2f) Emit(ViolationType.RedLight, v, v.SpeedKmh, 0);
                }
                v.S -= l.Length; v.Link = nl; l = Net.Links[nl];
                v.Lane = Math.Min(v.Lane, l.Lanes - 1);
                v.LatTarget = v.Mode == DriverMode.Pulling || v.Mode == DriverMode.YieldToEmergency ? EdgeOffset(l) : LaneOffset(l, v.Lane);
                if (v.Plan.Count > 0 && v.Plan[0] == nl) v.Plan.RemoveAt(0);
                v.NextLink = v.Plan.Count > 0 ? v.Plan[0] : ChooseNext(v, l); v.RouteHops++;
                v.Indicator = TurnIndicator(l, v.NextLink >= 0 ? Net.Links[v.NextLink] : null);
            }
        }

        sbyte TurnIndicator(TLink a, TLink b)
        {
            if (b == null) return 0;
            double ah = Math.Atan2(a.E[a.E.Length - 1] - a.E[a.E.Length - 2], a.N[a.N.Length - 1] - a.N[a.N.Length - 2]);
            double bh = Math.Atan2(b.E[1] - b.E[0], b.N[1] - b.N[0]);
            double d = bh - ah; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
            if (d > 0.5) return 1; if (d < -0.5) return -1; return 0;       // heading measured from north towards east: positive = right turn
        }

        float EdgeOffset(TLink l) => l.OneWay ? (l.Lanes * l.LaneWidth) * 0.5f - 0.9f : l.LaneWidth * 1.0f - 0.55f;

        void Pose(TrafficVehicle v)
        {
            var l = Net.Links[v.Link];
            l.PointAt(v.S, out double e, out double n, out float dx, out float dn);
            v.DirE = dx; v.DirN = dn;
            v.E = e + dn * v.Lat; v.N = n - dx * v.Lat;      // right of travel = (dn, -dx)
        }

        // ------------------------------------------------------------------ mode handling
        void UpdateMode(TrafficVehicle v, float dt)
        {
            var l = Net.Links[v.Link];
            bool siren = SirenActive && DistTo(v, SirenE, SirenN) < 70 && Ahead(v, SirenE, SirenN, SirenDirE, SirenDirN);
            if (siren && (v.Mode == DriverMode.Driving || v.Mode == DriverMode.YieldToEmergency)) { v.Mode = DriverMode.YieldToEmergency; v.EmergencyLastSeen = Time; v.LatTarget = EdgeOffset(l); }
            else if (v.Mode == DriverMode.YieldToEmergency && Time - v.EmergencyLastSeen > 5f) { v.Mode = DriverMode.Driving; v.LatTarget = LaneOffset(l, v.Lane); }
            if (v.Mode == DriverMode.PulloverRequested) { v.LatTarget = EdgeOffset(l); v.Indicator = 1; v.Mode = DriverMode.Pulling; v.TimeInMode = 0; }
            if (v.Mode == DriverMode.Pulling && v.V < 0.2f && v.TimeInMode > 2f) { v.Mode = DriverMode.Stopped; v.Hazard = true; v.Indicator = 0; }
        }

        static double DistTo(TrafficVehicle v, double e, double n) => Math.Sqrt((v.E - e) * (v.E - e) + (v.N - n) * (v.N - n));
        static bool Ahead(TrafficVehicle v, double e, double n, float dirE, float dirN)
        {
            // siren vehicle is BEHIND v when v lies in front of it, in a narrow cone along its heading
            double rx = v.E - e, ry = v.N - n, d = Math.Sqrt(rx * rx + ry * ry); if (d < 1) return true;
            double cos = (rx * dirE + ry * dirN) / d;
            double vcos = v.DirE * dirE + v.DirN * dirN;           // same direction of travel
            return cos > 0.5 && vcos > 0.3;
        }

        /// <summary>Police asks a driver to stop (Anhaltezeichen). The driver pulls over to the right edge and halts.</summary>
        public bool RequestStop(TrafficVehicle v)
        {
            if (v == null || v.Mode == DriverMode.Stopped || v.Mode == DriverMode.Crashed) return false;
            if (v.Driver.Wanted && _rnd.NextDouble() < 0.5 || v.Driver.Impaired && _rnd.NextDouble() < 0.3 || v.Driver.NoLicense && _rnd.NextDouble() < 0.25)
            { v.Mode = DriverMode.Fleeing; v.V0Factor = 1.8f; v.TimeInMode = 0; return true; }
            v.Mode = DriverMode.PulloverRequested; return true;
        }

        public void ReleaseStop(TrafficVehicle v) { if (v.Mode == DriverMode.Stopped || v.Mode == DriverMode.Pulling) { v.Mode = DriverMode.Driving; v.Hazard = false; v.Indicator = -1; v.LatTarget = LaneOffset(Net.Links[v.Link], v.Lane); v.V0Factor = Math.Min(v.V0Factor, 1f); } }

        // ------------------------------------------------------------------ car following
        float Acceleration(TrafficVehicle v)
        {
            var l = Net.Links[v.Link];
            if (v.Mode == DriverMode.Stopped || v.Mode == DriverMode.Crashed || v.Mode == DriverMode.Parked) return -Math.Max(8f, v.V * 4f);
            float limit = l.LimitMs * v.V0Factor;
            if (v.Mode == DriverMode.YieldToEmergency) limit = Math.Min(limit, 7f);
            if (v.Mode == DriverMode.Pulling) limit = Math.Min(limit, v.StoppedTime > 0 ? 0 : 5.5f);
            // curvature profile with lookahead
            float look = v.V * v.V / (2 * 2.2f) + 12;
            float vc = ProfileAhead(l, v.S, look);
            float v0 = Math.Min(limit, vc * (v.Mode == DriverMode.Fleeing ? 1.3f : 1.0f));
            if (v.Driver.Impaired) v0 *= 0.85f;

            float gap = float.MaxValue, vLead = 0; bool hasLead = false;
            LeaderOnRoute(v, l, ref gap, ref vLead, ref hasLead);
            v.Why = hasLead ? "leader" : "free";

            // obstacles at upcoming nodes (signals / stop / yield / blocked exit), looking through short links
            if (v.Mode == DriverMode.Pulling && v.TimeInMode > 1.5f) { gap = Math.Min(gap, 1.0f + Math.Max(0, 15f - v.TimeInMode * 3f)); vLead = 0; hasLead = true; }
            EnsurePlan(v);
            {
                float dist = l.Length - v.S; var cur = l; int k = 0;
                while (k < 3 && dist < 140)
                {
                    int nextId = k < v.Plan.Count ? v.Plan[k] : -1; var nxt = nextId >= 0 ? Net.Links[nextId] : null;
                    bool must = false;
                    if (cur.Out.Count == 0) must = v.Mode != DriverMode.Driving && dist < 80;
                    else if (nxt != null && (v.Mode != DriverMode.Fleeing || v.V < 4f)) must = MustHoldAtNode(v, cur, nxt, dist);
                    if (must) { float g = Math.Max(0.1f, dist - 1.5f); if (g < gap) { gap = g; vLead = 0; hasLead = true; v.Why = "hold@" + k + (cur.SignalAtEnd ? ":signal" : cur.StopAtEnd ? ":stop" : ":yield/merge"); } break; }
                    if (nxt == null) break;
                    dist += nxt.Length; cur = nxt; k++;
                }
            }

            float A = v.Accel, B = v.Decel, T = v.THeadway, s0 = v.MinGap;
            float a = A * (1 - (float)Math.Pow(v.V / Math.Max(0.5f, v0), 4));
            if (hasLead)
            {
                float dv = v.V - vLead;
                float sStar = s0 + Math.Max(0, v.V * T + v.V * dv / (2 * (float)Math.Sqrt(A * B)));
                a = A * (1 - (float)Math.Pow(v.V / Math.Max(0.5f, v0), 4) - (sStar / Math.Max(0.1f, gap)) * (sStar / Math.Max(0.1f, gap)));
            }
            return Math.Max(-9f, Math.Min(A * 1.3f, a));
        }

        float ProfileAhead(TLink l, float s, float look)
        {
            float min = float.MaxValue; int i = 0; while (i < l.Cum.Length - 1 && l.Cum[i + 1] < s) i++;
            for (; i < l.Cum.Length && l.Cum[i] <= s + look; i++) min = Math.Min(min, l.VMax[i]);
            if (min == float.MaxValue) min = l.VMax[l.VMax.Length - 1];
            return min;
        }

        void LeaderOnRoute(TrafficVehicle v, TLink l, ref float gap, ref float vLead, ref bool hasLead)
        {
            // same link
            var list = _byLink[v.Link];
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i]; if (o == v || o.S <= v.S) continue;
                if (o.Lane != v.Lane && l.Lanes > 1 && Math.Abs(o.Lat - v.Lat) > 1.6f) continue;
                float g = o.S - v.S - o.Length;
                if (g < gap) { gap = g; vLead = o.V; hasLead = true; }
            }
            if (hasLead) return;
            // next link(s) within 120 m
            float dist = l.Length - v.S; int nl = v.NextLink; int hops = 0;
            while (nl >= 0 && dist < 120 && hops++ < 3)
            {
                var nlk = Net.Links[nl]; var nlist = _byLink[nl];
                for (int i = nlist.Count - 1; i >= 0; i--)
                {
                    var o = nlist[i]; if (o == v) continue;
                    float g = dist + o.S - o.Length;
                    if (g < gap) { gap = g; vLead = o.V; hasLead = true; }
                    break;      // last in the list is the rearmost... list sorted descending by S: rearmost is the closest to the entry
                }
                if (hasLead) return;
                dist += nlk.Length; nl = nlk.Out.Count > 0 ? nlk.Out[0] : -1;
            }
        }

        /// <summary>Should this vehicle hold at the end of <paramref name="l"/> (red light, yield, stop sign, blocked exit)? <paramref name="toEnd"/> = distance from the vehicle to that node.</summary>
        bool MustHoldAtNode(TrafficVehicle v, TLink l, TLink next, float toEnd)
        {
            bool onThis = l.Id == v.Link;
            if (l.SignalAtEnd)
            {
                var st = SignalModel.State(Time, l.SignalOffset, l.SignalGroup);
                if (st == SignalState.Red || st == SignalState.RedAmber) return !(v.Driver.RunsRedLights && v.V > 6f && toEnd < 22f);
                if (st == SignalState.Amber)
                {
                    float brake = v.V * v.V / (2 * 3.0f);
                    return toEnd - 2f > brake && !v.Driver.RunsRedLights;
                }
                return v.WaitTime <= 20f && ExitBlocked(v, l, next, toEnd);
            }
            if (l.StopAtEnd)
            {
                if (onThis && toEnd < 4f && v.V < 0.4f) return v.WaitTime < 1.2f;
                return true;
            }
            if (toEnd > 30f) return false;
            if (v.WaitTime > 22f) return false;                     // gridlock breaker: after a long wait drivers push in (courtesy / police-less real life)
            if (YieldRequired(v, l, next, toEnd)) return v.WaitTime <= 9f;
            if (l.GiveWayAtEnd && v.V > 3f && toEnd < 12f && YieldRequired(v, l, next, toEnd)) return true;
            return ExitBlocked(v, l, next, toEnd);
        }

        bool ExitBlocked(TrafficVehicle v, TLink l, TLink next, float toEnd)
        {
            if (next == null) return false;
            var list = _byLink[next.Id];
            for (int i = 0; i < list.Count; i++) if (list[i] != v && list[i].S < v.Length + 5f && list[i].V < 2f) return true;
            // merge claim: another vehicle that reaches the shared exit link first (or, on a tie, has the lower id) keeps it
            float myEta = toEnd / Math.Max(1.5f, v.V);
            foreach (int inId in next.In)
            {
                if (inId == l.Id) continue;
                var o = Net.Links[inId];
                foreach (var ov in _byLink[inId])
                {
                    float oToEnd = o.Length - ov.S;
                    if (oToEnd > 14f || ov.Mode == DriverMode.Crashed) continue;
                    float eta = oToEnd / Math.Max(1.5f, ov.V);
                    if (eta < myEta - 0.3f || (Math.Abs(eta - myEta) <= 0.3f && ov.Id < v.Id)) return true;
                }
            }
            return false;
        }

        /// <summary>Swiss rules: roundabout traffic first, then higher road priority, then right-before-left (Rechtsvortritt).</summary>
        bool YieldRequired(TrafficVehicle v, TLink l, TLink next, float toEnd)
        {
            if (next == null) return false;
            foreach (int idx in next.In)
            {
                if (idx == l.Id) continue;
                var o = Net.Links[idx]; var list = _byLink[idx];
                if (list.Count == 0) continue;
                int rel = ComparePriority(l, o);
                if (rel > 0) continue;                                   // I have priority
                if (rel == 0 && !OnMyRight(l, o)) continue;              // equal: only vehicles from my right have priority
                foreach (var ov in list)
                {
                    float oToEnd = o.Length - ov.S;
                    if (oToEnd < 0) continue;
                    float tOther = ov.V > 0.5f ? oToEnd / ov.V : (oToEnd < 6f ? 0f : 99f);
                    if (oToEnd < 28f && tOther < 4.5f) return true;
                }
            }
            return false;
        }

        // >0: l has priority over o; <0: o has priority; 0: equal
        static int ComparePriority(TLink l, TLink o)
        {
            if (o.Roundabout && !l.Roundabout) return -1;
            if (l.Roundabout && !o.Roundabout) return 1;
            if (l.Priority > o.Priority) return 1; if (l.Priority < o.Priority) return -1;
            return 0;
        }

        static bool OnMyRight(TLink me, TLink other)
        {
            int n1 = me.E.Length, n2 = other.E.Length;
            double mh = Math.Atan2(me.E[n1 - 1] - me.E[n1 - 2], me.N[n1 - 1] - me.N[n1 - 2]);
            double oh = Math.Atan2(other.E[n2 - 1] - other.E[n2 - 2], other.N[n2 - 1] - other.N[n2 - 2]);
            // the other vehicle comes from the right when its heading is my heading rotated by -90° → its travel direction points to my left
            double d = oh - mh; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
            return d < -0.35 && d > -2.8;
        }

        // ------------------------------------------------------------------ violations
        void CheckViolations(TrafficVehicle v, float dt)
        {
            var l = Net.Links[v.Link];
            float kmh = v.V * 3.6f, tol = P.SpeedTolerance(l.LimitKmh);
            if (v.Mode == DriverMode.Driving || v.Mode == DriverMode.Fleeing)
            {
                if (kmh > l.LimitKmh + tol + 2f) { v.SpeedingTime += dt; if (v.SpeedingTime > 2.5f) { Emit(ViolationType.Speeding, v, kmh, l.LimitKmh); v.SpeedingTime = -8f; } }
                else if (v.SpeedingTime > 0) v.SpeedingTime = 0; else if (v.SpeedingTime < 0) v.SpeedingTime = Math.Min(0, v.SpeedingTime + dt);
            }
        }

        void Emit(ViolationType t, TrafficVehicle v, float value, float limit)
        {
            if (Time - v.LastViolationTime < 4f && v.LastViolation == t) return;
            v.LastViolation = t; v.LastViolationTime = Time;
            Violation?.Invoke(new TrafficEvent { Type = t, VehicleId = v.Id, E = v.E, N = v.N, Time = Time, Value = value, Limit = limit, Link = v.Link });
        }

        void RebuildLists()
        {
            for (int i = 0; i < _byLink.Count; i++) _byLink[i].Clear();
            foreach (var v in Vehicles) _byLink[v.Link].Add(v);
            for (int i = 0; i < _byLink.Count; i++) if (_byLink[i].Count > 1) _byLink[i].Sort((a, b) => b.S.CompareTo(a.S));
        }

        void DetectCrashes()
        {
            RebuildLists();
            for (int i = 0; i < _byLink.Count; i++)
            {
                var list = _byLink[i];
                for (int k = 0; k + 1 < list.Count; k++)
                {
                    var a = list[k]; var b = list[k + 1];     // a ahead of b
                    float gap = a.S - b.S - a.Length;
                    if (gap < -0.3f && Math.Abs(a.Lat - b.Lat) < 1.4f && (b.V - a.V) > 1.5f && !a.External && !b.External && a.Mode != DriverMode.Crashed)
                    {
                        a.Mode = DriverMode.Crashed; b.Mode = DriverMode.Crashed; a.Hazard = b.Hazard = true;
                        Crash?.Invoke(a, b);
                    }
                }
            }
        }

        public TrafficVehicle Find(int id) { foreach (var v in Vehicles) if (v.Id == id) return v; return null; }

        public TrafficVehicle Nearest(double e, double n, float maxDist, Func<TrafficVehicle, bool> filter = null)
        {
            TrafficVehicle best = null; double bd = maxDist * maxDist;
            foreach (var v in Vehicles) { if (v.External) continue; if (filter != null && !filter(v)) continue; double d = (v.E - e) * (v.E - e) + (v.N - n) * (v.N - n); if (d < bd) { bd = d; best = v; } }
            return best;
        }
    }

    public static class TrafficDemand
    {
        static readonly float[] Hour = { 0.10f, 0.07f, 0.05f, 0.05f, 0.10f, 0.28f, 0.60f, 0.95f, 1.0f, 0.75f, 0.62f, 0.66f, 0.78f, 0.72f, 0.66f, 0.72f, 0.88f, 1.0f, 0.92f, 0.66f, 0.46f, 0.34f, 0.24f, 0.15f };
        public static float Factor(float hour) { int h = (int)Math.Floor(hour) % 24; int h2 = (h + 1) % 24; float t = hour - (float)Math.Floor(hour); return Hour[h] * (1 - t) + Hour[h2] * t; }
    }

    public static class Plates
    {
        static readonly string[] Cantons = { "ZH", "BE", "LU", "UR", "SZ", "OW", "NW", "GL", "ZG", "FR", "SO", "BS", "BL", "SH", "AR", "AI", "SG", "GR", "AG", "TG", "TI", "VD", "VS", "NE", "GE", "JU" };
        static readonly double[] Weight = { 14, 12, 5, .4, 1.6, .5, .6, .5, 1.5, 3.4, 3.2, 2.2, 3.4, 1, .7, .2, 6, 2.2, 8, 3.3, 4.3, 9, 4, 2, 5.3, 1 };
        public static string Random(Random r)
        {
            double tot = 0; foreach (var w in Weight) tot += w; double x = r.NextDouble() * tot; int i = 0;
            for (; i < Weight.Length - 1; i++) { x -= Weight[i]; if (x <= 0) break; }
            return Cantons[i] + " " + r.Next(100000, 999999);
        }
    }
}
