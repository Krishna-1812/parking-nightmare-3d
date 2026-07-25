using System.Collections.Generic;

namespace PN3D.Core
{
    /// <summary>
    /// Handling constants for one vehicle, loaded from design-spec/data/vehicles.json.
    /// DESIGN_SPEC §3.4.
    /// </summary>
    public sealed class VehicleDef
    {
        public string Key;
        public string Name;
        public string Drive;      // "car" | "tank" | "ufo"
        public string Flavor;

        public double Len;        // metres
        public double Wid;
        public double Hgt;
        public double Wb;         // wheelbase

        public double MaxSpeed;   // m/s
        public double Accel;
        public double SteerSpeed; // steering rack rate, rad/s
        public double Grip;
        public double Mass;
        public double Fragility;  // scales damage taken

        // Livery. Presentation, but it is authored in vehicles.json alongside the handling
        // constants, so it is parsed here rather than duplicated in a second table that
        // could drift. Plain strings, so Core stays engine-free.
        public string BodyHex;
        public string RoofHex;

        public static VehicleDef FromJson(string key, JsonValue j) => new VehicleDef
        {
            Key = key,
            Name = j.OptString("name"),
            Drive = j.OptString("drive"),
            Flavor = j.OptString("flavor"),
            Len = j.DoubleOr("len", 0),
            Wid = j.DoubleOr("wid", 0),
            Hgt = j.DoubleOr("hgt", 0),
            Wb = j.DoubleOr("wb", 0),
            MaxSpeed = j.DoubleOr("maxSpeed", 0),
            Accel = j.DoubleOr("accel", 0),
            SteerSpeed = j.DoubleOr("steerSpeed", 0),
            Grip = j.DoubleOr("grip", 0),
            Mass = j.DoubleOr("mass", 1),
            Fragility = j.DoubleOr("fragility", 1),
            BodyHex = j.OptString("body"),
            RoofHex = j.OptString("roof"),
        };

        /// <summary>vehicles.json is an object keyed by vehicle id, not an array.</summary>
        public static Dictionary<string, VehicleDef> ParseAll(string json)
        {
            var root = JsonValue.Parse(json);
            var map = new Dictionary<string, VehicleDef>();
            foreach (var kv in root.Members) map[kv.Key] = FromJson(kv.Key, kv.Value);
            return map;
        }
    }
}
