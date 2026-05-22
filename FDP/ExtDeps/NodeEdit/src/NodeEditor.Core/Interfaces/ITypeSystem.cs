using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Host-defined type system. Provides display info, colors, shapes, and
/// compatibility rules for typed pins.
/// </summary>
public interface ITypeSystem
{
    /// <summary>Try to fetch display info for a type key.</summary>
    bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info);

    /// <summary>Pin color for the given data type.</summary>
    Vector4 GetPinColor(TypeKey key);

    /// <summary>Pin shape for the given type and container kind.</summary>
    PinShape GetPinShape(TypeKey key, ContainerKind container);

    /// <summary>Get the registered default-value editor for the type, if any.</summary>
    IPinDefaultValueEditor? GetDefaultEditor(TypeKey key);

    /// <summary>True if a value of type <paramref name="from"/> can be used where <paramref name="to"/> is expected.</summary>
    bool AreCompatible(TypeKey from, TypeKey to);

    /// <summary>True if the compatibility is implicit (no cast node needed).</summary>
    bool IsImplicitCast(TypeKey from, TypeKey to);
}

/// <summary>Display info for a type.</summary>
public sealed record TypeDisplayInfo(
    string DisplayName,
    string? Description,
    string? IconKey);

/// <summary>Editor for a pin's default value.</summary>
public interface IPinDefaultValueEditor
{
    /// <summary>
    /// Render and edit a value.
    /// </summary>
    /// <param name="value">Current value (boxed). Modified in place on edit.</param>
    /// <param name="ctx">Context describing pin, max width, metadata.</param>
    /// <param name="committed">True when the change should be committed as an undoable command.</param>
    /// <returns>True if the value changed this frame.</returns>
    bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed);
}

/// <summary>Context passed to default-value editors during Draw.</summary>
public readonly record struct DefaultEditorContext(
    PinId Pin,
    TypeKey Type,
    float MaxWidth,
    bool IsReadOnly,
    PinDefaultMetadata Metadata);
