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
                run.Step(dt, Autopilot(run, targetT));
                steps++;

                if (run.Timer - lastReport >= 10.0)
                {
                    lastReport = run.Timer;
                    int crossers = 0;
                    foreach (var kv in run.Traffic.Crossers) crossers += kv.Value.Count;
                    Log($"  t={run.Timer,6:0.0}s  s={run.Proj.S,7:0.0}  v={run.Car.Speed,5:0.0}  " +
                        $"shame={run.Shame.Shame,5:0.0}  style={run.Style.Style,4:0}  " +
                        $"cars={run.Traffic.Cars.Count,2}+{crossers}  " +
                        $"col={run.Collisions}  phase={run.Phase}");
                }
            }

            Log($"finished  : phase={run.Phase} after {steps} steps ({run.Timer:0.0}s sim time)");
            Log($"            shame={run.Shame.Shame:0.00}  style={run.Style.Style:0}  " +
                $"damage={run.Car.Damage:0.0}  collisions={run.Collisions}  distDriven={run.DistDriven:0}m");

            int filmed = 0, dived = 0;
            foreach (var p in run.Peds.List) { if (p.Filmed) filmed++; if (p.State == PedState.Dive) dived++; }
            Log($"            peds={run.Peds.List.Count} (filmed {filmed}, mid-dive {dived})  " +
                $"traffic alive={run.Traffic.Cars.Count}  lights={run.Traffic.Lights.Count}");

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
        /// Writes the generated textures out as PNGs.
        ///
        /// Every surface in this game is painted by code, which means a texture that comes
        /// out wrong is invisible until it is already on a model and mixed up with the
        /// lighting, the UVs and the material. Looking at the bitmap on its own separates
        /// "the painter is wrong" from "the mapping is wrong", and those have completely
        /// different fixes.
        /// </summary>
        [MenuItem("PN3D/Dump Textures")]
        public static void DumpTextures()
        {
            string outDir = ArgValue("-pn3dOut", Path.Combine(Path.GetTempPath(), "pn3d-tex"));
            Directory.CreateDirectory(outDir);

            // Raster.ToTexture uploads with makeNoLongerReadable, so EncodeToPNG cannot
            // touch the pixels — they only exist on the GPU. Blitting to a render texture
            // and reading back is the way to get them, and it needs a graphics device, so
            // this entry point must NOT be run with -nographics.
            void Write(string name, Texture2D t)
            {
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0,
                                                    RenderTextureFormat.ARGB32,
                                                    RenderTextureReadWrite.Linear);
                var prev = RenderTexture.active;
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new UnityEngine.Rect(0, 0, t.width, t.height), 0, 0);
                copy.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), copy.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(copy);
                Log($"  wrote {name}.png  {t.width}x{t.height}");
            }

            Write("face", PN3D.Game.Art.ProcTex.Face(1, new Color(0.18f, 0.12f, 0.09f),
                                                     new Color(0.27f, 0.17f, 0.08f), true));
            Write("skinDetail", PN3D.Game.Art.ProcTex.SkinDetail());
            Log("DUMP OK -> " + outDir);
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
                var built = WorldBuilder.Build(run, holder.transform);

                var camGo = new GameObject("CaptureCam");
                camGo.transform.SetParent(holder.transform, false);
                var cam = camGo.AddComponent<Camera>();
                PN3D.Game.Art.PostFx.SetupCamera(cam, 60f);

                // 1) behind the car at the start line.
                // Warm-up render first: in batch mode the very first Camera.Render can
                // land before lighting and ambient settle, which tints the whole frame.
                PlaceCar(built, run.Car.X, run.Car.Y, run.Car.H);
                Behind(cam, built.Car, 11f, 5f);
                Shot(cam, outDir, "00_warmup", width, height);
                Shot(cam, outDir, "01_start", width, height);

                // 2) mid-route with live traffic and pedestrians. Simulate first — the
                // actors only exist once the run has been stepped, and they populate
                // relative to where the player actually is.
                const double dt = 1.0 / 120.0;
                for (int i = 0; i < 3000 && run.Proj.S < run.Route.Length * 0.45; i++)
                    run.Step(dt, Autopilot(run, run.Spot.T));

                int crossers = 0;
                foreach (var kv in run.Traffic.Crossers) crossers += kv.Value.Count;
                Log($"  simulated to s={run.Proj.S:0} with {run.Traffic.Cars.Count} cars, " +
                    $"{crossers} crossers, {run.Peds.List.Count} peds");
                ActorViews.SpawnStatic(run, holder.transform);

                PlaceCar(built, run.Car.X, run.Car.Y, run.Car.H);
                Behind(cam, built.Car, 13f, 6f);
                Shot(cam, outDir, "02_traffic", width, height);

                // elevated three-quarter view: shows the traffic ahead and the
                // pedestrians on both sidewalks, which the chase view flattens out
                var t2 = built.Car;
                cam.transform.position = t2.position - t2.forward * 22f
                                       + t2.right * 13f + Vector3.up * 15f;
                cam.transform.rotation = Quaternion.LookRotation(
                    (t2.position + t2.forward * 16f) - cam.transform.position, Vector3.up);
                Shot(cam, outDir, "02b_actors", width, height);

                // 2c) a pedestrian at conversational distance. Every other shot here is
                // from the chase camera or further, and at that range a person made of
                // boxes and a person made of anything else look identical — which is how
                // the crowd stayed boxes through three art passes.
                var actors = holder.transform.Find("PN3D_Actors_Static");
                if (actors != null)
                    foreach (Transform who in actors)
                    {
                        if (who.name != "Ped") continue;
                        // In FRONT of them and slightly to one side. The first version of
                        // this shot framed the back of a head, which is the one angle that
                        // cannot tell me whether the face works.
                        cam.transform.position = who.position + who.forward * 1.9f
                                               + who.right * 0.9f + Vector3.up * 1.45f;
                        cam.transform.rotation = Quaternion.LookRotation(
                            (who.position + Vector3.up * 1.12f) - cam.transform.position,
                            Vector3.up);
                        Shot(cam, outDir, "02c_ped", width, height);

                        // And a head-and-shoulders. A face is a few centimetres of a
                        // 1.7 m figure; at any framing that shows the whole person it is
                        // thirty pixels, and thirty pixels cannot tell you whether the
                        // mapping is right or whether you are looking at the back of a
                        // skull.
                        var eye = who.position + Vector3.up * 1.56f;
                        // 0.58 m, not 0.34: at a third of a metre the near clip plane is
                        // already inside the skull and the shot renders the inside of the
                        // back of the head.
                        cam.transform.position = eye + who.forward * 0.58f
                                               + who.right * 0.19f + Vector3.up * 0.03f;
                        cam.transform.rotation = Quaternion.LookRotation(
                            eye - cam.transform.position, Vector3.up);
                        Shot(cam, outDir, "02d_face", width, height);

                        // And the same head from directly behind. Two views settle in one
                        // render what guessing at one view cannot: whether a blank face is
                        // a mapping bug or just the back of a head.
                        //
                        // 0.58, for the reason given four lines up — which this shot did
                        // not take on board and then spent a debugging round proving. At
                        // 0.34 the near plane sits inside the skull, so the back of the
                        // head is clipped away and the render is the inside of the face
                        // with the street showing through it. It looks exactly like a hole
                        // in the mesh, and it is a hole in the camera.
                        cam.transform.position = eye - who.forward * 0.58f;
                        cam.transform.rotation = Quaternion.LookRotation(
                            eye - cam.transform.position, Vector3.up);
                        Shot(cam, outDir, "02e_back", width, height);
                        break;
                    }

                // 3) approaching the parking spot
                run.Route.PosAt(run.Spot.S - 26.0, run.Spot.T, out double ax, out double ay, out double ah);
                PlaceCar(built, ax, ay, ah);
                Behind(cam, built.Car, 12f, 6f);
                Shot(cam, outDir, "03_approach", width, height);

                // 3b) close three-quarter on the car itself. The player looks at this more
                // than anything else in the game, so it gets its own check shot.
                run.Route.PosAt(run.Spot.S - 40.0, run.Spot.T, out double cx, out double cy, out double ch);
                PlaceCar(built, cx, cy, ch);
                var tc = built.Car;
                cam.transform.position = tc.position + tc.forward * 4.6f
                                       + tc.right * 3.4f + Vector3.up * 1.55f;
                cam.transform.rotation = Quaternion.LookRotation(
                    (tc.position + Vector3.up * 0.55f) - cam.transform.position, Vector3.up);
                Shot(cam, outDir, "03b_car", width, height);

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

        /// <summary>
        /// Lane-holding autopilot: steer by the route projection, ease speed to zero at
        /// the spot centre. A test harness, not gameplay — but it exercises the whole
        /// pipeline including traffic reacting to the player.
        /// </summary>
        public static VehicleInput Autopilot(MissionRun run, double targetT)
        {
            double toGo = run.Spot.S - run.Proj.S;
            double lateralErr = targetT - run.Proj.T;
            double headErr = MathX.AngNorm(run.Proj.H - run.Car.H);
            double steer = MathX.Clamp(headErr * 1.7 + lateralErr * 0.30, -1, 1);

            // Ease proportionally to zero *at the spot centre*. Stopping short is not
            // good enough: the check needs all four corners inside the box, and the
            // hatch's 1.95 m rear overhang leaves the 3.25 m half-length if the centre
            // halts even ~1.5 m early.
            double speed = run.Car.Speed;
            double wantSpeed = MathX.Clamp(toGo * 0.55, 0.0, 12.0);
            if (toGo < 0.2) wantSpeed = 0.0;

            double throttle;
            if (speed < wantSpeed - 0.3) throttle = 1.0;
            // never brake below ~0.6 m/s: negative throttle under 0.45 is reverse, not
            // braking (§3.1), so it would back out of the spot instead of settling
            else if (speed > wantSpeed + 0.3 && speed > 0.6) throttle = -1.0;
            else throttle = 0.0;

            return new VehicleInput { Steer = steer, Throttle = throttle };
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
