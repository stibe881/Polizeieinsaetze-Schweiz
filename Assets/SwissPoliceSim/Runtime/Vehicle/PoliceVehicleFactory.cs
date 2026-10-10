using SwissPoliceSim.Util;
using UnityEngine;

namespace SwissPoliceSim.Vehicle
{
    /// <summary>
    /// Builds a drivable placeholder patrol car from primitives (no art assets needed for Phase 1).
    /// Livery is deliberately generic (white/blue, no cantonal insignia: real emblems need a rights clearance).
    /// </summary>
    public static class PoliceVehicleFactory
    {
        public static PoliceVehicleController Create(Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("Patrouille 1");
            root.transform.SetPositionAndRotation(position, rotation);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1900f; rb.linearDamping = 0.02f; rb.angularDamping = 0.3f;

            var white = MaterialUtil.Create(new Color(0.93f, 0.94f, 0.95f), 0.6f);
            var blue = MaterialUtil.Create(new Color(0.05f, 0.15f, 0.45f), 0.5f);
            var glass = MaterialUtil.Create(new Color(0.08f, 0.1f, 0.12f), 0.9f);
            var tyre = MaterialUtil.Create(new Color(0.05f, 0.05f, 0.05f), 0.1f);

            var body = Part(root.transform, PrimitiveType.Cube, "Body", new Vector3(0, 0.78f, 0), new Vector3(1.85f, 0.7f, 4.7f), white);
            Part(root.transform, PrimitiveType.Cube, "Cabin", new Vector3(0, 1.3f, -0.15f), new Vector3(1.7f, 0.55f, 2.5f), glass);
            Part(root.transform, PrimitiveType.Cube, "Roof", new Vector3(0, 1.6f, -0.15f), new Vector3(1.72f, 0.06f, 2.3f), white);
            Part(root.transform, PrimitiveType.Cube, "Stripe", new Vector3(0, 0.82f, 0), new Vector3(1.87f, 0.12f, 4.5f), blue);
            var col = body.GetComponent<BoxCollider>();
            Object.Destroy(col);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.95f, 0); box.size = new Vector3(1.85f, 1.2f, 4.6f);

            // wheels
            var vc = root.AddComponent<PoliceVehicleController>();
            Vector3[] wp = { new Vector3(-0.8f, 0.4f, 1.45f), new Vector3(0.8f, 0.4f, 1.45f), new Vector3(-0.8f, 0.4f, -1.4f), new Vector3(0.8f, 0.4f, -1.4f) };
            var holder = new GameObject("Wheels").transform; holder.SetParent(root.transform, false);
            for (int i = 0; i < 4; i++)
            {
                var wcGo = new GameObject("WheelCollider " + i);
                wcGo.transform.SetParent(holder, false);
                wcGo.transform.localPosition = wp[i] + Vector3.up * 0.15f;
                var wc = wcGo.AddComponent<WheelCollider>();
                wc.radius = 0.34f; wc.mass = 25f; wc.suspensionDistance = 0.25f;
                wc.suspensionSpring = new JointSpring { spring = 38000f, damper = 4800f, targetPosition = 0.5f };
                wc.forwardFriction = Stiff(wc.forwardFriction, 1.6f); wc.sidewaysFriction = Stiff(wc.sidewaysFriction, 1.8f);
                vc.wheelColliders[i] = wc;

                var mesh = Part(holder, PrimitiveType.Cylinder, "Wheel " + i, wp[i], new Vector3(0.68f, 0.12f, 0.68f), tyre);
                Object.Destroy(mesh.GetComponent<Collider>());
                // cylinder axis is Y; rotate so it spins around X. GetWorldPose rotation is applied to a parent so the mesh keeps its offset.
                var pivot = new GameObject("WheelPivot " + i).transform;
                pivot.SetParent(holder, false); pivot.position = wcGo.transform.position;
                mesh.SetParent(pivot, false); mesh.localPosition = Vector3.zero; mesh.localRotation = Quaternion.Euler(0, 0, 90);
                vc.wheelMeshes[i] = pivot;
            }

            // lights
            var lights = root.AddComponent<VehicleLights>();
            var bar = new GameObject("LightBar").transform; bar.SetParent(root.transform, false); bar.localPosition = new Vector3(0, 1.72f, -0.1f);
            var blueMat = MaterialUtil.Create(new Color(0.05f, 0.08f, 0.2f), 0.6f);
            var l = Part(bar, PrimitiveType.Cube, "BlueL", new Vector3(-0.35f, 0, 0), new Vector3(0.6f, 0.12f, 0.25f), blueMat);
            var r = Part(bar, PrimitiveType.Cube, "BlueR", new Vector3(0.35f, 0, 0), new Vector3(0.6f, 0.12f, 0.25f), new Material(blueMat));
            Object.Destroy(l.GetComponent<Collider>()); Object.Destroy(r.GetComponent<Collider>());
            lights.blueLeft = new[] { l.GetComponent<Renderer>() }; lights.blueRight = new[] { r.GetComponent<Renderer>() };
            lights.blueLights = new[] { PointLight(l, new Color(0.2f, 0.4f, 1f)), PointLight(r, new Color(0.2f, 0.4f, 1f)) };

            lights.headlights = new[] { Spot(root.transform, new Vector3(-0.6f, 0.75f, 2.35f)), Spot(root.transform, new Vector3(0.6f, 0.75f, 2.35f)) };
            foreach (var h in lights.headlights) h.enabled = false;

            var amber = MaterialUtil.Create(new Color(0.25f, 0.15f, 0.02f), 0.5f);
            lights.indicatorsLeft = new[] { Indicator(root.transform, new Vector3(-0.85f, 0.8f, 2.34f), amber), Indicator(root.transform, new Vector3(-0.85f, 0.8f, -2.34f), amber) };
            lights.indicatorsRight = new[] { Indicator(root.transform, new Vector3(0.85f, 0.8f, 2.34f), amber), Indicator(root.transform, new Vector3(0.85f, 0.8f, -2.34f), amber) };

            var src = root.AddComponent<AudioSource>();
            src.clip = VehicleLights.MakeSirenClip(); src.loop = true; src.spatialBlend = 1f; src.maxDistance = 400f; src.playOnAwake = false;
            lights.sirenSource = src;

            // camera anchors
            var cockpit = new GameObject("CockpitCamAnchor").transform; cockpit.SetParent(root.transform, false); cockpit.localPosition = new Vector3(-0.35f, 1.28f, 0.25f);
            var hood = new GameObject("HoodCamAnchor").transform; hood.SetParent(root.transform, false); hood.localPosition = new Vector3(0, 1.45f, 0.9f);
            return vc;
        }

        static WheelFrictionCurve Stiff(WheelFrictionCurve c, float stiffness) { c.stiffness = stiffness; return c; }

        static Transform Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var c = go.GetComponent<Collider>();
            if (c != null && name != "Body") Object.Destroy(c);
            return go.transform;
        }

        static Light PointLight(Transform parent, Color c)
        {
            var go = new GameObject("BlueLight"); go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(0, 0.3f, 0);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = 18f; l.intensity = 6f; l.enabled = false;
            return l;
        }

        static Light Spot(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Headlight"); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Spot; l.range = 60f; l.spotAngle = 60f; l.intensity = 8f;
            return l;
        }

        static Renderer Indicator(Transform parent, Vector3 pos, Material m)
        {
            var t = Part(parent, PrimitiveType.Cube, "Indicator", pos, new Vector3(0.18f, 0.1f, 0.06f), new Material(m));
            return t.GetComponent<Renderer>();
        }
    }
}
