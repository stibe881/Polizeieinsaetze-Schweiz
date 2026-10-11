using System;
using SwissPoliceSim.Characters;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Traffic;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    /// <summary>A named person in the world that the officer can talk to: incident parties and suspects. Has a Citizen (data), an Encounter (conversation + legal state) and a body.</summary>
    public sealed class NpcActor : MonoBehaviour
    {
        public Citizen Person; public ScenarioRole Role; public Encounter Enc; public HumanVisual Visual; public string Label = ""; public TrafficVehicle Vehicle;
        public bool Fleeing, Cuffed, Arrested, Escaped, Seated; public Transform LookAt; Vector3 _fleeDir; float _fleeTime;

        public static NpcActor Spawn(Citizen c, Vector3 pos, float yawDeg, string label, ScenarioRole role = null)
        {
            bool female = c.Gender == Gender.Female; var r = new System.Random(c.Seed);
            var spec = HumanSpec.FromPed(female, false, c.Age > 68, (byte)r.Next(12), (byte)r.Next(8), (byte)r.Next(6), (byte)r.Next(7), (female ? 1.62f : 1.76f) + (float)r.NextDouble() * 0.14f, c.Seed);
            spec.Shoes = new byte[] { 40, 40, 44 };
            var go = HumanFactory.Create(spec, "NPC " + c.FullName, 0, true); go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, yawDeg, 0);
            var a = go.AddComponent<NpcActor>(); a.Person = c; a.Role = role; a.Label = label; a.Visual = go.GetComponent<HumanVisual>(); a.Visual.EnableFootsteps(); a.Visual.Pose = PoseKind.Idle; return a;
        }

        public void Flee(Vector3 awayFrom)
        {
            if (Fleeing || Cuffed) return; Fleeing = true; _fleeTime = 0; var d = transform.position - awayFrom; d.y = 0; _fleeDir = d.sqrMagnitude < 0.01f ? transform.forward : d.normalized;
        }

        public void SetCuffed(bool v) { Cuffed = v; if (v) { Fleeing = false; Visual.Pose = PoseKind.Cuffed; Visual.Speed = 0; } }

        void Update()
        {
            if (Visual == null) return;
            if (Fleeing)
            {
                _fleeTime += Time.deltaTime; var step = _fleeDir * 5.2f * Time.deltaTime; var np = transform.position + step; if (Physics.Raycast(np + Vector3.up * 3f, Vector3.down, out var h, 8f)) np.y = h.point.y; transform.position = np;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_fleeDir), 8f * Time.deltaTime); Visual.Pose = PoseKind.Run; Visual.Speed = 5f;
                if (_fleeTime > 25f) { Escaped = true; Fleeing = false; Notifier.Warn(Label + " ist entkommen."); gameObject.SetActive(false); }
                return;
            }
            if (Cuffed) { Visual.Pose = PoseKind.Cuffed; }
            if (LookAt != null) Visual.LookTarget = LookAt;
        }

        public void FaceTowards(Vector3 p) { var d = p - transform.position; d.y = 0; if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 0.15f); }
    }
}
