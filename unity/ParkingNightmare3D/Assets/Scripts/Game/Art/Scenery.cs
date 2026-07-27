using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// Everything beside the road: lots, houses, trees, hedges, fences, bins, power poles.
    ///
    /// Placement follows <c>World.buildScenery</c> in <c>src/n3_d.js</c> — a house pitch of
    /// 17 m on both sides with an 18% skip, trees on the district's <c>treeEvery</c>
    /// spacing, nothing within 20 m of an intersection. The draws come from a seeded
    /// <see cref="Rng"/>, so a given mission always dresses itself identically; the
    /// reference seeds its layout stream the same way (<c>World.rng</c>, n3_d.js:580).
    ///
    /// WHAT CHANGED AND WHY. The rhythm above is unaltered, but what sits in it is not.
    /// Previously each prop was scattered independently against the same arc positions:
    /// houses on open lawn, mailboxes somewhere near them, hedges somewhere else, and
    /// nothing joining any of it to the road. From the air it read as a row of identical
    /// boxes on a billiard table, because that is what it was. A house does not stand on
    /// grass, it stands on a LOT — with a driveway to the kerb, a path to its door, a
    /// boundary to the neighbours and a garden in between. Building the lot as one thing
    /// and hanging the props off it is what turns a row of houses into a street, and it
    /// costs no more geometry than scattering them did.
    ///
    /// None of this is collidable. Physics is 2D and knows only the road cross-section
    /// (DESIGN_SPEC §2, §8), so a house is scenery in the strictest sense — putting
    /// colliders on it would be the first step toward a second, contradictory simulation.
    /// </summary>
    public static class Scenery
    {
        static readonly string[] ShingleHex = { "#7a4438", "#5c4632", "#43507a", "#4c6244", "#585450" };

        /// <summary>House plans. The roofline is what reads from a car, so it leads.</summary>
        enum Plan { Gable, Hip, Wing, Garage, TwoStorey }

        public static void Build(CompiledRoute route, int lanes, District d, uint seed,
                                 Transform parent, Terrain ground)
        {
            var root = new GameObject("PN3D_Scenery").transform;
            root.SetParent(parent, false);

            var rng = new Rng(seed);
            var sec = RoadBuilder.SectionFor(lanes);

            if (d.Houses) Lots(route, sec, d, rng, root, ground);
            if (d.TreeEvery > 0) Trees(route, sec, d, rng, root, ground);
            Furniture(route, sec, d, rng, root, ground);
            if (d.Houses) PowerLine(route, sec, rng, root, ground);
            Backdrop(route, sec, rng, root, ground);
        }

        static bool NearIntersection(CompiledRoute route, double s, double pad)
        {
            foreach (var it in route.Inters)
                if (s > it.S0 - pad && s < it.S1 + pad) return true;
            return false;
        }

        /// <summary>
        /// Place a prop at arc position s, lateral offset t, standing ON THE GROUND.
        ///
        /// The elevation used to be the constant kerb height, which is only correct while
        /// the ground is a plane. It is not one any more, so the height comes from the same
        /// field the ground mesh was built from — see <see cref="Terrain.HeightAt"/>. Props
        /// inside the flat corridor get exactly the old answer, so nothing near the road
        /// has moved.
        /// </summary>
        static void Place(GameObject go, CompiledRoute route, Terrain ground,
                          double s, double t, float yaw = 0f, float sink = 0f)
        {
            RoadBuilder.PosAtExtended(route, s, t, out double x, out double y, out double h);
            var w = WorldBuilder.ToWorld(x, y, RoadBuilder.CurbY);
            w.y += ground.HeightAt(w.x, w.z) - sink;
            go.transform.position = w;
            go.transform.rotation = WorldBuilder.ToRotation(h) * Quaternion.Euler(0, yaw, 0);
        }

        /// <summary>
        /// The arc range scenery covers: the whole corridor the road builder draws,
        /// including the leads past each end. A street that stops being dressed exactly
        /// where the route starts is the clearest possible tell that the world is a strip.
        /// </summary>
        static (double From, double To) Span(CompiledRoute route, double inset)
            => (-RoadBuilder.Lead + inset, route.Length + RoadBuilder.Lead - inset);

        // ------------------------------------------------------------------ lots

        static void Lots(CompiledRoute route, RoadBuilder.Section sec, District d,
                         Rng rng, Transform root, Terrain ground)
        {
            float walk = sec.WalkOuter;

            var (from, to) = Span(route, 14);
            for (double s = from; s < to; s += 17)
            {
                if (NearIntersection(route, s, 20)) continue;
                foreach (int sgn in new[] { 1, -1 })
                {
                    if (rng.Chance(0.18)) continue;
                    BuildLot(route, sec, d, rng, root, ground, s, sgn, walk);
                }
            }
        }

        static void BuildLot(CompiledRoute route, RoadBuilder.Section sec, District d,
                             Rng rng, Transform root, Terrain ground,
                             double s, int sgn, float walk)
        {
            var plan = PickPlan(rng);
            float depth = (float)rng.Rand(6.2, 8.4);
            float wide = plan == Plan.TwoStorey ? (float)rng.Rand(7.0, 8.6)
                                                : (float)rng.Rand(7.2, 10.2);

            // Setback varies house to house. A constant one is what made the row read as a
            // fence rather than as frontages: real plots are not surveyed to the centimetre
            // and the eye picks the repeat out instantly.
            float setback = (float)rng.Rand(1.2, 3.4);
            float hT = walk + setback + depth / 2f;

            // The house is authored the way the reference authors it: ridge along local X,
            // doors and windows on the local +/-Z faces. Under ToRotation, local +X is
            // LATERAL and +Z is along the street — the opposite of what we want — so yaw 90
            // turns the frontage to face the road and lays the ridge parallel to it.
            var go = new GameObject("House");
            go.transform.SetParent(root, false);
            float garageSide = rng.Chance(0.5) ? 1f : -1f;
            BuildHouse(go.transform, d, rng, wide, depth, plan, garageSide);
            Place(go, route, ground, s, sgn * hT, 90f);

            // ---- driveway: kerb to the house, on the garage side when there is one ----
            // This is the single strongest cue that a house belongs to the road it faces.
            var tarmac = MatLib.Textured("mat_drive", ProcTex.PlainAsphalt(d.Night),
                                         new Color(0.62f, 0.62f, 0.64f), new Vector2(1.4f, 3f),
                                         smoothness: 0.10f);
            // EVERYTHING THAT RUNS AWAY FROM THE KERB is laid out along local X and anchored
            // at its own midpoint. Under ToRotation local +X is LATERAL and +Z is along the
            // street, so a slab authored down +Z and placed at the kerb lies across the
            // carriageway rather than up the garden — which is exactly what the first cut of
            // this did, fences included. Centring on the midpoint also makes the sign of X
            // irrelevant, so there is no second chance to get the handedness backwards.
            float driveOff = plan == Plan.Garage ? garageSide * wide * 0.28f
                                                 : (float)rng.Rand(-1.0, 1.0) * wide * 0.22f;
            double driveS = s + driveOff;
            float driveLen = setback + depth * (plan == Plan.Garage ? 0.55f : 0.18f) + 1.2f;
            var drive = new GameObject("Driveway");
            drive.transform.SetParent(root, false);
            Geo.Box("Slab", drive.transform, new Vector3(driveLen, 0.05f, (float)rng.Rand(2.7, 3.2)),
                    new Vector3(0, 0.02f, 0), tarmac, shadows: false);
            Place(drive, route, ground, driveS, sgn * (walk + driveLen * 0.5f));

            // A parked car on some driveways. Nothing sells a lived-in street faster, and
            // the mesh cache means a resident is free once traffic has built that style
            // once — same key, same hull.
            if (rng.Chance(0.30))
            {
                var res = new GameObject("Resident");
                res.transform.SetParent(root, false);
                int id = (int)(rng.Next() * 9999);
                var st = CarStyles.ForTraffic("sedan", id);
                var veh = new VehicleDef
                {
                    Key = "sedan",
                    Len = rng.Rand(4.2, 4.9),
                    Wid = rng.Rand(1.78, 1.92),
                    Hgt = 1.5,
                };
                CarView.Build(res.transform, $"res_{id}", veh, st, CarStyles.PaintFor(id));
                Place(res, route, ground, driveS, sgn * (walk + driveLen * 0.60f),
                      sgn > 0 ? 90f : 270f);
            }

            // ---- front path from the kerb to the door ----
            var pave = MatLib.Textured("mat_path", ProcTex.Sidewalk(d.Night),
                                       new Color(0.94f, 0.93f, 0.90f), new Vector2(0.6f, 1.6f),
                                       smoothness: 0.06f);
            float pathLen = setback + 0.9f;
            var path = new GameObject("Path");
            path.transform.SetParent(root, false);
            Geo.Box("Slab", path.transform, new Vector3(pathLen, 0.05f, 1.05f),
                    new Vector3(0, 0.02f, 0), pave, shadows: false);
            Place(path, route, ground, s, sgn * (walk + pathLen * 0.5f));

            // ---- boundary to the neighbours ----
            // Alternating fence and hedge, and sometimes neither — an unbroken run of
            // identical boundaries is as obvious a repeat as an unbroken run of houses.
            double boundary = s + 8.5;
            if (!NearIntersection(route, boundary, 14) && rng.Chance(0.62))
            {
                var b = new GameObject("Boundary");
                b.transform.SetParent(root, false);
                float run = hT - walk + depth * 0.5f;
                float mid = walk + run * 0.5f;
                if (rng.Chance(0.55))
                {
                    var picket = MatLib.Solid(new Color(0.90f, 0.89f, 0.85f), 0.10f);
                    int n = Mathf.Max(3, Mathf.RoundToInt(run / 0.34f));
                    for (int i = 0; i < n; i++)
                        Geo.Box("Picket", b.transform, new Vector3(0.05f, 0.95f, 0.06f),
                                new Vector3(0.4f + i * 0.34f - run * 0.5f, 0.48f, 0), picket);
                    foreach (float ry in new[] { 0.30f, 0.78f })
                        Geo.Box("Rail", b.transform, new Vector3(run, 0.07f, 0.04f),
                                new Vector3(0, ry, 0), picket);
                }
                else
                {
                    var leaf = MatLib.Solid(new Color(0.20f, 0.40f, 0.20f), 0.05f);
                    // Broken into clumps of slightly different height, because a hedge
                    // modelled as one long box is a wall painted green.
                    int n = Mathf.Max(2, Mathf.RoundToInt(run / 1.5f));
                    for (int i = 0; i < n; i++)
                        Geo.Box("Clump", b.transform,
                                new Vector3(1.55f, (float)rng.Rand(0.95, 1.35),
                                            (float)rng.Rand(0.75, 0.95)),
                                new Vector3(0.5f + i * 1.5f - run * 0.5f,
                                            (float)rng.Rand(0.48, 0.66),
                                            (float)rng.Rand(-0.06, 0.06)), leaf);
                }
                Place(b, route, ground, boundary, sgn * mid);
            }

            // ---- garden ----
            if (rng.Chance(0.7)) Shrub(root, route, ground, rng,
                                       s + rng.Rand(-3.5, 3.5), sgn * (walk + rng.Rand(1.0, 2.6)));
            if (rng.Chance(0.35)) Shrub(root, route, ground, rng,
                                        s + rng.Rand(-5, 5), sgn * (hT + depth * 0.5f + 1.5));
        }

        static Plan PickPlan(Rng rng)
        {
            double r = rng.Next();
            if (r < 0.30) return Plan.Gable;
            if (r < 0.52) return Plan.Hip;
            if (r < 0.72) return Plan.Wing;
            if (r < 0.89) return Plan.Garage;
            return Plan.TwoStorey;
        }

        // ------------------------------------------------------------------ houses

        static void BuildHouse(Transform g, District d, Rng rng, float wide, float depth,
                               Plan plan, float garageSide)
        {
            string wallHex = d.WallHex[(int)(rng.Next() * d.WallHex.Length)];
            var wallC = ColorUtility.TryParseHtmlString(wallHex, out var wc) ? wc : Color.white;
            float storey = (float)rng.Rand(3.2, 4.2);
            float bodyH = plan == Plan.TwoStorey ? storey + (float)rng.Rand(2.5, 3.0) : storey;
            string shingHex = ShingleHex[(int)(rng.Next() * ShingleHex.Length)];
            var shingC = ColorUtility.TryParseHtmlString(shingHex, out var shc) ? shc : Color.grey;

            var siding = MatLib.Textured("mat_siding" + wallHex, ProcTex.Siding(), wallC,
                                         new Vector2(wide / 2.2f, bodyH / 2.2f), smoothness: 0.05f);
            var trim = MatLib.Solid(new Color(0.949f, 0.937f, 0.902f), 0.12f);
            var shingleMat = MatLib.Textured("mat_shing" + shingHex, ProcTex.Shingle(shingHex),
                                             Color.white, new Vector2(1, 1), smoothness: 0.05f);
            var gableMat = MatLib.Solid(wallC * 0.92f, 0.05f);
            var doorMat = MatLib.Solid(new Color(0.42f, 0.29f, 0.18f), 0.2f);
            var glassMat = MatLib.Glass(new Color(0.10f, 0.14f, 0.17f));
            var shutterMat = MatLib.Solid(new Color(0.216f, 0.259f, 0.227f), 0.1f);
            var gutterMat = MatLib.Solid(new Color(0.86f, 0.85f, 0.82f), 0.22f);

            Geo.Box("Body", g, new Vector3(wide, bodyH, depth), new Vector3(0, bodyH / 2f, 0), siding);
            Geo.Box("Foundation", g, new Vector3(wide + 0.12f, 0.42f, depth + 0.12f),
                    new Vector3(0, 0.21f, 0), MatLib.Solid(new Color(0.604f, 0.588f, 0.549f)));

            // Roof. Pitch varies per house as well as per plan — the old fixed 1.5..2.1 rise
            // on a fixed depth gave every roof the same angle, which is most of why the row
            // looked stamped out.
            float rise = depth * (float)rng.Rand(0.20, 0.34);
            float eave = (float)rng.Rand(0.5, 1.0);
            var roofMesh = plan == Plan.Hip
                ? Geo.HipRoof(wide + eave, depth + eave, rise)
                : Geo.GableRoof(wide + eave, depth + eave * 1.1f, rise);
            var roof = Geo.Node("Roof", g, roofMesh, shingleMat, new Vector3(0, bodyH - 0.02f, 0));
            roof.GetComponent<MeshRenderer>().sharedMaterials = new[] { shingleMat, gableMat };

            Geo.Box("Fascia", g, new Vector3(wide + eave * 0.7f, 0.22f, depth + eave * 0.7f),
                    new Vector3(0, bodyH + 0.05f, 0), trim);
            if (plan != Plan.Hip)
                Geo.Box("RidgeCap", g, new Vector3(wide + eave + 0.06f, 0.1f, 0.34f),
                        new Vector3(0, bodyH + rise, 0), MatLib.Solid(shingC * 0.75f));

            // Gutters along both eaves and a downpipe. Small, but it is the difference
            // between a roof that was built and a roof that was extruded.
            foreach (int zs in new[] { 1, -1 })
                Geo.Box("Gutter", g, new Vector3(wide + eave * 0.7f, 0.11f, 0.13f),
                        new Vector3(0, bodyH + 0.02f, zs * (depth + eave * 0.7f) * 0.5f), gutterMat);
            Geo.Box("Downpipe", g, new Vector3(0.1f, bodyH, 0.1f),
                    new Vector3(wide * 0.46f, bodyH * 0.5f, depth * 0.5f + 0.06f), gutterMat);

            Frontage(g, rng, wide, depth, storey, trim, doorMat, glassMat, shutterMat, shingC);

            if (plan == Plan.TwoStorey)
                foreach (int zs in new[] { 1, -1 })
                    foreach (float wx in new[] { -wide * 0.28f, 0f, wide * 0.28f })
                        Window(g, wx, storey + 1.35f, zs * (depth / 2f + 0.02f),
                               trim, glassMat, shutterMat, 1.1f, 1.0f);

            // ---- projecting wing or attached garage ----
            if (plan == Plan.Wing)
            {
                float ww = wide * 0.40f, wd = depth * 0.55f;
                float wz = depth * 0.5f + wd * 0.5f - 0.4f;
                float wh = storey * 0.92f;
                Geo.Box("Wing", g, new Vector3(ww, wh, wd),
                        new Vector3(garageSide * wide * 0.28f, wh / 2f, wz), siding);
                float wr = wd * 0.34f;
                var wroof = Geo.Node("WingRoof", g, Geo.GableRoof(ww + 0.5f, wd + 0.5f, wr), shingleMat,
                                     new Vector3(garageSide * wide * 0.28f, wh - 0.02f, wz));
                wroof.transform.localRotation = Quaternion.Euler(0, 90, 0);
                wroof.GetComponent<MeshRenderer>().sharedMaterials = new[] { shingleMat, gableMat };
                Window(g, garageSide * wide * 0.28f, 1.85f, wz + wd * 0.5f + 0.03f,
                       trim, glassMat, shutterMat, 1.5f, 1.15f);
            }
            else if (plan == Plan.Garage)
            {
                float gw = 3.4f, gd = depth * 0.78f, gh = storey * 0.74f;
                float gx = garageSide * (wide * 0.5f + gw * 0.5f - 0.15f);
                float gz = depth * 0.5f - gd * 0.5f + 0.6f;
                Geo.Box("Garage", g, new Vector3(gw, gh, gd), new Vector3(gx, gh / 2f, gz), siding);
                var groof = Geo.Node("GarageRoof", g, Geo.GableRoof(gw + 0.5f, gd + 0.5f, gd * 0.24f),
                                     shingleMat, new Vector3(gx, gh - 0.02f, gz));
                groof.GetComponent<MeshRenderer>().sharedMaterials = new[] { shingleMat, gableMat };
                Geo.Box("GarageDoor", g, new Vector3(gw * 0.78f, gh * 0.72f, 0.12f),
                        new Vector3(gx, gh * 0.36f, gz + gd * 0.5f + 0.02f),
                        MatLib.Solid(new Color(0.88f, 0.87f, 0.84f), 0.14f));
                for (int i = 1; i <= 3; i++)
                    Geo.Box("DoorRib", g, new Vector3(gw * 0.78f, 0.04f, 0.16f),
                            new Vector3(gx, gh * 0.72f * i / 4f, gz + gd * 0.5f + 0.03f),
                            MatLib.Solid(new Color(0.72f, 0.71f, 0.69f), 0.14f));
            }

            if (rng.Chance(0.4))
            {
                Geo.Box("Chimney", g, new Vector3(0.7f, 1.8f, 0.7f),
                        new Vector3(wide * 0.28f, bodyH + 1.1f, depth * 0.15f),
                        MatLib.Solid(new Color(0.604f, 0.353f, 0.267f)));
                Geo.Box("ChimneyCap", g, new Vector3(0.82f, 0.1f, 0.82f),
                        new Vector3(wide * 0.28f, bodyH + 2.02f, depth * 0.15f),
                        MatLib.Solid(new Color(0.431f, 0.416f, 0.392f)));
            }
        }

        /// <summary>Door, porch and windows on both street-facing sides — one of them faces us.</summary>
        static void Frontage(Transform g, Rng rng, float wide, float depth, float storey,
                             Material trim, Material doorMat, Material glassMat,
                             Material shutterMat, Color shingC)
        {
            foreach (int zs in new[] { 1, -1 })
            {
                float zf = zs * (depth / 2f + 0.02f);
                Geo.Box("Door", g, new Vector3(1.0f, 1.9f, 0.1f), new Vector3(0, 0.95f, zf), doorMat);
                Geo.Box("Knob", g, new Vector3(0.08f, 0.08f, 0.14f), new Vector3(0.32f, 0.95f, zf),
                        MatLib.Solid(new Color(0.847f, 0.753f, 0.353f), 0.7f, 0.8f));

                Geo.Box("PorchSlab", g, new Vector3(2.1f, 0.16f, 1.15f),
                        new Vector3(0, 0.08f, zs * (depth / 2f + 0.55f)),
                        MatLib.Solid(new Color(0.667f, 0.651f, 0.608f)));
                foreach (float px in new[] { -0.85f, 0.85f })
                    Geo.Box("PorchPost", g, new Vector3(0.1f, 2.15f, 0.1f),
                            new Vector3(px, 1.08f, zs * (depth / 2f + 0.95f)), trim);
                var hood = Geo.Box("PorchHood", g, new Vector3(2.3f, 0.1f, 1.35f),
                                   new Vector3(0, 2.24f, zs * (depth / 2f + 0.5f)),
                                   MatLib.Solid(shingC * 1.08f));
                hood.transform.localRotation = Quaternion.Euler(zs * 9.2f, 0, 0);

                foreach (float wx in new[] { -wide * 0.28f, wide * 0.28f })
                    Window(g, wx, 1.9f, zf, trim, glassMat, shutterMat, 1.3f, 1.1f);
            }
        }

        static void Window(Transform g, float x, float y, float z, Material trim,
                           Material glass, Material shutter, float w, float h)
        {
            Geo.Box("WinFrame", g, new Vector3(w, h, 0.08f), new Vector3(x, y, z), trim);
            Geo.Box("Win", g, new Vector3(w - 0.2f, h - 0.2f, 0.16f), new Vector3(x, y, z), glass);
            Geo.Box("Sill", g, new Vector3(w + 0.1f, 0.09f, 0.16f),
                    new Vector3(x, y - h * 0.53f, z), trim);
            foreach (float sx in new[] { -1f, 1f })
                Geo.Box("Shutter", g, new Vector3(0.2f, h - 0.04f, 0.06f),
                        new Vector3(x + sx * (w * 0.5f + 0.13f), y, z), shutter);
        }

        // ------------------------------------------------------------------ trees

        static void Trees(CompiledRoute route, RoadBuilder.Section sec, District d,
                          Rng rng, Transform root, Terrain ground)
        {
            var (from, to) = Span(route, 10);
            for (double s = from; s < to; s += d.TreeEvery)
            {
                if (NearIntersection(route, s, 12)) continue;
                int sgn = rng.Chance(0.5) ? 1 : -1;
                // on the verge, just outside the sidewalk
                double t = sgn * (sec.WalkOuter - 1.2 + rng.Rand(-0.4, 0.6));
                var go = new GameObject("Tree");
                go.transform.SetParent(root, false);
                BuildTree(go.transform, rng, 1f);
                Place(go, route, ground, s, t, (float)rng.Rand(0, 360));
                go.transform.localScale = Vector3.one * (float)rng.Rand(0.85, 1.25);
            }
        }

        /// <summary>
        /// A tree, and the thing that makes it one is the canopy having more than one value
        /// in it.
        ///
        /// It used to be three or four big blobs in two flat greens, which from any distance
        /// is a lollipop: one silhouette, one tone, no depth. A real canopy is a cloud of
        /// small masses, lit brightly on top and falling into shadow underneath, and that is
        /// almost entirely what tells the eye it is foliage rather than a painted shape. So
        /// the blobs got smaller and far more numerous, and each is tinted by how high in
        /// the crown it sits.
        /// </summary>
        static void BuildTree(Transform g, Rng rng, float scale)
        {
            var bark = MatLib.Solid(new Color(0.353f, 0.243f, 0.161f), 0.06f);

            if (rng.Chance(0.24))
            {
                // conifer: tapered trunk, tiers all the way up rather than three big cones
                Geo.Node("Trunk", g, Geo.Cylinder(0.19f, 0.07f, 4.6f, 7), bark,
                         new Vector3(0, 2.3f, 0));
                int tiers = 5 + (int)(rng.Next() * 3);
                for (int i = 0; i < tiers; i++)
                {
                    float f = i / (float)(tiers - 1);
                    float y = 1.25f + f * 3.3f;
                    float rad = Mathf.Lerp(1.25f, 0.28f, f) * (float)rng.Rand(0.9, 1.1);
                    var col = Color.Lerp(new Color(0.13f, 0.30f, 0.17f),
                                         new Color(0.29f, 0.58f, 0.30f), f * 0.85f + 0.1f);
                    var tier = Geo.Node($"Tier{i}", g,
                                        Geo.Cylinder(rad, rad * 0.12f, 1.15f, 9, flat: true),
                                        MatLib.Solid(col, 0.05f), new Vector3(0, y, 0));
                    tier.transform.localRotation = Quaternion.Euler(
                        (float)rng.Rand(-4, 4), (float)rng.Rand(0, 360), (float)rng.Rand(-4, 4));
                }
                return;
            }

            // Broadleaf. A short trunk that forks, then a crown built from many small
            // masses shaded by height.
            float trunkH = (float)rng.Rand(1.9, 2.8);
            Geo.Node("Trunk", g, Geo.Cylinder(0.28f, 0.16f, trunkH, 8), bark,
                     new Vector3(0, trunkH * 0.5f, 0));
            int limbs = 3;
            for (int i = 0; i < limbs; i++)
            {
                float a = (float)(i * 2.0 * Mathf.PI / limbs + rng.Rand(-0.4, 0.4));
                var limb = Geo.Node($"Limb{i}", g, Geo.Cylinder(0.11f, 0.06f, 1.5f, 6), bark,
                                    new Vector3(Mathf.Cos(a) * 0.42f, trunkH + 0.55f,
                                                Mathf.Sin(a) * 0.42f));
                limb.transform.localRotation = Quaternion.Euler(
                    Mathf.Sin(a) * 34f, 0f, -Mathf.Cos(a) * 34f);
            }

            float crownY = trunkH + (float)rng.Rand(1.0, 1.5);
            float crownR = (float)rng.Rand(1.5, 2.1);
            int blobs = 9 + (int)(rng.Next() * 5);
            for (int i = 0; i < blobs; i++)
            {
                // Spread through a squashed sphere, biased outward so the crown is a shell
                // and not a solid ball — the inside is never seen and costs the same.
                float a = (float)rng.Rand(0, Mathf.PI * 2);
                float rr = Mathf.Sqrt((float)rng.Rand(0.15, 1.0)) * crownR;
                float yy = (float)rng.Rand(-0.55, 0.75) * crownR;
                var pos = new Vector3(Mathf.Cos(a) * rr, crownY + yy, Mathf.Sin(a) * rr);

                // Height in the crown drives the tint: sunlit above, shadowed beneath.
                float lit = Mathf.InverseLerp(-0.6f * crownR, 0.8f * crownR, yy);
                var col = Color.Lerp(new Color(0.13f, 0.27f, 0.14f),
                                     new Color(0.36f, 0.66f, 0.30f), lit)
                        * (float)rng.Rand(0.9, 1.1);
                float r = crownR * (float)rng.Rand(0.34, 0.55);
                var blob = Geo.Node($"Leaf{i}", g, Geo.Blob(1 + i % 5), MatLib.Solid(col, 0.05f), pos);
                blob.transform.localScale = new Vector3(r, r * (float)rng.Rand(0.75, 1.0), r);
                blob.transform.localRotation = Quaternion.Euler(
                    (float)rng.Rand(0, 360), (float)rng.Rand(0, 360), (float)rng.Rand(0, 360));
            }
        }

        static void Shrub(Transform root, CompiledRoute route, Terrain ground, Rng rng,
                          double s, double t)
        {
            var go = new GameObject("Shrub");
            go.transform.SetParent(root, false);
            int n = 2 + (int)(rng.Next() * 3);
            for (int i = 0; i < n; i++)
            {
                float r = (float)rng.Rand(0.32, 0.62);
                var col = Color.Lerp(new Color(0.17f, 0.34f, 0.17f),
                                     new Color(0.33f, 0.56f, 0.26f), (float)rng.Next());
                var b = Geo.Node($"B{i}", go.transform, Geo.Blob(2 + i % 4),
                                 MatLib.Solid(col, 0.05f),
                                 new Vector3((float)rng.Rand(-0.4, 0.4), r * 0.8f,
                                             (float)rng.Rand(-0.4, 0.4)));
                b.transform.localScale = new Vector3(r, r * 0.85f, r);
                b.transform.localRotation = Quaternion.Euler(0, (float)rng.Rand(0, 360), 0);
            }
            Place(go, route, ground, s, t);
        }

        // ------------------------------------------------------------------ backdrop

        /// <summary>
        /// What fills the ground between the street and the fog.
        ///
        /// Without it the world is a dressed strip on an empty lawn, and the emptiness is
        /// the first thing the eye finds because it is most of the frame. These are clumps
        /// of trees and the occasional far-off roof, scattered well beyond the lots and deep
        /// enough into the fog that they cost almost nothing to be wrong about — they are
        /// there to give the middle distance something in it, not to be looked at.
        /// </summary>
        static void Backdrop(CompiledRoute route, RoadBuilder.Section sec, Rng rng,
                             Transform root, Terrain ground)
        {
            var (from, to) = Span(route, -60);
            for (double s = from; s < to; s += 21)
            {
                foreach (int sgn in new[] { 1, -1 })
                {
                    if (rng.Chance(0.42)) continue;
                    double t = sgn * (sec.WalkOuter + rng.Rand(34, 190));

                    if (rng.Chance(0.22))
                    {
                        // a far barn or outbuilding: a box and a roof, nothing more
                        var b = new GameObject("FarBuilding");
                        b.transform.SetParent(root, false);
                        float w = (float)rng.Rand(7, 13), dp = (float)rng.Rand(6, 10),
                              hh = (float)rng.Rand(3.5, 5.5);
                        var wallC = Color.Lerp(new Color(0.60f, 0.56f, 0.50f),
                                               new Color(0.78f, 0.74f, 0.68f), (float)rng.Next());
                        Geo.Box("Body", b.transform, new Vector3(w, hh, dp),
                                new Vector3(0, hh / 2f, 0), MatLib.Solid(wallC, 0.05f));
                        var rf = Geo.Node("Roof", b.transform,
                                          Geo.GableRoof(w + 0.6f, dp + 0.6f, dp * 0.28f),
                                          MatLib.Solid(new Color(0.36f, 0.31f, 0.28f), 0.05f),
                                          new Vector3(0, hh, 0));
                        rf.GetComponent<MeshRenderer>().sharedMaterials = new[]
                        {
                            MatLib.Solid(new Color(0.36f, 0.31f, 0.28f), 0.05f),
                            MatLib.Solid(wallC * 0.9f, 0.05f),
                        };
                        Place(b, route, ground, s + rng.Rand(-8, 8), t, (float)rng.Rand(0, 360));
                        continue;
                    }

                    // a clump of two to four trees
                    int n = 2 + (int)(rng.Next() * 3);
                    for (int i = 0; i < n; i++)
                    {
                        var go = new GameObject("FarTree");
                        go.transform.SetParent(root, false);
                        BuildTree(go.transform, rng, 1f);
                        Place(go, route, ground, s + rng.Rand(-11, 11),
                              t + rng.Rand(-13, 13), (float)rng.Rand(0, 360));
                        go.transform.localScale = Vector3.one * (float)rng.Rand(1.0, 1.7);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ street furniture

        static void Furniture(CompiledRoute route, RoadBuilder.Section sec, District d,
                              Rng rng, Transform root, Terrain ground)
        {
            var post = MatLib.Solid(new Color(0.35f, 0.26f, 0.20f), 0.1f);
            var metal = MatLib.Solid(new Color(0.30f, 0.33f, 0.38f), 0.55f, 0.6f);

            var (from, to) = Span(route, 16);
            for (double s = from; s < to; s += 17)
            {
                if (NearIntersection(route, s, 14)) continue;
                foreach (int sgn in new[] { 1, -1 })
                {
                    double edge = sgn * (sec.WalkOuter - 0.5);

                    if (rng.Chance(0.55))
                    {
                        var mb = new GameObject("Mailbox");
                        mb.transform.SetParent(root, false);
                        Geo.Box("Post", mb.transform, new Vector3(0.09f, 1.0f, 0.09f),
                                new Vector3(0, 0.5f, 0), post);
                        Geo.Box("Box", mb.transform, new Vector3(0.22f, 0.24f, 0.42f),
                                new Vector3(0, 1.08f, 0), metal);
                        Geo.Box("Flag", mb.transform, new Vector3(0.03f, 0.2f, 0.1f),
                                new Vector3(0.13f, 1.24f, -0.1f),
                                MatLib.Solid(new Color(0.85f, 0.24f, 0.26f)));
                        Place(mb, route, ground, s + rng.Rand(-2, 2), edge);
                    }

                    if (rng.Chance(0.22))
                    {
                        var bin = new GameObject("Bin");
                        bin.transform.SetParent(root, false);
                        Geo.Node("Body", bin.transform, Geo.Cylinder(0.26f, 0.30f, 0.9f, 10),
                                 MatLib.Solid(new Color(0.26f, 0.31f, 0.28f), 0.2f), new Vector3(0, 0.45f, 0));
                        Geo.Node("Lid", bin.transform, Geo.Cylinder(0.32f, 0.28f, 0.1f, 10),
                                 MatLib.Solid(new Color(0.18f, 0.22f, 0.20f), 0.2f), new Vector3(0, 0.94f, 0));
                        Place(bin, route, ground, s + rng.Rand(-6, 6), sgn * (sec.RoadHalf + 0.9));
                    }
                }
            }
        }

        /// <summary>
        /// Poles with a catenary span between them. The sag is what sells the wire — a
        /// straight line between two poles reads as a scratch on the lens.
        /// </summary>
        static void PowerLine(CompiledRoute route, RoadBuilder.Section sec, Rng rng,
                              Transform root, Terrain ground)
        {
            const double Spacing = 32.0;
            var poleMat = MatLib.Solid(new Color(0.36f, 0.28f, 0.22f), 0.08f);
            var wireMat = MatLib.Solid(new Color(0.09f, 0.09f, 0.10f), 0.2f);
            double t = sec.WalkOuter - 0.9;

            var tops = new List<(Vector3 P, double S)>();
            var (from, to) = Span(route, 20);
            for (double s = from; s < to; s += Spacing)
            {
                if (NearIntersection(route, s, 10)) continue;
                var go = new GameObject("PowerPole");
                go.transform.SetParent(root, false);
                Geo.Box("Pole", go.transform, new Vector3(0.24f, 8.4f, 0.24f), new Vector3(0, 4.2f, 0), poleMat);
                // crossarm runs across the pole; local +X is lateral, so rotate it to
                // hang over the street like the reference's does
                Geo.Box("Crossarm", go.transform, new Vector3(2.0f, 0.14f, 0.14f),
                        new Vector3(0, 7.7f, 0), poleMat);
                Place(go, route, ground, s, t);
                tops.Add((go.transform.position + Vector3.up * 7.9f, s));
            }

            // A wire is a straight chord; the route is not. Across a 45-degree curve a
            // long span cuts the corner and ends up hanging over the carriageway, so skip
            // any span whose chord is meaningfully shorter than the arc it covers.
            for (int i = 1; i < tops.Count; i++)
            {
                double arc = tops[i].S - tops[i - 1].S;
                float chord = Vector3.Distance(tops[i - 1].P, tops[i].P);
                if (arc <= 0 || chord < arc * 0.985) continue;
                Wire(root, tops[i - 1].P, tops[i].P, wireMat);
            }
        }

        static void Wire(Transform parent, Vector3 a, Vector3 b, Material mat)
        {
            const int Segs = 8;
            float sag = Vector3.Distance(a, b) * 0.035f;
            var prev = a;
            var go = new GameObject("Wire");
            go.transform.SetParent(parent, false);
            for (int i = 1; i <= Segs; i++)
            {
                float u = (float)i / Segs;
                var p = Vector3.Lerp(a, b, u);
                p.y -= Mathf.Sin(u * Mathf.PI) * sag;
                var mid = (prev + p) * 0.5f;
                var seg = Geo.Box($"Seg{i}", go.transform, new Vector3(0.05f, 0.05f, (p - prev).magnitude),
                                  Vector3.zero, mat, shadows: false);
                seg.transform.position = mid;
                seg.transform.rotation = Quaternion.LookRotation(p - prev, Vector3.up);
                prev = p;
            }
        }
    }
}
