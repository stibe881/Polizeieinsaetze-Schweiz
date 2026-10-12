using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Traffic;

namespace SwissServer.Sim
{
    public sealed class Measurement
    {
        public int VehicleId; public string Plate = ""; public float Kmh; public int Limit; public SpeedZone Zone; public double Time; public float Distance; public SpeedingFine Fine; public string Street = ""; public string Colour = "";
    }

    /// <summary>Contact with a person, radar/stop enforcement, pursuit and navigation (ported from the former Unity controllers).</summary>
    public sealed partial class Game
    {
        public Encounter Encounter; public Pedestrian EncPed; public SActor EncActor; public TrafficVehicle EncVehicle; public bool RadarActive; public Measurement LastMeasure; public TrafficVehicle StoppedVehicle; double _stopRequestTime;
        public PursuitManager Pursuit = new PursuitManager(); public TrafficVehicle PursuitTarget; public float RiskScore; public bool RoadblockReady = true; float _assessTimer;
        public Nav Nav = new Nav(); public readonly List<Measurement> History = new List<Measurement>();
        static readonly string[] Colours = { "weiss", "schwarz", "silber", "grau", "blau", "rot", "grün", "braun", "beige", "orange", "gelb", "bordeaux" };
        public bool InEncounter => Encounter != null; public bool EncounterWindowRequested;

        // ------------------------------------------------------------------ radar & stop
        public void Radar(bool on) { RadarActive = on; Info(on ? "Lasermessgerät eingeschaltet. Linksklick = Messung." : "Lasermessgerät ausgeschaltet."); }

        public void Measure(int vehicleId, float distance)
        {
            var v = Traffic?.Find(vehicleId); if (v == null || !RadarActive) return; var rnd = _rnd;
            int limit = 50; var zone = SpeedZone.Urban; string street = "";
            if (W.NearestRoad(v.E, v.N, 20, out var ri)) { limit = ri.SpeedLimitKmh; street = ri.Name; zone = ri.Way.Class == SwissPoliceSim.Core.Roads.RoadClass.Motorway ? SpeedZone.Motorway : ri.Way.Motorroad ? SpeedZone.Motorroad : ri.Urban ? SpeedZone.Urban : SpeedZone.Rural; }
            float kmh = v.V * 3.6f + (float)(rnd.NextDouble() - 0.5) * 0.8f; int measured = (int)Math.Round(kmh);
            var m = new Measurement { VehicleId = v.Id, Plate = v.Plate, Kmh = measured, Limit = limit, Zone = zone, Time = Clock.ElapsedGameSeconds, Distance = distance, Street = street, Colour = Colours[((v.ColorIndex % 12) + 12) % 12], Fine = SpeedingRules.Assess(Obv, measured, limit, zone) };
            LastMeasure = m; History.Add(m); if (History.Count > 30) History.RemoveAt(0);
            if (m.Fine.IsViolation) Note("warn", $"Messung: {measured} km/h bei erlaubten {limit} km/h → nach Toleranz +{m.Fine.ExcessKmh} km/h" + (m.Fine.IsOrdnungsbusse ? $" · Ordnungsbusse {m.Fine.Amount} CHF" : " · über dem Ordnungsbussenbereich: Verzeigung") + ". [G] = Anhalten", 10f);
            else Info($"Messung: {measured} km/h bei erlaubten {limit} km/h – keine Überschreitung (Toleranz {SpeedingRules.Tolerance(measured)} km/h).");
            Hub.Broadcast(new { t = "measure", vehicleId = v.Id, plate = v.Plate, kmh = measured, limit, violation = m.Fine.IsViolation, excess = m.Fine.ExcessKmh, fine = m.Fine.IsOrdnungsbusse ? m.Fine.Amount : 0, street, colour = m.Colour });
        }

