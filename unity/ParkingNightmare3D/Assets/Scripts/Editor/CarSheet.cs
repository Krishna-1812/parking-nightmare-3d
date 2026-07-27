using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PN3D.Core;
using PN3D.Game;
using PN3D.Game.Art;

namespace PN3D.EditorTools
{
    /// <summary>
    /// Renders every entry in <see cref="CarStyles.All"/> to its own PNG, from a fixed
    /// three-quarter view under the game's own sky and sun.
    ///
    /// This exists because the alternative is judging car art off a 30-pixel object on a
    /// moving chase camera, over a two-minute APK build and a device install. Every visual
    /// mistake made on this model so far — a rim barrel capping off the spokes, lamps
    /// floating outside the bodywork, metallic paint going black — was invisible at that
    /// size and obvious at this one.
    ///
    ///   Unity.exe -quit -batchmode -projectPath &lt;proj&gt; \
    ///             -executeMethod PN3D.EditorTools.CarSheet.Render -logFile - -pn3dOut &lt;dir&gt;
    ///
    /// NOT -nographics: this renders, so it needs a graphics device.
    /// </summary>
    public static class CarSheet
    {
        [MenuItem("PN3D/Render car contact sheet")]
        public static void Render()
        {
            string outDir = ArgValue("-pn3dOut", Path.Combine(Path.GetTempPath(), "pn3d-cars"));
            Directory.CreateDirectory(outDir);
            int w = int.Parse(ArgValue("-pn3dWidth", "900"), CultureInfo.InvariantCulture);
            int h = int.Parse(ArgValue("-pn3dHeight", "620"), CultureInfo.InvariantCulture);

            var holder = new GameObject("PN3D_CarSheet");
            try
            {
                // Same sky, sun and ambient the game builds, so the paint is judged under
                // the light it will actually ship in rather than the editor default.
                var district = District.Load(DataPaths.Load("districts.json"), 0);
                SceneEnv.Build(district, Vector3.zero, holder.transform);
                // No PostFx.Build here: it returns a Volume, and pulling
                // Unity.RenderPipelines.Core.Runtime into the editor asmdef to name that
                // type is not worth it for a sheet that exists to judge form and paint.
                // Bloom on the lamps gets checked in game, where it actually matters.

                var camGo = new GameObject("SheetCam");
                camGo.transform.SetParent(holder.transform, false);
                var cam = camGo.AddComponent<Camera>();
                PostFx.SetupCamera(cam, 42f);
                cam.clearFlags = CameraClearFlags.Skybox;

                // a plain ground plane so the car has something to sit on and cast onto
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.SetParent(holder.transform, false);
                ground.transform.localScale = Vector3.one * 6f;
                ground.GetComponent<MeshRenderer>().sharedMaterial =
                    MatLib.Solid(new Color(0.34f, 0.35f, 0.36f), 0.25f);
                Object.DestroyImmediate(ground.GetComponent<Collider>());

                bool first = true;
                foreach (var st in CarStyles.All)
                {
                    var veh = SizeFor(st);
                    var stage = new GameObject("Stage_" + st.Key).transform;
                    stage.SetParent(holder.transform, false);

                    var rig = CarView.Build(stage, st.Key, veh, st, PaintFor(st));

                    // frame from the length, so a limo and a go-kart both fill the frame
                    // Tight three-quarter from just above waist height — the angle a car is
                    // photographed from, and close enough that a wrong 3 cm shows.
                    float len = (float)veh.Len;
                    float dist = len * 1.05f + 1.5f;
                    float aim = st.WheelR + st.BodyH * 0.5f;

                    void Aim(Vector3 at)
                    {
                        cam.transform.position = at;
                        cam.transform.rotation = Quaternion.LookRotation(
                            new Vector3(0, aim, 0) - at, Vector3.up);
                    }

                    Aim(new Vector3(dist * 0.78f, aim + len * 0.16f, -dist * 0.66f));

                    // The first Camera.Render in batch mode can land before ambient and the
                    // skybox settle, which tints the whole frame. Burn one.
                    if (first) { Shot(cam, outDir, "00_warmup", w, h); first = false; }

                    Shot(cam, outDir, st.Key, w, h);

                    // The nose is at +Z, so the shot above — the only one this sheet ever
                    // took — is a REAR three-quarter. The front had never been looked at.
                    Aim(new Vector3(dist * 0.72f, aim + len * 0.16f, dist * 0.70f));
                    Shot(cam, outDir, st.Key + "_front", w, h);

                    // And the chase camera's own angle: behind, high, near the centreline,
                    // looking down the boot lid. This is what the player stares at for the
                    // whole game, it is the least flattering view the model has, and it is
                    // the one that revealed the greenhouse was a black hole — on a phone,
                    // after every other angle had been signed off in here.
                    Aim(new Vector3(len * 0.10f, aim + len * 0.42f, -len * 1.15f));
                    Shot(cam, outDir, st.Key + "_chase", w, h);
                    Debug.Log($"[PN3D]   {st.Key,-8} {st.Label,-22} " +
                              $"{veh.Len:0.0} x {veh.Wid:0.00} m, wheel r={st.WheelR:0.00}");

                    Object.DestroyImmediate(rig.Root.gameObject);
                    Object.DestroyImmediate(stage.gameObject);
                }

                Debug.Log($"[PN3D] CAR SHEET OK — {CarStyles.All.Length} styles -> {outDir}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PN3D] car sheet FAILED: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// Plausible real dimensions per archetype. These are for the contact sheet only —
        /// in game the length and width always come from the simulation.
        /// </summary>
        static VehicleDef SizeFor(CarStyle st)
        {
            var (len, wid) = st.Key switch
            {
                "exec" => (4.75, 1.85),
                "coupe" => (4.55, 1.87),
                "hatch" => (3.95, 1.75),
                "suv" => (4.80, 1.95),
                "wagon" => (4.85, 1.86),
                "taxi" => (4.50, 1.80),
                "police" => (4.70, 1.85),
                "van" => (6.40, 2.30),
                "limo" => (8.60, 1.95),
                "pickup" => (5.70, 2.20),
                _ => (3.90, 1.78),
            };
            return new VehicleDef { Key = st.Key, Len = len, Wid = wid, Hgt = 1.5 };
        }

        static Color PaintFor(CarStyle st) => st.Key switch
        {
            "taxi" => CarStyles.TaxiYellow,
            "police" => CarStyles.PoliceWhite,
            "rusty" => new Color(0.753f, 0.337f, 0.231f),
            _ => CarStyles.PaintFor(System.Array.IndexOf(CarStyles.All, st) * 2 + 3),
        };

        static void Shot(Camera cam, string dir, string name, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = prev;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static string ArgValue(string flag, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
