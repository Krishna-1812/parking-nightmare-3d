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
            var canopy = Hull(cabLen + cabHeight * 0.55f, cabHeight * 0.92f, cabW, new HullOpts
            {
                Key = $"cn_{st.Key}_{cabLen}_{cabHeight}_{cabW}",
                PCross = st.CabPCross, PPlan = st.CabPPlan, Tumble = st.CabTumble,
                // Taper the glass in at both ends. At 0.95/0.97 the greenhouse stayed
                // almost full width to its tips while the body's plan superellipse pulls
                // in hard at nose and tail, so on the long-roof styles the rounded end of
                // the glass surfaced outside the rear quarter as a dark lump stuck to the
                // tailgate. The cabin must always be narrower than the body it sits on.
                WNose = 0.88f, WTail = 0.86f,
                Top = st.Roof(), Bot = _ => 0f,
            });
            // Glass, not Textured. The canopy kept the pillar/seal detail map but was being
            // built at metallic 0, which is why it read as painted plastic rather than a
            // windscreen — see MatLib.Glass for why near-mirror beats alpha here.
            // Lifted off near-black. Once the frame went in, the glass stopped being the
            // whole cabin and became panels between pillars — and at that tint the panels
            // read as holes punched through the car rather than windows. Real glazing seen
            // from outside is dark but never void: it carries a sky reflection.
            var glassMat = MatLib.Glass(new Color(0.135f, 0.150f, 0.175f), ProcTex.CanopySide());
            // Inset on both axes. Laterally so the frame built below stands proud of it,
            // and 0.95 in Z so the glass tips tuck inside the frame — that spans u
            // 0.025..0.975, so the last slice at each end would otherwise have no member
            // around it and surface as a sliver past the pillar.
            //
            // Without the lateral inset the
            // pillars sit exactly on the glass surface, half in and half out, and the cabin
            // reads as a roll cage strapped over a bubble instead of glazing dropped into
            // bodywork. Windows are recessed on a real car and that shadow line is most of
            // what tells you so.
            Geo.Node("Canopy", body, canopy, glassMat,
                     new Vector3(0, baseY + bodyH + cabHeight * 0.46f - 0.08f, cabOff - cabHeight * 0.1f),
                     Quaternion.identity, new Vector3(0.93f, 1f, 0.95f));

            var dark = MatLib.Solid(new Color(0.063f, 0.071f, 0.086f), 0.35f);
            var chrome = MatLib.Chrome();

            // ---- greenhouse frame ----
            // A cabin is not a glass dome. It is painted structure with windows cut into
            // it, and modelling it as one shell is why every archetype wore a black bubble
            // however the tint was tuned. Laying a cap on top could never fix that: the
            // dome flares out below the cap's edge, so glass kept showing where bodywork
            // belonged. There is no boolean cutter here, so instead of cutting holes in
            // paint we build the frame around the glass — A, B and C pillars, a cant rail
            // over the side glass, a beltline under it, and a roof across the plateau.
            // What is left showing between them is a window, which is what a window is.
            //
            // Pillars lean inward as they rise because the glass does. Cabin tumblehome
            // makes the roof narrower than the waist, so a pillar held at one width would
            // peel off the glass by the time it reached the top.
            var roofFn = st.Roof();
            // Padding for the raked ends, but 0.9 * cabHeight was too much: on the
            // long-roof archetypes it pushed the greenhouse out past the tailgate, where
            // the rounded end of the glass shell surfaced above the rear deck as a dark
            // lump sitting outside the bodywork.
            float canLen = cabLen + cabHeight * 0.55f;
            float canH = cabHeight * 0.92f;
            float canopyY = baseY + bodyH + cabHeight * 0.46f - 0.08f;
            float roofZ = cabOff - cabHeight * 0.1f;

            float TopY(float u) => canopyY + (roofFn(u) - 0.5f) * canH;
            float ZAt(float u) => roofZ + (u - 0.5f) * canLen;

            float beltY = canopyY - canH * 0.5f;
            float latBelt = cabW * 0.5f * 0.99f;
            float latRoof = cabW * 0.5f * (1f - st.CabTumble);

            // Plateau edges are where the roof stops being flat, which is exactly where the
            // A and C pillars should meet it.
            float uFront = Mathf.Clamp01(st.RoofPeak + st.RoofFlat * (1f - st.RoofPeak));
            float uRear = Mathf.Clamp01(st.RoofPeak - st.RoofFlat * st.RoofPeak);
            // The frame has to reach the ends of the glass. At 0.93/0.07 it stopped seven
            // per cent short at each tip, and the unframed remainder surfaced as a dark
            // stub past the C-pillar — very obvious on the estate, where RoofTail keeps the
            // glass tall right to the tailgate.
            const float UWind = 0.975f, UBack = 0.025f;

            // Lateral taper of the canopy, matching the WNose/WTail above. The pillar feet
            // land near the tips where the glass has already drawn in, so holding them at
            // full cabin width would leave them hanging beside it in open air.
            float Taper(float u) => Mathf.Lerp(0.86f, 1f, Mathf.Clamp01(u / 0.35f))
                                  * Mathf.Lerp(0.88f, 1f, Mathf.Clamp01((1f - u) / 0.35f));

            float roofY = TopY(st.RoofPeak);   // for roof rails, light bars, taxi signs
            float pil = Mathf.Clamp(wid * 0.042f, 0.032f, 0.072f);

            foreach (float s in new[] { 1f, -1f })
            {
                var aBot = new Vector3(s * latBelt * Taper(UWind), beltY, ZAt(UWind));
                var aTop = new Vector3(s * latRoof * Taper(uFront), TopY(uFront), ZAt(uFront));
                var cBot = new Vector3(s * latBelt * Taper(UBack), beltY, ZAt(UBack));
                var cTop = new Vector3(s * latRoof * Taper(uRear), TopY(uRear), ZAt(uRear));
                var bBot = new Vector3(s * latBelt * Taper(st.RoofPeak), beltY, ZAt(st.RoofPeak));
                var bTop = new Vector3(s * latRoof * Taper(st.RoofPeak), TopY(st.RoofPeak), ZAt(st.RoofPeak));

                Strut(body, paint, aBot, aTop, pil);           // A-pillar
                Strut(body, paint, cBot, cTop, pil * 1.30f);   // C-pillar, always the thick one
                Strut(body, paint, bBot, bTop, pil * 0.85f);   // B-pillar
                Strut(body, paint, aTop, cTop, pil * 0.85f);   // cant rail over the side glass
                Strut(body, paint, aBot, cBot, pil * 0.75f);   // beltline under it
            }

            // CROSS MEMBERS, and this is what the rear-quarter bulge actually was.
            //
            // The side frame gave every window a pillar fore and aft, but the windscreen
            // and the backlight are not side windows: they span the full width, so their
            // frame runs across the car, not along it. Without a header above and a rail
            // below, the backlight was an unbounded sheet of near-black glass bounded only
            // by the two C-pillars — which on the long-roof styles is a large area, and it
            // read as a dark mass stuck to the rear quarter rather than as a tailgate
            // window. Three earlier attempts at this treated it as the glass being too
            // wide, too long, or outside the body. It was none of those: the geometry was
            // in the right place and simply had no frame around it.
            float xTopF = latRoof * Taper(uFront), xTopR = latRoof * Taper(uRear);
            float xBotF = latBelt * Taper(UWind), xBotR = latBelt * Taper(UBack);

            Strut(body, paint, new Vector3(xTopF, TopY(uFront), ZAt(uFront)),
                               new Vector3(-xTopF, TopY(uFront), ZAt(uFront)), pil * 0.90f);
            Strut(body, paint, new Vector3(xTopR, TopY(uRear), ZAt(uRear)),
                               new Vector3(-xTopR, TopY(uRear), ZAt(uRear)), pil * 0.90f);
            Strut(body, paint, new Vector3(xBotF, beltY, ZAt(UWind)),
                               new Vector3(-xBotF, beltY, ZAt(UWind)), pil * 0.80f);
            Strut(body, paint, new Vector3(xBotR, beltY, ZAt(UBack)),
                               new Vector3(-xBotR, beltY, ZAt(UBack)), pil * 0.80f);

            // Slightly longer than the plateau it caps. Cut exactly to uRear..uFront it
            // ended flush with the headers, leaving a strip of bare glass crown just behind
            // each one — the roof only starts falling gently there, so a little overhang
            // still lands on the shell and closes the gap.
            var roofCap = Hull(Mathf.Max(0.18f, (uFront - uRear + 0.10f) * canLen), 0.09f, latRoof * 2f,
                new HullOpts
                {
                    Key = $"rf_{st.Key}_{uFront - uRear}_{canLen}_{latRoof}",
                    PCross = 2.8f, PPlan = 3.0f, Tumble = 0.05f, WNose = 0.90f, WTail = 0.92f,
                    Top = _ => 1f, Bot = _ => 0f,
                });
            Geo.Node("Roof", body, roofCap, paint,
                     new Vector3(0, roofY - 0.030f, ZAt((uFront + uRear) * 0.5f)));

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

            // ---- door mirrors ----
            // Flat body colour, not the panel texture: a 13 cm box lands on whatever part
            // of the flank atlas its UVs happen to hit, which was the wheel-arch AO, and
            // that made the mirrors read as black wings rather than painted caps.
            var paintFlat = MatLib.CarPaint("mat_paintflat" + ColorUtility.ToHtmlStringRGB(bodyC), bodyC);

            // Hung off the base of the A-pillar on a stalk, which is where a door mirror
            // actually lives. Floating them beside the cabin with no visible attachment was
            // a large part of why the cars felt like assemblies of parts.
            foreach (float s in new[] { 1f, -1f })
            {
                var stalk = new Vector3(s * cabW * 0.50f, beltY - 0.03f, ZAt(UWind) - 0.06f);
                var tip = new Vector3(s * (cabW * 0.50f + 0.10f), beltY + 0.005f, ZAt(UWind) - 0.13f);
                Strut(body, dark, stalk, tip, 0.026f);
                Geo.Node("Mirror", body, Geo.UnitCube, paintFlat, tip,
                         Quaternion.Euler(0, s * 10f, 0),
                         new Vector3(0.05f, 0.07f, 0.135f), shadows: false);
            }

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
            foreach (float s in new[] { 1f, -1f })
            {
                Geo.Node("Shoulder", body, Geo.UnitCube, paintFlat,
                         new Vector3(s * (wid * 0.5f - 0.008f), baseY + bodyH * 0.66f, len * 0.02f),
                         Quaternion.identity,
                         new Vector3(0.022f, bodyH * 0.075f, len * 0.70f), shadows: false);

                if (!st.Cladding)
                    Geo.Node("Rocker", body, Geo.UnitCube, dark,
                             new Vector3(s * (wid * 0.5f - 0.030f), baseY + bodyH * 0.10f, 0f),
                             Quaternion.identity,
                             new Vector3(0.035f, bodyH * 0.17f, len * 0.52f), shadows: false);
            }

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

            float axF = len * 0.32f, axR = -len * 0.32f, wx = wid / 2f - 0.19f;

            var steer = new List<Transform>();
            var spin = new List<Transform>();
            // A FENDER LIP, NOT A HOOP. This was a 6 cm dark tube arching over each wheel,
            // and at 6 cm in near-black it read as a roll bar bolted to the side rather
            // than as bodywork. Thinner, tucked closer to the tyre, and painted — except on
            // the archetypes that carry plastic arch trim in real life, where dark is
            // correct and is part of what says "SUV".
            var archMesh = Geo.HalfTorus(wheelR + 0.045f, 0.030f);
            var archMat = st.Cladding ? dark : paintFlat;

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

                // Half-buried in the flank on purpose. Sat on the surface it is a hoop; sunk
                // three centimetres in, only the lip shows and it becomes the edge of the
                // wing where the metal turns down to the tyre.
                Geo.Node("Arch", body, archMesh, archMat,
                         new Vector3(x > 0 ? wid / 2f - 0.032f : -(wid / 2f - 0.032f), wheelR, az),
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
                    Geo.Box("RoofRail", body, new Vector3(0.045f, 0.035f, (uFront - uRear) * canLen * 0.88f),
                            new Vector3(rx, roofY + 0.030f, ZAt((uFront + uRear) * 0.5f)), dark);

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
