using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3117</c> — the <b>paths</b> debug layer (📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a): a mover's planned path from its
/// RECORDED <see cref="PathTrace"/> — the polyline, every door step as a square, the progress point at
/// <see cref="NavState.ProgressS"/> (by distance) and the look-ahead point the controller steers at,
/// <c>ProgressS + CarKinematicsSystem.PathLookahead(params, speed)</c> — the same function, so the two cannot disagree. A per-entity
/// gizmo of the <c>Path</c> family (⭐ <c>CE-3120</c>): by default it draws for the selected and the pinned movers, switchable to every
/// mover in the layer panel (§5b). Toggled by the <c>Paths</c> bit of the layer control.
/// <para>⚠ The look-ahead is sampled on the polyline; a spline trajectory bends between its points, so on one the dot can sit a
/// little off the curve the controller samples. Pedestrian and navmesh paths are linear.</para>
/// </summary>
[GizmoProjector(typeof(PathTrace), typeof(NavState), Family = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path)]
public sealed class PlannedPathGizmo : IStatelessGizmo
{
    private static readonly Rgba32 PathColor      = new(40, 120, 230, 220);
    private static readonly Rgba32 DoorColor      = new(150, 60, 200, 255);
    private static readonly Rgba32 ProgressColor  = new(240, 140, 20, 255);
    private static readonly Rgba32 LookaheadColor = new(220, 40, 220, 255);

    /// <summary>The <c>TraversalKind</c> of a door step.</summary>
    public const byte DoorTraversal = (byte)Fdp.Toolkit.Navigation.TraversalKind.Door;

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        var trace = view.GetComponentRO<PathTrace>(entity);
        var pts = trace.PointsRO();
        if (trace.TrajectoryId < 0 || pts.Length < 2) return;

        for (int i = 1; i < pts.Length; i++)
            draw.DrawLine(pts[i - 1].Position, pts[i].Position, PathColor, 2f, layer: DebugTraceLayers.Paths);
        foreach (ref readonly var p in pts)
            if (p.Traversal == DoorTraversal)
                draw.DrawSphere(p.Position, 0.35f, DoorColor, thickness: 2f, layer: DebugTraceLayers.Paths);

        ref readonly var nav = ref view.GetComponentRO<NavState>(entity);
        var at = trace.Sample(nav.ProgressS);
        draw.DrawSphere(at, 0.3f, ProgressColor, layer: DebugTraceLayers.Paths, fillColor: ProgressColor);
        if (view.HasComponent<VehicleParams>(entity))
        {
            float speed = view.HasComponent<VehicleState>(entity) ? view.GetComponentRO<VehicleState>(entity).Speed : 0f;
            float ahead = CarKinematicsSystem.PathLookahead(view.GetComponentRO<VehicleParams>(entity), speed);
            var look = trace.Sample(nav.ProgressS + ahead);
            draw.DrawLine(at, look, LookaheadColor, 1f, layer: DebugTraceLayers.Paths, style: LineStyle.Dashed);
            draw.DrawSphere(look, 0.3f, LookaheadColor, layer: DebugTraceLayers.Paths, fillColor: LookaheadColor);
        }
    }
}
