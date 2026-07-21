using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Read-only <see cref="IPinModel"/> adapter projecting a <see cref="Hrot.Blueprints.Core.Assets.Pin"/>
/// onto the NodeEdit canvas contract.
/// </summary>
internal sealed class BlueprintPinModel : IPinModel
{
    public PinId        Id          { get; }
    public NodeId       OwnerNodeId { get; }
    public string       Label       { get; }
    public PinDirection Direction   { get; }
    public PinKind      Kind        { get; }
    public TypeKey?     Type        { get; }
    public PinShape     Shape       { get; }
    public bool         IsAdvanced  { get; }
    public bool         IsOptional  { get; }
    public string?      Tooltip     { get; }

    /// <summary>
    /// Returns an <see cref="IPinDefaultValue"/> for unconnected input data pins.
    /// <para>
    /// When a <paramref name="editorRegistry"/> is supplied and the pin's type has a
    /// registered editor, <c>Default</c> is <b>always</b> non-null for unconnected
    /// In-data pins — even when <see cref="Hrot.Blueprints.Core.Assets.Pin.DefaultValue"/>
    /// has not been set yet.  In that case <see cref="BlueprintPinDefaultValue"/> synthesises
    /// a type-zero boxed value (0 / 0f / false / "") so the widget can render immediately.
    /// </para>
    /// <para>
    /// When no registry is supplied (legacy two-arg ctor, headless tests), the previous
    /// behaviour is preserved: <c>Default</c> is non-null only when
    /// <see cref="Hrot.Blueprints.Core.Assets.Pin.DefaultValue"/> is already set.
    /// </para>
    /// The canvas <c>NodeRenderer.DrawInlineEditors</c> already hides the editor when the
    /// pin is connected (<c>connectedInputPins.Contains(p.Id)</c>) — do not re-implement
    /// that guard here.
    /// </summary>
    public IPinDefaultValue? Default { get; }

    /// <summary>
    /// Constructs a BlueprintPinModel without an editor registry (legacy / headless-test path).
    /// <c>Default</c> is non-null only when <paramref name="pin"/>.DefaultValue is already set.
    /// </summary>
    public BlueprintPinModel(
        Hrot.Blueprints.Core.Assets.Pin pin,
        NodeId ownerNodeId)
        : this(pin, ownerNodeId, editorRegistry: null, enumProvider: null)
    { }

    /// <summary>
    /// Constructs a BlueprintPinModel with an optional editor registry.
    /// When <paramref name="editorRegistry"/> is non-null and the pin's type has a registered
    /// editor, <c>Default</c> is always non-null for unconnected In-data pins (showing type-zero).
    /// </summary>
    public BlueprintPinModel(
        Hrot.Blueprints.Core.Assets.Pin pin,
        NodeId ownerNodeId,
        IPinDefaultValueEditorRegistry? editorRegistry)
        : this(pin, ownerNodeId, editorRegistry, enumProvider: null)
    { }

