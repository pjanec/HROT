using System;
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
    /// Startup-time registry that maps stable assigned <c>int</c> IDs to their
    /// <see cref="DoctrineDefinition"/>s.
    ///
    /// Thread-safe for reads after the world is started (registrations happen before
    /// the first frame).  All mutations must complete before the simulation loop begins.
    ///
    /// <b>DEBT-006 fix:</b> Key is now a stable assigned <c>int</c> constant (see
    /// <see cref="DoctrineIds"/>), NOT <c>name.GetHashCode()</c>.  Using
    /// <c>string.GetHashCode()</c> is forbidden: it is process-randomised in .NET,
    /// making doctrine IDs non-reproducible across runs and processes.
    /// </summary>
    public sealed class DoctrineRegistry
    {
        private readonly Dictionary<int, DoctrineDefinition> _definitions = new();
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.Ordinal);

        /// <summary>
        /// Register a doctrine with a stable assigned <paramref name="id"/>.
        /// The <paramref name="id"/> must be a unique compile-time constant
        /// (see <see cref="DoctrineIds"/>).  It becomes the value stored in
        /// <see cref="Components.DoctrineState.ActiveDoctrineHash"/>.
        /// </summary>
        public void Register(int id, string name, DoctrineDefinition definition)
        {
            _definitions[id] = definition;
            _nameToId[name] = id;
        }

        /// <summary>
        /// Resolve a doctrine name to its stable integer ID.
        /// Returns <c>false</c> when the name has not been registered.
        /// Used by <see cref="Systems.DoctrineIngressSystem"/> to map event names
        /// to IDs without calling <c>string.GetHashCode()</c>.
        /// </summary>
        public bool TryGetId(string name, out int id)
            => _nameToId.TryGetValue(name, out id);

        /// <summary>
        /// Look up a definition by its stable integer ID.  Returns <c>false</c>
        /// when the doctrine has not been registered (entity is silently skipped
        /// by brain tick systems).
        /// </summary>
        public bool TryGetDefinition(
            int doctrineId,
            [MaybeNullWhen(false)] out DoctrineDefinition definition)
            => _definitions.TryGetValue(doctrineId, out definition);

        /// <summary>
        /// Reverse-maps a stable integer doctrine ID back to its registered name.
        /// Returns <c>false</c> when the ID has not been registered.
        /// Used by egress translators to emit the human-readable <c>BehaviorId</c>
        /// string rather than the raw numeric doctrine ID.
        /// </summary>
        public bool TryGetName(int doctrineId, [MaybeNullWhen(false)] out string name)
        {
            if (_definitions.TryGetValue(doctrineId, out var def))
            {
                name = def.Name;
                return true;
            }
            name = null;
            return false;
        }
    }
}
