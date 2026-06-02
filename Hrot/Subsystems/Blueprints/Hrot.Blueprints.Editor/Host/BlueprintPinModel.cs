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
    public IPinDefaultValue? Default => null;

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
    }
}
