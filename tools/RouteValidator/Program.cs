using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PN3D.Core;

namespace PN3D.Validate
{
    /// <summary>
    /// Diffs the C# route compiler against a golden reference produced by running the
    /// shipping JavaScript (tools/gen_golden_routes.js). Exits non-zero on any
    /// mismatch so it can gate a commit.
    /// </summary>
    internal static class Program
    {
        // Trig differs by an ULP or so between V8 and .NET, and the compiler
        // integrates ~550 steps, so allow a little accumulated drift. Observed drift
        // is far below this; anything approaching it means a real porting error.
        const double Eps = 1e-9;

        static int _checks;
        static double _maxRelDrift;
        static string _maxRelWhere = "(none)";
        static readonly List<string> Failures = new List<string>();

        static int Main(string[] args)
        {
            string repo = FindRepoRoot();
            string goldenPath = args.Length > 0
                ? args[0]
                : Path.Combine(repo, "tools", "RouteValidator", "golden_routes.json");
            string missionsPath = Path.Combine(repo, "design-spec", "data", "missions.json");

            if (!File.Exists(goldenPath))
            {
                Console.Error.WriteLine($"golden reference not found: {goldenPath}");
                Console.Error.WriteLine("regenerate it with:  node tools/gen_golden_routes.js");
                return 2;
            }

            var golden = JsonValue.Parse(File.ReadAllText(goldenPath));
            var missions = Mission.ParseAll(File.ReadAllText(missionsPath));

            Console.WriteLine($"golden   : {goldenPath}");
            Console.WriteLine($"missions : {missionsPath}  ({missions.Count} missions)");
            Console.WriteLine();

            if (golden.Count != missions.Count)
                Fail("global", $"mission count {missions.Count} != golden {golden.Count}");

            for (int gi = 0; gi < golden.Count; gi++)
            {
                var g = golden[gi];
                int id = g.IntOr("id", -1);
                var mission = missions.Find(m => m.Id == id);
                if (mission == null) { Fail($"m{id}", "mission id missing from missions.json"); continue; }

                string tag = $"m{id:00} {mission.Name}";

                // ---- enrichment ----
                double rawPar = mission.Par;
                int rawSegs = mission.Segs.Count;
                EqInt(tag, "rawSegs", rawSegs, g.IntOr("rawSegs", -1));
                EqNum(tag, "rawPar", rawPar, g.DoubleOr("rawPar", -1));

                var route = RouteCompiler.CompileMission(mission);

                EqInt(tag, "enrichedSegs", mission.Segs.Count, g.IntOr("enrichedSegs", -1));
                EqNum(tag, "enrichedPar", mission.Par, g.DoubleOr("enrichedPar", -1));
                EqBool(tag, "enriched", mission.Enriched, g.BoolOr("enriched", false));

                // enriched segment list, field by field
                var gsegs = g["segs"];
                if (gsegs != null && gsegs.Count == mission.Segs.Count)
                {
                    for (int i = 0; i < gsegs.Count; i++)
                    {
                        var a = mission.Segs[i];
                        var b = gsegs[i];
                        EqStr(tag, $"seg[{i}].t", a.T, b.OptString("t"));
                        EqNumOpt(tag, $"seg[{i}].len", a.Len, b.OptDouble("len"));
                        EqNumOpt(tag, $"seg[{i}].r", a.R, b.OptDouble("r"));
                        EqNumOpt(tag, $"seg[{i}].a", a.A, b.OptDouble("a"));
                    }
                }
                else
                {
                    Fail(tag, $"enriched seg count {mission.Segs.Count} != golden {gsegs?.Count}");
                }

                // ---- compiled geometry ----
                EqNum(tag, "length", route.Length, g.DoubleOr("length", -1));
                EqInt(tag, "ptsCount", route.Pts.Length, g.IntOr("ptsCount", -1));

                // ---- intersections / zones / curves ----
                var gi2 = g["inters"];
                EqInt(tag, "inters.count", route.Inters.Count, gi2?.Count ?? -1);
                for (int i = 0; i < Math.Min(route.Inters.Count, gi2?.Count ?? 0); i++)
                {
                    var a = route.Inters[i]; var b = gi2[i];
                    EqNum(tag, $"inter[{i}].s0", a.S0, b.DoubleOr("s0", -1));
                    EqNum(tag, $"inter[{i}].s1", a.S1, b.DoubleOr("s1", -1));
                    EqNum(tag, $"inter[{i}].cx", a.Cx, b.DoubleOr("cx", -1));
                    EqNum(tag, $"inter[{i}].cy", a.Cy, b.DoubleOr("cy", -1));
                    EqNum(tag, $"inter[{i}].h", a.H, b.DoubleOr("h", -1));
                    EqBool(tag, $"inter[{i}].lights", a.Lights, b.BoolOr("lights", false));
                }

                EqInt(tag, "zones.count", route.Zones.Count, g["zones"]?.Count ?? -1);

                var gc = g["curves"];
                EqInt(tag, "curves.count", route.Curves.Count, gc?.Count ?? -1);
                for (int i = 0; i < Math.Min(route.Curves.Count, gc?.Count ?? 0); i++)
                {
                    var a = route.Curves[i]; var b = gc[i];
                    EqNum(tag, $"curve[{i}].s", a.S, b.DoubleOr("s", -1));
                    EqStr(tag, $"curve[{i}].dir", a.Dir, b.OptString("dir"));
                    EqNum(tag, $"curve[{i}].end", a.End, b.DoubleOr("end", -1));
                }

                // ---- SampleAt ----
                var gs = g["samples"];
                for (int i = 0; i < (gs?.Count ?? 0); i++)
                {
                    var b = gs[i];
                    var a = route.SampleAt(b.DoubleOr("s", 0));
                    EqNum(tag, $"sample[{i}].x", a.X, b.DoubleOr("x", 0));
                    EqNum(tag, $"sample[{i}].y", a.Y, b.DoubleOr("y", 0));
                    EqNum(tag, $"sample[{i}].h", a.H, b.DoubleOr("h", 0));
                    EqStr(tag, $"sample[{i}].kind", a.Kind, b.OptString("kind"));
                }

                // ---- Project ----
                var gp = g["probes"];
                for (int i = 0; i < (gp?.Count ?? 0); i++)
                {
                    var b = gp[i];
                    var a = route.Project(b.DoubleOr("px", 0), b.DoubleOr("py", 0));
                    EqNum(tag, $"probe[{i}].s", a.S, b.DoubleOr("s", 0));
                    EqNum(tag, $"probe[{i}].t", a.T, b.DoubleOr("t", 0));
                    EqNum(tag, $"probe[{i}].h", a.H, b.DoubleOr("h", 0));
                    EqInt(tag, $"probe[{i}].idx", a.Idx, b.IntOr("idx", -1));
                    EqStr(tag, $"probe[{i}].kind", a.Kind, b.OptString("kind"));
                }

                // ---- hinted Project must agree with the global one ----
                for (int i = 0; i < (gp?.Count ?? 0); i++)
                {
                    var b = gp[i];
                    double px = b.DoubleOr("px", 0), py = b.DoubleOr("py", 0);
                    var globalP = route.Project(px, py);
                    var hintedP = route.Project(px, py, globalP.Idx);
                    EqInt(tag, $"hinted[{i}].idx", hintedP.Idx, globalP.Idx);
                    EqNum(tag, $"hinted[{i}].s", hintedP.S, globalP.S);
                }
            }

            Console.WriteLine($"{_checks} checks across {missions.Count} missions");
            Console.WriteLine($"max relative drift vs JS: {_maxRelDrift:E3}  (tolerance {Eps:E0})  at {_maxRelWhere}");
            if (Failures.Count == 0)
            {
                Console.WriteLine("PASS — C# route compiler matches the JavaScript reference");
                return 0;
            }

            Console.WriteLine($"FAIL — {Failures.Count} mismatch(es):");
            for (int i = 0; i < Math.Min(Failures.Count, 40); i++) Console.WriteLine("  " + Failures[i]);
            if (Failures.Count > 40) Console.WriteLine($"  ... and {Failures.Count - 40} more");
            return 1;
        }

