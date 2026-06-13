using System.Numerics;
using Hrot.Blueprints.Core.Assets;
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
    public IReadOnlyList<Vector2> Waypoints { get; }

    /// <param name="id">The stable derived link id.</param>
    /// <param name="fromPin">The source pin id.</param>
    /// <param name="toPin">The target pin id.</param>
    /// <param name="assetWaypoints">
    /// The asset-level waypoint list from <see cref="Link.Waypoints"/>, or <see langword="null"/> /
    /// empty for a straight wire (the default before any reroute is inserted).
    /// </param>
    public BlueprintLinkModel(LinkId id, PinId fromPin, PinId toPin,
        List<LinkWaypoint>? assetWaypoints = null)
    {
        Id      = id;
        FromPin = fromPin;
        ToPin   = toPin;

        if (assetWaypoints is { Count: > 0 })
        {
            var vec = new Vector2[assetWaypoints.Count];
            for (int i = 0; i < assetWaypoints.Count; i++)
                vec[i] = new Vector2(assetWaypoints[i].X, assetWaypoints[i].Y);
            Waypoints = vec;
        }
        else
        {
            Waypoints = Array.Empty<Vector2>();
        }
    }
}
