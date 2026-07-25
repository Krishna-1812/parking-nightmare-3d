using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PN3D.Game;

namespace PN3D.EditorTools
{
    /// <summary>
    /// Generates the committed Mission 1 scene and the assets it needs.
    ///
    /// The scene is a real .unity asset — milestone step 5 calls for replacing the
    /// <c>[RuntimeInitializeOnLoadMethod]</c> bootstrap, because a project you cannot open
    /// and look at is not a project anyone else can work on, and a player build needs a
    /// scene in its build list either way.
    ///
    /// It is *generated* rather than hand-authored so it stays reproducible: the earlier
    /// objection to a scene asset was that its serialized references rot silently. Rebuild
    /// it with PN3D/Rebuild Mission 1 Scene and the diff tells you exactly what changed.
    /// The world itself is still built at load by <see cref="WorldBuilder"/>; the scene
    /// holds only the things that genuinely want to be authored — camera, HUD document,
    /// audio listener and the mission to run.
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Mission01.unity";
        const string PanelPath = "Assets/Resources/UI/PN3D_PanelSettings.asset";
        const string ThemePath = "Assets/Resources/UI/UnityDefaultRuntimeTheme.tss";

        [MenuItem("PN3D/Rebuild Mission 1 Scene")]
        public static void Build()
        {
            EnsurePanelSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var runner = new GameObject("MissionRunner");
            runner.AddComponent<MissionHost>().MissionId = 1;

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            PN3D.Game.Art.PostFx.SetupCamera(cam, 62f);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ChaseCamera>();

            // Lighting comes from SceneEnv at load, driven by the district palette, so the
            // scene deliberately carries no light and no lightmap data. Baking a suburbs
            // sun into this scene would make the other five districts wrong.
            var settings = new GameObject("_SceneNotes");
            settings.transform.position = Vector3.zero;
            settings.SetActive(false);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[PN3D] wrote " + ScenePath);
        }

        static void EnsurePanelSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PanelPath));

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }

            // Scale with the screen against a 1080p reference, matching the sizes Hud.uss
            // is written in. Phones get the same layout, just smaller.
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.clearColor = false;

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null) panel.themeStyleSheet = theme;
            else Debug.LogWarning("[PN3D] runtime theme missing at " + ThemePath);

            EditorUtility.SetDirty(panel);
        }

        static void AddToBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
