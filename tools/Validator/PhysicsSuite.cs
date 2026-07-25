using System;
using System.IO;
using PN3D.Core;

namespace PN3D.Validate
{
    /// <summary>
    /// Drives <see cref="VehicleSim"/> through the same deterministic input programs as
    /// tools/gen_golden_physics.js and diffs the resulting state traces against the
    /// shipping JavaScript, step for step.
    ///
    /// The input programs here must stay in lockstep with the SCENARIOS table in the
    /// generator — the golden file records the step count, surface grip and slip
    /// injection so a mismatch in those shows up as a failure rather than silently
    /// comparing different runs.
    /// </summary>
    internal static class PhysicsSuite
    {
        const double Step = 1.0 / 120.0;

        static VehicleInput Program(string scenario, int i) => scenario switch
        {
            "launch-straight" => new VehicleInput { Steer = 0, Throttle = 1 },

            "launch-steady-steer" => new VehicleInput { Steer = 0.6, Throttle = 1 },

            "full-lock-both-ways" => new VehicleInput
            { Steer = i < 480 ? 1 : (i < 960 ? -1 : 0), Throttle = 1 },

            "brake-through-zero" => new VehicleInput
            { Steer = 0, Throttle = i < 500 ? 1 : (i < 1300 ? -1 : 1) },

            "coast-to-stop" => new VehicleInput { Steer = 0, Throttle = i < 400 ? 1 : 0 },

            "handbrake-turn" => new VehicleInput
            {
                Steer = i < 300 ? 0 : 1,
                Throttle = i < 300 ? 1 : 0,
                Handbrake = i >= 300 && i < 700,
            },

            "slalom-keyboard" => new VehicleInput
            { Steer = (int)Math.Floor(i / 90.0) % 2 == 0 ? 1 : -1, Throttle = 1 },

            "slalom-analog" => new VehicleInput
            { Steer = Math.Sin(i * 0.01) * 0.8, Throttle = 1, SteerAnalog = true },

            "low-grip-surface" => new VehicleInput { Steer = i < 400 ? 0 : 0.8, Throttle = 1 },

            "slip-recovery" => new VehicleInput { Steer = 0.7, Throttle = 1 },

            "reverse-out" => new VehicleInput { Steer = i < 600 ? 0 : -0.5, Throttle = -1 },

            "creep-kill" => new VehicleInput { Steer = 0, Throttle = i < 60 ? 0.35 : 0 },

            _ => throw new ArgumentException("unknown scenario: " + scenario),
        };

        public static void Run(string repo)
        {
            string goldenPath = Path.Combine(repo, "tools", "Validator", "golden_physics.json");
            string vehiclesPath = Path.Combine(repo, "design-spec", "data", "vehicles.json");

            if (!File.Exists(goldenPath))
            {
                Checks.Fail("physics", $"golden reference missing: {goldenPath} " +
                                       "(regenerate with: node tools/gen_golden_physics.js)");
                return;
            }

            var golden = JsonValue.Parse(File.ReadAllText(goldenPath));
            var vehicles = VehicleDef.ParseAll(File.ReadAllText(vehiclesPath));

            Console.WriteLine($"physics : {golden.Count} scenarios, {vehicles.Count} vehicle defs loaded");

            for (int si = 0; si < golden.Count; si++)
            {
                var g = golden[si];
                string name = g.OptString("name");
                string vehKey = g.OptString("veh");
                int steps = g.IntOr("steps", 0);
                double surfaceGrip = g.DoubleOr("surfaceGrip", 1.0);
                int slipAt = g.IntOr("slipAt", -1);
                string tag = "phys:" + name;

                if (!vehicles.TryGetValue(vehKey, out var def))
                {
                    Checks.Fail(tag, $"unknown vehicle '{vehKey}'");
                    continue;
                }

                var sim = new VehicleSim(def) { SurfaceGrip = surfaceGrip };

                var frames = g["frames"];
                int fi = 0;

                for (int i = 0; i < steps; i++)
                {
                    if (slipAt >= 0 && i == slipAt) sim.SlipTimer = 0.45;
                    sim.Step(Step, Program(name, i));

                    if (i % 20 != 0 && i != steps - 1) continue;

                    if (fi >= frames.Count)
                    {
                        Checks.Fail(tag, $"ran out of golden frames at step {i}");
                        break;
                    }
                    var f = frames[fi++];
                    Checks.Int(tag, $"f{fi}.i", i, f.IntOr("i", -1));
                    Checks.Num(tag, $"f{fi}.x", sim.X, f.DoubleOr("x", 0));
                    Checks.Num(tag, $"f{fi}.y", sim.Y, f.DoubleOr("y", 0));
                    Checks.Num(tag, $"f{fi}.h", sim.H, f.DoubleOr("h", 0));
                    Checks.Num(tag, $"f{fi}.vx", sim.Vx, f.DoubleOr("vx", 0));
                    Checks.Num(tag, $"f{fi}.vy", sim.Vy, f.DoubleOr("vy", 0));
                    Checks.Num(tag, $"f{fi}.steer", sim.Steer, f.DoubleOr("steer", 0));
                    Checks.Num(tag, $"f{fi}.steerCmd", sim.SteerCmd, f.DoubleOr("steerCmd", 0));
                    Checks.Num(tag, $"f{fi}.slideAmt", sim.SlideAmt, f.DoubleOr("slideAmt", 0));
                    Checks.Num(tag, $"f{fi}.accF", sim.AccF, f.DoubleOr("accF", 0));
                    Checks.Num(tag, $"f{fi}.pitch", sim.Pitch, f.DoubleOr("pitch", 0));
                    Checks.Num(tag, $"f{fi}.roll", sim.Roll, f.DoubleOr("roll", 0));
                    Checks.Num(tag, $"f{fi}.slipTimer", sim.SlipTimer, f.DoubleOr("slipTimer", 0));
                    Checks.Bool(tag, $"f{fi}.braking", sim.Braking, f.BoolOr("braking", false));
                    Checks.Bool(tag, $"f{fi}.reversing", sim.Reversing, f.BoolOr("reversing", false));
                }

                if (fi != frames.Count)
                    Checks.Fail(tag, $"consumed {fi} frames but golden has {frames.Count}");
            }
        }
    }
}
