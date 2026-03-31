using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Defaults;
using ModuleHost.Core.Abstractions;
using Raylib_cs;
using FdpStandardInteractionTool = FDP.Toolkit.Vis2D.Tools.StandardInteractionTool;

namespace Hrot.IG.Tools;

/// <summary>
/// IG-specific wrapper that wires the FDP
/// <see cref="FDP.Toolkit.Vis2D.Tools.StandardInteractionTool"/> into the
/// IG ECS world.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>
///     Delegates all <see cref="IMapTool"/> methods to the inner FDP tool so that
///     click / drag / hover / box-select mechanics are fully reused.
///   </item>
///   <item>
///     Subscribes to <see cref="FDP.Toolkit.Vis2D.Tools.StandardInteractionTool.OnEntitySelectRequest"/>
///     and <see cref="FDP.Toolkit.Vis2D.Tools.StandardInteractionTool.OnRegionSelected"/>
///     to synchronise both:
///     <list type="bullet">
///       <item>The in-memory <see cref="DefaultSelectionState"/> consumed by
///             <see cref="FDP.Toolkit.Vis2D.Layers.EntityRenderLayer"/>.</item>
///       <item>The ECS <see cref="SelectionState"/> components read by
///             <see cref="Hrot.IG.Systems.SelectionRenderSystem"/>.</item>
///     </list>
///   </item>
/// </list>
///
/// No allocations on the hot path (§CODE-STANDARDS §4).
/// </summary>
public class StandardInteractionTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => StandardInteractionToolConstants.ToolName;

    private readonly FdpStandardInteractionTool _inner;
    private readonly EntityRepository           _world;
    private readonly DefaultSelectionState      _selection;

    /// <summary>
    /// Passes through the inner FDP tool's world-click event so that
    /// IgApplication can subscribe without accessing the private <c>_inner</c> field.
    /// Fires with (worldPos, button, isShift, isCtrl, hitEntity).
    /// </summary>
    public event Action<Vector2, MouseButton, bool, bool, Entity>? OnWorldClick
    {
        add    => _inner.OnWorldClick += value;
        remove => _inner.OnWorldClick -= value;
    }

    /// <summary>
    /// Passes through the inner FDP tool's drag-end event so that
    /// IgApplication can send a single network position update on mouse-up.
    /// Fires with the dragged entity once the user releases the mouse button.
    /// </summary>
    public event Action<Entity>? OnEntityDragEnd
    {
        add    => _inner.OnEntityDragEnd += value;
        remove => _inner.OnEntityDragEnd -= value;
    }

    /// <summary>
    /// Passes through the inner FDP tool's per-frame entity-moved event.
    /// Fires every frame while an entity drag is in progress, carrying the
    /// current world-space cursor position. Subscribe to track the drop
    /// position for network updates in <see cref="OnEntityDragEnd"/>.
    /// </summary>
    public event Action<Entity, System.Numerics.Vector2>? OnEntityMoved
    {
        add    => _inner.OnEntityMoved += value;
        remove => _inner.OnEntityMoved -= value;
    }

    /// <summary>
    /// Constructs a wired selection tool.
    /// </summary>
    /// <param name="world">
    /// The live entity repository — must have <see cref="SelectionState"/> registered.
    /// </param>
    /// <param name="query">
    /// Entity query supplying the pickable entity set
    /// (<c>With&lt;NetworkIdentity, SimTransform&gt;</c> recommended).
    /// </param>
    /// <param name="adapter">
    /// Visualizer adapter used by the inner tool for hit-radius queries.
    /// </param>
    /// <param name="selection">
    /// Shared selection state consumed by the <see cref="FDP.Toolkit.Vis2D.Layers.EntityRenderLayer"/>.
    /// </param>
    public StandardInteractionTool(
        EntityRepository      world,
        EntityQuery           query,
        IVisualizerAdapter    adapter,
        DefaultSelectionState selection)
    {
        _world     = world;
        _selection = selection;

        _inner = new FdpStandardInteractionTool(world, query, adapter);
        _inner.OnEntitySelectRequest += HandleEntitySelectRequest;
        _inner.OnRegionSelected      += HandleRegionSelected;
    }

    // ── IMapTool — delegated ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public void OnEnter(MapCanvas canvas) => _inner.OnEnter(canvas);

    /// <inheritdoc/>
    public void OnExit() => _inner.OnExit();

    /// <inheritdoc/>
    public void Update(float dt) => _inner.Update(dt);

    /// <inheritdoc/>
    public void Draw(RenderContext ctx) => _inner.Draw(ctx);

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MouseButton button)
        => _inner.HandleClick(worldPos, button);

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        => _inner.HandleDrag(worldPos, delta);

    /// <inheritdoc/>
    public bool HandleHover(Vector2 worldPos)
        => _inner.HandleHover(worldPos);

    /// <inheritdoc/>
    public bool HandleKeyPressed(KeyboardKey key)
        => ((IMapTool)_inner).HandleKeyPressed(key);

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleEntitySelectRequest(Entity entity, bool augment)
    {
        if (!augment)
            ClearAllSelections();

        if (!_world.IsAlive(entity))
        {
            // Click on empty space without augment → deselect handled by ClearAllSelections above.
            return;
        }

        if (!augment)
        {
            // Single-select: this entity becomes the sole primary selection.
            _selection.PrimarySelected = entity; // also updates internal set
            ApplySelectionState(entity, isSelected: true, isPrimary: true);
        }
        else
        {
            // Multi-select (Shift/Ctrl): add to existing selection.
            _selection.AddSelection(entity);
            // If it is now the only selected item, it is primary; otherwise secondary.
            bool isPrimary = _selection.PrimarySelected == entity;
            ApplySelectionState(entity, isSelected: true, isPrimary: isPrimary);
        }
    }

    private void HandleRegionSelected(List<Entity> entities)
    {
        ClearAllSelections();

        bool first = true;
        foreach (var entity in entities)
        {
            if (!_world.IsAlive(entity))
                continue;

            _selection.AddSelection(entity);
            ApplySelectionState(entity, isSelected: true, isPrimary: first);
            first = false;
        }

        // Ensure PrimarySelected points to the first entity in the region.
        foreach (var entity in entities)
        {
            if (_world.IsAlive(entity))
            {
                _selection.PrimarySelected = entity;
                break;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears the <see cref="DefaultSelectionState"/> and resets all live entities'
    /// <see cref="SelectionState"/> components to deselected.
    /// </summary>
    private void ClearAllSelections()
    {
        // Snapshot the current set before clearing so we can update ECS state.
        // ToArray avoids modifying the collection while iterating.
        var toDeselect = new List<Entity>(_selection.SelectedEntities);
        _selection.ClearSelection();

        foreach (var entity in toDeselect)
        {
            if (_world.IsAlive(entity) && ((ISimulationView)_world).HasComponent<SelectionState>(entity))
                _world.SetComponent(entity, new SelectionState { IsSelected = false, IsPrimarySelection = false });
        }
    }

    /// <summary>
    /// Upserts a <see cref="SelectionState"/> onto <paramref name="entity"/>.
    /// Assumes <see cref="SelectionState"/> is already registered in <see cref="_world"/>.
    /// </summary>
    private void ApplySelectionState(Entity entity, bool isSelected, bool isPrimary)
    {
        _world.SetComponent(entity, new SelectionState
        {
            IsSelected        = isSelected,
            IsPrimarySelection = isPrimary,
        });
    }

    // ── Test hook (internal — accessible via InternalsVisibleTo) ─────────────

    /// <summary>
    /// Directly invokes the entity-selection handler as if the inner FDP tool had
    /// fired its <c>OnEntitySelectRequest</c> event.
    ///
    /// Intended for use in headless unit tests that cannot instantiate a real
    /// <see cref="MapCanvas"/> or Raylib input pipeline.
    /// </summary>
    internal void TestHook_SelectEntity(Entity entity, bool augment)
        => HandleEntitySelectRequest(entity, augment);
}
