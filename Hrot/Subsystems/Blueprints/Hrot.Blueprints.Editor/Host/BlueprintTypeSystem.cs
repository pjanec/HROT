using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <para><c>ITypeSystem</c> for Blueprint data-flow pins.</para>
/// <para>
/// Type keys match the <c>TypeId</c> strings stored in <c>BlueprintTypeRef</c>.
/// Exec pins use the special sentinel <c>TypeKey.Empty</c>; their type key is never
/// compared as a data type.
/// </para>
/// <para>
/// Implicit-cast rules: Blueprint supports a single widening cast: <c>System.Int32</c> →
/// <c>System.Single</c> (int to float).  All other same-type connections are exact matches.
/// </para>
/// </summary>
public sealed class BlueprintTypeSystem : ITypeSystem
{
    // ── well-known type-id constants ──────────────────────────────────────────

    public const string Bool    = "System.Boolean";
    public const string Int32   = "System.Int32";
    public const string Single  = "System.Single";
    public const string String  = "System.String";
    public const string Vector2 = "System.Numerics.Vector2";
    public const string Vector3 = "System.Numerics.Vector3";
    public const string Float64 = "System.Double";
    public const string Byte    = "System.Byte";
    public const string UInt32  = "System.UInt32";
    public const string Entity  = "Fdp.Core.Entity";

    // ── colour palette (mirrors FakeTypeSystem conventions; Blueprint-specific palette) ─

    private static readonly Dictionary<string, (Vector4 Color, string Name)> _types = new()
    {
        [Bool]    = (new Vector4(0.60f, 0.00f, 0.00f, 1f), "Boolean"),
        [Int32]   = (new Vector4(0.03f, 0.41f, 0.18f, 1f), "Integer"),
        [Single]  = (new Vector4(0.15f, 0.63f, 0.90f, 1f), "Float"),
        [String]  = (new Vector4(0.87f, 0.35f, 0.11f, 1f), "String"),
        [Vector2] = (new Vector4(1.00f, 0.90f, 0.10f, 1f), "Vector2"),
        [Vector3] = (new Vector4(1.00f, 0.90f, 0.10f, 1f), "Vector3"),
        [Float64] = (new Vector4(0.20f, 0.65f, 0.90f, 1f), "Double"),
        [Byte]    = (new Vector4(0.50f, 0.50f, 0.50f, 1f), "Byte"),
        [UInt32]  = (new Vector4(0.30f, 0.55f, 0.25f, 1f), "UInt32"),
        [Entity]  = (new Vector4(0.20f, 0.85f, 0.70f, 1f), "Entity"),
        // EQS handle type
        ["FDP.Eqs.EqsSensorHandle"] = (new Vector4(0.78f, 0.50f, 0.10f, 1f), "EqsSensorHandle"),
    };

    private readonly IPinDefaultValueEditorRegistry _editors;

    /// <param name="editors">
    /// Registry for per-type inline default-value editors.
    /// Pass <c>NullPinDefaultValueEditorRegistry.Instance</c> in tests.
    /// </param>
    public BlueprintTypeSystem(IPinDefaultValueEditorRegistry editors)
    {
        _editors = editors;
    }

    // ── ITypeSystem ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
    {
        if (_types.TryGetValue(key.Id, out var t))
        {
            info = new TypeDisplayInfo(t.Name, null, null);
            return true;
        }
        info = default!;
        return false;
    }

    /// <inheritdoc/>
    public Vector4 GetPinColor(TypeKey key)
    {
        if (key.IsEmpty)
            return new Vector4(1f, 1f, 1f, 1f); // exec: white
        return _types.TryGetValue(key.Id, out var t)
            ? t.Color
            : new Vector4(0.8f, 0.8f, 0.8f, 1f); // unknown: grey
    }

    /// <inheritdoc/>
    public PinShape GetPinShape(TypeKey key, ContainerKind container)
    {
        if (key.IsEmpty)
            return PinShape.Triangle; // exec pins use the triangle glyph

        return container switch
        {
            ContainerKind.Array => PinShape.Diamond,
            ContainerKind.Map   => PinShape.Square,
            ContainerKind.Set   => PinShape.Pentagon,
            _                   => PinShape.Circle,
        };
    }

    /// <inheritdoc/>
    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => _editors.GetEditor(key);

    /// <inheritdoc/>
    /// <remarks>
    /// Compatibility rules:
    /// <list type="bullet">
    ///   <item>Exec (empty key) is compatible only with Exec.</item>
    ///   <item>Data pins are compatible when their type keys are equal.</item>
    ///   <item>Int32 → Single is also compatible (widening; Blueprint allows this).</item>
    /// </list>
    /// </remarks>
    public bool AreCompatible(TypeKey from, TypeKey to)
    {
        if (from == to) return true;
        // Widening: int → float
        if (from.Id == Int32 && to.Id == Single) return true;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>Only the int→float cast is implicit; all others require an explicit Cast node.</remarks>
    public bool IsImplicitCast(TypeKey from, TypeKey to)
        => from.Id == Int32 && to.Id == Single;

    /// <summary>
    /// The selectable variable type ids offered by the editor (e.g. the variable-create
    /// modal's type dropdown). Ordered for a stable UI; the first entry is a sensible default.
    /// </summary>
    public static IReadOnlyList<string> SelectableTypeIds { get; } = new[]
    {
        Bool, Int32, Single, Float64, String, Byte, UInt32,
        Vector2, Vector3, Entity,
    };

    // ── Static helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the accent color for a type id, using the same palette as
    /// <see cref="GetPinColor"/>. Returns <c>null</c> for exec (empty typeId)
    /// or unknown types (callers may choose to omit the dot in that case).
    /// </summary>
    public static Vector4? GetAccentColorForTypeId(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return null;
        return _types.TryGetValue(typeId, out var t) ? t.Color : null;
    }
}
