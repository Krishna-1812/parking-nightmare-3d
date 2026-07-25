using System;

namespace PN3D.Core
{
    /// <summary>
    /// The reward channel (DESIGN_SPEC §10). Port of <c>Game.addStyle</c>, src/n3_e.js:671.
    ///
    /// Awards inside 4 s of the last one build a combo; the multiplier is
    /// min(4, combo). Capped at 800 when it reaches scoring, but accumulates uncapped.
    /// </summary>
    public sealed class StyleSystem
    {
        public double Style;
        public int Combo;
        public double ComboT;

        /// <summary>Fired with (label, awardedTotal, multiplier) for the combo popup.</summary>
        public event Action<string, int, int> OnAward;

        public const double SmoothBase = 20.0;
        public const double OvertakeBase = 30.0;
        public const double NearMissBase = 50.0;

        /// <summary>Style is only earned while driving — not during the parking phases.</summary>
        public void Add(double amt, GamePhase phase, string label)
        {
            if (phase != GamePhase.Drive) return;

            ComboT = 4.0;
            Combo++;
            int mult = Math.Min(4, Combo);
            double total = amt * mult;
            Style += total;
            OnAward?.Invoke(label, (int)total, mult);
        }

        public void Tick(double dt)
        {
            if (ComboT > 0.0)
            {
                ComboT -= dt;
                if (ComboT <= 0.0) Combo = 0;
            }
        }

        public void Smooth(GamePhase phase) => Add(SmoothBase, phase, "SMOOTH");
        public void Overtake(GamePhase phase) => Add(OvertakeBase, phase, "OVERTAKE!");
        public void NearMiss(GamePhase phase) => Add(NearMissBase, phase, "CLOSE ONE!");

        /// <summary>Multiplier the HUD shows, or 1 when no combo is running.</summary>
        public int DisplayMultiplier => Math.Min(4, Math.Max(1, Combo));
    }
}
