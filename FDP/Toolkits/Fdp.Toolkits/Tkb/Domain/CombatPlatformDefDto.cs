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

        /// <summary>⭐ M1 (D-A) — roof armour (mm RHA), struck by a round descending ≥ 45°. <b>0 = <see cref="ArmorRear"/></b>
        /// (the thinnest declared face — never "no armour").</summary>
        [EditUnit("mm RHA")]
        public float ArmorTop { get; init; }

        /// <summary>⭐ M2 (D-B) — front armour against a SHAPED CHARGE (mm RHA). <b>0 = <see cref="ArmorFront"/></b>.</summary>
        [EditUnit("mm RHA")]
        public float ArmorFrontCe { get; init; }

        /// <summary>⭐ M2 (D-B) — side armour against a shaped charge. <b>0 = <see cref="ArmorSide"/></b>.</summary>
        [EditUnit("mm RHA")]
        public float ArmorSideCe { get; init; }

        /// <summary>⭐ M2 (D-B) — rear armour against a shaped charge. <b>0 = <see cref="ArmorRear"/></b>.</summary>
        [EditUnit("mm RHA")]
        public float ArmorRearCe { get; init; }

        /// <summary>⭐ M2 (D-B) — roof armour against a shaped charge. <b>0 = the roof's kinetic value</b>.</summary>
        [EditUnit("mm RHA")]
        public float ArmorTopCe { get; init; }
    }
}
