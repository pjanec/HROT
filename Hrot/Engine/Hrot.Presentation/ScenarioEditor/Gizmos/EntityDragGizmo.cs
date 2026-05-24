using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// Entity-stateful gizmo that handles spatial drag interactions for selectable map entities.
///
/// Lifecycle:
///   OnInteractionStarted -- user pressed on the entity's pick sphere; record initial position.
///   OnDragUpdate         -- user is dragging; write live position to SimTransform so the
///                           entity moves in real time. Also resets VehicleState.Speed to 0
///                           if present (entity is stationary while being dragged).
///   OnCommit             -- drag released; write final position, fire OnDragCommitted callback.
///   OnCancel             -- restore the original position recorded at OnInteractionStarted.
///   UpdateAndDraw        -- emits the entity pick sphere (makes the entity selectable via
///                           DebugGizmoLayer) and, while dragging, a preview line from the
///                           original position to the cursor.
///
/// Replaces <c>Hrot.ScenarioEditor.Tools.EntityDragTool</c> and
/// <c>Fdp.Toolkit.Vis2D.Tools.EntityDragTool</c> (Phase 5 eradication).
/// </summary>
public sealed class EntityDragGizmo : IEntityStatefulGizmo
{
    // Sphere pick radius in world metres. Must be large enough to be
    // easily clickable but not so large it overlaps adjacent entities.
    private const float PickRadius = 8f;

    private static readonly Rgba32 PickSphereColor  = new Rgba32(0, 0, 0, 0);   // transparent
    private static readonly Rgba32 DragLineColor    = new Rgba32(255, 255, 0, 200);
    private static readonly Rgba32 DragSphereColor  = new Rgba32(255, 200, 0, 180);

    private readonly ISimulationView _view;
    private readonly Entity          _entity;

    private bool    _isDragging;
    private Vector3 _originalPos;
    private Vector3 _currentDragPos;
    private Vector3 _dragOffset;

    /// <summary>
    /// Optional callback fired after a successful drag commit.
    /// Receives the entity and its final world position.
    /// Subscribe to trigger network update (e.g. SendGeoSpatialUpdate).
    /// </summary>
    public Action<Entity, Vector2>? OnDragCommitted;

    public bool RequiresExclusiveFocus => false;
    public bool IsFocused { get; private set; }

    public EntityDragGizmo(ISimulationView view, Entity entity)
    {
        _view   = view;
        _entity = entity;
    }

    public void Dispose() { }

    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    // -- UpdateAndDraw ---------------------------------------------------------

    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        if (!view.IsAlive(_entity)) return;
        if (!view.HasComponent<SimTransform>(_entity)) return;

        ref readonly var tf = ref view.GetComponentRO<SimTransform>(_entity);
        var worldPos = new Vector3(tf.Position.X, tf.Position.Y, 0f);

        long networkId = 0;
        if (view.HasComponent<NetworkIdentity>(_entity))
            networkId = view.GetComponentRO<NetworkIdentity>(_entity).Value;

        // Emit transparent Box2D so DebugGizmoLayer can hit-test this entity.
        var pickBox = default(DebugPrimitive);
        pickBox.Shape            = DebugPrimitiveShape.Box2D;
        pickBox.Space            = CoordinateSpace.World;
        pickBox.TargetView       = PipelineTarget.Map2D;
        pickBox.BoxCenterX       = tf.Position.X;
        pickBox.BoxCenterY       = tf.Position.Y;
        pickBox.BoxExtentX       = PickRadius;
        pickBox.BoxExtentY       = PickRadius;
        pickBox.Color            = PickSphereColor;
        pickBox.AnchorIndex      = _entity.Index;
        pickBox.AnchorGeneration = (ushort)_entity.Generation;
        pickBox.BoxAnchorId      = networkId;
        draw.EmitRaw(in pickBox);

        // While dragging: show a yellow preview line from original to current position.
        if (_isDragging)
        {
            draw.DrawLine(_originalPos, _currentDragPos, DragLineColor, thickness: 2f);
            draw.DrawSphere(_currentDragPos, 5f, DragSphereColor);
        }
    }

    // -- IGizmoInteractionHandler ---------------------------------------------

    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
    {
        if (!_view.IsAlive(_entity) || !_view.HasComponent<SimTransform>(_entity)) return;
        ref readonly var tf = ref _view.GetComponentRO<SimTransform>(_entity);
        _originalPos    = new Vector3(tf.Position.X, tf.Position.Y, 0f);
        _dragOffset     = _originalPos - worldPos;
        _currentDragPos = _originalPos;
        _isDragging     = false;   // drag starts only when OnDragUpdate fires
    }

    public void OnDragUpdate(Vector3 worldPos)
    {
        if (!_view.IsAlive(_entity)) return;
        _isDragging     = true;
        _currentDragPos = worldPos + _dragOffset;
        ApplyPosition(_currentDragPos);
    }

    public void OnCommit(Vector3 worldPos)
    {
        if (!_view.IsAlive(_entity)) return;

        if (_isDragging)
        {
            ApplyPosition(_currentDragPos);
            OnDragCommitted?.Invoke(_entity, new Vector2(_currentDragPos.X, _currentDragPos.Y));
        }

        _isDragging = false;
    }

    public void OnCancel()
    {
        if (!_view.IsAlive(_entity)) return;
        _isDragging = false;
        ApplyPosition(_originalPos);
    }

    public void OnMenuAction(int actionId) { }
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }

    // -- Helpers ---------------------------------------------------------------

    private void ApplyPosition(Vector3 worldPos)
    {
        if (_view is not EntityRepository repo) return;
        if (!repo.IsAlive(_entity) || !repo.HasComponent<SimTransform>(_entity)) return;

        ref var tf = ref repo.GetComponentRW<SimTransform>(_entity);
        tf.Position = new Vector3(worldPos.X, worldPos.Y, tf.Position.Z);

        // Reset speed to 0 while the entity is being repositioned manually.
        if (repo.HasComponent<VehicleState>(_entity))
        {
            ref var vs = ref repo.GetComponentRW<VehicleState>(_entity);
            vs.Speed = 0;
        }
    }
}

/// <summary>Wires EntityDragGizmo into the GizmoRegistry.</summary>
public sealed class EntityDragGizmoDefinition : IGizmoDefinition
{
    private readonly Action<Entity, Vector2>? _onDragCommitted;

    public Type[] RequiredComponents { get; } =
    {
        typeof(NetworkIdentity),
        typeof(SimTransform),
    };

    public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

    // FNV-1a hash of the fully-qualified type name — used as composite routing key.
    public uint GizmoTypeId { get; } = Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry.ComputeHash(typeof(EntityDragGizmoDefinition).FullName!);

    public EntityDragGizmoDefinition(Action<Entity, Vector2>? onDragCommitted = null)
    {
        _onDragCommitted = onDragCommitted;
    }

    public IEntityStatefulGizmo CreateInstance(ISimulationView view, Entity entity)
    {
        var gizmo = new EntityDragGizmo(view, entity);
        if (_onDragCommitted != null)
            gizmo.OnDragCommitted += _onDragCommitted;
        return gizmo;
    }
}
