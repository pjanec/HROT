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

        /// <summary>Whether the entity can move under its own power.</summary>
        public bool CanMove { get; init; }

        /// <summary>Whether the entity can fire weapons.</summary>
        public bool CanShoot { get; init; }

        /// <summary>Whether the entity can interact with other entities (e.g. embark/disembark).</summary>
        public bool CanInteract { get; init; }
    }
}