        public void SignalStop(int vehicleId)
        {
            if (InEncounter || Traffic == null) return; var target = vehicleId > 0 ? Traffic.Find(vehicleId) : (LastMeasure != null && Clock.ElapsedGameSeconds - LastMeasure.Time < 40 ? Traffic.Find(LastMeasure.VehicleId) : null);
            if (target == null) { Info("Kein Fahrzeug im Sichtbereich zum Anhalten."); return; }
            bool signal = !PInVehicle || PBlue || PSiren; if (!signal) { Warn("Zum Anhalten Blaulicht (B) oder Sirene (Q) einschalten."); return; }
            bool caught = LastMeasure != null && LastMeasure.VehicleId == target.Id && LastMeasure.Fine != null && LastMeasure.Fine.IsViolation;
            var ctx = new LegalContext { Suspicion = caught ? Suspicion.CaughtInAct : Suspicion.Routine, InOwnTerritory = Jurisdiction.InOwnTerritory, CooperationAgreement = Jurisdiction.CooperationAgreement };
            var a = LegalSystem.Assess(OfficerAct.StopVehicle, ctx); if (!a.Lawful) Judge(a);
            if (Traffic.RequestStop(target))
            {
                _stopRequestTime = Clock.ElapsedGameSeconds; StoppedVehicle = target; Info("Anhaltezeichen gegeben.");
                if (target.Mode == DriverMode.Fleeing) { Warn("Das Fahrzeug beschleunigt – es flüchtet!"); BeginPursuit(target, OffenceClass.Uebertretung, Suspicion.Concrete); StoppedVehicle = null; }
            }
            else Warn("Das Fahrzeug reagiert nicht.");
        }

        void TickEncounterAndEnforcement(float dt)
        {
            if (StoppedVehicle != null)
            {
                if (Traffic == null || !Traffic.Vehicles.Contains(StoppedVehicle) || StoppedVehicle.Mode == DriverMode.Crashed) StoppedVehicle = null;
                else if (StoppedVehicle.Mode == DriverMode.Driving && Clock.ElapsedGameSeconds - _stopRequestTime > 25 * Clock.TimeScale) StoppedVehicle = null;
                else if (StoppedVehicle.Mode == DriverMode.Fleeing && !Pursuit.Active) { BeginPursuit(StoppedVehicle, OffenceClass.Uebertretung, Suspicion.Concrete); StoppedVehicle = null; }
            }
            // flee animation of actors
            foreach (var a in Actors) if (a.Fleeing && !a.Escaped) { a.FleeTime += dt; a.E += a.DirE * 5.2 * dt; a.N += a.DirN * 5.2 * dt; a.H = HeightAt(a.E, a.N); a.Pose = (byte)PoseKind.Run; if (a.FleeTime > 25) { a.Escaped = true; a.Fleeing = false; Warn(a.Label + " ist entkommen."); } }
        }

        // ------------------------------------------------------------------ encounter
        public void Approach(int vehicleId, int actorId, int pedId = 0)
        {
            if (InEncounter) return;
            if (pedId > 0) { var pd = Peds?.Peds.Find(x => x.Id == pedId); if (pd != null && Dist(pd.E, pd.N) < 12) BeginWithPedestrian(pd); return; }
            if (actorId > 0) { var a = Actors.FirstOrDefault(x => x.Id == actorId); if (a != null && Dist(a.E, a.N) < 12) BeginWithActor(a); return; }
            var v = Traffic?.Find(vehicleId); if (v == null || Dist(v.E, v.N) > 12) return; var citizen = CitizenForVehicle(v); BeginWithVehicle(v, citizen, LastMeasure != null && LastMeasure.VehicleId == v.Id ? LastMeasure : null);
        }

        double Dist(double e, double n) { double cx = PInVehicle ? VE : PE, cn = PInVehicle ? VN : PN; return Math.Sqrt((e - PE) * (e - PE) + (n - PN) * (n - PN)); }

        void ApplyJurisdiction(Encounter e) { JurisdictionManager.Apply(e.Ctx, Jurisdiction, Pursuit.Active, false); }

        /// <summary>Personenkontrolle einer Passantin / eines Passanten (Ausweis, Befragung).</summary>
        public void BeginWithPedestrian(Pedestrian p)
        {
            if (p == null || InEncounter) return;
            var c = CitizenFactory.Create(p.Id * 6151 + Seed, "BE", PlaceName ?? "", "", 0, 0);
            var sit = new NpcSituation { Place = PlaceName, IsDriver = false, InVehicle = false, TimeOfDayNight = Hour < 6 || Hour > 21 };
            Encounter = new Encounter(EncounterKind.PersonCheck, c, sit, Corps, Obv, c.Seed); EncActor = null; EncVehicle = null; EncPed = p; ApplyJurisdiction(Encounter);
            p.State = PedState.Standing; p.Speed = 0; if (Peds != null) p.WaitUntil = (float)(Peds.Time + 600);
            Computer.Register(c); EncounterWindowRequested = true; PushEncounter();
        }

