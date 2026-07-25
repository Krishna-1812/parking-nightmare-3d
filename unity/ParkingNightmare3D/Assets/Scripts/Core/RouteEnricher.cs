using System.Collections.Generic;

namespace PN3D.Core
{
    /// <summary>
    /// Difficulty pass applied to every hand-authored campaign mission at load
    /// (port of <c>enrichRoute</c>, src/n3_d.js:105; the web build calls it via
    /// <c>LEVELS.forEach(enrichRoute)</c> at src/n3_d.js:424).
    ///
    /// It sharpens every curve, weaves chicane switchbacks into long straights, and
    /// stretches the final approach so the parking spot sits further down the road.
    /// Par is rescaled by the length gain plus a small penalty per added turn.
    ///
    /// This is NOT optional cosmetic polish — it is what the shipped game actually
    /// plays, and design-spec/data/missions.json holds the pre-enrichment authored
    /// values. Compiling missions.json directly gives the wrong route and a par time
    /// 20-30% too tight on 23 of the 24 missions.
    /// </summary>
    public static class RouteEnricher
    {
        /// <summary>
        /// Mutates <paramref name="level"/> in place and returns it. Idempotent, and a
        /// no-op for tutorial missions (mission 1 is the only one, and it is therefore
        /// the only mission whose authored par is also its real par).
        /// </summary>
        public static Mission Enrich(Mission level)
        {
            if (level == null || level.Segs == null || level.Enriched || level.Tutorial)
                return level;

            var src = level.Segs;
            var outSegs = new List<RouteSeg>();
            int last = src.Count - 1;
            double oldLen = 0.0, newLen = 0.0;
            int added = 0;

            for (int i = 0; i < src.Count; i++)
            {
                var sg = src[i];
                oldLen += sg.Length();

                if (sg.IsCurve)
                {
                    // tighten the radius (smaller r = sharper); nudge lazy 45s up to 60
                    double nr = System.Math.Max(20.0, MathX.JsRound(MathX.Or(sg.R, 34.0) * 0.6));
                    double na = MathX.Or(sg.A, 90.0) <= 45.0 ? 60.0 : MathX.Or(sg.A, 90.0);
                    var ns = new RouteSeg(sg.T, null, nr, na);
                    outSegs.Add(ns);
                    newLen += ns.Length();
                }
                else if (sg.T == "X")
                {
                    outSegs.Add(sg);
                    newLen += sg.Length();
                }
                else // straight
                {
                    double len = sg.Len ?? 0.0;
                    if (i == last)
                    {
                        // longer run-in to the parking destination
                        var ns = new RouteSeg("S", len + 80.0);
                        outSegs.Add(ns);
                        newLen += ns.Len.Value;
                    }
                    else if (i != 0 && len >= 105.0)
                    {
                        // carve a long straight into a sharp S-bend chicane (net heading kept)
                        double a = len / 3.2;
                        string d1 = (i % 2 == 0) ? "L" : "R";
                        string d2 = d1 == "L" ? "R" : "L";
                        var parts = new[]
                        {
                            new RouteSeg("S", a * 1.25),
                            new RouteSeg(d1, null, 24.0, 45.0),
                            new RouteSeg("S", a * 0.9),
                            new RouteSeg(d2, null, 24.0, 45.0),
                            new RouteSeg("S", a * 1.25),
                        };
                        // NOTE (faithful port of a reference-implementation bug): the
                        // rebuilt straights do not carry sg.Zone across, so the
                        // `zone: "school"` authored on missions 3, 12, 18 and 20 is
                        // discarded here and in the two branches around it. Every
                        // compiled route therefore has an empty zone list, and the
                        // school-zone shame rule (DESIGN_SPEC §10, 2.2/s) never fires
                        // in the shipped game. Preserved deliberately so the Unity
                        // build matches; carrying Zone across is a one-word change.
                        foreach (var p in parts) { outSegs.Add(p); newLen += p.Length(); }
                        added += 2;
                    }
                    else
                    {
                        var ns = new RouteSeg("S", len * 1.22);
                        outSegs.Add(ns);
                        newLen += ns.Len.Value;
                    }
                }
            }

            level.Segs = outSegs;
            if (!double.IsNaN(level.Par) && !double.IsInfinity(level.Par) && level.Par < 9000.0)
                level.Par = MathX.JsRound(level.Par * (newLen / System.Math.Max(1.0, oldLen)) + added * 3.0);
            level.Enriched = true;
            return level;
        }
    }
}
