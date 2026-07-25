using System;

namespace PN3D.Core
{
    public struct Vec2
    {
        public double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }
    }

    /// <summary>
    /// Oriented bounding box on the 2D physics plane.
    /// Ports <c>obbCorners</c> (src/n3_b.js:80) and <c>pointInObb</c> (src/n3_b.js:117).
    /// </summary>
    public struct Obb
    {
        public double X, Y;   // centre
        public double H;      // heading, radians
        public double Hl;     // half-length, along the heading
        public double Hw;     // half-width, across it

        public Obb(double x, double y, double h, double hl, double hw)
        {
            X = x; Y = y; H = h; Hl = hl; Hw = hw;
        }

        /// <summary>
        /// Corners in the reference implementation's order: front-left, front-right,
        /// rear-right, rear-left. Order is not load-bearing for the parking check (it
        /// tests all four) but keeping it identical makes traces comparable.
        /// </summary>
        public void Corners(Vec2[] outCorners)
        {
            if (outCorners == null || outCorners.Length < 4)
                throw new ArgumentException("need an array of at least 4", nameof(outCorners));

            double c = Math.Cos(H), s = Math.Sin(H);
            double lx = c * Hl, ly = s * Hl;
            double wx = -s * Hw, wy = c * Hw;

            outCorners[0] = new Vec2(X + lx + wx, Y + ly + wy);
            outCorners[1] = new Vec2(X + lx - wx, Y + ly - wy);
            outCorners[2] = new Vec2(X - lx - wx, Y - ly - wy);
            outCorners[3] = new Vec2(X - lx + wx, Y - ly + wy);
        }

        public Vec2[] Corners()
        {
            var c = new Vec2[4];
            Corners(c);
            return c;
        }

        /// <summary>Is the point inside, allowing <paramref name="eps"/> metres of slack?</summary>
        public bool Contains(double px, double py, double eps = 0.0)
        {
            double c = Math.Cos(H), s = Math.Sin(H);
            double dx = px - X, dy = py - Y;
            double lx = dx * c + dy * s;
            double ly = -dx * s + dy * c;
            return Math.Abs(lx) <= Hl + eps && Math.Abs(ly) <= Hw + eps;
        }
    }
}
