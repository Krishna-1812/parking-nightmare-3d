using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// The vehicle models, ported from <c>CarFactory.hull</c> and
    /// <c>CarFactory.standard</c> in <c>src/n3_c.js</c>.
    ///
    /// The hull is a densely segmented box pushed through two superellipses (one in plan
    /// for the nose and tail, one in cross-section for the sills and roof edges), then a
    /// roofline curve, end taper and tumblehome, then smooth normals. That single
    /// generator is what separates this from a stack of boxes: the body is one seamless
    /// painted shell with rounded corners, and the canopy is a second shell sunk into it.
    ///
    /// AXES. The reference builds cars with +X forward and +Z lateral. Here the car root
    /// is oriented by <see cref="WorldBuilder.ToRotation"/>, which puts local +Z along the
    /// direction of travel — so every ported coordinate swaps X and Z. Getting that
    /// backwards produces a car that drives sideways, which is at least easy to spot.
    /// </summary>
    public static class CarView
    {
        public sealed class Rig
        {
            public Transform Root;
            public Transform Body;           // pitch/roll go here, not on the root
            public Transform[] Steer;        // front wheel yaw groups
            public Transform[] WheelSpin;    // all four, spun about local X
            public float WheelRadius;
            public Material BrakeLight;      // instanced, so it can flare under braking
        }

        // ------------------------------------------------------------------ hull

        public struct HullOpts
        {
            public string Key;
            public float PCross, PPlan, Tumble, WNose, WTail;
            public System.Func<float, float> Top, Bot;

            /// <summary>
            /// Per-quad cutter. Returns true to emit the quad, and is handed <c>u</c> along
            /// the length (0 tail, 1 nose), the quad's centroid in the shell's own local
            /// space, and its centroid in CUBE space. Null keeps everything.
            ///
            /// The cube coordinate is there so a cut can ask how far around the section it
            /// is — <c>atan2(|qLat|, qy)</c>, which is 0 at the crown, pi/2 at the waist and
            /// pi at the floor. That was not the first attempt. The first used the deformed
            /// surface normal, on the reasoning that where the roof turns over is a question
            /// about which way the surface faces; it is, but the normal is not continuous
            /// across the cube's face seams, and the shell is six grids of different pitch
            /// welded together. Along the shoulder, where the roof grid (28 by 80) meets the
            /// flank grid (80 by 24), |n.x| wanders back and forth over any fixed threshold
            /// and the two grids interleave — which is what the torn edge round every rear
            /// window was. The cube angle is monotone, and it is identical on both sides of
            /// a seam, because a seam is only where one coordinate stops being 1 and another
            /// starts. Raising mesh density against this does nothing: it was tried twice
            /// and each time the teeth got smaller and stayed.
            ///
            /// The centroid is a POSITION and not the normalised height y01, which was the
            /// first thing tried and is the same mistake the shoulder crease made: y01 is a
            /// fraction of the local section, and the cabin's section at the tail is a third
            /// its height at the peak. A beltline set at 0.17 of it therefore sat thirty
            /// millimetres above the cabin floor back there, so the whole rounded tail cap
            /// counted as backlight — measured, 0.72 m² of it against 0.41 m² of side glass
            /// for both flanks together. A beltline is a height. It has to be given as one.
            ///
            /// The normal is there because a cabin's roof and its flank are not separable by
            /// any coordinate. On a rounded section the side curves up and the roof curves
            /// down and they meet without either reaching a distinguishing value of y01 —
            /// cut the side glass at a height and on a round-shouldered archetype the window
            /// carries on over the crown and comes out as a stripe across the roof. Where
            /// the roof turns over is a question about which way the surface faces, so ask
            /// that: it is exactly what a cant rail is, and it needs no per-style tuning.
            ///
            /// This is what makes a greenhouse possible. A cabin is painted structure with
            /// windows cut into it; modelled as a single shell it can only ever be all glass
            /// or all paint, and the "black bubble" every archetype wore was that choice
            /// showing. There is no boolean cutter available here, but there does not need
            /// to be one: the shell is generated triangle by triangle, so the openings are
            /// simply the triangles we decline to emit. The same hull is then built twice —
            /// once keeping the paint, once keeping the glass — and the two interlock
            /// exactly, because they are the same surface sampled by the same function.
            /// </summary>
            public System.Func<float, Vector3, Vector3, bool> Keep;

            /// <summary>Reverses winding, so the shell renders as the inside of itself.</summary>
            public bool Invert;

            /// <summary>
            /// Mesh density: lateral, vertical, lengthwise. Zero means the default.
            /// The cabin needs a finer vertical grid than the body because its cuts run
            /// horizontally — a beltline and a cant rail land between the body's eight rows.
            /// </summary>
            public int SLat, SY, SLen;
        }

        static float S3(float a, float b, float t)
        {
            t = Mathf.Clamp01((t - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Wheel-arch profile: 1 at the axle, 0 at the edges of the opening.
        ///
        /// The exponent shapes the arch. A plain smooth bump reads as a sag in the sill;
        /// a true semicircle (0.5) meets the rocker at a vertical tangent and facets badly
        /// at this mesh density. 0.75 keeps the round crown of an arch while landing on the
        /// rocker at an angle the segments can actually follow.
        /// </summary>
        static float ArchHump(float u, float centre, float halfWidth)
        {
            float d = Mathf.Abs(u - centre) / Mathf.Max(1e-4f, halfWidth);
            if (d >= 1f) return 0f;
            return Mathf.Pow(1f - d * d, 0.75f);
        }

        /// <summary>
        /// One point of the deformed shell, from a cube coordinate to its final position.
        ///
        /// THIS IS THE ONLY PLACE THE SHELL'S SHAPE IS DEFINED, and it is public for a
        /// reason worth spelling out, because getting it wrong cost several rounds of
        /// "fixes" that each looked like progress.
        ///
        /// Everything bolted onto the cabin — pillars, cant rails, the roof cap, roof rails
        /// — has to be positioned ON the glass surface. The placement code used to compute
        /// that position with its own simplified formula: half-width times (1 - Tumble) for
        /// the roof edge, a hand-written Taper() for the ends. That formula is not the
        /// deformation. It ignores the cross-section superellipse entirely, which at the
        /// roof edge pulls the surface in by 2^(-1/PCross) BEFORE tumblehome is applied on
        /// the already-reduced height. For the coupe the shortcut says 0.680 of half-width
        /// and the real surface is at 0.591 — the frame sat 15% outboard of the glass it
        /// was supposed to be holding, which is exactly the gap that made every archetype
        /// read as a roll cage floating over a bubble.
        ///
        /// Two implementations of one surface will always drift apart. There is now one,
        /// and <see cref="SurfaceAtU"/> samples it, so a pillar cannot float by construction.
        /// </summary>
        public static Vector3 Deform(HullOpts o, float len, float hgt, float wid,
                                     float qLat, float qy, float qLen,
                                     out float u, out float y01)
        {
            float pC = o.PCross, pP = o.PPlan;
            var top = o.Top ?? (_ => 1f);
            var bot = o.Bot ?? (_ => 0f);

            // plan-view corner rounding (nose and tail)
            float m = Mathf.Max(Mathf.Abs(qLen), Mathf.Abs(qLat));
            if (m > 1e-4f)
            {
                float pn = Mathf.Pow(Mathf.Pow(Mathf.Abs(qLen), pP) +
                                     Mathf.Pow(Mathf.Abs(qLat), pP), 1f / pP);
                float k = m / pn; qLen *= k; qLat *= k;
            }

            // cross-section corner rounding (sills, roof edges)
            m = Mathf.Max(Mathf.Abs(qy), Mathf.Abs(qLat));
            if (m > 1e-4f)
            {
                float pn = Mathf.Pow(Mathf.Pow(Mathf.Abs(qy), pC) +
                                     Mathf.Pow(Mathf.Abs(qLat), pC), 1f / pC);
                float k = m / pn; qy *= k; qLat *= k;
            }

            u = qLen * 0.5f + 0.5f;   // 0 = tail, 1 = nose
            qLat *= Mathf.Lerp(o.WTail, 1f, Mathf.Clamp01(u / 0.35f))
                  * Mathf.Lerp(o.WNose, 1f, Mathf.Clamp01((1f - u) / 0.35f));

            // tumblehome: the upper body leans inward like real sheet metal
            y01 = qy * 0.5f + 0.5f;
            qLat *= 1f - o.Tumble * Mathf.Pow(Mathf.Max(0f, y01 - 0.35f) / 0.65f, 1.6f);

            float yy = bot(u) + y01 * (top(u) - bot(u));
            return new Vector3(qLat * 0.5f * wid, (yy - 0.5f) * hgt, qLen * 0.5f * len);
        }

        /// <summary>
        /// Where the shell's surface actually is at a target position along its length.
        ///
        /// <paramref name="qy"/> selects the height in cube space: +1 the roof, -1 the sill.
        /// <paramref name="side"/> selects the flank: +1, -1, or 0 for the centreline crown.
        ///
        /// Solved by sampling rather than inverted algebraically, because u falls out of the
        /// plan superellipse and is not analytically invertible in closed form. 192 samples
        /// at build time is free and, unlike an approximation, cannot be subtly wrong.
        /// </summary>
        public static Vector3 SurfaceAtU(HullOpts o, float len, float hgt, float wid,
                                         float targetU, float qy, float side)
        {
            const int N = 192;
            Vector3 best = Vector3.zero;
            float bestErr = float.MaxValue;
            for (int i = 0; i <= N; i++)
            {
                float qLen = -1f + 2f * i / N;
                var p = Deform(o, len, hgt, wid, side, qy, qLen, out float uu, out _);
                float e = Mathf.Abs(uu - targetU);
                if (e < bestErr) { bestErr = e; best = p; }
            }
            return best;
        }

        /// <summary>
        /// The flank surface at a station along the length and a given HEIGHT, rather than a
        /// given cube coordinate.
        ///
        /// The distinction matters once the sill has wheel arches in it. A cube-space height
        /// is a fraction of the local section, and over an arch that section is squeezed to
        /// a third of its depth — so a trim line held at constant qy climbs the arch, rides
        /// over the wheel and exits at the nose as a horizontal blade hanging off the front
        /// of the car. A shoulder crease is a roughly level line down the side; it has to be
        /// specified in height, and only its lateral position taken from the shell.
        ///
        /// Solved in two 1-D passes, not one 2-D one: u falls out of the plan superellipse,
        /// which never sees qy, so the station can be found once and the height scanned
        /// along it. That matters because this runs per car at load, not in the editor.
        /// </summary>
        public static Vector3 FlankAtHeight(HullOpts o, float len, float hgt, float wid,
                                            float targetU, float localY, float side)
        {
            const int NZ = 128, NY = 64;

            float bestQLen = 0f, bestUErr = float.MaxValue;
            for (int j = 0; j <= NZ; j++)
            {
                float qLen = -1f + 2f * j / NZ;
                Deform(o, len, hgt, wid, side, 0f, qLen, out float uu, out _);
                float e = Mathf.Abs(uu - targetU);
                if (e < bestUErr) { bestUErr = e; bestQLen = qLen; }
            }

            Vector3 best = Vector3.zero;
            float bestYErr = float.MaxValue;
            for (int i = 0; i <= NY; i++)
            {
                float qy = -1f + 2f * i / NY;
                var p = Deform(o, len, hgt, wid, side, qy, bestQLen, out _, out _);
                float e = Mathf.Abs(p.y - localY);
                if (e < bestYErr) { bestYErr = e; best = p; }
            }
            return best;
        }


        /// <summary>
        /// Segmented box deformed into a car shell. Vertices are welded across the cube's
        /// face boundaries before deforming, so the smooth normals really are seamless —
        /// the reference relies on Three.js's own box being indexed for the same effect.
        /// </summary>
        public static Mesh Hull(float len, float hgt, float wid, HullOpts o) =>
            Geo.Get($"hull_{o.Key}", () =>
            {
                // SegLen carries the wheel arches. An arch spans about 19% of the length,
                // so at the old 28 it had five segments to round itself off with and came
                // out visibly faceted; 48 gives it nine or ten.
                int SegLen = o.SLen > 0 ? o.SLen : 48;
                int SegY = o.SY > 0 ? o.SY : 8;
                int SegLat = o.SLat > 0 ? o.SLat : 12;
                var (cube, tris) = WeldedBox(SegLat, SegY, SegLen);

                var verts = new Vector3[cube.Count];
                var uvs = new Vector2[cube.Count];

                for (int i = 0; i < cube.Count; i++)
                {
                    // cube space in [-1, 1]: x lateral, y up, z along the length
                    verts[i] = Deform(o, len, hgt, wid,
                                      cube[i].x * 2f, cube[i].y * 2f, cube[i].z * 2f,
                                      out float u, out float y01);
                    // planar UVs down the flank, which is the only face the panel-detail
                    // texture needs to land on correctly
                    uvs[i] = new Vector2(u, y01);
                }

                // The cutter runs per QUAD — six indices at a time — and not per triangle.
                //
                // Per triangle it tears. A quad's two triangles have different centroids, so
                // along any boundary that crosses the grid at an angle one is kept and the
                // other dropped, and the opening comes out with a sawtooth one quad deep and
                // one quad wide. It is not subtle: at the header, where the cut runs across
                // the roof and the chase camera looks straight at it, every archetype had a
                // torn-paper edge along the top of its backlight. Deciding once per quad
                // makes the worst case a clean staircase instead, and a staircase of this
                // pitch disappears into the smooth normals.
                //
                // WeldedBox always emits a quad as two consecutive triangles, so the grouping
                // is safe to assume; the union of their indices is the quad's four corners.
                if (o.Keep != null)
                {
                    var kept = new List<int>(tris.Count);
                    for (int q = 0; q + 5 < tris.Count; q += 6)
                    {
                        int a = tris[q], b = tris[q + 1], c = tris[q + 2], d = tris[q + 4];
                        if (d == a || d == b || d == c) d = tris[q + 5];
                        float u = (uvs[a].x + uvs[b].x + uvs[c].x + uvs[d].x) * 0.25f;
                        var p = (verts[a] + verts[b] + verts[c] + verts[d]) * 0.25f;
                        var qc = (cube[a] + cube[b] + cube[c] + cube[d]) * 0.5f;  // to [-1, 1]
                        if (!o.Keep(u, p, qc)) continue;
                        for (int k = 0; k < 6; k++) kept.Add(tris[q + k]);
                    }
                    tris = kept;
                }

                if (o.Invert)
                    for (int t = 0; t < tris.Count; t += 3)
                        (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);

                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            });

        /// <summary>
        /// Unit cube surface as a welded vertex soup: six segmented grids sharing their
        /// edge vertices, so nothing splits open when the deformation moves them.
        /// </summary>
        static (List<Vector3>, List<int>) WeldedBox(int sx, int sy, int sz)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var index = new Dictionary<(int, int, int), int>();

            int Vert(float x, float y, float z)
            {
                var key = (Mathf.RoundToInt(x * 10000), Mathf.RoundToInt(y * 10000), Mathf.RoundToInt(z * 10000));
                if (index.TryGetValue(key, out int at)) return at;
                verts.Add(new Vector3(x, y, z));
                index[key] = verts.Count - 1;
                return verts.Count - 1;
            }

            // face: origin corner plus two edge vectors, gridded; `flip` fixes winding so
            // every face ends up pointing out of the cube
            void Face(Vector3 origin, Vector3 du, Vector3 dv, int nu, int nv, bool flip)
            {
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        float u0 = (float)i / nu, u1 = (float)(i + 1) / nu;
                        float v0 = (float)j / nv, v1 = (float)(j + 1) / nv;
                        int a = Vert(origin.x + du.x * u0 + dv.x * v0, origin.y + du.y * u0 + dv.y * v0, origin.z + du.z * u0 + dv.z * v0);
                        int b = Vert(origin.x + du.x * u1 + dv.x * v0, origin.y + du.y * u1 + dv.y * v0, origin.z + du.z * u1 + dv.z * v0);
                        int c = Vert(origin.x + du.x * u1 + dv.x * v1, origin.y + du.y * u1 + dv.y * v1, origin.z + du.z * u1 + dv.z * v1);
                        int d = Vert(origin.x + du.x * u0 + dv.x * v1, origin.y + du.y * u0 + dv.y * v1, origin.z + du.z * u0 + dv.z * v1);
                        if (flip) { tris.AddRange(new[] { a, c, b, a, d, c }); }
                        else { tris.AddRange(new[] { a, b, c, a, c, d }); }
                    }
            }

            const float h = 0.5f;
            var X = new Vector3(1, 0, 0); var Y = new Vector3(0, 1, 0); var Z = new Vector3(0, 0, 1);

            // WINDING. A face's outward normal is cross(du, dv), and `flip` negates it.
            // Four of these six were inside out, which is not a subtle defect: back-face
            // culling then deletes the flanks and the roof, so the car renders as a floor
            // pan with a nose and a tail and you look straight through where the doors
            // should be. It reads as a chassis with no body on it, because that is
            // literally what is left.
            //
            //   +Z  X*Y = +Z   keep
            //   -Z  X*Y = +Z   flip
            //   +X  Z*Y = -X   flip   <- was keep
            //   -X  Z*Y = -X   keep   <- was flip
            //   +Y  X*Z = -Y   flip   <- was keep
            //   -Y  X*Z = -Y   keep   <- was flip
            //
            // Worth stating why it hid for so long: the two Z faces were correct, so the
            // nose and tail were solid and the grille, lamps and bumpers all looked right.
            // Only the sides and roof were missing, and the bright plane left behind is the
            // inside of the floor — whose inverted normal points up at the camera, so it
            // lights convincingly. It was mistaken twice for a sky reflection washing out
            // the flanks.
            Face(new Vector3(-h, -h, h), X, Y, sx, sy, false);    // +Z
            Face(new Vector3(-h, -h, -h), X, Y, sx, sy, true);    // -Z
            Face(new Vector3(h, -h, -h), Z, Y, sz, sy, true);     // +X
            Face(new Vector3(-h, -h, -h), Z, Y, sz, sy, false);   // -X
            Face(new Vector3(-h, h, -h), X, Z, sx, sz, true);     // +Y
            Face(new Vector3(-h, -h, -h), X, Z, sx, sz, false);   // -Y

            return (verts, tris);
        }

        // ------------------------------------------------------------------ assembly

        /// <summary>
        /// A car in a given <see cref="CarStyle"/>: painted hull, glass canopy, four wheels
        /// with the front pair on steering groups, the style's light signature, grille,
        /// bumpers, mirrors, plates and whatever extras the archetype carries.
        ///
        /// Length and width are the caller's, always. Those two numbers are simulation
        /// state — TrafficSystem sizes its gaps from them — so a style may reshape a roof
        /// but never the footprint the physics tests.
        /// </summary>
        public static Rig Build(Transform parent, string key, VehicleDef veh,
                                CarStyle st, Color bodyC)
        {
            float len = (float)veh.Len, wid = (float)veh.Wid;
            float bodyH = st.BodyH, cabHeight = st.CabHeight, wheelR = st.WheelR;
            float cabLen = len * st.CabLenFrac, cabOff = len * st.CabOffFrac;
            float baseY = wheelR - 0.05f;
            // cabW is not set here any more; it is measured off the body's own shoulder
            // once BODY() exists. See the note there.
            float half = len * 0.5f;

            var root = new GameObject("Car_" + key).transform;
            root.SetParent(parent, false);
            var body = new GameObject("Body").transform;
            body.SetParent(root, false);

            // ---- painted shell ----
            // The style key is part of the mesh cache key. Without it two archetypes that
            // happen to share a length, height and width — an exec and a patrol car, say —
            // would silently be handed the same hull and every visual difference between
            // them would vanish.
            // WHEEL ARCHES, CUT INTO THE BODY RATHER THAN DRAWN ON IT.
            //
            // The sill used to run dead flat from u 0.20 to 0.80 — straight past both axles
            // — so the shell was a constant-height extrusion over the entire wheelbase and
            // the wheels were slots in a slab. Measured on the player hatch, the tyre stood
            // proud of the flank by four millimetres at mid-height: the bodywork ran down
            // past the wheel in the same plane as it, which no car does and which is most of
            // why these read as blocks on castors.
            //
            // Lifting the sill over each axle makes the shell itself arch, so the opening is
            // real geometry with the fender turning down around it on both sides. The lift
            // clears the top of the tyre — anything less and the tyre's crown is hidden
            // behind sheet metal, which looks like the wheel is punching through the wing.
            var baseSill = st.Sill();
            var deckFn = st.Deck();
            float archLift = Mathf.Clamp01((2f * wheelR + 0.035f - baseY) / bodyH);
            float archU = Mathf.Clamp(wheelR * 1.18f / len, 0.05f, 0.20f);

            var bdOpts = new HullOpts
            {
                Key = $"bd_{st.Key}_{len}_{bodyH}_{wid}_{archLift:0.000}_{archU:0.000}",
                PCross = st.PCross, PPlan = st.PPlan, Tumble = st.Tumble,
                WNose = st.WNose, WTail = st.WTail,
                Top = deckFn,
                // Axles sit at +/- 0.32 of the length, so u 0.82 and 0.18. Max, not sum, so
                // overlapping arches on a very short wheelbase cannot cut twice as deep. The
                // final clamp guarantees the section never collapses to nothing however
                // large a wheel an archetype asks for.
                Bot = u =>
                {
                    float v = baseSill(u);
                    float lift = archLift * Mathf.Max(ArchHump(u, 0.82f, archU),
                                                      ArchHump(u, 0.18f, archU));
                    return Mathf.Min(Mathf.Max(v, lift), Mathf.Max(v, deckFn(u) - 0.11f));
                },
            };
            var hull = Hull(len, bodyH, wid, bdOpts);
            var paint = MatLib.CarPaint(
                $"mat_paint{ColorUtility.ToHtmlStringRGB(bodyC)}_{st.PaintMetallic:0.00}_{st.PaintSmooth:0.00}",
                Color.white, ProcTex.CarSide(bodyC), st.PaintMetallic, st.PaintSmooth);
            var shellPos = new Vector3(0, baseY + bodyH / 2f, 0);
            Geo.Node("Shell", body, hull, paint, shellPos);

            // The painted shell's real surface, same contract as GLASS below: anything that
            // lands on the bonnet or the boot lid asks this rather than assuming the body is
            // a flat-topped box. It is not — Deck() rakes the bonnet down and drops the boot
            // — so "baseY + bodyH" is the top of the bounding box, not of the car. The rear
            // spoiler was pinned there and floated 99 mm clear of the boot it was bolted to.
            Vector3 BODY(float u, float qy, float side)
                => shellPos + SurfaceAtU(bdOpts, len, bodyH, wid, u, qy, side);

            // ---- the cabin: painted structure with windows cut into it ----
            float canLen = cabLen + cabHeight * 0.55f;
            float canH = cabHeight * 0.92f;

            // CABIN WIDTH, taken off the body instead of off the bounding box.
            //
            // It used to be wid * CabWidFrac, then scaled again by the lateral glass inset:
            // 0.86 * 0.93 = 80% of full beam. But full beam is not what the cabin sits on.
            // The body has its own tumblehome and its own cross-section superellipse, so by
            // the time the flank reaches the deck it has already pulled in — and the cabin
            // was then inset from a width the car never has. The result is the pod-on-a-
            // flatbed look: 175 mm of bare deck down each side, which reads as a pickup with
            // a canopy rather than as a car with a roof.
            //
            // A cabin's base is the body's shoulder. So ask the body where its shoulder is.
            // Same rule as everything else on this model now: sample the surface, do not
            // reconstruct it.
            float shoulderHalf = Mathf.Abs(BODY(st.RoofPeak, 0.86f, 1f).x);
            float cabW = 2f * Mathf.Max(0.2f, shoulderHalf - wid * 0.012f) * st.CabWidFrac;

            // Plateau edges are where the roof stops being flat, which is exactly where the
            // A and C pillars stand. The cutter needs them, so they are computed up here.
            float uFront = Mathf.Clamp01(st.RoofPeak + st.RoofFlat * (1f - st.RoofPeak));
            float uRear = Mathf.Clamp01(st.RoofPeak - st.RoofFlat * st.RoofPeak);

            // THE GREENHOUSE, and why it is cut rather than framed.
            //
            // Every previous attempt built the cabin as one glass dome and then laid painted
            // parts on top of it — a roof cap, then pillars, then cant rails, then headers.
            // Each pass covered more of the dome and each pass left the car still wearing a
            // black bubble, because the approach cannot win: whatever is not covered is
            // glass, and a curved dome always shows more of itself than a set of bars can
            // hide. The first device screenshot settled it. From the chase camera the whole
            // upper half of the car was a void, on grass and on asphalt alike.
            //
            // A cabin is bodywork with holes in it, so cut holes in bodywork. Below is the
            // window layout in the shell's own coordinates; the paint shell keeps its
            // complement and the glass shell keeps it, so pillars, cant rail and beltline
            // are all bodywork that was simply never removed — on the surface by
            // construction, at no cost, and impossible to leave a gap.
            // Angles around the section, in radians: 0 is the crown, pi/2 the waist. The
            // cube's own corner — the shoulder — is at 45 degrees, so a cant rail just past
            // it and a quarter-panel edge a little further round are what these are.
            //
            // Tuned by measuring glazed area per opening rather than by eye, because the
            // eye is exactly what got this wrong for three passes. These give side glass
            // 0.51 m², windscreen 0.42 and backlight 0.38 — side glass largest, which is
            // the order a hatchback actually has. The first guess had it last, at 0.30
            // against 0.94 of screens.
            const float RailA = 0.88f;      // cant rail: side glass starts below this
            const float QuartA = 1.00f;     // rear and front quarter panels start beyond it
            const float PillarU = 0.042f;   // half-width of the A and C pillars, in u
            const float BPillU = 0.032f;    // half-width of the B-pillar

            // Headers and pillars are cut on the CUBE's z — not on the deformed z, and not
            // on u. This is the last of the torn edges and the one that took longest to see.
            //
            // Measured, along one row of the roof grid the deformed z swings 120 mm and is
            // NOT MONOTONE: the plan superellipse pulls qLen in as qLat grows, and the pull
            // changes character where |qLat| overtakes |qLen|, so across a row the z falls,
            // rises and falls again. A threshold against that crosses the same row three or
            // four times, and the boundary comes out as needle-thin spikes rather than as a
            // staircase — which is exactly what sat over every rear window. u is the same
            // quantity rescaled, so switching between the two changed nothing at all, twice.
            //
            // The cube's z is monotone by construction and its iso-lines ARE the grid rows,
            // so a cut on it is straight. On the centreline the superellipse is the identity,
            // so these still land where uFront and uRear say; off the centreline the header
            // sweeps very slightly forward, which is what a real one does anyway.
            float peak = st.RoofPeak;
            float qFront = uFront * 2f - 1f;
            float qRear = uRear * 2f - 1f;
            float qPeak = peak * 2f - 1f;
            float qPillar = PillarU * 2f, qBPill = BPillU * 2f;

            // Where the cabin sits. Declared here rather than next to its own Geo.Node
            // because the cutter needs it: the beltline is a world height and the cutter
            // works in the shell's local space, so one of them has to be converted, and
            // recomputing this expression in two places is precisely how the greenhouse
            // came adrift from the glass the last three times.
            var canopyPos = new Vector3(0, baseY + bodyH + cabHeight * 0.46f - 0.08f,
                                        cabOff - cabHeight * 0.1f);

            // The beltline, measured up from the body's deck, which is the surface a real
            // one is set above. Held as a height it does the right thing everywhere without
            // being told to: where the roofline falls below it — the last few per cent at
            // the nose and tail — the opening simply closes, so the screens end in bodywork
            // instead of running off the end of the car.
            float beltAboveDeck = Mathf.Clamp(cabHeight * 0.20f, 0.07f, 0.16f);
            float beltLocal = baseY + bodyH + beltAboveDeck - canopyPos.y;

            // ...but TESTED in cube space, by asking which cube height the beltline sits at
            // for this station and comparing cube to cube.
            //
            // Comparing the deformed y directly is correct and looks wrong, for the same
            // reason the header did: the deformed coordinate is not monotone along a grid
            // row, so the boundary crosses a row several times and frays. Held this way the
            // beltline is still a height — the roof profile is applied, so it still closes
            // the opening wherever the roofline falls below it — but both sides of the
            // comparison are now smooth functions of the grid, so the edge is a clean
            // staircase. Above the roof the fraction exceeds 1, which no cube height can
            // reach, and the opening shuts on its own.
            var roofFn = st.Roof();
            float BeltQy(float qz)
            {
                float t = Mathf.Max(1e-3f, roofFn(qz * 0.5f + 0.5f));
                return ((beltLocal / canH + 0.5f) / t) * 2f - 1f;
            }

            bool IsWindow(Vector3 p, Vector3 q)
            {
                // How far around the section this quad sits. See HullOpts.Keep for why this
                // and not the surface normal.
                float around = Mathf.Atan2(Mathf.Abs(q.x), q.y);
                // Below the beltline the cabin is door tops and rear quarter panel, and on
                // most archetypes it is sunk into the body shell anyway.
                if (q.y < BeltQy(q.z)) return false;
                // Beyond the headers, a screen — but only the part of the end that is still
                // near the crown. Given the whole end, the glass wrapped the cabin's rounded
                // tail cap and came out as a black blister bulging over the boot: the tail
                // does not taper to a point, it finishes low and rolls round, and the sides
                // of that roll are quarter panel.
                if (q.z > qFront + qPillar || q.z < qRear - qPillar) return around < QuartA;
                // Between the headers: pillars and roof are paint, and what is left down the
                // side is side glass. The cant rail is not a height — a height cut on a
                // domed cabin carries the window over the crown and out as a stripe across
                // the roof — it is a line around the section, which is where one is welded.
                if (Mathf.Abs(q.z - qPeak) < qBPill) return false;
                if (q.z > qFront - qPillar || q.z < qRear + qPillar) return false;
                return around > RailA;
            }

            var cnOpts = new HullOpts
            {
                Key = $"cn_{st.Key}_{cabLen}_{cabHeight}_{cabW}",
                PCross = st.CabPCross, PPlan = st.CabPPlan, Tumble = st.CabTumble,
                // Taper the glass in at both ends. At 0.95/0.97 the greenhouse stayed
                // almost full width to its tips while the body's plan superellipse pulls
                // in hard at nose and tail, so on the long-roof styles the rounded end of
                // the glass surfaced outside the rear quarter as a dark lump stuck to the
                // tailgate. The cabin must always be narrower than the body it sits on.
                WNose = 0.88f, WTail = 0.86f,
                Top = roofFn, Bot = _ => 0f,
                // Density is set by the cuts, not by the shape, and it has to be spent on
                // the faces the cuts actually cross. The header that bounds the top of the
                // backlight does not cross the roof — it crosses the cabin's END CAP, which
                // is gridded SLat by SY. At 16 by 20 against 96 down the flanks, each step
                // of that staircase was 84 mm, an eighth of the opening's width, and the
                // chase camera looks straight at it: every archetype had a torn-paper edge
                // over its rear window. Spending 96 segments on the one face with no cut
                // running across it was the whole mistake.
                SLat = 28, SY = 24, SLen = 80,
            };
            string cutKey = $"_{uFront:0.000}_{uRear:0.000}_{peak:0.000}_{beltLocal:0.000}";
            var cnPaint = cnOpts; cnPaint.Key += "_body" + cutKey;
            // The paint shell also drops everything well below the body's deck. That part of
            // the cabin is sunk inside the body and can never be seen, and at the density
            // the cuts need it was a third of the shell's triangles. The interior liner
            // keeps its floor, since that one is looked at through the windows.
            float deckLocal = baseY + bodyH - canopyPos.y;
            cnPaint.Keep = (_, p, q) => !IsWindow(p, q) && p.y > deckLocal - 0.03f;
            var cnGlass = cnOpts; cnGlass.Key += "_glass" + cutKey;
            cnGlass.Keep = (_, p, q) => IsWindow(p, q);
            // The cabin's own inside. Without it a window is a hole you see the far side of
            // the world through, which is worse than the bubble it replaced. An inverted
            // copy of the same shell is the whole interior: you look through the near glass
            // and see the far wall of the cabin, correctly shaded and at a real distance,
            // for one cached mesh and no new geometry.
            // Coarse on purpose: it is a dark shell seen through glass, it carries no cut
            // edges, and at the cabin's own density it would double the car's triangle count
            // to render detail nothing can resolve.
            var cnIn = cnOpts; cnIn.Key += "_in";
            cnIn.Invert = true;
            cnIn.SLat = 10; cnIn.SY = 8; cnIn.SLen = 24;
            // Glass, not Textured. The canopy kept the pillar/seal detail map but was being
            // built at metallic 0, which is why it read as painted plastic rather than a
            // windscreen — see MatLib.Glass for why near-mirror beats alpha here.
            //
            // NO DETAIL MAP, and this is what was actually making the glass black.
            //
            // CanopySide() paints pillars and window panes onto a texture: it is #0b0f14
            // over most of its area with three dim gradient panes on it, and it existed
            // because the canopy used to BE the whole cabin and the pillars had to be
            // faked somewhere. URP multiplies base colour by base map, so every tint set
            // here was being multiplied by near zero — which is why lifting the tint twice
            // changed nothing, and why the openings read as holes cut through the car
            // rather than as windows. The pillars are geometry now. The map is a leftover
            // of the approach that has just been deleted, and it goes with it.
            var glassMat = MatLib.Glass(new Color(0.185f, 0.205f, 0.240f));
            // Inset on both axes, purely so the cabin tucks inside the body's shoulders
            // rather than surfacing past them.
            //
            // Anything anchored to the cabin goes through GLASS() below, which runs the same
            // deformation the mesh did and then applies this same inset — so the two cannot
            // disagree. Anything that instead recomputes "where the glass is" by hand will
            // be wrong; see the note on Deform for the 15% that cost.
            // Both insets are gone. They existed to stop the cabin surfacing outside the
            // body, which was the right worry and the wrong cure: an inset shrinks the whole
            // cabin to fix an overhang at its widest point, and it was being applied on top
            // of a width that was already a guess. cabW is now measured off the body's
            // shoulder, so the cabin cannot overhang and does not need shrinking, and canLen
            // means what it says instead of five per cent less.
            var glassScale = Vector3.one;

            var dark = MatLib.Solid(new Color(0.063f, 0.071f, 0.086f), 0.35f);

            // Interior first, so it is behind everything. Slightly shrunk as well as
            // inverted: coincident with the paint shell it z-fights along every pillar.
            // Not black. A car interior in daylight is a mid-dark grey — headlining, seat
            // backs, a parcel shelf all catching sky through the glass — and at 0.085 the
            // cabin read as an unlit hole rather than as somewhere with room in it.
            Geo.Node("CabinIn", body, Hull(canLen, canH, cabW, cnIn),
                     MatLib.Solid(new Color(0.165f, 0.162f, 0.172f), 0.14f), canopyPos,
                     Quaternion.identity, Vector3.Scale(glassScale, new Vector3(0.965f, 0.97f, 0.975f)));

            // The cabin's bodywork: roof, pillars, cant rail, beltline, rear quarters. One
            // mesh, because they are one panel on a real car too.
            Geo.Node("Cabin", body, Hull(canLen, canH, cabW, cnPaint), paint, canopyPos,
                     Quaternion.identity, glassScale);

            // The glazing, at EXACTLY the paint shell's scale.
            //
            // It was 0.6% proud, to read as glass set into an aperture. That was the source
            // of the spiked edge over every rear window — the one defect left after the cant
            // rail and the header had both been fixed, and the reason neither fix appeared
            // to do anything. Paint and glass are complementary quads of one surface, so
            // they cannot z-fight and they do not need separating; but scale them apart and
            // near the roof, where the surface lies almost along the line of sight, a four
            // millimetre radial offset walks the glass a long way up the roof in screen
            // space, by an amount that varies quad to quad. Hence spikes rather than a
            // staircase. Coincident, the two tile the surface exactly and the seam is a
            // shared edge.
            Geo.Node("Glazing", body, Hull(canLen, canH, cabW, cnGlass), glassMat, canopyPos,
                     Quaternion.identity, glassScale);
            var chrome = MatLib.Chrome();
            // Blacked-out trim: window seals, the B-pillar strip, wiper arms. Kept as a
            // material rather than as geometry now that the pillars are bodywork.
            var pillarMat = MatLib.Solid(new Color(0.045f, 0.048f, 0.055f), 0.30f, 0.05f);

            // The greenhouse frame used to be built here: ten struts a side plus four cross
            // members, laid over a glass dome in the hope of hiding enough of it. All of it
            // is gone. Pillars, cant rail, header and beltline are now the paint shell's own
            // triangles — the ones IsWindow declined to give to the glass — so they are on
            // the surface by construction and cannot drift off it, which is the failure the
            // struts kept reintroducing every time an archetype changed shape.
            float canLenGlass = canLen;
            float roofZ = canopyPos.z;

            // Every anchor below comes from here. GLASS(u, qy, side) is the real surface:
            // qy +1 is the roof, -1 the sill; side +1/-1 picks a flank, 0 the centreline
            // crown. Nothing in this section is allowed to guess a coordinate any more.
            Vector3 GLASS(float u, float qy, float side)
                => canopyPos + Vector3.Scale(
                       SurfaceAtU(cnOpts, canLen, canH, cabW, u, qy, side), glassScale);

            // Z along the centreline is the one coordinate the superellipse leaves alone
            // (at qLat = qy = 0 both roundings are identity), so it stays closed-form —
            // used by the fin, mirrors, wipers and rails, which hang off the cabin rather
            // than sitting on its glass.
            float ZAt(float u) => roofZ + (u - 0.5f) * canLenGlass;

            const float UWind = 0.975f;

            // Crown and roof-edge half-width, both read off the real surface rather than
            // reconstructed. roofY drives the rails, light bars and taxi signs.
            float roofY = GLASS(st.RoofPeak, 1f, 0f).y;
            float latRoof = Mathf.Abs(GLASS(st.RoofPeak, 1f, 1f).x);
            // The canopy's floor is flat (Bot is identically zero), so the beltline is one
            // height for the whole cabin — mirrors and wipers hang off it.
            float beltY = canopyPos.y - canH * 0.5f;
            float pil = Mathf.Clamp(wid * 0.034f, 0.028f, 0.060f);

            // ---- interior fittings ----
            // The inverted shell gives the cabin depth; these give it something to be. Two
            // headrests and a dash are the whole of what is legible through a side window at
            // any distance the game is ever played at, and without them a window reads as a
            // dark panel rather than as somewhere a driver sits.
            var trim = MatLib.Solid(new Color(0.115f, 0.112f, 0.125f), 0.20f);
            float seatY = beltY + canH * 0.10f;
            float seatX = cabW * 0.21f;
            foreach (float s in new[] { 1f, -1f })
            {
                // Backrest and headrest, at the front seats. The rear bench sits low enough
                // that only its headrests would show, and on the short archetypes there is
                // no room for one at all.
                Geo.Box("SeatBack", body, new Vector3(cabW * 0.30f, canH * 0.42f, 0.07f),
                        new Vector3(s * seatX, seatY + canH * 0.16f, ZAt(peak + 0.06f)), trim);
                Geo.Box("Headrest", body, new Vector3(cabW * 0.20f, canH * 0.20f, 0.09f),
                        new Vector3(s * seatX, seatY + canH * 0.44f, ZAt(peak + 0.05f)), trim);
            }
            // Dash and parcel shelf: the two horizontals you see through the screens.
            Geo.Box("Dash", body, new Vector3(cabW * 0.80f, canH * 0.13f, canLen * 0.10f),
                    new Vector3(0, beltY + canH * 0.13f, ZAt(uFront + 0.02f)), trim);
            Geo.Box("Shelf", body, new Vector3(cabW * 0.74f, canH * 0.06f, canLen * 0.09f),
                    new Vector3(0, beltY + canH * 0.16f, ZAt(uRear - 0.02f)), trim);

            // The separate roof cap is gone too. It was the last survivor of the lay-panels-
            // over-a-dome approach, and it could only ever cover the plateau — everything
            // fore and aft of it stayed glass, which is most of what the chase camera sees.
            // The roof is now simply the part of the paint shell above the cant rail and
            // between the two headers.

            // Shark fin, sat on the rear of the roof. It used to be pinned to
            // "baseY + bodyH + 0.8 * cabHeight", a height with no relationship to the roof,
            // and at the Z it was given — where the roofline has already begun to fall — it
            // hung in the air beside the cabin as a small black block.
            float finU = Mathf.Lerp(uRear, st.RoofPeak, 0.35f);
            Geo.Box("Fin", body, new Vector3(0.05f, 0.07f, 0.16f),
                    new Vector3(0, GLASS(finU, 1f, 0f).y + 0.020f, ZAt(finU)), dark);
            if (st.TwinExhaust)
                foreach (float x in new[] { wid * 0.26f, wid * 0.15f })
                    Geo.Node("Exhaust", body, Geo.Cylinder(0.040f, 0.040f, 0.11f, 10), chrome,
                             new Vector3(x, wheelR - 0.06f, -half + 0.10f),
                             Quaternion.Euler(90, 0, 0));
            else
                Geo.Node("Exhaust", body, Geo.Cylinder(0.034f, 0.034f, 0.10f, 8), dark,
                         new Vector3(wid * 0.24f, wheelR - 0.06f, -half + 0.09f),
                         Quaternion.Euler(90, 0, 0));

            // ---- door mirrors ----
            // Flat body colour, not the panel texture: a 13 cm box lands on whatever part
            // of the flank atlas its UVs happen to hit, which was the wheel-arch AO, and
            // that made the mirrors read as black wings rather than painted caps.
            var paintFlat = MatLib.CarPaint("mat_paintflat" + ColorUtility.ToHtmlStringRGB(bodyC), bodyC);

            // A trim line laid along the flank, following the shell instead of running
            // straight past it.
            //
            // Every one of these used to be a single long box at a fixed lateral offset,
            // which is only correct if the car is a cuboid. Measured on the player hatch,
            // the rocker sat 122-133 mm outside the bodywork for its whole length — a dark
            // rail hanging in mid-air under the doors — and the shoulder crease drifted from
            // 24 mm proud at the waist to 101 mm by the front wheel, because the body tapers
            // in plan and the crease did not. Both read as rails bolted to a slab, which is
            // precisely the look being chased out here. Sampling the surface per segment
            // puts them on the car and lets them follow its taper.
            // The height is a world height, not a cube coordinate — see FlankAtHeight for
            // why that distinction stopped the crease turning into a beak over the arches.
            void FlankLine(Material mat, float u0, float u1, float worldY, float thick, int segs)
            {
                float localY = worldY - shellPos.y;
                foreach (float s in new[] { 1f, -1f })
                {
                    var prev = Vector3.zero;
                    for (int i = 0; i <= segs; i++)
                    {
                        var p = shellPos + FlankAtHeight(bdOpts, len, bodyH, wid,
                                    Mathf.Lerp(u0, u1, (float)i / segs), localY, s);
                        p.x += s * thick * 0.30f;      // stand proud, do not straddle
                        if (i > 0) Strut(body, mat, prev, p, thick);
                        prev = p;
                    }
                }
            }

            // Hung off the base of the A-pillar on a stalk, which is where a door mirror
            // actually lives. Floating them beside the cabin with no visible attachment was
            // a large part of why the cars felt like assemblies of parts.
            foreach (float s in new[] { 1f, -1f })
            {
                // Rooted at the A-pillar foot on the real glass edge, not at the cabin's
                // nominal half-width — that put the stalk in open air beside the car.
                float xFoot = Mathf.Abs(GLASS(UWind, -1f, s).x);
                var stalk = new Vector3(s * xFoot, beltY - 0.03f, ZAt(UWind) - 0.06f);
                var tip = new Vector3(s * (xFoot + 0.10f), beltY + 0.005f, ZAt(UWind) - 0.13f);
                Strut(body, dark, stalk, tip, 0.026f);
                Geo.Node("Mirror", body, Geo.UnitCube, paintFlat, tip,
                         Quaternion.Euler(0, s * 10f, 0),
                         new Vector3(0.05f, 0.07f, 0.135f), shadows: false);
            }

            // ---- wipers ----
            // Resting at the base of the windscreen, offset off-centre like a real pair
            // rather than mirrored — a windscreen with no wipers at all is one of the
            // fastest "toy car" tells there is, and it costs two thin bars to fix.
            float wiperZ = ZAt(UWind) - 0.015f;
            foreach (var (wpx, wLen) in new[] { (cabW * 0.16f, 0.20f), (-cabW * 0.05f, 0.16f) })
                Strut(body, dark, new Vector3(wpx, beltY + 0.012f, wiperZ),
                      new Vector3(wpx - wLen, beltY + 0.045f, wiperZ - 0.02f), 0.013f);

            // ---- grille ----
            // Sized off the body, not a constant: a 44 cm slot is right on a hatchback and
            // looks like a letterbox on a van. Deliberately generic shapes — an upright or
            // a wide slot — because a manufacturer's grille outline is the one part of a
            // car's face that is actually protected.
            switch (st.Grille)
            {
                case GrilleKind.Wide:
                    Geo.Box("Grille", body, new Vector3(wid * 0.46f, bodyH * 0.17f, 0.02f),
                            new Vector3(0, baseY + bodyH * 0.38f, half - 0.01f), dark);
                    break;
                case GrilleKind.Tall:
                    Geo.Box("Grille", body, new Vector3(wid * 0.40f, bodyH * 0.40f, 0.02f),
                            new Vector3(0, baseY + bodyH * 0.50f, half - 0.01f), dark);
                    Geo.Box("GrilleTrim", body, new Vector3(wid * 0.42f, bodyH * 0.05f, 0.03f),
                            new Vector3(0, baseY + bodyH * 0.70f, half - 0.012f), chrome);
                    break;
                case GrilleKind.Twin:
                    foreach (float gx in new[] { wid * 0.13f, -wid * 0.13f })
                        Geo.Box("Grille", body, new Vector3(wid * 0.20f, bodyH * 0.26f, 0.02f),
                                new Vector3(gx, baseY + bodyH * 0.44f, half - 0.01f), dark);
                    break;
            }
            Geo.Box("PlateF", body, new Vector3(0.34f, 0.12f, 0.02f),
                    new Vector3(0, baseY + bodyH * 0.2f, half - 0.045f),
                    MatLib.Solid(new Color(0.88f, 0.88f, 0.84f), 0.3f));
            Geo.Box("PlateR", body, new Vector3(0.34f, 0.12f, 0.02f),
                    new Vector3(0, baseY + bodyH * 0.26f, -half + 0.02f),
                    MatLib.Solid(new Color(0.88f, 0.88f, 0.84f), 0.3f));

            // ---- flank relief ----
            // A car's side is never a bare slab. Two features do nearly all the work of
            // making it not one: a shoulder crease running the length of the flank, which
            // catches a hard highlight and splits the side into an upper and lower surface,
            // and a rocker below the doors that puts the sill in shadow and stops the body
            // looking like it was extruded straight down to the road.
            //
            // The crease is body colour, not a dark stripe. What reads is the highlight on
            // its edge, not the colour — paint a line on instead and it looks like a decal.
            // It runs high on the flank, above the arches, so it can sweep the full length.
            FlankLine(paintFlat, 0.13f, 0.93f, baseY + bodyH * 0.66f, 0.024f, 18);

            // The rocker only runs between the arches now. Carried across them it would
            // climb the arch and back down, tracing a wave along the bottom of the car.
            if (!st.Cladding)
                FlankLine(dark, 0.29f, 0.71f, baseY + bodyH * 0.11f, 0.036f, 8);

            // Lower intake under the grille, and a matching rear valance. Without them the
            // nose is one flat painted face from the bonnet to the road, which no car has.
            Geo.Box("Intake", body, new Vector3(wid * 0.56f, bodyH * 0.15f, 0.03f),
                    new Vector3(0, baseY + bodyH * 0.10f, half - 0.035f), dark);
            Geo.Box("Valance", body, new Vector3(wid * 0.60f, bodyH * 0.13f, 0.03f),
                    new Vector3(0, baseY + bodyH * 0.09f, -half + 0.035f), dark);

            // ---- wheels ----
            // Width scales with radius, so a lifted pickup gets fat tyres and a coupe gets
            // wide low ones without either being hand-tuned.
            float wheelW = wheelR * st.WheelWFrac;
            var tyreMat  = MatLib.Rubber();
            // metallic 0.72 rather than near-1: with only a skybox reflection to draw on,
            // a near-mirror alloy just samples the ground and reads as a khaki blob
            var alloyMat = MatLib.Solid(new Color(0.70f, 0.72f, 0.75f), 0.65f, 0.72f);
            var lipMat   = MatLib.Chrome(0.88f);
            var wellMat  = MatLib.Solid(new Color(0.10f, 0.10f, 0.11f), 0.35f);
            var calMat   = MatLib.Solid(new Color(0.55f, 0.14f, 0.11f), 0.45f);

            // Track is deliberately unchanged. The wheels looked buried because the flank
            // ran straight down past them — four millimetres of tyre proud at mid-height —
            // and the instinct was to widen the track to push them out. Measured, that is
            // the wrong lever: it puts the tyre 30 mm OUTSIDE the arch lip, which is a
            // stanced show car, not a hatchback. The arch cut above exposes the wheel by
            // removing the bodywork in front of it, so the track can stay where the lip
            // still covers the tyre.
            float axF = len * 0.32f, axR = -len * 0.32f, wx = wid / 2f - 0.19f;

            var steer = new List<Transform>();
            var spin = new List<Transform>();
            // AN OPENING, NOT A HOOP. Painting this tube in body colour was the mistake: a
            // pale torus standing off a pale flank is lit as its own object, so it read as a
            // plastic band stuck to the side — worst on the white and silver cars, where it
            // was the first thing the eye found. What a wheel arch actually looks like from
            // ten feet away is a dark crescent: the shadowed gap between the tyre and the
            // sheet metal turning in around it. So it is always dark now, thinner, and sunk
            // far enough into the flank that only that crescent shows. Cladding archetypes
            // get the same shape a shade lighter, because their arch trim is a visible
            // plastic moulding rather than pure shadow.
            var archMesh = Geo.HalfTorus(wheelR + 0.026f, 0.016f);
            var archMat = st.Cladding
                ? MatLib.Solid(new Color(0.115f, 0.120f, 0.132f), 0.30f)
                : MatLib.Solid(new Color(0.048f, 0.050f, 0.056f), 0.22f);

            foreach (var (az, x, front) in new[]
                     { (axF, wx, true), (axF, -wx, true), (axR, wx, false), (axR, -wx, false) })
            {
                Transform mount = body;
                if (front)
                {
                    var sg = new GameObject("SteerGroup").transform;
                    sg.SetParent(body, false);
                    sg.localPosition = new Vector3(x, wheelR, az);
                    steer.Add(sg);
                    mount = sg;
                }

                var wheel = new GameObject("Wheel").transform;
                wheel.SetParent(mount, false);
                wheel.localPosition = front ? Vector3.zero : new Vector3(x, wheelR, az);
                spin.Add(wheel);

                BuildWheel(wheel, mount, wheel.localPosition, wheelR, wheelW, x > 0, st.Spokes,
                           tyreMat, alloyMat, lipMat, wellMat, calMat);

                // Mostly buried in the flank on purpose. Sat on the surface it is a hoop;
                // sunk deeper, only a crescent shows and it becomes the shadowed edge where
                // the wing turns down to the tyre.
                Geo.Node("Arch", body, archMesh, archMat,
                         new Vector3(x > 0 ? wid / 2f - 0.062f : -(wid / 2f - 0.062f), wheelR, az),
                         Quaternion.Euler(0, 90, 0), shadows: false);
            }

            // ---- bumper lips and full-width light bars ----
            // Inset from the reference's own 0.82 x width at +/-(half - 0.03): the hull's
            // plan superellipse rounds the nose and tail hard, so a bar sized to the full
            // beam pokes out of the corners as two floating black rods.
            if (st.Bumpers)
            {
                var bumper = MatLib.Solid(new Color(0.141f, 0.149f, 0.180f), 0.25f);
                Geo.Node("BumperF", body, Geo.Cylinder(0.06f, 0.06f, wid * 0.66f, 10), bumper,
                         new Vector3(0, wheelR + 0.05f, half - 0.16f), Quaternion.Euler(0, 0, 90));
                Geo.Node("BumperR", body, Geo.Cylinder(0.06f, 0.06f, wid * 0.66f, 10), bumper,
                         new Vector3(0, wheelR + 0.05f, -half + 0.16f), Quaternion.Euler(0, 0, 90));
            }

            float lightY = baseY + bodyH * 0.62f;
            // A lens, not a lamp. Mission 1 is broad daylight and headlights are off, so
            // this is glossy pale glass catching the sun. It was Emissive at 2.2, which
            // was invisible while _EMISSION was being stripped and then, the moment the
            // lamps moved to Unlit and actually worked, became a blazing white rod across
            // the nose at midday. Give it a lamp when there is a night district to need one.
            var headMat = MatLib.Solid(new Color(0.82f, 0.81f, 0.76f), 0.92f, 0.15f);

            // LIGHT SIGNATURE. This is doing more work than its size suggests — the shape
            // and placement of the lamps is the single strongest cue for "what kind of car
            // is that", more than the body, which at 30 px on a phone is mostly silhouette.
            //
            // Whatever the shape, lamps must sit PROUD of the surface. At these lateral
            // offsets the nose has already drawn back from `half` (the plan superellipse
            // rounds it hard), so anything tucked fully inside z = half is swallowed by the
            // hull and renders as nothing. Equally, a bar sized to the full beam escapes
            // the bodywork at both corners and floats — which is exactly how the original
            // full-width cylinder failed.
            switch (st.Head)
            {
                case HeadSig.Slim:
                    // 18% of the beam, not 30%. At 30% these were 55 cm boxes reading as
                    // white handlebars bolted across the nose.
                    foreach (float lx in new[] { wid * 0.25f, -wid * 0.25f })
                    {
                        Geo.Node("LampBezel", body, Geo.UnitCube, dark,
                                 new Vector3(lx, lightY, half - 0.055f), Quaternion.identity,
                                 new Vector3(wid * 0.21f, bodyH * 0.105f, 0.06f), shadows: false);
                        Geo.Node("Headlamp", body, Geo.UnitCube, headMat,
                                 new Vector3(lx, lightY, half - 0.025f), Quaternion.identity,
                                 new Vector3(wid * 0.18f, bodyH * 0.075f, 0.09f), shadows: false);
                    }
                    break;
                case HeadSig.Quad:
                    foreach (float lx in new[] { wid * 0.30f, wid * 0.18f, -wid * 0.18f, -wid * 0.30f })
                    {
                        Geo.Node("LampBezel", body, Geo.Cylinder(0.055f, 0.055f, 0.06f, 12), dark,
                                 new Vector3(lx, lightY, half - 0.055f), Quaternion.Euler(90, 0, 0),
                                 shadows: false);
                        Geo.Node("Headlamp", body, Geo.Cylinder(0.043f, 0.043f, 0.09f, 12), headMat,
                                 new Vector3(lx, lightY, half - 0.03f), Quaternion.Euler(90, 0, 0),
                                 shadows: false);
                    }
                    break;
                default:
                    // The bezel is what makes a lamp look set into the wing. Without it the
                    // lens is a pale nub glued to the paint, which is how these read before.
                    foreach (float lx in new[] { wid * 0.27f, -wid * 0.27f })
                    {
                        Geo.Node("LampBezel", body, Geo.Cylinder(0.071f, 0.071f, 0.06f, 12), dark,
                                 new Vector3(lx, lightY, half - 0.055f), Quaternion.Euler(90, 0, 0),
                                 shadows: false);
                        Geo.Node("Headlamp", body, Geo.Cylinder(0.058f, 0.058f, 0.10f, 12), headMat,
                                 new Vector3(lx, lightY, half - 0.03f), Quaternion.Euler(90, 0, 0),
                                 shadows: false);
                    }
                    break;
            }

            // brake light gets its own instance: the driver flares it, so it must not be
            // shared with any other car's tail lamps
            var brakeMat = new Material(MatLib.Emissive(new Color(0.33f, 0.07f, 0.07f),
                                                        new Color(1f, 0.23f, 0.19f), 0.35f));
            float tailY = lightY + 0.04f;
            switch (st.Tail)
            {
                case TailSig.Bar:
                    foreach (float lx in new[] { wid * 0.23f, -wid * 0.23f })
                        Geo.Node("Taillamp", body, Geo.UnitCube, brakeMat,
                                 new Vector3(lx, tailY, -half + 0.025f), Quaternion.identity,
                                 new Vector3(wid * 0.20f, bodyH * 0.07f, 0.08f), shadows: false);
                    break;
                case TailSig.LShape:
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        Geo.Node("Taillamp", body, Geo.UnitCube, brakeMat,
                                 new Vector3(sgn * wid * 0.26f, tailY, -half + 0.025f),
                                 Quaternion.identity,
                                 new Vector3(wid * 0.17f, bodyH * 0.07f, 0.08f), shadows: false);
                        // the short vertical return that makes it an L rather than a dash
                        Geo.Node("TaillampR", body, Geo.UnitCube, brakeMat,
                                 new Vector3(sgn * wid * 0.325f, tailY - bodyH * 0.07f, -half + 0.025f),
                                 Quaternion.identity,
                                 new Vector3(wid * 0.045f, bodyH * 0.15f, 0.08f), shadows: false);
                    }
                    break;
                default:
                    foreach (float lx in new[] { wid * 0.28f, -wid * 0.28f })
                        Geo.Node("Taillamp", body, Geo.Cylinder(0.052f, 0.052f, 0.10f, 12), brakeMat,
                                 new Vector3(lx, tailY, -half + 0.03f), Quaternion.Euler(90, 0, 0),
                                 shadows: false);
                    break;
            }

            // ---- archetype extras ----
            // Rails follow the roof panel, not the cabin: latRoof is the width the roof
            // actually is after tumblehome, and the plateau is where it is flat enough to
            // mount anything. Sized off cabW they hung out past the roof edge in mid-air.
            if (st.RoofRails)
                foreach (float rx in new[] { latRoof * 0.80f, -latRoof * 0.80f })
                    Geo.Box("RoofRail", body, new Vector3(0.045f, 0.035f, (uFront - uRear) * canLenGlass * 0.88f),
                            new Vector3(rx, roofY + 0.030f, ZAt((uFront + uRear) * 0.5f)), dark);

            // On the boot lid, and standing on two visible risers. Pinned to the bounding
            // box top it floated 99 mm clear of the deck — a black slab hanging behind the
            // car — and even seated, a lip spoiler with no attachment reads as a decal.
            if (st.Spoiler)
            {
                float spU = (-half + 0.16f) / len + 0.5f;
                float deckY = BODY(spU, 1f, 0f).y;
                float bladeY = deckY + 0.055f;
                Geo.Box("Spoiler", body, new Vector3(wid * 0.62f, 0.035f, 0.16f),
                        new Vector3(0, bladeY, -half + 0.16f), dark);
                foreach (float rx in new[] { wid * 0.24f, -wid * 0.24f })
                    Geo.Box("SpoilerRiser", body, new Vector3(0.035f, 0.055f, 0.10f),
                            new Vector3(rx, (deckY + bladeY) * 0.5f, -half + 0.16f), dark);
            }

            if (st.Cladding)
            {
                // On the surface, between the arches — same reason as the rocker above.
                FlankLine(dark, 0.28f, 0.72f, baseY + bodyH * 0.15f, 0.042f, 8);
                Geo.Box("SkidF", body, new Vector3(wid * 0.42f, bodyH * 0.10f, 0.10f),
                        new Vector3(0, baseY + bodyH * 0.06f, half - 0.10f),
                        MatLib.Solid(new Color(0.55f, 0.56f, 0.58f), 0.45f));
            }

            if (st.LightBar)
            {
                Geo.Box("BarBase", body, new Vector3(wid * 0.52f, 0.04f, 0.13f),
                        new Vector3(0, roofY + 0.03f, roofZ), dark);
                Geo.Box("BarRed", body, new Vector3(wid * 0.22f, 0.07f, 0.11f),
                        new Vector3(wid * 0.13f, roofY + 0.08f, roofZ),
                        MatLib.Emissive(new Color(0.30f, 0.02f, 0.02f), new Color(1f, 0.12f, 0.10f), 1.6f));
                Geo.Box("BarBlue", body, new Vector3(wid * 0.22f, 0.07f, 0.11f),
                        new Vector3(-wid * 0.13f, roofY + 0.08f, roofZ),
                        MatLib.Emissive(new Color(0.02f, 0.04f, 0.30f), new Color(0.16f, 0.35f, 1f), 1.6f));
            }

            if (st.TaxiSign)
            {
                Geo.Box("TaxiSign", body, new Vector3(wid * 0.30f, 0.11f, 0.13f),
                        new Vector3(0, roofY + 0.06f, roofZ),
                        MatLib.Emissive(new Color(0.20f, 0.16f, 0.03f), new Color(1f, 0.82f, 0.25f), 0.9f));
                // the chequer stripe down the flank, which is what actually says "taxi"
                FlankLine(MatLib.Solid(new Color(0.08f, 0.08f, 0.09f), 0.4f),
                          0.20f, 0.80f, baseY + bodyH * 0.45f, 0.026f, 12);
            }

            if (st.Rust)
                Geo.Box("Rust", body, new Vector3(0.04f, bodyH * 0.24f, len * 0.13f),
                        new Vector3(wid / 2f - 0.08f, baseY + bodyH * 0.40f, -len * 0.08f),
                        MatLib.Solid(new Color(0.478f, 0.290f, 0.180f), 0.05f));

            return new Rig
            {
                Root = root,
                Body = body,
                Steer = steer.ToArray(),
                WheelSpin = spin.ToArray(),
                WheelRadius = wheelR,
                BrakeLight = brakeMat,
            };
        }

        /// <summary>
        /// A square-section bar between two points — pillars, cant rails, beltlines.
        ///
        /// The up-hint matters. A B-pillar is very nearly vertical, and
        /// Quaternion.LookRotation(dir, Vector3.up) with dir parallel to up is degenerate:
        /// Unity returns identity and the bar silently lies down flat along +Z. Swap the
        /// hint to forward when the bar is steep.
        /// </summary>
        static void Strut(Transform body, Material mat, Vector3 a, Vector3 b, float thick)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            var dir = d / len;
            var up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.985f ? Vector3.forward : Vector3.up;
            Geo.Node("Pillar", body, Geo.UnitCube, mat, (a + b) * 0.5f,
                     Quaternion.LookRotation(dir, up),
                     new Vector3(thick, thick, len), shadows: false);
        }

        /// <summary>
        /// One wheel: tyre, alloy rim with spokes and a polished flange, and the brake
        /// hardware behind it.
        ///
        /// Everything is modelled about +Y and the whole assembly is then rolled a quarter
        /// turn onto the lateral axis, so the parts can be placed in plain polar terms
        /// instead of every spoke carrying a compound rotation. The roll flips per side so
        /// the dished face and the flange point outboard on both wheels rather than one
        /// wheel showing the world its rim barrel.
        ///
        /// The caliper hangs off <paramref name="mount"/> rather than the spinning wheel,
        /// because a caliper that rotates with the disc is the sort of detail nobody
        /// consciously notices and everybody feels. The disc itself does spin, correctly.
        /// </summary>
        static void BuildWheel(Transform wheel, Transform mount, Vector3 wheelPos,
                               float r, float w, bool rightSide, int spokeCount,
                               Material tyre, Material alloy, Material lip,
                               Material well, Material caliper)
        {
            var roll = Quaternion.Euler(0, 0, rightSide ? -90f : 90f);

            var gfx = new GameObject("WheelGfx").transform;
            gfx.SetParent(wheel, false);
            gfx.localRotation = roll;

            float out_ = w * 0.5f;   // +Y is outboard inside gfx

            Geo.Node("Tyre", gfx, Geo.Tyre(r, w), tyre);

            // THE WELL FLOOR, AND WHY IT IS WHERE IT IS. Geo.Cylinder is capped, so this
            // disc hides everything inboard of it. The first version centred the barrel on
            // the axle, which put its outboard cap at 0.46w — beyond the spokes, the hub
            // cap and the flange — and the entire rim rendered as one blank grey disc.
            // Sinking it to 0.11w turns that same cap into the floor of the wheel well,
            // with the spokes standing proud in front of it and daylight between them.
            Geo.Node("Well", gfx, Geo.Cylinder(r * 0.60f, r * 0.60f, w * 0.60f, 20), well,
                     new Vector3(0, -w * 0.19f, 0));

            // Open ring, not a cylinder, for exactly the reason above: a capped cylinder
            // here would be a solid disc across the whole face again.
            Geo.Node("Flange", gfx, Geo.Lathe($"rimlip{r}_{w}", new[]
            {
                new Vector2(r * 0.600f, w * 0.34f),
                new Vector2(r * 0.638f, w * 0.40f),
                new Vector2(r * 0.638f, w * 0.44f),
                new Vector2(r * 0.600f, w * 0.48f),
            }, 24), lip, shadows: false);

            int spokes = Mathf.Max(3, spokeCount);
            // thinner spokes as the count goes up, so a ten-spoke rim reads as fine and
            // expensive rather than as a solid disc
            float spokeW = Mathf.Lerp(0.155f, 0.075f, Mathf.InverseLerp(5f, 10f, spokes));
            for (int i = 0; i < spokes; i++)
            {
                float a = Mathf.PI * 2f * i / spokes;
                // box local +Z points radially outward: Euler(0, 90 - a, 0) maps +Z to
                // (cos a, 0, sin a)
                Geo.Node("Spoke", gfx, Geo.UnitCube, alloy,
                         new Vector3(Mathf.Cos(a) * r * 0.37f, out_ * 0.46f, Mathf.Sin(a) * r * 0.37f),
                         Quaternion.Euler(0, 90f - a * Mathf.Rad2Deg, 0),
                         new Vector3(r * spokeW, w * 0.10f, r * 0.48f),
                         shadows: false);
            }

            Geo.Node("HubCap", gfx, Geo.Cylinder(r * 0.185f, r * 0.165f, w * 0.13f, 14), lip,
                     new Vector3(0, out_ * 0.56f, 0), shadows: false);

            // Fixed to the upright, not to the spinning wheel — a caliper that rotated with
            // the disc is the sort of thing nobody consciously notices and everybody feels.
            var stat = new GameObject("WheelStatic").transform;
            stat.SetParent(mount, false);
            stat.localPosition = wheelPos;
            stat.localRotation = roll;
            Geo.Node("Caliper", stat, Geo.UnitCube, caliper,
                     new Vector3(-r * 0.40f, -w * 0.02f, r * 0.26f),
                     Quaternion.Euler(0, -35f, 0),
                     new Vector3(r * 0.11f, w * 0.26f, r * 0.30f), shadows: false);
        }

        /// <summary>The player's hatch, with the rust patch the flavour text promises.</summary>
        public static Rig BuildHatch(Transform parent, VehicleDef veh)
        {
            // Livery comes from vehicles.json, shape from the archetype table. The rust
            // patch is now part of the RustyHatch style rather than bolted on afterwards,
            // so it scales with the body instead of being three fixed numbers that only
            // happened to land correctly on a 3.9 m car.
            var bodyC = ParseHex(veh.BodyHex, new Color(0.753f, 0.337f, 0.231f));
            var st = CarStyles.ForVehicle(veh.Key);
            return Build(parent, veh.Key ?? "player", veh, st, bodyC);
        }

        public static Color ParseHex(string hex, Color fallback)
            => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

        /// <summary>
        /// Roll the wheels and steer the front pair. Called from the render loop, never
        /// from FixedUpdate — this is presentation, and the simulation has no wheels.
        /// </summary>
        public static void Animate(Rig rig, double speed, double steerAngle, float dt, ref float rollAngle)
        {
            if (rig == null) return;
            rollAngle += (float)(speed / Mathf.Max(0.05f, rig.WheelRadius)) * dt * Mathf.Rad2Deg;
            foreach (var w in rig.WheelSpin)
                w.localRotation = Quaternion.Euler(rollAngle, 0, 0);
            foreach (var s in rig.Steer)
                s.localRotation = Quaternion.Euler(0, (float)(steerAngle * Mathf.Rad2Deg), 0);
        }
    }
}
