using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using PN3D.Core;

namespace PN3D.Game
{
    public enum RunStage { Brief, Countdown, Driving, Results, Failed }

    /// <summary>
    /// Drives one mission: owns the <see cref="MissionRun"/>, feeds it input from
    /// FixedUpdate at 120 Hz, and interpolates the car transform between fixed steps.
    ///
    /// There is no accumulator here on purpose. Unity's own fixed loop already is the
    /// accumulator, and ProjectSettings/TimeManager.asset is committed with
    /// Fixed Timestep 1/120 and Maximum Allowed Timestep 10/120 — which reproduces the
    /// reference loop's 10-substep clamp, including the fact that it discards time debt
    /// rather than spiralling (src/n3_f.js:977).
    /// </summary>
    public sealed class MissionDriver : MonoBehaviour
    {
        public MissionRun Run { get; private set; }
        public RunStage Stage { get; private set; } = RunStage.Brief;
        public double CountdownT { get; private set; }
        public string FailReason { get; private set; }

        /// <summary>Transient banner text (threshold messages, popups) with expiry.</summary>
        public readonly List<(string Text, string Color, float Until)> Popups = new();
        public string ThresholdBanner { get; private set; }
        float _thresholdUntil;

        Transform _car;
        Transform _carBody;

        public void Init(MissionRun run, Transform car, Transform carBody)
        {
            Run = run;
            _car = car;
            _carBody = carBody;

            run.Shame.OnThreshold += (pct, msg) =>
            {
                ThresholdBanner = msg;
                _thresholdUntil = Time.time + 2.5f;
            };
            run.Shame.OnPopup += (label, color) => Push(label, color);
            run.Style.OnAward += (label, total, mult) =>
                Push($"{label} +{total}{(mult > 1 ? " x" + mult : "")}", "#ffc23e");
            run.OnFailed += reason => { FailReason = reason; Stage = RunStage.Failed; };
            run.OnSucceeded += _ => Stage = RunStage.Results;
        }

        void Push(string text, string color)
        {
            Popups.Add((text, color, Time.time + 1.6f));
            if (Popups.Count > 6) Popups.RemoveAt(0);
        }

        public void BeginCountdown()
        {
            if (Stage != RunStage.Brief) return;
            Stage = RunStage.Countdown;
            CountdownT = 3.0;
        }

        static VehicleInput ReadInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return VehicleInput.Idle;

            double steer = 0, throttle = 0;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1;

            return new VehicleInput
            {
                Steer = steer,
                Throttle = throttle,
                Handbrake = kb.spaceKey.isPressed,
                SteerAnalog = false,   // keyboard takes the swept path (§4)
            };
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame && Stage == RunStage.Brief)
                BeginCountdown();

            if (Time.time > _thresholdUntil) ThresholdBanner = null;
            Popups.RemoveAll(p => Time.time > p.Until);

            if (Run == null || _car == null) return;

            // interpolate the render transform between fixed steps
            float alpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 1f;

            var car = Run.Car;
            double x = MathX.Lerp(car.Px, car.X, alpha);
            double y = MathX.Lerp(car.Py, car.Y, alpha);
            double h = car.Ph + MathX.AngNorm(car.H - car.Ph) * alpha;

            _car.position = WorldBuilder.ToWorld(x, y, car.Bounce * 0.25);
            _car.rotation = WorldBuilder.ToRotation(h);

            if (_carBody != null)
            {
                // cosmetic pitch and roll (§3.1) — visual only
                _carBody.localRotation = Quaternion.Euler(
                    (float)(-car.Pitch * Mathf.Rad2Deg * 6.0),
                    0f,
                    (float)(car.Roll * Mathf.Rad2Deg * 6.0));
            }
        }

        void FixedUpdate()
        {
            if (Run == null) return;

            if (Stage == RunStage.Countdown)
            {
                CountdownT -= Time.fixedDeltaTime;
                if (CountdownT <= 0) Stage = RunStage.Driving;
                return;
            }

            if (Stage != RunStage.Driving) return;

            Run.Step(Time.fixedDeltaTime, ReadInput());
        }
    }
}
