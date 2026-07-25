namespace PN3D.Core
{
    /// <summary>Road cross-section constants (DESIGN_SPEC §2), from src/n3_d.js:9.</summary>
    public static class RoadGeom
    {
        public const double LaneW = 3.5;      // one traffic lane
        public const double ParkStrip = 2.3;  // curbside parking lane, each side
        public const double SidewalkW = 3.0;

        /// <summary>
        /// Road half-width including the parking strip. This is the reference line the
        /// curb gap is measured against.
        /// </summary>
        public static double HalfWidth(int lanes) => lanes * LaneW + ParkStrip;
    }
}
