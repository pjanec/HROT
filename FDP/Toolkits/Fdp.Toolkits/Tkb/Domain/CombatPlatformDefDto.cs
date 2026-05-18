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
