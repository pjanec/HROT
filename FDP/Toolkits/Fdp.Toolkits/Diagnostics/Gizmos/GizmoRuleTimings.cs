using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ CE-4385 (handoff L-3) — opt-in per-RULE timing of the stateless gizmos, the "where does the gizmo time go" half of <c>HROT_FRAME_STATS=1</c> (the twin of
    /// <c>WindowTimings</c>, one layer down): measured on Windows 2026-11-10, <c>StatelessGizmoSystem</c> took 11.4 ms and 608 KB a frame on an idle 5.5 km scenario and
    /// the per-system profile could not say which rule. Keyed by the projector's type name. Off ⇒ one static bool read per rule, no allocation.
    /// </summary>
    public static class GizmoRuleTimings
    {
        private static readonly Dictionary<string, (double Ms, int Entities, long Bytes)> s_acc = new();

        /// <summary>On while a frame-stats log is wanted (the host's frame stats switch it).</summary>
        public static bool Enabled { get; set; }

        /// <summary>Adds one rule's pass: its time since <paramref name="start"/> (a <see cref="Stopwatch"/> timestamp), the entities it drew and the bytes it allocated.</summary>
        public static void Record(string rule, long start, int entities, long bytes)
        {
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            s_acc[rule] = s_acc.TryGetValue(rule, out var a) ? (a.Ms + ms, a.Entities + entities, a.Bytes + bytes) : (ms, entities, bytes);
        }

        /// <summary>The accumulated rules, largest time first; the accumulator is cleared.</summary>
        public static IReadOnlyList<(string Rule, double Ms, int Entities, long Bytes)> Drain()
        {
            var list = s_acc.Select(k => (k.Key, k.Value.Ms, k.Value.Entities, k.Value.Bytes)).OrderByDescending(x => x.Ms).ToList();
            s_acc.Clear();
            return list;
        }
    }
}
