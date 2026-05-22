using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Mutable pin model used by the fake graph.</summary>
public sealed class FakePinModel : IPinModel
{
    public PinId   Id           { get; }
    public NodeId  OwnerNodeId  { get; }
    public string  Label        { get; set; }
    public PinDirection Direction { get; }
    public PinKind Kind         { get; }
    public TypeKey? Type        { get; }
    public PinShape Shape       { get; set; } = PinShape.Circle;
    public bool IsAdvanced      { get; set; }
    public bool IsOptional      { get; set; }
    public string? Tooltip      { get; set; }
    public IPinDefaultValue? Default { get; set; }

    public FakePinModel(PinId id, NodeId ownerNodeId, string label,
                        PinDirection direction, PinKind kind, TypeKey? type = null)
    {
        Id          = id;
        OwnerNodeId = ownerNodeId;
        Label       = label;
        Direction   = direction;
        Kind        = kind;
        Type        = type;
    }
}

/// <summary>Simple in-memory default-value container.</summary>
public sealed class FakePinDefaultValue : IPinDefaultValue
{
    public object? Value    { get; set; }
    public PinDefaultMetadata Metadata { get; }

    public FakePinDefaultValue(object? value, PinDefaultMetadata? meta = null)
    {
        Value    = value;
        Metadata = meta ?? new PinDefaultMetadata(null, null, null, null, null, null, false);
    }
}
