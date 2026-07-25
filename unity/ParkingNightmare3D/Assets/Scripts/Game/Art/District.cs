using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;

namespace PN3D.Game.Art
{
    /// <summary>
    /// One district's look, loaded from <c>design-spec/data/districts.json</c>.
    ///
    /// The palette is data, not code, for the same reason it is on the web: six districts
    /// share one set of painters and differ only by these values. Mission 1 is district 0,
    /// SLEEPY SUBURBS.
    /// </summary>
    public sealed class District
    {
        public string Name, Tag;
        public Color SkyTop, SkyMid, SkyHorizon;
        public Color Fog;
        public float FogFar;
        public Color HemiSky, HemiGround;
        public float HemiIntensity;
        public Color SunColor;
        public float SunIntensity;
        public Vector3 SunDir;              // as authored, in the web build's axes
        public Color GroundA, GroundB;
        public bool Night;
        public string[] WallHex;
        public Color Window;
        public bool Houses, Birds;
        public float TreeEvery, LampEvery;

        static Color Hex(string s) => ColorUtility.TryParseHtmlString(s, out var c) ? c : Color.magenta;

        static Color HexNum(JsonValue v)
        {
            // hemi/sun colours are exported as 0xRRGGBB numbers, walls as "#rrggbb"
            if (v.Kind == JsonKind.String) return Hex(v.AsString);
            int n = (int)v.AsDouble;
            return new Color(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f);
        }

        public static District Load(string json, int index)
        {
            var root = JsonValue.Parse(json);
            // extract_spec.js writes districts as an object keyed "0".."5"; tolerate a
            // plain array too, so a future re-export cannot silently break the look
            var d = root.Kind == JsonKind.Array ? root[index] : root[index.ToString()];

            var sky = d["sky"];
            var hemi = d["hemi"];
            var sun = d["sun"];
            var sunDir = sun[2];
            var ground = d["ground"];

            var walls = new List<string>();
            var bw = d["bWall"];
            for (int i = 0; i < bw.Count; i++) walls.Add(bw[i].AsString);

            return new District
            {
                Name = d["name"].AsString,
                Tag = d["tag"].AsString,
                SkyTop = Hex(sky[0].AsString),
                SkyMid = Hex(sky[1].AsString),
                SkyHorizon = Hex(sky[2].AsString),
                Fog = Hex(d["fog"].AsString),
                FogFar = (float)d.DoubleOr("fogFar", 300),
                HemiSky = HexNum(hemi[0]),
                HemiGround = HexNum(hemi[1]),
                HemiIntensity = (float)hemi[2].AsDouble,
                SunColor = HexNum(sun[0]),
                SunIntensity = (float)sun[1].AsDouble,
                SunDir = new Vector3((float)sunDir[0].AsDouble, (float)sunDir[1].AsDouble,
                                     (float)sunDir[2].AsDouble),
                GroundA = Hex(ground[0].AsString),
                GroundB = Hex(ground[1].AsString),
                Night = d.BoolOr("night", false),
                WallHex = walls.ToArray(),
                Window = Hex(d["bWin"].AsString),
                Houses = d.BoolOr("houses", false),
                Birds = d.BoolOr("birds", false),
                TreeEvery = (float)d.DoubleOr("treeEvery", 0),
                LampEvery = (float)d.DoubleOr("lampEvery", 0),
            };
        }

        public string GroundAHex => "#" + ColorUtility.ToHtmlStringRGB(GroundA);
        public string GroundBHex => "#" + ColorUtility.ToHtmlStringRGB(GroundB);
    }
}