        public void BeginWithActor(SActor a)
        {
            if (a == null || a.Enc == null || InEncounter) return; Encounter = a.Enc; EncActor = a; EncVehicle = null; ApplyJurisdiction(Encounter); Computer.Register(a.Person); a.Pose = (byte)PoseKind.Talk; EncounterWindowRequested = true; PushEncounter();
        }

        public void BeginWithVehicle(TrafficVehicle v, Citizen driver, Measurement m)
        {
            if (v == null || InEncounter) return; EncVehicle = v; EncActor = null;
            var sit = new NpcSituation { Place = PlaceName, IsDriver = true, InVehicle = true, TimeOfDayNight = Hour < 6 || Hour > 21 };
            if (m != null && m.VehicleId == v.Id && Clock.ElapsedGameSeconds - m.Time < 120 * Clock.TimeScale) { sit.MeasuredKmh = (int)m.Kmh; sit.LimitKmh = m.Limit; sit.TrueViolation = m.Fine != null && m.Fine.IsViolation ? ViolationType.Speeding : ViolationType.None; sit.NpcGuilty = sit.TrueViolation != ViolationType.None; }
            else if (v.LastViolation != ViolationType.None && Traffic != null && Traffic.Time - v.LastViolationTime < 40 && v.LastViolation != ViolationType.Speeding) { sit.TrueViolation = v.LastViolation; sit.NpcGuilty = true; }
            if (v.Driver.Impaired) sit.NpcGuilty = true;
            var zone = m != null ? m.Zone : SpeedZone.Urban;
            Encounter = new Encounter(EncounterKind.TrafficStop, driver, sit, Corps, Obv, driver.Seed) { Zone = zone }; ApplyJurisdiction(Encounter);
            if (sit.TrueViolation == ViolationType.None && v.Driver.Impaired) Encounter.Ctx.Suspicion = Suspicion.Routine;
            Computer.Register(driver); EncounterWindowRequested = true; PushEncounter();
        }

        ActionResult Wrap(ActionResult r)
        {
            if (r == null) return null;
            if (r.Assessment != null) { var cons = Judge(r.Assessment, Encounter.Mind.Anger); foreach (var c in cons) Encounter.Transcript.Add(new TranscriptLine { Speaker = "", Text = c.Text, IsSystem = true, IsWarning = true }); }
            if (!string.IsNullOrEmpty(r.Text)) Encounter.Transcript.Add(new TranscriptLine { Speaker = "", Text = r.Text, IsSystem = true });
            foreach (var l in r.Lines) if (!l.StartsWith(Encounter.Person.FirstName + ":")) Encounter.Transcript.Add(new TranscriptLine { Speaker = "", Text = l, IsSystem = true });
            HandleReactions(); if (r.Lines.Count > 0) { string first = r.Lines[0]; int i = first.IndexOf(": "); if (first.StartsWith(Encounter.Person.FirstName + ":") && i > 0) Speak(first.Substring(i + 2)); }
            return r;
        }

        public void SpeakNow(string text) => Speak(text);
        void Speak(string text) { if (Encounter == null) return; Hub.Broadcast(new { t = "speak", text, lang = Encounter.Language, female = Encounter.Person.Gender == Gender.Female }); }

        public readonly LlmService Llm = new LlmService();

