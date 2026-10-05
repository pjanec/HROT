using Fdp.Core;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// AI/behavior profile descriptor for a TKB entity.
    /// Drives projection of behavior and cognitive memory ECS components
    /// by <c>BehaviorTkbTranslator</c>.
    /// </summary>
    [TkbDescriptor("AI.BehaviorProfile")]
    public record BehaviorProfileDto
    {
        /// <summary>
        /// Simulation fidelity tier.
        /// 1 = Civilian (TrafficBrainSystem), 2 = Tactical (BTree/HSM).
        /// </summary>
        public byte SimTier { get; init; }

        /// <summary>
        /// Cognitive brain type to allocate.
        /// 0 = None (civilian/static), 1 = FastHSM, 2 = FastBTree.
        /// </summary>
        public byte BrainTier { get; init; }

        /// <summary>
        /// Integer hash of the behavior assigned at spawn
        /// (e.g. WanderMilitary = 3011). Zero = no initial behavior.
        /// </summary>
        public int DefaultBehaviorHash { get; init; }

        /// <summary>⭐ <c>CE-2074</c> (R-200) — the unit type's default ROE fire rule; <c>Unset</c> = fire at will.</summary>
        public Fdp.Toolkit.Behavior.Components.RoeFire DefaultRoeFire { get; init; }

        /// <summary>⭐ <c>CE-2074</c> (R-200) — the unit type's default reaction rule; <c>Unset</c> = reactions allowed.</summary>
        public Fdp.Toolkit.Behavior.Components.RoeReactions DefaultRoeReactions { get; init; }

        /// <summary>⭐ <c>CE-2095</c> — the unit type's ReturnFire window in seconds; <c>0</c> = the default (5 s).</summary>
        public float DefaultRoeReturnFireWindowSeconds { get; init; }

        /// <summary>⭐ <c>CE-2077</c> — the unit type's SOP (its own logic: idle choice + reactions, R-198) by behaviour NAME;
        /// <c>null</c> = no SOP (the unit does only what it is told). Started through the ingress at spawn, origin Sop.</summary>
        public string? DefaultSop { get; init; }

        /// <summary>⭐ <c>CE-2077</c> — the SOP's params as JSON (R-191); <c>null</c> = its authored defaults.</summary>
        public string? DefaultSopParamsJson { get; init; }

        /// <summary>Whether the entity can move under its own power.</summary>
        public bool CanMove { get; init; }

        /// <summary>Whether the entity can fire weapons.</summary>
        public bool CanShoot { get; init; }

        /// <summary>Whether the entity can interact with other entities (e.g. embark/disembark).</summary>
        public bool CanInteract { get; init; }

        /// <summary>
        /// Force affiliation stamped onto <see cref="EntityInfo.ForceId"/> by the translator.
        /// Defaults to <see cref="ForceId.Neutral"/> (zero).
        /// </summary>
        public ForceId Faction { get; init; } = ForceId.Neutral;
    }
}
