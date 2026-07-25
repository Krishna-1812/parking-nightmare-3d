using UnityEngine;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// Greybox HUD: shame meter and face, timer against par, style with combo, distance
    /// to go, and the live alignment widget.
    ///
    /// IMGUI on purpose. It needs no fonts, atlases or prefabs, which keeps the whole
    /// vertical slice buildable from source with nothing to import — and the real UI is
    /// a UI Toolkit rebuild in milestone step 5 anyway (§11). The *readouts* are the
    /// point here, not the styling: the alignment widget in particular is required, since
    /// without live angle and curb feedback the parking tolerances read as arbitrary (§6).
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public MissionDriver Driver;

        GUIStyle _label, _big, _small, _center;
        Texture2D _white;

        void EnsureStyles()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _center ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        void Rect_(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        static Color Hex(string hex)
            => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        void OnGUI()
        {
            if (Driver?.Run == null) return;
            EnsureStyles();

            var run = Driver.Run;
            float w = Screen.width, h = Screen.height;

            // ---------- top-left: shame ----------
            Rect_(new Rect(14, 14, 250, 74), new Color(0, 0, 0, 0.55f));
            GUI.Label(new Rect(24, 18, 220, 22), $"{run.Shame.Face}  SHAME", _label);

            var barBg = new Rect(24, 44, 200, 18);
            Rect_(barBg, new Color(1, 1, 1, 0.18f));
            float frac = (float)(run.Shame.Shame / 100.0);
            Color shameCol = run.Shame.Shame < 25 ? new Color(0.35f, 0.8f, 0.45f)
                           : run.Shame.Shame < 50 ? new Color(0.95f, 0.82f, 0.3f)
                           : run.Shame.Shame < 75 ? new Color(0.98f, 0.55f, 0.25f)
                           : new Color(0.95f, 0.28f, 0.34f);
            // the meter pulses above 75 (§10)
            if (run.Shame.Pulsing)
                shameCol = Color.Lerp(shameCol, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 9f));
            Rect_(new Rect(barBg.x, barBg.y, barBg.width * frac, barBg.height), shameCol);
            GUI.Label(new Rect(230, 42, 60, 22), $"{Mathf.RoundToInt((float)run.Shame.Shame)}%", _small);

            // ---------- top-right: timer, style, distance ----------
            Rect_(new Rect(w - 264, 14, 250, 92), new Color(0, 0, 0, 0.55f));
            double par = run.Mission.Par;
            bool over = run.Timer > par;
            GUI.color = over ? new Color(1f, 0.5f, 0.5f) : Color.white;
            GUI.Label(new Rect(w - 254, 18, 240, 24),
                      $"⏱  {Scoring.FmtTime(run.Timer)}   par {Scoring.FmtTime(par)}", _label);
            GUI.color = Color.white;

            string combo = run.Style.Combo > 1 ? $"   🔥x{run.Style.DisplayMultiplier}" : "";
            GUI.Label(new Rect(w - 254, 44, 240, 22),
                      $"✨ STYLE {Mathf.RoundToInt((float)run.Style.Style)}{combo}", _label);

            GUI.Label(new Rect(w - 254, 70, 240, 22),
                      $"🅿  {run.DistanceToGo:0} m to go", _label);

            // ---------- warnings ----------
            if (!string.IsNullOrEmpty(run.Surface.WarnText))
            {
                GUI.color = new Color(1f, 0.72f, 0.3f);
                GUI.Label(new Rect(0, h * 0.24f, w, 30), run.Surface.WarnText, _center);
                GUI.color = Color.white;
            }

            // ---------- threshold banner ----------
            if (!string.IsNullOrEmpty(Driver.ThresholdBanner))
            {
                Rect_(new Rect(w * 0.5f - 200, h * 0.14f, 400, 40), new Color(0.6f, 0.05f, 0.12f, 0.85f));
                GUI.Label(new Rect(w * 0.5f - 200, h * 0.14f, 400, 40), Driver.ThresholdBanner, _center);
            }

            // ---------- popups ----------
            float py = h * 0.34f;
            foreach (var p in Driver.Popups)
            {
                GUI.color = Hex(p.Color);
                GUI.Label(new Rect(0, py, w, 26), p.Text, _center);
                GUI.color = Color.white;
                py += 26;
            }

            // ---------- alignment widget ----------
            if (run.Park.InZone) DrawAlignment(run, w, h);

            // ---------- stage overlays ----------
            switch (Driver.Stage)
            {
                case RunStage.Brief: DrawBrief(run, w, h); break;
                case RunStage.Countdown: DrawCountdown(w, h); break;
                case RunStage.Results: DrawResults(run, w, h); break;
                case RunStage.Failed: DrawFailed(w, h); break;
            }
        }

        void DrawAlignment(MissionRun run, float w, float h)
        {
            var m = run.Park.Measure;
            var box = new Rect(w * 0.5f - 150, h - 116, 300, 100);
            Rect_(box, new Color(0, 0, 0, 0.62f));

            GUI.Label(new Rect(box.x + 12, box.y + 6, 280, 22), "ALIGNMENT", _label);

            // angle
            var angOk = m.AngOk;
            GUI.color = angOk ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.45f, 0.45f);
            GUI.Label(new Rect(box.x + 12, box.y + 32, 150, 22), $"ANGLE  {m.AngDeg:0.0}°", _label);

            // curb gap, or in/out for a bay
            if (m.HasCurbGap)
            {
                int cm = Mathf.RoundToInt((float)(m.CurbGap * 100));
                GUI.color = m.CurbOk ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.45f, 0.45f);
                GUI.Label(new Rect(box.x + 160, box.y + 32, 140, 22),
                          cm < 0 ? "CURB  ON CURB" : $"CURB  {cm} cm", _label);
            }
            else
            {
                GUI.color = m.Inside ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.45f, 0.45f);
                GUI.Label(new Rect(box.x + 160, box.y + 32, 140, 22), m.Inside ? "BOX  IN" : "BOX  OUT", _label);
            }
            GUI.color = Color.white;

            // settle progress
            var pb = new Rect(box.x + 12, box.y + 62, 276, 14);
            Rect_(pb, new Color(1, 1, 1, 0.16f));
            float held = (float)(run.Park.ParkT / ParkChecker.HoldSeconds);
            Rect_(new Rect(pb.x, pb.y, pb.width * Mathf.Clamp01(held), pb.height),
                  new Color(0.35f, 0.85f, 0.5f));
            GUI.Label(new Rect(box.x + 12, box.y + 78, 280, 20),
                      run.Park.Phase == GamePhase.Settle ? "HOLD STILL…" : "slot in — watch angle and curb", _small);
        }

        void DrawBrief(MissionRun run, float w, float h)
        {
            Rect_(new Rect(0, 0, w, h), new Color(0, 0, 0, 0.72f));
            var box = new Rect(w * 0.5f - 330, h * 0.5f - 130, 660, 260);
            Rect_(box, new Color(0.08f, 0.09f, 0.12f, 0.96f));

            GUI.Label(new Rect(box.x, box.y + 16, box.width, 40),
                      $"MISSION {run.Mission.Id} — {run.Mission.Name}", _center);
            GUI.Label(new Rect(box.x + 30, box.y + 66, box.width - 60, 90),
                      run.Mission.Brief, new GUIStyle(_small) { wordWrap = true, fontSize = 15 });
            GUI.Label(new Rect(box.x, box.y + 162, box.width, 24),
                      $"{run.Veh.Name}  ·  par {Scoring.FmtTime(run.Mission.Par)}  ·  " +
                      $"{run.Mission.Park} finish  ·  {run.Route.Length / 1000.0:0.00} km", _center);
            GUI.Label(new Rect(box.x, box.y + 200, box.width, 24),
                      "WASD / arrows to drive   ·   SPACE handbrake", _center);
            GUI.Label(new Rect(box.x, box.y + 226, box.width, 24), "press any key to start", _center);
        }

        void DrawCountdown(float w, float h)
        {
            int n = Mathf.CeilToInt((float)Driver.CountdownT);
            GUI.color = new Color(1f, 0.9f, 0.4f);
            GUI.Label(new Rect(0, h * 0.4f, w, 80), n > 0 ? n.ToString() : "GO!", _big);
            GUI.color = Color.white;
        }

        void DrawResults(MissionRun run, float w, float h)
        {
            var r = run.Result;
            Rect_(new Rect(0, 0, w, h), new Color(0, 0, 0, 0.75f));
            var box = new Rect(w * 0.5f - 260, h * 0.5f - 190, 520, 380);
            Rect_(box, new Color(0.08f, 0.11f, 0.09f, 0.97f));

            GUI.Label(new Rect(box.x, box.y + 14, box.width, 36),
                      r.Perfect ? "PERFECT PARK!" : "PARKED!", _center);
            GUI.Label(new Rect(box.x, box.y + 52, box.width, 34),
                      new string('★', r.Stars) + new string('☆', 3 - r.Stars) + (r.SRank ? "   S-RANK" : ""), _center);

            float y = box.y + 100;
            foreach (var line in r.Lines)
            {
                GUI.Label(new Rect(box.x + 34, y, 320, 22), line.Label, _small);
                GUI.Label(new Rect(box.x + 380, y, 110, 22),
                          (line.Value >= 0 ? "+" : "") + line.Value, _small);
                y += 24;
            }

            y += 10;
            Rect_(new Rect(box.x + 34, y, 452, 1), new Color(1, 1, 1, 0.25f));
            y += 12;
            GUI.Label(new Rect(box.x + 34, y, 320, 26), "TOTAL", _label);
            GUI.Label(new Rect(box.x + 372, y, 120, 26), r.Total.ToString(), _label);
            y += 30;
            GUI.Label(new Rect(box.x + 34, y, 400, 22),
                      $"angle {r.AngDeg:0.0}°" +
                      (r.HasCurbGap ? $"   ·   curb {r.CurbGap * 100:0} cm" : "") +
                      $"   ·   +{r.Coins} coins", _small);
        }

        void DrawFailed(float w, float h)
        {
            Rect_(new Rect(0, 0, w, h), new Color(0.25f, 0.02f, 0.05f, 0.75f));
            GUI.Label(new Rect(0, h * 0.42f, w, 60),
                      Driver.FailReason == "SHAME" ? "TOO MUCH SHAME" : "WRECKED", _big);
            GUI.Label(new Rect(0, h * 0.52f, w, 30),
                      Driver.FailReason == "SHAME"
                        ? "the crowd has seen enough"
                        : "the car has seen enough", _center);
        }
    }
}