        static string FindRepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "design-spec"))) d = d.Parent;
            if (d == null) throw new DirectoryNotFoundException("could not locate repo root (no design-spec/ above cwd)");
            return d.FullName;
        }

        static void Fail(string tag, string msg) => Failures.Add($"{tag}: {msg}");

        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        static void EqNum(string tag, string field, double actual, double expected)
        {
            _checks++;
            double diff = Math.Abs(actual - expected);
            double scale = Math.Max(1.0, Math.Abs(expected));
            double rel = diff / scale;
            if (rel > _maxRelDrift) { _maxRelDrift = rel; _maxRelWhere = $"{tag} {field}"; }
            if (diff > Eps * scale) Fail(tag, $"{field}: got {F(actual)} expected {F(expected)} (diff {F(diff)})");
        }

        static void EqNumOpt(string tag, string field, double? actual, double? expected)
        {
            _checks++;
            if (!actual.HasValue && !expected.HasValue) return;
            if (actual.HasValue != expected.HasValue)
            {
                Fail(tag, $"{field}: got {(actual.HasValue ? F(actual.Value) : "absent")} " +
                          $"expected {(expected.HasValue ? F(expected.Value) : "absent")}");
                return;
            }
            EqNum(tag, field, actual.Value, expected.Value);
        }

        static void EqInt(string tag, string field, int actual, int expected)
        {
            _checks++;
            if (actual != expected) Fail(tag, $"{field}: got {actual} expected {expected}");
        }

        static void EqBool(string tag, string field, bool actual, bool expected)
        {
            _checks++;
            if (actual != expected) Fail(tag, $"{field}: got {actual} expected {expected}");
        }

        static void EqStr(string tag, string field, string actual, string expected)
        {
            _checks++;
            if (actual != expected) Fail(tag, $"{field}: got '{actual}' expected '{expected}'");
        }
    }
}
