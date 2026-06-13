using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// A hidden any-pin for HSM state nodes.
// States have one output pin (source of outgoing transitions)
// and one input pin (target of incoming transitions).
// These pins are invisible in the canvas; they exist only to satisfy
// NodeEditor's pin-based link primitive.
internal sealed class HsmPinModel : IPinModel
{
    public PinId Id { get; }
    public NodeId OwnerNodeId { get; }
    public string Label { get; }
    public PinDirection Direction { get; }
    public PinKind Kind => PinKind.Data;
    public TypeKey? Type => null;
    public PinShape Shape => PinShape.None;
    public bool IsAdvanced => false;
    public bool IsOptional => true;
    public string? Tooltip => null;
    public IPinDefaultValue? Default => null;

    internal HsmPinModel(PinId id, NodeId ownerNodeId, PinDirection direction)
    {
        Id = id;
        OwnerNodeId = ownerNodeId;
        Direction = direction;
        Label = "";
    }
}
