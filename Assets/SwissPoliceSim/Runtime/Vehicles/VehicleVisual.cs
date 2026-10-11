using System;
using System.Collections.Generic;
using SwissPoliceSim.Common;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Textures;
using SwissPoliceSim.Rendering;
using UnityEngine;

namespace SwissPoliceSim.Vehicles
{
    /// <summary>Renderer-side state of one car: wheel steering/spin, lamps (emission + optional real lights), indicators, hazard, blue lights, brake lights.</summary>
    public sealed class VehicleVisual : MonoBehaviour
    {
        public CarModel Model; public Transform[] WheelPivots = new Transform[4]; public Transform[] WheelSpin = new Transform[4];
        public MeshRenderer BodyRenderer; public Material LampFront, LampRear, Indicator, BlueL, BlueR, PlateFront; public Texture2D PlateTexture;
        public bool Headlights, Brake, Reverse, IndLeft, IndRight, Hazard, Blue;
        public bool IsPolice;
        Light[] _headLights, _blueLights, _tailLights; float _phase; bool _hasRealLights;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        public void AttachRealLights()
        {
            if (_hasRealLights || Model == null) return; _hasRealLights = true;
            _headLights = new Light[2]; _tailLights = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1 : 1;
                var h = new GameObject("Headlight" + i); h.transform.SetParent(transform, false); h.transform.localPosition = new Vector3(s * 0.6f, 0.75f, Model.Length * 0.5f - 0.05f); h.transform.localRotation = Quaternion.Euler(3, 0, 0);
                var l = h.AddComponent<Light>(); l.type = LightType.Spot; l.range = 70f; l.spotAngle = 62f; l.innerSpotAngle = 25f; l.intensity = 38f; l.color = new Color(1f, 0.95f, 0.85f); l.shadows = LightShadows.None; l.enabled = false; _headLights[i] = l;
                var t = new GameObject("Taillight" + i); t.transform.SetParent(transform, false); t.transform.localPosition = new Vector3(s * 0.7f, 0.9f, -Model.Length * 0.5f - 0.1f);
                var tl = t.AddComponent<Light>(); tl.type = LightType.Point; tl.range = 6f; tl.intensity = 1.2f; tl.color = new Color(1f, 0.05f, 0.03f); tl.shadows = LightShadows.None; tl.enabled = false; _tailLights[i] = tl;
            }
            if (IsPolice)
            {
                _blueLights = new Light[2];
                for (int i = 0; i < 2; i++)
                {
                    var b = new GameObject("BlueLight" + i); b.transform.SetParent(transform, false); b.transform.localPosition = new Vector3(i == 0 ? -0.35f : 0.35f, Model.Height + 0.25f, 0.2f);
                    var l = b.AddComponent<Light>(); l.type = LightType.Point; l.range = 22f; l.intensity = 14f; l.color = new Color(0.1f, 0.3f, 1f); l.shadows = LightShadows.None; l.enabled = false; _blueLights[i] = l;
                }
            }
        }

        public void SetWheels(float steerDeg, float distanceMetres)
        {
            if (Model == null) return; float spinDeg = distanceMetres / Mathf.Max(0.1f, Model.WheelRadius) * Mathf.Rad2Deg;
            for (int i = 0; i < 4; i++)
            {
                if (WheelPivots[i] == null) continue;
                if (i < 2) WheelPivots[i].localRotation = Quaternion.Euler(0, steerDeg, 0);
                if (WheelSpin[i] != null) WheelSpin[i].Rotate(Vector3.right, (i % 2 == 0 ? -1 : 1) * spinDeg * -1f, Space.Self);
            }
        }

        /// <summary>Wheel poses straight from WheelColliders (physics cars).</summary>
        public void SetWheelPose(int i, Vector3 worldPos, Quaternion worldRot) { if (WheelPivots[i] != null) WheelPivots[i].SetPositionAndRotation(worldPos, worldRot); }