    /// <summary>
    /// Constructs a BlueprintPinModel with an optional editor registry and optional enum-value
    /// provider.  The <paramref name="enumProvider"/> is forwarded to
    /// <see cref="BlueprintPinDefaultValue"/> so that enum-pin defaults stored as member names
    /// (ENUM-NAME) are resolved to the correct <c>long</c> for the combo editor.
    /// </summary>
    public BlueprintPinModel(
        Hrot.Blueprints.Core.Assets.Pin pin,
        NodeId ownerNodeId,
        IPinDefaultValueEditorRegistry? editorRegistry,
        IEnumValueProvider?             enumProvider,
        string?                         displayLabel = null,
        bool                            glyphless    = false)
    {
        Id          = new PinId(pin.Id);
        OwnerNodeId = ownerNodeId;
        // Display label may differ from the pin's identity Name (e.g. GetParameter's "Value" out-pin
        // shows the parameter's NAME). The identity Name is untouched, so pin GUIDs / link
        // rehydration are unaffected — this is render-only.
        // glyphless (Literal's inline-editor input pin): no pin glyph, no label — only the value box.
        Label       = glyphless ? "" : (string.IsNullOrEmpty(displayLabel) ? pin.Name : displayLabel);
        Direction   = pin.Direction == "In" ? PinDirection.Input : PinDirection.Output;
        Kind        = pin.IsExec ? PinKind.Exec : PinKind.Data;
        Type        = pin.IsExec ? null : new TypeKey(pin.TypeRef.TypeId);
        Shape       = glyphless ? PinShape.None
            : pin.IsExec ? PinShape.Triangle
            : pin.TypeRef.IsArray ? PinShape.Diamond
            : PinShape.Circle;

        // Punch-list #4: every data pin surfaces its data type on hover ("data type mandatory").
        // Exec pins are self-explanatory glyphs → no tooltip.
        Tooltip     = pin.IsExec ? null : BuildPinTooltip(pin.Name, pin.TypeRef.TypeId, pin.TypeRef.IsArray);

        // Expose a default-value container for unconnected input data pins.
        // Conditions: !Exec AND Direction=="In".
        // With registry: always show when the type has a registered editor (even if DefaultValue==null).
        // Without registry (legacy): only when DefaultValue is already persisted.
        if (!pin.IsExec && pin.Direction == "In")
        {
            if (pin.DefaultValue != null)
            {
                // Always expose persisted default value.
                // Pass the enum provider so that name-stored defaults resolve to the correct long.
                Default = new BlueprintPinDefaultValue(pin.TypeRef.TypeId, pin.DefaultValue, enumProvider);
            }
            else if (editorRegistry != null)
            {
                // No persisted value yet — show a type-zero editor only when the type has
                // a registered editor (avoids empty/blank widgets for unsupported types).
                var typeKey = new TypeKey(pin.TypeRef.TypeId);
                if (editorRegistry.GetEditor(typeKey) != null)
                    Default = new BlueprintPinDefaultValue(pin.TypeRef.TypeId, rawValue: null, enumProvider);
            }
        }
    }

    /// <summary>
    /// Punch-list #4: builds a data-pin hover tooltip. First line is <c>name : ShortType</c>; when the
    /// short name hides a distinct fully-qualified id, a second dimmer line shows the full type id so the
    /// author can disambiguate struct/class returns. Array pins are marked <c>[]</c>.
    /// </summary>
    private static string BuildPinTooltip(string name, string typeId, bool isArray)
    {
        var shortName = TooltipText.ShortTypeName(typeId) + (isArray ? "[]" : "");
        var display   = string.IsNullOrEmpty(typeId) ? "object" : typeId;
        if (display.StartsWith("global::", StringComparison.Ordinal)) display = display["global::".Length..];
        var line1 = $"{name} : {shortName}";
        return string.Equals(shortName.TrimEnd('[', ']'), display, StringComparison.Ordinal)
            ? line1
            : line1 + "\n" + display + (isArray ? "[]" : "");
    }
}

/// <summary>
/// Immutable <see cref="IPinDefaultValue"/> backed by the string stored in
/// <see cref="Hrot.Blueprints.Core.Assets.Pin.DefaultValue"/>.
/// Parses the string to a boxed CLR value that the registered <see cref="IPinDefaultValueEditor"/>
/// can read and display.
/// </summary>
internal sealed class BlueprintPinDefaultValue : IPinDefaultValue
{
    private static readonly PinDefaultMetadata _noMeta =
        new(null, null, null, null, null, null, false);

    public object? Value    { get; }
    public PinDefaultMetadata Metadata => _noMeta;

    public BlueprintPinDefaultValue(string typeId, string? rawValue)
        : this(typeId, rawValue, enumProvider: null)
    { }

    /// <summary>
    /// Constructs with an optional <paramref name="enumProvider"/> used to resolve enum member
    /// names stored in <paramref name="rawValue"/> back to their <c>long</c> values.
    /// When the provider is <c>null</c> or cannot resolve the name, falls back to 0L.
    /// </summary>
    public BlueprintPinDefaultValue(string typeId, string? rawValue, IEnumValueProvider? enumProvider)
    {
        Value = ParseValue(typeId, rawValue, enumProvider);
    }

