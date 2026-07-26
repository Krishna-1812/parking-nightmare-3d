using System.IO;
using UnityEngine;

namespace PN3D.Game
{
    /// <summary>
    /// Locates the mission / vehicle / district JSON.
    ///
    /// design-spec/data is the single source of truth and is still not duplicated by
    /// hand. It is MIRRORED into Assets/Resources/Data by PN3D.EditorTools.DataSync,
    /// which runs on editor load and again before every player build, so the mirror
    /// cannot drift — it is generated, gitignored, and rewritten whenever the source
    /// changes.
    ///
    /// Resources rather than StreamingAssets, deliberately. On Android StreamingAssets
    /// lives inside the compressed APK, where it has no filesystem path at all:
    /// File.Exists returns false and the only way in is an async UnityWebRequest. These
    /// files are small, needed synchronously during Awake, and never patched at runtime,
    /// which is exactly the case Resources exists to serve.
    /// </summary>
    public static class DataPaths
    {
        public static string Load(string fileName)
        {
            // Resources keys carry no extension; the mirror writes .txt because .json is
            // not an extension Unity imports as a TextAsset.
            var asset = Resources.Load<TextAsset>("Data/" + Path.GetFileNameWithoutExtension(fileName));
            if (asset != null) return asset.text;

            // Editor fallback, so a fresh clone still opens and plays before DataSync has
            // ever run. Never reached in a player build.
            var repo = Directory.GetParent(Application.dataPath)   // ParkingNightmare3D
                                ?.Parent                            // unity
                                ?.Parent;                           // repo root
            if (repo != null)
            {
                string p = Path.Combine(repo.FullName, "design-spec", "data", fileName);
                if (File.Exists(p)) return File.ReadAllText(p);
            }

            Debug.LogError($"[PN3D] could not locate {fileName} in Resources/Data " +
                           "or design-spec/data");
            return null;
        }
    }
}
