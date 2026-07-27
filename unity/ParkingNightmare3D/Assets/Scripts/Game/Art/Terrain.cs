using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// The ground the world stands on.
    ///
    /// It used to be two triangles: a 1.3 km square of flat lawn with one grass tile per
    /// 26 m. That is the single largest surface on screen and it was doing nothing — no
    /// form, no shading variation, no horizon other than the fog, so the street read as a
    /// strip laid on a billiard table and every house looked pasted onto it.
    ///
    /// This replaces it with a height field. Two things follow from that and both matter
    /// more than the mesh itself:
    ///
    /// - <see cref="HeightAt"/> IS the ground, and everything that stands on the ground asks
    ///   it where to sit. Scenery used to be placed at a constant elevation, which is only
    ///   correct while the ground is a plane. One function, sampled by the mesh builder and
    ///   by every prop, is the same discipline the car's shell needed: an approximation of a
    ///   surface, used alongside the surface, drifts off it.
    ///
    /// - The road corridor stays exactly flat. Physics is 2D and knows only the road
    ///   cross-section (DESIGN_SPEC §2, §8); the carriageway, kerbs and footways are drawn
    ///   at fixed elevations, so any relief under them would tear the strip open. The
    ///   amplitude is held at zero out to the far edge of the front gardens and eased in
    ///   past them, which is also where a real street's ground starts to move.
    /// </summary>
    public sealed class Terrain
    {
        /// <summary>Ground is dead flat within this distance of the centreline.</summary>
        const float FlatTo = 11f;
        /// <summary>...and reaches full relief this much further out.</summary>
        const float Fade = 46f;

        /// <summary>
        /// Broad swells: metres of rise over roughly a 130 m wavelength. Gentle enough that
        /// the street still reads as flat suburbia, steep enough that the sun finds a light
        /// and a dark side of every rise — which is the whole point, since the relief is
        /// almost never seen in silhouette and almost always seen as shading.
        /// </summary>
        const float SwellAmp = 4.6f;
        const float SwellFreq = 0.0076f;
        /// <summary>Lumps and hollows on top of the swells.</summary>
        const float LumpAmp = 0.95f;
        const float LumpFreq = 0.031f;

        /// <summary>One grass tile per this many metres.</summary>
        const float TileM = 26f;

        readonly Vector2[] _spine;

        Terrain(Vector2[] spine) { _spine = spine; }

        // ------------------------------------------------------------------ the field

        /// <summary>Shortest distance in the ground plane from a point to the road centreline.</summary>
        public float DistToRoad(float x, float z)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _spine.Length; i++)
            {
                float dx = x - _spine[i].x, dz = z - _spine[i].y;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>
        /// Ground elevation at a world point. Zero over the whole road corridor and the
        /// gardens either side of it, so the strip the road builder draws is never
        /// disturbed and a house never has to stand on a slope it was not built for.
        /// </summary>
        public float HeightAt(float x, float z)
        {
            float k = Smooth01(Mathf.InverseLerp(FlatTo, FlatTo + Fade, DistToRoad(x, z)));
            if (k <= 0f) return 0f;
            return k * (Fbm(x * SwellFreq, z * SwellFreq, 3) * SwellAmp
                      + Fbm(x * LumpFreq + 31.7f, z * LumpFreq - 12.4f, 2) * LumpAmp);
        }

        static float Smooth01(float t) => t * t * (3f - 2f * t);

        /// <summary>Hash-based value noise. Deterministic, no allocation, no lookup table.</summary>
        static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = Smooth01(xf), v = Smooth01(yf);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), u),
                              Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), u), v);
        }

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 8388608f - 1f;   // -1..1
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(x, y) * amp;
                norm += amp;
                amp *= 0.5f;
                x *= 2.03f; y *= 2.01f;
            }
            return sum / Mathf.Max(1e-4f, norm);
        }

        // ------------------------------------------------------------------ the mesh

        /// <summary>
        /// Grid resolution. The relief is long-wavelength, so this is set by how smoothly
        /// the ground shades and receives shadow near the camera, not by the height field.
        /// </summary>
        const int N = 97;

        /// <summary>
        /// How sharply the grid concentrates toward the middle. The ground has to reach the
        /// horizon ring 650 m out, and a uniform grid fine enough to look right under the
        /// car would spend a quarter of a million triangles on fog. Grading it quadratically
        /// gives about 3.5 m spacing where the car is and 18 m at the rim, in one mesh with
        /// no seam to hide.
        /// </summary>
        const float GridLinear = 0.25f;

        public static Terrain Build(CompiledRoute route, District d,
                                    Vector3 centre, float span, Transform parent)
        {
            var t = new Terrain(Spine(route));

            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            go.transform.position = centre;

            float half = span * 0.5f;
            var verts = new Vector3[N * N];
            var uvs = new Vector2[N * N];
            var cols = new Color32[N * N];
            var tris = new int[(N - 1) * (N - 1) * 6];

            var axis = new float[N];
            for (int i = 0; i < N; i++)
            {
                float s = i / (float)(N - 1) * 2f - 1f;
                axis[i] = Mathf.Sign(s) * (GridLinear * Mathf.Abs(s)
                                         + (1f - GridLinear) * s * s) * half;
            }

            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float lx = axis[i], lz = axis[j];
                    float wx = centre.x + lx, wz = centre.z + lz;
                    verts[j * N + i] = new Vector3(lx, t.HeightAt(wx, wz), lz);

                    // UVs come from world position, so the tile does not swim when the
                    // grid spacing changes across the mesh, and are warped by a slow noise
                    // so the 26 m repeat stops reading as a grid. The warp is under half a
                    // tile, which is enough to break the pattern and not enough to smear.
                    float wu = Fbm(wx * 0.0031f, wz * 0.0031f, 2) * 0.45f;
                    float wv = Fbm(wx * 0.0029f + 55f, wz * 0.0033f - 21f, 2) * 0.45f;
                    uvs[j * N + i] = new Vector2(wx / TileM + wu, wz / TileM + wv);
                    cols[j * N + i] = t.Cover(wx, wz);
                }

            int k = 0;
            for (int j = 0; j < N - 1; j++)
                for (int i = 0; i < N - 1; i++)
                {
                    int a = j * N + i, b = a + 1, c = a + N, e = c + 1;
                    tris[k++] = a; tris[k++] = c; tris[k++] = b;
                    tris[k++] = b; tris[k++] = c; tris[k++] = e;
                }

            var mesh = new Mesh { name = "ground", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GroundMat(d);
            // Casting from the ground would only ever self-shadow its own swells at grazing
            // sun, which on a 650 m mesh costs a shadow map full of nothing.
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;

            return t;
        }

        // ------------------------------------------------------------------ cover

        /// <summary>
        /// What is growing at a point, baked into the mesh as a vertex colour.
        ///
        /// <c>r</c> is lushness and <c>g</c> is bare earth. Both are large-scale — the
        /// lushness field turns over about every eighty metres and the worn patches about
        /// every thirty — because the point of them is the variation the *texture* cannot
        /// have. A 26 m tile can only repeat; a field that changes over eighty metres
        /// cannot be a tile at all, and it is the absence of that scale of change that
        /// made six hundred metres of lawn read as one painted carpet.
        ///
        /// Worn earth is pushed away from the road corridor deliberately. Bare patches
        /// look like desire lines and mud, which belong out in the paddocks, not on the
        /// mown verge the player is parking against.
        /// </summary>
        Color32 Cover(float x, float z)
        {
            float lush = Mathf.Clamp01(Fbm(x * 0.0128f + 4.2f, z * 0.0128f - 7.7f, 3) * 0.5f + 0.5f);
            lush = Smooth01(Mathf.InverseLerp(0.24f, 0.78f, lush));

            float worn = Fbm(x * 0.0335f - 19.3f, z * 0.0335f + 2.6f, 2) * 0.5f + 0.5f;
            worn = Smooth01(Mathf.InverseLerp(0.70f, 0.90f, worn)) * 0.55f;
            worn *= Smooth01(Mathf.InverseLerp(FlatTo + 6f, FlatTo + 40f, DistToRoad(x, z)));

            return new Color32((byte)(lush * 255f), (byte)(worn * 255f), 0, 255);
        }

        /// <summary>
        /// The ground material. Not <see cref="MatLib.Textured"/>, because the grass wants
        /// vertex colour and a second sampling rate that URP/Lit has no way to give it —
        /// see the header of <c>PN3D_Ground.shader</c>.
        /// </summary>
        static Material GroundMat(District d) => MatLib.Get("mat_ground" + d.GroundAHex, () =>
        {
            var sh = Shader.Find("PN3D/Ground");
            if (sh == null)
                throw new System.InvalidOperationException(
                    "Shader 'PN3D/Ground' is not in this build. It is created at runtime like " +
                    "every other material here, so add it to Always Included Shaders in " +
                    "ProjectSettings/GraphicsSettings.asset.");

            var m = new Material(sh);
            m.SetTexture("_BaseMap", ProcTex.Grass(d.GroundAHex, d.GroundBHex));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_MacroScale", 0.21f);
            m.SetFloat("_MacroDepth", 0.62f);
            // Gains, not colours — they multiply the grass map, and their midpoint sits at
            // white so the field keeps the district's own green. Dry runs warm and a shade
            // brighter, lush runs darker and greener; both pull blue down, which is what
            // takes the milkiness out of a fogged field.
            m.SetColor("_DryColor", new Color(1.22f, 1.12f, 0.80f));
            m.SetColor("_LushColor", new Color(0.78f, 0.94f, 0.66f));
            m.SetColor("_WornColor", new Color(0.40f, 0.34f, 0.25f));
            m.SetFloat("_Smoothness", 0.04f);
            return m;
        });

        /// <summary>
        /// The road centreline in world XZ, sampled about every four metres over the whole
        /// corridor the road builder draws — leads included, or the ground would rise
        /// through the tarmac just past each end of the route.
        /// </summary>
        static Vector2[] Spine(CompiledRoute route)
        {
            var pts = new List<Vector2>();
            double from = -RoadBuilder.Lead - 8, to = route.Length + RoadBuilder.Lead + 8;
            for (double s = from; s <= to; s += 4.0)
            {
                RoadBuilder.PosAtExtended(route, s, 0, out double x, out double y, out _);
                var w = WorldBuilder.ToWorld(x, y);
                pts.Add(new Vector2(w.x, w.z));
            }
            return pts.ToArray();
        }
    }
}