    /// <summary>
    /// Convert the persisted string representation to the boxed CLR type expected by the
    /// built-in mini-editors (BoolPinEditor → bool, IntPinEditor → int, etc.).
    /// Falls back to the raw string for unknown/string types.
    /// <para>
    /// When <paramref name="rawValue"/> is <c>null</c> or empty, returns the type's
    /// zero value (0 / 0f / false / "") so a freshly-placed unset pin renders at zero
    /// rather than showing nothing.
    /// </para>
    /// <para>
    /// <b>Enum pins</b> (<c>typeId</c> starts with <c>"global::"</c>): the persisted value is
    /// now a member NAME string (e.g. "Crouching") per ENUM-NAME.  If a provider is supplied
    /// the name is resolved to the matching <c>long</c>.  Pure-integer strings (backward compat
    /// / fallback) are still parsed directly.  Null/empty → <c>0L</c>.
    /// </para>
    /// </summary>
    public static object? ParseValue(string typeId, string? rawValue)
        => ParseValue(typeId, rawValue, enumProvider: null);

    /// <summary>
    /// Overload that accepts an optional <paramref name="enumProvider"/> for name→long resolution.
    /// </summary>
    public static object? ParseValue(string typeId, string? rawValue, IEnumValueProvider? enumProvider)
    {
        // Enum sentinel: "global::" prefix (AN2 contract).
        if (!string.IsNullOrEmpty(typeId)
            && typeId.StartsWith("global::", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(rawValue)) return 0L;

            // Fast path: pure integer string (old assets / fallback).
            if (long.TryParse(rawValue, out var enumLong)) return enumLong;

            // Name-based lookup (ENUM-NAME).
            if (enumProvider != null)
            {
                var typeKey = new TypeKey(typeId);
                var entries = enumProvider.GetValues(typeKey);
                foreach (var e in entries)
                {
                    if (string.Equals(e.DisplayName, rawValue, StringComparison.Ordinal))
                        return e.Value;
                }
            }

            // Unresolvable name → treat as zero (graceful fallback).
            return 0L;
        }

        // Null / empty raw value → synthesise a type-zero for known numeric/bool types,
        // empty string for System.String, and null for completely unknown types.
        if (string.IsNullOrEmpty(rawValue))
        {
            return typeId switch
            {
                "System.Boolean" => (object)false,
                "System.Int32"   => (object)0,
                "System.Single"  => (object)0f,
                "System.Double"  => (object)0.0,
                "System.Byte"    => (object)(byte)0,
                "System.UInt32"  => (object)0u,
                "System.String"           => (object)"",
                "Fdp.Core.FixedString32"  => (object)"",
                "Fdp.Core.FixedString64"  => (object)"",
                // FIX-B: vector zero-values for freshly-placed unset pins.
                "System.Numerics.Vector2"    => (object)System.Numerics.Vector2.Zero,
                "System.Numerics.Vector3"    => (object)System.Numerics.Vector3.Zero,
                "System.Numerics.Vector4"    => (object)System.Numerics.Vector4.Zero,
                "System.Numerics.Quaternion" => (object)System.Numerics.Quaternion.Identity,
                _                            => null,   // unsupported type — no widget shown
            };
        }
        return typeId switch
        {
            "System.Boolean" => bool.TryParse(rawValue, out var b)  ? b     : false,
            "System.Int32"   => int.TryParse(rawValue,  out var i)  ? i     : 0,
            "System.Single"  => float.TryParse(rawValue,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var f) ? f : 0f,
            "System.Double"  => double.TryParse(rawValue,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var d) ? d : 0.0,
            "System.Byte"    => byte.TryParse(rawValue,   out var by) ? by  : (byte)0,
            "System.UInt32"  => uint.TryParse(rawValue,   out var u)  ? u   : 0u,
            // FIX-B: Vector types — parse the InvariantCulture bracket format [x, y, z, …].
            // The old culture-dependent <x  y  z> form from value.ToString() is also tolerated
            // via the same float-parse path (whitespace is consumed by TryParse).
            "System.Numerics.Vector2"     => ParseVector2(rawValue),
            "System.Numerics.Vector3"     => ParseVector3(rawValue),
            "System.Numerics.Vector4"     => ParseVector4(rawValue),
            "System.Numerics.Quaternion"  => ParseQuaternion(rawValue),
            _                => rawValue,   // string, unknown → raw string
        };
    }