        public void Say(string text)
        {
            if (!InEncounter || string.IsNullOrWhiteSpace(text)) return; var enc = Encounter; var r = enc.Say(text); HandleReactions();
            bool useLlm = Llm.Ready && enc.Transcript.Count > 0;
            if (useLlm)
            {   // the model only rephrases the line; actions and state stay with the offline mind. The offline line is shown/spoken at once if the model is slow or fails.
                int line = enc.Transcript.Count - 1; string offline = r.Text; var parsed = Nlu.Parse(text); var mind = enc.Mind; string corps = Corps.Name; var speaker = enc.Person;
                Task.Run(async () =>
                {
                    string phrased = await Llm.PhraseAsync(mind, corps, parsed, text, offline);
                    Hub.Post(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(phrased) && line < enc.Transcript.Count) enc.Transcript[line].Text = phrased;
                        if (Encounter == enc) { PushEncounter(); Speak(!string.IsNullOrWhiteSpace(phrased) ? phrased : offline); }
                    });
                });
            }
            else Speak(r.Text);
            if (Encounter != null && Encounter.Mind.Anger > 0.85f && Encounter.Ctx.Resistance == Resistance.None) Encounter.Ctx.Resistance = Resistance.Passive;
            PushEncounter();
        }

        void HandleReactions()
        {
            if (Encounter == null || !Encounter.Fled) return;
            if (EncActor != null && !EncActor.Fleeing && !EncActor.Cuffed) { FleeActor(EncActor); Warn(EncActor.Label + " flüchtet!"); EndEncounter(false); }
            else if (EncVehicle != null && EncVehicle.Mode != DriverMode.Fleeing && Traffic != null)
            {
                BeginPursuit(EncVehicle, Encounter.Ctx.Offence == OffenceClass.None ? OffenceClass.Uebertretung : Encounter.Ctx.Offence, Suspicion.Concrete); Warn("Das Fahrzeug flüchtet! Verfolgung (Blaulicht und Horn einschalten).");
                Traffic.ReleaseStop(EncVehicle); EncVehicle.Mode = DriverMode.Fleeing; EndEncounter(false);
            }
        }

        public void FleeActor(SActor a)
        {
            if (a.Fleeing || a.Cuffed) return; a.Fleeing = true; a.FleeTime = 0; double dx = a.E - PE, dn = a.N - PN; double l = Math.Sqrt(dx * dx + dn * dn); if (l < 0.1) { dx = 1; dn = 0; l = 1; } a.DirE = (float)(dx / l); a.DirN = (float)(dn / l);
        }

        public void EncounterAction(string op, int fine, int slot)
        {
            if (!InEncounter) return; ActionResult r = null;
            switch (op)
            {
                case "docs": r = Wrap(Encounter.RequestDocuments()); break; case "breath": r = Wrap(Encounter.RequestBreathTest()); break; case "stepout": r = Wrap(Encounter.RequestStepOut()); break;
                case "reason": r = Wrap(Encounter.ExplainReason()); break; case "searchP": r = Wrap(Encounter.Search(false)); break; case "searchV": r = Wrap(Encounter.Search(true)); break;
                case "queryPlate": r = Wrap(Encounter.QueryComputer(Computer, false)); break; case "queryPerson": r = Wrap(Encounter.QueryComputer(Computer, true)); break;
                case "fine": r = Wrap(Encounter.IssueFine(fine)); if (Encounter.Fined) Career.RegisterFine(); break;
                case "warn": r = Wrap(Encounter.Warn()); break; case "rights": r = Wrap(Encounter.ReadRights()); break;
                case "cuff": r = Wrap(Encounter.Handcuff()); if (EncActor != null) { EncActor.Cuffed = true; EncActor.Fleeing = false; EncActor.Pose = (byte)PoseKind.Cuffed; } break;
                case "arrest": r = Wrap(Encounter.Arrest()); if (Encounter.Arrested) { Career.RegisterArrest(); if (EncActor != null) EncActor.Arrested = true; } break;
                case "report": r = Wrap(Encounter.Report()); break;
                case "release": r = Wrap(Encounter.Release()); EndEncounter(true); return;
                case "end": EndEncounter(true); return;
            }
            PushEncounter();
        }

        public int ExpectedFine()
        {
            if (Encounter == null) return 0;
            if (Encounter.TrueViolation == ViolationType.Speeding && Encounter.MeasuredKmh > 0) { var f = SpeedingRules.Assess(Obv, Encounter.MeasuredKmh, Encounter.LimitKmh, Encounter.Zone); return f.IsOrdnungsbusse ? f.Amount : 0; }
            return 0;
        }

        public void EndEncounter(bool evaluate)
        {
            if (Encounter == null) return; var enc = Encounter; var sum = enc.Evaluate();
            if (evaluate)
            {
                Career.IncidentCompleted(sum.Score01, sum.Correct, (float)Clock.ElapsedGameSeconds); Career.Xp += sum.Xp; if (enc.Politeness > 1 && enc.Mind.Anger < 0.4f) Career.RewardDeEscalation(); Career.RewardPoliteness(enc.Politeness);
            }
            string summary = enc.Kind == EncounterKind.TrafficStop ? (enc.Fined ? $"Ordnungsbusse {enc.FineChf} CHF – {enc.Person.FullName}" : enc.Reported ? "Verzeigung – " + enc.Person.FullName : enc.Arrested ? "Festnahme – " + enc.Person.FullName : "Verkehrskontrolle – " + enc.Person.FullName) : "Kontakt – " + enc.Person.FullName;
            AddCase(enc.Kind == EncounterKind.TrafficStop ? "Verkehr" : "Person", summary, enc.Person.Vehicle?.Plate, enc.Person.FullName, enc.FineChf, sum.Correct);
            foreach (var f in sum.Findings) Note(f.StartsWith("Rechtswidrig") ? "warn" : "info", f, 9f);
            if (EncVehicle != null && Traffic != null && EncVehicle.Mode != DriverMode.Fleeing && !enc.Arrested) Traffic.ReleaseStop(EncVehicle);
            if (EncActor != null && !EncActor.Cuffed && !EncActor.Fleeing) EncActor.Pose = (byte)PoseKind.Idle;
            if (EncPed != null && Peds != null) { EncPed.WaitUntil = (float)Peds.Time; EncPed.Speed = EncPed.Speed > 0 ? EncPed.Speed : 1.35f; EncPed = null; }
            Encounter = null; EncActor = null; EncVehicle = null; EncounterWindowRequested = false; Hub.Broadcast(new { t = "encounterEnd" });
            if (evaluate) Note(sum.Correct ? "good" : "warn", $"Kontakt beendet – Bewertung {(int)Math.Round(sum.Score01 * 100)} %", 7f);
        }

        public object EncounterJson()
        {
            var e = Encounter; if (e == null) return null; var p = e.Person;
            bool shown = e.DocumentsChecked || e.LicenseShown;
            return new
            {
                kind = e.Kind.ToString(), name = p.FormalAddress(e.Language), first = p.FirstName, lang = e.Language, anger = e.Mind.Anger, expectedFine = ExpectedFine(), fined = e.Fined, arrested = e.Arrested, cuffed = e.Cuffed, driver = e.Kind == EncounterKind.TrafficStop,
                card = shown ? new { name = p.FullName, birth = p.Birthdate, nat = p.Nationality, address = p.Address + ", " + p.Municipality, licence = $"Kat. {p.LicenseCategory} · gültig bis {p.LicenseExpires}", vehicle = $"{p.Vehicle.Make} {p.Vehicle.Model} · {p.Vehicle.Plate}", holder = string.IsNullOrEmpty(p.Vehicle.HolderName) ? p.FullName : p.Vehicle.HolderName } : null,
                transcript = e.Transcript.Select(l => new { who = l.IsSystem ? "" : (l.IsOfficer ? "Sie" : p.FirstName), text = l.Text, sys = l.IsSystem, warn = l.IsWarning, officer = l.IsOfficer }).ToArray()
            };
        }

        public void PushEncounter() { Hub.Broadcast(new { t = "encounter", open = EncounterWindowRequested, enc = EncounterJson() }); EncounterWindowRequested = false; }

        // ------------------------------------------------------------------ police computer
        public void ComputerQuery(string kind, string plate, string first, string last, string birth, int reasonIdx)
        {
            string[] reasons = { "Verkehrskontrolle / Verdacht auf Verkehrsdelikt", "Verdacht auf Straftat", "Fahndung / Vermisstensuche", "Unfallaufnahme", "" };
            string reason = reasons[Math.Max(0, Math.Min(reasons.Length - 1, reasonIdx))]; var ctx = new LegalContext { Suspicion = reasonIdx == 1 ? Suspicion.Concrete : reasonIdx < 4 ? Suspicion.Routine : Suspicion.None };
            var res = kind == "plate" ? Computer.QueryPlate(plate ?? "", reason, ctx) : Computer.QueryPerson(first ?? "", last ?? "", birth ?? "", reason, ctx);
            if (res.Assessment != null) Judge(res.Assessment);
            if (res.Found && res.Person != null && (res.Flags & QueryFlags.Wanted) != 0) Warn("Treffer: Person zur Fahndung ausgeschrieben.");
            Hub.Broadcast(new { t = "computerResult", title = res.Title, lines = res.Lines.ToArray(), lawful = res.Assessment == null || res.Assessment.Lawful, problems = res.Assessment != null && !res.Assessment.Lawful ? res.Assessment.Reasons.ToArray() : new string[0] });
        }

        public object ComputerLog() => Computer.Log.AsEnumerable().Reverse().Take(60).Select(q => new { time = TimeSpan.FromSeconds(q.GameSeconds).ToString(@"hh\:mm\:ss"), kind = q.Kind, subject = q.Subject, reason = q.Reason, lawful = q.Lawful }).ToArray();

        // ------------------------------------------------------------------ pursuit
        public bool BeginPursuit(TrafficVehicle v, OffenceClass offence, Suspicion s)
        {
            if (v == null || Traffic == null) return false; PursuitTarget = v; RiskScore = 0; _assessTimer = 0; RoadblockReady = true; return Pursuit.Start(Traffic, v, offence, s);
        }

        void TickPursuit(float dt)
        {
            if (!Pursuit.Active || Traffic == null) return; Pursuit.Update(Traffic, dt, PE, PN); _assessTimer += dt;
            if (_assessTimer > 4f) { _assessTimer = 0; float tv = Pursuit.TargetKmh; if ((UrbanHere && tv > 90f) || tv > 160f) RiskScore += 1f; if (PInVehicle && PSpeedKmh > (UrbanHere ? 110f : 190f)) RiskScore += 1f; }
            if (!Pursuit.Active) EndPursuit();
        }

        public void PursuitAction(string op)
        {
            if (!Pursuit.Active) return;
            if (op == "heli") Pursuit.CallHelicopter();
            else if (op == "abort") { Pursuit.Abort("durch die Einsatzleitung"); EndPursuit(); }
            else if (op == "roadblock" && RoadblockReady && PursuitTarget != null)
            {
                var net = Traffic.Net; var t = PursuitTarget; if (t.Link < 0 || t.Link >= net.Links.Count) return; double dist = 0; int link = t.Link; var l = net.Links[link]; int hops = 0;
                while (dist < 520 && hops < 6) { dist += l.Length - (hops == 0 ? t.S : 0); if (l.Out.Count == 0) break; link = l.Out[0]; l = net.Links[link]; hops++; }
                l.PointAt(l.Length * 0.5f, out double e, out double n, out _, out _); Pursuit.SetRoadblock(e, n); RoadblockReady = false; Props.Add(new SProp { Id = NextId(), Kind = "roadblock", E = e, N = n, H = HeightAt(e, n) });
            }
        }

        void EndPursuit()
        {
            var outcome = Pursuit.Outcome; bool lawful = RiskScore < 3; bool risk = RiskScore >= 3 && Pursuit.Offence <= OffenceClass.Uebertretung; bool blue = PBlue && PSiren;
            var a = Pursuit.Assess(blue, lawful, risk, Jurisdiction.InOwnTerritory, true); Judge(a, 0.3f);
            switch (outcome)
            {
                case PursuitOutcome.Stopped: Good("Verfolgung beendet: Fahrzeug gestoppt."); Career.Xp += 80; Career.RewardDeEscalation(); if (PursuitTarget != null) { PursuitTarget.Mode = DriverMode.Stopped; PursuitTarget.Hazard = true; BeginWithVehicle(PursuitTarget, CitizenForVehicle(PursuitTarget), null); if (Encounter != null) { Encounter.Fled = true; Encounter.Ctx.Suspicion = Suspicion.CaughtInAct; Encounter.Ctx.Offence = OffenceClass.Uebertretung; Encounter.TrueViolation = ViolationType.DangerousDriving; PushEncounter(); } } break;
                case PursuitOutcome.Escaped: Warn("Verfolgung abgebrochen: Fahrzeug entkommen. Fahndung der Zentrale läuft."); break;
                case PursuitOutcome.Crashed: Warn("Das Fluchtfahrzeug ist verunfallt. Unfallstelle sichern und Rettung aufbieten."); break;
                case PursuitOutcome.Aborted: Info("Verfolgung abgebrochen."); break;
            }
            PursuitTarget = null; Props.RemoveAll(p => p.Kind == "roadblock");
        }

        // ------------------------------------------------------------------ navigation
        public void SetDestination(double e, double n, string name)
        {
            Nav.DestE = e; Nav.DestN = n; Nav.DestinationName = name ?? ""; Nav.Error = null; Nav.ArrivedFired = false; StartRoute();
        }

        public void ClearRoute() { Nav = new Nav(); Hub.Broadcast(new { t = "route", points = new double[0] }); }

        void StartRoute()
        {
            if (W.Router == null) return; double fe = PInVehicle ? VE : PE, fn = PInVehicle ? VN : PN, te = Nav.DestE, tn = Nav.DestN; var router = W.Router; Nav.Computing = true; Nav.Task = Task.Run(() => router.Route(fe, fn, te, tn));
        }

        void TickNav(float dt)
        {
            var nav = Nav;
            if (nav.Task != null && nav.Task.IsCompleted)
            {
                nav.Computing = false;
                if (nav.Task.Status == TaskStatus.RanToCompletion && nav.Task.Result != null) SetRoute(nav.Task.Result); else { nav.Error = "Keine Route gefunden."; Warn("Navigation: keine Route zum Ziel gefunden."); nav.Route = null; }
                nav.Task = null;
            }
            if (nav.Route == null) return;
            double e = PInVehicle ? VE : PE, n = PInVehicle ? VN : PN; Progress(e, n);
            nav.AvgSpeed = Math.Max(5, nav.AvgSpeed * 0.995 + Math.Min(80, PSpeedKmh / 3.6) * 0.005); nav.EtaSeconds = (float)(nav.RemainingM / Math.Max(8, nav.AvgSpeed));
            if (nav.OffRouteM > 60) { nav.OffTime += dt; if (nav.OffTime > 3f && !nav.Computing && Environment.TickCount64 > nav.NextReroute) { nav.NextReroute = Environment.TickCount64 + 8000; nav.OffTime = 0; StartRoute(); } } else nav.OffTime = 0;
            if (nav.RemainingM < 30 && !nav.ArrivedFired) { nav.ArrivedFired = true; Good("Sie haben Ihr Ziel erreicht."); }
        }

        void SetRoute(RoutePath r)
        {
            var nav = Nav; nav.Route = r; nav.Instructions = NavigationGuide.Build(r); nav.Cum = new double[r.Count]; for (int i = 1; i < r.Count; i++) { double de = r.E[i] - r.E[i - 1], dn = r.N[i] - r.N[i - 1]; nav.Cum[i] = nav.Cum[i - 1] + Math.Sqrt(de * de + dn * dn); }
            nav.TotalM = nav.Cum[r.Count - 1]; nav.Idx = 0; nav.AlongM = 0; nav.RemainingM = nav.TotalM;
            int step = Math.Max(1, r.Count / 600); var pts = new List<double>(); for (int i = 0; i < r.Count; i += step) { pts.Add(r.E[i]); pts.Add(r.N[i]); } pts.Add(r.E[r.Count - 1]); pts.Add(r.N[r.Count - 1]);
            Hub.Broadcast(new { t = "route", points = pts.ToArray(), dest = nav.DestinationName, total = nav.TotalM });
        }

        void Progress(double e, double n)
        {
            var nav = Nav; if (nav.Cum == null) return; double best = double.MaxValue; int bi = nav.Idx; double bt = 0; int end = Math.Min(nav.Route.Count - 1, nav.Idx + 80);
            for (int i = Math.Max(0, nav.Idx - 2); i < end; i++)
            {
                double ax = nav.Route.E[i], ay = nav.Route.N[i], bx = nav.Route.E[i + 1], by = nav.Route.N[i + 1], dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy; if (l2 < 1e-9) continue;
                double t = Math.Max(0, Math.Min(1, ((e - ax) * dx + (n - ay) * dy) / l2)); double qx = ax + dx * t - e, qy = ay + dy * t - n, d = qx * qx + qy * qy; if (d < best) { best = d; bi = i; bt = t; }
            }
            nav.OffRouteM = Math.Sqrt(best); if (nav.OffRouteM < 40) nav.Idx = bi;
            nav.AlongM = nav.Cum[bi] + (nav.Cum[bi + 1] - nav.Cum[bi]) * bt; nav.RemainingM = Math.Max(0, nav.TotalM - nav.AlongM); nav.Next = NavigationGuide.Next(nav.Instructions, nav.AlongM, out nav.DistToNext);
        }
    }
}
