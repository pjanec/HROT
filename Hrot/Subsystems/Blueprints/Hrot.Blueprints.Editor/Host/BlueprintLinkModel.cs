using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Read-only <see cref="ILinkModel"/> adapter projecting a <see cref="Hrot.Blueprints.Core.Assets.Link"/>
/// onto the NodeEdit canvas contract.
/// </summary>
internal sealed class BlueprintLinkModel : ILinkModel
{
    public LinkId Id      { get; }
    public PinId  FromPin { get; }
    public PinId  ToPin   { get; }
    public LinkStyle Style { get; } = LinkStyle.Solid;
    public IReadOnlyList<Vector2> Waypoints { get; } = Array.Empty<Vector2>();

    public BlueprintLinkModel(LinkId id, PinId fromPin, PinId toPin)
    {
        Id      = id;
        FromPin = fromPin;
        ToPin   = toPin;
    }
}
