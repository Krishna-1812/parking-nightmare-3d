using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// Everything beside the road: houses, trees, mailboxes, hedges, bins, power poles.
    ///
    /// Placement follows <c>World.buildScenery</c> in <c>src/n3_d.js</c> — houses every
    /// 17 m on both sides with an 18% skip, trees on the district's <c>treeEvery</c>
    /// spacing, nothing within 20 m of an intersection. The draws come from a seeded
    /// <see cref="Rng"/>, so a given mission always dresses itself identically; the
    /// reference seeds its layout stream the same way (<c>World.rng</c>, n3_d.js:580).
    ///
    /// None of this is collidable. Physics is 2D and knows only the road cross-section
    /// (DESIGN_SPEC §2, §8), so a house is scenery in the strictest sense — putting
    /// colliders on it would be the first step toward a second, contradictory simulation.
    /// </summary>
    public static class Scenery
    {
        static readonly string[] ShingleHex = { "#7a4438", "#5c4632", "#43507a", "#4c6244", "#585450" };

        public static void Build(CompiledRoute route, int lanes, District d, uint seed, Transform parent)
        {
            var root = new GameObject("PN3D_Scenery").transform;
            root.SetParent(parent, false);

            var rng = new Rng(seed);
            var sec = RoadBuilder.SectionFor(lanes);

            if (d.Houses) Houses(route, sec, d, rng, root);
            if (d.TreeEvery > 0) Trees(route, sec, d, rng, root);
            Furniture(route, sec, d, rng, root);
            if (d.Houses) PowerLine(route, sec, rng, root);
        }

        static bool NearIntersection(CompiledRoute route, double s, double pad)
        {
            foreach (var it in route.Inters)
                if (s > it.S0 - pad && s < it.S1 + pad) return true;
            return false;
        }

        /// <summary>Place a prop at arc position s, lateral offset t, facing the road.</summary>
        static void Place(GameObject go, CompiledRoute route, double s, double t, float yaw = 0f)
        {
            RoadBuilder.PosAtExtended(route, s, t, out double x, out double y, out double h);
            go.transform.position = WorldBuilder.ToWorld(x, y, RoadBuilder.CurbY);
            go.transform.rotation = WorldBuilder.ToRotation(h) * Quaternion.Euler(0, yaw, 0);
        }

        /// <summary>
        /// The arc range scenery covers: the whole corridor the road builder draws,
        /// including the leads past each end. A street that stops being dressed exactly
        /// where the route starts is the clearest possible tell that the world is a strip.
        /// </summary>
        static (double From, double To) Span(CompiledRoute route, double inset)
            => (-RoadBuilder.Lead + inset, route.Length + RoadBuilder.Lead - inset);

        // ------------------------------------------------------------------ houses

        static void Houses(CompiledRoute route, RoadBuilder.Section sec, District d, Rng rng, Transform root)
        {
            float wallT = sec.WalkOuter;

            var (from, to) = Span(route, 14);
            for (double s = from; s < to; s += 17)
            {
                if (NearIntersection(route, s, 20)) continue;
                foreach (int sgn in new[] { 1, -1 })
                {
                    if (rng.Chance(0.18)) continue;
                    float depth = (float)rng.Rand(6, 8), wide = (float)rng.Rand(7, 10);
                    float hT = wallT + depth / 2f + 1.5f;

                    var go = new GameObject("House");
                    go.transform.SetParent(root, false);
                    BuildHouse(go.transform, d, rng, wide, depth);
                    // The house is authored the way the reference authors it: ridge along
                    // local X, doors and windows on the local +/-Z faces. Under
                    // ToRotation, local +X is LATERAL and +Z is along the street — the
                    // opposite of what we want — so yaw 90 turns the frontage to face the
                    // road and lays the ridge parallel to it.
                    Place(go, route, s, sgn * hT, 90f);
                }
            }
        }

        static void BuildHouse(Transform g, District d, Rng rng, float wide, float depth)
        {
            string wallHex = d.WallHex[(int)(rng.Next() * d.WallHex.Length)];
            var wallC = ColorUtility.TryParseHtmlString(wallHex, out var wc) ? wc : Color.white;
            float bodyH = (float)rng.Rand(3.2, 4.2);
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

            Geo.Box("Body", g, new Vector3(wide, bodyH, depth), new Vector3(0, bodyH / 2f, 0), siding);
            Geo.Box("Foundation", g, new Vector3(wide + 0.12f, 0.42f, depth + 0.12f),
                    new Vector3(0, 0.21f, 0), MatLib.Solid(new Color(0.604f, 0.588f, 0.549f)));

            // shingled gable roof with an eave overhang, ridge parallel to the street
            float rise = (float)rng.Rand(1.5, 2.1);
            var roof = Geo.Node("Roof", g, Geo.GableRoof(wide + 0.8f, depth + 0.9f, rise), shingleMat,
                                new Vector3(0, bodyH - 0.02f, 0));
            roof.GetComponent<MeshRenderer>().sharedMaterials = new[] { shingleMat, gableMat };
            Geo.Box("Fascia", g, new Vector3(wide + 0.5f, 0.22f, depth + 0.5f),
                    new Vector3(0, bodyH + 0.05f, 0), trim);
            Geo.Box("RidgeCap", g, new Vector3(wide + 0.86f, 0.1f, 0.34f),
                    new Vector3(0, bodyH + rise, 0), MatLib.Solid(shingC * 0.75f));

            // door, porch and windows on both street-facing sides — one of them faces us
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
                {
                    Geo.Box("WinFrame", g, new Vector3(1.3f, 1.1f, 0.08f), new Vector3(wx, 1.9f, zf), trim);
                    Geo.Box("Win", g, new Vector3(1.1f, 0.9f, 0.16f), new Vector3(wx, 1.9f, zf), glassMat);
                    Geo.Box("Sill", g, new Vector3(1.4f, 0.09f, 0.16f), new Vector3(wx, 1.32f, zf), trim);
                    foreach (float sx in new[] { -0.78f, 0.78f })
                        Geo.Box("Shutter", g, new Vector3(0.2f, 1.06f, 0.06f),
                                new Vector3(wx + sx, 1.9f, zf), shutterMat);
                }
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

        // ------------------------------------------------------------------ trees

        static void Trees(CompiledRoute route, RoadBuilder.Section sec, District d, Rng rng, Transform root)
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
                BuildTree(go.transform, rng);
                Place(go, route, s, t, (float)rng.Rand(0, 360));
                go.transform.localScale = Vector3.one * (float)rng.Rand(0.85, 1.25);
            }
        }

        static void BuildTree(Transform g, Rng rng)
        {
            var bark = MatLib.Solid(new Color(0.353f, 0.243f, 0.161f), 0.06f);

            if (rng.Chance(0.22))
            {
                // conifer: tapered trunk, three jittered cone tiers, dark base tier
                var dark = MatLib.Solid(new Color(0.16f, 0.36f, 0.20f), 0.05f);
                var light = MatLib.Solid(new Color(0.24f, 0.55f, 0.27f), 0.05f);
                Geo.Node("Trunk", g, Geo.Cylinder(0.17f, 0.09f, 1.9f, 7), bark, new Vector3(0, 0.95f, 0));
                int i = 0;
                foreach (var (y, sc) in new[] { (1.75f, 1.0f), (2.6f, 0.72f), (3.3f, 0.46f) })
                {
                    var tier = Geo.Node($"Tier{i}", g, Geo.Cylinder(1.05f, 0f, 1.5f, 9, flat: true),
                                        i == 0 ? dark : light, new Vector3(0, y, 0));
                    tier.transform.localScale = Vector3.one * sc * (float)rng.Rand(0.92, 1.08);
                    tier.transform.localRotation = Quaternion.Euler(0, (float)rng.Rand(0, 360), 0);
                    i++;
                }
                return;
            }

            // broadleaf: trunk, a couple of branches, faceted canopy blobs
            var leafLight = MatLib.Solid(new Color(0.29f, 0.61f, 0.29f), 0.05f);
            var leafDark = MatLib.Solid(new Color(0.19f, 0.40f, 0.19f), 0.05f);
            Geo.Node("Trunk", g, Geo.Cylinder(0.26f, 0.17f, 2.3f, 8), bark, new Vector3(0, 1.15f, 0));

            int blobs = rng.Chance(0.5) ? 3 : 4;
            for (int i = 0; i < blobs; i++)
            {
                float r = (float)rng.Rand(0.85, 1.35);
                var pos = new Vector3((float)rng.Rand(-0.7, 0.7),
                                      2.5f + (float)rng.Rand(-0.2, 0.9),
                                      (float)rng.Rand(-0.7, 0.7));
                var blob = Geo.Node($"Canopy{i}", g, Geo.Blob(i + 1), i == 0 ? leafDark : leafLight, pos);
                blob.transform.localScale = new Vector3(r, r * (float)rng.Rand(0.8, 1.05), r);
                blob.transform.localRotation = Quaternion.Euler(
                    (float)rng.Rand(0, 360), (float)rng.Rand(0, 360), (float)rng.Rand(0, 360));
            }
        }

        // ------------------------------------------------------------------ street furniture

        static void Furniture(CompiledRoute route, RoadBuilder.Section sec, District d, Rng rng, Transform root)
        {
            var post = MatLib.Solid(new Color(0.35f, 0.26f, 0.20f), 0.1f);
            var metal = MatLib.Solid(new Color(0.30f, 0.33f, 0.38f), 0.55f, 0.6f);
            var hedge = MatLib.Solid(new Color(0.22f, 0.44f, 0.22f), 0.05f);

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
                        Place(mb, route, s + rng.Rand(-2, 2), edge);
                    }

                    if (rng.Chance(0.4))
                    {
                        var h = new GameObject("Hedge");
                        h.transform.SetParent(root, false);
                        float len = (float)rng.Rand(2.4, 4.2);
                        Geo.Box("Bush", h.transform, new Vector3(len, 0.85f, 0.7f),
                                new Vector3(0, 0.42f, 0), hedge);
                        Place(h, route, s + rng.Rand(-4, 4), sgn * (sec.WalkOuter + 0.9), 90f);
                    }

                    if (rng.Chance(0.22))
                    {
                        var bin = new GameObject("Bin");
                        bin.transform.SetParent(root, false);
                        Geo.Node("Body", bin.transform, Geo.Cylinder(0.26f, 0.30f, 0.9f, 10),
                                 MatLib.Solid(new Color(0.26f, 0.31f, 0.28f), 0.2f), new Vector3(0, 0.45f, 0));
                        Geo.Node("Lid", bin.transform, Geo.Cylinder(0.32f, 0.28f, 0.1f, 10),
                                 MatLib.Solid(new Color(0.18f, 0.22f, 0.20f), 0.2f), new Vector3(0, 0.94f, 0));
                        Place(bin, route, s + rng.Rand(-6, 6), sgn * (sec.RoadHalf + 0.9));
                    }
                }
            }
        }

        /// <summary>
        /// Poles with a catenary span between them. The sag is what sells the wire — a
        /// straight line between two poles reads as a scratch on the lens.
        /// </summary>
        static void PowerLine(CompiledRoute route, RoadBuilder.Section sec, Rng rng, Transform root)
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
                Place(go, route, s, t);
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
