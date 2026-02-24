using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Fbt.Runtime;
using Fhsm.Kernel.Data;
using FDP.Toolkit.Behavior.Components;

namespace FDP.Toolkit.Behavior
{
    /// <summary>
    /// Delegate that parses a JSON parameter string and writes the result directly into
    /// a behaviour blackboard's inline memory — zero allocation, no boxing.
    /// </summary>
    /// <param name="json">Serialised parameter payload (cold path only).</param>
    /// <param name="memory">Pointer to the first byte of <see cref="BrainBlackboard.Memory"/>.</param>
    public unsafe delegate void ParseParamsDelegate(string json, byte* memory);

    /// <summary>
    /// Immutable definition of a single registered doctrine (i.e., a named AI behaviour).
    /// Created once at startup; read-only thereafter.
    /// </summary>
    public sealed class DoctrineDefinition
    {
        /// <summary>Human-readable name, e.g. "FleeToSafety" or "Patrol".</summary>
        public required string Name { get; init; }

        /// <summary>
        /// Brain tier for entities assigned this doctrine.
        /// Use <see cref="BehaviorConstants.BrainTierBTree"/> or
        /// <see cref="BehaviorConstants.BrainTierHsm"/>.
        /// </summary>
        public byte BrainTier { get; init; }

        /// <summary>
        /// Pre-built FastBTree interpreter for this doctrine.
        /// <c>null</c> when <see cref="BrainTier"/> is not <see cref="BehaviorConstants.BrainTierBTree"/>.
        /// </summary>
        public Interpreter<BrainBlackboard, BTreeContext>? BTreeInterpreter { get; init; }

        /// <summary>
        /// FastHSM definition blob for this doctrine.
        /// <c>null</c> when <see cref="BrainTier"/> is not <see cref="BehaviorConstants.BrainTierHsm"/>.
        /// </summary>
        public HsmDefinitionBlob? HsmDefinition { get; init; }

        /// <summary>
        /// Cold-path delegate that parses the doctrine's JSON parameter payload into
        /// <see cref="BrainBlackboard.Memory"/>.  May be <c>null</c> if the doctrine
        /// carries no configurable parameters.
        /// </summary>
        public ParseParamsDelegate? ParseParams { get; init; }
    }

    /// <summary>
    /// Startup-time registry that maps doctrine name hashes to their
    /// <see cref="DoctrineDefinition"/>s.
    ///
    /// Thread-safe for reads after the world is started (registrations happen before
    /// the first frame).  All mutations must complete before the simulation loop begins.
    /// </summary>
    public sealed class DoctrineRegistry
    {
        private readonly Dictionary<int, DoctrineDefinition> _definitions = new();

        /// <summary>
        /// Register a doctrine.  Key is <c>name.GetHashCode()</c>, which must match
        /// <see cref="Components.DoctrineState.ActiveDoctrineHash"/> written by
        /// <see cref="Systems.DoctrineIngressSystem"/>.
        /// </summary>
        public void Register(string name, DoctrineDefinition definition)
        {
            _definitions[name.GetHashCode()] = definition;
        }

        /// <summary>
        /// Look up a definition by its hash.  Returns <c>false</c> when the doctrine
        /// has not been registered (entity is silently skipped by brain tick systems).
        /// </summary>
        public bool TryGetDefinition(
            int doctrineHash,
            [MaybeNullWhen(false)] out DoctrineDefinition definition)
            => _definitions.TryGetValue(doctrineHash, out definition);
    }
}
