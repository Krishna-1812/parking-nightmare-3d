using System;
using System.Collections.Generic;
using System.IO;
using PN3D.Core;

namespace PN3D.Validate
{
    /// <summary>
    /// Diffs <see cref="TrafficSystem"/> and <see cref="PedSystem"/> against the shipping
    /// JavaScript (tools/gen_golden_actors.js), frame by frame.
    ///
    /// Both sides run the same seeded mulberry32 in place of Math.random, so this checks
    /// far more than "plausible behaviour": the two implementations must consume draws in
    /// exactly the same order. A single extra or missing draw — an eagerly evaluated
    /// short-circuit, a pooled car that should have skipped its kind roll — permanently
    /// desynchronises the stream and every later value diverges.
    /// </summary>
    internal static class ActorsSuite
    {
        const double Step = 1.0 / 120.0;

        struct Drive
        {
            public double X, Y, H, Vx, Vy, RecentShameT;
        }

        /// <summary>Pure functions of the step index, mirroring the generator's SCEN table.</summary>
        static Drive Program(string scenario, int i, CompiledRoute route, double rw)
        {
            double LaneAt(double s, double t, out double x, out double y, out double h)
            {
                var p = route.SampleAt(Math.Min(s, route.Length - 1));
                x = p.X - Math.Sin(p.H) * t;
                y = p.Y + Math.Cos(p.H) * t;
                h = p.H;
                return p.H;
            }

            double px, py, ph;
            switch (scenario)
            {
                case "m1-cruise":
                    LaneAt(20 + i * 0.09, rw * 0.5, out px, out py, out ph);
                    return new Drive { X = px, Y = py, H = ph,
                                       Vx = Math.Cos(ph) * 10.8, Vy = Math.Sin(ph) * 10.8 };

                case "m1-blocking":
                    LaneAt(120, rw * 0.5, out px, out py, out ph);
                    return new Drive { X = px, Y = py, H = ph, Vx = 0, Vy = 0 };

                case "m1-oncoming":
                    LaneAt(30 + i * 0.075, -rw * 0.45, out px, out py, out ph);
                    return new Drive { X = px, Y = py, H = ph,
                                       Vx = Math.Cos(ph) * 9, Vy = Math.Sin(ph) * 9 };

                case "m1-shameful-sidewalk":
                    LaneAt(30 + i * 0.06, rw + 1.6, out px, out py, out ph);
                    return new Drive { X = px, Y = py, H = ph,
                                       Vx = Math.Cos(ph) * 7.2, Vy = Math.Sin(ph) * 7.2,
                                       RecentShameT = 2 };

                case "m2-intersection":
                    LaneAt(20 + i * 0.085, rw * 0.5, out px, out py, out ph);
                    return new Drive { X = px, Y = py, H = ph,
                                       Vx = Math.Cos(ph) * 10.2, Vy = Math.Sin(ph) * 10.2 };

                case "m6-jingle":
                    LaneAt(25 + i * 0.05, rw * 0.5, out px, out py, out ph);
                    return new Drive { X = px, Y = py, H = ph,
                                       Vx = Math.Cos(ph) * 6, Vy = Math.Sin(ph) * 6 };

                default: throw new ArgumentException("unknown scenario: " + scenario);
            }
        }

        static string PedStateName(PedState s) => s switch
        {
            PedState.Walk => "walk",
            PedState.Film => "film",
            PedState.Cross => "cross",
            PedState.Dive => "dive",
            PedState.Soaked => "soaked",
            _ => "?",
        };

