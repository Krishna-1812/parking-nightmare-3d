using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PN3D.Validate
{
    /// <summary>Shared assertion helpers and failure accounting for the suites.</summary>
    internal static class Checks
    {
        /// <summary>
        /// Trig differs by an ULP or so between V8 and .NET and both suites integrate
        /// hundreds of steps, so a little accumulated drift is expected. Observed drift
        /// is orders of magnitude below this; anything approaching it is a real porting
        /// error, not float noise.
        /// </summary>
        public const double Eps = 1e-9;

        public static int Count;
        public static double MaxRelDrift;
        public static string MaxRelWhere = "(none)";
        public static readonly List<string> Failures = new List<string>();

        public static void Fail(string tag, string msg) => Failures.Add($"{tag}: {msg}");

        public static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        public static void Num(string tag, string field, double actual, double expected)
        {
            Count++;
            double diff = Math.Abs(actual - expected);
            double scale = Math.Max(1.0, Math.Abs(expected));
            double rel = diff / scale;
            if (rel > MaxRelDrift) { MaxRelDrift = rel; MaxRelWhere = $"{tag} {field}"; }
            if (diff > Eps * scale)
                Fail(tag, $"{field}: got {F(actual)} expected {F(expected)} (diff {F(diff)})");
        }

        public static void NumOpt(string tag, string field, double? actual, double? expected)
        {
            Count++;
            if (!actual.HasValue && !expected.HasValue) return;
            if (actual.HasValue != expected.HasValue)
            {
                Fail(tag, $"{field}: got {(actual.HasValue ? F(actual.Value) : "absent")} " +
                          $"expected {(expected.HasValue ? F(expected.Value) : "absent")}");
                return;
            }
            Num(tag, field, actual.Value, expected.Value);
        }

        public static void Int(string tag, string field, int actual, int expected)
        {
            Count++;
            if (actual != expected) Fail(tag, $"{field}: got {actual} expected {expected}");
        }

        public static void Bool(string tag, string field, bool actual, bool expected)
        {
            Count++;
            if (actual != expected) Fail(tag, $"{field}: got {actual} expected {expected}");
        }

        public static void Str(string tag, string field, string actual, string expected)
        {
            Count++;
            if (actual != expected) Fail(tag, $"{field}: got '{actual}' expected '{expected}'");
        }

        public static string FindRepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "design-spec"))) d = d.Parent;
            if (d == null)
                throw new DirectoryNotFoundException("could not locate repo root (no design-spec/ above cwd)");
            return d.FullName;
        }
    }
}
