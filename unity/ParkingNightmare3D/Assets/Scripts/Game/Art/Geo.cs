using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// Procedural mesh primitives and the little bit of scene-graph glue the prop and car
    /// builders share. Meshes are cached by shape key, so a street of thirty houses still
    /// hands the GPU one trunk mesh, one shingle mesh and so on.
    /// </summary>
    public static class Geo
    {
        static readonly Dictionary<string, Mesh> Cache = new();

        public static Mesh Get(string key, System.Func<Mesh> make)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            var made = make();
            made.name = key;
            Cache[key] = made;
            return made;
        }

        public static void Clear() => Cache.Clear();

        // ------------------------------------------------------------------ nodes

        /// <summary>Mesh + renderer under a parent, in one call.</summary>
        public static GameObject Node(string name, Transform parent, Mesh mesh, Material mat,
                                      Vector3 pos = default, Quaternion rot = default,
                                      Vector3 scale = default, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot == default ? Quaternion.identity : rot;
            go.transform.localScale = scale == default ? Vector3.one : scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Unit cube scaled to size — the workhorse of the whole art pass.</summary>
        public static GameObject Box(string name, Transform parent, Vector3 size, Vector3 pos,
                                     Material mat, bool shadows = true)
            => Node(name, parent, UnitCube, mat, pos, Quaternion.identity, size, shadows);

        // ------------------------------------------------------------------ shapes

        public static Mesh UnitCube => Get("cube", () =>
        {
            // hand-built rather than borrowed from CreatePrimitive so it carries no
            // collider, no extra UV set, and a UV layout we control per face
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();

            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
            {
                int b0 = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int i = 0; i < 4; i++) n.Add(nrm);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                // a,b,c,d are listed so that cross(b-a, c-a) — the same formula
                // RecalculateNormals uses, and the one that decides which side Unity
                // treats as front — comes out equal to nrm
                t.Add(b0); t.Add(b0 + 1); t.Add(b0 + 2);
                t.Add(b0); t.Add(b0 + 2); t.Add(b0 + 3);
            }

            const float h = 0.5f;
            Face(new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h), Vector3.forward);
            Face(new Vector3(h, -h, -h), new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(h, h, -h), Vector3.back);
            Face(new Vector3(h, -h, h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), Vector3.right);
            Face(new Vector3(-h, -h, -h), new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h), Vector3.left);
            Face(new Vector3(-h, h, h), new Vector3(h, h, h), new Vector3(h, h, -h), new Vector3(-h, h, -h), Vector3.up);
            Face(new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h), Vector3.down);

            var mesh = new Mesh();
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        });

        /// <summary>
        /// Tapered cylinder about +Y, centred on the origin. r0 is the bottom radius, r1
        /// the top; r1 = 0 gives a cone, which is what the conifer tiers use.
        /// </summary>
        public static Mesh Cylinder(float r0, float r1, float height, int sides, bool flat = false)
            => Get($"cyl{r0}_{r1}_{height}_{sides}_{flat}", () =>
            {
                var v = new List<Vector3>();
                var uv = new List<Vector2>();
                var t = new List<int>();
                float hh = height * 0.5f;

                for (int i = 0; i < sides; i++)
                {
                    float a0 = (float)i / sides * Mathf.PI * 2f;
                    float a1 = (float)(i + 1) / sides * Mathf.PI * 2f;
                    Vector3 b0 = new(Mathf.Cos(a0) * r0, -hh, Mathf.Sin(a0) * r0);
                    Vector3 b1 = new(Mathf.Cos(a1) * r0, -hh, Mathf.Sin(a1) * r0);
                    Vector3 t0 = new(Mathf.Cos(a0) * r1, hh, Mathf.Sin(a0) * r1);
                    Vector3 t1 = new(Mathf.Cos(a1) * r1, hh, Mathf.Sin(a1) * r1);

                    int b = v.Count;
                    v.Add(b0); v.Add(b1); v.Add(t1); v.Add(t0);
                    float u0 = (float)i / sides, u1 = (float)(i + 1) / sides;
                    uv.Add(new Vector2(u0, 0)); uv.Add(new Vector2(u1, 0));
                    uv.Add(new Vector2(u1, 1)); uv.Add(new Vector2(u0, 1));
                    t.Add(b); t.Add(b + 2); t.Add(b + 1);
                    t.Add(b); t.Add(b + 3); t.Add(b + 2);
                }

                // caps
                for (int cap = 0; cap < 2; cap++)
                {
                    float r = cap == 0 ? r0 : r1, y = cap == 0 ? -hh : hh;
                    if (r <= 1e-4f) continue;
                    int centre = v.Count;
                    v.Add(new Vector3(0, y, 0));
                    uv.Add(new Vector2(0.5f, 0.5f));
                    for (int i = 0; i <= sides; i++)
                    {
                        float a = (float)i / sides * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                        uv.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                    }
                    for (int i = 0; i < sides; i++)
                    {
                        if (cap == 0) { t.Add(centre); t.Add(centre + 1 + i); t.Add(centre + 2 + i); }
                        else { t.Add(centre); t.Add(centre + 2 + i); t.Add(centre + 1 + i); }
                    }
                }

                var mesh = new Mesh();
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                if (flat) Facet(mesh);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            });

        /// <summary>
        /// Faceted blob: a once-subdivided icosahedron with a deterministic radial wobble,
        /// flat-shaded. This is the foliage. Low-poly and hard-edged is the house style —
        /// a smooth sphere reads as a beach ball, not a tree.
        /// </summary>
        public static Mesh Blob(int seed, float wobble = 0.18f) => Get($"blob{seed}_{wobble}", () =>
        {
            var (verts, tris) = Icosphere(1);
            var rng = new Rng((uint)(seed * 2654435761u + 1));
            for (int i = 0; i < verts.Count; i++)
                verts[i] = verts[i] * (1f + (float)rng.Rand(-wobble, wobble));

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            Facet(mesh);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        });

        /// <summary>Split shared vertices so RecalculateNormals produces hard facets.</summary>
        static void Facet(Mesh mesh)
        {
            var v = mesh.vertices;
            var uv = mesh.uv;
            var t = mesh.triangles;
            var nv = new Vector3[t.Length];
            var nuv = uv.Length == v.Length ? new Vector2[t.Length] : null;
            var nt = new int[t.Length];
            for (int i = 0; i < t.Length; i++)
            {
                nv[i] = v[t[i]];
                if (nuv != null) nuv[i] = uv[t[i]];
                nt[i] = i;
            }
            mesh.Clear();
            mesh.SetVertices(nv);
            if (nuv != null) mesh.SetUVs(0, nuv);
            mesh.SetTriangles(nt, 0);
        }

        static (List<Vector3>, List<int>) Icosphere(int subdiv)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;

            var faces = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };

            for (int s = 0; s < subdiv; s++)
            {
                var next = new List<int>();
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m)) return m;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    mid[key] = v.Count - 1;
                    return v.Count - 1;
                }
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }
            return (v, faces);
        }

        /// <summary>
        /// Half torus arching over +Y in the XY plane, axis along Z — the fender arch trim
        /// above each wheel. A full torus would poke through the sill.
        /// </summary>
        public static Mesh HalfTorus(float radius, float tube, int arcSegs = 12, int tubeSegs = 6)
            => Get($"htorus{radius}_{tube}_{arcSegs}_{tubeSegs}", () =>
            {
                var v = new List<Vector3>();
                var uv = new List<Vector2>();
                var t = new List<int>();

                for (int i = 0; i <= arcSegs; i++)
                {
                    float a = Mathf.PI * i / arcSegs;          // 0..pi, sweeping over the top
                    var centre = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0);
                    var outward = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    for (int j = 0; j <= tubeSegs; j++)
                    {
                        float b = Mathf.PI * 2f * j / tubeSegs;
                        v.Add(centre + outward * (Mathf.Cos(b) * tube) + Vector3.forward * (Mathf.Sin(b) * tube));
                        uv.Add(new Vector2((float)i / arcSegs, (float)j / tubeSegs));
                    }
                }

                int ring = tubeSegs + 1;
                for (int i = 0; i < arcSegs; i++)
                    for (int j = 0; j < tubeSegs; j++)
                    {
                        int a0 = i * ring + j, a1 = a0 + 1, b0 = a0 + ring, b1 = b0 + 1;
                        t.Add(a0); t.Add(b0); t.Add(a1);
                        t.Add(a1); t.Add(b0); t.Add(b1);
                    }

                var mesh = new Mesh();
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            });

        /// <summary>
        /// Gable roof: two shingled slopes and two siding-coloured ends, as one mesh with
        /// two submeshes. Ported from <c>PropFactory.gableRoof</c>, ridge running along X.
        /// </summary>
        public static Mesh GableRoof(float wide, float depth, float rise)
            => Get($"gable{wide}_{depth}_{rise}", () =>
            {
                float w = wide * 0.5f, d = depth * 0.5f;
                var v = new[]
                {
                    new Vector3(-w, 0, d), new Vector3(w, 0, d), new Vector3(w, rise, 0), new Vector3(-w, rise, 0),
                    new Vector3(w, 0, -d), new Vector3(-w, 0, -d), new Vector3(-w, rise, 0), new Vector3(w, rise, 0),
                    new Vector3(w, 0, d), new Vector3(w, 0, -d), new Vector3(w, rise, 0),
                    new Vector3(-w, 0, -d), new Vector3(-w, 0, d), new Vector3(-w, rise, 0),
                };
                float ru = wide / 1.7f, rv = Mathf.Sqrt(rise * rise + d * d) / 1.5f;
                var uv = new[]
                {
                    new Vector2(0, 0), new Vector2(ru, 0), new Vector2(ru, rv), new Vector2(0, rv),
                    new Vector2(0, 0), new Vector2(ru, 0), new Vector2(ru, rv), new Vector2(0, rv),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1),
                };

                var mesh = new Mesh();
                mesh.SetVertices(v);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }, 0);   // slopes
                mesh.SetTriangles(new[] { 8, 9, 10, 11, 12, 13 }, 1);                 // gable ends
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            });
    }
}
