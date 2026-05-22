namespace NodeEditor.Primitives;

/// <summary>
/// Virtual identifier referring to a single reroute waypoint inside a link's
/// waypoint list. Reroutes are not standalone entities; they're nested
/// inside <c>ILinkModel.Waypoints</c>. This struct is used by selection
/// and command APIs.
/// </summary>
public readonly record struct RerouteRef(LinkId LinkId, int WaypointIndex)
{
    public override string ToString() => $"Reroute({LinkId}, #{WaypointIndex})";
}
