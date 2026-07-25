using System;
using System.Collections.Generic;

namespace PN3D.Core
{
    /// <summary>
    /// The fail condition and the emotional core (DESIGN_SPEC §10).
    /// Ports <c>Game.addShame</c> / <c>checkThresholds</c>, src/n3_e.js:650.
    ///
    /// Range 0-100; at 100 the run fails instantly regardless of damage or position.
    /// Shame is the real threat and damage is cosmetic pressure — keep that asymmetry,
    /// it is the design.
    /// </summary>
    public sealed class ShameSystem
    {
        public double Shame;

        /// <summary>Seconds since the last shame event. Decay only starts after 6.</summary>
        public double CalmT;

        /// <summary>Counts down from 3 after any shame; drives the HUD's agitated state.</summary>
        public double RecentShameT;

        public bool Failed;

        /// <summary>Total shame accrued over the run, for end-of-run stats.</summary>
        public double TotalAccrued;

        readonly HashSet<int> _thresholdsHit = new HashSet<int>();

        /// <summary>Fired with (label, colour) when an event wants a comic popup.</summary>
        public event Action<string, string> OnPopup;

        /// <summary>Fired once each at 25 / 50 / 75 with the banner text.</summary>
        public event Action<int, string> OnThreshold;

        public event Action OnFail;

        static readonly (int Pct, string Msg)[] Marks =
        {
            (25, "PEOPLE ARE STARING"),
            (50, "SOMEONE IS FILMING"),
            (75, "A CROWD GATHERS"),
        };

        public bool ThresholdHit(int pct) => _thresholdsHit.Contains(pct);

        /// <summary>
        /// The HUD face, which tracks shame: slightly nervous, then alarmed, then clown.
        /// </summary>
        public string Face => Shame < 25 ? "\U0001F642"
                            : Shame < 50 ? "\U0001F62C"
                            : Shame < 75 ? "\U0001F630"
                            : "\U0001F921";

        public bool Pulsing => Shame >= 75.0;

        /// <summary>
        /// Add shame. Ignored outside Drive/Park/Settle, which is what stops the results
        /// screen and the fail sequence from accruing more.
        /// </summary>
        public void Add(double amt, GamePhase phase, string label = null, string color = null)
        {
            if (phase != GamePhase.Drive && phase != GamePhase.Park && phase != GamePhase.Settle)
                return;

            Shame = MathX.Clamp(Shame + amt, 0.0, 100.0);
            TotalAccrued += Math.Max(0.0, amt);
            RecentShameT = 3.0;
            CalmT = 0.0;

            if (!string.IsNullOrEmpty(label))
                OnPopup?.Invoke(label, color ?? "#ff6b57");

            CheckThresholds();

            if (Shame >= 100.0 && !Failed)
            {
                Failed = true;
                OnFail?.Invoke();
            }
        }

        void CheckThresholds()
        {
            foreach (var m in Marks)
            {
                if (Shame >= m.Pct && _thresholdsHit.Add(m.Pct))
                    OnThreshold?.Invoke(m.Pct, m.Msg);
            }
        }

        /// <summary>
        /// Per-step timers. Decay is 0.5/s but only after 6 continuous seconds of calm,
        /// and only while driving — composing yourself takes visible effort, which is
        /// the joke.
        /// </summary>
        public void Tick(double dt, GamePhase phase)
        {
            if (RecentShameT > 0.0) RecentShameT -= dt;
            CalmT += dt;
            if (CalmT > 6.0 && Shame > 0.0 && phase == GamePhase.Drive)
                Shame = Math.Max(0.0, Shame - 0.5 * dt);
        }

        // ---- one-shot event helpers, so callers do not re-derive the constants ----

        public void Collision(double severity, GamePhase phase, CollisionKind kind, bool isTank)
        {
            double amt = 5.0 + severity * 9.0;
            string label = "BONK!";
            if (kind == CollisionKind.Traffic) { label = "CRUNCH!"; amt += 3.0; }
            if (kind == CollisionKind.Precious) { label = "THE FERRARI!!"; amt += 14.0; }
            if (kind == CollisionKind.Hydrant) { label = "GEYSER!"; }
            if (isTank) { amt *= 1.6; label = "TANK!!"; }
            Add(amt, phase, label);
        }

        public void CurbMount(GamePhase phase) => Add(4.0, phase, "CURB CHECK!", "#ff8f5e");
        public void CurbDismount(GamePhase phase) => Add(1.5, phase);
        public void PedDive(GamePhase phase) => Add(12.0, phase, "SO CLOSE!!", "#ff4757");
        public void RanRed(GamePhase phase) => Add(10.0, phase, "RAN THE RED!", "#ff4757");
        public void SoakedPed(double amount01, GamePhase phase) => Add(8.0 * amount01, phase, "SOAKED THEM!", "#3aa6ff");
        public void BusArm(GamePhase phase) => Add(5.0, phase, "THE ARM!!", "#ffc23e");
        public void Mirror(GamePhase phase) => Add(5.0, phase, "MIRROR!", "#ffc23e");
        public void Airborne(GamePhase phase) => Add(2.5, phase, "AIRBORNE!", "#ffc23e");
        public void PropBonk(GamePhase phase) => Add(2.0, phase, "BONK!", "#f28b30");
        public void Pothole(GamePhase phase) => Add(1.5, phase, "POTHOLE!", "#8891a5");
        public void Filmed(GamePhase phase) => Add(2.0, phase);
        public void OwnHorn(GamePhase phase) => Add(2.0, phase);
        public void TrafficHonk(GamePhase phase) => Add(1.2, phase);
    }

    public enum CollisionKind { Prop, Traffic, Precious, Hydrant }
}
