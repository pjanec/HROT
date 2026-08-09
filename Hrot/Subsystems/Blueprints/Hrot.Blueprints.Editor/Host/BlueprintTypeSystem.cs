using System.Collections.Concurrent;
using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Ir;
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
///
/// <para>
/// ⭐ <b>BP-203 — every type question is answered by resolving through
/// <see cref="StaticTypeRegistry"/> first.</b> Type ids reach this class in two spellings: the
/// <b>bare alias</b> the editor's own pickers write (<c>"int"</c>, <c>"FixedString32"</c> — see
/// <c>StaticTypeRegistry.EditorOfferableTypeIds</c>) and the <b>canonical FQN</b> recipes, literals
/// and the compiler use (<c>"System.Int32"</c>). Comparing the raw strings makes those two
/// spellings different types, so <b>anything authored in the editor was unwirable to anything from a
/// recipe</b> — the user's *"Return node pins could not be wired to int and string literals"*.
/// Resolution collapses both onto one <c>IrTypeRef.FullName</c> before anything is compared.
/// </para>
///
/// <para>
/// ⚠ <b>Coercion is <see cref="StaticTypeRegistry"/>'s answer, never a second list here.</b> This
/// class used to carry its own one-rung table (<c>Int32 → Single</c>) while the compiler's
/// <c>CoercionTable</c> carried 35 — so the editor <b>refused 34 wires the compiler accepts</b>,
/// including the <c>ushort → int</c> the user's own BP-87 ruling required. <see cref="AreCompatible"/>
/// now mirrors <c>Stage4_TypeResolve.VerifyLinkTypes</c> rung for rung. ⭐ <b>Third instance of the
/// same lesson</b> (BP-87 item 5, BP-114): a hand-maintained duplicate of a compiler table always
/// drifts, and the drift shows up as an unexplainable editor glitch.
/// </para>
/// </summary>
public sealed class BlueprintTypeSystem : ITypeSystem
{
    // ── well-known type-id constants ──────────────────────────────────────────

