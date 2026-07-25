using System;
using System.Collections.Generic;
using System.IO;
using PN3D.Core;

namespace PN3D.Validate
{
    /// <summary>
    /// Diffs <see cref="Scoring"/> (§9) and the <see cref="ShameSystem"/> /
    /// <see cref="StyleSystem"/> / <see cref="SurfaceRules"/> (§10) against the shipping
    /// JavaScript (tools/gen_golden_scoring.js).
    /// </summary>
    internal static class ScoringSuite
    {
        const double Step = 1.0 / 120.0;

        static GamePhase Phase(string s) => s switch
        {
            "drive" => GamePhase.Drive,
            "park" => GamePhase.Park,
            "settle" => GamePhase.Settle,
            "success" => GamePhase.Success,
            _ => GamePhase.Fail,
        };

        public static void Run(string repo)
        {
            string goldenPath = Path.Combine(repo, "tools", "Validator", "golden_scoring.json");
            if (!File.Exists(goldenPath))
            {
                Checks.Fail("scoring", $"golden reference missing: {goldenPath} " +
                                       "(regenerate with: node tools/gen_golden_scoring.js)");
                return;
            }

            var golden = JsonValue.Parse(File.ReadAllText(goldenPath));
            var cases = golden["scoreCases"];
            var shameScripts = golden["shameScripts"];
            var surfaceRuns = golden["surfaceRuns"];

            Console.WriteLine($"scoring : {cases.Count} score cases, {shameScripts.Count} shame scripts, " +
                              $"{surfaceRuns.Count} surface runs");

            // ---- §9 scoring ----
            for (int i = 0; i < cases.Count; i++)
            {
                var c = cases[i];
                var o = c["out"];
                string tag = $"score[{i}]";

                var gap = c["curbGap"];
                bool hasGap = gap != null && !gap.IsNull;

                var r = Scoring.Compute(
                    c.DoubleOr("par", 0), c.DoubleOr("timer", 0), c.DoubleOr("style", 0),
                    c.DoubleOr("angDeg", 0), hasGap ? gap.AsDouble : 0.0, hasGap,
                    c.DoubleOr("damage", 0), c.DoubleOr("shame", 0), c.IntOr("collisions", 0),
                    c.IntOr("s2", 0), c.IntOr("s3", 0));

                Checks.Int(tag, "timeScore", r.TimeScore, o.IntOr("timeScore", 0));
                Checks.Int(tag, "styleScore", r.StyleScore, o.IntOr("styleScore", 0));
                Checks.Int(tag, "parkScore", r.ParkScore, o.IntOr("parkScore", 0));
                Checks.Int(tag, "dmgScore", r.DmgScore, o.IntOr("dmgScore", 0));
                Checks.Int(tag, "shameScore", r.ShameScore, o.IntOr("shameScore", 0));
                Checks.Int(tag, "cleanBonus", r.CleanBonus, o.IntOr("cleanBonus", 0));
                Checks.Int(tag, "total", r.Total, o.IntOr("total", 0));
                Checks.Int(tag, "stars", r.Stars, o.IntOr("stars", 0));
                Checks.Bool(tag, "sRank", r.SRank, o.BoolOr("sRank", false));
                Checks.Bool(tag, "perfect", r.Perfect, o.BoolOr("perfect", false));
                Checks.Int(tag, "coins", r.Coins, o.IntOr("coins", 0));
                Checks.Str(tag, "timeStr", Scoring.FmtTime(c.DoubleOr("timer", 0)), o.OptString("timeStr"));
                Checks.Str(tag, "parStr", Scoring.FmtTime(c.DoubleOr("par", 0)), o.OptString("parStr"));
            }

            // ---- shame / style scripts ----
            for (int si = 0; si < shameScripts.Count; si++)
            {
                var g = shameScripts[si];
                string tag = "shame:" + g.OptString("name");
                var frames = g["frames"];

                var shame = new ShameSystem();
                var style = new StyleSystem();
                var phase = GamePhase.Drive;

                // hitting 100 fails the run, and a failed run stops decaying — the
                // reference does this by having failShame() set state = 'fail'.
                // MissionRun wires it the same way; mirror it here.
                shame.OnFail += () => phase = GamePhase.Fail;

                for (int i = 0; i < frames.Count; i++)
                {
                    var f = frames[i];
                    var op = f["op"];

                    int ticks = op.IntOr("t", 0);
                    for (int k = 0; k < ticks; k++)
                    {
                        style.Tick(Step);
                        shame.Tick(Step, phase);
                    }

                    var sv = op["shame"];
                    if (sv != null && !sv.IsNull) shame.Add(sv.AsDouble, phase);

                    var st = op["style"];
                    if (st != null && !st.IsNull) style.Add(st.AsDouble, phase, "X");

                    var ps = op["state"];
                    if (ps != null && !ps.IsNull) phase = Phase(ps.AsString);

                    Checks.Num(tag, $"f{i}.shame", shame.Shame, f.DoubleOr("shame", 0));
                    Checks.Num(tag, $"f{i}.style", style.Style, f.DoubleOr("style", 0));
                    Checks.Int(tag, $"f{i}.combo", style.Combo, f.IntOr("combo", 0));
                    Checks.Num(tag, $"f{i}.comboT", style.ComboT, f.DoubleOr("comboT", 0));
                    Checks.Num(tag, $"f{i}.calmT", shame.CalmT, f.DoubleOr("calmT", 0));
                    Checks.Num(tag, $"f{i}.recentShameT", shame.RecentShameT, f.DoubleOr("recentShameT", 0));

                    var th = f["thresholds"];
                    var hit = new List<int>();
                    foreach (int pct in new[] { 25, 50, 75 }) if (shame.ThresholdHit(pct)) hit.Add(pct);
                    Checks.Int(tag, $"f{i}.thresholds.count", hit.Count, th?.Count ?? 0);
                    for (int k = 0; k < Math.Min(hit.Count, th?.Count ?? 0); k++)
                        Checks.Int(tag, $"f{i}.thresholds[{k}]", hit[k], th[k].AsInt);
                }
            }

            // ---- surfaceLogic runs ----
            var hatch = VehicleDef.ParseAll(
                File.ReadAllText(Path.Combine(repo, "design-spec", "data", "vehicles.json")))["hatch"];

            // the generator uses a bare stub route with no zones/inters and RW 5.8
            var bareRoute = new CompiledRoute
            {
                Pts = new RoutePoint[0], Step = 2.0, Length = 0,
                Inters = new List<Intersection>(),
                Zones = new List<RouteZone>(),
                Curves = new List<RouteCurve>(),
            };
            const double RW = 5.8;

            for (int si = 0; si < surfaceRuns.Count; si++)
            {
                var g = surfaceRuns[si];
                string tag = "surface:" + g.OptString("name");
                var frames = g["frames"];

                var shame = new ShameSystem();
                var style = new StyleSystem();
                var rules = new SurfaceRules();
                var car = new VehicleSim(hatch);
                var flags = new SurfaceFlags();

                for (int i = 0; i < frames.Count; i++)
                {
                    var f = frames[i];
                    var inp = f["in"];

                    car.X = 0; car.Y = 0;
                    car.H = inp.DoubleOr("h", 0);
                    car.Vx = inp.DoubleOr("spd", 0);   // SpeedAbs == |Vx|
                    car.Vy = 0;
                    var phase = Phase(inp.OptString("state"));

                    if (rules.CurbCd > 0) rules.CurbCd -= Step;
                    shame.CalmT += Step;   // generator advances calmT directly, no decay path

                    var proj = new Projection
                    {
                        S = inp.DoubleOr("s", 0), T = inp.DoubleOr("t", 0),
                        H = 0, Idx = 0, Kind = "road",
                    };

                    rules.Step(Step, car, proj, bareRoute, RW, phase, flags, shame, style);

                    Checks.Num(tag, $"f{i}.shame", shame.Shame, f.DoubleOr("shame", 0));
                    Checks.Num(tag, $"f{i}.style", style.Style, f.DoubleOr("style", 0));
                    Checks.Num(tag, $"f{i}.grip", car.SurfaceGrip, f.DoubleOr("grip", 0));
                    Checks.Num(tag, $"f{i}.damage", car.Damage, f.DoubleOr("damage", 0));
                    Checks.Num(tag, $"f{i}.bounceV", car.BounceV, f.DoubleOr("bounceV", 0));
                    Checks.Num(tag, $"f{i}.curbCd", rules.CurbCd, f.DoubleOr("curbCd", 0));
                    Checks.Num(tag, $"f{i}.wrongWayT", rules.WrongWayT, f.DoubleOr("wrongWayT", 0));
                    Checks.Num(tag, $"f{i}.smoothMark", rules.SmoothMark, f.DoubleOr("smoothMark", 0));
                }
            }
        }
    }
}
