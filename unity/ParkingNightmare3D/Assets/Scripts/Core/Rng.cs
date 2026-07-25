using System;
using System.Collections.Generic;

namespace PN3D.Core
{
    /// <summary>
    /// mulberry32, ported from <c>src/n3_b.js:32</c>.
    ///
    /// The web build's traffic and pedestrian AI actually call bare `Math.random()`
    /// (`rand`/`chance`/`pick`, src/n3_b.js:14-17) — only the static level layout uses a
    /// seeded stream. Routing the AI through a seeded stream here is a deliberate
    /// improvement, not a port error: it makes runs reproducible, which the harness needs
    /// to diff behaviour frame by frame, and which the shareable challenge codes (§7)
    /// want anyway.
    ///
    /// Draw ORDER is part of the contract. Any branch that consumes a different number of
    /// draws desynchronises the whole stream, so the port reproduces short-circuit
    /// evaluation exactly — see the comments in <see cref="TrafficSystem"/>.
    /// </summary>
    public sealed class Rng
    {
        uint _a;

        public Rng(uint seed) { _a = seed; }

        /// <summary>Uniform in [0, 1).</summary>
        public double Next()
        {
            unchecked
            {
                _a += 0x6D2B79F5u;
                uint a = _a;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public double Rand(double a, double b) => a + Next() * (b - a);

        public bool Chance(double p) => Next() < p;

        public T Pick<T>(IList<T> arr) => arr[(int)Math.Floor(Next() * arr.Count)];

        public int RandI(int a, int b) => (int)Math.Floor(Rand(a, b + 1));
    }
}
