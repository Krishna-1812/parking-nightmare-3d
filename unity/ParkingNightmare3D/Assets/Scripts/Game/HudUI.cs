using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// The HUD, on UI Toolkit.
    ///
    /// Replaces the greybox IMGUI pass. IMGUI rebuilt the whole interface every frame in
    /// OnGUI and allocated as it went — fine for reading numbers off a greybox, not fine
    /// on a phone. Here the tree is built once from <c>Assets/UI/Hud.uxml</c> and each
    /// frame only writes the values that changed.
    ///
    /// The layout intent lives in <c>Hud.uss</c>; this file is only the binding.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudUI : MonoBehaviour
    {
        public MissionDriver Driver;

        /// <summary>
        /// The on-screen controls, once the visual tree exists. Null until OnEnable has
        /// run. Exposed because Attach cannot hand these to the driver from inside
        /// OnEnable — see the note there.
        /// </summary>
        public TouchControls Touch { get; private set; }

        VisualElement _root;
        VisualElement _shamePanel, _shameFill, _mouth, _face, _align, _holdFill, _popups;
        VisualElement _brief, _results, _failed, _resLines;
        Label _shamePct, _timer, _par, _style, _combo, _togo, _banner, _warn;
        Label _angle, _curb, _alignHint, _countdown;
        Label _briefTitle, _briefText, _briefMeta;
        Label _resTitle, _resStars, _resTotal, _resDetail;
        Label _failTitle, _failSub;

        readonly List<Label> _popupPool = new();
        bool _resultsFilled;

        /// <summary>
        /// Attach a fully wired HUD to a GameObject.
        ///
        /// The UI assets live under Resources so this works from a bare GameObject as well
        /// as from the authored scene — the editor capture tool and the smoke test build
        /// their world from script and have no scene references to bind.
        /// </summary>
        public static HudUI Attach(GameObject host, MissionDriver driver)
        {
            var tree = Resources.Load<VisualTreeAsset>("UI/Hud");
            var panel = Resources.Load<PanelSettings>("UI/PN3D_PanelSettings");
            if (tree == null || panel == null)
            {
                Debug.LogError("[PN3D] HUD assets missing from Resources/UI — no HUD this run");
                return null;
            }

            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = tree;

            // AddComponent runs OnEnable synchronously, so the line below is already too
            // late for anything OnEnable reads — AttachTouch has run and found Driver null.
            // The touch controls are therefore collected from the HUD afterwards rather
            // than pushed from inside it. Symptom when this was wrong: the wheel and pads
            // drew correctly and did absolutely nothing, because TouchControls unhides
            // itself in its constructor but only the driver ever reads it.
            var hud = host.AddComponent<HudUI>();
            hud.Driver = driver;
            if (hud.Touch != null && driver != null) driver.Touch = hud.Touch;
            return hud;
        }

        static readonly Color Calm = new(0.35f, 0.80f, 0.45f);
        static readonly Color Warm = new(0.95f, 0.82f, 0.30f);
        static readonly Color Hot = new(0.98f, 0.55f, 0.25f);
        static readonly Color Crit = new(0.95f, 0.28f, 0.34f);
        static readonly Color Good = new(0.41f, 0.88f, 0.52f);
        static readonly Color Bad = new(1.00f, 0.45f, 0.42f);

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            if (_root == null) return;

            _shamePanel = _root.Q("shame-panel");
            _face = _root.Q("face");
            _mouth = _root.Q("mouth");
            _shameFill = _root.Q("shame-fill");
            _shamePct = _root.Q<Label>("shame-pct");

            _timer = _root.Q<Label>("timer");
            _par = _root.Q<Label>("par");
            _style = _root.Q<Label>("style");
            _combo = _root.Q<Label>("combo");
            _togo = _root.Q<Label>("togo");

            _banner = _root.Q<Label>("banner");
            _warn = _root.Q<Label>("warn");
            _popups = _root.Q("popups");

            _align = _root.Q("align");
            _angle = _root.Q<Label>("angle");
            _curb = _root.Q<Label>("curb");
            _holdFill = _root.Q("hold-fill");
            _alignHint = _root.Q<Label>("align-hint");

            _brief = _root.Q("brief");
            _briefTitle = _root.Q<Label>("brief-title");
            _briefText = _root.Q<Label>("brief-text");
            _briefMeta = _root.Q<Label>("brief-meta");
            _countdown = _root.Q<Label>("countdown");

            _results = _root.Q("results");
            _resTitle = _root.Q<Label>("res-title");
            _resStars = _root.Q<Label>("res-stars");
            _resLines = _root.Q("res-lines");
            _resTotal = _root.Q<Label>("res-total");
            _resDetail = _root.Q<Label>("res-detail");

            _failed = _root.Q("failed");
            _failTitle = _root.Q<Label>("fail-title");
            _failSub = _root.Q<Label>("fail-sub");

            AttachTouch();
        }

        /// <summary>
        /// Build the on-screen controls and hand them to the driver. Done here rather
        /// than in Attach because the visual tree only exists once the UIDocument is
        /// enabled, and the controls need the live elements to hit-test against.
        /// </summary>
        void AttachTouch()
        {
            var touch = new TouchControls(_root);
            Touch = touch;
            // Set here too, for the authored-scene path where Driver is bound in the
            // inspector and is therefore already live by the time OnEnable runs.
            if (Driver != null) Driver.Touch = touch;
            if (!touch.Enabled) return;

            // The brief tells the player how to drive; on a phone the keyboard line is
            // a lie and "press any key" has nothing to press.
            var keys = _root.Q<Label>("brief-keys");
            if (keys != null) keys.text = "wheel to steer    ·    GO and BRAKE    ·    HAND for handbrake";
            var prompt = _root.Q<Label>("brief-prompt");
            if (prompt != null) prompt.text = "tap to start";
        }

        static void Show(VisualElement e, bool on)
        {
            if (e == null) return;
            e.EnableInClassList("hidden", !on);
        }

        void Update()
        {
            if (_root == null || Driver?.Run == null) return;
            var run = Driver.Run;

            UpdateShame(run);
            UpdateStatus(run);
            UpdateTransient();
            UpdateAlignment(run);
            UpdateStage(run);
        }

        // ------------------------------------------------------------------ shame

        void UpdateShame(MissionRun run)
        {
            double s = run.Shame.Shame;
            _shameFill.style.width = Length.Percent(Mathf.Clamp01((float)(s / 100.0)) * 100f);

            var col = s < 25 ? Calm : s < 50 ? Warm : s < 75 ? Hot : Crit;
            // above 75 the meter pulses (§10) — the same tell the reference uses to say
            // "you are one incident from failing"
            if (run.Shame.Pulsing)
                col = Color.Lerp(col, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 9f));
            _shameFill.style.backgroundColor = col;

            _shamePct.text = Mathf.RoundToInt((float)s) + "%";

            _face.style.backgroundColor = Color.Lerp(new Color(0.97f, 0.80f, 0.29f), col, 0.35f);
            // four moods across the same thresholds the shame bands use
            _mouth.EnableInClassList("frown", s >= 50);
            _mouth.EnableInClassList("flat", s >= 25 && s < 50);
        }

        // ------------------------------------------------------------------ status

        void UpdateStatus(MissionRun run)
        {
            _timer.text = Scoring.FmtTime(run.Timer);
            _timer.EnableInClassList("over", run.Timer > run.Mission.Par);
            _par.text = "par " + Scoring.FmtTime(run.Mission.Par);

            _style.text = "STYLE " + Mathf.RoundToInt((float)run.Style.Style);
            bool combo = run.Style.Combo > 1;
            _combo.text = combo ? "x" + run.Style.DisplayMultiplier : "";
            Show(_combo, combo);

            _togo.text = run.Park.InZone
                ? "park it"
                : $"{run.DistanceToGo:0} m to the spot";
        }

        // ------------------------------------------------------------------ transient

        void UpdateTransient()
        {
            bool hasBanner = !string.IsNullOrEmpty(Driver.ThresholdBanner);
            Show(_banner, hasBanner);
            if (hasBanner) _banner.text = Driver.ThresholdBanner;

            string warn = Driver.Run.Surface.WarnText;
            bool hasWarn = !string.IsNullOrEmpty(warn);
            Show(_warn, hasWarn);
            if (hasWarn) _warn.text = warn;

            // pooled labels: popups fire several times a second under a combo, and
            // rebuilding the subtree each time is exactly the allocation churn UI Toolkit
            // was brought in to avoid
            var list = Driver.Popups;
            while (_popupPool.Count < list.Count)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList("popup");
                _popups.Add(l);
                _popupPool.Add(l);
            }
            for (int i = 0; i < _popupPool.Count; i++)
            {
                bool on = i < list.Count;
                Show(_popupPool[i], on);
                if (!on) continue;
                _popupPool[i].text = list[i].Text;
                _popupPool[i].style.color =
                    ColorUtility.TryParseHtmlString(list[i].Color, out var c) ? c : Color.white;
            }
        }

        // ------------------------------------------------------------------ alignment

        void UpdateAlignment(MissionRun run)
        {
            bool on = run.Park.InZone && Driver.Stage == RunStage.Driving;
            Show(_align, on);
            if (!on) return;

            var m = run.Park.Measure;

            _angle.text = $"ANGLE {m.AngDeg:0.0}°";
            _angle.EnableInClassList("ok", m.AngOk);
            _angle.EnableInClassList("bad", !m.AngOk);

            if (m.HasCurbGap)
            {
                int cm = Mathf.RoundToInt((float)(m.CurbGap * 100));
                _curb.text = cm < 0 ? "CURB  ON CURB" : $"CURB  {cm} cm";
                _curb.EnableInClassList("ok", m.CurbOk);
                _curb.EnableInClassList("bad", !m.CurbOk);
            }
            else
            {
                _curb.text = m.Inside ? "BOX  IN" : "BOX  OUT";
                _curb.EnableInClassList("ok", m.Inside);
                _curb.EnableInClassList("bad", !m.Inside);
            }

            float held = Mathf.Clamp01((float)(run.Park.ParkT / ParkChecker.HoldSeconds));
            _holdFill.style.width = Length.Percent(held * 100f);
            _holdFill.style.backgroundColor = Color.Lerp(Good, Color.white, held * 0.35f);
            _alignHint.text = run.Park.Phase == GamePhase.Settle
                ? "HOLD STILL…"
                : "slot in — watch angle and curb";
        }

        // ------------------------------------------------------------------ stages

        void UpdateStage(MissionRun run)
        {
            var stage = Driver.Stage;

            Show(_brief, stage == RunStage.Brief);
            Show(_countdown, stage == RunStage.Countdown);
            Show(_results, stage == RunStage.Results);
            Show(_failed, stage == RunStage.Failed);
            Show(_shamePanel, stage != RunStage.Brief);

            switch (stage)
            {
                case RunStage.Brief:
                    _briefTitle.text = $"MISSION {run.Mission.Id} — {run.Mission.Name}";
                    _briefText.text = run.Mission.Brief;
                    _briefMeta.text = $"{run.Veh.Name}   ·   par {Scoring.FmtTime(run.Mission.Par)}   ·   " +
                                      $"{run.Mission.Park} finish   ·   {run.Route.Length / 1000.0:0.00} km";
                    break;

                case RunStage.Countdown:
                    int n = Mathf.CeilToInt((float)Driver.CountdownT);
                    _countdown.text = n > 0 ? n.ToString() : "GO!";
                    break;

                case RunStage.Results:
                    if (!_resultsFilled) FillResults(run);
                    break;

                case RunStage.Failed:
                    bool shame = Driver.FailReason == "SHAME";
                    _failTitle.text = shame ? "TOO MUCH SHAME" : "WRECKED";
                    _failSub.text = shame ? "the crowd has seen enough" : "the car has seen enough";
                    break;
            }
        }

        void FillResults(MissionRun run)
        {
            var r = run.Result;
            if (r == null) return;
            _resultsFilled = true;

            _resTitle.text = r.Perfect ? "PERFECT PARK!" : "PARKED!";
            _resStars.text = new string('★', r.Stars) + new string('☆', 3 - r.Stars)
                           + (r.SRank ? "   S-RANK" : "");

            _resLines.Clear();
            foreach (var line in r.Lines)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("line");

                var label = new Label(line.Label);
                label.AddToClassList("line-label");

                var value = new Label((line.Value >= 0 ? "+" : "") + line.Value);
                value.AddToClassList("line-value");
                value.EnableInClassList("negative", line.Value < 0);

                row.Add(label);
                row.Add(value);
                _resLines.Add(row);
            }

            _resTotal.text = r.Total.ToString();
            _resDetail.text = $"angle {r.AngDeg:0.0}°"
                            + (r.HasCurbGap ? $"   ·   curb {r.CurbGap * 100:0} cm" : "")
                            + $"   ·   +{r.Coins} coins";
        }
    }
}