        public static void Run(string repo)
        {
            string goldenPath = Path.Combine(repo, "tools", "Validator", "golden_actors.json");
            if (!File.Exists(goldenPath))
            {
                Checks.Fail("actors", $"golden reference missing: {goldenPath} " +
                                      "(regenerate with: node tools/gen_golden_actors.js)");
                return;
            }

            var golden = JsonValue.Parse(File.ReadAllText(goldenPath));
            var missions = Mission.ParseAll(
                File.ReadAllText(Path.Combine(repo, "design-spec", "data", "missions.json")));
            var vehicles = VehicleDef.ParseAll(
                File.ReadAllText(Path.Combine(repo, "design-spec", "data", "vehicles.json")));

            Console.WriteLine($"actors  : {golden.Count} scenarios");

            for (int si = 0; si < golden.Count; si++)
            {
                var g = golden[si];
                string name = g.OptString("name");
                int missionId = g.IntOr("missionId", 1);
                uint seed = (uint)g.DoubleOr("seed", 1);
                int steps = g.IntOr("steps", 0);
                string tag = "actors:" + name;

                var authored = missions.Find(m => m.Id == missionId);
                if (authored == null) { Checks.Fail(tag, $"mission {missionId} missing"); continue; }
                if (!vehicles.TryGetValue(authored.Veh, out var veh))
                { Checks.Fail(tag, $"vehicle {authored.Veh} missing"); continue; }

                var run = MissionRun.Create(authored, veh, seed);
                double rw = RoadGeom.HalfWidth(run.Mission.Lanes);

                var events = new List<(string K, int I)>();
                run.OnHonk += _ => events.Add(("honk", CurrentStep));
                // shame popups distinguish the ped events: dive raises "SO CLOSE!!"
                run.Shame.OnPopup += (label, _) =>
                { if (label == "SO CLOSE!!") events.Add(("dive", CurrentStep)); };

                // filming has no popup, so hook the system directly
                int filmedCount = 0;
                var frames = g["frames"];
                int fi = 0;

                for (int i = 0; i < steps; i++)
                {
                    CurrentStep = i;
                    var d = Program(name, i, run.Route, rw);

                    run.Car.X = d.X; run.Car.Y = d.Y; run.Car.H = d.H;
                    run.Car.Vx = d.Vx; run.Car.Vy = d.Vy;
                    run.Shame.RecentShameT = d.RecentShameT;

                    run.Proj = run.Route.Project(d.X, d.Y, run.Proj.Idx);

                    run.Traffic.Update(Step, run);
                    run.Peds.Update(Step, run);

                    if (i % 20 != 0 && i != steps - 1) continue;
                    if (fi >= frames.Count) { Checks.Fail(tag, $"ran out of golden frames at {i}"); break; }

                    var f = frames[fi++];
                    Checks.Int(tag, $"f{fi}.i", i, f.IntOr("i", -1));

                    // ---- traffic lights ----
                    var gl = f["lights"];
                    Checks.Int(tag, $"f{fi}.lights.count", run.Traffic.Lights.Count, gl?.Count ?? 0);
                    for (int k = 0; k < Math.Min(run.Traffic.Lights.Count, gl?.Count ?? 0); k++)
                        Checks.Int(tag, $"f{fi}.light[{k}]", run.Traffic.Lights[k].State, gl[k].AsInt);

                    // ---- cars ----
                    var gc = f["cars"];
                    Checks.Int(tag, $"f{fi}.cars.count", run.Traffic.Cars.Count, gc?.Count ?? 0);
                    for (int k = 0; k < Math.Min(run.Traffic.Cars.Count, gc?.Count ?? 0); k++)
                    {
                        var a = run.Traffic.Cars[k];
                        var b = gc[k];
                        Checks.Int(tag, $"f{fi}.car[{k}].id", a.Id, b.IntOr("id", -1));
                        Checks.Str(tag, $"f{fi}.car[{k}].kind", a.Kind, b.OptString("kind"));
                        Checks.Num(tag, $"f{fi}.car[{k}].s", a.S, b.DoubleOr("s", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].t", a.T, b.DoubleOr("t", 0));
                        Checks.Int(tag, $"f{fi}.car[{k}].dir", a.Dir, b.IntOr("dir", 0));
                        Checks.Int(tag, $"f{fi}.car[{k}].lane", a.Lane, b.IntOr("lane", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].v", a.V, b.DoubleOr("v", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].cruise", a.Cruise, b.DoubleOr("cruise", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].len", a.Len, b.DoubleOr("len", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].x", a.X, b.DoubleOr("x", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].y", a.Y, b.DoubleOr("y", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].h", a.H, b.DoubleOr("h", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].honkCd", a.HonkCd, b.DoubleOr("honkCd", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].blockT", a.BlockT, b.DoubleOr("blockT", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].hitT", a.HitT, b.DoubleOr("hitT", 0));
                        Checks.Num(tag, $"f{fi}.car[{k}].panicT", a.PanicT, b.DoubleOr("panicT", 0));
                    }

                    // ---- crossers ----
                    var gx = f["crossers"];
                    var mine = new List<CrossCar>();
                    for (int k = 0; k < run.Route.Inters.Count; k++)
                        if (run.Traffic.Crossers.TryGetValue(k, out var lst)) mine.AddRange(lst);
                    Checks.Int(tag, $"f{fi}.crossers.count", mine.Count, gx?.Count ?? 0);
                    for (int k = 0; k < Math.Min(mine.Count, gx?.Count ?? 0); k++)
                    {
                        var a = mine[k]; var b = gx[k];
                        Checks.Int(tag, $"f{fi}.cross[{k}].inter", a.InterIdx, b.IntOr("inter", -1));
                        Checks.Int(tag, $"f{fi}.cross[{k}].id", a.Id, b.IntOr("id", -1));
                        Checks.Num(tag, $"f{fi}.cross[{k}].u", a.U, b.DoubleOr("u", 0));
                        Checks.Int(tag, $"f{fi}.cross[{k}].dir", a.Dir, b.IntOr("dir", 0));
                        Checks.Num(tag, $"f{fi}.cross[{k}].v", a.V, b.DoubleOr("v", 0));
                        Checks.Num(tag, $"f{fi}.cross[{k}].x", a.X, b.DoubleOr("x", 0));
                        Checks.Num(tag, $"f{fi}.cross[{k}].y", a.Y, b.DoubleOr("y", 0));
                        Checks.Num(tag, $"f{fi}.cross[{k}].h", a.H, b.DoubleOr("h", 0));
                    }

                    // ---- pedestrians ----
                    var gp = f["peds"];
                    Checks.Int(tag, $"f{fi}.peds.count", run.Peds.List.Count, gp?.Count ?? 0);
                    for (int k = 0; k < Math.Min(run.Peds.List.Count, gp?.Count ?? 0); k++)
                    {
                        var a = run.Peds.List[k]; var b = gp[k];
                        Checks.Num(tag, $"f{fi}.ped[{k}].s", a.S, b.DoubleOr("s", 0));
                        Checks.Num(tag, $"f{fi}.ped[{k}].t", a.T, b.DoubleOr("t", 0));
                        Checks.Int(tag, $"f{fi}.ped[{k}].side", a.Side, b.IntOr("side", 0));
                        Checks.Int(tag, $"f{fi}.ped[{k}].dir", a.Dir, b.IntOr("dir", 0));
                        Checks.Num(tag, $"f{fi}.ped[{k}].x", a.X, b.DoubleOr("x", 0));
                        Checks.Num(tag, $"f{fi}.ped[{k}].y", a.Y, b.DoubleOr("y", 0));
                        Checks.Num(tag, $"f{fi}.ped[{k}].face", a.Face, b.DoubleOr("face", 0));
                        Checks.Str(tag, $"f{fi}.ped[{k}].state", PedStateName(a.State), b.OptString("state"));
                        Checks.Num(tag, $"f{fi}.ped[{k}].speed", a.Speed, b.DoubleOr("speed", 0));
                        Checks.Num(tag, $"f{fi}.ped[{k}].phase", a.Phase, b.DoubleOr("phase", 0));
                        Checks.Num(tag, $"f{fi}.ped[{k}].stateT", a.StateT, b.DoubleOr("stateT", 0));
                        Checks.Bool(tag, $"f{fi}.ped[{k}].onRoad", a.OnRoad, b.BoolOr("onRoad", false));
                        Checks.Bool(tag, $"f{fi}.ped[{k}].filmed", a.Filmed, b.BoolOr("filmed", false));
                        Checks.Bool(tag, $"f{fi}.ped[{k}].attracted", a.Attracted, b.BoolOr("attracted", false));
                    }
                }

                // ---- event log: honks, dives, filming ----
                var ge = g["events"];
                var gHonk = new List<int>();
                var gDive = new List<int>();
                var gFilm = new List<int>();
                for (int k = 0; k < (ge?.Count ?? 0); k++)
                {
                    string kind = ge[k].OptString("k");
                    int at = ge[k].IntOr("i", -1);
                    if (kind == "honk") gHonk.Add(at);
                    else if (kind == "dive") gDive.Add(at);
                    else if (kind == "filmed") gFilm.Add(at);
                }

                var myHonk = events.FindAll(e => e.K == "honk").ConvertAll(e => e.I);
                var myDive = events.FindAll(e => e.K == "dive").ConvertAll(e => e.I);

                Checks.Int(tag, "events.honk.count", myHonk.Count, gHonk.Count);
                for (int k = 0; k < Math.Min(myHonk.Count, gHonk.Count); k++)
                    Checks.Int(tag, $"events.honk[{k}]", myHonk[k], gHonk[k]);

                Checks.Int(tag, "events.dive.count", myDive.Count, gDive.Count);
                for (int k = 0; k < Math.Min(myDive.Count, gDive.Count); k++)
                    Checks.Int(tag, $"events.dive[{k}]", myDive[k], gDive[k]);

                // filming is observable as the per-ped Filmed flag
                filmedCount = 0;
                foreach (var p in run.Peds.List) if (p.Filmed) filmedCount++;
                Checks.Int(tag, "events.filmed.count", filmedCount, gFilm.Count);
            }
        }

        static int CurrentStep;
    }
}
