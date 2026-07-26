using System.IO;
using UnityEditor;
using UnityEngine;

namespace PN3D.EditorTools
{
    /// <summary>
    /// Mirrors design-spec/data into Assets/Resources/Data so a player build can carry it.
    ///
    /// The mirror is generated and gitignored, never edited. Keeping it generated is the
    /// whole point: design-spec/data stays the single source of truth, and there is no
    /// second copy anyone can accidentally edit and have the two diverge. Files are only
    /// written when the text actually differs, so this does not churn the asset database
    /// on every editor load.
    /// </summary>
    [InitializeOnLoad]
    public static class DataSync
    {
        const string Dest = "Assets/Resources/Data";

        static DataSync() => EditorApplication.delayCall += () => Sync(false);

        [MenuItem("PN3D/Sync mission data into Resources")]
        static void SyncMenu() => Sync(true);

        /// <summary>Returns the number of files written. Safe to call repeatedly.</summary>
        public static int Sync(bool verbose)
        {
            string src = SourceDir();
            if (src == null)
            {
                Debug.LogError("[PN3D] design-spec/data not found; cannot sync mission data");
                return 0;
            }

            Directory.CreateDirectory(Dest);

            int written = 0;
            foreach (string file in Directory.GetFiles(src, "*.json"))
            {
                // .json is not a recognised text asset extension, so Unity would import
                // it as a generic asset that Resources.Load<TextAsset> cannot return.
                // .txt is, and the loader strips the extension anyway.
                string target = Path.Combine(Dest, Path.GetFileNameWithoutExtension(file) + ".txt");
                string text = File.ReadAllText(file);
                if (File.Exists(target) && File.ReadAllText(target) == text) continue;
                File.WriteAllText(target, text);
                written++;
            }

            if (written > 0)
            {
                AssetDatabase.Refresh();
                Debug.Log($"[PN3D] synced {written} data file(s) into {Dest}");
            }
            else if (verbose)
            {
                Debug.Log($"[PN3D] {Dest} already up to date");
            }
            return written;
        }

        static string SourceDir()
        {
            var repo = Directory.GetParent(Application.dataPath)?.Parent?.Parent;
            if (repo == null) return null;
            string p = Path.Combine(repo.FullName, "design-spec", "data");
            return Directory.Exists(p) ? p : null;
        }
    }
}
