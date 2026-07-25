using System.Collections.Generic;

namespace PN3D.Core
{
    /// <summary>
    /// One segment of the route DSL (DESIGN_SPEC §5).
    ///
    /// Fields are nullable because the compiler distinguishes "absent" from "zero":
    /// <c>sg.r || 34</c> in the reference implementation falls back on both.
    /// </summary>
    public sealed class RouteSeg
    {
        /// <summary>"S" straight, "L"/"R" arc, "X" intersection.</summary>
        public string T;

        public double? Len;     // S: length in metres
        public double? R;       // L/R: radius, default 34
        public double? A;       // L/R: sweep in degrees, default 90
        public double? W;       // X: width, default 26
        public bool? Lights;    // X: absent means true (`sg.lights !== false`)
        public string Zone;     // e.g. "school"

        public RouteSeg() { }

        public RouteSeg(string t, double? len = null, double? r = null, double? a = null)
        {
            T = t; Len = len; R = r; A = a;
        }

        public RouteSeg Clone() => new RouteSeg
        {
            T = T, Len = Len, R = R, A = A, W = W, Lights = Lights, Zone = Zone,
        };

        public static RouteSeg FromJson(JsonValue j) => new RouteSeg
        {
            T = j.OptString("t"),
            Len = j.OptDouble("len"),
            R = j.OptDouble("r"),
            A = j.OptDouble("a"),
            W = j.OptDouble("w"),
            Lights = j.OptBool("lights"),
            Zone = j.OptString("zone"),
        };

        /// <summary>Length in metres, matching <c>seglen()</c> in enrichRoute.</summary>
        public double Length() => T switch
        {
            "S" => Len ?? 0.0,
            "X" => MathX.Or(W, 26.0),
            _ => MathX.Or(R, 34.0) * MathX.Rad(MathX.Or(A, 90.0)),
        };

        public bool IsCurve => T == "L" || T == "R";
    }

    /// <summary>
    /// A campaign mission as authored in design-spec/data/missions.json.
    ///
    /// IMPORTANT: that file holds the *authored* (pre-enrichment) segments and par.
    /// The shipping game applies <see cref="RouteEnricher"/> to every non-tutorial
    /// mission at load, which rewrites the segments and rescales par. Always run a
    /// mission through the enricher before compiling. See DESIGN_SPEC §5.1.
    /// </summary>
    public sealed class Mission
    {
        public int Id;
        public int District;
        public string Name;
        public string Veh;
        public int Lanes;
        public string Brief;
        public double Par;
        public double Traffic;
        public int Peds;
        public bool Tutorial;

        public string Park;      // "parallel" | "bay"
        public double Margin;
        public int S2, S3;       // 2- and 3-star score thresholds

        public int Cones;        // count of cones to scatter
        public int Ice;          // count of ice patches to scatter
        public bool Rain, Snow;
        public string Time;      // "day" | "night" | "dusk"

        public List<RouteSeg> Segs = new List<RouteSeg>();

        /// <summary>Mirrors <c>level._enriched</c>; makes enrichment idempotent.</summary>
        public bool Enriched;

        public Mission Clone()
        {
            var m = (Mission)MemberwiseClone();
            m.Segs = new List<RouteSeg>(Segs.Count);
            foreach (var s in Segs) m.Segs.Add(s.Clone());
            return m;
        }

        public static Mission FromJson(JsonValue j)
        {
            var m = new Mission
            {
                Id = j.IntOr("id", 0),
                District = j.IntOr("district", 0),
                Name = j.OptString("name"),
                Veh = j.OptString("veh"),
                Lanes = j.IntOr("lanes", 1),
                Brief = j.OptString("brief"),
                Par = j.DoubleOr("par", 0.0),
                Traffic = j.DoubleOr("traffic", 0.0),
                Peds = j.IntOr("peds", 0),
                Tutorial = j.BoolOr("tutorial", false),
                Park = j.OptString("park"),
                Margin = j.DoubleOr("margin", 0.0),
                S2 = j.IntOr("s2", 0),
                S3 = j.IntOr("s3", 0),
                Cones = j.IntOr("cones", 0),
                Ice = j.IntOr("ice", 0),
                Rain = j.BoolOr("rain", false),
                Snow = j.BoolOr("snow", false),
                Time = j.OptString("time"),
            };
            var segs = j["segs"];
            if (segs != null && segs.Kind == JsonKind.Array)
                for (int i = 0; i < segs.Count; i++)
                    m.Segs.Add(RouteSeg.FromJson(segs[i]));
            return m;
        }

        public static List<Mission> ParseAll(string json)
        {
            var arr = JsonValue.Parse(json);
            var list = new List<Mission>(arr.Count);
            for (int i = 0; i < arr.Count; i++) list.Add(FromJson(arr[i]));
            return list;
        }
    }
}
