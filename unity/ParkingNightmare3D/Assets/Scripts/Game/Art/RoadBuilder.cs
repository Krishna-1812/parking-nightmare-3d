using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// The road corridor: carriageway, curbs, sidewalks, cross streets at intersections,
    /// and the painted parking bay.
    ///
    /// Every surface is a strip swept along the compiled centreline between two lateral
    /// offsets, which is the only way to get markings that follow a curved route without
    /// projected decals fighting the surface for depth. Lane paint is baked into the road
    /// texture (see <see cref="ProcTex.Road"/>) so it curves with the ribbon for free.
    /// </summary>
    public static class RoadBuilder
    {
        // Cross-section heights. Physics is 2D (DESIGN_SPEC §2) — none of this is
        // simulated, it exists so the curb reads as something you can mount.
        public const float RoadY = 0.012f;   // just clear of the ground plane at y = 0
        public const float CurbY = 0.16f;
        public const float CurbW = 0.35f;

        /// <summary>Sample spacing along the route. 2 m matches the compiler's own step.</summary>
        const double Stride = 2.0;

        /// <summary>
        /// How far the corridor runs beyond each end of the route.
        ///
        /// <see cref="CompiledRoute.SampleAt"/> clamps to [0, Length] — correct for the
        /// simulation, which has nothing to say about a car that is not on the route — but
        /// it means the road would otherwise stop dead at the start line with a visible
        /// edge onto the lawn. These stations extrapolate straight from the end headings,
        /// so the street runs off into the fog at both ends.
        /// </summary>
        public const double Lead = 120.0;

        public struct Section
        {
            public float RoadHalf;    // RW: lanes * 3.5 + 2.3
            public float WalkOuter;   // RW + curb + sidewalk
        }

        public static Section SectionFor(int lanes)
        {
            float rw = (float)RoadGeom.HalfWidth(lanes);
            return new Section { RoadHalf = rw, WalkOuter = rw + CurbW + (float)RoadGeom.SidewalkW };
        }

        // ------------------------------------------------------------------ strips

        /// <summary>
        /// One quad strip between two offset curves. A and B may differ in elevation as
        /// well as lateral offset, which is what lets the same routine produce both the
        /// flat ribbons and the vertical curb riser.
        ///
        /// Vertex order is (A, B) per station and the triangles wind so that A on the
        /// smaller offset gives an upward normal. That is only correct because
        /// <see cref="WorldBuilder.ToWorld"/> negates Z: the mirror flips handedness and
        /// therefore winding. Change one and you must change the other.
        /// </summary>
        /// <param name="flip">
        /// Reverse the winding. Needed for every strip on the -t side of the road: the
        /// rule above assumes A sits at the smaller offset, and mirroring the cross-section
        /// to the other side of the centreline swaps that. Without it the whole left curb
        /// and sidewalk render back-faced, which shows up as them simply not being there.
        /// </param>
        public static Mesh Strip(CompiledRoute route,
                                 double tA, float yA, double tB, float yB,
                                 float uA, float uB, float metresPerV,
                                 double sStart, double sEnd, bool flip = false)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((float)((sEnd - sStart) / Stride)));

            var verts = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[steps * 6];

            for (int i = 0; i <= steps; i++)
            {
                double s = sStart + (sEnd - sStart) * i / steps;
                PosAtExtended(route, s, tA, out double ax, out double ay);
                PosAtExtended(route, s, tB, out double bx, out double by);
                verts[i * 2] = WorldBuilder.ToWorld(ax, ay, yA);
                verts[i * 2 + 1] = WorldBuilder.ToWorld(bx, by, yB);
                float v = (float)(s / metresPerV);
                uvs[i * 2] = new Vector2(uA, v);
                uvs[i * 2 + 1] = new Vector2(uB, v);

                if (i > 0)
                {
                    int b = (i - 1) * 2, t = (i - 1) * 6;
                    if (flip)
                    {
                        tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                        tris[t + 3] = b + 1; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
                    }
                    else
                    {
                        tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                        tris[t + 3] = b + 1; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
                    }
                }
            }

            var mesh = new Mesh { name = "strip" };
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// <see cref="CompiledRoute.PosAt"/>, but valid outside [0, Length]: past either
        /// end it continues in a straight line along the end heading. Presentation only —
        /// nothing off the route is ever projected onto it by the simulation.
        /// </summary>
        public static void PosAtExtended(CompiledRoute route, double s, double t,
                                         out double x, out double y, out double h)
        {
            double clamped = MathX.Clamp(s, 0.0, route.Length - 0.01);
            route.PosAt(clamped, t, out x, out y, out h);
            double over = s - clamped;
            if (over == 0.0) return;
            x += Math.Cos(h) * over;
            y += Math.Sin(h) * over;
        }

        static void PosAtExtended(CompiledRoute route, double s, double t,
                                  out double x, out double y)
            => PosAtExtended(route, s, t, out x, out y, out _);

        public static GameObject Piece(string name, Transform parent, Mesh mesh, Material mat,
                                       bool castShadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }

        // ------------------------------------------------------------------ corridor

        public static void Build(CompiledRoute route, int lanes, District d, Transform parent)
        {
            var sec = SectionFor(lanes);

            var roadMat = MatLib.Textured("mat_road" + lanes, ProcTex.Road(lanes, d.Night),
                Color.white, new Vector2(1f, 1f), smoothness: 0.16f,
                normal: ProcTex.AsphaltNormal(), normalScale: 0.6f);
            var curbMat = MatLib.Textured("mat_curb", ProcTex.Curb(d.Night),
                Color.white, Vector2.one, smoothness: 0.08f);
            // The footway fills the bottom third of every ground-level shot. Flat, it is
            // four metres of blank paper: the tonal variation baked into the map does
            // nothing at midday, when the sun is behind the camera and nothing on a level
            // surface can shade itself. The joints have to be real relief to read.
            var walkMat = MatLib.Textured("mat_walk", ProcTex.Sidewalk(d.Night),
                Color.white, Vector2.one, smoothness: 0.06f,
                normal: ProcTex.SidewalkNormal(), normalScale: 0.9f);

            // ---- carriageway: one strip, UV u spans the full width, v repeats per 24 m
            Piece("Road", parent,
                  Strip(route, -sec.RoadHalf, RoadY, sec.RoadHalf, RoadY,
                        0f, 1f, ProcTex.RoadRepeatMetres, -Lead, route.Length + Lead),
                  roadMat);

            // ---- curbs and sidewalks, broken at intersections so cross streets connect
            foreach (var (s0, s1) in OpenSpans(route))
                foreach (int sgn in new[] { 1, -1 })
                {
                    double kerbIn = sgn * sec.RoadHalf;
                    double kerbOut = sgn * (sec.RoadHalf + CurbW);
                    double walkOut = sgn * sec.WalkOuter;
                    bool flip = sgn < 0;

                    // riser: vertical face from the gutter up to the curb top.
                    // u spans the curb texture's width (its canvas x is across the curb).
                    Piece($"CurbFace{sgn}", parent,
                          Strip(route, kerbIn, RoadY, kerbIn, CurbY, 0f, 0.5f, 3.0f, s0, s1, flip),
                          curbMat);
                    Piece($"CurbTop{sgn}", parent,
                          Strip(route, kerbIn, CurbY, kerbOut, CurbY, 0.5f, 1f, 3.0f, s0, s1, flip),
                          curbMat);
                    Piece($"Walk{sgn}", parent,
                          Strip(route, kerbOut, CurbY, walkOut, CurbY,
                                0f, (float)RoadGeom.SidewalkW / 4f, 4.0f, s0, s1, flip),
                          walkMat);
                    // outer face down to the lawn, so the slab has thickness from behind
                    Piece($"WalkFace{sgn}", parent,
                          Strip(route, walkOut, CurbY, walkOut, 0f, 0f, 0.2f, 3.0f, s0, s1, flip),
                          curbMat);
                }

            BuildCrossStreets(route, sec, d, parent);
        }

        /// <summary>
        /// Arc spans of open road, i.e. the extended corridor minus the intersections —
        /// the curb and sidewalk have to break where a cross street meets them.
        /// </summary>
        static IEnumerable<(double, double)> OpenSpans(CompiledRoute route)
        {
            double cursor = -Lead;
            foreach (var it in route.Inters)
            {
                if (it.S0 - cursor > 1.0) yield return (cursor, it.S0);
                cursor = it.S1;
            }
            if (route.Length + Lead - cursor > 1.0) yield return (cursor, route.Length + Lead);
        }

        /// <summary>
        /// The crossing street at each intersection: plain asphalt through the junction
        /// (real junctions carry no lane paint) plus a crosswalk band at each mouth.
        /// </summary>
        static void BuildCrossStreets(CompiledRoute route, Section sec, District d, Transform parent)
        {
            if (route.Inters.Count == 0) return;

            var asphMat = MatLib.Textured("mat_asph", ProcTex.PlainAsphalt(d.Night),
                Color.white, new Vector2(6f, 6f), smoothness: 0.16f,
                normal: ProcTex.AsphaltNormal(), normalScale: 0.6f);
            var crossMat = MatLib.Textured("mat_cross", ProcTex.Crosswalk(d.Night),
                Color.white, Vector2.one, smoothness: 0.14f);

            const float ArmLength = 46f;   // how far the side street runs before the fog

            foreach (var it in route.Inters)
            {
                var centre = WorldBuilder.ToWorld(it.Cx, it.Cy, RoadY);
                var rot = WorldBuilder.ToRotation(it.H);
                float along = (float)(it.S1 - it.S0);

                // junction box + the two arms, as one flat quad each.
                // Local +X is lateral and local +Z is travel once ToRotation has been
                // applied, so the arms just extend along local X.
                Quad(parent, "InterBox", centre, rot, along, sec.WalkOuter * 2f,
                     asphMat, new Vector2(sec.WalkOuter * 2f / 8f, along / 8f));
                foreach (int sgn in new[] { 1, -1 })
                {
                    var armCentre = centre + rot * new Vector3(
                        sgn * (sec.WalkOuter + ArmLength * 0.5f), 0, 0);
                    Quad(parent, $"InterArm{sgn}", armCentre, rot, along, ArmLength,
                         asphMat, new Vector2(ArmLength / 8f, along / 8f));
                }

                // crosswalk bands just inside each mouth of the junction; the stripes run
                // across the carriageway, one texture repeat per 4 m of road width
                foreach (int sgn in new[] { 1, -1 })
                {
                    var bandCentre = centre + rot * new Vector3(0, 0.002f, sgn * (along * 0.5f - 1.6f));
                    Quad(parent, $"Crosswalk{sgn}", bandCentre, rot, 2.6f, sec.RoadHalf * 2f,
                         crossMat, new Vector2(sec.RoadHalf * 2f / 4f, 1f));
                }
            }
        }

        /// <summary>
        /// Flat quad in the route's local frame: local +Z is travel, local +X is lateral.
        /// Winding matches <see cref="Strip"/> for the same handedness reason.
        /// </summary>
        static void Quad(Transform parent, string name, Vector3 centre, Quaternion rot,
                         float alongZ, float acrossX, Material mat, Vector2 uvRepeats)
        {
            float hz = alongZ * 0.5f, hx = acrossX * 0.5f;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new[]
            {
                new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz),
                new Vector3(-hx, 0,  hz), new Vector3(hx, 0,  hz),
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0, 0), new Vector2(uvRepeats.x, 0),
                new Vector2(0, uvRepeats.y), new Vector2(uvRepeats.x, uvRepeats.y),
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var go = Piece(name, parent, mesh, mat);
            go.transform.position = centre;
            go.transform.rotation = rot;
        }

        // ------------------------------------------------------------------ the spot

        /// <summary>
        /// The parking bay, painted onto the road rather than floating above it: a hatched
        /// box outline plus a soft glow pad, in the same green the HUD uses for "in
        /// tolerance". The corner posts stay — they are what makes the box read in 3D from
        /// a chase camera, and they are the reference's own affordance.
        /// </summary>
        public static GameObject BuildSpot(ParkingSpot spot, Transform parent)
        {
            var go = new GameObject("ParkingSpot");
            go.transform.SetParent(parent, false);
            go.transform.position = WorldBuilder.ToWorld(spot.X, spot.Y, RoadY + 0.004);
            go.transform.rotation = WorldBuilder.ToRotation(spot.H);

            float hw = (float)spot.Hw, hl = (float)spot.Hl;
            var paint = MatLib.Emissive(new Color(0.16f, 0.62f, 0.32f),
                                        new Color(0.25f, 1f, 0.45f), 0.55f);

            // outline: four painted bars, inset so the tyres sit inside the box
            const float bar = 0.14f;
            Bar(go.transform, paint, new Vector3(0, 0, hl - bar * 0.5f), new Vector3(hw * 2, 0.01f, bar));
            Bar(go.transform, paint, new Vector3(0, 0, -hl + bar * 0.5f), new Vector3(hw * 2, 0.01f, bar));
            Bar(go.transform, paint, new Vector3(hw - bar * 0.5f, 0, 0), new Vector3(bar, 0.01f, hl * 2));
            Bar(go.transform, paint, new Vector3(-hw + bar * 0.5f, 0, 0), new Vector3(bar, 0.01f, hl * 2));

            // fill pad, dim enough to read as paint under the car rather than a light box
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "SpotPad";
            pad.transform.SetParent(go.transform, false);
            UnityEngine.Object.DestroyImmediate(pad.GetComponent<BoxCollider>());
            pad.transform.localScale = new Vector3(hw * 2 - bar * 2, 0.008f, hl * 2 - bar * 2);
            pad.transform.localPosition = new Vector3(0, -0.001f, 0);
            pad.GetComponent<MeshRenderer>().sharedMaterial =
                MatLib.Emissive(new Color(0.10f, 0.34f, 0.18f), new Color(0.16f, 0.7f, 0.30f), 0.18f);
            pad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? 1 : -1;
                float sz = (i < 2) ? 1 : -1;
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = $"Post{i}";
                post.transform.SetParent(go.transform, false);
                UnityEngine.Object.DestroyImmediate(post.GetComponent<BoxCollider>());
                post.transform.localScale = new Vector3(0.11f, 1.15f, 0.11f);
                post.transform.localPosition = new Vector3(sx * hw, 0.575f, sz * hl);
                post.GetComponent<MeshRenderer>().sharedMaterial =
                    MatLib.Emissive(new Color(0.18f, 0.7f, 0.35f), new Color(0.3f, 1f, 0.5f), 1.1f);
                post.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            return go;
        }

        static void Bar(Transform parent, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SpotEdge";
            go.transform.SetParent(parent, false);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
