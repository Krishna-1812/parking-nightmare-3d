using System;
using UnityEngine;

namespace PN3D.Game.Art
{
    /// <summary>Headlight signature — the strongest single "which brand is that" cue.</summary>
    public enum HeadSig { Pods, Slim, Quad }

    /// <summary>Tail signature.</summary>
    public enum TailSig { Pods, Bar, LShape }

    public enum GrilleKind { None, Wide, Tall, Twin }

    /// <summary>
    /// Everything that makes one car body look like a different car from another, with no
    /// bearing whatsoever on the simulation.
    ///
    /// WHY THIS IS PRESENTATION-ONLY. Length and width come from the caller, never from
    /// here, because those two numbers are simulation state: TrafficSystem sizes gaps and
    /// collisions from them and vehicles.json feeds the par times. A style may change how
    /// tall a roof is or where a light sits; it may not change the box the physics tests.
    ///
    /// ON RESEMBLANCE. These are class archetypes — the big executive saloon, the fastback
    /// coupe, the luxury SUV — not copies of specific cars. What actually makes a player
    /// say "that's a 3-series" is proportion, stance, roofline and light signature, and all
    /// four are free to use. Manufacturer grille shapes, badges and model names are not,
    /// and a storefront listing is precisely where that gets noticed, so none appear here.
    /// </summary>
    public sealed class CarStyle
    {
        public string Key;
        public string Label;

        // ---- stance ----
        public float BodyH = 0.62f;      // painted shell height, metres
        public float WheelR = 0.34f;     // tyre radius, metres
        public float WheelWFrac = 0.70f; // tyre width as a fraction of radius
        public int Spokes = 5;

        // ---- greenhouse ----
        public float CabHeight = 0.55f;
        public float CabLenFrac = 0.50f;
        public float CabOffFrac = -0.06f;   // + is toward the nose
        public float CabWidFrac = 0.86f;

        // ---- painted shell shaping ----
        public float PCross = 3.4f, PPlan = 5.5f, Tumble = 0.10f;
        public float WNose = 0.86f, WTail = 0.92f;
        public float BonnetDrop = 0.15f, BootDrop = 0.08f;
        public float SillFront = 0.08f, SillRear = 0.10f;

        // ---- roofline ----
        public float RoofPeak = 0.45f;   // 0 = tail, 1 = nose
        public float RoofFlat = 0.30f;   // width of the flat plateau either side of the peak
        public float RoofNose = 0.12f;   // height the glass falls to at the windscreen
        public float RoofTail = 0.20f;   // ...and at the backlight
        public float CabTumble = 0.32f, CabPCross = 2.7f, CabPPlan = 3.4f;

        // ---- identity ----
        public HeadSig Head = HeadSig.Pods;
        public TailSig Tail = TailSig.Pods;
        public GrilleKind Grille = GrilleKind.Wide;

        public bool RoofRails, Spoiler, Cladding, LightBar, TaxiSign, Rust, TwinExhaust = true;

        /// <summary>
        /// Exposed black bumper bars. Off by default: since the nineties bumpers have been
        /// body-coloured and moulded into the shell, and a dark cylinder slung across a
        /// rounded nose reads as a detached scaffolding pole rather than a bumper. Kept for
        /// the archetypes where it is period-correct — the wreck and the work van.
        /// </summary>
        public bool Bumpers;

        /// <summary>
        /// Paint finish. Per-style because one gloss level for the whole fleet is wrong in
        /// both directions: it makes an executive saloon look cheap and it makes a wreck
        /// look freshly detailed. It also has a practical effect — a very smooth surface
        /// goes fully reflective at grazing angles, so at high smoothness the flanks mirror
        /// the sky and the car's form washes out to white against the horizon.
        /// </summary>
        public float PaintMetallic = 0.22f, PaintSmooth = 0.74f;

        public Func<float, float> Deck() => CarStyles.Deck(BonnetDrop, BootDrop);
        public Func<float, float> Sill() => CarStyles.Sill(SillRear, SillFront);
        public Func<float, float> Roof() => CarStyles.Roof(RoofPeak, RoofFlat, RoofNose, RoofTail);
    }

