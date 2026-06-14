# BATCH-S2-O — Click-to-select + click-to-move in the Stride 3D view (+ destination marker)

## Goal (UX approved by user)
In the Stride 3D window:
- **Left-click an entity** → select it (drives the existing cyan selection box; syncs with the
  inspector panel since they share `EditorStrideSubsystem.SelectionState`).
- **Right-click the ground** → issue a move order to the SELECTED entity to that point, and show a
  destination marker there.
- Ground target via **physics raycast** (option a). Marker drawn at the clicked point.

This also fixes the "missing selection box": the box code is intact but nothing was SELECTING in
the 3D view — left-click-select now drives it.

## Verified facts (use these; don't re-derive)
- `EditorStrideSubsystem.SelectionState` (`EditorSelectionState`) — shared selection; `.Select(Entity)`,
  `.SelectedEntity`, `.HasSelection`. Writing it updates the box + inspector.
- `IStrideRaycastService.Raycast(Vector3 fromFdp, Vector3 toFdp, int groups=-1, int filter=-1)` →
  `StrideRaycastHit` with `.HasHit`, `.HitEntity` (FDP Entity, Entity.Null for static geometry),
  `.Point` (FDP hit position). Concrete `StrideRaycastService(Stride.Physics.Simulation)`.
- `FdpStrideTransform.ScreenRayToFdp(CameraComponent cam, System.Numerics.Vector2 screenPx)` →
  `FdpRay { Origin, Direction }` (FDP). **screenPx must be normalized [0,1].** Stride's
  `Input.MousePosition` IS already normalized [0,1] — pass it directly.
- `Input.IsMouseButtonPressed(MouseButton.Left/Right)` and `Input.MousePosition` available in
  `StrideHrotGame.Update` (`using Stride.Input;` present).
- `_cameraEntity` (StrideHrotGame field, set at boot); `_cameraEntity.Get<CameraComponent>()`.
- `Stride.Physics.Simulation` available at boot: `SceneSystem.SceneInstance.GetProcessor<PhysicsProcessor>().Simulation` (StrideHrotGame.cs ~677).
- `EditorStrideSubsystem.ProducerBuffer.EmitRaw(DebugPrimitive.MakeLine(from, to, color, sizeMode, target))`
  + `.Space = CoordinateSpace.World; .LifetimeSeconds = ...` — proven by `EmitSelectionLine`
  (EditorStrideSubsystem.cs ~1404-1413). Reuse `MakeLine` for the marker (a small 3-axis cross).
- Move routing: `world.HasComponent<VehicleState>(entity)` → vehicle, else character.
  Vehicle: set `NavigationIntent { Mode=NavigationMode.DirectPoint, FinalDestination=targetFdp,
  TargetSpeed, ArrivalRadius, IntentId=prev+1 }`. Character: `FdpNavigationOrders.IssueMoveTo(world,
  entity, targetFdp, speed, arrivalRadius, NavLayerMask.Infantry)`.

## Scope — TWO FILES

### File 1: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` — destination marker
Add a move-destination marker that renders for a few seconds, emitted alongside the selection
highlight (so timing into ProducerBuffer is correct).

1. Add fields near the selection-highlight constants (~line 1340):
```csharp
// BATCH-S2-O: click-to-move destination marker (FDP world position + remaining lifetime).
private System.Numerics.Vector3? _moveMarkerFdp;
private float _moveMarkerSecondsRemaining;
private const float MoveMarkerTotalSeconds = 3.0f;
private static readonly Stride.Core.Mathematics.Color MoveMarkerColor = new(255, 215, 0, 255); // amber
private const float MoveMarkerHalfSizeM = 0.6f;
```

2. Public setter (call from StrideHrotGame on right-click):
```csharp
/// <summary>Show a destination marker at the given FDP world position for a few seconds (BATCH-S2-O).</summary>
public void ShowMoveMarker(System.Numerics.Vector3 fdpPos)
{
    _moveMarkerFdp = fdpPos;
    _moveMarkerSecondsRemaining = MoveMarkerTotalSeconds;
}
```

3. A per-frame emit method that draws a 3-axis cross at the marker and decays the timer. Model it on
   `EmitSelectionLine`/`EmitSelectionHighlight` (same DebugPrimitive.MakeLine + World space + short
   per-frame LifetimeSeconds). Decrement `_moveMarkerSecondsRemaining` by the frame dt; clear when ≤0.
   The marker position is FDP (the gizmo buffer is World/FDP space, same as the selection box which
   reads SimTransform.Position directly — so pass the FDP position as-is). Emit three crossing lines
   (±X, ±Y, ±Z about the point, each `MoveMarkerHalfSizeM`).
```csharp
private void EmitMoveMarker(float dt)
{
    if (_moveMarkerFdp is not { } c) return;
    _moveMarkerSecondsRemaining -= dt;
    if (_moveMarkerSecondsRemaining <= 0f) { _moveMarkerFdp = null; return; }
    float h = MoveMarkerHalfSizeM;
    void Seg(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
    {
        var line = Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeLine(
            a, b, MoveMarkerColor,
            sizeMode: Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.WorldMeters,
            target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All);
        line.Space = Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace.World;
        line.LifetimeSeconds = 0.05f;
        ProducerBuffer.EmitRaw(line);
    }
    Seg(new(c.X - h, c.Y, c.Z), new(c.X + h, c.Y, c.Z));
    Seg(new(c.X, c.Y - h, c.Z), new(c.X, c.Y + h, c.Z));
    Seg(new(c.X, c.Y, c.Z - h), new(c.X, c.Y, c.Z + h));
}
```
   IMPORTANT: match the exact `MakeLine` argument names/types and `DebugPrimitive`/`SizeMode`/
   `PipelineTarget`/`CoordinateSpace` enum values used by the existing `EmitSelectionLine` — copy its
   pattern verbatim (adjust only color/coords). If `MakeLine`'s first two args are a different vector
   type (Stride vs System.Numerics), convert to match what `EmitSelectionLine` passes.

