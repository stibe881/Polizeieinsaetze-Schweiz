using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Meshing;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwissPoliceSim.Common
{
    /// <summary>Unity 2021 … Unity 6 differences in one place.</summary>
    public static class RbCompat
    {
        public static Vector3 GetVelocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static void SetDamping(Rigidbody rb, float linear, float angular)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = linear; rb.angularDamping = angular;
#else
            rb.drag = linear; rb.angularDrag = angular;
#endif
        }

        public static T FindFirst<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>();
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }
    }

    public static class PhysCompat
    {
        /// <summary>Low-friction material for car bodies so they slide along walls instead of sticking (type renamed in Unity 6).</summary>
        public static void MakeSlippery(Collider c, float dynamicFriction = 0.15f, float staticFriction = 0.2f)
        {
#if UNITY_6000_0_OR_NEWER
            var m = new PhysicsMaterial("slippery") { dynamicFriction = dynamicFriction, staticFriction = staticFriction, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Minimum };
#else
            var m = new PhysicMaterial("slippery") { dynamicFriction = dynamicFriction, staticFriction = staticFriction, bounciness = 0.05f, frictionCombine = PhysicMaterialCombine.Minimum };
#endif
            c.sharedMaterial = m;
        }
    }

    /// <summary>Mesh data converted to Unity-friendly arrays. Creating this is thread safe (worker threads); <see cref="Build"/> must run on the main thread.</summary>
    public sealed class UnityMeshArrays
    {
        public Vector3[] Vertices, Normals; public Vector2[] Uv, Uv2; public Color32[] Colors; public int[][] Indices; public bool HasUv2;

        public static UnityMeshArrays From(MeshData m, bool withUv2 = false)
        {
            if (m == null || m.VertexCount == 0) return null;
            int n = m.VertexCount; var a = new UnityMeshArrays { Vertices = new Vector3[n], Normals = new Vector3[n], Uv = new Vector2[n], Colors = new Color32[n], Indices = new int[m.Indices.Length][] };
            for (int i = 0; i < n; i++)
            {
                a.Vertices[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                a.Normals[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
                a.Uv[i] = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]);
                a.Colors[i] = new Color32(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], m.Colors[i * 4 + 3]);
            }
            if (withUv2) { a.HasUv2 = true; a.Uv2 = new Vector2[n]; for (int i = 0; i < n; i++) a.Uv2[i] = new Vector2(m.Uv2[i * 2], m.Uv2[i * 2 + 1]); }
            for (int s = 0; s < m.Indices.Length; s++) a.Indices[s] = m.Indices[s].ToArray();
            return a;
        }

        public Mesh Build(string name, bool markNoLongerReadable = false)
        {
            var mesh = new Mesh { name = name, indexFormat = Vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = Vertices; mesh.normals = Normals; mesh.uv = Uv; mesh.colors32 = Colors; if (HasUv2) mesh.uv2 = Uv2;
            mesh.subMeshCount = Indices.Length;
            for (int s = 0; s < Indices.Length; s++) mesh.SetTriangles(Indices[s], s, false);
            mesh.RecalculateBounds();
            if (markNoLongerReadable) mesh.UploadMeshData(true);
            return mesh;
        }
    }

    public static class MeshConv
    {
        public static Mesh ToMesh(MeshData m, string name, bool withUv2 = false) => UnityMeshArrays.From(m, withUv2)?.Build(name);
    }
}
