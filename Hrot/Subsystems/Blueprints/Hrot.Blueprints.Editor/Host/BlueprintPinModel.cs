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
    /// Returns an <see cref="IPinDefaultValue"/> for unconnected input data pins whose
    /// <see cref="Hrot.Blueprints.Core.Assets.Pin.DefaultValue"/> has been set; null otherwise.
    /// The canvas inline-editor only renders when this is non-null.
    /// </summary>
    public IPinDefaultValue? Default { get; }

    public BlueprintPinModel(
        Hrot.Blueprints.Core.Assets.Pin pin,
        NodeId ownerNodeId)
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
        // Direction=="In", Kind==Data, and a persisted DefaultValue are all required.
        if (!pin.IsExec && pin.Direction == "In" && pin.DefaultValue != null)
            Default = new BlueprintPinDefaultValue(pin.TypeRef.TypeId, pin.DefaultValue);
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

    public BlueprintPinDefaultValue(string typeId, string rawValue)
    {
        Value = ParseValue(typeId, rawValue);
    }

    /// <summary>
    /// Convert the persisted string representation to the boxed CLR type expected by the
    /// built-in mini-editors (BoolPinEditor → bool, IntPinEditor → int, etc.).
    /// Falls back to the raw string for unknown/string types.
    /// </summary>
    public static object? ParseValue(string typeId, string rawValue)
    {
        if (rawValue == null) return null;
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
            _                => rawValue,   // string, enum raw int string, unknown → raw string
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
