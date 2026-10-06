using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// ⭐ <c>CE-3092</c> — this unit TYPE loses mobility to a NON-lethal hit, once its health is below
    /// <see cref="BelowFraction"/> × max (1 = any hit, the UrbanCombat APC's "mobility kill"). 🔒 User, <c>2026-10-06</c>:
    /// approved the lean "death always stops; a non-lethal hit stops a unit only when its TKB platform says so".
    /// </summary>
    /// <remarks>
    /// Stamped by <c>CombatTkbTranslator</c> from <c>CombatPlatformDefDto.MobilityKillBelowFraction</c> on the types that opt in —
    /// ⭐ an ABSENT component means "only death stops it" (infantry, tanks, everything by default). Read by the one rule,
    /// <see cref="Fdp.Toolkit.Combat.CombatLife.ApplyHitCapabilities"/>. 📄 <c>docs/designs/packs-1/DESIGN.md</c> §2.B.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MobilityKill)]
    public struct MobilityKill
    {
        /// <summary>Health fraction (0..1] below which a non-lethal hit strips <c>CanMove</c>. 1 = any hit.</summary>
        public float BelowFraction;
    }
}
