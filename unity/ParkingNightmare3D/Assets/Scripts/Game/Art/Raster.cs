using System;
using System.Collections.Generic;
using UnityEngine;

namespace PN3D.Game.Art
{
    /// <summary>
    /// A tiny software rasteriser with just enough of the Canvas 2D API to port the
    /// web build's texture painters (<c>Assets.*</c> in <c>src/n3_c.js</c>) directly.
    ///
    /// Why not author PNGs instead: the reference generates every surface in a canvas at
    /// load, so the *painting code* is the art source. Porting the painters keeps the two
    /// builds visually in step and keeps the repo asset-free — nothing to license, nothing
    /// to re-export when a colour changes. It also means a district palette swap is a data
    /// change, exactly as it is on the web.
    ///
    /// Colour space matches canvas: bytes are sRGB and blending is source-over on those
    /// bytes, so the ported constants land on the same pixels. Textures are therefore
    /// uploaded as sRGB (<c>linear: false</c>), never as raw data.
    /// </summary>
    public sealed class Raster
    {
        public readonly int W, H;

        // sRGB channels 0..255 stored PREMULTIPLIED by _a (0..1), row 0 at the top like
        // canvas. Premultiplied because the skyline silhouette composites over a
        // transparent canvas, and straight alpha loses colour wherever alpha is near zero.
        readonly float[] _r, _g, _b, _a;

        /// <summary>Canvas <c>globalAlpha</c>: scales the source alpha of every op.</summary>
        public float GlobalAlpha = 1f;

        public Raster(int w, int h)
        {
            W = w; H = h;
            _r = new float[w * h];
            _g = new float[w * h];
            _b = new float[w * h];
            _a = new float[w * h];
        }

        // ---------------------------------------------------------------- paints

        public interface IPaint { RGBA At(float x, float y); }

        public struct RGBA
        {
            public float R, G, B, A;   // R/G/B in 0..255, A in 0..1
            public RGBA(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }

            public static RGBA Rgb(float r, float g, float b) => new RGBA(r, g, b, 1f);
            public static RGBA Rgba(float r, float g, float b, float a) => new RGBA(r, g, b, a);

            /// <summary>"#rgb" / "#rrggbb", the form the district palettes use.</summary>
            public static RGBA Hex(string hex, float a = 1f)
            {
                if (!ColorUtility.TryParseHtmlString(hex, out var c))
                    throw new ArgumentException("bad colour " + hex);
                // TryParseHtmlString hands back the sRGB bytes as 0..1 floats
                return new RGBA(c.r * 255f, c.g * 255f, c.b * 255f, a);
            }

            public static RGBA FromColor(Color c, float a = 1f)
                => new RGBA(c.r * 255f, c.g * 255f, c.b * 255f, a);

            public Color ToColor() => new Color(R / 255f, G / 255f, B / 255f, A);

            public RGBA WithA(float a) => new RGBA(R, G, B, a);

            /// <summary>Multiply the channels, as THREE.Color.multiplyScalar does.</summary>
            public RGBA Scale(float f) => new RGBA(
                Mathf.Clamp(R * f, 0, 255), Mathf.Clamp(G * f, 0, 255), Mathf.Clamp(B * f, 0, 255), A);

            public RGBA Lerp(RGBA o, float t) => new RGBA(
                Mathf.Lerp(R, o.R, t), Mathf.Lerp(G, o.G, t), Mathf.Lerp(B, o.B, t), Mathf.Lerp(A, o.A, t));
        }

        public readonly struct Solid : IPaint
        {
            readonly RGBA _c;
            public Solid(RGBA c) { _c = c; }
            public RGBA At(float x, float y) => _c;
        }

        /// <summary>Gradient stop list shared by the linear and radial paints.</summary>
        public sealed class Stops
        {
            readonly List<(float T, RGBA C)> _s = new();

            public Stops Add(float t, RGBA c) { _s.Add((t, c)); return this; }

            public RGBA Eval(float t)
            {
                if (_s.Count == 0) return new RGBA(0, 0, 0, 0);
                if (t <= _s[0].T) return _s[0].C;
                for (int i = 1; i < _s.Count; i++)
                {
                    if (t <= _s[i].T)
                    {
                        var (t0, c0) = _s[i - 1];
                        var (t1, c1) = _s[i];
                        float u = t1 > t0 ? (t - t0) / (t1 - t0) : 0f;
                        return c0.Lerp(c1, u);
                    }
                }
                return _s[_s.Count - 1].C;
            }
        }