    public const string Bool         = "System.Boolean";
    public const string Int32        = "System.Int32";
    public const string Single       = "System.Single";
    public const string String       = "System.String";
    public const string Vector2      = "System.Numerics.Vector2";
    public const string Vector3      = "System.Numerics.Vector3";
    public const string Float64      = "System.Double";
    public const string Byte         = "System.Byte";
    public const string UInt32       = "System.UInt32";
    public const string Entity       = "Fdp.Core.Entity";
    public const string FixedString32 = "Fdp.Core.FixedString32";
    public const string FixedString64 = "Fdp.Core.FixedString64";
    public const string FixedString128 = "Fdp.Core.FixedString128";

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
        // Fdp.Core fixed-length string types (unmanaged, blittable; teal-green, string-ish)
        [FixedString32] = (new Vector4(0.25f, 0.75f, 0.55f, 1f), "FixedString32"),
        [FixedString64] = (new Vector4(0.25f, 0.65f, 0.50f, 1f), "FixedString64"),
        [FixedString128] = (new Vector4(0.25f, 0.55f, 0.45f, 1f), "FixedString128"),
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
        if (TryGetPaletteEntry(key.Id, out var t))
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
        if (TryGetPaletteEntry(key.Id, out var t))
            return t.Color;
        // Enum pins carry a "global::" prefix (AN2 sentinel); render in a distinct lavender.
        if (!string.IsNullOrEmpty(key.Id)
            && key.Id.StartsWith("global::", StringComparison.Ordinal))
            return new Vector4(0.65f, 0.55f, 0.85f, 1f); // lavender
        return new Vector4(0.8f, 0.8f, 0.8f, 1f); // unknown: grey
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
    ///   <item>
    ///   <c>System.Object</c> on either side is a "typed-unknown placeholder" wildcard -- mirrors
    ///   the compiler's <c>Stage4_TypeResolve.VerifyLinkTypes</c> identical rule EXACTLY (CA-07c):
    ///   a freshly-placed <c>ComponentForEachNode</c>/<c>ComponentItemGetNode</c>/
    ///   <c>ComponentItemCountNode</c>/<c>ComponentContainsNode</c>/<c>ComponentFindNode</c>
    ///   (CA-07d-1)'s "Collection" data-IN pin projects as
    ///   <c>System.Object</c> (IsArray) until <see cref="BlueprintCommandSink"/>'s wire-bake hook
    ///   re-types it from the source <c>GetComponentNode</c> collection pin's real element type --
    ///   without this rule the FIRST wire attempt (real element type -&gt; System.Object) would be
    ///   rejected here even though the compiler already accepts it, so the consumer nodes could
    ///   never be wired up in the editor at all.
    ///   </item>
    /// </list>
    /// </remarks>
    public bool AreCompatible(TypeKey from, TypeKey to)
    {
        if (from == to) return true;

        // Exec pins carry TypeKey.Empty (Id ""). An empty id resolves to nothing, so the checks
        // below can never pair exec with data -- but bail early rather than relying on that.
        if (string.IsNullOrEmpty(from.Id) || string.IsNullOrEmpty(to.Id)) return false;

        var fromType = Resolve(from.Id);
        var toType   = Resolve(to.Id);

        // BP-203: one canonical spelling per type, so "int" and "System.Int32" are the same type.
        if (fromType != null && toType != null)
        {
            if (fromType.FullName == toType.FullName) return true;
            if (StaticTypeRegistry.Instance.TryGetCoercion(fromType, toType, out _)) return true;
        }

        // Typed-unknown placeholder wildcard (mirrors Stage4_TypeResolve.VerifyLinkTypes). Only a
        // DATA pin can be the placeholder: the OTHER side must be a real data type (non-empty Id),
        // so this never swallows the exec/data kind split (exec pins carry TypeKey.Empty — Id "").
        // ⚠ Compared on the resolved name where one exists, so a "System.Object" reached via an alias
        // is recognised too; the array-element unwrap matches VerifyLinkTypes.WildcardFullName.
        if (WildcardName(from.Id, fromType) == "System.Object") return true;
        if (WildcardName(to.Id,   toType)   == "System.Object") return true;

        // An unresolvable id is a project/enum type the reflection-less registry cannot verify
        // (AN2's "global::" sentinel resolves; a curated struct FQN may not). The compiler's
        // VerifyLinkTypes simply returns when a pin type is absent from its map rather than
        // reporting a mismatch, so refusing the wire here would be STRICTER than the compiler --
        // exactly the drift BP-203 is about. Fall back to the raw-string equality it replaced.
        if (fromType == null || toType == null) return from.Id == to.Id;

        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// True when the wire needs a widening conversion the compiler will insert for the designer --
    /// i.e. the types differ but <c>StaticTypeRegistry</c> has a coercion rung. ⚠ Purely a display
    /// concern (the canvas marks such wires); <see cref="AreCompatible"/> is what accepts them, and
    /// both read the SAME table so a rung can never be wirable-but-unmarked or vice versa.
    /// </remarks>
    public bool IsImplicitCast(TypeKey from, TypeKey to)
    {
        if (from == to) return false;
        if (string.IsNullOrEmpty(from.Id) || string.IsNullOrEmpty(to.Id)) return false;

        var fromType = Resolve(from.Id);
        var toType   = Resolve(to.Id);
        if (fromType == null || toType == null) return false;
        if (fromType.FullName == toType.FullName) return false;

        return StaticTypeRegistry.Instance.TryGetCoercion(fromType, toType, out _);
    }

    // ── BP-203: canonical type resolution ─────────────────────────────────────

    /// <summary>
    /// Resolved <c>IrTypeRef</c> per type id, or <c>null</c> when the registry cannot resolve it.
    ///
    /// <para>
    /// ⚠ <b>Cached because these run per ImGui frame.</b> <see cref="AreCompatible"/> is called while
    /// dragging a wire — once per candidate pin, every frame — and <see cref="GetPinColor"/> once per
    /// drawn pin. The id set is small, fixed and closed (it comes from the registry and from asset
    /// JSON), so an unbounded cache is a lookup table, not a leak.
    /// </para>
    /// </summary>
    private static readonly ConcurrentDictionary<string, IrTypeRef?> _resolved = new(StringComparer.Ordinal);

    private static IrTypeRef? Resolve(string typeId)
        => _resolved.GetOrAdd(typeId, static id =>
            StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = id }, out var ir)
                ? ir
                : null);

    /// <summary>
    /// The name the <c>System.Object</c> wildcard test compares, unwrapping an array to its element
    /// exactly as <c>Stage4_TypeResolve.WildcardFullName</c> does — an array of the placeholder is
    /// just as much a typed-unknown as the scalar placeholder. Falls back to the raw id when the
    /// type does not resolve, so a literal <c>"System.Object"</c> id still reads as the wildcard.
    /// </summary>
    private static string WildcardName(string typeId, IrTypeRef? resolved)
    {
        if (resolved == null) return typeId;
        return resolved.IsArray && resolved.ElementType != null
            ? resolved.ElementType.FullName
            : resolved.FullName;
    }

    /// <summary>
    /// The selectable variable type ids offered by the editor (e.g. the variable-create
    /// modal's type dropdown). Ordered for a stable UI; the first entry is a sensible default.
    /// </summary>
    public static IReadOnlyList<string> SelectableTypeIds { get; } = new[]
    {
        Bool, Int32, Single, Float64, String, Byte, UInt32,
        Vector2, Vector3, Entity,
        FixedString32, FixedString64, FixedString128,
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
        return TryGetPaletteEntry(typeId, out var t) ? t.Color : (Vector4?)null;
    }

    /// <summary>
    /// BP-203, display half: look the palette up by the raw id first, then by the id's canonical
    /// <c>IrTypeRef.FullName</c>.
    ///
    /// <para>
    /// ⚠ <b>The palette is keyed by FQN only</b>, so before this a pin whose type came from a picker
    /// (which writes bare aliases like <c>"int"</c>) rendered as an unnamed grey circle while the
    /// identical type written as <c>"System.Int32"</c> rendered green and named <i>Integer</i>. Same
    /// alias-vs-FQN split as the wiring half, one surface over — and visibly so, which is how a
    /// designer would first notice it.
    /// </para>
    /// </summary>
    private static bool TryGetPaletteEntry(string typeId, out (Vector4 Color, string Name) entry)
    {
        if (!string.IsNullOrEmpty(typeId))
        {
            if (_types.TryGetValue(typeId, out entry)) return true;

            var resolved = Resolve(typeId);
            if (resolved != null && _types.TryGetValue(resolved.FullName, out entry)) return true;
        }

        entry = default;
        return false;
    }
}
