using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Mutable node model used by the fake graph.</summary>
public sealed class FakeNodeModel : INodeModel
{
    private readonly List<IPinModel> _pins = new();

    public NodeId        Id           { get; }
    public NodeKindKey   Kind         { get; }
    public string        Title        { get; set; }
    public string?       Subtitle     { get; set; }
    public NodeCategory  Category     { get; set; } = NodeCategory.Function;
    public Vector2       Position     { get; set; }
    public Vector2?      SizeOverride { get; set; }
    public NodeState     State        { get; set; } = NodeState.Normal;
    public string?       StatusTooltip { get; set; }
    public bool          IsCollapsed  { get; set; }
    public bool          ShowAdvancedPins { get; set; }
    public IReadOnlyList<IPinModel> Pins => _pins;

    public FakeNodeModel(NodeId id, NodeKindKey kind, string title, Vector2 position)
    {
        Id       = id;
        Kind     = kind;
        Title    = title;
        Position = position;
    }

    public FakePinModel AddPin(
        string label,
        PinDirection direction,
        PinKind kind,
        TypeKey? type = null,
        PinShape shape = PinShape.Circle)
    {
        var id  = IdGenerator.NewPinId();
        var pin = new FakePinModel(id, Id, label, direction, kind, type) { Shape = shape };
        _pins.Add(pin);
        return pin;
    }

    public void SetPosition(Vector2 pos) => Position = pos;
}
