# BATCH-S2-W — Mannequin walks (add CrowdMotorIntent) + 3D drag-and-drop authoring

Two changes, both in `Stride/HrotStrideApp.Game/StrideHrotGame.cs`.

## Part 1 — Mannequin doesn't move (missing CrowdMotorIntent)
Root cause: `NavigationIntentBridgeSystem` crowd-registers the infantry but does NOT add the
`CrowdMotorIntent` component. `CrowdAgentUpdateSystem` and `BulletCharacterMotor` both query
`With<CrowdMotorIntent>()`, so without it the mannequin gets no velocity. The working F5/F6 harness
cases add `CrowdMotorIntent` explicitly before issuing the order.

Fix: in `IssueMoveOrder`, in the CHARACTER branch, ensure the entity has a `CrowdMotorIntent`
component before/around `IssueMoveTo` (mirroring F5/F6). Add right after the VehicleState strip:
```csharp
// The crowd pipeline (CrowdAgentUpdateSystem) and BulletCharacterMotor both query With<CrowdMotorIntent>;
// NavigationIntentBridgeSystem registers the agent but does NOT add this component, so add it here
// (mirrors the working F5 'Navmesh Walk' / F6 'FDP Move Order char' harness cases). (BATCH-S2-W)
if (world.IsComponentTypeRegistered<CrowdMotorIntent>() && !world.HasComponent<CrowdMotorIntent>(entity))
    world.AddComponent(entity, new CrowdMotorIntent());
```
`CrowdMotorIntent` is in `CarKinem.Core` (already `using`'d for VehicleState from BATCH-S2-T) — verify
the exact namespace by how `KinematicVehicleMotor.cs` / `BulletCharacterMotor.cs` reference
`CrowdMotorIntent`, and match it. (If it's `Fdp.Toolkit.Navigation`, use that.)

## Part 2 — 3D drag-and-drop (reposition by dragging in the 3D view)
Let the operator grab a unit in the 3D view with the LEFT mouse and drag it along the ground; on
release it stays. This reuses the existing external-reposition path (writing SimTransform →
SyncBodyToExternalPose teleports the body to follow).

Restructure the LMB handling (currently select-on-press) into select + drag:

1. Add fields (near the RMB drag fields):
```csharp
// BATCH-S2-W: 3D drag-and-drop (LMB press selects + arms; LMB drag repositions along the ground).
private Fdp.Core.Entity? _dragEntity;
private bool  _dragging;
private float _lmbDragAccum;
private const float DragStartDeadzone = 0.01f; // accumulated normalized delta before a drag starts
```

2. Replace the existing `if (lmb) { ... select ... }` block with press/held/release handling. Compute
   `lmbDown`/`lmbHeld`/`lmbUp` alongside the RMB booleans:
```csharp
bool lmb     = Input.IsMouseButtonPressed(MouseButton.Left);
bool lmbHeld = Input.IsMouseButtonDown(MouseButton.Left);
bool lmbUp   = Input.IsMouseButtonReleased(MouseButton.Left);
```
   (Keep the existing RMB `rmbDown`/`rmbUp` + accumulator logic from BATCH-S2-U unchanged.)

   LMB-press: select the hit entity and arm a potential drag:
```csharp
if (lmb)
{
    var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition);
    var hit = _raycastService.Raycast(ray.Origin, ray.Origin + ray.Direction * 1000f);
    Log.Info("[ClickDiag] LMB mouse=({0:F3},{1:F3}) hasHit={2} hitEntity=#{3} point=({4:F2},{5:F2},{6:F2})",
        Input.MousePosition.X, Input.MousePosition.Y, hit.HasHit,
        (hit.HitEntity == Fdp.Core.Entity.Null ? -1 : hit.HitEntity.Index),
        hit.PointFdp.X, hit.PointFdp.Y, hit.PointFdp.Z);
    if (hit.HasHit && hit.HitEntity != Fdp.Core.Entity.Null && world.IsAlive(hit.HitEntity))
    {
        _editorSubsystem.SelectionState.Select(hit.HitEntity);
        _dragEntity = hit.HitEntity;   // arm a possible drag of this entity
        _lmbDragAccum = 0f;
        _dragging = false;
        Log.Info("[ClickDiag] LMB selected entity #{0}", hit.HitEntity.Index);
    }
    else { _dragEntity = null; _dragging = false; }
}
```

   LMB-held: once movement exceeds the deadzone, drag the entity along the FDP ground plane (Z=0):
```csharp
if (_dragEntity is { } dragE && lmbHeld)
{
    _lmbDragAccum += Input.MouseDelta.Length();
    if (_lmbDragAccum > DragStartDeadzone) _dragging = true;
    if (_dragging && world.IsAlive(dragE) && world.HasComponent<SimTransform>(dragE))
    {
        var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition);
        // Intersect the FDP ground plane Z=0 (FDP Z is up). Avoids self-hit of the dragged body
        // and is stable for the flat arena floor.
        if (MathF.Abs(ray.Direction.Z) > 1e-4f)
        {
            float t = -ray.Origin.Z / ray.Direction.Z;
            if (t > 0f)
            {
                var p = ray.Origin + ray.Direction * t;
                var cur = world.GetComponent<SimTransform>(dragE);
                world.SetComponent(dragE, new SimTransform
                {
                    Position = new System.Numerics.Vector3(p.X, p.Y, cur.Position.Z), // keep authored height
                    Rotation = cur.Rotation,
                });
            }
        }
    }
}
```

   LMB-release: finalize:
```csharp
if (lmbUp)
{
    if (_dragging && _dragEntity is { } droppedE)
        Log.Info("[StrideHrotGame] Drag-drop entity #{0} released.", droppedE.Index);
    _dragEntity = null;
    _dragging = false;
    _lmbDragAccum = 0f;
}
```

   Keep the RMB move block exactly as-is (BATCH-S2-U). Keep the `if (_raycastService != null) { cam/world
   guards }` wrapper.

## Constraints
- One file (`StrideHrotGame.cs`). Don't change the RMB logic, selection sync, IssueMoveOrder vehicle
  path, raycast service, or marker.
- A plain LMB click (no movement past the deadzone) must still just SELECT (no reposition).
- `Input.IsMouseButtonDown`, `Input.MouseDelta` exist (Stride.Input). `SimTransform` is `Fdp.Core`
  (add `using` only if needed; the harness uses it). Verify `CrowdMotorIntent` namespace.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User, time RUNNING) Select the mannequin → clean RMB-click on the arena floor → it WALKS to the
  point (crowd now has CrowdMotorIntent → CrowdAgentUpdateSystem writes velocity → BulletCharacterMotor
  drives the capsule).
- (User) LEFT-press a unit and DRAG → it follows the cursor along the ground; release → it stays
  (works for both vehicle and mannequin via the existing reposition). A plain LMB click still only
  selects.
