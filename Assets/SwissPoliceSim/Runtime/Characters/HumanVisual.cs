using System;
using System.Collections.Generic;
using SwissPoliceSim.Common;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Rendering;
using UnityEngine;

namespace SwissPoliceSim.Characters
{
    public enum PoseKind { Idle, Walk, Run, Talk, HandsUp, Cuffed, Seated, Phone, Point, Fallen, Search }

    /// <summary>Procedural animation of the 11-part human rig: walk/run cycle, idle sway, gestures while talking, hands up, handcuffed, seated, phone call, fallen.</summary>
    public sealed class HumanVisual : MonoBehaviour
    {
        public Transform Pelvis, Head, UpperArmL, UpperArmR, ForearmL, ForearmR, ThighL, ThighR, ShinL, ShinR;
        public PoseKind Pose; public float Speed; public Transform LookTarget; public float Height = 1.75f; public bool IsPolice;
        float _phase, _t, _seed; Vector3 _pelvisBase; Quaternion _headBase = Quaternion.identity;
        AudioSource _foot; float _lastStep; public bool PlayFootsteps;

        void Awake() { _seed = UnityEngine.Random.value * 10f; }

        public void Bind() { if (Pelvis != null) _pelvisBase = Pelvis.localPosition; }

        void Update()
        {
            if (Pelvis == null) return; float dt = Time.deltaTime; _t += dt;
            float k = Height / 1.75f;
            Quaternion qUL = Quaternion.identity, qUR = Quaternion.identity, qFL = Quaternion.identity, qFR = Quaternion.identity, qTL = Quaternion.identity, qTR = Quaternion.identity, qSL = Quaternion.identity, qSR = Quaternion.identity;
            Vector3 pelvisOffset = Vector3.zero; Quaternion pelvisRot = Quaternion.identity; Quaternion headRot = Quaternion.identity;
            float breathe = Mathf.Sin(_t * 1.6f + _seed) * 0.004f * k;
            switch (Pose)
            {
                case PoseKind.Walk:
                case PoseKind.Run:
                {
                    bool run = Pose == PoseKind.Run; float rate = (run ? 3.3f : 2.1f) * Mathf.Clamp(Speed / (run ? 4.5f : 1.4f), 0.3f, 1.6f) / k;
                    _phase += dt * rate * 2f * Mathf.PI * 0.5f; float amp = run ? 48f : 27f, sw = Mathf.Sin(_phase), cw = Mathf.Cos(_phase);
                    qTL = Quaternion.Euler(sw * amp, 0, 0); qTR = Quaternion.Euler(-sw * amp, 0, 0);
                    qSL = Quaternion.Euler(Mathf.Max(0, -cw * 1f + 0.2f) * (run ? 85f : 38f) * (sw < 0 ? 0.5f : 1f), 0, 0); qSR = Quaternion.Euler(Mathf.Max(0, cw * 1f + 0.2f) * (run ? 85f : 38f) * (sw > 0 ? 0.5f : 1f), 0, 0);
                    float armAmp = run ? 52f : 24f; qUL = Quaternion.Euler(-sw * armAmp, 0, 3f); qUR = Quaternion.Euler(sw * armAmp, 0, -3f);
                    qFL = Quaternion.Euler(run ? -70f : -18f - Mathf.Max(0, sw) * 14f, 0, 0); qFR = Quaternion.Euler(run ? -70f : -18f - Mathf.Max(0, -sw) * 14f, 0, 0);
                    pelvisOffset = new Vector3(0, Mathf.Abs(cw) * (run ? 0.055f : 0.022f) * k - 0.01f, 0); pelvisRot = Quaternion.Euler(run ? 7f : 2f, sw * (run ? 6f : 3f), 0);
                    if (PlayFootsteps && _foot != null && Mathf.Sign(sw) != _lastStep && Mathf.Abs(sw) < 0.2f) { _lastStep = Mathf.Sign(sw); _foot.PlayOneShot(Audio.ProceduralAudio.Step((int)(_t * 3)), 0.35f); }
                    break;
                }
                case PoseKind.Talk:
                {
                    float g = Mathf.Sin(_t * 2.3f + _seed), g2 = Mathf.Sin(_t * 1.1f + _seed * 2f);
                    qUR = Quaternion.Euler(-35f + g * 14f, 0, -8f); qFR = Quaternion.Euler(-70f + g2 * 25f, 0, 0); qUL = Quaternion.Euler(g2 * 4f, 0, 4f); qFL = Quaternion.Euler(-14f, 0, 0);
                    headRot = Quaternion.Euler(g2 * 4f, g * 5f, 0); pelvisOffset = new Vector3(0, breathe, 0);
                    break;
                }
                case PoseKind.Phone:
                    qUR = Quaternion.Euler(-60f, 0, -15f); qFR = Quaternion.Euler(-110f, 0, 0); qUL = Quaternion.Euler(0, 0, 5f); qFL = Quaternion.Euler(-20f, 0, 0); headRot = Quaternion.Euler(8f, 12f, 8f); pelvisOffset = new Vector3(0, breathe, 0); break;
                case PoseKind.HandsUp:
                    qUL = Quaternion.Euler(-30f, 0, 150f); qUR = Quaternion.Euler(-30f, 0, -150f); qFL = Quaternion.Euler(-30f, 0, 0); qFR = Quaternion.Euler(-30f, 0, 0); pelvisOffset = new Vector3(0, breathe, 0); break;
                case PoseKind.Cuffed:
                    qUL = Quaternion.Euler(28f, 0, 6f); qUR = Quaternion.Euler(28f, 0, -6f); qFL = Quaternion.Euler(-25f, 0, 0); qFR = Quaternion.Euler(-25f, 0, 0); pelvisRot = Quaternion.Euler(3f, 0, 0); headRot = Quaternion.Euler(14f, 0, 0); pelvisOffset = new Vector3(0, breathe, 0); break;
                case PoseKind.Search:
                    qUL = Quaternion.Euler(-20f, 0, 105f); qUR = Quaternion.Euler(-20f, 0, -105f); qFL = Quaternion.Euler(0, 0, 0); qFR = Quaternion.Euler(0, 0, 0); qTL = Quaternion.Euler(0, 0, -9f); qTR = Quaternion.Euler(0, 0, 9f); pelvisRot = Quaternion.Euler(10f, 0, 0); pelvisOffset = new Vector3(0, breathe, 0); break;
                case PoseKind.Point:
                    qUR = Quaternion.Euler(-88f, 8f, -6f); qFR = Quaternion.Euler(-4f, 0, 0); qUL = Quaternion.Euler(0, 0, 4f); qFL = Quaternion.Euler(-12f, 0, 0); pelvisOffset = new Vector3(0, breathe, 0); break;
                case PoseKind.Seated:
                    qTL = Quaternion.Euler(-88f, 0, 0); qTR = Quaternion.Euler(-88f, 0, 0); qSL = Quaternion.Euler(92f, 0, 0); qSR = Quaternion.Euler(92f, 0, 0); qUL = Quaternion.Euler(-48f, 0, 6f); qUR = Quaternion.Euler(-48f, 0, -6f); qFL = Quaternion.Euler(-48f, 0, 0); qFR = Quaternion.Euler(-48f, 0, 0);
                    pelvisOffset = new Vector3(0, breathe, 0); break;
                case PoseKind.Fallen:
                    pelvisRot = Quaternion.Euler(-88f, 0, 0); pelvisOffset = new Vector3(0, -0.45f * k, 0.05f); qUL = Quaternion.Euler(0, 0, 40f); qUR = Quaternion.Euler(0, 0, -50f); qTL = Quaternion.Euler(8, 0, 6); qTR = Quaternion.Euler(-12, 0, -10); break;
                default:
                    qUL = Quaternion.Euler(Mathf.Sin(_t * 0.9f + _seed) * 2f, 0, 3.5f); qUR = Quaternion.Euler(-Mathf.Sin(_t * 0.9f + _seed) * 2f, 0, -3.5f); qFL = Quaternion.Euler(-9f, 0, 0); qFR = Quaternion.Euler(-9f, 0, 0);
                    pelvisOffset = new Vector3(0, breathe, 0); pelvisRot = Quaternion.Euler(0, Mathf.Sin(_t * 0.4f + _seed) * 2.2f, 0); break;
            }
            float lerp = 1f - Mathf.Exp(-14f * dt);
            Pelvis.localPosition = Vector3.Lerp(Pelvis.localPosition, _pelvisBase + pelvisOffset, lerp); Pelvis.localRotation = Quaternion.Slerp(Pelvis.localRotation, pelvisRot, lerp);
            Slerp(UpperArmL, qUL, lerp); Slerp(UpperArmR, qUR, lerp); Slerp(ForearmL, qFL, lerp); Slerp(ForearmR, qFR, lerp); Slerp(ThighL, qTL, lerp); Slerp(ThighR, qTR, lerp); Slerp(ShinL, qSL, lerp); Slerp(ShinR, qSR, lerp);
            if (Head != null)
            {
                if (LookTarget != null && Pose != PoseKind.Fallen)
                {   // turn the head towards the target, limited to ±60° yaw / ±30° pitch
                    var local = Pelvis.InverseTransformPoint(LookTarget.position) - Head.localPosition; float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -60f, 60f); float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -30f, 30f);
                    headRot = Quaternion.Euler(pitch, yaw, 0) * headRot;
                }
                Slerp(Head, headRot, lerp);
            }
        }

        static void Slerp(Transform t, Quaternion q, float k) { if (t != null) t.localRotation = Quaternion.Slerp(t.localRotation, q, k); }
        public void EnableFootsteps() { PlayFootsteps = true; _foot = gameObject.AddComponent<AudioSource>(); _foot.spatialBlend = 1f; _foot.maxDistance = 25f; _foot.rolloffMode = AudioRolloffMode.Linear; }
    }

    public static class HumanFactory
    {
        static readonly Dictionary<string, Mesh[]> MeshCache = new Dictionary<string, Mesh[]>();
        static readonly Dictionary<string, HumanModel> ModelCache = new Dictionary<string, HumanModel>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { MeshCache.Clear(); ModelCache.Clear(); }

        static string Key(HumanSpec s, int lod)
        {
            return $"{Mathf.Round(s.Height * 33f)}|{Mathf.Round(s.Build * 20f)}|{s.Female}{s.Child}{s.Elderly}{s.Police}{s.HiVis}{s.Cap}{s.Backpack}{s.ShortSleeves}|{s.HairStyle}|{string.Join(",", s.Shirt)}|{string.Join(",", s.Trousers)}|{string.Join(",", s.Skin)}|{string.Join(",", s.Hair)}|{string.Join(",", s.Shoes)}|{lod}";
        }

        public static GameObject Create(HumanSpec spec, string name = "Human", int lod = 0, bool collider = true)
        {
            string key = Key(spec, lod);
            if (!ModelCache.TryGetValue(key, out var model))
            {
                model = HumanMesher.Build(spec, lod); ModelCache[key] = model; var meshes = new Mesh[model.Parts.Count];
                for (int i = 0; i < meshes.Length; i++) meshes[i] = MeshConv.ToMesh(model.Parts[i].Mesh, name + "_" + model.Parts[i].Name);
                MeshCache[key] = meshes;
            }
            var meshesArr = MeshCache[key]; var mats = MaterialLibrary.Humans();
            var root = new GameObject(name); var hv = root.AddComponent<HumanVisual>(); hv.Height = spec.Height; hv.IsPolice = spec.Police;
            var byName = new Dictionary<string, Transform>();
            for (int i = 0; i < model.Parts.Count; i++)
            {
                var part = model.Parts[i]; var go = new GameObject(part.Name); go.transform.SetParent(part.Parent == null ? root.transform : byName[part.Parent], false);
                go.transform.localPosition = new Vector3(part.Pivot[0], part.Pivot[1], part.Pivot[2]); byName[part.Name] = go.transform;
                go.AddComponent<MeshFilter>().sharedMesh = meshesArr[i]; var mr = go.AddComponent<MeshRenderer>(); var arr = new Material[meshesArr[i].subMeshCount]; for (int s = 0; s < arr.Length; s++) arr[s] = mats[s]; mr.sharedMaterials = arr;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            hv.Pelvis = byName["Pelvis"]; hv.Head = byName["Head"]; hv.UpperArmL = byName["UpperArmL"]; hv.UpperArmR = byName["UpperArmR"]; hv.ForearmL = byName["ForearmL"]; hv.ForearmR = byName["ForearmR"];
            hv.ThighL = byName["ThighL"]; hv.ThighR = byName["ThighR"]; hv.ShinL = byName["ShinL"]; hv.ShinR = byName["ShinR"]; hv.Bind();
            if (collider) { var cc = root.AddComponent<CapsuleCollider>(); cc.height = spec.Height; cc.radius = 0.27f; cc.center = new Vector3(0, spec.Height * 0.5f, 0); cc.isTrigger = true; }
            return root;
        }
    }
}