    /// <summary>
    /// Convert the boxed CLR value back to the persisted string representation.
    /// For non-enum types, this is a simple ToString / invariant-culture format.
    /// For enum pins use <see cref="FormatEnumValue"/> instead.
    /// <para>
    /// Vector2/3/4 and Quaternion are formatted as <c>[x, y]</c> / <c>[x, y, z]</c> /
    /// <c>[x, y, z, w]</c> using <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
    /// so the value is locale-independent and round-trips correctly on every machine (FIX-B).
    /// </para>
    /// </summary>
    public static string? FormatValue(object? value)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return value switch
        {
            null      => null,
            bool b    => b.ToString().ToLowerInvariant(),
            float f   => f.ToString(inv),
            double d  => d.ToString(inv),
            System.Numerics.Vector2 v2 =>
                $"[{v2.X.ToString(inv)}, {v2.Y.ToString(inv)}]",
            System.Numerics.Vector3 v3 =>
                $"[{v3.X.ToString(inv)}, {v3.Y.ToString(inv)}, {v3.Z.ToString(inv)}]",
            System.Numerics.Vector4 v4 =>
                $"[{v4.X.ToString(inv)}, {v4.Y.ToString(inv)}, {v4.Z.ToString(inv)}, {v4.W.ToString(inv)}]",
            System.Numerics.Quaternion q =>
                $"[{q.X.ToString(inv)}, {q.Y.ToString(inv)}, {q.Z.ToString(inv)}, {q.W.ToString(inv)}]",
            _         => value.ToString(),
        };
    }

    /// <summary>
    /// Converts the long integer value selected by <see cref="NodeEditor.UI.MiniEditors.EnumPinEditor"/>
    /// to the member NAME string for persistence (ENUM-NAME).
    /// When the provider is null or the value doesn't match any member, falls back to the
    /// decimal integer string (backward-compat / graceful degradation).
    /// </summary>
    public static string FormatEnumValue(long value, string typeId, IEnumValueProvider? provider)
    {
        if (provider != null)
        {
            var typeKey = new TypeKey(typeId);
            var entries = provider.GetValues(typeKey);
            foreach (var e in entries)
            {
                if (e.Value == value)
                    return e.DisplayName;
            }
        }
        // Fallback: decimal integer string (still readable by FormatDefaultLiteral back-compat branch).
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ── FIX-B: Vector / Quaternion parse helpers ──────────────────────────────
    // Accepts the new InvariantCulture bracket format "[x, y, z]" produced by FormatValue,
    // AND the old locale-dependent "<x  y  z>" form from value.ToString() for migration.
    // Parsing strips any leading/trailing bracket characters and splits on commas / whitespace.

    private static float[] SplitFloats(string raw)
    {
        // Strip bracket/angle-bracket delimiters: "[", "]", "<", ">" then split.
        var stripped = raw.Trim().TrimStart('[', '<').TrimEnd(']', '>');
        var parts    = stripped.Split(new[] { ',', ' ', '\t' },
                           System.StringSplitOptions.RemoveEmptyEntries);
        var inv      = System.Globalization.CultureInfo.InvariantCulture;
        var result   = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            float.TryParse(parts[i],
                System.Globalization.NumberStyles.Float, inv, out result[i]);
        return result;
    }

    private static object ParseVector2(string raw)
    {
        var c = SplitFloats(raw);
        return c.Length >= 2
            ? new System.Numerics.Vector2(c[0], c[1])
            : System.Numerics.Vector2.Zero;
    }

    private static object ParseVector3(string raw)
    {
        var c = SplitFloats(raw);
        return c.Length >= 3
            ? new System.Numerics.Vector3(c[0], c[1], c[2])
            : System.Numerics.Vector3.Zero;
    }

    private static object ParseVector4(string raw)
    {
        var c = SplitFloats(raw);
        return c.Length >= 4
            ? new System.Numerics.Vector4(c[0], c[1], c[2], c[3])
            : System.Numerics.Vector4.Zero;
    }

    private static object ParseQuaternion(string raw)
    {
        var c = SplitFloats(raw);
        return c.Length >= 4
            ? new System.Numerics.Quaternion(c[0], c[1], c[2], c[3])
            : System.Numerics.Quaternion.Identity;
    }
}
