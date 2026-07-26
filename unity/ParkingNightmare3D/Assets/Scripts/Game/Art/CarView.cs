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
        }

        static float S3(float a, float b, float t)
        {
            t = Mathf.Clamp01((t - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Segmented box deformed into a car shell. Vertices are welded across the cube's
        /// face boundaries before deforming, so the smooth normals really are seamless —
        /// the reference relies on Three.js's own box being indexed for the same effect.
        /// </summary>
        public static Mesh Hull(float len, float hgt, float wid, HullOpts o) =>
            Geo.Get($"hull_{o.Key}", () =>
            {
                const int SegLen = 28, SegY = 8, SegLat = 12;
                var (cube, tris) = WeldedBox(SegLat, SegY, SegLen);

                float pC = o.PCross, pP = o.PPlan;
                var top = o.Top ?? (_ => 1f);
                var bot = o.Bot ?? (_ => 0f);

                var verts = new Vector3[cube.Count];
                var uvs = new Vector2[cube.Count];

                for (int i = 0; i < cube.Count; i++)
                {
                    // cube space in [-1, 1]: x lateral, y up, z along the length
                    float qLat = cube[i].x * 2f, qy = cube[i].y * 2f, qLen = cube[i].z * 2f;

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

                    float u = qLen * 0.5f + 0.5f;   // 0 = tail, 1 = nose
                    qLat *= Mathf.Lerp(o.WTail, 1f, Mathf.Clamp01(u / 0.35f))
                          * Mathf.Lerp(o.WNose, 1f, Mathf.Clamp01((1f - u) / 0.35f));

                    // tumblehome: the upper body leans inward like real sheet metal
                    float y01 = qy * 0.5f + 0.5f;
                    qLat *= 1f - o.Tumble * Mathf.Pow(Mathf.Max(0f, y01 - 0.35f) / 0.65f, 1.6f);

                    float yy = bot(u) + y01 * (top(u) - bot(u));
                    verts[i] = new Vector3(qLat * 0.5f * wid, (yy - 0.5f) * hgt, qLen * 0.5f * len);
                    // planar UVs down the flank, which is the only face the panel-detail
                    // texture needs to land on correctly
                    uvs[i] = new Vector2(u, y01);
                }

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

            Face(new Vector3(-h, -h, h), X, Y, sx, sy, false);    // +Z
            Face(new Vector3(-h, -h, -h), X, Y, sx, sy, true);    // -Z
            Face(new Vector3(h, -h, -h), Z, Y, sz, sy, false);    // +X
            Face(new Vector3(-h, -h, -h), Z, Y, sz, sy, true);    // -X
            Face(new Vector3(-h, h, -h), X, Z, sx, sz, false);    // +Y
            Face(new Vector3(-h, -h, -h), X, Z, sx, sz, true);    // -Y

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
            float cabW = wid * st.CabWidFrac;
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
            var hull = Hull(len, bodyH, wid, new HullOpts
            {
                Key = $"bd_{st.Key}_{len}_{bodyH}_{wid}",
                PCross = st.PCross, PPlan = st.PPlan, Tumble = st.Tumble,
                WNose = st.WNose, WTail = st.WTail,
                Top = st.Deck(), Bot = st.Sill(),
            });
            var paint = MatLib.CarPaint(
                $"mat_paint{ColorUtility.ToHtmlStringRGB(bodyC)}_{st.PaintMetallic:0.00}_{st.PaintSmooth:0.00}",
                Color.white, ProcTex.CarSide(bodyC), st.PaintMetallic, st.PaintSmooth);
            Geo.Node("Shell", body, hull, paint, new Vector3(0, baseY + bodyH / 2f, 0));

            // ---- glass canopy: the roofline that carries most of the silhouette ----
            var canopy = Hull(cabLen + cabHeight * 0.9f, cabHeight * 0.92f, cabW, new HullOpts
            {
                Key = $"cn_{st.Key}_{cabLen}_{cabHeight}_{cabW}",
                PCross = st.CabPCross, PPlan = st.CabPPlan, Tumble = st.CabTumble,
                WNose = 0.95f, WTail = 0.97f,
                Top = st.Roof(), Bot = _ => 0f,
            });
            // Glass, not Textured. The canopy kept the pillar/seal detail map but was being
            // built at metallic 0, which is why it read as painted plastic rather than a
            // windscreen — see MatLib.Glass for why near-mirror beats alpha here.
            var glassMat = MatLib.Glass(new Color(0.075f, 0.085f, 0.105f), ProcTex.CanopySide());
            Geo.Node("Canopy", body, canopy, glassMat,
                     new Vector3(0, baseY + bodyH + cabHeight * 0.46f - 0.08f, cabOff - cabHeight * 0.1f));

            var dark = MatLib.Solid(new Color(0.063f, 0.071f, 0.086f), 0.35f);
            var chrome = MatLib.Chrome();

            // Top of the greenhouse. The canopy hull is 0.92 * cabHeight tall and is centred
            // at 0.46 of it, so its crown sits at exactly this height — get the 0.92 wrong
            // and anything "mounted on the roof" is in fact buried inside the glass.
            float roofZ = cabOff - cabHeight * 0.1f;
            float roofY = baseY + bodyH + cabHeight * 0.92f - 0.08f;

            // ---- painted roof panel ----
            // Only the windows are glass on a real car; the roof and the pillars are body
            // colour. Without this the greenhouse is a single dark shell and every car
            // reads as a bubble-top — ruinous on the tall archetypes, where an all-glass
            // SUV cabin looks like a conservatory on wheels.
            //
            // Width is derived from the canopy's tumblehome, not from cabW: the glass leans
            // inward as it rises, so a cap cut to the full cabin width would overhang the
            // side windows like a mushroom.
            float canLen = cabLen + cabHeight * 0.9f;
            float capW = cabW * (1f - st.CabTumble) * 1.03f;
            // Generous: the roof should be most of the greenhouse, with glass only where
            // the windows are. Cut too short, the car keeps its dark bubble and the panel
            // reads as a sunroof. The RoofFlat term still lets a fastback keep more glass
            // than an estate, which is the difference between the two silhouettes.
            float capLen = Mathf.Clamp(canLen * (st.RoofFlat * 1.2f + 0.42f), 0.22f, canLen * 0.93f);
            var roofCap = Hull(capLen, 0.09f, capW, new HullOpts
            {
                Key = $"rf_{st.Key}_{capLen}_{capW}",
                PCross = 2.8f, PPlan = 3.0f, Tumble = 0.05f, WNose = 0.88f, WTail = 0.90f,
                Top = _ => 1f, Bot = _ => 0f,
            });
            // Sunk just enough that the glass meets it, and proud enough that it is the
            // roof rather than a plate suspended inside the cabin.
            Geo.Node("Roof", body, roofCap, paint,
                     new Vector3(0, roofY - 0.025f, roofZ + (st.RoofPeak - 0.5f) * canLen));

            // shark fin + exhaust tips
            Geo.Box("Fin", body, new Vector3(0.05f, 0.07f, 0.16f),
                    new Vector3(0, baseY + bodyH + cabHeight * 0.8f, cabOff - cabLen * 0.3f), dark);
            if (st.TwinExhaust)
                foreach (float x in new[] { wid * 0.26f, wid * 0.15f })
                    Geo.Node("Exhaust", body, Geo.Cylinder(0.040f, 0.040f, 0.11f, 10), chrome,
                             new Vector3(x, wheelR - 0.06f, -half + 0.10f),
                             Quaternion.Euler(90, 0, 0));
            else
                Geo.Node("Exhaust", body, Geo.Cylinder(0.034f, 0.034f, 0.10f, 8), dark,
                         new Vector3(wid * 0.24f, wheelR - 0.06f, -half + 0.09f),
                         Quaternion.Euler(90, 0, 0));

            // mirrors
            float cabF = cabOff + cabLen / 2f;
            // flat body colour, not the panel texture: a 15 cm box lands on whatever part
            // of the flank atlas its UVs happen to hit, which was the wheel-arch AO —
            // making the mirrors read as black wings rather than painted caps
            var paintFlat = MatLib.CarPaint("mat_paintflat" + ColorUtility.ToHtmlStringRGB(bodyC), bodyC);
            foreach (float x in new[] { cabW / 2f + 0.07f, -(cabW / 2f + 0.07f) })
                Geo.Box("Mirror", body, new Vector3(0.13f, 0.08f, 0.07f),
                        new Vector3(x, baseY + bodyH + 0.05f, cabF - 0.04f), paintFlat);

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

            float axF = len * 0.32f, axR = -len * 0.32f, wx = wid / 2f - 0.19f;

            var steer = new List<Transform>();
            var spin = new List<Transform>();
            var archMesh = Geo.HalfTorus(wheelR + 0.06f, 0.06f);

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

                Geo.Node("Arch", body, archMesh, dark,
                         new Vector3(x > 0 ? wid / 2f - 0.02f : -(wid / 2f - 0.02f), wheelR, az),
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
                        Geo.Node("Headlamp", body, Geo.UnitCube, headMat,
                                 new Vector3(lx, lightY, half - 0.025f), Quaternion.identity,
                                 new Vector3(wid * 0.18f, bodyH * 0.075f, 0.09f), shadows: false);
                    break;
                case HeadSig.Quad:
                    foreach (float lx in new[] { wid * 0.30f, wid * 0.18f, -wid * 0.18f, -wid * 0.30f })
                        Geo.Node("Headlamp", body, Geo.Cylinder(0.043f, 0.043f, 0.09f, 12), headMat,
                                 new Vector3(lx, lightY, half - 0.03f), Quaternion.Euler(90, 0, 0),
                                 shadows: false);
                    break;
                default:
                    foreach (float lx in new[] { wid * 0.27f, -wid * 0.27f })
                        Geo.Node("Headlamp", body, Geo.Cylinder(0.058f, 0.058f, 0.10f, 12), headMat,
                                 new Vector3(lx, lightY, half - 0.03f), Quaternion.Euler(90, 0, 0),
                                 shadows: false);
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
            if (st.RoofRails)
                foreach (float rx in new[] { cabW * 0.36f, -cabW * 0.36f })
                    Geo.Box("RoofRail", body, new Vector3(0.05f, 0.04f, cabLen * 0.72f),
                            new Vector3(rx, roofY + 0.02f, roofZ), dark);

            if (st.Spoiler)
                Geo.Box("Spoiler", body, new Vector3(wid * 0.62f, 0.04f, 0.16f),
                        new Vector3(0, baseY + bodyH + 0.05f, -half + 0.16f), dark);

            if (st.Cladding)
            {
                foreach (float sx in new[] { wid * 0.49f, -wid * 0.49f })
                    Geo.Box("Cladding", body, new Vector3(0.04f, bodyH * 0.18f, len * 0.52f),
                            new Vector3(sx, baseY + bodyH * 0.14f, 0), dark);
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
                foreach (float sx in new[] { wid * 0.505f, -wid * 0.505f })
                    Geo.Box("TaxiStripe", body, new Vector3(0.02f, bodyH * 0.16f, len * 0.46f),
                            new Vector3(sx, baseY + bodyH * 0.44f, 0),
                            MatLib.Solid(new Color(0.08f, 0.08f, 0.09f), 0.4f));
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
