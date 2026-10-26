using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    public class TypeNameJsonConverter : JsonConverter<Type>
    {
        public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? name = reader.GetString();
            if (string.IsNullOrEmpty(name))
                return null;

            // ⭐ The short name or the full one (measured `2026-10-23`: the route doc's own example used the full name and matched nothing).
            return ComponentTypeRegistry.GetAllRegistered().FirstOrDefault(t => t.Name == name || t.FullName == name)
                ?? EventType.GetAllRegistered().FirstOrDefault(t => t.Name == name || t.FullName == name);
        }

        public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value?.Name);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Base predicate
    // ──────────────────────────────────────────────────────────────────────────

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(CompoundPredicateDto),           "Compound")]
    [JsonDerivedType(typeof(PropertyMatchDto),               "PropertyMatch")]
    [JsonDerivedType(typeof(NumericPredicateDto),            "Numeric")]
    [JsonDerivedType(typeof(StringPredicateDto),             "String")]
    [JsonDerivedType(typeof(TransientEventPredicateDto),     "TransientEvent")]
    [JsonDerivedType(typeof(LifecyclePredicateDto),          "Lifecycle")]
    [JsonDerivedType(typeof(SpatialBoundingPredicateDto),    "SpatialBounding")]
    [JsonDerivedType(typeof(StructuralPredicateDto),         "Structural")]
    [JsonDerivedType(typeof(BehaviorParamPredicateDto),      "BehaviorParam")]
    [JsonDerivedType(typeof(TraceBufferScanPredicateDto),    "TraceBufferScan")]
    [JsonDerivedType(typeof(BlueprintVariablePredicateDto),  "BlueprintVariable")]
    [JsonDerivedType(typeof(ExternalHitTagPredicateDto),     "ExternalHitTag")]
    [JsonDerivedType(typeof(BehaviourStepPredicateDto),      "BehaviourStep")]
    [JsonDerivedType(typeof(RingEntryPredicateDto),          "RingEntry")]
    public abstract class SearchPredicateDto { }

    // ──────────────────────────────────────────────────────────────────────────
    // Compound
    // ──────────────────────────────────────────────────────────────────────────

    public enum LogicalOperator { And, Or }

    public sealed class CompoundPredicateDto : SearchPredicateDto
    {
        public LogicalOperator Operator { get; set; } = LogicalOperator.And;
        public List<SearchPredicateDto> Conditions { get; set; } = new();
        /// <summary>
        /// Zero-based indices of children that the editor should render as read-only.
        /// Auto-synthesised breakpoints mark the structural trace-buffer branch
        /// [EditReadOnly] so the operator cannot drift it away from the visual node.
        /// </summary>
        public List<int> ReadOnlyChildIndices { get; set; } = new();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Property match
    // ──────────────────────────────────────────────────────────────────────────

    public enum SearchOperator { Equals, Contains, GreaterThan, LessThan, Changed, StartsWith }

    public sealed class PropertyMatchDto : SearchPredicateDto
    {
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type ComponentType { get; set; } = null!;
        /// <summary>Dot-separated field path, e.g. "Position.X".</summary>
        [PropertyPathPicker]
        public string PropertyPath { get; set; } = string.Empty;
        public SearchOperator Operator { get; set; } = SearchOperator.Equals;
        /// <summary>Value sub-predicate (NumericPredicateDto, StringPredicateDto, etc.).</summary>
        public SearchPredicateDto Predicate { get; set; } = null!;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Value predicates
    // ──────────────────────────────────────────────────────────────────────────

    public abstract class SearchPredicateValueDto : SearchPredicateDto { }

    public sealed class NumericPredicateDto : SearchPredicateValueDto
    {
        public double MinValue { get; set; } = double.MinValue;
        public double MaxValue { get; set; } = double.MaxValue;
    }

    public sealed class StringPredicateDto : SearchPredicateValueDto
    {
        public string Substring { get; set; } = "";
        public bool StartsWith { get; set; }
        public bool ExactMatch { get; set; }
    }

    /// <summary>
    /// Value predicate for enum fields.
    /// Not registered in the JsonPolymorphic chain since the generic type parameter
    /// prevents static registration. Use a concrete subclass or StringPredicateDto for
    /// round-trip serialization in scenarios that require it.
    /// </summary>
    public sealed class EnumPredicateDto<TEnum> : SearchPredicateValueDto
        where TEnum : struct, Enum
    {
        public List<TEnum> AllowedValues { get; set; } = new();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Transient event
    // ──────────────────────────────────────────────────────────────────────────

    public sealed class TransientEventPredicateDto : SearchPredicateDto
    {
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type EventType { get; set; } = null!;
        public bool AnyOccurrence { get; set; } = true;
        [PropertyPathPicker]
        public string PropertyPath { get; set; } = string.Empty;
        public SearchOperator Operator { get; set; } = SearchOperator.Equals;
        public string TargetValue { get; set; } = string.Empty;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    public enum EntityIdentifierType { EcsHandle, NetworkId, NameSubstring }

    public sealed class LifecyclePredicateDto : SearchPredicateDto
    {
        public EntityIdentifierType IdentifierType { get; set; } = EntityIdentifierType.NameSubstring;
        public string TargetValue { get; set; } = string.Empty;

        /// <summary>
        /// Optional component type that carries the entity's name field.
        /// If null, EcsHandle mode is used as a fallback for NameSubstring.
        /// </summary>
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type? NameComponentType { get; set; }

        /// <summary>
        /// Field path within NameComponentType that holds the name string.
        /// Defaults to "Name".
        /// </summary>
        [PropertyPathPicker]
        public string NamePropertyPath { get; set; } = "Name";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Spatial bounding
    // ──────────────────────────────────────────────────────────────────────────

    public enum BoundaryEvent { Entry, Exit, EntryOrExit }

    public sealed class SpatialBoundingPredicateDto : SearchPredicateDto
    {
        [MapPickableBoundingBox]
        public BoundingBox2D Bounds { get; set; }

        public BoundaryEvent TriggerEvent { get; set; } = BoundaryEvent.EntryOrExit;

        /// <summary>
        /// Component type that carries the entity's 2D world position.
        /// Required for spatial search to work.
        /// </summary>
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type PositionComponentType { get; set; } = null!;

        /// <summary>Field path for the X coordinate within PositionComponentType.</summary>
        [PropertyPathPicker]
        public string PositionXPath { get; set; } = "X";

        /// <summary>Field path for the Y coordinate within PositionComponentType.</summary>
        [PropertyPathPicker]
        public string PositionYPath { get; set; } = "Y";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Structural modification
    // ──────────────────────────────────────────────────────────────────────────

    public enum StructuralModification { Added, Removed, AnyChange }

    /// <summary>
    /// Distinguishes locally-owned components from ghost replicas in a distributed ECS.
    /// In a multi-host deployment an entity can carry the same component bit in its
    /// ComponentMask on every host but only one host holds AuthorityMask for it; the
    /// others are read-only ghosts. Diagnostic searches must be able to scope to one
    /// or the other to avoid investigating phantom state changes on replicas.
    /// </summary>
    public enum AuthorityRequirement { AnyAuthority, RequireAuthority, RequireGhost }

    public sealed class StructuralPredicateDto : SearchPredicateDto
    {
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type ComponentType { get; set; } = null!;
        public StructuralModification ModificationType { get; set; } = StructuralModification.Added;
        public AuthorityRequirement AuthorityRequirement { get; set; } = AuthorityRequirement.AnyAuthority;
    }

    public sealed class BehaviorParamPredicateDto : SearchPredicateDto
    {
        /// <summary>
        /// ⭐⭐⭐ <b>WHICH occurrence slot this predicate reads.</b> <c>0</c> means the behaviour's
        /// ROOT PARAMS slot; any other value is the <c>SlotKey</c> of one of the behaviour's
        /// <c>StatefulWorkingSlots</c>.
        ///
        /// <para>🔴 <b><c>CE-308</c> — this REPLACES the <c>BlackboardTarget</c> enum</b>, which named
        /// two COMPONENTS: <c>BrainBlackboard</c> and <c>Blackboard1024</c>. Both are retired, and the
        /// <c>Blackboard1024</c> arm never worked — it resolved through <c>HeavyDtoType</c>, which is
        /// <c>null</c> at every production site, so the compiler short-circuited to
        /// <c>(_, _) =&gt; false</c> before it could run.</para>
        ///
        /// <para>⭐⭐ <b>Why a slot KEY and not a renamed enum.</b> Occurrence storage keys everything —
        /// root params and every working state — in ONE key space. ⇒ the question *"which region does
        /// this predicate read?"* has exactly one honest answer shape, and it is a key. ⛔ A two-member
        /// enum could not name WHICH working state on a behaviour that has several, which is precisely
        /// the case worth searching.</para>
        ///
        /// <para>⚠ <c>0</c> is safe as the root sentinel: a real slot key is an FNV hash and the
        /// allocator never issues <c>0</c>, and the root key is computed from the behaviour hash
        /// rather than stored.</para>
        /// </summary>
        [WorkingSlotPicker]
        public int WorkingSlotKey { get; set; }

        [BehaviorHashPicker]
        public int BehaviorId { get; set; }

        [PropertyPathPicker]
        public string PropertyPath { get; set; } = string.Empty;

        public SearchOperator Operator { get; set; } = SearchOperator.Equals;

        public SearchPredicateDto Predicate { get; set; } = null!;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Trace buffer scan
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Predicate that scans a BTree or HSM trace ring buffer for any record that
    /// matches the specified opcode and optional field constraints.
    /// Zero allocation on the hot evaluation path -- uses pointer arithmetic over
    /// the component's raw 16-byte-stride buffer.
    /// </summary>
    public sealed class TraceBufferScanPredicateDto : SearchPredicateDto
    {
        /// <summary>
        /// Component type to scan.
        /// Must be <c>BTreeTraceWorkingMemory1024</c> or <c>HsmTraceWorkingMemory1024</c>.
        /// </summary>
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type ComponentType { get; set; } = null!;

        /// <summary>
        /// Opcode byte to match (cast from <c>BTreeTraceOpCode</c> or <c>TraceOpCode</c>).
        /// Always checked -- there is no "match-any-opcode" mode.
        /// </summary>
        public byte OpCode { get; set; }

        /// <summary>
        /// Value to match at byte offset 8-9 of each record.
        /// BTree: NodeIndex (for NodeEvaluated / Wait*) or StackDepth (for Scope*).
        /// HSM: StateIndex (StateEnter/Exit), EventId (EventHandled), FromState (Transition), etc.
        /// Only checked when <see cref="MatchIndexField"/> is true.
        /// </summary>
        public ushort IndexField { get; set; }

        /// <summary>Whether to check <see cref="IndexField"/>.</summary>
        public bool MatchIndexField { get; set; }

        /// <summary>
        /// Value to match at byte offset 10 of each record (the <em>status/result</em> byte).
        /// BTree: <c>NodeStatus</c> byte for NodeEvaluated.
        /// HSM: <c>GuardResult</c> byte (0=false, 1=true) for GuardEvaluated.
        /// Only checked when <see cref="MatchStatusField"/> is true.
        /// </summary>
        public byte StatusField { get; set; }

        /// <summary>Whether to check <see cref="StatusField"/>.</summary>
        public bool MatchStatusField { get; set; }

        /// <summary>
        /// Value to match at byte offset 12-13 of each record.
        /// HSM: <c>TriggerEventId</c> for Transition records.
        /// BTree: low 16-bits of <c>Duration</c> for Wait* records (rarely useful).
        /// Only checked when <see cref="MatchTriggerEventId"/> is true.
        /// </summary>
        public ushort TriggerEventId { get; set; }

        /// <summary>Whether to check <see cref="TriggerEventId"/>.</summary>
        public bool MatchTriggerEventId { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────────────    // Blueprint variable breakpoints
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Predicate that navigates the multi-tier BlueprintBlackboard partition
    /// allocator, finds the slot for <see cref="TargetBlueprintAssetId"/>,
    /// reads <see cref="VariableName"/> at the baked field offset, and evaluates
    /// <see cref="Predicate"/> against the value.
    /// The delegate re-runs the slot scan on every evaluation, so tier upgrades
    /// never invalidate a compiled delegate (see DESIGN §6.5).
    /// </summary>
    public sealed class BlueprintVariablePredicateDto : SearchPredicateDto
    {
        /// <summary>
        /// Asset GUID of the target Blueprint.
        /// Converted to a 32-bit int at compile time via
        /// <c>BlueprintIdHash.Compute(TargetBlueprintAssetId)</c>.
        /// </summary>
        public Guid TargetBlueprintAssetId { get; set; }

        /// <summary>
        /// Variable name as declared in <c>BlueprintDefinition.StateFields</c>.
        /// Resolved to a byte offset at compile time.
        /// </summary>
        public string VariableName { get; set; } = string.Empty;

        public SearchOperator Operator { get; set; } = SearchOperator.Equals;

        /// <summary>
        /// Value sub-predicate: <see cref="NumericPredicateDto"/> or
        /// <see cref="StringPredicateDto"/> (same as <see cref="PropertyMatchDto.Predicate"/>).
        /// </summary>
        public SearchPredicateDto Predicate { get; set; } = null!;
    }

    // ──────────────────────────────────────────────────────────────────────────    // Result types
    // ──────────────────────────────────────────────────────────────────────────

    /// <param name="Payload">⭐ R-4077 extension (§9c ③) — what a <see cref="ResultProjectionDto"/> kept; null without one.</param>
    public sealed record SearchResultDto(
        int FrameIndex,
        long WallClockTicks,
        Entity Entity,
        string ContextMessage,
        SearchResultPayload? Payload = null);

    // ──────────────────────────────────────────────────────────────────────────
    // Ring entries — R-4077 extension (docs/DESIGN_Director_Timeline_Editor.md §9c ③, TE-22 ② / R-4076)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ One result per NEW entry of a recorded ring or history (a fixed <c>[InlineArray]</c> list on a component): an entry is new when its
    /// key was not among the entity's entries on the previous frame — so two entries in one frame are two results, a ring's eviction never
    /// loses one, and an entry whose counted field ticks (an order repeated) is new again. ⛔ The first frame an entity is seen is its
    /// BASELINE: its ring holds the past, and nothing is reported for it.
    /// </summary>
    public sealed class RingEntryPredicateDto : SearchPredicateDto
    {
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type ComponentType { get; set; } = null!;
        /// <summary>The component's <c>[InlineArray]</c> field holding the entries.</summary>
        public string ItemsPath { get; set; } = "Items";
        /// <summary>An int field: the valid entries are <c>[0, Count)</c>. Empty = every slot.</summary>
        public string? CountPath { get; set; }
        /// <summary>The entry fields that identify it (its own counter, or time + counterpart).</summary>
        public List<string> KeyPaths { get; set; } = new();
        /// <summary>Optional: only entries whose field compares true (e.g. a switch count above 0).</summary>
        public string? FilterPath { get; set; }
        public SearchOperator FilterOperator { get; set; } = SearchOperator.Equals;
        public string FilterValue { get; set; } = string.Empty;
    }

    public sealed record LifecycleSearchResultDto(
        Entity Entity,
        int StartFrame,
        int EndFrame,
        string MatchContext);

    // ──────────────────────────────────────────────────────────────────────────
    // Spatial helper
    // ──────────────────────────────────────────────────────────────────────────

    public struct BoundingBox2D
    {
        public Vector2 Min { get; set; }
        public Vector2 Max { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Custom attributes
    // ──────────────────────────────────────────────────────────────────────────

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class MapPickableBoundingBoxAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class BehaviorHashPickerAttribute : Attribute { }

    /// <summary>
    /// Marks an <c>int</c> field as an occurrence-SLOT selector: the drawer offers the sibling
    /// behaviour's root params plus each of its stateful working-state slots, by label and scope.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class WorkingSlotPickerAttribute : Attribute { }

    // ──────────────────────────────────────────────────────────────────────────
    // External-hit tag predicate
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Synthetic predicate used as a "fire from external probe" marker.
    /// The component-predicate compiler always returns <c>static (_, _) =&gt; false</c> for
    /// this type; it is never evaluated through <see cref="DataBreakpointSystem"/>.
    /// Instead, <see cref="IDataBreakpointManager.OnExternalHit"/> scans breakpoints
    /// whose <see cref="SearchPredicateDto"/> tree contains this DTO and fires them
    /// when the tag matches.
    /// </summary>
    public sealed class ExternalHitTagPredicateDto : SearchPredicateDto
    {
        /// <summary>
        /// Opaque string tag that must match the first argument of
        /// <see cref="IDataBreakpointManager.OnExternalHit"/>.
        /// Convention: Blueprint node probes use the raw <c>nodeId</c> string;
        /// future Slice 1 surfaces may use other prefixes.
        /// </summary>
        public string Tag { get; set; } = string.Empty;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Behaviour step (CE-1323)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ <c>CE-1323</c> (Hrot DESIGN_Behaviour_Debugging §9, DBG-10) — what a behaviour breakpoint waits for.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BehaviourStepKind
    {
        /// <summary>A BTree node runs (<see cref="BehaviourStepPredicateDto.Node"/>).</summary>
        NodeRuns,
        /// <summary>An HSM state is entered (<see cref="BehaviourStepPredicateDto.State"/>).</summary>
        StateEnter,
        /// <summary>An HSM state is left.</summary>
        StateExit,
        /// <summary>Any state INSIDE a composite state is entered — or inside one region (<see cref="BehaviourStepPredicateDto.Region"/>)
        /// of a parallel state.</summary>
        InsideEnter,
        /// <summary>A transition fires: <see cref="BehaviourStepPredicateDto.From"/> → <see cref="BehaviourStepPredicateDto.To"/>, narrowed
        /// by <see cref="BehaviourStepPredicateDto.Event"/>; no From = a global transition (from any state).</summary>
        TransitionFires,
        /// <summary>The machine takes an event off its queue (consumed or deferred), in whatever state.</summary>
        EventHandled,
    }

    /// <summary>
    /// ⭐ <c>CE-1323</c> (DBG-9) — a BEHAVIOUR BREAKPOINT: a step of a running BTree / HSM, named by NAMES (a state, a transition's two ends,
    /// an event) or by the element's id, so it works on a behaviour built in C# that has no document. ⛔ Not evaluated by the component
    /// path: the editor's evaluator reads the units' trace records written since its last look and reports a hit
    /// (<c>IDataBreakpointManager.OnBehaviourStepHit</c>) — which is why a continue does not stop on the same arrival again (the whole-ring
    /// <see cref="TraceBufferScanPredicateDto"/> did). Inside an AND <see cref="CompoundPredicateDto"/> the other conditions are checked on
    /// the unit at the hit; the breakpoint's unit filter scopes it to one unit.
    /// </summary>
    public sealed class BehaviourStepPredicateDto : SearchPredicateDto
    {
        /// <summary>The behaviour's registered name (the unit's running BTree / HSM).</summary>
        public string Behaviour { get; set; } = string.Empty;

        public BehaviourStepKind Kind { get; set; }

        /// <summary>The state's name (StateEnter / StateExit / InsideEnter).</summary>
        public string? State { get; set; }

        /// <summary>InsideEnter on a parallel state: which region (0-based); -1 = anything under the state.</summary>
        public int Region { get; set; } = -1;

        /// <summary>TransitionFires: the source state's name; empty = a global transition.</summary>
        public string? From { get; set; }

        /// <summary>TransitionFires: the target state's name.</summary>
        public string? To { get; set; }

        /// <summary>EventHandled: the event's name; TransitionFires: narrows to the transition on this event.</summary>
        public string? Event { get; set; }

        /// <summary>NodeRuns: the BTree node's id (its VisualId).</summary>
        public Guid Node { get; set; }

        /// <summary>The element's id when known (a state's StableId, a transition's VisualId) — preferred over the names.</summary>
        public Guid Element { get; set; }

        /// <summary>For a person: what the element is called ("Withdrawing", "Engaging → Withdrawing").</summary>
        public string Label { get; set; } = string.Empty;
    }
}
