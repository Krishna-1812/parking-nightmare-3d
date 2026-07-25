using System;
using System.IO;
using PN3D.Core;

namespace PN3D.Validate
{
    /// <summary>
    /// Diffs <see cref="RouteCompiler"/> and <see cref="RouteEnricher"/> against a golden
    /// reference produced by running the shipping JavaScript (tools/gen_golden_routes.js).
    /// </summary>
    internal static class RouteSuite
    {
        public static void Run(string repo)
        {
            string goldenPath = Path.Combine(repo, "tools", "Validator", "golden_routes.json");
            string missionsPath = Path.Combine(repo, "design-spec", "data", "missions.json");

            if (!File.Exists(goldenPath))
            {
                Checks.Fail("routes", $"golden reference missing: {goldenPath} " +
                                      "(regenerate with: node tools/gen_golden_routes.js)");
                return;
            }

            var golden = JsonValue.Parse(File.ReadAllText(goldenPath));
            var missions = Mission.ParseAll(File.ReadAllText(missionsPath));

            Console.WriteLine($"routes  : {missions.Count} missions, {golden.Count} golden entries");

            if (golden.Count != missions.Count)
                Checks.Fail("routes", $"mission count {missions.Count} != golden {golden.Count}");

            for (int gi = 0; gi < golden.Count; gi++)
            {
                var g = golden[gi];
                int id = g.IntOr("id", -1);
                var mission = missions.Find(m => m.Id == id);
                if (mission == null) { Checks.Fail($"m{id}", "mission id missing from missions.json"); continue; }

                string tag = $"m{id:00} {mission.Name}";

                // ---- enrichment (DESIGN_SPEC §5.1) ----
                Checks.Int(tag, "rawSegs", mission.Segs.Count, g.IntOr("rawSegs", -1));
                Checks.Num(tag, "rawPar", mission.Par, g.DoubleOr("rawPar", -1));

                var route = RouteCompiler.CompileMission(mission);

                Checks.Int(tag, "enrichedSegs", mission.Segs.Count, g.IntOr("enrichedSegs", -1));
                Checks.Num(tag, "enrichedPar", mission.Par, g.DoubleOr("enrichedPar", -1));
                Checks.Bool(tag, "enriched", mission.Enriched, g.BoolOr("enriched", false));

                var gsegs = g["segs"];
                if (gsegs != null && gsegs.Count == mission.Segs.Count)
                {
                    for (int i = 0; i < gsegs.Count; i++)
                    {
                        var a = mission.Segs[i];
                        var b = gsegs[i];
                        Checks.Str(tag, $"seg[{i}].t", a.T, b.OptString("t"));
                        Checks.NumOpt(tag, $"seg[{i}].len", a.Len, b.OptDouble("len"));
                        Checks.NumOpt(tag, $"seg[{i}].r", a.R, b.OptDouble("r"));
                        Checks.NumOpt(tag, $"seg[{i}].a", a.A, b.OptDouble("a"));
                    }
                }
                else
                {
                    Checks.Fail(tag, $"enriched seg count {mission.Segs.Count} != golden {gsegs?.Count}");
                }

                // ---- compiled geometry ----
                Checks.Num(tag, "length", route.Length, g.DoubleOr("length", -1));
                Checks.Int(tag, "ptsCount", route.Pts.Length, g.IntOr("ptsCount", -1));

                var ginters = g["inters"];
                Checks.Int(tag, "inters.count", route.Inters.Count, ginters?.Count ?? -1);
                for (int i = 0; i < Math.Min(route.Inters.Count, ginters?.Count ?? 0); i++)
                {
                    var a = route.Inters[i]; var b = ginters[i];
                    Checks.Num(tag, $"inter[{i}].s0", a.S0, b.DoubleOr("s0", -1));
                    Checks.Num(tag, $"inter[{i}].s1", a.S1, b.DoubleOr("s1", -1));
                    Checks.Num(tag, $"inter[{i}].cx", a.Cx, b.DoubleOr("cx", -1));
                    Checks.Num(tag, $"inter[{i}].cy", a.Cy, b.DoubleOr("cy", -1));
                    Checks.Num(tag, $"inter[{i}].h", a.H, b.DoubleOr("h", -1));
                    Checks.Bool(tag, $"inter[{i}].lights", a.Lights, b.BoolOr("lights", false));
                }

                // enrichment strips `zone`, so this is 0 everywhere — see DESIGN_SPEC §5.1
                Checks.Int(tag, "zones.count", route.Zones.Count, g["zones"]?.Count ?? -1);

                var gcurves = g["curves"];
                Checks.Int(tag, "curves.count", route.Curves.Count, gcurves?.Count ?? -1);
                for (int i = 0; i < Math.Min(route.Curves.Count, gcurves?.Count ?? 0); i++)
                {
                    var a = route.Curves[i]; var b = gcurves[i];
                    Checks.Num(tag, $"curve[{i}].s", a.S, b.DoubleOr("s", -1));
                    Checks.Str(tag, $"curve[{i}].dir", a.Dir, b.OptString("dir"));
                    Checks.Num(tag, $"curve[{i}].end", a.End, b.DoubleOr("end", -1));
                }

                // ---- SampleAt ----
                var gs = g["samples"];
                for (int i = 0; i < (gs?.Count ?? 0); i++)
                {
                    var b = gs[i];
                    var a = route.SampleAt(b.DoubleOr("s", 0));
                    Checks.Num(tag, $"sample[{i}].x", a.X, b.DoubleOr("x", 0));
                    Checks.Num(tag, $"sample[{i}].y", a.Y, b.DoubleOr("y", 0));
                    Checks.Num(tag, $"sample[{i}].h", a.H, b.DoubleOr("h", 0));
                    Checks.Str(tag, $"sample[{i}].kind", a.Kind, b.OptString("kind"));
                }

                // ---- Project, global path ----
                var gp = g["probes"];
                for (int i = 0; i < (gp?.Count ?? 0); i++)
                {
                    var b = gp[i];
                    var a = route.Project(b.DoubleOr("px", 0), b.DoubleOr("py", 0));
                    Checks.Num(tag, $"probe[{i}].s", a.S, b.DoubleOr("s", 0));
                    Checks.Num(tag, $"probe[{i}].t", a.T, b.DoubleOr("t", 0));
                    Checks.Num(tag, $"probe[{i}].h", a.H, b.DoubleOr("h", 0));
                    Checks.Int(tag, $"probe[{i}].idx", a.Idx, b.IntOr("idx", -1));
                    Checks.Str(tag, $"probe[{i}].kind", a.Kind, b.OptString("kind"));
                }

                // ---- hinted Project must agree with the global one ----
                for (int i = 0; i < (gp?.Count ?? 0); i++)
                {
                    var b = gp[i];
                    double px = b.DoubleOr("px", 0), py = b.DoubleOr("py", 0);
                    var globalP = route.Project(px, py);
                    var hintedP = route.Project(px, py, globalP.Idx);
                    Checks.Int(tag, $"hinted[{i}].idx", hintedP.Idx, globalP.Idx);
                    Checks.Num(tag, $"hinted[{i}].s", hintedP.S, globalP.S);
                }
            }
        }
    }
}
