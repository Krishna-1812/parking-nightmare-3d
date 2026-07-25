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
        /// A standard car: painted hull, glass canopy, four wheels with the front pair on
        /// steering groups, light bars, bumpers, mirrors and plates.
        /// </summary>
        public static Rig BuildStandard(Transform parent, string key, VehicleDef veh,
                                        Color bodyC, Color roofC,
                                        float bodyH = 0.62f, float cabHeight = 0.55f,
                                        float cabLenFrac = 0.5f, float cabOffFrac = -0.06f,
                                        float wheelR = 0.34f)
        {
            float len = (float)veh.Len, wid = (float)veh.Wid;
            float cabLen = len * cabLenFrac, cabOff = len * cabOffFrac;
            float baseY = wheelR - 0.05f;
            float cabW = wid * 0.86f;
            float half = len * 0.5f;

            var root = new GameObject("Car_" + key).transform;
            root.SetParent(parent, false);
            var body = new GameObject("Body").transform;
            body.SetParent(root, false);

            // ---- painted shell ----
            var hull = Hull(len, bodyH, wid, new HullOpts
            {
                Key = $"bd{len}_{bodyH}_{wid}",
                PCross = 3.4f, PPlan = 5.5f, Tumble = 0.10f, WNose = 0.86f, WTail = 0.92f,
                Top = u => 1f - 0.15f * S3(0.7f, 0.98f, u) - 0.08f * (1f - S3(0.03f, 0.22f, u)),
                Bot = u => 0.10f * S3(0.82f, 1f, u) + 0.08f * (1f - S3(0f, 0.14f, u)),
            });
            var paint = MatLib.Textured("mat_paint" + ColorUtility.ToHtmlStringRGB(bodyC),
                ProcTex.CarSide(bodyC), Color.white, Vector2.one, smoothness: 0.72f);
            Geo.Node("Shell", body, hull, paint, new Vector3(0, baseY + bodyH / 2f, 0));

            // ---- glass canopy: plateau roof with a raked windscreen, sunk into the body ----
            var canopy = Hull(cabLen + cabHeight * 0.9f, cabHeight * 0.92f, cabW, new HullOpts
            {
                Key = $"cn{cabLen}_{cabHeight}_{cabW}",
                PCross = 2.7f, PPlan = 3.4f, Tumble = 0.32f, WNose = 0.95f, WTail = 0.97f,
                Top = u => 0.14f + 0.86f * Mathf.Min(1f, 1.3f * Mathf.Pow(
                    Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp(u, 0.001f, 0.999f), 0.9f)), 0.8f)),
                Bot = _ => 0f,
            });
            var glassMat = MatLib.Textured("mat_canopy", ProcTex.CanopySide(), Color.white,
                                           Vector2.one, smoothness: 0.92f);
            Geo.Node("Canopy", body, canopy, glassMat,
                     new Vector3(0, baseY + bodyH + cabHeight * 0.46f - 0.08f, cabOff - cabHeight * 0.1f));

            var dark = MatLib.Solid(new Color(0.063f, 0.071f, 0.086f), 0.35f);
            var chrome = MatLib.Solid(new Color(0.72f, 0.74f, 0.78f), 0.85f, 0.9f);

            // shark fin + twin exhaust tips
            Geo.Box("Fin", body, new Vector3(0.05f, 0.07f, 0.16f),
                    new Vector3(0, baseY + bodyH + cabHeight * 0.8f, cabOff - cabLen * 0.3f), dark);
            foreach (float x in new[] { wid * 0.3f, wid * 0.18f })
                Geo.Node("Exhaust", body, Geo.Cylinder(0.045f, 0.045f, 0.14f, 8), dark,
                         new Vector3(x, wheelR - 0.04f, -half + 0.03f),
                         Quaternion.Euler(90, 0, 0));

            // mirrors
            float cabF = cabOff + cabLen / 2f;
            // flat body colour, not the panel texture: a 15 cm box lands on whatever part
            // of the flank atlas its UVs happen to hit, which was the wheel-arch AO —
            // making the mirrors read as black wings rather than painted caps
            var paintFlat = MatLib.Solid(bodyC, 0.72f);
            foreach (float x in new[] { cabW / 2f + 0.07f, -(cabW / 2f + 0.07f) })
                Geo.Box("Mirror", body, new Vector3(0.13f, 0.08f, 0.07f),
                        new Vector3(x, baseY + bodyH + 0.05f, cabF - 0.04f), paintFlat);

            // grille and plates
            Geo.Box("Grille", body, new Vector3(0.44f, 0.13f, 0.02f),
                    new Vector3(0, baseY + bodyH * 0.42f, half - 0.01f), dark);
            Geo.Box("PlateF", body, new Vector3(0.34f, 0.12f, 0.02f),
                    new Vector3(0, baseY + bodyH * 0.2f, half - 0.045f),
                    MatLib.Solid(new Color(0.88f, 0.88f, 0.84f), 0.3f));
            Geo.Box("PlateR", body, new Vector3(0.34f, 0.12f, 0.02f),
                    new Vector3(0, baseY + bodyH * 0.26f, -half + 0.02f),
                    MatLib.Solid(new Color(0.88f, 0.88f, 0.84f), 0.3f));

            // ---- wheels ----
            var tyre = MatLib.Solid(new Color(0.086f, 0.090f, 0.102f), 0.18f);
            var hubMat = MatLib.Solid(new Color(0.62f, 0.64f, 0.68f), 0.72f, 0.85f);
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

                // cylinder is about +Y; roll it onto the lateral axis so it spins about X
                var tyreGo = Geo.Node("Tyre", wheel, Geo.Cylinder(wheelR, wheelR, 0.26f, 20), tyre,
                                      Vector3.zero, Quaternion.Euler(0, 0, 90));
                Geo.Node("Hub", wheel, Geo.Cylinder(wheelR * 0.55f, wheelR * 0.55f, 0.28f, 12), hubMat,
                         Vector3.zero, Quaternion.Euler(0, 0, 90));
                spin.Add(wheel);

                Geo.Node("Arch", body, archMesh, dark,
                         new Vector3(x > 0 ? wid / 2f - 0.02f : -(wid / 2f - 0.02f), wheelR, az),
                         Quaternion.Euler(0, 90, 0), shadows: false);
            }

            // ---- bumper lips and full-width light bars ----
            // Inset from the reference's own 0.82 x width at +/-(half - 0.03): the hull's
            // plan superellipse rounds the nose and tail hard, so a bar sized to the full
            // beam pokes out of the corners as two floating black rods.
            var bumper = MatLib.Solid(new Color(0.141f, 0.149f, 0.180f), 0.25f);
            Geo.Node("BumperF", body, Geo.Cylinder(0.07f, 0.07f, wid * 0.70f, 10), bumper,
                     new Vector3(0, wheelR + 0.05f, half - 0.13f), Quaternion.Euler(0, 0, 90));
            Geo.Node("BumperR", body, Geo.Cylinder(0.07f, 0.07f, wid * 0.70f, 10), bumper,
                     new Vector3(0, wheelR + 0.05f, -half + 0.13f), Quaternion.Euler(0, 0, 90));

            float lightY = baseY + bodyH * 0.62f;
            var headMat = MatLib.Emissive(new Color(0.13f, 0.13f, 0.13f),
                                          new Color(1f, 0.95f, 0.75f), 2.2f);
            Geo.Node("Headlights", body, Geo.Cylinder(0.045f, 0.045f, wid * 0.56f, 10), headMat,
                     new Vector3(0, lightY, half - 0.05f), Quaternion.Euler(0, 0, 90), shadows: false);

            // brake light gets its own instance: the driver flares it, so it must not be
            // shared with any other car's tail bar
            var brakeMat = new Material(MatLib.Emissive(new Color(0.33f, 0.07f, 0.07f),
                                                        new Color(1f, 0.23f, 0.19f), 0.35f));
            Geo.Node("Taillights", body, Geo.Cylinder(0.04f, 0.04f, wid * 0.6f, 10), brakeMat,
                     new Vector3(0, lightY + 0.04f, -half + 0.04f), Quaternion.Euler(0, 0, 90),
                     shadows: false);

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

        /// <summary>The player's hatch, with the rust patch the flavour text promises.</summary>
        public static Rig BuildHatch(Transform parent, VehicleDef veh)
        {
            var bodyC = ParseHex(veh.BodyHex, new Color(0.753f, 0.337f, 0.231f));
            var roofC = ParseHex(veh.RoofHex, bodyC * 0.86f);
            var rig = BuildStandard(parent, "hatch", veh, bodyC, roofC);

            Geo.Box("Rust", rig.Body, new Vector3(0.04f, 0.14f, 0.5f),
                    new Vector3((float)veh.Wid / 2f - 0.08f, 0.52f, -(float)veh.Len * 0.08f),
                    MatLib.Solid(new Color(0.478f, 0.290f, 0.180f), 0.05f));
            return rig;
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
