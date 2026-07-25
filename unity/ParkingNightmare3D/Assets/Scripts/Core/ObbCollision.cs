using System;

namespace PN3D.Core
{
    /// <summary>Minimum translation vector: push A out of B along (Nx, Ny) by Depth.</summary>
    public struct Mtv
    {
        public bool Hit;
        public double Nx, Ny, Depth;
    }

    /// <summary>Separating-axis test with MTV. Port of <c>obbVsObb</c>, src/n3_b.js:91.</summary>
    public static class ObbCollision
    {
        public static Mtv Test(Obb a, Obb b)
        {
            var ca = a.Corners();
            var cb = b.Corners();

            Span<double> axX = stackalloc double[4];
            Span<double> axY = stackalloc double[4];
            axX[0] = Math.Cos(a.H); axY[0] = Math.Sin(a.H);
            axX[1] = -Math.Sin(a.H); axY[1] = Math.Cos(a.H);
            axX[2] = Math.Cos(b.H); axY[2] = Math.Sin(b.H);
            axX[3] = -Math.Sin(b.H); axY[3] = Math.Cos(b.H);

            double minDepth = double.PositiveInfinity;
            double bestX = 0, bestY = 0;

            for (int i = 0; i < 4; i++)
            {
                double ax = axX[i], ay = axY[i];
                double aMin = double.PositiveInfinity, aMax = double.NegativeInfinity;
                double bMin = double.PositiveInfinity, bMax = double.NegativeInfinity;

                for (int k = 0; k < 4; k++)
                {
                    double d = ca[k].X * ax + ca[k].Y * ay;
                    if (d < aMin) aMin = d;
                    if (d > aMax) aMax = d;
                }
                for (int k = 0; k < 4; k++)
                {
                    double d = cb[k].X * ax + cb[k].Y * ay;
                    if (d < bMin) bMin = d;
                    if (d > bMax) bMax = d;
                }

                double overlap = Math.Min(aMax, bMax) - Math.Max(aMin, bMin);
                if (overlap <= 0) return default;   // separating axis found

                if (overlap < minDepth)
                {
                    minDepth = overlap;
                    double aC = (aMin + aMax) / 2.0, bC = (bMin + bMax) / 2.0;
                    if (aC < bC) { bestX = -ax; bestY = -ay; }
                    else { bestX = ax; bestY = ay; }
                }
            }

            return new Mtv { Hit = true, Nx = bestX, Ny = bestY, Depth = minDepth };
        }
    }
}
