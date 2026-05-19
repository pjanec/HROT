using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Fbt.Runtime;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// Delegate that parses a JSON parameter string and writes the result directly into
    /// a behaviour blackboard's inline memory — zero allocation, no boxing.
    /// </summary>
    /// <param name="json">Serialised parameter payload (cold path only).</param>
    /// <param name="memory">Pointer to the first byte of <see cref="BrainBlackboard.BehaviorParameters"/>.</param>
    public unsafe delegate void ParseParamsDelegate(string json, byte* memory);

    /// <summary>
    /// Immutable definition of a single registered behavior (i.e., a named AI behaviour).
    /// Created once at startup; read-only thereafter.
    /// </summary>
    public sealed class BehaviorDefinition
    {
        /// <summary>Human-readable name, e.g. "FleeToSafety" or "Patrol".</summary>
        public required string Name { get; init; }

        /// <summary>
        /// Brain tier for entities assigned this behavior.
        /// Use <see cref="BehaviorConstants.BrainTierBTree"/> or
        /// <see cref="BehaviorConstants.BrainTierHsm"/>.
        /// </summary>
        public byte BrainTier { get; init; }

        /// <summary>
        /// Pre-built FastBTree interpreter for this behavior.
        /// <c>null</c> when <see cref="BrainTier"/> is not <see cref="BehaviorConstants.BrainTierBTree"/>.
        /// </summary>
        public Interpreter<BrainBlackboard, BTreeContext>? BTreeInterpreter { get; init; }

        /// <summary>
        /// FastHSM definition blob for this behavior.
        /// <c>null</c> when <see cref="BrainTier"/> is not <see cref="BehaviorConstants.BrainTierHsm"/>.
        /// </summary>
        public HsmDefinitionBlob? HsmDefinition { get; init; }

        /// <summary>
        /// Optional FastHSM symbolication metadata. Populated by <c>AiBehaviorFactory</c>
        /// for HSM-backed behaviors so diagnostic renderers / JSON translators can
        /// resolve raw state, event, and action IDs to human-readable names. May be
        /// <c>null</c> for legacy behaviors compiled without sidecar metadata.
        /// </summary>
        public MachineMetadata? HsmMetadata { get; init; }

        /// <summary>
        /// Cold-path delegate that parses the behavior's JSON parameter payload into
        /// <see cref="BrainBlackboard.BehaviorParameters"/>.  May be <c>null</c> if the behavior
        /// carries no configurable parameters.
        /// </summary>
        public ParseParamsDelegate? ParseParams { get; init; }

        /// <summary>
        /// Optional type of the params DTO struct stored at the start of
        /// <see cref="BrainBlackboard.BehaviorParameters"/> for this behavior.
        /// When non-null, enables typed rendering in <c>BrainBlackboardRenderer</c>.
        /// The type must be unmanaged (enforced by convention, not the compiler).
        /// </summary>
        public Type? ParamsDtoType { get; init; }

        /// <summary>
        /// Optional DTO type stored in a generic heavy blackboard component (e.g., <c>Blackboard1024</c>)
        /// for this behavior.  When non-null, enables typed rendering in <c>Blackboard1024Renderer</c>
        /// for unmanaged DTOs projected via <c>Unsafe.As</c> over the component's raw byte array.
        /// For managed components assigned via <c>[SharedAiHeavyAction]</c>, leave this null
        /// (the managed class reference is fetched directly and does not need Inspector projection).
        /// </summary>
        public Type? HeavyDtoType { get; init; }
    }

    /// <summary>
    /// Startup-time registry that maps stable assigned <c>int</c> IDs to their
    /// <see cref="BehaviorDefinition"/>s.
    ///
    /// Thread-safe for reads after the world is started (registrations happen before
    /// the first frame).  All mutations must complete before the simulation loop begins.
    ///
    /// <b>DEBT-006 fix:</b> Key is now a stable assigned <c>int</c> constant (see
    /// <see cref="BehaviorIds"/>), NOT <c>name.GetHashCode()</c>.  Using
    /// <c>string.GetHashCode()</c> is forbidden: it is process-randomised in .NET,
    /// making behavior IDs non-reproducible across runs and processes.
    /// </summary>
    public sealed class BehaviorRegistry
    {
        private readonly Dictionary<int, BehaviorDefinition> _definitions = new();
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.Ordinal);

        /// <summary>
        /// Register a behavior with a stable assigned <paramref name="id"/>.
        /// The <paramref name="id"/> must be a unique compile-time constant
        /// (see <see cref="BehaviorIds"/>).  It becomes the value stored in
        /// <see cref="Components.BehaviorState.ActiveBehaviorHash"/>.
        /// </summary>
        public void Register(int id, string name, BehaviorDefinition definition)
        {
            // Startup-time firewall: ensure the params DTO won't overrun the 60-byte
            // BehaviorParameters region and corrupt the SoftAdvice or Interrupt registers.
            // Source generators enforce this at compile time via BHU_004; this check is
            // the runtime backstop for behaviors whose DTO is bound without [SharedAiAction].
            if (definition.ParamsDtoType != null)
            {
                int dtoSize = System.Runtime.InteropServices.Marshal.SizeOf(definition.ParamsDtoType);
                if (dtoSize > BehaviorConstants.MaxBehaviorParamByteSize)
                    throw new InvalidOperationException(
                        $"Behavior '{name}' params DTO '{definition.ParamsDtoType.Name}' requires {dtoSize} bytes, " +
                        $"which exceeds the maximum allowed parameter size of {BehaviorConstants.MaxBehaviorParamByteSize} bytes. " +
                        "This would corrupt the SoftAdvice and Interrupt registers in BrainBlackboard.");
            }

            _definitions[id] = definition;
            _nameToId[name] = id;
        }

        /// <summary>
        /// Resolve a behavior name to its stable integer ID.
        /// Returns <c>false</c> when the name has not been registered.
        /// Used by <see cref="Systems.BehaviorIngressSystem"/> to map event names
        /// to IDs without calling <c>string.GetHashCode()</c>.
        /// </summary>
        public bool TryGetId(string name, out int id)
            => _nameToId.TryGetValue(name, out id);

        /// <summary>
        /// Look up a definition by its stable integer ID.  Returns <c>false</c>
        /// when the behavior has not been registered (entity is silently skipped
        /// by brain tick systems).
        /// </summary>
        public bool TryGetDefinition(
            int behaviorId,
            [MaybeNullWhen(false)] out BehaviorDefinition definition)
            => _definitions.TryGetValue(behaviorId, out definition);

        /// <summary>
        /// Returns a snapshot of all behavior names currently registered.
        /// The returned list is a copy of the internal key set and cannot mutate the registry.
        /// </summary>
        public IReadOnlyList<string> GetRegisteredNames()
            => _nameToId.Keys.ToList();

        /// <summary>
        /// Reverse-maps a stable integer behavior ID back to its registered name.
        /// Returns <c>false</c> when the ID has not been registered.
        /// Used by egress translators to emit the human-readable <c>BehaviorId</c>
        /// string rather than the raw numeric behavior ID.
        /// </summary>
        public bool TryGetName(int behaviorId, [MaybeNullWhen(false)] out string name)
        {
            if (_definitions.TryGetValue(behaviorId, out var def))
            {
                name = def.Name;
                return true;
            }
            name = null;
            return false;
        }
    }
}
