using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3133</c> (R-233) — the BAKED NAVMESH: the outline of every polygon of the chosen layer (<see cref="NavmeshLayerSetting"/>:
/// Infantry — the default — Vehicle, or both), read from the <see cref="INavmeshProvider"/> world singleton through
/// <see cref="INavmeshDebugGeometry"/>; a doorway polygon in the colour of its door's state as this view sees it (the Doors layer's
/// colours). Has data = can draw: SimHost, Editor, Stride; CGF, IG and the Replay Browser bake none, so it draws nothing there.
/// Toggled by the <c>Navmesh</c> layer, off by default — it draws the whole layer (no backend gizmo knows the view).
/// 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5c.
/// </summary>
[GizmoProjector]
public sealed class NavmeshGizmo : IGlobalStatelessGizmo
{
    public static readonly Rgba32 InfantryColor = new(0, 190, 210, 170);
    public static readonly Rgba32 VehicleColor  = new(235, 140, 0, 170);

    /// <summary>Lift above the polygon so the outline is not z-fought by the ground.</summary>
    public const float Lift = 0.05f;

    private readonly GizmoSettingsRegistry? _settings;

    public NavmeshGizmo() { }
    public NavmeshGizmo(GizmoSettingsRegistry settings) => _settings = settings;

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingletonManaged<INavmeshProvider>()) return;
        if (repo.GetSingletonManaged<INavmeshProvider>() is not INavmeshDebugGeometry geometry) return;
        var choice = NavmeshLayerSetting.Of(_settings);
        var doors = DoorStates.Of(view);
        if (choice != NavmeshDrawLayers.Vehicle) Draw(geometry.DebugMesh(NavLayerMask.Infantry), InfantryColor, doors, draw);
        if (choice != NavmeshDrawLayers.Infantry) Draw(geometry.DebugMesh(NavLayerMask.Vehicle), VehicleColor, doors, draw);
    }

    private static void Draw(NavmeshDebugMesh? mesh, Rgba32 color, DoorStates? doors, IDebugDrawBuilder draw)
    {
        if (mesh == null) return;
        var lift = new Vector3(0f, 0f, Lift);
        for (int i = 0; i < mesh.PolyCount; i++)
        {
            int door = mesh.DoorIndex[i];
            var c = door < 0 ? color
                : DoorLeafGizmo.ColorOf(doors != null && door < doors.Count ? doors[door] : TerrainDoorState.Open);
            var poly = mesh.Polygon(i);
            for (int j = 0; j < poly.Length; j++)
                draw.DrawLine(poly[j] + lift, poly[(j + 1) % poly.Length] + lift, c, door < 0 ? 1f : 2f, SizeMode.ScreenPixels,
                    layer: DebugTraceLayers.Navmesh);
        }
    }
}
