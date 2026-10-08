using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Terrain;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3117</c> — the <b>doors</b> debug layer (📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a): each door's leaf as a short line
/// from its hinge — along the wall when closed or locked, swung 90° when open — coloured by its live state (grey closed, green open,
/// purple locked, red ✕ destroyed). The leaf geometry is the terrain's (<see cref="TerrainWorld.DoorLeaves"/>, index-aligned with
/// <see cref="TerrainWorld.Doors"/>); the state is <see cref="DoorStates.Of(ISimulationView, TerrainWorld)"/>, i.e. the recorded
/// <c>DoorState</c> of each door entity, so replay shows it too wherever the terrain is loaded (the Replay Browser: <c>CE-3118</c>).
/// Toggled by the <c>Doors</c> bit of the layer control.
/// </summary>
[GizmoProjector]
public sealed class DoorLeafGizmo : IGlobalStatelessGizmo
{
    private static readonly Rgba32 ClosedColor    = new(110, 110, 110, 255);
    private static readonly Rgba32 OpenColor      = new(40, 180, 60, 255);
    private static readonly Rgba32 LockedColor    = new(150, 60, 200, 255);
    private static readonly Rgba32 DestroyedColor = new(220, 40, 40, 255);

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingletonManaged<TerrainWorld>()) return;
        var world = repo.GetSingletonManaged<TerrainWorld>();
        if (world == null || world.Doors.Count == 0) return;
        var states = DoorStates.Of(repo, world);
        var leaves = world.DoorLeaves;
        for (int i = 0; i < world.Doors.Count && i < leaves.Count; i++)
        {
            var fp = leaves[i].Footprint;
            if (fp.Length < 4) continue;
            var (hinge, closedTip) = Leaf(fp);
            float z = leaves[i].BaseZ;
            var h3 = new Vector3(hinge, z);
            switch (states[i])
            {
                case TerrainDoorState.Open:
                    draw.DrawLine(h3, new Vector3(OpenTip(hinge, closedTip), z), OpenColor, 3f, layer: DebugTraceLayers.Doors);
                    break;
                case TerrainDoorState.Locked:
                    draw.DrawLine(h3, new Vector3(closedTip, z), LockedColor, 3f, layer: DebugTraceLayers.Doors);
                    break;
                case TerrainDoorState.Destroyed:
                    var mid = new Vector3((hinge + closedTip) * 0.5f, z);
                    draw.DrawLine(mid + new Vector3(-0.4f, -0.4f, 0), mid + new Vector3(0.4f, 0.4f, 0), DestroyedColor, 2.5f, layer: DebugTraceLayers.Doors);
                    draw.DrawLine(mid + new Vector3(-0.4f, 0.4f, 0), mid + new Vector3(0.4f, -0.4f, 0), DestroyedColor, 2.5f, layer: DebugTraceLayers.Doors);
                    break;
                default:
                    draw.DrawLine(h3, new Vector3(closedTip, z), ClosedColor, 3f, layer: DebugTraceLayers.Doors);
                    break;
            }
        }
    }

    /// <summary>The hinge and the closed tip of a leaf footprint built as <c>{ p0−n, p1−n, p1+n, p0+n }</c> (<c>BuildDoorLeaves</c>).</summary>
    public static (Vector2 Hinge, Vector2 ClosedTip) Leaf(Vector2[] fp) => ((fp[0] + fp[3]) * 0.5f, (fp[1] + fp[2]) * 0.5f);

    /// <summary>The tip of the leaf swung 90° on its hinge (to the footprint's +n side).</summary>
    public static Vector2 OpenTip(Vector2 hinge, Vector2 closedTip)
    {
        var d = closedTip - hinge;
        return hinge + new Vector2(-d.Y, d.X);
    }
}