        void Update()
        {
            if (Model == null) return;
            _phase += Time.deltaTime; bool blink = ((int)(_phase * 2.8f) & 1) == 0;
            bool left = (IndLeft || Hazard) && blink, right = (IndRight || Hazard) && blink;
            var head = Headlights ? new Color(1f, 0.96f, 0.85f) * 3.2f : new Color(0.05f, 0.05f, 0.06f);
            var tail = Brake ? new Color(1f, 0.04f, 0.02f) * 3.5f : Headlights ? new Color(0.9f, 0.02f, 0.01f) * 0.7f : new Color(0.15f, 0.0f, 0.0f);
            if (Reverse) tail = Color.Lerp(tail, Color.white * 2f, 0.6f);
            MaterialLibrary.SetEmission(LampFront, head); MaterialLibrary.SetEmission(LampRear, tail);
            MaterialLibrary.SetEmission(Indicator, (left || right) ? new Color(1f, 0.55f, 0.05f) * 3f : new Color(0.25f, 0.12f, 0.0f) * 0.2f);
            bool flashA = Blue && (((int)(_phase * 7f)) & 1) == 0, flashB = Blue && (((int)(_phase * 7f + 1)) & 1) == 0;
            MaterialLibrary.SetEmission(BlueL, flashA ? new Color(0.1f, 0.35f, 1f) * 7f : new Color(0.02f, 0.05f, 0.25f) * 0.4f); MaterialLibrary.SetEmission(BlueR, flashB ? new Color(0.1f, 0.35f, 1f) * 7f : new Color(0.02f, 0.05f, 0.25f) * 0.4f);
            if (_hasRealLights)
            {
                foreach (var l in _headLights) l.enabled = Headlights;
                foreach (var l in _tailLights) { l.enabled = Brake || Headlights; l.intensity = Brake ? 2.2f : 0.7f; }
                if (_blueLights != null) { _blueLights[0].enabled = flashA; _blueLights[1].enabled = flashB; }
            }
        }

