using System.IO;
using UnityEditor;
using UnityEngine;
using PN3D.Game;
using PN3D.Game.Art;

namespace PN3D.EditorTools
{
    /// <summary>
    /// Renders the car on its own, from several angles, straight to a PNG contact sheet.
    ///
    /// This exists because the alternative is judging vehicle art from a chase-cam
    /// screenshot of a moving car, where the whole vehicle is about thirty pixels tall and
    /// a wheel is six. That loop is also slow — build the APK, install it, drive it, grab a
    /// frame — and it answers "does it look wrong" without ever answering "which part".
    ///
    /// Lighting comes from the real <see cref="SceneEnv"/> and the real district, so what
    /// this shows is what the game shows. It never enters play mode, so it is safe in
    /// -batchmode:
    ///
    ///   Unity.exe -quit -batchmode -projectPath &lt;proj&gt; \
    ///             -executeMethod PN3D.EditorTools.CarShot.Shoot -logFile -
    ///
    /// Note: NOT -nographics. Rendering to a RenderTexture needs a real graphics device.
    /// </summary>
    public static class CarShot
    {
        const int Tile = 512;
        static readonly string Out =
            Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Build", "carshot.png");

        [MenuItem("PN3D/Shoot the car (contact sheet)")]
        public static void Shoot()
        {
            var run = MissionHost.CreateRun(1);
            if (run == null)
            {
                Debug.LogError("[PN3D] could not create mission 1 — no vehicle to shoot");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var root = new GameObject("CarShot");
            try
            {
                // Real district lighting, so the shot matches the game rather than a
                // studio setup that flatters the material and lies about it.
                var district = WorldBuilder.LoadDistrict(run.Mission);
                SceneEnv.Build(district, Vector3.zero, root.transform);
                DynamicGI.UpdateEnvironment();

                var rig = CarView.BuildHatch(root.transform, run.Veh);
                rig.Root.localPosition = Vector3.zero;

                // A plain ground plane: a car floating in the void loses its contact
                // shadow, and the shadow is half of what makes it look planted.
                Geo.Node("Ground", root.transform, Geo.UnitCube,
                         MatLib.Solid(new Color(0.30f, 0.31f, 0.29f), 0.15f),
                         new Vector3(0, -0.5f, 0), Quaternion.identity,
                         new Vector3(40f, 1f, 40f));

                float len = (float)run.Veh.Len;
                var angles = new (string name, Vector3 dir, float height)[]
                {
                    ("front",    new Vector3( 0.0f, 0f,  1.0f), 0.22f),
                    ("rear 3/4", new Vector3(-0.9f, 0f, -1.0f), 0.55f),
                    ("side",     new Vector3( 1.0f, 0f,  0.0f), 0.30f),
                    ("top",      new Vector3( 0.0f, 0f,  0.01f), 6.0f),
                    ("wheel",    new Vector3( 1.0f, 0f,  0.45f), 0.12f),
                };

                var sheet = new Texture2D(Tile * angles.Length, Tile, TextureFormat.RGB24, false);
                for (int i = 0; i < angles.Length; i++)
                {
                    // the wheel tile is a close-up on the front axle, everything else frames
                    // the whole car
                    bool closeUp = angles[i].name == "wheel";
                    var focus = closeUp
                        ? new Vector3((float)run.Veh.Wid * 0.5f, 0.34f, len * 0.32f)
                        : new Vector3(0f, 0.55f, 0f);
                    float dist = closeUp ? 1.5f : len * 1.75f;

                    var px = Render(root.transform, angles[i].dir, angles[i].height, focus, dist);
                    sheet.SetPixels(i * Tile, 0, Tile, Tile, px.GetPixels());
                    Object.DestroyImmediate(px);
                }
                sheet.Apply();

                Directory.CreateDirectory(Path.GetDirectoryName(Out)!);
                File.WriteAllBytes(Out, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                Debug.Log($"[PN3D] CARSHOT OK {Out}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PN3D] carshot failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            finally
            {
                Object.DestroyImmediate(root);
                Geo.Clear();
                MatLib.Clear();
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static Texture2D Render(Transform parent, Vector3 dir, float height, Vector3 focus, float dist)
        {
            var camGo = new GameObject("ShotCam");
            camGo.transform.SetParent(parent, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;

            var offset = dir.normalized * dist + Vector3.up * (dist * height);
            camGo.transform.position = focus + offset;
            camGo.transform.LookAt(focus);

            var rt = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8,
            };
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Tile, Tile, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            return tex;
        }
    }
}
