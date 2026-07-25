using System.IO;
using UnityEngine;

namespace PN3D.Game
{
    /// <summary>
    /// Locates the mission / vehicle / district JSON.
    ///
    /// design-spec/data is the single source of truth and is deliberately NOT duplicated
    /// into the project, so this looks in StreamingAssets first and falls back to the
    /// repo copy two levels above the Unity project. The fallback is what runs today in
    /// the editor; wiring StreamingAssets (or Addressables) properly is part of the
    /// packaging work in milestone step 5, when there is a player build to feed.
    /// </summary>
    public static class DataPaths
    {
        public static string Load(string fileName)
        {
            string streaming = Path.Combine(Application.streamingAssetsPath, "Data", fileName);
            if (File.Exists(streaming)) return File.ReadAllText(streaming);

            // <repo>/unity/ParkingNightmare3D/Assets -> <repo>
            var repo = Directory.GetParent(Application.dataPath)   // ParkingNightmare3D
                                ?.Parent                            // unity
                                ?.Parent;                           // repo root
            if (repo != null)
            {
                string p = Path.Combine(repo.FullName, "design-spec", "data", fileName);
                if (File.Exists(p)) return File.ReadAllText(p);
            }

            Debug.LogError($"[PN3D] could not locate {fileName} in StreamingAssets/Data " +
                           "or design-spec/data");
            return null;
        }
    }
}