        void OnDestroy()
        {
            foreach (var m in new[] { LampFront, LampRear, Indicator, BlueL, BlueR, PlateFront }) if (m != null) Destroy(m);
            if (PlateTexture != null) Destroy(PlateTexture);
        }
    }

    public static class VehicleFactory
    {
        sealed class Entry { public CarModel Model; public Mesh Body, Wheel; }
        static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Cache.Clear(); }

        public static readonly byte[][] Palette =
        {
            new byte[] { 238, 238, 236 }, new byte[] { 28, 28, 30 }, new byte[] { 176, 180, 186 }, new byte[] { 96, 100, 106 }, new byte[] { 36, 60, 110 }, new byte[] { 150, 28, 30 },
            new byte[] { 40, 84, 52 }, new byte[] { 96, 70, 50 }, new byte[] { 200, 184, 150 }, new byte[] { 210, 120, 40 }, new byte[] { 226, 200, 70 }, new byte[] { 90, 24, 40 }
        };

        static string Key(BodyKind k, CarLivery l)
        {
            return $"{k}|{l.Body[0]},{l.Body[1]},{l.Body[2]}|{(l.Stripe != null ? l.Stripe[0] + "," + l.Stripe[1] + "," + l.Stripe[2] : "-")}|{l.LightBar}|{l.PoliceDecals}";
        }

        static Entry GetEntry(BodyKind kind, CarLivery liv)
        {
            string key = Key(kind, liv);
            if (!Cache.TryGetValue(key, out var e))
            {
                var model = CarMesher.Build(kind, liv);
                e = new Entry { Model = model, Body = MeshConv.ToMesh(model.Body, "car " + key), Wheel = MeshConv.ToMesh(model.Wheel, "wheel") };
                Cache[key] = e;
            }
            return e;
        }

        public static CarLivery CivilianLivery(int colorIndex, string plate) { var c = Palette[((colorIndex % Palette.Length) + Palette.Length) % Palette.Length]; return CarLivery.Civilian(c[0], c[1], c[2], plate); }

        /// <summary>
        /// Drop-in replacement of the procedural car by a modelled prefab. Put a prefab at Resources/Models/Vehicles/&lt;Name&gt;.prefab where Name is
        /// "Police_Wagon" (patrol car) or the body kind ("Wagon", "Hatchback", "Sedan", "Suv", "Van"). Axes: +Z forward, +Y up, origin on the ground in the middle of the car,
        /// real size (about 4.7 x 1.85 x 1.5 m). Wheels are optional children named Wheel_FL, Wheel_FR, Wheel_RL, Wheel_RR (pivot at the wheel centre; they are steered and spun).
        /// Without a wheel child the wheels of the procedural car stay visible.
        /// </summary>
        static void TryApplyCustomModel(GameObject root, VehicleVisual vv, BodyKind kind, bool police)
        {
            var prefab = Resources.Load<GameObject>("Models/Vehicles/" + (police ? "Police_" : "") + kind);
            if (prefab == null && police) prefab = Resources.Load<GameObject>("Models/Vehicles/Police_Wagon");
            if (prefab == null) return;
            var inst = UnityEngine.Object.Instantiate(prefab, root.transform, false); inst.name = "CustomModel";
            foreach (var col in inst.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(col);
            if (vv.BodyRenderer != null) vv.BodyRenderer.enabled = false;
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++)
            {
                var w = FindDeep(inst.transform, names[i]); if (w == null) continue;
                if (vv.WheelPivots[i] != null) { foreach (var r in vv.WheelPivots[i].GetComponentsInChildren<Renderer>()) r.enabled = false; }
                vv.WheelPivots[i] = w; vv.WheelSpin[i] = w.childCount > 0 ? w.GetChild(0) : w;
            }
        }

        static Transform FindDeep(Transform t, string name) { if (t.name == name) return t; for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; } return null; }

        public static VehicleVisual Create(BodyKind kind, CarLivery liv, bool realLights = false, string name = "Car")
        {
            if (liv.Body == null) liv.Body = new byte[] { 200, 200, 200 };
            var e = GetEntry(kind, liv); var model = e.Model;
            var root = new GameObject(name); var vv = root.AddComponent<VehicleVisual>(); vv.Model = model; vv.IsPolice = liv.LightBar;
            var bodyGo = new GameObject("Body"); bodyGo.transform.SetParent(root.transform, false);
            bodyGo.AddComponent<MeshFilter>().sharedMesh = e.Body;
            string plate = liv.Plate ?? "BE 000000";
            var mats = MaterialLibrary.CarBody(plate, liv.Body, liv.LightBar);
            // per-car plate texture (destroyed with the car)
            var plateTex = MaterialLibrary.ToTexture(ProceduralTextures.Plate(plate), true, true, TextureWrapMode.Clamp, 4); plateTex.name = "plate " + plate;
            var plateMat = new Material(mats[CarMesher.SubPlate]); plateMat.mainTexture = plateTex; if (plateMat.HasProperty("_BaseMap")) plateMat.SetTexture("_BaseMap", plateTex);
            mats[CarMesher.SubPlate] = plateMat; vv.PlateTexture = plateTex; vv.PlateFront = plateMat;
            var mr = bodyGo.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; vv.BodyRenderer = mr;
            vv.LampFront = mats[CarMesher.SubLampFront]; vv.LampRear = mats[CarMesher.SubLampRear]; vv.Indicator = mats[CarMesher.SubIndicator]; vv.BlueL = mats[CarMesher.SubBlueL]; vv.BlueR = mats[CarMesher.SubBlueR];
            var wheelMats = MaterialLibrary.Wheel();
            for (int i = 0; i < 4; i++)
            {
                var c = model.WheelCentres[i];
                var pivot = new GameObject("Wheel" + i); pivot.transform.SetParent(root.transform, false); pivot.transform.localPosition = new Vector3(c[0], c[1], c[2]); vv.WheelPivots[i] = pivot.transform;
                var spin = new GameObject("Spin"); spin.transform.SetParent(pivot.transform, false); if (c[0] < 0) spin.transform.localRotation = Quaternion.Euler(0, 180, 0);
                spin.AddComponent<MeshFilter>().sharedMesh = e.Wheel; var wr = spin.AddComponent<MeshRenderer>(); wr.sharedMaterials = wheelMats; wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; vv.WheelSpin[i] = spin.transform;
            }
            TryApplyCustomModel(root, vv, kind, liv.LightBar);
            if (realLights) vv.AttachRealLights();
            return vv;
        }
    }
}
