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

            return ComponentTypeRegistry.GetAllRegistered().FirstOrDefault(t => t.Name == name)
                ?? EventType.GetAllRegistered().FirstOrDefault(t => t.Name == name);
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
        public BlackboardTarget TargetBlackboard { get; set; } = BlackboardTarget.BrainBlackboard;

        [BehaviorHashPicker]
        public int BehaviorId { get; set; }

        [PropertyPathPicker]
        public string PropertyPath { get; set; } = string.Empty;

        public SearchOperator Operator { get; set; } = SearchOperator.Equals;

        public SearchPredicateDto Predicate { get; set; } = null!;
    }

    public enum BlackboardTarget
    {
        BrainBlackboard,
        Blackboard1024
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

    public sealed record SearchResultDto(
        int FrameIndex,
        long WallClockTicks,
        Entity Entity,
        string ContextMessage);

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
}
