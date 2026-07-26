using UnityEngine;

namespace PN3D.Game
{
    /// <summary>
    /// Last-resort on-screen error display.
    ///
    /// This exists because of how the first Android build failed: an exception unwound out
    /// of world construction, the app kept running at a locked 60fps, and the screen showed
    /// sky and ground and nothing else. An Android RELEASE build prints nothing under the
    /// Unity logcat tag, so from the device there was no way to tell a crash from a camera
    /// pointing the wrong way. Silence is the worst possible failure mode on a phone.
    ///
    /// Deliberately IMGUI. It has no dependency on the UI Toolkit panel, the HUD assets or
    /// anything else that might itself be the thing that failed — the whole point is that
    /// it still draws when the rest of the scene did not.
    /// </summary>
    public sealed class FatalOverlay : MonoBehaviour
    {
        string _message;

        public static void Show(GameObject host, string context, System.Exception e)
        {
            Debug.LogError($"[PN3D] {context}: {e}");
            var o = host.AddComponent<FatalOverlay>();
            o._message = $"{context}\n\n{e.GetType().Name}: {e.Message}\n\n{e.StackTrace}";
        }

        void OnGUI()
        {
            if (_message == null) return;

            // Scale up for a phone: the default IMGUI font is unreadable at 1080p density.
            var prev = GUI.matrix;
            float scale = Mathf.Max(1f, Screen.dpi > 0f ? Screen.dpi / 160f : Screen.height / 480f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float w = Screen.width / scale, h = Screen.height / scale;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.45f, 0.42f);
            GUI.Label(new Rect(16, 16, w - 32, h - 32), _message);
            GUI.color = Color.white;

            GUI.matrix = prev;
        }
    }
}
