using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
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
    public abstract class SearchPredicateDto { }

    // ──────────────────────────────────────────────────────────────────────────
    // Compound
    // ──────────────────────────────────────────────────────────────────────────

    public enum LogicalOperator { And, Or }

    public sealed class CompoundPredicateDto : SearchPredicateDto
    {
        public LogicalOperator Operator { get; set; } = LogicalOperator.And;
        public List<SearchPredicateDto> Conditions { get; set; } = new();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Property match
    // ──────────────────────────────────────────────────────────────────────────

    public enum SearchOperator { Equals, Contains, GreaterThan, LessThan, Changed, StartsWith }

    public sealed class PropertyMatchDto : SearchPredicateDto
    {
        [JsonIgnore]
        public Type ComponentType { get; set; } = null!;
        /// <summary>Dot-separated field path, e.g. "Position.X".</summary>
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
        [JsonIgnore]
        public Type EventType { get; set; } = null!;
        public bool AnyOccurrence { get; set; } = true;
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
        [JsonIgnore]
        public Type? NameComponentType { get; set; }

        /// <summary>
        /// Field path within NameComponentType that holds the name string.
        /// Defaults to "Name".
        /// </summary>
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
        [JsonIgnore]
        public Type PositionComponentType { get; set; } = null!;

        /// <summary>Field path for the X coordinate within PositionComponentType.</summary>
        public string PositionXPath { get; set; } = "X";

        /// <summary>Field path for the Y coordinate within PositionComponentType.</summary>
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
    public enum AuthorityRequirement { Any, RequireAuthority, RequireGhost }

    public sealed class StructuralPredicateDto : SearchPredicateDto
    {
        [JsonIgnore]
        public Type ComponentType { get; set; } = null!;
        public StructuralModification ModificationType { get; set; } = StructuralModification.Added;
        public AuthorityRequirement AuthorityRequirement { get; set; } = AuthorityRequirement.Any;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Result types
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
}
