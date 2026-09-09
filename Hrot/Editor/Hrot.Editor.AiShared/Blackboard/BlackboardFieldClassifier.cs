using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fbt.Kernel;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Indicates how the editor treats a blackboard field.
/// </summary>
public enum FieldClassification
{
    /// <summary>
    /// The editor owns this field: it is shown in the Variables panel and can be renamed,
    /// retyped, or have its doc comment edited visually.
    /// </summary>
    EditorManaged,

    /// <summary>
    /// The field is preserved byte-for-byte on round-trip but is not editable in the
    /// Variables panel.
    /// </summary>
    ReadOnlyPassthrough,
}

/// <summary>
/// Result of classifying a single blackboard field.
/// </summary>
/// <param name="Classification">The assigned classification.</param>
/// <param name="ReadOnlyReason">
/// Null when <c>Classification == EditorManaged</c>; a human-readable description of the
/// violated rule when <c>Classification == ReadOnlyPassthrough</c>.
/// </param>
public record FieldClassificationResult(
    FieldClassification Classification,
    string? ReadOnlyReason
);

/// <summary>
/// Applies the six-condition rule (BB design §3.4) to decide whether a field is
/// editor-managed or must be treated as a read-only passthrough.
///
/// All six conditions must hold for a field to be <see cref="FieldClassification.EditorManaged"/>:
/// <list type="number">
///   <item>Declaration is single-line (<see cref="FieldParseResult.IsSingleLineDeclaration"/>).</item>
///   <item>Type is in the known-type set (primitives, vectors, enums, [BlackboardDtoStruct], schema DTO types).</item>
///   <item>Leading <c>///</c> comment is allowed and does NOT force ReadOnly.</item>
///   <item>No attributes in the span (<see cref="FieldParseResult.HasAttribute"/>).</item>
///   <item>No initializer (<see cref="FieldParseResult.HasInitializer"/>).</item>
///   <item>Single-line declaration (same as condition 1 -- one check covers both).</item>
/// </list>
/// </summary>
public static class BlackboardFieldClassifier
{
    private static readonly HashSet<Type> BuiltInKnownTypes = new()
    {
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(Vector2),
        typeof(Vector3),
        typeof(Vector4),
        typeof(Quaternion),
    };

    /// <summary>
    /// Classifies <paramref name="parseResult"/> using the six-condition rule.
    /// </summary>
    /// <param name="parseResult">
    /// Parse result from <see cref="BlackboardSourceTextParser"/> for this field.
    /// </param>
    /// <param name="fieldInfo">
    /// <see cref="FieldInfo"/> from reflection for the same field.
    /// </param>
    /// <param name="knownTypes">
    /// Additional types considered editor-managed: BTree/HSM action DTO types discovered by
    /// <see cref="IActionSchemaExporter"/>, plus any explicitly registered types.
    /// </param>
    public static FieldClassificationResult Classify(
        FieldParseResult parseResult,
        FieldInfo fieldInfo,
        IReadOnlySet<Type> knownTypes)
    {
        // Condition 1 + 6: single-line declaration.
        if (!parseResult.IsSingleLineDeclaration)
            return ReadOnly("multi-line declaration is not editor-managed");

        // Condition 4: no attributes.
        if (parseResult.HasAttribute)
            return ReadOnly("field has an attribute; fields with attributes are not editor-managed");

        // Condition 5: no initializer.
        if (parseResult.HasInitializer)
            return ReadOnly("field has an initializer; fields with initializers are not editor-managed");

        // Condition 2: type must be in the known set.
        var fieldType = fieldInfo.FieldType;
        if (!IsKnownType(fieldType, knownTypes))
            return ReadOnly($"unknown type '{fieldType.FullName ?? fieldType.Name}' is not editor-managed");

        // Condition 3: leading /// comment is acceptable -- no action needed.

        return new FieldClassificationResult(FieldClassification.EditorManaged, null);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static FieldClassificationResult ReadOnly(string reason) =>
        new(FieldClassification.ReadOnlyPassthrough, reason);

    private static bool IsKnownType(Type t, IReadOnlySet<Type> knownTypes)
    {
        if (BuiltInKnownTypes.Contains(t)) return true;
        if (t.IsEnum) return true;
        if (t.IsDefined(typeof(BlackboardDtoStructAttribute), inherit: false)) return true;
        if (knownTypes.Contains(t)) return true;
        // FC-3a (Q#21-A1): the fixed-list WRAPPER pattern is a first-class known shape, provided
        // its element type is itself known by this same rule (recursion terminates: an element
        // cannot be another wrapper -- TryGetFixedListShape rejects nesting).
        if (TryGetFixedListShape(t, out var elem, out _) && IsKnownType(elem, knownTypes)) return true;
        return false;
    }

    /// <summary>
    /// FC-3a (Q#21-A1) -- structural recognition of the canonical fixed-list WRAPPER pattern.
    /// Thin delegate to <see cref="Fdp.Core.FixedListShape"/>, THE single definition of the
    /// shape (also used by the FC-3b JSON converter and the FC-3c inspector view provider),
    /// kept on the classifier for editor-side discoverability. Loose twin-field DTOs
    /// (<c>Items</c> + <c>Count</c> declared side-by-side on the DTO) are deliberately NOT
    /// recognized -- they remain read-only passthrough (Q#21-A1).
    /// </summary>
    public static bool TryGetFixedListShape(Type t, out Type elementType, out int capacity)
        => Fdp.Core.FixedListShape.TryGet(t, out elementType, out capacity);
}