4. Call `EmitMoveMarker(dt)` from BOTH places that currently call `EmitSelectionHighlight()` (the OFF
   `Tick` ~line 1197-1198 and the hosted `TickHosted` ~line 1267-1270), passing the same `dt` those
   ticks use. (If those tick methods don't have `dt` in scope, use the dt parameter they receive.)

### File 2: `Stride/HrotStrideApp.Game/StrideHrotGame.cs` — picking + click handling
1. Add a field: `private IStrideRaycastService? _raycastService;`
   (add `using Hrot.Stride.Core;` if needed for the type.)

2. At boot where the PhysicsProcessor.Simulation is obtained (~line 680, inside
   `if (physicsProcessor?.Simulation != null)`), construct the raycast service:
   `_raycastService = new StrideRaycastService(physicsProcessor.Simulation);`

3. In `Update`, in the existing `if (_editorSubsystem != null && _cameraEntity != null)` block
   (~line 407), add click handling AFTER the existing center logic. Use the verified APIs:
```csharp
// BATCH-S2-O: 3D click-to-select (LMB) and click-to-move (RMB).
if (_raycastService != null)
{
    var cam = _cameraEntity.Get<CameraComponent>();
    var world = _editorSubsystem.World;
    if (cam != null && world != null)
    {
        bool lmb = Input.IsMouseButtonPressed(MouseButton.Left);
        bool rmb = Input.IsMouseButtonPressed(MouseButton.Right);
        if (lmb || rmb)
        {
            var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition); // MousePosition is [0,1]
            var hit = _raycastService.Raycast(ray.Origin, ray.Origin + ray.Direction * 1000f);
            if (lmb)
            {
                // Select the hit entity (ignore static-geometry/no-entity hits).
                if (hit.HasHit && hit.HitEntity != Fdp.Core.Entity.Null && world.IsAlive(hit.HitEntity))
                    _editorSubsystem.SelectionState.Select(hit.HitEntity);
            }
            else if (rmb && hit.HasHit) // RMB: move the SELECTED entity to the hit point
            {
                var sel = _editorSubsystem.SelectionState;
                if (sel.HasSelection && world.IsAlive(sel.SelectedEntity))
                {
                    IssueMoveOrder(world, sel.SelectedEntity, hit.Point);
                    _editorSubsystem.ShowMoveMarker(hit.Point);
                    Log.Info("[StrideHrotGame] Move order: entity #{0} → FDP ({1:F2},{2:F2},{3:F2}).",
                        sel.SelectedEntity.Index, hit.Point.X, hit.Point.Y, hit.Point.Z);
                }
            }
        }
    }
}
```

4. Add the `IssueMoveOrder` helper (private method on StrideHrotGame). Vehicle vs character routing:
```csharp
private static void IssueMoveOrder(EntityRepository world, Fdp.Core.Entity entity, System.Numerics.Vector3 targetFdp)
{
    const float Speed = 5f;
    const float ArrivalRadius = 2f;
    bool isVehicle = world.IsComponentTypeRegistered<VehicleState>() && world.HasComponent<VehicleState>(entity);
    if (isVehicle)
    {
        if (!world.IsComponentTypeRegistered<NavigationIntent>()) return;
        var intent = world.HasComponent<NavigationIntent>(entity)
            ? world.GetComponent<NavigationIntent>(entity) : default;
        intent.Mode = NavigationMode.DirectPoint;
        intent.FinalDestination = targetFdp;
        intent.TargetSpeed = Speed;
        intent.ArrivalRadius = ArrivalRadius;
        intent.IntentId = intent.IntentId + 1;
        intent.ReverseAllowed = 0;
        if (world.HasComponent<NavigationIntent>(entity)) world.SetComponent(entity, intent);
        else world.AddComponent(entity, intent);
        // ensure a NavigationStatus exists so the muscle can report progress
        if (world.IsComponentTypeRegistered<NavigationStatus>() && !world.HasComponent<NavigationStatus>(entity))
            world.AddComponent(entity, new NavigationStatus { Result = NavigationResult.InProgress });
    }
    else
    {
        FdpNavigationOrders.IssueMoveTo(world, entity, targetFdp, Speed, ArrivalRadius, NavLayerMask.Infantry);
    }
}
```
   Add required `using`s: `Fdp.Core` (VehicleState, EntityRepository, Entity), `Fdp.Toolkit.Navigation`
   (NavigationIntent, NavigationMode, NavigationStatus, NavigationResult, NavLayerMask),
   `Hrot.Stride.Core` (FdpNavigationOrders, IStrideRaycastService, StrideRaycastService,
   FdpStrideTransform). Verify exact names/namespaces against the source before using; mirror how
   `StrideSelfTest.cs` sets NavigationIntent (Mode/FinalDestination/IntentId) and how
   `FdpNavigationOrders.IssueMoveTo` is declared.

## Constraints
- Two files only. Don't change the selection-box code, the swizzle, physics, or time-control.
- LMB on empty ground / static geometry must NOT clear an existing selection (only select on entity hit).
- Defensive: null-check `_raycastService`, `cam`, `world`; guard `IsAlive`.

## Acceptance (USER verifies interactively — harness cannot click)
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Left-click a unit in the 3D view → cyan selection box appears; inspector shows it selected.
- (User) Right-click ground with a unit selected → unit drives toward the point; amber cross marker
  shows at the destination for ~3 s.
- Selecting in 3D is reflected in the inspector (shared SelectionState) and vice-versa.
