using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-2269</c> TS-3 (R-2034) — a unit TYPE's temperament: the defaults its men are born with, each field optional. Stamped at
    /// creation by <c>BehaviorTkbTranslator</c> into <c>CombatTemperament</c>'s TYPE fields, ± <see cref="Spread"/> per man from his own
    /// seeded draw, so no two insurgents are clones; the scenario's per-man values win. ⭐ A type with none stamps nothing ⇒ its men read
    /// <c>TemperamentOf</c>'s defaults, exactly as before. 📄 <c>docs/DESIGN_Temperament_And_Stress.md</c> §4 TS-1…TS-3.
    /// </summary>
    [TkbDescriptor("AI.Temperament")]
    public record TemperamentDto
    {
        /// <summary>Bravery 0–1; <c>null</c> = not the type's to say.</summary>
        public float? RiskAcceptance { get; init; }
        /// <summary>Obedience 0–1; <c>null</c> = not the type's to say.</summary>
        public float? Obedience { get; init; }
        /// <summary>Nerve 0–1; <c>null</c> = not the type's to say.</summary>
        public float? Nerve { get; init; }
        /// <summary>Resolve 0–1; <c>null</c> = not the type's to say.</summary>
        public float? Resolve { get; init; }
        /// <summary>± per man, on the fields the type authors.</summary>
        public float Spread { get; init; } = Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultSpread;

        /// <summary>TS-2's obedience by kind of force: a regular soldier 0.8 (the default — he needs no dto), police 0.8, militia 0.5,
        /// insurgents 0.3.</summary>
        public static TemperamentDto RegularSoldier() => new() { Obedience = 0.8f };
        /// <inheritdoc cref="RegularSoldier"/>
        public static TemperamentDto Police() => new() { Obedience = 0.8f };
        /// <inheritdoc cref="RegularSoldier"/>
        public static TemperamentDto Militia() => new() { Obedience = 0.5f };
        /// <inheritdoc cref="RegularSoldier"/>
        public static TemperamentDto Insurgent() => new() { Obedience = 0.3f };
    }
}
