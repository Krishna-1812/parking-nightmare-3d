using System;
using System.IO;
using PN3D.Core;

namespace PN3D.Validate
{
    /// <summary>
    /// Diffs <see cref="ParkingSpot"/> geometry and the <see cref="ParkChecker"/>
    /// tolerance check and settle state machine against the shipping JavaScript
    /// (tools/gen_golden_parking.js).
    /// </summary>
    internal static class ParkingSuite
    {
        const double Step = 1.0 / 120.0;

        static string PhaseName(GamePhase p) => p switch
        {
            GamePhase.Drive => "drive",
            GamePhase.Park => "park",
            GamePhase.Settle => "settle",
            GamePhase.Success => "success",
            _ => "?",
        };

        public static void Run(string repo)
        {
            string goldenPath = Path.Combine(repo, "tools", "Validator", "golden_parking.json");
            if (!File.Exists(goldenPath))
            {
                Checks.Fail("parking", $"golden reference missing: {goldenPath} " +
                                       "(regenerate with: node tools/gen_golden_parking.js)");
                return;
            }

            var golden = JsonValue.Parse(File.ReadAllText(goldenPath));
            var missions = Mission.ParseAll(
                File.ReadAllText(Path.Combine(repo, "design-spec", "data", "missions.json")));
            var vehicles = VehicleDef.ParseAll(
                File.ReadAllText(Path.Combine(repo, "design-spec", "data", "vehicles.json")));

            var gSpots = golden["spots"];
            var gGrids = golden["grids"];
            var gSettles = golden["settles"];

            Console.WriteLine($"parking : {gSpots.Count} spots, {gGrids.Count} pose grids, " +
                              $"{gSettles.Count} settle scripts");

            // ---- spot geometry, all 24 missions ----
            for (int i = 0; i < gSpots.Count; i++)
            {
                var b = gSpots[i];
                int id = b.IntOr("id", -1);
                string tag = $"spot m{id:00}";

                var (spot, _) = BuildSpot(missions, vehicles, id);
                if (spot == null) { Checks.Fail(tag, "could not build spot"); continue; }

                Checks.Str(tag, "type", spot.Type == ParkType.Bay ? "bay" : "parallel", b.OptString("type"));
                Checks.Num(tag, "x", spot.X, b.DoubleOr("x", 0));
                Checks.Num(tag, "y", spot.Y, b.DoubleOr("y", 0));
                Checks.Num(tag, "h", spot.H, b.DoubleOr("h", 0));
                Checks.Num(tag, "hl", spot.Hl, b.DoubleOr("hl", 0));
                Checks.Num(tag, "hw", spot.Hw, b.DoubleOr("hw", 0));
                Checks.Num(tag, "t", spot.T, b.DoubleOr("t", 0));
                Checks.Num(tag, "s", spot.S, b.DoubleOr("s", 0));
                Checks.Num(tag, "zoneS", spot.ZoneS, b.DoubleOr("parkZoneS", 0));
                Checks.Num(tag, "RW", RoadGeom.HalfWidth(b.IntOr("lanes", 1)), b.DoubleOr("RW", 0));
            }

            // ---- measurement grid ----
            for (int gi = 0; gi < gGrids.Count; gi++)
            {
                var g = gGrids[gi];
                int id = g.IntOr("id", -1);
                var (spot, ctx) = BuildSpot(missions, vehicles, id);
                if (spot == null) { Checks.Fail($"grid m{id}", "could not build spot"); continue; }

                var samples = g["samples"];
                for (int i = 0; i < samples.Count; i++)
                {
                    var b = samples[i];
                    string tag = $"grid m{id:00}[{i}]";

                    var car = new VehicleSim(ctx.Veh)
                    {
                        X = b.DoubleOr("px", 0),
                        Y = b.DoubleOr("py", 0),
                        H = b.DoubleOr("ph", 0),
                    };

                    var checker = new ParkChecker
                    {
                        InZone = true,
                        Phase = GamePhase.Park,
                        IsUfo = ctx.Veh.Drive == "ufo",
                    };

                    var proj = ctx.Route.Project(car.X, car.Y);
                    Checks.Int(tag, "projIdx", proj.Idx, b.IntOr("projIdx", -1));

                    checker.Step(Step, car, spot, ctx.Route, proj);
                    var m = checker.Measure;

                    Checks.Bool(tag, "inside", m.Inside, b.BoolOr("inside", false));
                    Checks.Num(tag, "dAng", m.DAng, b.DoubleOr("dAng", 0));
                    Checks.Bool(tag, "angOk", m.AngOk, b.BoolOr("angOk", false));
                    Checks.Bool(tag, "curbOk", m.CurbOk, b.BoolOr("curbOk", false));

                    var gGap = b["curbGap"];
                    bool goldenHasGap = gGap != null && !gGap.IsNull;
                    Checks.Bool(tag, "hasCurbGap", m.HasCurbGap, goldenHasGap);
                    if (m.HasCurbGap && goldenHasGap)
                        Checks.Num(tag, "curbGap", m.CurbGap, gGap.AsDouble);
                }
            }

            // ---- settle state machine ----
            for (int si = 0; si < gSettles.Count; si++)
            {
                var g = gSettles[si];
                string name = g.OptString("name");
                int id = g.IntOr("missionId", -1);
                string tag = "settle:" + name;

                var (spot, ctx) = BuildSpot(missions, vehicles, id);
                if (spot == null) { Checks.Fail(tag, "could not build spot"); continue; }

                var car = new VehicleSim(ctx.Veh);
                var checker = new ParkChecker { IsUfo = ctx.Veh.Drive == "ufo" };

                double ca = Math.Cos(spot.H), sa = Math.Sin(spot.H);
                var frames = g["frames"];

                for (int i = 0; i < frames.Count; i++)
                {
                    var b = frames[i];
                    // replay the recorded pose program rather than re-deriving it, so a
                    // divergence here is a checker bug and never a script mismatch
                    double dl = b.DoubleOr("dl", 0), dw = b.DoubleOr("dw", 0);
                    double dh = b.DoubleOr("dh", 0), speed = b.DoubleOr("speed", 0);

                    car.X = spot.X + ca * dl - sa * dw;
                    car.Y = spot.Y + sa * dl + ca * dw;
                    car.H = spot.H + dh;
                    car.Vx = speed;   // SpeedAbs == |Vx| when Vy is 0
                    car.Vy = 0.0;

                    var proj = ctx.Route.Project(car.X, car.Y);
                    checker.Step(Step, car, spot, ctx.Route, proj);

                    Checks.Int(tag, $"f{i}.i", i, b.IntOr("i", -1));
                    Checks.Str(tag, $"f{i}.state", PhaseName(checker.Phase), b.OptString("state"));
                    Checks.Num(tag, $"f{i}.parkT", checker.ParkT, b.DoubleOr("parkT", 0));
                    Checks.Bool(tag, $"f{i}.inZone", checker.InZone, b.BoolOr("inZone", false));
                }
            }
        }

        sealed class SpotCtx
        {
            public CompiledRoute Route;
            public VehicleDef Veh;
        }

        static (ParkingSpot, SpotCtx) BuildSpot(
            System.Collections.Generic.List<Mission> missions,
            System.Collections.Generic.Dictionary<string, VehicleDef> vehicles,
            int id)
        {
            var m = missions.Find(x => x.Id == id);
            if (m == null) return (null, null);
            var mission = m.Clone();                        // enrichment mutates
            var route = RouteCompiler.CompileMission(mission);
            if (!vehicles.TryGetValue(mission.Veh, out var veh)) return (null, null);
            var spot = ParkingSpot.Build(route, ParkingSpot.ParseType(mission.Park),
                                         veh, mission.Margin, mission.Lanes);
            return (spot, new SpotCtx { Route = route, Veh = veh });
        }
    }
}
