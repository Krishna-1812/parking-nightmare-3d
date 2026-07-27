using System.Collections.Generic;
using UnityEngine;
using PN3D.Core;
using RG = PN3D.Game.Art.Raster.RGBA;

namespace PN3D.Game.Art
{
    /// <summary>
    /// The surface textures, ported painter-for-painter from <c>Assets.*</c> in
    /// <c>src/n3_c.js</c>: asphalt with its lane markings baked in, curb, sidewalk, grass,
    /// shingles, siding, crosswalk.
    ///
    /// Every painter takes its randomness from a seeded <see cref="Rng"/> rather than
    /// <c>Math.random</c>, so two machines building the same commit get byte-identical
    /// textures. The reference re-rolls its noise on every page load; here the art is part
    /// of the build, and art that changes between builds cannot be reviewed in a diff.
    ///
    /// Results are cached per key for the lifetime of the domain.
    /// </summary>
    public static class ProcTex
    {
        static readonly Dictionary<string, Texture2D> Cache = new();

        static Texture2D Get(string key, System.Func<Texture2D> make)
        {
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            var made = make();
            Cache[key] = made;
            return made;
        }

        /// <summary>Stable per-texture seed, so adding a painter never reshuffles the rest.</summary>
        static Rng Seed(string key)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in key) { h ^= c; h *= 16777619u; }
                return new Rng(h);
            }
        }

        // ------------------------------------------------------------ asphalt base

        /// <summary>
        /// Believable asphalt: weathering blotches, sealant patch repairs, aggregate
        /// speckle, hairline cracks and the glossy tar crack-seal worms road crews leave.
        /// Shared by the road, plain asphalt and crosswalk textures.
        /// </summary>
        static void AsphaltBase(Raster c, Rng r, bool night)
        {
            int W = c.W, H = c.H;
            c.Clear(RG.Hex(night ? "#31343d" : "#484b53"));

            for (int i = 0; i < Mathf.RoundToInt(W * H / 9000f); i++)
            {
                float x = (float)r.Rand(0, W), y = (float)r.Rand(0, H), rad = (float)r.Rand(20, 90);
                bool dark = r.Chance(0.5);
                var g = new Raster.RadialGrad(x, y, 2, rad)
                    .Stop(0, dark ? RG.Rgba(0, 0, 0, .09f) : RG.Rgba(255, 255, 255, .05f))
                    .Stop(1, RG.Rgba(0, 0, 0, 0));
                c.FillRect(x - rad, y - rad, rad * 2, rad * 2, g);
            }

            for (int i = 0; i < Mathf.RoundToInt(W * H / 300000f); i++)
            {
                float x = (float)r.Rand(0, W * 0.8), y = (float)r.Rand(0, H * 0.8);
                float pw = (float)r.Rand(60, 170), ph = (float)r.Rand(50, 130);
                c.FillRect(x, y, pw, ph, RG.Rgba(0, 0, 0, .12f));
                c.StrokeRect(x, y, pw, ph, 3, RG.Rgba(0, 0, 0, .22f));
                c.StrokeRect(x - 2, y - 2, pw + 4, ph + 4, 1, RG.Rgba(255, 255, 255, .04f));
            }

            for (int i = 0; i < Mathf.RoundToInt(W * H / 220f); i++)
            {
                float v = r.Chance(0.5) ? 255 : 0;
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(1, 2.5), (float)r.Rand(1, 2.5),
                           RG.Rgba(v, v, v, (float)r.Rand(0.02, 0.07)));
            }

            for (int i = 0; i < Mathf.RoundToInt(H / 90f); i++)
                Squiggle(c, r, 6, RG.Rgba(0, 0, 0, .28f), (float)r.Rand(0.8, 1.6), 16, 8, 26);

            // tar crack-seal: dark worm lines with a faint highlight beside them
            for (int i = 0; i < Mathf.RoundToInt(H / 170f); i++)
                Squiggle(c, r, 8, night ? RG.Rgba(14, 16, 20, .55f) : RG.Rgba(24, 26, 30, .5f),
                         (float)r.Rand(2.5, 4), 22, 10, 30);
        }

        static void Squiggle(Raster c, Rng r, int steps, RG col, float width,
                             float jitterX, float stepMin, float stepMax)
        {
            float x = (float)r.Rand(0, c.W), y = (float)r.Rand(0, c.H);
            for (int k = 0; k < steps; k++)
            {
                float nx = x + (float)r.Rand(-jitterX, jitterX);
                float ny = y + (float)r.Rand(stepMin, stepMax);
                c.StrokeSegment(x, y, nx, ny, width, col);
                x = nx; y = ny;
            }
        }

        // ------------------------------------------------------------ road

        /// <summary>
        /// The full road surface for one side count, markings included. One repeat spans
        /// the whole carriageway across and <see cref="RoadRepeatMetres"/> along.
        ///
        /// Baking the paint into the surface rather than laying decal quads is what the
        /// reference does, and it is the right call here too: the route is a curved ribbon,
        /// so decals would need to be re-projected per segment and would z-fight on the
        /// curves. As a bonus the markings inherit the asphalt's wear for free.
        /// </summary>
        public static Texture2D Road(int lanesPerSide, bool night) => Get($"road{lanesPerSide}{(night ? "n" : "")}", () =>
        {
            const int W = 1024, H = 1024;
            var c = new Raster(W, H);
            var r = Seed($"road{lanesPerSide}{night}");
            AsphaltBase(c, r, night);

            float RW = lanesPerSide * (float)RoadGeom.LaneW + (float)RoadGeom.ParkStrip;
            float U(float t) => (t + RW) / (2 * RW) * W;

            // tyre-wear tracks: darkened wheel paths in each lane
            for (int ln = 0; ln < lanesPerSide; ln++)
                foreach (int side in new[] { 1, -1 })
                {
                    float center = side * (ln * 3.5f + 1.75f);
                    foreach (float off in new[] { -0.8f, 0.8f })
                    {
                        float px = U(center + off), w = W / (2 * RW) * 0.85f;
                        var g = new Raster.LinearGrad(px - w, 0, px + w, 0)
                            .Stop(0, RG.Rgba(0, 0, 0, 0))
                            .Stop(0.5f, RG.Rgba(0, 0, 0, .14f))
                            .Stop(1, RG.Rgba(0, 0, 0, 0));
                        c.FillRect(px - w, 0, w * 2, H, g);
                    }
                }

            // oil drips down each lane centre — engines leak between the wheels
            for (int ln = 0; ln < lanesPerSide; ln++)
                foreach (int side in new[] { 1, -1 })
                {
                    float px = U(side * (ln * 3.5f + 1.75f));
                    for (float y = (float)r.Rand(0, 120); y < H; y += (float)r.Rand(120, 330))
                    {
                        float len = (float)r.Rand(40, 130), w = (float)r.Rand(5, 12);
                        float cy = y + len / 2;
                        var g = new Raster.RadialGrad(px, cy, 2, len / 2)
                            .Stop(0, RG.Rgba(10, 10, 14, .28f))
                            .Stop(1, RG.Rgba(10, 10, 14, 0));
                        c.FillEllipse(px, cy, len / 2 * (w / len), len / 2, g);
                    }
                }

            // gutter grime at both parking strips
            foreach (int e in new[] { 0, W })
            {
                float inner = e == 0 ? 40 : W - 40;
                var g = new Raster.LinearGrad(e, 0, inner, 0)
                    .Stop(0, RG.Rgba(0, 0, 0, .22f))
                    .Stop(1, RG.Rgba(0, 0, 0, 0));
                c.FillRect(Mathf.Min(e, inner), 0, 40, H, g);
            }

            // centre double yellow, weathered and chipped
            c.FillRect(W / 2f - 11, 0, 7, H, RG.Rgba(228, 178, 52, .92f));
            c.FillRect(W / 2f + 4, 0, 7, H, RG.Rgba(228, 178, 52, .92f));
            var chip = night ? RG.Rgba(49, 52, 61, .65f) : RG.Rgba(72, 75, 83, .65f);
            for (int i = 0; i < 46; i++)
                c.FillRect(W / 2f - 12 + (float)r.Rand(0, 24), (float)r.Rand(0, H),
                           (float)r.Rand(2, 6), (float)r.Rand(3, 14), chip);

            // white edge lines at the parking-strip boundary
            float edge = RW - (float)RoadGeom.ParkStrip;
            c.FillRect(U(-edge) - 4, 0, 8, H, RG.Rgba(255, 255, 255, .8f));
            c.FillRect(U(edge) - 4, 0, 8, H, RG.Rgba(255, 255, 255, .8f));

            // parking bay ticks inside the strips
            for (float y = 0; y < H; y += 340)
            {
                c.FillRect(U(edge), y, W - U(edge), 8, RG.Rgba(255, 255, 255, .42f));
                c.FillRect(0, y, U(-edge), 8, RG.Rgba(255, 255, 255, .42f));
            }

            // dashed lane separators, two lanes per side only
            if (lanesPerSide == 2)
                for (float y = 0; y < H; y += 256)
                {
                    c.FillRect(U(-3.5f) - 5, y + 40, 10, 128, RG.Rgba(255, 255, 255, .7f));
                    c.FillRect(U(3.5f) - 5, y + 40, 10, 128, RG.Rgba(255, 255, 255, .7f));
                }

            // speckle over the paint so the markings read worn in, not printed on
            for (int i = 0; i < 1400; i++)
            {
                var col = night ? RG.Rgba(49, 52, 61, 0) : RG.Rgba(72, 75, 83, 0);
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(1, 3), (float)r.Rand(1, 3),
                           col.WithA((float)r.Rand(0.1, 0.4)));
            }

            return c.ToTexture($"road{lanesPerSide}");
        });

        /// <summary>Metres of road per vertical repeat of <see cref="Road"/>.</summary>
        public const float RoadRepeatMetres = 24f;

        public static Texture2D PlainAsphalt(bool night) => Get($"asph{night}", () =>
        {
            var c = new Raster(512, 512);
            AsphaltBase(c, Seed("asph" + night), night);
            return c.ToTexture("asphalt");
        });

        public static Texture2D Crosswalk(bool night) => Get($"cross{night}", () =>
        {
            var c = new Raster(512, 256);
            var r = Seed("cross" + night);
            AsphaltBase(c, r, night);
            for (float x = 16; x < c.W; x += 84)
                c.FillRect(x, 12, 48, c.H - 24, RG.Rgba(255, 255, 255, .85f));
            for (int i = 0; i < 700; i++)
            {
                var col = night ? RG.Rgba(49, 52, 61, 0) : RG.Rgba(72, 75, 83, 0);
                c.FillRect((float)r.Rand(0, c.W), (float)r.Rand(0, c.H),
                           (float)r.Rand(1, 3), (float)r.Rand(1, 3),
                           col.WithA((float)r.Rand(0.12, 0.45)));
            }
            return c.ToTexture("crosswalk");
        });

        /// <summary>
        /// Curb: stained concrete with expansion joints and gutter grime. The ribbon UV
        /// puts canvas x across the curb width (x = 0 is the road side, where the grime
        /// washes up) and canvas y along the road, so the joints are horizontal here.
        /// </summary>
        public static Texture2D Curb(bool night) => Get($"curb{night}", () =>
        {
            var c = new Raster(64, 512);
            var r = Seed("curb" + night);
            int W = c.W, H = c.H;
            c.Clear(RG.Hex(night ? "#666a78" : "#b6b2a6"));

            for (int i = 0; i < 700; i++)
            {
                float v = r.Chance(0.5) ? 255 : 0;
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(1, 3), (float)r.Rand(1, 3),
                           RG.Rgba(v, v, v, (float)r.Rand(0.03, 0.09)));
            }

            var grime = new Raster.LinearGrad(W * 0.55f, 0, 0, 0)
                .Stop(0, RG.Rgba(0, 0, 0, 0))
                .Stop(1, RG.Rgba(30, 28, 24, .42f));
            c.FillRect(0, 0, W, H, grime);

            for (int i = 0; i < 12; i++)
                c.FillRect((float)r.Rand(0, W * 0.4), (float)r.Rand(0, H), W, (float)r.Rand(3, 9),
                           RG.Rgba(50, 46, 40, (float)r.Rand(0.06, 0.16)));

            for (float y = 40; y < H; y += 128)
                c.FillRect(0, y, W, 3, RG.Rgba(0, 0, 0, .42f));

            // sun-caught arris on the road-side edge
            c.FillRect(0, 0, 4, H, RG.Rgba(255, 255, 255, .2f));
            return c.ToTexture("curb");
        });

        public static Texture2D Sidewalk(bool night) => Get($"swalk{night}", () =>
        {
            var c = new Raster(512, 512);
            var r = Seed("swalk" + night);
            int W = c.W, H = c.H;
            c.Clear(RG.Hex(night ? "#5c5f6b" : "#aaa69d"));

            const int slab = 128;
            for (int sy = 0; sy < H; sy += slab)
                for (int sx = 0; sx < W; sx += slab)
                {
                    float v = r.Chance(0.5) ? 255 : 0;
                    c.FillRect(sx, sy, slab, slab, RG.Rgba(v, v, v, (float)r.Rand(0.02, 0.07)));
                }

            for (int i = 0; i < 1100; i++)
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(1, 3), (float)r.Rand(1, 3),
                           RG.Rgba(0, 0, 0, (float)r.Rand(0.02, 0.06)));

            for (int i = 0; i < 8; i++)
            {
                float x = (float)r.Rand(0, W), y = (float)r.Rand(0, H), rad = (float)r.Rand(14, 44);
                var g = new Raster.RadialGrad(x, y, 2, rad)
                    .Stop(0, RG.Rgba(60, 55, 45, .1f))
                    .Stop(1, RG.Rgba(0, 0, 0, 0));
                c.FillRect(x - rad, y - rad, rad * 2, rad * 2, g);
            }

            for (int y = 0; y <= H; y += slab) c.StrokeSegment(0, y, W, y, 3, RG.Rgba(0, 0, 0, .3f));
            for (int x = 0; x <= W; x += slab) c.StrokeSegment(x, 0, x, H, 3, RG.Rgba(0, 0, 0, .3f));
            for (int y = 2; y <= H; y += slab) c.StrokeSegment(0, y, W, y, 1.5f, RG.Rgba(255, 255, 255, .14f));

            for (int i = 0; i < 5; i++)
                Squiggle(c, r, 4, RG.Rgba(0, 0, 0, .2f), (float)r.Rand(0.8, 1.4), 14, 10, 26);

            return c.ToTexture("sidewalk");
        });

        /// <summary>Verge and lawn: patchy growth, mow bands, blades, clover.</summary>
        public static Texture2D Grass(string baseHex, string spotHex) => Get("grass" + baseHex, () =>
        {
            var c = new Raster(1024, 1024);
            var r = Seed("grass" + baseHex);
            int W = c.W, H = c.H;

            var baseC = RG.Hex(baseHex);
            c.Clear(baseC);

            // TONAL RANGE. Lawn is not one colour, and the old draw kept every overlay under
            // 0.4 alpha, which averaged out to a single flat green — fine at a metre, and
            // at the distance this is actually seen it is a painted sheet covering half the
            // frame. Real grass has drought patches, shade, moss and worn earth in it, and
            // the spread between them is most of what stops a field reading as a colour.
            var straw = baseC.Lerp(RG.Hex("#cbbe72"), 0.62f);
            var lush = baseC.Scale(0.66f);
            var moss = baseC.Lerp(RG.Hex("#4f7a3a"), 0.5f);
            var dirt = baseC.Lerp(RG.Hex("#8a6f4d"), 0.72f);

            // WRAPPED. Every patch is drawn nine times, once per neighbouring tile offset,
            // so anything crossing an edge comes back in on the far side.
            //
            // This texture never tiled seamlessly, and it did not show while every overlay
            // sat under 0.4 alpha and 190 px across — the seams were there, just too faint
            // to find. Raising the contrast to give the lawn some tonal range turned them
            // into a hard 26 m grid across the entire field: the ground is the largest
            // surface in the frame, so a repeat in it is the most visible repeat there is.
            // Contrast and tiling are the same problem, and only one of them is fixable in
            // the draw calls.
            void Patch(float x, float y, float rad, Raster.RGBA col, float a0, float a1)
            {
                for (int ox = -1; ox <= 1; ox++)
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        float px = x + ox * W, py = y + oy * H;
                        if (px + rad < 0 || px - rad > W || py + rad < 0 || py - rad > H) continue;
                        var g = new Raster.RadialGrad(px, py, rad * 0.12f, rad)
                            .Stop(0, col.WithA(a0)).Stop(0.6f, col.WithA(a1)).Stop(1, col.WithA(0));
                        c.FillCircle(px, py, rad, g);
                    }
            }

            // Big soft patches first: these carry the macro variation.
            for (int i = 0; i < 26; i++)
            {
                double which = r.Next();
                Patch((float)r.Rand(0, W), (float)r.Rand(0, H), (float)r.Rand(150, 420),
                      which < 0.42 ? straw : (which < 0.78 ? lush : moss), 0.55f, 0.26f);
            }

            // Then smaller, harder ones on top, including bare earth showing through.
            for (int i = 0; i < 46; i++)
            {
                double which = r.Next();
                var col = which < 0.34 ? straw : (which < 0.68 ? lush : (which < 0.86 ? moss : dirt));
                Patch((float)r.Rand(0, W), (float)r.Rand(0, H), (float)r.Rand(40, 165),
                      col, which < 0.86 ? 0.44f : 0.62f, 0.18f);
            }

            for (int i = 0; i < 90; i++)
            {
                bool dark = r.Chance(0.5);
                Patch((float)r.Rand(0, W), (float)r.Rand(0, H), (float)r.Rand(14, 48),
                      dark ? RG.Rgba(0, 0, 0, 1) : RG.Rgba(255, 255, 235, 1),
                      dark ? 0.09f : 0.07f, dark ? 0.04f : 0.03f);
            }

            // The faint diagonal mow bands are gone. A diagonal stripe cannot tile on a
            // square, so they were laying a seam across every tile boundary for a effect
            // worth two and a half per cent alpha.

            var spotC = RG.Hex(spotHex);
            for (int i = 0; i < 9000; i++)
            {
                float x = (float)r.Rand(0, W), y = (float)r.Rand(0, H), len = (float)r.Rand(3, 8);
                float a = (float)r.Rand(-0.5, 0.5);
                var col = r.Chance(0.5) ? spotC
                        : (r.Chance(0.5) ? RG.Rgba(30, 60, 20, .35f) : RG.Rgba(195, 225, 125, .25f));
                c.GlobalAlpha = (float)r.Rand(0.22, 0.55);
                c.StrokeSegment(x, y, x + Mathf.Sin(a) * len, y - Mathf.Cos(a) * len,
                                (float)r.Rand(0.7, 1.5), col);
            }
            c.GlobalAlpha = 1f;

            for (int i = 0; i < 320; i++)
                c.FillCircle((float)r.Rand(0, W), (float)r.Rand(0, H), (float)r.Rand(1.2, 3),
                    new Raster.Solid(r.Chance(0.7) ? RG.Rgba(24, 52, 18, .3f) : RG.Rgba(225, 240, 160, .3f)));

            return c.ToTexture("grass");
        });

        /// <summary>Offset tab rows with per-tab shading and weathering streaks.</summary>
        public static Texture2D Shingle(string baseHex) => Get("shing" + baseHex, () =>
        {
            var c = new Raster(256, 256);
            var r = Seed("shing" + baseHex);
            int W = c.W, H = c.H;
            var bc = RG.Hex(baseHex);
            c.Clear(bc);

            const float rowH = 22, tabW = 30;
            int row = 0;
            for (float y = 0; y < H + rowH; y += rowH, row++)
            {
                float off = (row % 2) * (tabW / 2);
                for (float x = -tabW; x < W + tabW; x += tabW)
                {
                    c.FillRect(x + off, y, tabW - 2, rowH - 2, bc.Scale(1f + (float)r.Rand(-0.1, 0.12)));
                    c.FillRect(x + off, y + rowH - 4, tabW - 2, 2.5f, RG.Rgba(0, 0, 0, .3f));
                }
                c.FillRect(0, y - 1, W, 2, RG.Rgba(0, 0, 0, .22f));
            }

            for (int i = 0; i < 10; i++)
                c.FillRect((float)r.Rand(0, W), 0, (float)r.Rand(3, 10), H,
                           RG.Rgba(0, 0, 0, (float)r.Rand(0.04, 0.1)));

            return c.ToTexture("shingle");
        });

        /// <summary>
        /// Horizontal lap siding. Painted white and tinted by the material's base colour,
        /// so one texture serves the whole <c>bWall</c> palette.
        /// </summary>
        public static Texture2D Siding() => Get("siding", () =>
        {
            var c = new Raster(128, 128);
            var r = Seed("siding");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(255, 255, 255));
            for (int y = 0; y < H; y += 14)
            {
                c.FillRect(0, y + 11, W, 3, RG.Rgba(0, 0, 0, .16f));
                c.FillRect(0, y, W, 1.5f, RG.Rgba(255, 255, 255, .5f));
            }
            for (int i = 0; i < 160; i++)
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(1, 4), (float)r.Rand(1, 2),
                           RG.Rgba(0, 0, 0, (float)r.Rand(0.01, 0.05)));
            return c.ToTexture("siding");
        });

        /// <summary>
        /// Car body panel detail: shading gradient, character line, rocker shadow, wheel
        /// arch AO, door seams and handles. Wrapped round the hull by planar UVs, which is
        /// what turns a coloured blob into something that reads as a car.
        /// </summary>
        public static Texture2D CarSide(Color body) => Get("cside" + ColorUtility.ToHtmlStringRGB(body), () =>
        {
            var c = new Raster(512, 256);
            var r = Seed("cside" + ColorUtility.ToHtmlStringRGB(body));
            int W = c.W, H = c.H;
            var bc = RG.FromColor(body);
            var white = RG.Rgb(255, 255, 255);
            var black = RG.Rgb(0, 0, 0);

            var g = new Raster.LinearGrad(0, 0, 0, H)
                .Stop(0, bc.Lerp(white, 0.16f))
                .Stop(0.45f, bc)
                .Stop(1, bc.Lerp(black, 0.28f));
            c.FillRect(0, 0, W, H, g);

            var rg = new Raster.LinearGrad(0, H * 0.2f, 0, H * 0.44f)
                .Stop(0, RG.Rgba(255, 255, 255, 0))
                .Stop(0.5f, RG.Rgba(255, 255, 255, .10f))
                .Stop(1, RG.Rgba(255, 255, 255, 0));
            c.FillRect(0, H * 0.2f, W, H * 0.24f, rg);

            c.FillRect(0, H * 0.4f, W, 2, RG.Rgba(255, 255, 255, .16f));
            c.FillRect(0, H * 0.4f + 2, W, 3, RG.Rgba(0, 0, 0, .2f));
            c.FillRect(0, H * 0.9f, W, H * 0.1f, RG.Rgba(0, 0, 0, .5f));

            foreach (float ax in new[] { 0.2f, 0.8f })
            {
                var gg = new Raster.RadialGrad(W * ax, H * 1.02f, H * 0.1f, H * 0.42f)
                    .Stop(0, RG.Rgba(0, 0, 0, .55f))
                    .Stop(1, RG.Rgba(0, 0, 0, 0));
                c.FillRect(W * ax - H * 0.45f, H * 0.55f, H * 0.9f, H * 0.45f, gg);
            }

            // Door shutlines. A single flat stroke read as a stripe painted on rather than a
            // pressed seam, and at 2px/0.42 alpha the high-gloss paint's sky reflection
            // buried it almost entirely. A seam is a shadow with a highlight riding its
            // shoulder — the panel edge catches light on one side of the gap and casts it on
            // the other — so pair a wider, darker line with a thin bright one beside it.
            foreach (float sx in new[] { 0.4f, 0.63f })
            {
                c.StrokeSegment(W * sx, H * 0.16f, W * sx, H * 0.90f, 3, RG.Rgba(0, 0, 0, .62f));
                c.StrokeSegment(W * sx + 3, H * 0.16f, W * sx + 3, H * 0.90f, 1, RG.Rgba(255, 255, 255, .16f));
            }
            // Handles: a dark recess with a small pale insert, not a flat dark blob — the
            // insert is what reads as the grip itself rather than a smudge on the door.
            foreach (float hx in new[] { 0.46f, 0.69f })
            {
                c.FillRect(W * hx - 2, H * 0.44f, 30, 11, RG.Rgba(0, 0, 0, .58f));
                c.FillRect(W * hx + 1, H * 0.465f, 24, 5, RG.Rgba(225, 227, 230, .60f));
            }

            for (int i = 0; i < 260; i++)
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H), 2, 2,
                           RG.Rgba(255, 255, 255, (float)r.Rand(0.01, 0.04)));

            return c.ToTexture("carside", wrap: TextureWrapMode.Clamp);
        });

        /// <summary>
        /// The glasshouse: three panes in a black pillar surround, each with a vertical
        /// sky-to-sill gradient and a highlight along the top edge.
        ///
        /// The reference splits this across the canopy box's faces (side / end / roof
        /// materials). The port's canopy is one welded hull, so the pane pattern is laid
        /// along the length by the hull's own u coordinate instead — same read, one
        /// material, and it survives the rounded corners without a seam.
        /// </summary>
        public static Texture2D CanopySide() => Get("canopy", () =>
        {
            var c = new Raster(512, 128);
            int W = c.W, H = c.H;
            c.Clear(RG.Hex("#0b0f14"));       // pillar black

            void Pane(float x0, float x1)
            {
                var g = new Raster.LinearGrad(0, 0, 0, H)
                    .Stop(0, RG.Hex("#3d4f63"))
                    .Stop(0.35f, RG.Hex("#22303f"))
                    .Stop(1, RG.Hex("#0e151d"));
                c.FillRect(W * x0, H * 0.12f, W * (x1 - x0), H * 0.82f, g);
                c.FillRect(W * x0 + 3, H * 0.16f, W * (x1 - x0) - 6, 3, RG.Rgba(255, 255, 255, .14f));
            }

            Pane(0.06f, 0.44f);
            Pane(0.5f, 0.72f);
            Pane(0.78f, 0.95f);
            return c.ToTexture("canopy", wrap: TextureWrapMode.Clamp);
        });

        // ------------------------------------------------------------ normal maps

        /// <summary>
        /// The asphalt aggregate as a tangent-space normal map. The reference feeds this
        /// height field to a <c>bumpMap</c>, which URP's Lit shader has no equivalent for,
        /// so it is converted here by central difference instead of adding a custom shader.
        /// </summary>
        public static Texture2D AsphaltNormal() => Get("asphNrm", () =>
        {
            const int N = 256;
            var c = new Raster(N, N);
            var r = Seed("asphBump");
            c.Clear(RG.Rgb(128, 128, 128));
            for (int i = 0; i < 2600; i++)
            {
                float v = Mathf.Round((float)r.Rand(70, 185));
                c.FillRect((float)r.Rand(0, N), (float)r.Rand(0, N),
                           (float)r.Rand(1, 3), (float)r.Rand(1, 3),
                           RG.Rgba(v, v, v, (float)r.Rand(0.25, 0.7)));
            }
            for (int i = 0; i < 26; i++)
            {
                float x = (float)r.Rand(0, N), y = (float)r.Rand(0, N), rad = (float)r.Rand(18, 60);
                var g = new Raster.RadialGrad(x, y, 1, rad)
                    .Stop(0, r.Chance(0.5) ? RG.Rgba(255, 255, 255, .10f) : RG.Rgba(0, 0, 0, .10f))
                    .Stop(1, RG.Rgba(128, 128, 128, 0));
                c.FillRect(x - rad, y - rad, rad * 2, rad * 2, g);
            }
            return c.ToNormalMap("asphaltNormal", 2.2f);
        });

        // ------------------------------------------------------------ horizon

        /// <summary>
        /// The distant horizon silhouette, wrapped round a cylinder past the fog distance.
        /// Two layers of rolling hills with ridge trees and a water tower, tinted toward
        /// the fog colour so the ring dissolves into the sky instead of ending at an edge.
        /// </summary>
        public static Texture2D HillsSkyline(Color fog) => Get("skyhills" + ColorUtility.ToHtmlStringRGB(fog), () =>
        {
            const int W = 2048, H = 256;
            var c = new Raster(W, H);
            c.ClearTransparent();

            var fc = RG.FromColor(fog);
            RG Shade(float mix, float alpha) => fc.Lerp(RG.Rgb(26, 32, 48), mix).WithA(alpha);

            // The reference uses frequencies 0.006 and 0.017 rad/px. Those do not complete
            // a whole number of cycles across the canvas, and the ring wraps the texture
            // three times — so each seam becomes a vertical cliff in the ridge that reads
            // as a mesa on the horizon. Snapped to the nearest exactly-periodic
            // frequencies, which is visually identical and seamless.
            const float TwoPi = Mathf.PI * 2f;
            float K1 = Mathf.Round(0.006f * W / TwoPi) * TwoPi / W;
            float K2 = Mathf.Round(0.017f * W / TwoPi) * TwoPi / W;

            float Ridge(float x, float baseY, float amp, float mix)
                => baseY - Mathf.Sin(x * K1 + mix * 40f) * amp
                         - Mathf.Sin(x * K2 + mix * 9f) * amp * 0.4f;

            foreach (var (mix, baseY, amp, alpha) in new[]
                     { (0.18f, 150f, 34f, 0.85f), (0.38f, 190f, 26f, 0.95f) })
            {
                var col = Shade(mix, alpha);
                var poly = new List<Vector2> { new Vector2(0, H) };
                for (int x = 0; x <= W; x += 16) poly.Add(new Vector2(x, Ridge(x, baseY, amp, mix)));
                poly.Add(new Vector2(W, H));
                c.FillPolygon(poly, col);

                for (int x = 30; x < W; x += 90 + (x % 70))
                {
                    float y = Ridge(x, baseY, amp, mix);
                    c.FillCircle(x, y - 5, 6, new Raster.Solid(col));
                    c.FillRect(x - 1, y - 4, 2, 6, col);
                }
            }

            var tower = Shade(0.45f, 0.95f);
            c.FillRect(600, 128, 5, 46, tower);
            c.FillRect(636, 128, 5, 46, tower);
            c.FillEllipse(620, 122, 26, 16, new Raster.Solid(tower));

            return c.ToTexture("skyline", wrap: TextureWrapMode.Repeat);
        });
    }
}
