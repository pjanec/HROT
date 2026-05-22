using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Mutable link model.</summary>
public sealed class FakeLinkModel : ILinkModel
{
    private readonly List<Vector2> _waypoints = new();

    public LinkId         Id       { get; }
    public PinId          FromPin  { get; }
    public PinId          ToPin    { get; }
    public LinkStyle      Style    { get; set; } = LinkStyle.Solid;
    public IReadOnlyList<Vector2> Waypoints => _waypoints;

    public FakeLinkModel(LinkId id, PinId from, PinId to)
    {
        Id      = id;
        FromPin = from;
        ToPin   = to;
    }

    public void AddWaypoint(Vector2 pos) => _waypoints.Add(pos);

    public void MoveWaypoint(int index, Vector2 pos)
    {
        if ((uint)index < (uint)_waypoints.Count) _waypoints[index] = pos;
    }

    public void RemoveWaypoint(int index)
    {
        if ((uint)index < (uint)_waypoints.Count) _waypoints.RemoveAt(index);
    }
}
