using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Combat platform (health and armour) descriptor for a TKB entity.
    /// Drives projection of <c>Health</c> and <c>PhysicsCollider</c>
    /// by <c>CombatTkbTranslator</c>.
    /// </summary>
    [TkbDescriptor("Combat.PlatformDef")]
    public record CombatPlatformDefDto
    {
        /// <summary>Maximum and initial hit-point pool.</summary>
        public float MaxHealth { get; init; }

        /// <summary>
        /// ⭐ <c>CE-3092</c> — below what fraction of <see cref="MaxHealth"/> a NON-lethal hit immobilises this type
        /// (<c>MobilityKill</c>). <b>0 (default) = only death stops it</b>; 1 = any hit (the UrbanCombat APC's mobility kill).
        /// 🔒 User, <c>2026-10-06</c>: "death always stops; a non-lethal hit stops a unit only when its TKB platform says so".
        /// </summary>
        public float MobilityKillBelowFraction { get; init; }

        /// <summary>Front armour thickness in mm RHA equivalent.</summary>
        [EditUnit("mm RHA")]
        public float ArmorFront { get; init; }

        /// <summary>Side armour thickness in mm RHA equivalent.</summary>
        [EditUnit("mm RHA")]
        public float ArmorSide { get; init; }

        /// <summary>Rear armour thickness in mm RHA equivalent.</summary>
        [EditUnit("mm RHA")]
        public float ArmorRear { get; init; }
    }
}
