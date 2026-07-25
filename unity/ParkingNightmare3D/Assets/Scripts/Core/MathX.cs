using System;

namespace PN3D.Core
{
    /// <summary>
    /// JavaScript-faithful math helpers.
    ///
    /// The web build (src/n3_b.js) is the reference implementation; these mirror its
    /// helpers exactly so the ported route compiler and physics reproduce the tuned
    /// numbers bit-for-bit rather than approximately.
    /// </summary>
    public static class MathX
    {
        public const double Tau = Math.PI * 2.0;

        /// <summary>
        /// JS <c>Math.round</c> is <c>floor(x + 0.5)</c> — it rounds .5 upward (toward
        /// +inf), unlike .NET's <see cref="Math.Round(double)"/>, which rounds half to
        /// even. Segment subdivision counts and the enriched par times both depend on
        /// this, so using the .NET default would silently produce different routes.
        /// </summary>
        public static double JsRound(double v) => Math.Floor(v + 0.5);

        public static int JsRoundInt(double v) => (int)Math.Floor(v + 0.5);

        public static double Clamp(double v, double a, double b) => v < a ? a : (v > b ? b : v);

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double Dist2(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return dx * dx + dy * dy;
        }

        /// <summary>Wrap an angle into (-pi, pi].</summary>
        public static double AngNorm(double a)
        {
            while (a > Math.PI) a -= Tau;
            while (a < -Math.PI) a += Tau;
            return a;
        }

        public static double Rad(double deg) => deg * Math.PI / 180.0;

        /// <summary>
        /// JS <c>value || fallback</c> for numbers: absent, zero and NaN all fall
        /// through to the fallback. The route DSL relies on this for its defaults
        /// (<c>sg.r || 34</c>, <c>sg.a || 90</c>, <c>sg.w || 26</c>).
        /// </summary>
        public static double Or(double? v, double fallback)
            => v.HasValue && v.Value != 0.0 && !double.IsNaN(v.Value) ? v.Value : fallback;
    }
}
