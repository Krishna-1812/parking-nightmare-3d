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
            // Concrete is not white. At #aaa69d in full sun with the hemisphere ambient on
            // top it clipped, and a clipped surface has no texture at all no matter what
            // is painted on it — which is why the tonal work below was invisible.
            c.Clear(RG.Hex(night ? "#545762" : "#9e9a8f"));

            // The footway is four metres of the frame in every ground-level shot and it was
            // reading as blank paper. Paving is cast in batches, laid by different gangs and
            // weathered separately, so no two slabs are the same tone — at 0.02 to 0.07
            // alpha they all were. Each slab now gets a real tonal offset and, on some, a
            // warm or cool cast, which is what stops a pavement looking printed.
            const int slab = 128;
            for (int sy = 0; sy < H; sy += slab)
                for (int sx = 0; sx < W; sx += slab)
                {
                    float v = r.Chance(0.5) ? 255 : 0;
                    c.FillRect(sx, sy, slab, slab, RG.Rgba(v, v, v, (float)r.Rand(0.03, 0.16)));
                    if (r.Chance(0.45))
                        c.FillRect(sx, sy, slab, slab, r.Chance(0.5)
                            ? RG.Rgba(158, 130, 88, (float)r.Rand(0.03, 0.07))
                            : RG.Rgba(92, 112, 134, (float)r.Rand(0.03, 0.06)));

                    // Chipping and dirt gathering along the joints, worst at the corners.
                    for (int e = 0; e < 4; e++)
                    {
                        float ex = sx + (e == 1 ? slab - 5 : 0), ey = sy + (e == 3 ? slab - 5 : 0);
                        float ew = e % 2 == 0 ? slab : 5, eh = e % 2 == 0 ? 5 : slab;
                        c.FillRect(ex, ey, ew, eh, RG.Rgba(0, 0, 0, (float)r.Rand(0.03, 0.09)));
                    }
                }

            for (int i = 0; i < 1600; i++)
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(1, 3), (float)r.Rand(1, 3),
                           RG.Rgba(0, 0, 0, (float)r.Rand(0.02, 0.08)));

            // Stains: rain shadow, spilt drinks, whatever a pavement collects.
            for (int i = 0; i < 16; i++)
            {
                float x = (float)r.Rand(0, W), y = (float)r.Rand(0, H), rad = (float)r.Rand(14, 62);
                var g = new Raster.RadialGrad(x, y, 2, rad)
                    .Stop(0, RG.Rgba(60, 55, 45, (float)r.Rand(0.08, 0.18)))
                    .Stop(1, RG.Rgba(0, 0, 0, 0));
                c.FillRect(x - rad, y - rad, rad * 2, rad * 2, g);
            }

            // Moss in the joints, which is where it grows.
            for (int i = 0; i < 220; i++)
            {
                float jx = Mathf.Round((float)r.Rand(0, W) / slab) * slab;
                float jy = (float)r.Rand(0, H);
                if (r.Chance(0.5)) { float t = jx; jx = jy; jy = Mathf.Round(t / slab) * slab; }
                c.FillCircle(jx + (float)r.Rand(-2.5, 2.5), jy, (float)r.Rand(1.2, 3.4),
                    new Raster.Solid(RG.Rgba(74, 96, 52, (float)r.Rand(0.10, 0.30))));
            }

            for (int y = 0; y <= H; y += slab) c.StrokeSegment(0, y, W, y, 3, RG.Rgba(0, 0, 0, .3f));
            for (int x = 0; x <= W; x += slab) c.StrokeSegment(x, 0, x, H, 3, RG.Rgba(0, 0, 0, .3f));
            for (int y = 2; y <= H; y += slab) c.StrokeSegment(0, y, W, y, 1.5f, RG.Rgba(255, 255, 255, .14f));

            for (int i = 0; i < 5; i++)
                Squiggle(c, r, 4, RG.Rgba(0, 0, 0, .2f), (float)r.Rand(0.8, 1.4), 14, 10, 26);

            return c.ToTexture("sidewalk");
        });

        /// <summary>
        /// Relief for <see cref="Sidewalk"/>: the joints between slabs, the trowelled
        /// camber of each slab, and the exposed aggregate. Same 128 px slab grid, so the
        /// grooves land exactly on the painted joints.
        /// </summary>
        public static Texture2D SidewalkNormal() => Get("swalkNrm", () =>
        {
            var c = new Raster(512, 512);
            var r = Seed("swalkNrm");
            int W = c.W, H = c.H;
            const int slab = 128;
            c.Clear(RG.Rgb(150, 150, 150));

            // each slab crowns very slightly toward its middle, as a floated slab does
            for (int sy = 0; sy < H; sy += slab)
                for (int sx = 0; sx < W; sx += slab)
                {
                    var dome = new Raster.RadialGrad(sx + slab / 2f, sy + slab / 2f, 2, slab * 0.72f)
                        .Stop(0, RG.Rgb(168, 168, 168))
                        .Stop(1, RG.Rgba(150, 150, 150, 0f));
                    c.FillRect(sx, sy, slab, slab, dome);
                }

            for (int i = 0; i < 5200; i++)
            {
                float v = (float)r.Rand(120, 182);
                c.FillEllipse((float)r.Rand(0, W), (float)r.Rand(0, H),
                              (float)r.Rand(1, 3), (float)r.Rand(1, 2.5),
                              new Raster.Solid(RG.Rgba(v, v, v, (float)r.Rand(0.2, 0.5))));
            }

            // the joints themselves, cut last so nothing fills them back in
            for (int y = 0; y <= H; y += slab) c.FillRect(0, y - 2f, W, 4f, RG.Rgb(52, 52, 52));
            for (int x = 0; x <= W; x += slab) c.FillRect(x - 2f, 0, 4f, H, RG.Rgb(52, 52, 52));

            return c.ToNormalMap("sidewalkNormal", 1.6f);
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
        /// Horizontal lap siding, one board per 14 px and one repeat per 2.2 m. Painted
        /// white and tinted by the material's base colour, so one texture serves the whole
        /// <c>bWall</c> palette.
        /// </summary>
        public static Texture2D Siding() => Get("siding", () =>
        {
            var c = new Raster(128, 128);
            var r = Seed("siding");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(255, 255, 255));

            for (int y = 0; y < H; y += Board)
            {
                // Each board is a shallow wedge: thin at the top where it tucks under the
                // one above, thick at the bottom where it stands proud and throws a line
                // of shadow. That shadow is the whole reason lap siding is legible from
                // across a street, so it gets a gradient rather than a hard bar.
                var face = new Raster.LinearGrad(0, y, 0, y + Board)
                    .Stop(0f, RG.Rgba(0, 0, 0, .10f))
                    .Stop(0.35f, RG.Rgba(255, 255, 255, .05f))
                    .Stop(1f, RG.Rgba(0, 0, 0, .04f));
                c.FillRect(0, y, W, Board, face);
                c.FillRect(0, y + Board - 3f, W, 2.6f, RG.Rgba(0, 0, 0, .22f));
                c.FillRect(0, y + Board - 0.4f, W, 1.2f, RG.Rgba(255, 255, 255, .45f));
            }

            // butt joints between board lengths, and the nail line above each one
            for (int i = 0; i < 7; i++)
            {
                float x = (float)r.Rand(0, W);
                int row = (int)r.Rand(0, H / Board);
                c.FillRect(x, row * Board + 1, 1.2f, Board - 3, RG.Rgba(0, 0, 0, .16f));
            }

            // grain and weathering: streaks run along the board, never across it
            for (int i = 0; i < 220; i++)
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(3, 14), 1,
                           RG.Rgba(0, 0, 0, (float)r.Rand(0.01, 0.045)));
            for (int i = 0; i < 26; i++)
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(6, 30), (float)r.Rand(2, 7),
                           RG.Rgba(255, 255, 255, (float)r.Rand(0.02, 0.06)));

            return c.ToTexture("siding");
        });

        const int Board = 14;

        /// <summary>The relief that goes with <see cref="Siding"/>: the same wedge, as height.</summary>
        public static Texture2D SidingNormal() => Get("sidingNrm", () =>
        {
            var c = new Raster(128, 128);
            var r = Seed("sidingNrm");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(128, 128, 128));
            for (int y = 0; y < H; y += Board)
            {
                var wedge = new Raster.LinearGrad(0, y, 0, y + Board - 1)
                    .Stop(0f, RG.Rgb(96, 96, 96))
                    .Stop(1f, RG.Rgb(196, 196, 196));
                c.FillRect(0, y, W, Board - 1, wedge);
                c.FillRect(0, y + Board - 1, W, 1.4f, RG.Rgb(40, 40, 40));
            }
            for (int i = 0; i < 300; i++)
            {
                float v = (float)r.Rand(100, 156);
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H), (float)r.Rand(4, 16), 1,
                           RG.Rgba(v, v, v, 0.5f));
            }
            return c.ToNormalMap("sidingNormal", 1.7f);
        });

        /// <summary>
        /// Troweled render. Painted white so the wall palette tints it, like the siding.
        /// The look is entirely in the relief — flat stucco is just a coloured rectangle —
        /// so the albedo stays nearly uniform and <see cref="StuccoNormal"/> does the work.
        /// </summary>
        public static Texture2D Stucco() => Get("stucco", () =>
        {
            var c = new Raster(256, 256);
            var r = Seed("stucco");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(255, 255, 255));
            // Render is *painted*. Whatever texture it has lives in the light, not in the
            // pigment — so the albedo barely moves and every mark here is a couple of
            // percent. The first attempt used 2600 marks at five percent each; they
            // accumulated, dragged the wall to grey, and turned a painted house into a
            // gravel driveway stood on end.
            for (int i = 0; i < 1500; i++)
            {
                bool light = r.Chance(0.5);
                c.FillEllipse((float)r.Rand(0, W), (float)r.Rand(0, H),
                              (float)r.Rand(1.5, 5), (float)r.Rand(1.5, 4),
                              new Raster.Solid(light ? RG.Rgba(255, 255, 255, .028f)
                                                     : RG.Rgba(0, 0, 0, .022f)));
            }
            // rain streaking below where the gutters overflow
            for (int i = 0; i < 12; i++)
                c.FillRect((float)r.Rand(0, W), 0, (float)r.Rand(2, 9), H,
                           RG.Rgba(0, 0, 0, (float)r.Rand(0.01, 0.03)));
            return c.ToTexture("stucco");
        });

        public static Texture2D StuccoNormal() => Get("stuccoNrm", () =>
        {
            var c = new Raster(256, 256);
            var r = Seed("stuccoNrm");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(128, 128, 128));
            for (int i = 0; i < 2400; i++)
            {
                float v = (float)r.Rand(96, 164);
                c.FillEllipse((float)r.Rand(0, W), (float)r.Rand(0, H),
                              (float)r.Rand(1.5, 5), (float)r.Rand(1.5, 4),
                              new Raster.Solid(RG.Rgba(v, v, v, (float)r.Rand(0.15, 0.35))));
            }
            return c.ToNormalMap("stuccoNormal", 1.0f);
        });

        /// <summary>
        /// Brick in running bond, one repeat per 2.2 m — so a course is about 75 mm, which
        /// is a brick. Unlike the siding and the stucco this carries its own colour: a
        /// brick wall tinted mint green is not a house, it is a mistake.
        /// </summary>
        public static Texture2D Brick(string baseHex) => Get("brick" + baseHex, () =>
        {
            var c = new Raster(256, 256);
            var r = Seed("brick" + baseHex);
            int W = c.W, H = c.H;
            var bc = RG.Hex(baseHex);
            var mortar = RG.Rgb(196, 192, 182);
            c.Clear(mortar);

            const float courseH = 256f / 29f;   // 29 courses over 2.2 m
            const float brickW = 256f / 9f;
            const float joint = 1.6f;
            int course = 0;
            for (float y = 0; y < H; y += courseH, course++)
            {
                float off = (course % 2) * (brickW * 0.5f);
                for (float x = -brickW; x < W + brickW; x += brickW)
                {
                    // Every brick fired slightly differently. The spread is wide on
                    // purpose — a wall of identical bricks reads as printed paper.
                    var col = bc.Scale(1f + (float)r.Rand(-0.15, 0.17))
                                .Lerp(RG.Rgb(70, 52, 46), (float)r.Rand(0, 0.10));
                    c.FillRect(x + off + joint, y + joint,
                               brickW - joint * 2f, courseH - joint * 2f, col);
                    // the face is not flat: light catches the top arris
                    c.FillRect(x + off + joint, y + joint, brickW - joint * 2f, 1.1f,
                               RG.Rgba(255, 255, 255, .09f));
                    c.FillRect(x + off + joint, y + courseH - joint - 1.1f,
                               brickW - joint * 2f, 1.1f, RG.Rgba(0, 0, 0, .13f));
                }
            }

            // efflorescence and soot, both of which run downwards
            for (int i = 0; i < 12; i++)
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(6, 26), (float)r.Rand(20, 90),
                           r.Chance(0.5) ? RG.Rgba(255, 255, 255, .05f) : RG.Rgba(0, 0, 0, .06f));

            return c.ToTexture("brick");
        });

        public static Texture2D BrickNormal() => Get("brickNrm", () =>
        {
            var c = new Raster(256, 256);
            var r = Seed("brickNrm");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(74, 74, 74));            // the mortar sits back
            const float courseH = 256f / 29f;
            const float brickW = 256f / 9f;
            const float joint = 1.6f;
            int course = 0;
            for (float y = 0; y < H; y += courseH, course++)
            {
                float off = (course % 2) * (brickW * 0.5f);
                for (float x = -brickW; x < W + brickW; x += brickW)
                {
                    float v = (float)r.Rand(178, 208);
                    c.FillRect(x + off + joint, y + joint,
                               brickW - joint * 2f, courseH - joint * 2f, RG.Rgb(v, v, v));
                }
            }
            for (int i = 0; i < 2400; i++)
            {
                float v = (float)r.Rand(150, 220);
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H), 2, 2,
                           RG.Rgba(v, v, v, 0.35f));
            }
            return c.ToNormalMap("brickNormal", 2.6f);
        });

        /// <summary>Tab relief for <see cref="Shingle"/>: each course steps down over the next.</summary>
        public static Texture2D ShingleNormal() => Get("shingNrm", () =>
        {
            var c = new Raster(256, 256);
            var r = Seed("shingNrm");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(128, 128, 128));
            const float rowH = 22, tabW = 30;
            int row = 0;
            for (float y = 0; y < H + rowH; y += rowH, row++)
            {
                float off = (row % 2) * (tabW / 2);
                var step = new Raster.LinearGrad(0, y, 0, y + rowH)
                    .Stop(0f, RG.Rgb(112, 112, 112))
                    .Stop(0.8f, RG.Rgb(186, 186, 186))
                    .Stop(1f, RG.Rgb(52, 52, 52));
                c.FillRect(0, y, W, rowH, step);
                for (float x = -tabW; x < W + tabW; x += tabW)
                    c.FillRect(x + off - 0.7f, y, 1.4f, rowH - 2, RG.Rgb(88, 88, 88));
            }
            for (int i = 0; i < 1800; i++)
            {
                float v = (float)r.Rand(105, 155);
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H), 2, 2, RG.Rgba(v, v, v, 0.4f));
            }
            return c.ToNormalMap("shingleNormal", 1.5f);
        });

        /// <summary>
        /// Bark. White-ish so the trunk colour tints it, with the fissures running up the
        /// trunk — which is the direction that matters, because a trunk is a cylinder and
        /// every horizontal feature on it would read as a growth ring seen from the side.
        /// </summary>
        public static Texture2D Bark() => Get("bark", () =>
        {
            var c = new Raster(128, 256);
            var r = Seed("bark");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(255, 255, 255));

            for (int i = 0; i < 190; i++)
            {
                float x = (float)r.Rand(0, W);
                float wdt = (float)r.Rand(1.5, 6);
                bool dark = r.Chance(0.62);
                // Held low deliberately. Bark's contrast in life is nearly all shading —
                // the fissures are dark because they are in shadow, and the normal map
                // already does that. Painting the shadow in as well gives a striped pole.
                float a = (float)r.Rand(0.03, 0.14);
                // a fissure wanders as it climbs
                float y = 0;
                while (y < H)
                {
                    float seg = (float)r.Rand(8, 26);
                    c.FillRect(x, y, wdt, seg, dark ? RG.Rgba(0, 0, 0, a) : RG.Rgba(255, 255, 255, a * 0.9f));
                    x += (float)r.Rand(-1.6, 1.6);
                    y += seg;
                }
            }
            for (int i = 0; i < 500; i++)
                c.FillRect((float)r.Rand(0, W), (float)r.Rand(0, H),
                           (float)r.Rand(1, 3), (float)r.Rand(2, 8),
                           RG.Rgba(0, 0, 0, (float)r.Rand(0.012, 0.045)));
            return c.ToTexture("bark");
        });

        public static Texture2D BarkNormal() => Get("barkNrm", () =>
        {
            var c = new Raster(128, 256);
            var r = Seed("barkNrm");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(128, 128, 128));
            for (int i = 0; i < 240; i++)
            {
                float x = (float)r.Rand(0, W);
                float wdt = (float)r.Rand(1.5, 6);
                float v = (float)r.Rand(55, 205);
                float y = 0;
                while (y < H)
                {
                    float seg = (float)r.Rand(8, 26);
                    c.FillRect(x, y, wdt, seg, RG.Rgba(v, v, v, 0.55f));
                    x += (float)r.Rand(-1.6, 1.6);
                    y += seg;
                }
            }
            return c.ToNormalMap("barkNormal", 1.5f);
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

        // ------------------------------------------------------------ people

        /// <summary>
        /// Skin, away from the face: mottling, veining and a faint flush at the joints.
        /// White-based, so one texture serves every tone in the palette.
        ///
        /// The point is that skin is never one value. A flat-coloured limb reads as plastic
        /// however good the lighting model on top of it is, because real skin varies by
        /// several percent over a centimetre and by more than that between the inside and
        /// the outside of a forearm.
        /// </summary>
        public static Texture2D SkinDetail() => Get("skinDetail", () =>
        {
            var c = new Raster(256, 256);
            var r = Seed("skinDetail");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(255, 255, 255));

            for (int i = 0; i < 260; i++)
            {
                float x = (float)r.Rand(0, W), y = (float)r.Rand(0, H), rad = (float)r.Rand(8, 40);
                var g = new Raster.RadialGrad(x, y, 1, rad)
                    .Stop(0, r.Chance(0.55) ? RG.Rgba(214, 138, 122, .10f)
                                            : RG.Rgba(255, 246, 232, .09f))
                    .Stop(1, RG.Rgba(255, 255, 255, 0));
                c.FillRect(x - rad, y - rad, rad * 2, rad * 2, g);
            }
            // freckles and moles, sparse
            for (int i = 0; i < 90; i++)
                c.FillCircle((float)r.Rand(0, W), (float)r.Rand(0, H), (float)r.Rand(0.6, 2.0),
                             new Raster.Solid(RG.Rgba(126, 84, 62, (float)r.Rand(0.05, 0.22))));
            return c.ToTexture("skinDetail");
        });

        /// <summary>Pores and the fine creasing that catches a grazing sun.</summary>
        public static Texture2D SkinNormal() => Get("skinNrm", () =>
        {
            var c = new Raster(256, 256);
            var r = Seed("skinNrm");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(128, 128, 128));
            for (int i = 0; i < 7000; i++)
            {
                float v = (float)r.Rand(104, 152);
                c.FillCircle((float)r.Rand(0, W), (float)r.Rand(0, H), (float)r.Rand(0.5, 1.6),
                             new Raster.Solid(RG.Rgba(v, v, v, (float)r.Rand(0.25, 0.6))));
            }
            for (int i = 0; i < 40; i++)
                Squiggle(c, r, 5, RG.Rgba(112, 112, 112, .30f), (float)r.Rand(0.7, 1.4), 9, 6, 16);
            return c.ToNormalMap("skinNormal", 0.7f);
        });

        /// <summary>
        /// The face, painted.
        ///
        /// Eyebrows, lips, nostrils, the shadow in an eye socket and the shading under a
        /// cheekbone are all a few millimetres of tone. Modelled, each one is a box the
        /// size of a fingernail that catches the light wrong and reads as a growth. Painted
        /// on a properly mapped head they cost nothing and land exactly where they belong.
        ///
        /// The mapping is fixed by the loft: u = 0.5 is dead centre front and the seam runs
        /// down the back of the skull, v = 0 is under the chin and v = 1 is the crown. So
        /// features go at known coordinates and stay there whatever size the head is.
        /// </summary>
        public static Texture2D Face(int skinIdx, Color hair, Color eye, bool stubble)
            => Get($"face{skinIdx}{ColorUtility.ToHtmlStringRGB(hair)}"
                   + $"{ColorUtility.ToHtmlStringRGB(eye)}{stubble}", () =>
        {
            const int W = 512, H = 512;
            var c = new Raster(W, H);
            var r = Seed($"face{skinIdx}{stubble}");
            c.Clear(RG.Rgb(255, 255, 255));

            // v runs bottom (chin) to top (crown) but the raster's y runs downward, so
            // everything is placed through these and the numbers below read the right way
            // up. The v values are NOT fractions of head height: the loft's UV runs one
            // step per ring, and the rings are not evenly spaced, so a feature's v is where
            // its ring is in the list. Eye line lands on 0.48, not on 0.5.
            float X(float u) => u * W;
            float Y(float v) => (1f - v) * H;

            var hairRG = RG.FromColor(hair);
            const float EyeU = 0.058f;      // half the interpupillary distance, in u

            // Cheeks and ears carry more blood than the rest.
            foreach (var (u, v, rad, col, a) in new[]
                     {
                         (0.500f, 0.28f, 70f, RG.Rgb(222, 132, 116), 0.16f),  // mouth region
                         (0.410f, 0.40f, 58f, RG.Rgb(226, 140, 120), 0.15f),  // cheek
                         (0.590f, 0.40f, 58f, RG.Rgb(226, 140, 120), 0.15f),
                         (0.500f, 0.42f, 40f, RG.Rgb(228, 150, 128), 0.10f),  // nose
                         (0.500f, 0.64f, 88f, RG.Rgb(236, 214, 196), 0.12f),  // forehead
                     })
            {
                var g = new Raster.RadialGrad(X(u), Y(v), 2, rad)
                    .Stop(0, col.WithA(a)).Stop(1, RG.Rgba(255, 255, 255, 0));
                c.FillRect(X(u) - rad, Y(v) - rad, rad * 2, rad * 2, g);
            }

            // ---- eyes ----
            // Painted, deliberately. Modelled at the correct anatomical size and placed a
            // couple of millimetres proud, they read as ping-pong balls stuck to a face:
            // an eye only works when it sits in a socket that shades it, and a socket is
            // three pixels of gradient. Painting it also means it cannot be geometrically
            // wrong, which the modelled attempt very much was.
            var eyeRG = RG.FromColor(eye);
            foreach (float side in new[] { -1f, 1f })
            {
                float ex = X(0.5f + side * EyeU), ey = Y(0.48f);

                var socket = new Raster.RadialGrad(ex, ey + 2f, 2, 34f)
                    .Stop(0, RG.Rgba(96, 62, 52, .32f)).Stop(1, RG.Rgba(255, 255, 255, 0));
                c.FillRect(ex - 34, ey - 32, 68, 68, socket);

                c.FillEllipse(ex, ey, 15.5f, 8.5f, new Raster.Solid(RG.Rgb(232, 230, 226)));
                // corners are pinker and darker than the middle of the white
                c.FillEllipse(ex - 12f, ey, 5f, 5f, new Raster.Solid(RG.Rgba(196, 158, 150, .55f)));
                c.FillEllipse(ex + 12f, ey, 5f, 5f, new Raster.Solid(RG.Rgba(196, 158, 150, .55f)));

                c.FillCircle(ex, ey, 7.6f, new Raster.Solid(eyeRG));
                c.FillCircle(ex, ey, 7.6f, new Raster.Solid(RG.Rgba(0, 0, 0, .0f)));
                // limbal ring: the dark edge round an iris, and the single detail that
                // most separates a drawn eye from a coloured dot
                for (float k = 7.6f; k > 6.2f; k -= 0.5f)
                    c.FillCircle(ex, ey, k, new Raster.Solid(RG.Rgba(18, 12, 10, .30f)));
                c.FillCircle(ex, ey, 3.4f, new Raster.Solid(RG.Rgb(12, 10, 10)));
                // the wet highlight
                c.FillCircle(ex - 2.6f, ey - 2.6f, 2.2f, new Raster.Solid(RG.Rgba(255, 255, 255, .92f)));

                // upper lid shadow and lash line
                c.FillEllipse(ex, ey - 7.5f, 16f, 4.5f, new Raster.Solid(RG.Rgba(58, 38, 32, .50f)));
                c.FillEllipse(ex, ey + 8.5f, 14f, 2.4f, new Raster.Solid(RG.Rgba(120, 82, 70, .30f)));
            }

            // Eyebrows. Hair-coloured, thin, angled, drawn as strokes — a solid bar over
            // each eye reads as one black band across a face.
            foreach (float side in new[] { -1f, 1f })
            {
                float bx0 = X(0.5f + side * EyeU);
                for (int i = 0; i < 26; i++)
                {
                    float t = i / 25f;
                    float bx = bx0 + side * (t - 0.5f) * 46f;
                    float by = Y(0.552f) - Mathf.Sin(t * Mathf.PI) * 6f;
                    c.StrokeSegment(bx, by + (float)r.Rand(-1.2, 1.2),
                                    bx + side * 3f, by - 4.5f + (float)r.Rand(-1.2, 1.2),
                                    (float)r.Rand(1.5, 2.8),
                                    hairRG.WithA((float)r.Rand(0.45, 0.8)));
                }
            }

            // Nostrils and the shadow under the tip.
            foreach (float nu in new[] { 0.482f, 0.518f })
                c.FillEllipse(X(nu), Y(0.372f), 5.0f, 3.2f,
                              new Raster.Solid(RG.Rgba(64, 40, 34, .55f)));
            c.FillEllipse(X(0.5f), Y(0.364f), 17f, 4.5f,
                          new Raster.Solid(RG.Rgba(120, 74, 62, .18f)));

            // Lips. Upper darker than lower, because the lower one faces the sky.
            c.FillEllipse(X(0.5f), Y(0.298f), 26f, 8f,
                          new Raster.Solid(RG.Rgba(178, 96, 88, .55f)));
            c.FillEllipse(X(0.5f), Y(0.286f), 23f, 7f,
                          new Raster.Solid(RG.Rgba(206, 122, 110, .50f)));
            c.FillRect(X(0.5f) - 25f, Y(0.294f), 50f, 2.0f, RG.Rgba(86, 48, 44, .55f));

            // A little shadow under the jaw, so the head has form before the light gets
            // to it.
            var jaw = new Raster.LinearGrad(0, Y(0.20f), 0, Y(0.02f))
                .Stop(0, RG.Rgba(255, 255, 255, 0)).Stop(1, RG.Rgba(92, 62, 54, .34f));
            c.FillRect(0, Y(0.20f), W, Y(0.02f) - Y(0.20f), jaw);

            if (stubble)
                for (int i = 0; i < 5200; i++)
                {
                    // only on the jaw and chin, and only on the front half of the head
                    float u = (float)r.Rand(0.30, 0.70), v = (float)r.Rand(0.12, 0.36);
                    float fade = Mathf.Min(1f, (0.36f - v) * 4f);
                    if (r.Next() > fade) continue;
                    c.FillCircle(X(u), Y(v), (float)r.Rand(0.6, 1.5),
                                 new Raster.Solid(hairRG.WithA((float)r.Rand(0.10, 0.28))));
                }

            // pores, over everything
            for (int i = 0; i < 4200; i++)
                c.FillCircle((float)r.Rand(0, W), (float)r.Rand(0, H), (float)r.Rand(0.5, 1.4),
                             new Raster.Solid(RG.Rgba(150, 108, 92, (float)r.Rand(0.03, 0.10))));

            return c.ToTexture("face", wrap: TextureWrapMode.Clamp);
        });

        /// <summary>Woven cloth. Subtle: at two metres a shirt is a value, not a weave.</summary>
        public static Texture2D ClothNormal() => Get("clothNrm", () =>
        {
            var c = new Raster(128, 128);
            var r = Seed("clothNrm");
            int W = c.W, H = c.H;
            c.Clear(RG.Rgb(128, 128, 128));
            for (int y = 0; y < H; y += 3)
                c.FillRect(0, y, W, 1.4f, RG.Rgba(148, 148, 148, .55f));
            for (int x = 0; x < W; x += 3)
                c.FillRect(x, 0, 1.4f, H, RG.Rgba(108, 108, 108, .55f));
            // folds, which are the part you actually see
            for (int i = 0; i < 16; i++)
                Squiggle(c, r, 6, RG.Rgba(102, 102, 102, .35f), (float)r.Rand(2, 5), 10, 12, 26);
            return c.ToNormalMap("clothNormal", 0.9f);
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

        // ------------------------------------------------------------ tiling noise

        /// <summary>
        /// Value noise that repeats exactly every <paramref name="period"/> cells.
        ///
        /// The ordinary hash noise used by <see cref="Terrain"/> cannot be used for a
        /// texture: a texture that does not wrap shows its edge as a seam, and the cloud
        /// deck is sampled over hundreds of repeats across one sky. Folding the lattice
        /// coordinate into the period before hashing is what makes the field periodic —
        /// the interpolation then wraps with it for free.
        /// </summary>
        /// <summary>Tiling value noise with independent periods across and along.</summary>
        static float PNoise2(float u, float v, int px, int py, uint seed)
        {
            float x = u * px, y = v * py;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float a = xf * xf * (3f - 2f * xf), b = yf * yf * (3f - 2f * yf);
            float h00 = PHash2(xi, yi, px, py, seed), h10 = PHash2(xi + 1, yi, px, py, seed);
            float h01 = PHash2(xi, yi + 1, px, py, seed), h11 = PHash2(xi + 1, yi + 1, px, py, seed);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, a), Mathf.Lerp(h01, h11, a), b);
        }

        static float PHash2(int x, int y, int px, int py, uint seed)
        {
            x = ((x % px) + px) % px;
            y = ((y % py) + py) % py;
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + seed;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        // ------------------------------------------------------------ sky

        /// <summary>
        /// The cloud deck, as one tiling sheet the sky shader projects onto a flat ceiling.
        ///
        /// Four channels, none of them a colour:
        /// <list type="bullet">
        /// <item>A — cumulus coverage, the alpha the deck is composited with.</item>
        /// <item>R — how brightly that cumulus is lit. Thin edges are near 1 and dense cores
        /// near 0, because a cloud seen from below is bright where you can nearly see
        /// through it and grey where you cannot. Baking it means the shader gets internal
        /// form for one fetch instead of marching a density field.</item>
        /// <item>G — cirrus coverage, stretched eight to one so it reads as wind-drawn
        /// streaks rather than a second layer of the same puffs.</item>
        /// <item>B — cirrus brightness.</item>
        /// </list>
        ///
        /// A large-scale mask modulates the cumulus threshold, so the puffs gather into
        /// fronts with clear sky between them. Without it the coverage is uniform and the
        /// sky reads as wallpaper — the giveaway being that no part of it is empty.
        /// </summary>
        public static Texture2D CloudDeck() => Get("clouddeck", () =>
        {
            const int N = 512;
            var px = new Color32[N * N];

            for (int y = 0; y < N; y++)
            {
                float v = y / (float)N;
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N;

                    // where the weather is: slow, large, and the only thing that decides
                    // whether this part of the sky has cloud in it at all
                    float front = PFbmA(u, v, 2, 2, 1201u);
                    float cover = Mathf.Lerp(0.66f, 0.34f, Smooth01(Mathf.InverseLerp(0.35f, 0.72f, front)));

                    float d = PFbmA(u, v, 4, 6, 77u);
                    float a = Smooth01(Mathf.InverseLerp(cover, cover + 0.20f, d));
                    // dense cores are the part you cannot see through
                    float lit = 1f - Smooth01(Mathf.InverseLerp(cover + 0.10f, cover + 0.42f, d));
                    // a little high-frequency billow so the interior is not a flat wash
                    lit = Mathf.Clamp01(lit * 0.82f + PFbmA(u, v, 24, 3, 913u) * 0.30f);

                    float w = PFbmB(u, v, 3, 24, 5, 4441u);
                    float wa = Smooth01(Mathf.InverseLerp(0.52f, 0.78f, w)) * 0.75f;

                    px[y * N + x] = new Color32(
                        (byte)(Mathf.Clamp01(lit) * 255f),
                        (byte)(Mathf.Clamp01(wa) * 255f),
                        (byte)(Mathf.Clamp01(w) * 255f),
                        (byte)(Mathf.Clamp01(a) * 255f));
                }
            }

            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true)
            {
                name = "cloudDeck",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        });

        /// <summary>
        /// The same weather, as a light cookie.
        ///
        /// A cloud deck the ground knows nothing about is a painted ceiling. Putting the
        /// field on the sun instead — as a repeating directional cookie that drifts —
        /// drags shadow across the street, the fields and the hills, and it is the one
        /// thing that makes the sky and the world look like they are in the same place.
        /// It costs one texture lookup inside the light loop.
        ///
        /// Drawn from the same seeds as <see cref="CloudDeck"/>, so the shadows have the
        /// same size and spacing as the clouds overhead. They cannot line up exactly —
        /// the deck is projected onto a ceiling from the camera and this is projected onto
        /// the ground from the sun — and it does not matter: nobody can check, and what
        /// the eye is reading is that both are the same weather.
        ///
        /// The edges are much softer than the deck's, and the darkest patch only reaches
        /// 0.55. A cloud shadow on a clear day is a dimming, not a silhouette; the sky is
        /// still lighting the ground underneath it.
        /// </summary>
        public static Texture2D CloudShadow() => Get("cloudshadow", () =>
        {
            const int N = 256;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                float v = y / (float)N;
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N;
                    float front = PFbmA(u, v, 2, 2, 1201u);
                    float cover = Mathf.Lerp(0.66f, 0.34f,
                        Smooth01(Mathf.InverseLerp(0.35f, 0.72f, front)));
                    float d = PFbmA(u, v, 4, 5, 77u);
                    // Biased clear of cloud and floored at 0.70. The first version matched
                    // the deck's coverage and bottomed out at 0.55, which put roughly half
                    // the world under a 45% dimming at any moment — that is not weather,
                    // that is turning the sun down. What is wanted is a few slow patches
                    // crossing an otherwise sunlit street.
                    float a = Smooth01(Mathf.InverseLerp(cover + 0.06f, cover + 0.34f, d));
                    byte b = (byte)(Mathf.Lerp(1f, 0.70f, a) * 255f);
                    px[y * N + x] = new Color32(b, b, b, 255);
                }
            }

            // linear, not sRGB: this is a multiplier on light, not a colour to look at
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true)
            {
                name = "cloudShadow",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        });

        static float Smooth01(float t) => t * t * (3f - 2f * t);

        /// <summary>Isotropic tiling fbm.</summary>
        static float PFbmA(float u, float v, int basePeriod, int octaves, uint seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int p = basePeriod;
            for (int i = 0; i < octaves; i++)
            {
                sum += PNoise2(u, v, p, p, seed + (uint)i * 7919u) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        /// <summary>Tiling fbm with independent periods, for wind-stretched streaks.</summary>
        static float PFbmB(float u, float v, int px0, int py0, int octaves, uint seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int px = px0, py = py0;
            for (int i = 0; i < octaves; i++)
            {
                sum += PNoise2(u, v, px, py, seed + (uint)i * 6151u) * amp;
                norm += amp;
                amp *= 0.5f;
                px *= 2; py *= 2;
            }
            return sum / norm;
        }

        // ------------------------------------------------------------ horizon

        /// <summary>
        /// The distant horizon silhouette, wrapped once round a cylinder past the fog
        /// distance.
        ///
        /// Three things were wrong with the version this replaces, and all three came from
        /// forgetting how big a pixel of this texture is once it is 640 m away and 150 m
        /// tall. The ridge trees were 6 px circles on 2 px stalks, which is a seven-metre
        /// ball on a two-metre stick, spaced ninety metres apart — the mushrooms. The water
        /// tower was 46 px across, so forty-six metres wide. And the texture wrapped three
        /// times, so there were three of it.
        ///
        /// This draws the ridges as columns rather than as filled polygons, which is what
        /// buys the thing that actually makes distant hills read as distant: aerial
        /// perspective *within* each ridge. Haze pools in the low ground, so a hill is
        /// closest to its own colour along the crest and closest to the fog colour at its
        /// foot. A flat silhouette has no such gradient, which is why flat silhouettes look
        /// like cardboard.
        /// </summary>
        public static Texture2D HillsSkyline(Color fog) => Get("skyhills" + ColorUtility.ToHtmlStringRGB(fog), () =>
        {
            // One repeat around the whole ring: 4021 m of circumference over 4096 px is
            // very nearly a metre per pixel across, and 150 m over 128 px is 1.2 m up.
            const int W = 4096, H = 128;
            var c = new Raster(W, H);
            var r = Seed("skyline");
            c.ClearTransparent();

            var fc = RG.FromColor(fog);
            RG Shade(float mix) => fc.Lerp(RG.Rgb(24, 34, 46), mix);

            // Every frequency has to complete a whole number of cycles across the canvas
            // or the wrap point is a cliff in the ridge.
            const float TwoPi = Mathf.PI * 2f;
            float K(float cycles) => cycles * TwoPi / W;

            // (mix, crest, amp, treeline, fade)
            foreach (var (mix, baseY, amp, trees, fade) in new[]
                     {
                         (0.10f, 60f, 15f, 0.0f, 34f),
                         (0.24f, 78f, 12f, 1.6f, 26f),
                         (0.46f, 96f,  9f, 2.6f, 20f),
                     })
            {
                var crestCol = Shade(mix);
                var footCol = Shade(mix * 0.30f);
                float phase = mix * 37f;

                float Ridge(float x)
                    => baseY - Mathf.Sin(x * K(3) + phase) * amp
                             - Mathf.Sin(x * K(7) + phase * 2.3f) * amp * 0.42f
                             - Mathf.Sin(x * K(17) + phase * 0.7f) * amp * 0.16f
                             - PNoise2(x / W, 0.37f, 41, 1, (uint)(mix * 1000f)) * amp * 0.5f;

                for (int x = 0; x < W; x++)
                {
                    float y = Ridge(x);

                    // The treeline. A forested crest is a continuous serration a few
                    // metres deep, not a row of lollipops — so the bumps are two to four
                    // pixels and they overlap.
                    if (trees > 0f)
                        y -= trees * (0.45f + 0.55f * PNoise2(x / (float)W, 0.11f, 900, 1, 7u));

                    var grad = new Raster.LinearGrad(0, y, 0, y + fade)
                        .Stop(0, crestCol)
                        .Stop(1, footCol);
                    c.FillRect(x, y, 1, H - y, grad);
                }
            }

            // One landmark, once, at a believable size: a water tower about ten metres
            // across and twenty tall, standing on the nearest ridge.
            var tower = Shade(0.52f);
            float tx = 2380f, ty = 84f;
            c.FillRect(tx - 4, ty, 1.6f, 14, tower);
            c.FillRect(tx + 4, ty, 1.6f, 14, tower);
            c.FillEllipse(tx, ty - 1, 5.5f, 3.5f, new Raster.Solid(tower));

            // A couple of far grain silos, small enough to be scenery rather than a subject
            foreach (float sx in new[] { 700f, 3310f })
            {
                var silo = Shade(0.34f);
                c.FillRect(sx, 70, 2.2f, 9, silo);
                c.FillRect(sx + 3.4f, 72, 2.2f, 7, silo);
            }

            // The last hundred metres of air between here and the ring: everything low in
            // the frame washes out, which is what stops the ring's base being a hard line
            // laid across the field.
            var haze = new Raster.LinearGrad(0, H * 0.52f, 0, H)
                .Stop(0, fc.WithA(0f))
                .Stop(1, fc.WithA(0.92f));
            c.FillRect(0, H * 0.52f, W, H * 0.48f, haze);

            return c.ToTexture("skyline", wrap: TextureWrapMode.Repeat);
        });
    }
}