    /// <summary>The archetype table, plus the profile curves the hull generator eats.</summary>
    public static class CarStyles
    {
        public static float S3(float a, float b, float t)
        {
            t = Mathf.Clamp01((t - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Bonnet and boot line of the painted shell. u is 0 at the tail, 1 at the nose.
        /// A long bonnet drop with almost no boot drop is what reads as rear-wheel-drive
        /// executive; the reverse reads as a cheap front-drive hatch.
        /// </summary>
        public static Func<float, float> Deck(float bonnet, float boot)
            => u => 1f - bonnet * S3(0.62f, 0.98f, u) - boot * (1f - S3(0.03f, 0.26f, u));

        /// <summary>Sill line, lifted at both ends so the valances tuck under.</summary>
        public static Func<float, float> Sill(float rear, float front)
            => u => rear * S3(0.82f, 1f, u) + front * (1f - S3(0f, 0.14f, u));

        /// <summary>
        /// Roofline as a plateau with a smooth fall to the windscreen and the backlight.
        ///
        /// This one function carries most of the silhouette. peak is where the roof is
        /// highest, flat how much of it is level, and the two end values how far the glass
        /// rakes away. A saloon has a short plateau falling hard both ways; an estate holds
        /// the plateau almost to the tail; a fastback drops the tail end nearly to nothing
        /// so the backlight runs out into the boot in one line.
        /// </summary>
        public static Func<float, float> Roof(float peak, float flat, float nose, float tail)
            => u =>
            {
                float span = Mathf.Max(1e-3f, u < peak ? peak : 1f - peak);
                float d = Mathf.Abs(u - peak) / span;                       // 0 at peak, 1 at the end
                float t = Mathf.Clamp01((d - flat) / Mathf.Max(1e-3f, 1f - flat));
                return Mathf.Lerp(1f, u < peak ? tail : nose, t * t * (3f - 2f * t));
            };

        // ------------------------------------------------------------------ archetypes

        public static readonly CarStyle Exec = new CarStyle
        {
            Key = "exec", PaintMetallic = 0.30f, PaintSmooth = 0.88f, Label = "executive saloon",
            BodyH = 0.60f, WheelR = 0.335f, Spokes = 10,
            CabHeight = 0.52f, CabLenFrac = 0.46f, CabOffFrac = -0.04f, CabWidFrac = 0.86f,
            PCross = 3.6f, PPlan = 6.0f, Tumble = 0.13f, WNose = 0.88f, WTail = 0.90f,
            BonnetDrop = 0.19f, BootDrop = 0.05f,
            RoofPeak = 0.44f, RoofFlat = 0.24f, RoofNose = 0.08f, RoofTail = 0.12f,
            Head = HeadSig.Slim, Tail = TailSig.LShape, Grille = GrilleKind.Wide,
        };

        public static readonly CarStyle Coupe = new CarStyle
        {
            Key = "coupe", PaintMetallic = 0.32f, PaintSmooth = 0.90f, Label = "fastback coupe",
            BodyH = 0.56f, WheelR = 0.35f, Spokes = 5, WheelWFrac = 0.78f,
            CabHeight = 0.46f, CabLenFrac = 0.46f, CabOffFrac = -0.01f, CabWidFrac = 0.84f,
            PCross = 3.0f, PPlan = 5.0f, Tumble = 0.20f, WNose = 0.84f, WTail = 0.87f,
            BonnetDrop = 0.21f, BootDrop = 0.09f,
            RoofPeak = 0.54f, RoofFlat = 0.08f, RoofNose = 0.10f, RoofTail = 0.02f,
            Head = HeadSig.Slim, Tail = TailSig.Bar, Grille = GrilleKind.Wide,
            Spoiler = true,
        };

        public static readonly CarStyle Hatch = new CarStyle
        {
            Key = "hatch", PaintMetallic = 0.20f, PaintSmooth = 0.72f, Label = "hot hatch",
            BodyH = 0.62f, WheelR = 0.32f, Spokes = 5,
            CabHeight = 0.58f, CabLenFrac = 0.52f, CabOffFrac = -0.06f,
            PCross = 3.4f, PPlan = 5.5f, Tumble = 0.10f,
            BonnetDrop = 0.15f, BootDrop = 0.08f,
            RoofPeak = 0.44f, RoofFlat = 0.34f, RoofNose = 0.12f, RoofTail = 0.34f,
            Head = HeadSig.Pods, Tail = TailSig.Pods, Grille = GrilleKind.Wide,
        };

        public static readonly CarStyle Suv = new CarStyle
        {
            Key = "suv", PaintMetallic = 0.24f, PaintSmooth = 0.78f, Label = "luxury SUV",
            BodyH = 0.86f, WheelR = 0.40f, Spokes = 6, WheelWFrac = 0.72f,
            CabHeight = 0.62f, CabLenFrac = 0.54f, CabOffFrac = -0.04f, CabWidFrac = 0.88f,
            PCross = 4.6f, PPlan = 7.0f, Tumble = 0.06f, WNose = 0.92f, WTail = 0.95f,
            BonnetDrop = 0.10f, BootDrop = 0.04f, SillFront = 0.05f, SillRear = 0.06f,
            RoofPeak = 0.44f, RoofFlat = 0.56f, RoofNose = 0.14f, RoofTail = 0.26f,
            CabTumble = 0.18f, CabPCross = 3.6f, CabPPlan = 4.4f,
            Head = HeadSig.Quad, Tail = TailSig.Bar, Grille = GrilleKind.Tall,
            RoofRails = true, Cladding = true,
        };

        public static readonly CarStyle Wagon = new CarStyle
        {
            Key = "wagon", PaintMetallic = 0.24f, PaintSmooth = 0.80f, Label = "estate",
            BodyH = 0.62f, WheelR = 0.34f, Spokes = 5,
            CabHeight = 0.56f, CabLenFrac = 0.60f, CabOffFrac = -0.10f, CabWidFrac = 0.87f,
            PCross = 3.8f, PPlan = 6.0f, Tumble = 0.09f,
            BonnetDrop = 0.16f, BootDrop = 0.03f,
            RoofPeak = 0.38f, RoofFlat = 0.58f, RoofNose = 0.11f, RoofTail = 0.52f,
            Head = HeadSig.Slim, Tail = TailSig.LShape, Grille = GrilleKind.Wide,
            RoofRails = true,
        };

        public static readonly CarStyle Taxi = new CarStyle
        {
            Key = "taxi", PaintMetallic = 0.14f, PaintSmooth = 0.62f, Label = "taxi",
            BodyH = 0.63f, WheelR = 0.335f, Spokes = 6,
            CabHeight = 0.57f, CabLenFrac = 0.50f, CabOffFrac = -0.05f,
            PCross = 3.6f, PPlan = 5.8f, Tumble = 0.10f,
            BonnetDrop = 0.15f, BootDrop = 0.07f,
            RoofPeak = 0.44f, RoofFlat = 0.30f, RoofNose = 0.11f, RoofTail = 0.18f,
            Head = HeadSig.Pods, Tail = TailSig.Pods, Grille = GrilleKind.Wide,
            TaxiSign = true,
        };

        public static readonly CarStyle Police = new CarStyle
        {
            Key = "police", PaintMetallic = 0.16f, PaintSmooth = 0.70f, Label = "patrol car",
            BodyH = 0.61f, WheelR = 0.345f, Spokes = 5,
            CabHeight = 0.54f, CabLenFrac = 0.48f, CabOffFrac = -0.05f,
            PCross = 3.6f, PPlan = 5.8f, Tumble = 0.11f,
            BonnetDrop = 0.17f, BootDrop = 0.06f,
            RoofPeak = 0.44f, RoofFlat = 0.28f, RoofNose = 0.10f, RoofTail = 0.15f,
            Head = HeadSig.Quad, Tail = TailSig.Bar, Grille = GrilleKind.Wide,
            LightBar = true,
        };

        /// <summary>Box van. The cab is a short bubble pushed right to the nose.</summary>
        public static readonly CarStyle Van = new CarStyle
        {
            Key = "van", PaintMetallic = 0.10f, PaintSmooth = 0.52f, Label = "delivery van",
            BodyH = 1.55f, WheelR = 0.44f, Spokes = 6, WheelWFrac = 0.62f,
            CabHeight = 0.55f, CabLenFrac = 0.30f, CabOffFrac = 0.28f, CabWidFrac = 0.90f,
            PCross = 6.0f, PPlan = 8.0f, Tumble = 0.03f, WNose = 0.94f, WTail = 0.99f,
            BonnetDrop = 0.06f, BootDrop = 0.01f, SillFront = 0.04f, SillRear = 0.04f,
            RoofPeak = 0.40f, RoofFlat = 0.70f, RoofNose = 0.30f, RoofTail = 0.85f,
            CabTumble = 0.10f, CabPCross = 4.0f, CabPPlan = 4.0f,
            Head = HeadSig.Pods, Tail = TailSig.Bar, Grille = GrilleKind.Tall,
            TwinExhaust = false, Bumpers = true,
        };

        public static readonly CarStyle Limo = new CarStyle
        {
            Key = "limo", PaintMetallic = 0.34f, PaintSmooth = 0.91f, Label = "stretch limousine",
            BodyH = 0.60f, WheelR = 0.34f, Spokes = 10,
            CabHeight = 0.54f, CabLenFrac = 0.64f, CabOffFrac = -0.04f, CabWidFrac = 0.88f,
            PCross = 4.0f, PPlan = 7.0f, Tumble = 0.08f, WNose = 0.92f, WTail = 0.94f,
            BonnetDrop = 0.14f, BootDrop = 0.05f,
            RoofPeak = 0.46f, RoofFlat = 0.66f, RoofNose = 0.10f, RoofTail = 0.14f,
            Head = HeadSig.Quad, Tail = TailSig.Bar, Grille = GrilleKind.Tall,
        };

        /// <summary>Lifted pickup: tall shell, cab forward, open bed behind.</summary>
        public static readonly CarStyle Pickup = new CarStyle
        {
            Key = "pickup", PaintMetallic = 0.18f, PaintSmooth = 0.66f, Label = "lifted pickup",
            BodyH = 1.05f, WheelR = 0.60f, Spokes = 6, WheelWFrac = 0.85f,
            CabHeight = 0.66f, CabLenFrac = 0.40f, CabOffFrac = 0.13f, CabWidFrac = 0.86f,
            PCross = 5.0f, PPlan = 7.5f, Tumble = 0.05f, WNose = 0.93f, WTail = 0.97f,
            BonnetDrop = 0.13f, BootDrop = 0.02f, SillFront = 0.04f, SillRear = 0.04f,
            RoofPeak = 0.56f, RoofFlat = 0.30f, RoofNose = 0.16f, RoofTail = 0.30f,
            CabTumble = 0.14f, CabPCross = 4.0f, CabPPlan = 4.5f,
            Head = HeadSig.Quad, Tail = TailSig.Pods, Grille = GrilleKind.Tall,
            Cladding = true,
        };

        /// <summary>The player's opener, which the flavour text promises is a wreck.</summary>
        public static readonly CarStyle RustyHatch = new CarStyle
        {
            Key = "rusty", PaintMetallic = 0.08f, PaintSmooth = 0.38f, Label = "rusty hatchback",
            BodyH = 0.62f, WheelR = 0.32f, Spokes = 5,
            CabHeight = 0.58f, CabLenFrac = 0.52f, CabOffFrac = -0.06f,
            PCross = 3.4f, PPlan = 5.5f, Tumble = 0.10f,
            BonnetDrop = 0.15f, BootDrop = 0.08f,
            RoofPeak = 0.44f, RoofFlat = 0.34f, RoofNose = 0.12f, RoofTail = 0.34f,
            Head = HeadSig.Pods, Tail = TailSig.Pods, Grille = GrilleKind.Wide,
            Rust = true, TwinExhaust = false, Bumpers = true,
        };

        public static readonly CarStyle[] All =
        {
            Exec, Coupe, Hatch, Suv, Wagon, Taxi, Police, Van, Limo, Pickup, RustyHatch,
        };

        /// <summary>
        /// Style for a traffic kind. `id` picks between the variants a kind can wear, so a
        /// street of "sedan" is not a street of one identical car — but deterministically,
        /// because the same car must look the same every frame it is pooled and re-shown.
        /// </summary>
        public static CarStyle ForTraffic(string kind, int id) => kind switch
        {
            "hatch" => Hatch,
            "suv" => (id & 1) == 0 ? Suv : Pickup,
            "taxi" => Taxi,
            "police" => Police,
            "truck" => Van,
            _ => (id % 3) switch { 0 => Exec, 1 => Coupe, _ => Wagon },
        };

        /// <summary>Style for a player vehicle key from vehicles.json.</summary>
        public static CarStyle ForVehicle(string key) => key switch
        {
            "hatch" => RustyHatch,
            "wagon" => Wagon,
            "limo" => Limo,
            "monster" => Pickup,
            "icecream" => Van,
            _ => Exec,
        };

        // ------------------------------------------------------------------ paint

        /// <summary>
        /// A real car park's worth of colours rather than a hue wheel. Traffic used to pick
        /// an arbitrary hue, which is why the road looked like a toy box: actual traffic is
        /// mostly white, black, grey and silver, with one or two saturated cars per street
        /// to keep it from going monochrome.
        /// </summary>
        static readonly Color[] Palette =
        {
            new Color(0.86f, 0.87f, 0.89f),   // pearl white
            new Color(0.86f, 0.87f, 0.89f),
            new Color(0.09f, 0.10f, 0.12f),   // jet black
            new Color(0.09f, 0.10f, 0.12f),
            new Color(0.62f, 0.65f, 0.69f),   // silver
            new Color(0.62f, 0.65f, 0.69f),
            new Color(0.28f, 0.31f, 0.35f),   // gunmetal
            new Color(0.16f, 0.24f, 0.42f),   // deep navy
            new Color(0.55f, 0.09f, 0.11f),   // burgundy
            new Color(0.72f, 0.14f, 0.10f),   // racing red
            new Color(0.13f, 0.31f, 0.24f),   // british green
            new Color(0.75f, 0.62f, 0.35f),   // champagne
        };

        public static Color PaintFor(int id)
        {
            if (id < 0) id = -id;
            return Palette[id % Palette.Length];
        }

        public static readonly Color TaxiYellow = new Color(0.94f, 0.72f, 0.09f);
        public static readonly Color PoliceWhite = new Color(0.88f, 0.89f, 0.91f);
    }
}
