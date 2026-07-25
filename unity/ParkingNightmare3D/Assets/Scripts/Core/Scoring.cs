using System;
using System.Collections.Generic;

namespace PN3D.Core
{
    public struct ScoreLine
    {
        public string Label;
        public int Value;
        public ScoreLine(string label, int value) { Label = label; Value = value; }
    }

    public sealed class ScoreResult
    {
        public int TimeScore, StyleScore, ParkScore, DmgScore, ShameScore, CleanBonus;
        public int Total;
        public int Stars;
        public bool SRank;
        public bool Perfect;
        public int Coins;
        public double Time;
        public double AngDeg;
        public double CurbGap;
        public bool HasCurbGap;
        public List<ScoreLine> Lines = new List<ScoreLine>();
    }

    /// <summary>
    /// End-of-run scoring (DESIGN_SPEC §9). Port of <c>Game.succeed</c>, src/n3_e.js:1244.
    ///
    /// Every term is rounded individually with JS semantics before being summed, and the
    /// rounding sits *inside* the min/max clamps — `min(600, round(d * 8))`, not
    /// `round(min(600, d * 8))`. Rounding at the end instead would shift totals by a
    /// point or two and can flip a star threshold.
    /// </summary>
    public static class Scoring
    {
        public static ScoreResult Compute(
            double par, double elapsed, double style,
            double angDeg, double curbGap, bool hasCurbGap,
            double damage, double shame, int collisions,
            int s2, int s3,
            bool freeRoam = false, int roamCoins = 0)
        {
            double timeD = par - elapsed;
            int timeScore = timeD >= 0
                ? (int)Math.Min(600.0, MathX.JsRound(timeD * 8.0))
                : (int)Math.Max(-400.0, MathX.JsRound(timeD * 4.0));

            int styleScore = (int)Math.Min(800.0, MathX.JsRound(style));

            int parkScore = 700;
            parkScore += (int)MathX.JsRound(Math.Max(0.0, 8.0 - angDeg) * 25.0);
            if (hasCurbGap)
                parkScore += (int)MathX.JsRound(MathX.Clamp((0.4 - curbGap) / 0.4, 0.0, 1.0) * 250.0);

            int dmgScore = -(int)MathX.JsRound(damage * 4.0);
            int shameScore = -(int)MathX.JsRound(shame * 6.0);
            int cleanBonus = collisions == 0 ? 250 : 0;

            int total = Math.Max(0, timeScore + styleScore + parkScore + dmgScore + shameScore + cleanBonus);
            int stars = total >= s3 ? 3 : (total >= s2 ? 2 : 1);
            bool sRank = total >= s3 + 350 && collisions == 0 && shame < 25.0;
            bool perfect = angDeg < 2.0 && (!hasCurbGap || curbGap < 0.15);

            int coins = freeRoam
                ? roamCoins + 50 + (perfect ? 50 : 0)
                : Math.Max(25, (int)MathX.JsRound(total / 12.0)) + stars * 25 + (sRank ? 100 : 0);

            var r = new ScoreResult
            {
                TimeScore = timeScore, StyleScore = styleScore, ParkScore = parkScore,
                DmgScore = dmgScore, ShameScore = shameScore, CleanBonus = cleanBonus,
                Total = total, Stars = stars, SRank = sRank, Perfect = perfect, Coins = coins,
                Time = elapsed, AngDeg = angDeg, CurbGap = curbGap, HasCurbGap = hasCurbGap,
            };

            r.Lines.Add(new ScoreLine(freeRoam
                ? "Road coins scooped"
                : $"Time {FmtTime(elapsed)} (par {FmtTime(par)})", freeRoam ? roamCoins : timeScore));
            r.Lines.Add(new ScoreLine("Style points", styleScore));
            r.Lines.Add(new ScoreLine("Parking precision", parkScore));
            r.Lines.Add(new ScoreLine("Damage", dmgScore));
            r.Lines.Add(new ScoreLine("Shame", shameScore));
            r.Lines.Add(new ScoreLine("Clean driving bonus", cleanBonus));
            return r;
        }

        /// <summary>Port of <c>fmtTime</c>, src/n3_b.js:24 — "m:ss.t".</summary>
        public static string FmtTime(double seconds)
        {
            double s = Math.Max(0.0, seconds);
            int m = (int)Math.Floor(s / 60.0);
            int sec = (int)Math.Floor(s % 60.0);
            int tenth = (int)Math.Floor((s % 1.0) * 10.0);
            return $"{m}:{sec:00}.{tenth}";
        }
    }
}
