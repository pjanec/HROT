using System;
using Hrot.Map.Common.Components;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.World;

namespace Hrot.Player.Systems;

/// <summary>
/// PostSimulation-phase system that evaluates camera-frustum visibility and
/// level-of-detail for every entity carrying <see cref="SimTransform"/>.
///
/// Each frame it writes or updates a <see cref="CullingState"/> component:
/// <list type="bullet">
///   <item>
///     <see cref="CullingState.IsVisible"/> is <c>true</c> when the entity's XY
///     position falls within the active camera viewport provided by
///     <see cref="MapCameraViewport"/>.
///   </item>
///   <item>
///     <see cref="CullingState.LodLevel"/> is derived from the current zoom using
///     the thresholds in <see cref="CullingStateConstants"/>:
///     below <see cref="CullingStateConstants.LodIconOnlyZoomThreshold"/> → LOD 2,
///     below <see cref="CullingStateConstants.LodSimplifiedZoomThreshold"/> → LOD 1,
///     otherwise LOD 0.
///   </item>
/// </list>
///
/// The <c>DebugGizmoLayer</c> reads <see cref="CullingState.IsVisible"/> to skip
/// all draw calls for off-screen entities, eliminating wasted GPU work.
///
/// Design constraints (§CODE-STANDARDS §4):
/// <list type="bullet">
///   <item>Zero allocations in <see cref="Execute"/>.</item>
///   <item>No LINQ — plain <c>foreach</c> over the entity query.</item>
///   <item>All thresholds and LOD levels via <see cref="CullingStateConstants"/> named constants (§CODE-STANDARDS §1).</item>
/// </list>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.EveryFrame)]
public class MapCullingSystem : IEcsModuleSystem
{
    private readonly MapCameraViewport _viewport;

    // ⭐ CE-1480 G4 (docs/DESIGN_Change_Gated_Systems.md) — the ONE ChangeGate, with this system's inputs: the entity's position (SimTransform, a
    //   required component of the query ⇒ a Delta is exact: only the hot chunks are re-tested) and the camera (the viewport's Version — the
    //   rectangle and the zoom ⇒ any move is a full pass). A state is written only when it differs from the one the entity holds.
    //   ⚠ D-5 (docs/DESIGN_Map_View_Filter.md §6: "CullingState fed from MapViewSnapshot") is NOT built: this keeps the rectangle test where it is;
    //   when D-5 lands it swaps the viewport's Version for the snapshot's — the gate does not change.
    private readonly ChangeGate _gate;
    private EntityQuery? _query;
    private ISimulationView? _queryView;

    // the pass's constants, read once (fields, not locals: the per-entity step is a method)
    private bool _viewportIsSet;
    private float _minX, _maxX, _minY, _maxY;
    private byte _lod;

    /// <summary>The gate (its counters) — for rails and diagnostics.</summary>
    public ChangeGate Gate => _gate;

    /// <summary>How many entity walks actually ran (the gate skipped the rest) — for rails and diagnostics.</summary>
    public int Passes { get; private set; }

    /// <param name="viewport">
    /// Application-owned object updated each frame before the kernel ticks.
    /// </param>
    public MapCullingSystem(MapCameraViewport viewport)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _gate = new ChangeGate().Watch(typeof(SimTransform)).WatchExternal(() => _viewport.Version);
    }

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var change = _gate.Next(view);
        if (change.Kind == ChangeKind.Unchanged) return;   // neither a position nor the camera moved ⇒ every CullingState is as it was
        Passes++;

        var cmd = view.GetCommandBuffer();

        // Capture viewport fields into locals — avoids repeated property reads in the hot loop.
        float minX = _viewport.WorldMinX;
        float maxX = _viewport.WorldMaxX;
        float minY = _viewport.WorldMinY;
        float maxY = _viewport.WorldMaxY;
        float zoom = _viewport.Zoom;

        // CE-131: an UNSET viewport is not a small viewport - it carries NO information, and culling
        // against it is not culling, it is blanking.
        //
        // MapCameraViewport's four bounds are auto-properties with no initializer, so they start at 0f:
        // a degenerate point at the origin, against which every entity tests OUT of view. They are filled
        // from the projected screen corners in IgApplication.Update - inside `if (!_headless)`. So on a
        // headless node, or before the first frame with a real camera, this system marked EVERY entity
        // invisible, every tick.
        //
        // This went unnoticed for a long time because IG's map was in fact drawn by SimHost's and CGF's
        // entity projectors, which ignored culling entirely. UXI-23 S2a merged those away, which is what
        // finally made it visible (docs/UX/UX_Feature_Map_Parity.md 3.9j.5b).
        //
        // Absence means VISIBLE - the same rule EntityPresentationGizmo and CullingStateVisibilityPolicy
        // already follow. A host with a real viewport culls exactly as before.
        bool viewportIsSet = maxX > minX && maxY > minY;

        // Resolve LOD once per frame — all entities share the same zoom level.
        byte lod = zoom < CullingStateConstants.LodIconOnlyZoomThreshold
            ? CullingStateConstants.LodIconOnly
            : zoom < CullingStateConstants.LodSimplifiedZoomThreshold
                ? CullingStateConstants.LodSimplified
                : CullingStateConstants.LodFull;

        _viewportIsSet = viewportIsSet; _minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY; _lod = lod;

        if (_query == null || !ReferenceEquals(_queryView, view))
        {
            _query = view.Query().With<SimTransform>().Build();
            _queryView = view;
        }
        var query = _query;

        if (change.Kind == ChangeKind.Delta && view is EntityRepository repo)
        {
            foreach (var entity in change.Entities(repo, query))
                Visit(view, cmd, entity);
        }
        else
        {
            foreach (var entity in query)
                Visit(view, cmd, entity);
        }
    }

    private void Visit(ISimulationView view, IEntityCommandBuffer cmd, Entity entity)
    {
        ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);
        float x = transform.Position.X;
        float y = transform.Position.Y;

        bool inView = !_viewportIsSet
                   || (x >= _minX && x <= _maxX && y >= _minY && y <= _maxY);

        // ⭐ write only on change — a CullingState that is already right is left alone (its chunk stays clean)
        if (view.HasComponent<CullingState>(entity))
        {
            ref readonly var current = ref view.GetComponentRO<CullingState>(entity);
            if (current.IsVisible == inView && current.LodLevel == _lod) return;
        }

        cmd.SetComponent(entity, new CullingState { IsVisible = inView, LodLevel = _lod });
    }
}
