using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using PN3D.Core;
using PN3D.Game;

namespace PN3D.EditorTools
{
    /// <summary>
    /// Batch-mode entry points for the vertical slice, so both the simulation and the
    /// rendering can be exercised from the command line without a human driving the
    /// editor. Invoked with -executeMethod.
    /// </summary>
    public static class SliceTools
    {
        static string ArgValue(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        /// <summary>
        /// Plays mission 1 headlessly with a scripted driver and reports the outcome.
        /// This is the end-to-end proof: route -> physics -> parking -> scoring, through
        /// the same MissionRun the game uses, with no rendering involved.
        /// </summary>
        [MenuItem("PN3D/Smoke Test Mission 1")]
        public static void SmokeTest()
        {
            const double dt = 1.0 / 120.0;
            var run = Bootstrap.CreateRun(1);
            if (run == null) { Fail("could not create run"); return; }

            Log($"mission   : {run.Mission.Id} {run.Mission.Name} ({run.Veh.Name})");
            Log($"route     : {run.Route.Length:0.0} m, par {run.Mission.Par}s, " +
                $"spot at s={run.Spot.S:0.0}, zone arms at s={run.Spot.ZoneS:0.0}");

            // A simple autopilot: hold the lane at the parking-strip offset, steer by the
            // route projection, and brake down as the spot approaches. Good enough to
            // reach the spot; it is a test harness, not gameplay.
            double targetT = run.Spot.T;
            int steps = 0, maxSteps = (int)(200.0 / dt);
            double lastReport = 0;

            while (run.Phase != GamePhase.Success && run.Phase != GamePhase.Fail && steps < maxSteps)
            {
                double toGo = run.Spot.S - run.Proj.S;
                double lateralErr = targetT - run.Proj.T;
                double headErr = MathX.AngNorm(run.Proj.H - run.Car.H);

                // steer toward the lane centre, blended with holding the route heading
                double steer = MathX.Clamp(headErr * 1.7 + lateralErr * 0.30, -1, 1);

                // Ease proportionally to zero *at the spot centre*. Stopping short is not
                // good enough: the check needs all four corners inside the box, and the
                // hatch's 1.95 m rear overhang leaves the 3.25 m half-length if the
                // centre halts even ~1.5 m early.
                double speed = run.Car.Speed;
                double wantSpeed = MathX.Clamp(toGo * 0.55, 0.0, 12.0);
                if (toGo < 0.2) wantSpeed = 0.0;

                double throttle;
                if (speed < wantSpeed - 0.3) throttle = 1.0;
                // never brake below ~0.6 m/s: negative throttle under 0.45 is reverse, not
                // braking (§3.1), so it would back out of the spot instead of settling
                else if (speed > wantSpeed + 0.3 && speed > 0.6) throttle = -1.0;
                else throttle = 0.0;

                run.Step(dt, new VehicleInput { Steer = steer, Throttle = throttle });
                steps++;

                if (run.Timer - lastReport >= 10.0)
                {
                    lastReport = run.Timer;
                    Log($"  t={run.Timer,6:0.0}s  s={run.Proj.S,7:0.0}  t_off={run.Proj.T,6:0.00}  " +
                        $"v={run.Car.Speed,5:0.0}  shame={run.Shame.Shame,5:0.0}  " +
                        $"style={run.Style.Style,4:0}  phase={run.Phase}");
                }
            }

            Log($"finished  : phase={run.Phase} after {steps} steps ({run.Timer:0.0}s sim time)");
            Log($"            shame={run.Shame.Shame:0.00}  style={run.Style.Style:0}  " +
                $"damage={run.Car.Damage:0.0}  distDriven={run.DistDriven:0}m");

            if (run.Phase == GamePhase.Success)
            {
                var r = run.Result;
                Log("score:");
                foreach (var l in r.Lines) Log($"  {l.Label,-34} {l.Value,6}");
                Log($"  {"TOTAL",-34} {r.Total,6}");
                Log($"  stars={r.Stars} sRank={r.SRank} perfect={r.Perfect} coins={r.Coins} " +
                    $"angle={r.AngDeg:0.00}deg curbGap={r.CurbGap * 100:0.0}cm");
                Log("SMOKE TEST PASS");
            }
            else
            {
                Fail($"did not reach Success (phase={run.Phase}, " +
                     $"distance left={run.Spot.S - run.Proj.S:0.0} m)");
            }
        }

        /// <summary>
        /// Renders the greybox world to a PNG. Needs a graphics device, so run batchmode
        /// WITHOUT -nographics.
        /// </summary>
        [MenuItem("PN3D/Capture Screenshots")]
        public static void Capture()
        {
            string outDir = ArgValue("-pn3dOut", Path.Combine(Path.GetTempPath(), "pn3d-shots"));
            Directory.CreateDirectory(outDir);

            int width = int.Parse(ArgValue("-pn3dWidth", "1600"), CultureInfo.InvariantCulture);
            int height = int.Parse(ArgValue("-pn3dHeight", "900"), CultureInfo.InvariantCulture);

            var run = Bootstrap.CreateRun(1);
            if (run == null) { Fail("could not create run"); return; }

            var holder = new GameObject("PN3D_Capture");
            try
            {
                WorldBuilder.BuildLighting(holder.transform);
                var built = WorldBuilder.Build(run, holder.transform);

                var camGo = new GameObject("CaptureCam");
                camGo.transform.SetParent(holder.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 60f;
                cam.farClipPlane = 900f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.49f, 0.71f, 0.89f);

                // 1) behind the car at the start line.
                // Warm-up render first: in batch mode the very first Camera.Render can
                // land before lighting and ambient settle, which tints the whole frame.
                PlaceCar(built, run.Car.X, run.Car.Y, run.Car.H);
                Behind(cam, built.Car, 11f, 5f);
                Shot(cam, outDir, "00_warmup", width, height);
                Shot(cam, outDir, "01_start", width, height);

                // 2) mid-route, on the first curve
                run.Route.PosAt(run.Route.Length * 0.42, run.Spot.T, out double mx, out double my, out double mh);
                PlaceCar(built, mx, my, mh);
                Behind(cam, built.Car, 12f, 5.5f);
                Shot(cam, outDir, "02_midroute", width, height);

                // 3) approaching the parking spot
                run.Route.PosAt(run.Spot.S - 26.0, run.Spot.T, out double ax, out double ay, out double ah);
                PlaceCar(built, ax, ay, ah);
                Behind(cam, built.Car, 12f, 6f);
                Shot(cam, outDir, "03_approach", width, height);

                // 4) parked in the spot, overhead assist view
                PlaceCar(built, run.Spot.X, run.Spot.Y, run.Spot.H);
                var t = built.Car;
                cam.transform.position = t.position - t.forward * 6f + Vector3.up * 15f;
                cam.transform.rotation = Quaternion.LookRotation(
                    (t.position + t.forward * 1.5f) - cam.transform.position, Vector3.up);
                Shot(cam, outDir, "04_parked", width, height);

                Log("CAPTURE OK -> " + outDir);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        static void PlaceCar(WorldBuilder.Built built, double x, double y, double h)
        {
            built.Car.position = WorldBuilder.ToWorld(x, y);
            built.Car.rotation = WorldBuilder.ToRotation(h);
        }

        static void Behind(Camera cam, Transform target, float dist, float height)
        {
            cam.transform.position = target.position - target.forward * dist + Vector3.up * height;
            cam.transform.rotation = Quaternion.LookRotation(
                (target.position + target.forward * 6f) - cam.transform.position, Vector3.up);
        }

        static void Shot(Camera cam, string dir, string name, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new UnityEngine.Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                string path = Path.Combine(dir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                Log($"  wrote {path}");
            }
            finally
            {
                cam.targetTexture = prev;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        static void Log(string m) => Debug.Log("[PN3D] " + m);

        static void Fail(string m)
        {
            Debug.LogError("[PN3D] FAILED: " + m);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