        public sealed class LinearGrad : IPaint
        {
            readonly float _x0, _y0, _dx, _dy, _len2;
            public readonly Stops S = new();

            public LinearGrad(float x0, float y0, float x1, float y1)
            {
                _x0 = x0; _y0 = y0; _dx = x1 - x0; _dy = y1 - y0;
                _len2 = _dx * _dx + _dy * _dy;
            }

            public LinearGrad Stop(float t, RGBA c) { S.Add(t, c); return this; }

            public RGBA At(float x, float y)
            {
                if (_len2 <= 1e-9f) return S.Eval(0f);
                float t = ((x - _x0) * _dx + (y - _y0) * _dy) / _len2;
                return S.Eval(Mathf.Clamp01(t));
            }
        }

        /// <summary>
        /// Concentric radial gradient. The reference only ever calls
        /// <c>createRadialGradient(x, y, r0, x, y, r1)</c>, so the general two-circle form
        /// is deliberately not implemented — it would be dead code that could still drift.
        /// </summary>
        public sealed class RadialGrad : IPaint
        {
            readonly float _cx, _cy, _r0, _r1;
            public readonly Stops S = new();

            public RadialGrad(float cx, float cy, float r0, float r1)
            {
                _cx = cx; _cy = cy; _r0 = r0; _r1 = Mathf.Max(r1, r0 + 1e-4f);
            }

            public RadialGrad Stop(float t, RGBA c) { S.Add(t, c); return this; }

            public RGBA At(float x, float y)
            {
                float d = Mathf.Sqrt((x - _cx) * (x - _cx) + (y - _cy) * (y - _cy));
                return S.Eval(Mathf.Clamp01((d - _r0) / (_r1 - _r0)));
            }
        }

        // ---------------------------------------------------------------- blending

        void Blend(int px, int py, in RGBA src, float coverage)
        {
            if (px < 0 || py < 0 || px >= W || py >= H) return;
            float a = src.A * coverage * GlobalAlpha;
            if (a <= 0f) return;
            if (a > 1f) a = 1f;
            int i = py * W + px;
            float inv = 1f - a;
            _r[i] = src.R * a + _r[i] * inv;
            _g[i] = src.G * a + _g[i] * inv;
            _b[i] = src.B * a + _b[i] * inv;
            _a[i] = a + _a[i] * inv;
        }

        // ---------------------------------------------------------------- shapes

        /// <summary>Opaque fill of the whole canvas — canvas <c>fillRect(0,0,W,H)</c>.</summary>
        public void Clear(RGBA c)
        {
            for (int i = 0; i < _r.Length; i++) { _r[i] = c.R; _g[i] = c.G; _b[i] = c.B; _a[i] = 1f; }
        }

        /// <summary>Canvas <c>clearRect</c>: fully transparent, for the silhouette layers.</summary>
        public void ClearTransparent()
        {
            System.Array.Clear(_r, 0, _r.Length);
            System.Array.Clear(_g, 0, _g.Length);
            System.Array.Clear(_b, 0, _b.Length);
            System.Array.Clear(_a, 0, _a.Length);
        }

        public void FillRect(float x, float y, float w, float h, RGBA c) => FillRect(x, y, w, h, new Solid(c));

        public void FillRect<T>(float x, float y, float w, float h, T paint) where T : IPaint
        {
            if (w < 0) { x += w; w = -w; }
            if (h < 0) { y += h; h = -h; }
            int x0 = Mathf.FloorToInt(x), x1 = Mathf.CeilToInt(x + w);
            int y0 = Mathf.FloorToInt(y), y1 = Mathf.CeilToInt(y + h);

            for (int py = y0; py < y1; py++)
            {
                if (py < 0 || py >= H) continue;
                // vertical coverage of this pixel row by the rect
                float cy = Mathf.Min(py + 1f, y + h) - Mathf.Max(py, y);
                if (cy <= 0f) continue;
                for (int px = x0; px < x1; px++)
                {
                    if (px < 0 || px >= W) continue;
                    float cx = Mathf.Min(px + 1f, x + w) - Mathf.Max(px, x);
                    if (cx <= 0f) continue;
                    Blend(px, py, paint.At(px + 0.5f, py + 0.5f), Mathf.Min(1f, cx) * Mathf.Min(1f, cy));
                }
            }
        }

