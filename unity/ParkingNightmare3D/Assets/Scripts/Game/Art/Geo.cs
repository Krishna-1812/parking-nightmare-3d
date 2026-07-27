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

        /// <summary>
        /// A box whose texture runs at a fixed number of metres per repeat, however big
        /// the box is.
        ///
        /// <see cref="Box"/> cannot do this. Its UVs are 0..1 on every face, so the tiling
        /// has to come from the material — and a material is shared, so the first house
        /// built decides the brick size for every later house of that colour. Putting the
        /// scale in the mesh instead keeps one material per cladding (which is what the
        /// SRP batcher wants) and still gives a garage and a two-storey wall the same size
        /// bricks. Meshes are cached on the size rounded to a quarter metre.
        /// </summary>
        public static GameObject BoxClad(string name, Transform parent, Vector3 size, Vector3 pos,
                                         Material mat, float metresPerRepeat, bool shadows = true)
            => Node(name, parent, CladCube(size, metresPerRepeat), mat, pos,
                    Quaternion.identity, size, shadows);

        static Mesh CladCube(Vector3 size, float repeat)
        {
            float q = 0.25f;
            float sx = Mathf.Round(size.x / q) * q;
            float sy = Mathf.Round(size.y / q) * q;
            float sz = Mathf.Round(size.z / q) * q;
            return Get($"clad{sx}_{sy}_{sz}_{repeat:0.00}", () =>
            {
                var src = UnitCube;
                var mesh = Object.Instantiate(src);
                mesh.name = "cladCube";
                var uv = mesh.uv;
                // faces come out of UnitCube in this order, four verts each:
                // +Z, -Z, +X, -X, +Y, -Y
                var spans = new[]
                {
                    new Vector2(sx, sy), new Vector2(sx, sy),
                    new Vector2(sz, sy), new Vector2(sz, sy),
                    new Vector2(sx, sz), new Vector2(sx, sz),
                };
                for (int f = 0; f < 6; f++)
                    for (int k = 0; k < 4; k++)
                    {
                        int i = f * 4 + k;
                        uv[i] = new Vector2(uv[i].x * spans[f].x / repeat,
                                            uv[i].y * spans[f].y / repeat);
                    }
                mesh.uv = uv;
                return mesh;
            });
        }

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
        /// Surface of revolution about +Y. The profile is a list of (radius, y) points
        /// walked from one end to the other; consecutive points become a ring of quads.
        ///
        /// Normals are smoothed across the whole surface and then stitched at the seam,
        /// because the duplicated seam column would otherwise light as a visible crease
        /// running down the shape. Open at both ends by design — callers cap it with
        /// whatever belongs there, which for a tyre is the rim.
        /// </summary>
        public static Mesh Lathe(string key, IReadOnlyList<Vector2> profile, int segs)
            => Get($"lathe{key}_{segs}", () =>
            {
                int cols = segs + 1;                     // +1 duplicates the seam for UVs
                var v = new List<Vector3>(cols * profile.Count);
                var uv = new List<Vector2>(cols * profile.Count);
                var t = new List<int>();

                for (int i = 0; i < cols; i++)
                {
                    float a = Mathf.PI * 2f * i / segs;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    for (int j = 0; j < profile.Count; j++)
                    {
                        v.Add(new Vector3(cos * profile[j].x, profile[j].y, sin * profile[j].x));
                        uv.Add(new Vector2((float)i / segs, (float)j / (profile.Count - 1)));
                    }
                }

                int rows = profile.Count;
                for (int i = 0; i < segs; i++)
                    for (int j = 0; j < rows - 1; j++)
                    {
                        int a0 = i * rows + j, a1 = a0 + 1;
                        int b0 = (i + 1) * rows + j, b1 = b0 + 1;
                        t.Add(a0); t.Add(a1); t.Add(b0);
                        t.Add(a1); t.Add(b1); t.Add(b0);
                    }

                var mesh = new Mesh();
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();

                // stitch: the first and last columns are the same ring of positions, so
                // give them the same normal or the seam lights as a crease
                var nrm = mesh.normals;
                for (int j = 0; j < rows; j++)
                {
                    var avg = (nrm[j] + nrm[segs * rows + j]).normalized;
                    nrm[j] = avg;
                    nrm[segs * rows + j] = avg;
                }
                mesh.SetNormals(nrm);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            });

        /// <summary>
        /// A tyre: bead, bulged sidewall, shoulder and a flat tread crown, revolved about
        /// the axle (+Y here; the caller rolls it onto the lateral axis).
        ///
        /// The bulge is the whole point. A plain cylinder has a straight wall and a hard
        /// 90-degree shoulder, which is what made the old wheels read as cotton reels — a
        /// real tyre swells past the rim and turns the corner over a radius, so it catches
        /// a soft band of light all the way round.
        /// </summary>
        public static Mesh Tyre(float radius, float width, int segs = 24)
            => Lathe($"tyre{radius}_{width}", BuildTyreProfile(radius, width), segs);

        static Vector2[] BuildTyreProfile(float r, float w)
        {
            float hw = w * 0.5f;
            // (radius, y) from the inboard bead round to the outboard bead
            var half = new[]
            {
                new Vector2(0.615f * r, 1.00f * hw),   // bead, sits on the rim flange
                new Vector2(0.780f * r, 1.00f * hw),
                new Vector2(0.905f * r, 0.90f * hw),   // sidewall bulge
                new Vector2(0.968f * r, 0.72f * hw),
                new Vector2(0.997f * r, 0.44f * hw),   // shoulder radius
                new Vector2(1.000f * r, 0.16f * hw),   // tread crown
            };
            var full = new Vector2[half.Length * 2];
            for (int i = 0; i < half.Length; i++)
            {
                full[i] = new Vector2(half[i].x, -half[i].y);                    // inboard
                full[full.Length - 1 - i] = half[i];                             // outboard
            }
            return full;
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

        /// <summary>
        /// Hipped roof: four slopes meeting a ridge shorter than the building, so there is
        /// no vertical gable end. Submesh 0 is every slope; there is no submesh 1, which is
        /// the point — the caller can hand it the same two materials a gable takes and the
        /// second simply goes unused.
        ///
        /// Worth having purely for the silhouette. A street where every roof is the same
        /// gable at the same pitch reads as one house stamped out repeatedly however much
        /// the walls and colours are varied, and the roofline is the part of a house you
        /// actually see from a car.
        /// </summary>
        public static Mesh HipRoof(float wide, float depth, float rise, float ridgeFrac = 0.45f)
            => Get($"hip{wide}_{depth}_{rise}_{ridgeFrac}", () =>
            {
                float w = wide * 0.5f, d = depth * 0.5f, r = w * Mathf.Clamp01(ridgeFrac);
                var v = new[]
                {
                    // eaves, clockwise from the front-left
                    new Vector3(-w, 0,  d), new Vector3( w, 0,  d),
                    new Vector3( w, 0, -d), new Vector3(-w, 0, -d),
                    // ridge, running along X
                    new Vector3(-r, rise, 0), new Vector3( r, rise, 0),
                };
                float ru = wide / 1.7f, rv = Mathf.Sqrt(rise * rise + d * d) / 1.5f;
                var uv = new[]
                {
                    new Vector2(0, 0), new Vector2(ru, 0),
                    new Vector2(ru, 0), new Vector2(0, 0),
                    new Vector2(ru * 0.28f, rv), new Vector2(ru * 0.72f, rv),
                };

                var mesh = new Mesh();
                mesh.SetVertices(v);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(new[]
                {
                    0, 1, 5, 0, 5, 4,   // front slope
                    2, 3, 4, 2, 4, 5,   // back slope
                    1, 2, 5,            // right hip
                    3, 0, 4,            // left hip
                }, 0);
                mesh.SetTriangles(System.Array.Empty<int>(), 1);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            });
    }
}
