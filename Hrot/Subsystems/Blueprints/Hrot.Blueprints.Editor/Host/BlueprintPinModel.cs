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
        : this(pin, ownerNodeId, editorRegistry: null)
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
    {
        Id          = new PinId(pin.Id);
        OwnerNodeId = ownerNodeId;
        Label       = pin.Name;
        Direction   = pin.Direction == "In" ? PinDirection.Input : PinDirection.Output;
        Kind        = pin.IsExec ? PinKind.Exec : PinKind.Data;
        Type        = pin.IsExec ? null : new TypeKey(pin.TypeRef.TypeId);
        Shape       = pin.IsExec ? PinShape.Triangle
            : pin.TypeRef.IsArray ? PinShape.Diamond
            : PinShape.Circle;

        // Expose a default-value container for unconnected input data pins.
        // Conditions: !Exec AND Direction=="In".
        // With registry: always show when the type has a registered editor (even if DefaultValue==null).
        // Without registry (legacy): only when DefaultValue is already persisted.
        if (!pin.IsExec && pin.Direction == "In")
        {
            if (pin.DefaultValue != null)
            {
                // Always expose persisted default value.
                Default = new BlueprintPinDefaultValue(pin.TypeRef.TypeId, pin.DefaultValue);
            }
            else if (editorRegistry != null)
            {
                // No persisted value yet — show a type-zero editor only when the type has
                // a registered editor (avoids empty/blank widgets for unsupported types).
                var typeKey = new TypeKey(pin.TypeRef.TypeId);
                if (editorRegistry.GetEditor(typeKey) != null)
                    Default = new BlueprintPinDefaultValue(pin.TypeRef.TypeId, rawValue: null);
            }
        }
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
    {
        Value = ParseValue(typeId, rawValue);
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
    /// <b>Enum pins</b> (<c>typeId</c> starts with <c>"global::"</c>): the persisted value is an
    /// integer string (per ENUM-DESIGN.md §RESOLVED — byte-stable, survives member renames).
    /// Returns <c>(long)N</c> so <see cref="NodeEditor.UI.MiniEditors.EnumPinEditor.Draw"/>
    /// can index the combo (<c>value is long</c> check).  Null/empty → <c>0L</c>.
    /// </para>
    /// </summary>
    public static object? ParseValue(string typeId, string? rawValue)
    {
        // Enum sentinel: "global::" prefix (AN2 contract).
        // Persisted as integer string; return long for EnumPinEditor.Draw.
        if (!string.IsNullOrEmpty(typeId)
            && typeId.StartsWith("global::", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(rawValue)) return 0L;
            return long.TryParse(rawValue, out var enumLong) ? enumLong : 0L;
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
                "System.String"       => (object)"",
                "Fdp.Core.FixedString32" => (object)"",
                "Fdp.Core.FixedString64" => (object)"",
                _                     => null,   // unsupported type — no widget shown
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
            _                => rawValue,   // string, unknown → raw string
        };
    }

    /// <summary>
    /// Convert the boxed CLR value back to the persisted string representation.
    /// </summary>
    public static string? FormatValue(object? value) => value switch
    {
        null      => null,
        bool b    => b.ToString().ToLowerInvariant(),
        float f   => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double d  => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _         => value.ToString(),
    };
}
