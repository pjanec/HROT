using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐⭐ <c>CE-3260</c> (BR-7, DA-3, R-2035; docs/DESIGN_Sensors_And_Doctrine.md §5.7) — how long a man must stay seen before he is
    /// ACQUIRED: longer with range and with how little of him shows, shorter for a better observer; 0 for a fully exposed man within
    /// 100 m. The sight test writes the exposure into the result (<see cref="Encode"/>), the memory stage reads it (<see cref="ExposureOf"/>)
    /// and stores <see cref="DelaySeconds"/> on the contact; <c>ContactHysteresis</c> holds the acquisition until it has passed.
    /// </summary>
    public static class Acquisition
    {
        /// <summary>The result <c>Flags</c> bits: the clear body points (8–11) and the stance's points (12–14). ⚠ A VISUAL / thermal result's
        /// bits — the acoustic sensor's bits 8–12 mean its kind and class (its results never reach this reader).</summary>
        public const short PointsShift = 8, PointsMask = 0xF << 8, TotalShift = 12, TotalMask = 0x7 << 12;

        /// <summary>The delay at the reference (a Regular observer, a man half hidden, or 500 m off).</summary>
        public const float BaseSeconds = 2f;

        /// <summary>Within this range a fully exposed man is acquired the tick he is seen (DA-3).</summary>
        public const float ExposedRangeM = 100f;

        /// <summary>The range beyond <see cref="ExposedRangeM"/> that adds one <see cref="BaseSeconds"/>.</summary>
        public const float RangeSpanM = 400f;

        // × on the delay by the observer's proficiency (through ProficiencyOf): Untrained 1.8 · Militia 1.4 · Regular 1.0 · Veteran 0.85 · Elite 0.7
        private static readonly float[] s_factor = { 1.8f, 1.4f, 1.0f, 0.85f, 0.7f };

        /// <summary>Writes <paramref name="clear"/> of <paramref name="total"/> body points into the result's flags.</summary>
        public static void Encode(ref EqsResult r, int clear, int total)
        {
            r.Flags = (short)((r.Flags & ~(PointsMask | TotalMask)) | ((Math.Clamp(clear, 0, 15) << PointsShift) & PointsMask)
                              | ((Math.Clamp(total, 0, 7) << TotalShift) & TotalMask));
            r.FlagsMeaningful = (short)(r.FlagsMeaningful | PointsMask | TotalMask);
        }

        /// <summary>Does <paramref name="r"/> carry an exposure (a sight-tested result: vision, thermal)? Only those wait (AQ-6).</summary>
        public static bool Carries(in EqsResult r) => (r.FlagsMeaningful & TotalMask) != 0;

        /// <summary>The dwell <paramref name="r"/> needs before its contact is acquired: <see cref="DelaySeconds"/> for a sight-tested result,
        /// 0 for any other sensor's (a radar, a host's own test) — they are acquired the tick they are seen, as before (AQ-6).</summary>
        public static float DwellOf(in EqsResult r, float rangeM, float observerFactor)
            => Carries(in r) ? DelaySeconds(rangeM, ExposureOf(in r), observerFactor) : 0f;

        /// <summary>The share of him that shows (0..1); 1 for a result that carries none (another sensor's, a host's own test).</summary>
        public static float ExposureOf(in EqsResult r)
        {
            if ((r.FlagsMeaningful & TotalMask) == 0) return 1f;
            int total = (r.Flags & TotalMask) >> TotalShift;
            int clear = (r.Flags & PointsMask) >> PointsShift;
            return total <= 0 ? 1f : Math.Clamp(clear / (float)total, 0f, 1f);
        }

        /// <summary>The observer's factor on the delay, read through <see cref="ProficiencyOf"/> (Regular and none ⇒ 1.0).</summary>
        public static float ObserverFactor(ISimulationView view, Entity observer) => ProficiencyOf.Lerp(s_factor, ProficiencyOf.Value(view, observer));

        /// <summary>
        /// AQ-2 — <c>BaseSeconds × (max(0, range − 100 m) / 400 m + (1 − exposure)) × factor</c>: a fully exposed man within 100 m ⇒ 0; one
        /// of three points at 300 m ≈ 2.3 s for a Regular observer; fully exposed at 300 m 1 s.
        /// </summary>
        public static float DelaySeconds(float rangeM, float exposure, float observerFactor)
            => BaseSeconds * (MathF.Max(0f, rangeM - ExposedRangeM) / RangeSpanM + (1f - Math.Clamp(exposure, 0f, 1f))) * observerFactor;
    }
}
