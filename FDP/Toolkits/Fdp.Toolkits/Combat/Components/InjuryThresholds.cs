using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2155</c> (R-263 D1 / R-2011, docs/DESIGN_Injury_States.md) — where this TYPE's people become wounded and incapacitated,
    /// and how fast an incapacitated one bleeds out. A pure type fact: stamped by <c>InjuryTkbTranslator</c> from <c>Gen.Injury</c> on every
    /// node at spawn, never sent. ⭐ ABSENT = no injury band (ok until dead), the default for everything that does not declare it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.InjuryThresholds)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct InjuryThresholds
    {
        /// <summary>Health fraction below which the person is wounded.</summary>
        public float WoundedBelow;

        /// <summary>Health fraction below which the person is incapacitated.</summary>
        public float IncapacitatedBelow;

        /// <summary>Health FRACTION lost per second while incapacitated (<c>IncapacitatedBelow / BleedOutSeconds</c>); 0 = no bleed-out.</summary>
        public float BleedPerSecond;

        /// <summary>⭐ CE-1082 (slice I-b) — a wounded person's top speed as a share of his healthy one; 0 (unset) = not slowed.</summary>
        public float WoundedSpeedFactor;
    }
}