        public void FillCircle<T>(float cx, float cy, float r, T paint) where T : IPaint
            => FillEllipse(cx, cy, r, r, paint);

        public void FillEllipse<T>(float cx, float cy, float rx, float ry, T paint) where T : IPaint
        {
            int x0 = Mathf.FloorToInt(cx - rx) - 1, x1 = Mathf.CeilToInt(cx + rx) + 1;
            int y0 = Mathf.FloorToInt(cy - ry) - 1, y1 = Mathf.CeilToInt(cy + ry) + 1;
            for (int py = Mathf.Max(0, y0); py < Mathf.Min(H, y1); py++)
            {
                for (int px = Mathf.Max(0, x0); px < Mathf.Min(W, x1); px++)
                {
                    float dx = (px + 0.5f - cx) / rx, dy = (py + 0.5f - cy) / ry;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // approximate the edge in pixels so the rim antialiases
                    float scale = Mathf.Min(rx, ry);
                    float cov = Mathf.Clamp01((1f - d) * scale + 0.5f);
                    if (cov <= 0f) continue;
                    Blend(px, py, paint.At(px + 0.5f, py + 0.5f), cov);
                }
            }
        }

        /// <summary>Axis-aligned rect rotated about its own centre — the mow bands.</summary>
        public void FillRotatedRect(float cx, float cy, float w, float h, float angle, RGBA c)
        {
            float ca = Mathf.Cos(-angle), sa = Mathf.Sin(-angle);
            float rad = Mathf.Sqrt(w * w + h * h) * 0.5f + 2f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rad)), x1 = Mathf.Min(W, Mathf.CeilToInt(cx + rad));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - rad)), y1 = Mathf.Min(H, Mathf.CeilToInt(cy + rad));
            for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                {
                    float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                    float lx = dx * ca - dy * sa, ly = dx * sa + dy * ca;
                    if (Mathf.Abs(lx) <= w * 0.5f && Mathf.Abs(ly) <= h * 0.5f)
                        Blend(px, py, c, 1f);
                }
        }

        /// <summary>Round-capped segment, the capsule distance field. Canvas lineTo runs.</summary>
        public void StrokeSegment(float ax, float ay, float bx, float by, float width, RGBA c)
        {
            float hw = width * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, bx) - hw - 1));
            int x1 = Mathf.Min(W, Mathf.CeilToInt(Mathf.Max(ax, bx) + hw + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, by) - hw - 1));
            int y1 = Mathf.Min(H, Mathf.CeilToInt(Mathf.Max(ay, by) + hw + 1));

            float ex = bx - ax, ey = by - ay;
            float len2 = ex * ex + ey * ey;

            for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                {
                    float qx = px + 0.5f - ax, qy = py + 0.5f - ay;
                    float t = len2 > 1e-9f ? Mathf.Clamp01((qx * ex + qy * ey) / len2) : 0f;
                    float dx = qx - ex * t, dy = qy - ey * t;
                    float cov = Mathf.Clamp01(hw + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    if (cov > 0f) Blend(px, py, c, cov);
                }
        }

        public void StrokePolyline(IReadOnlyList<Vector2> pts, float width, RGBA c)
        {
            for (int i = 1; i < pts.Count; i++)
                StrokeSegment(pts[i - 1].x, pts[i - 1].y, pts[i].x, pts[i].y, width, c);
        }

        /// <summary>
        /// Even-odd scanline polygon fill with 4 sub-scanlines per row, so the horizon
        /// ridge antialiases instead of stair-stepping across 2048 px.
        /// </summary>
        public void FillPolygon(IReadOnlyList<Vector2> pts, RGBA c)
        {
            if (pts.Count < 3) return;

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts) { if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y; }
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(H, Mathf.CeilToInt(maxY));

            const int Sub = 4;
            var cov = new float[W];
            var xs = new List<float>();

            for (int py = y0; py < y1; py++)
            {
                System.Array.Clear(cov, 0, W);
                for (int s = 0; s < Sub; s++)
                {
                    float sy = py + (s + 0.5f) / Sub;
                    xs.Clear();
                    for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
                    {
                        Vector2 a = pts[j], b = pts[i];
                        if ((a.y <= sy) == (b.y <= sy)) continue;
                        xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                    if (xs.Count < 2) continue;
                    xs.Sort();
                    for (int k = 0; k + 1 < xs.Count; k += 2)
                        AccumulateSpan(cov, xs[k], xs[k + 1], 1f / Sub);
                }
                for (int px = 0; px < W; px++)
                    if (cov[px] > 0f) Blend(px, py, c, Mathf.Min(1f, cov[px]));
            }
        }

        static void AccumulateSpan(float[] cov, float xa, float xb, float weight)
        {
            int w = cov.Length;
            if (xb <= 0 || xa >= w) return;
            xa = Mathf.Max(xa, 0); xb = Mathf.Min(xb, w);
            int ia = Mathf.FloorToInt(xa), ib = Mathf.CeilToInt(xb);
            for (int x = ia; x < ib && x < w; x++)
            {
                if (x < 0) continue;
                float overlap = Mathf.Min(x + 1f, xb) - Mathf.Max(x, xa);
                if (overlap > 0f) cov[x] += overlap * weight;
            }
        }

        public void StrokeRect(float x, float y, float w, float h, float width, RGBA c)
        {
            StrokeSegment(x, y, x + w, y, width, c);
            StrokeSegment(x + w, y, x + w, y + h, width, c);
            StrokeSegment(x + w, y + h, x, y + h, width, c);
            StrokeSegment(x, y + h, x, y, width, c);
        }

        // ---------------------------------------------------------------- output

        /// <summary>
        /// Upload as sRGB. Canvas painting happens in gamma space, so the texture must be
        /// tagged sRGB or every ported colour constant comes out washed or muddy.
        /// </summary>
        public Texture2D ToTexture(string name, bool mips = true, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, mips, linear: false)
            {
                name = name,
                wrapMode = wrap,
                anisoLevel = 8,
                filterMode = FilterMode.Trilinear,
            };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                // canvas row 0 is the top; Unity texture row 0 is the bottom
                int src = (H - 1 - y) * W, dst = y * W;
                for (int x = 0; x < W; x++)
                {
                    int i = src + x;
                    // un-premultiply back to straight alpha for upload
                    float a = _a[i];
                    float inv = a > 1e-4f ? 1f / a : 0f;
                    px[dst + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(_r[i] * inv), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(_g[i] * inv), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(_b[i] * inv), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(mips, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>
        /// Read the painted image back as a tangent-space normal map.
        ///
        /// The reference hands its aggregate noise to a Three.js <c>bumpMap</c>, which
        /// derives slopes from a height field. URP's Lit shader has no bump input, only a
        /// normal map, so the conversion happens here — by central difference on the
        /// buffer we still hold, rather than by blitting the uploaded texture back off the
        /// GPU. That keeps it working under <c>-nographics</c>, where there is no device
        /// to blit through.
        /// </summary>
        public Texture2D ToNormalMap(string name, float strength)
        {
            float L(int x, int y)
            {
                int xi = ((x % W) + W) % W, yi = ((y % H) + H) % H;
                int i = yi * W + xi;
                float inv = _a[i] > 1e-4f ? 1f / _a[i] : 0f;
                return (_r[i] * 0.299f + _g[i] * 0.587f + _b[i] * 0.114f) * inv / 255f;
            }

            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                int dst = (H - 1 - y) * W;   // same vertical flip as ToTexture
                for (int x = 0; x < W; x++)
                {
                    float dx = (L(x + 1, y) - L(x - 1, y)) * strength;
                    float dy = (L(x, y + 1) - L(x, y - 1)) * strength;
                    var n = new Vector3(-dx, dy, 1f).normalized;
                    px[dst + x] = new Color32(
                        (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, linear: true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 4,
                filterMode = FilterMode.Trilinear,
            };
            tex.SetPixels32(px);
            tex.Apply(true, makeNoLongerReadable: true);
            return tex;
        }
    }
}
