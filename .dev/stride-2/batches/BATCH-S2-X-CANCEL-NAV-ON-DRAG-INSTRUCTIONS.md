# BATCH-S2-X — Cancel the active nav order when an entity is dragged in 3D

## Problem
3D drag-and-drop only rewrites SimTransform (position); it does NOT cancel the unit's active move
order. So on drop the unit resumes its old target:
- A vehicle that had a DirectPoint order drives back to the old FinalDestination (NavigationIntent
  + VehicleState still active).
- A mannequin keeps steering to its old crowd target (crowd agent still registered).

## Fix
When a drag STARTS (the frame dragging begins), cancel the dragged entity's navigation:
- VEHICLE: NavigationIntent.Mode = None + IntentId++ (VehicleNavigationIntentSystem drops the route)
  AND zero VehicleState.Speed/SteerAngle (the route-drop does NOT zero it, so KinematicVehicleMotor
  would keep driving otherwise).
- CHARACTER: UnregisterAgent on the DotRecast crowd + zero CrowdMotorIntent.Velocity (so
  CrowdAgentUpdateSystem/BulletCharacterMotor stop commanding velocity).

## Scope — TWO FILES

### File 1: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` — public CancelMove
Add a public method (near the other public accessors / SelectionState). It has `World` and
`InfantryCrowdProvider` (public `DotRecastDtCrowdProvider?`, line ~171).
```csharp
/// <summary>
/// Cancels any active navigation/move order on <paramref name="entity"/> so it stops where it is
/// (used when the operator drags the entity in 3D — BATCH-S2-X). Handles both vehicle
/// (NavigationIntent + VehicleState) and character (DotRecast crowd + CrowdMotorIntent) drives.
/// </summary>
public void CancelMove(Fdp.Core.Entity entity)
{
    if (World == null || !World.IsAlive(entity)) return;

    // Vehicle: stop DirectPoint steering and zero the commanded VehicleState.
    if (World.IsComponentTypeRegistered<NavigationIntent>() && World.HasComponent<NavigationIntent>(entity))
    {
        var intent = World.GetComponent<NavigationIntent>(entity);
        intent.Mode     = NavigationMode.None;       // VehicleNavigationIntentSystem drops the route
        intent.IntentId = intent.IntentId + 1;       // mark as a new (idle) command
        World.SetComponent(entity, intent);
    }
    if (World.IsComponentTypeRegistered<VehicleState>() && World.HasComponent<VehicleState>(entity))
    {
        var vs = World.GetComponent<VehicleState>(entity);
        vs.Speed = 0f; vs.SteerAngle = 0f;           // route-drop does NOT zero this — do it here
        World.SetComponent(entity, vs);
    }

    // Character: pull the agent out of the crowd and zero its motor intent.
    InfantryCrowdProvider?.UnregisterAgent(entity);
    if (World.IsComponentTypeRegistered<CrowdMotorIntent>() && World.HasComponent<CrowdMotorIntent>(entity))
        World.SetComponent(entity, new CrowdMotorIntent { Velocity = System.Numerics.Vector3.Zero });
}
```
- Verify the exact namespaces/types: `NavigationIntent`, `NavigationMode.None`, `CrowdMotorIntent`
  (Fdp.Toolkit.Navigation — already used in this file's component registration), `VehicleState`
  (CarKinem.Core / Fdp.Core — already referenced). `InfantryCrowdProvider.UnregisterAgent(Entity)`
  exists on `IDtCrowdProvider`. If `NavigationMode.None` is not the exact enum name for the idle
  value, use the correct non-DirectPoint idle value (read NavigationMode).
- `VehicleState` is a struct → get/modify/SetComponent (as shown), not GetComponentRW (World API).
  Match how other code reads/writes VehicleState via the World/EntityRepository.

### File 2: `Stride/HrotStrideApp.Game/StrideHrotGame.cs` — call CancelMove when a drag begins
In the LMB-held drag block (BATCH-S2-W), change the drag-start so it cancels nav exactly once when
the drag transitions from armed → active:
```csharp
if (_dragEntity is { } dragE && lmbHeld)
{
    _lmbDragAccum += Input.MouseDelta.Length();
    if (!_dragging && _lmbDragAccum > DragStartDeadzone)
    {
        _dragging = true;
        _editorSubsystem.CancelMove(dragE); // BATCH-S2-X: drop the old nav target so it doesn't resume on release
        Log.Info("[StrideHrotGame] Drag started — cancelled nav for entity #{0}.", dragE.Index);
    }
    if (_dragging && world.IsAlive(dragE) && world.HasComponent<SimTransform>(dragE))
    {
        // ... existing ground-plane reposition (unchanged) ...
    }
}
```
(Keep the rest of the drag block, the LMB-press select, the LMB-release finalize, and the RMB logic
unchanged.)

## Constraints
- Two files. CancelMove is called ONCE per drag (on the armed→active transition), not every frame.
- Don't change the reposition write, RMB logic, selection, or IssueMoveOrder.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Drag a vehicle that had a move order (arrived or mid-drive) and drop it → it STAYS at the
  drop point (does not resume driving to the old target). Same for a mannequin. Issuing a NEW
  RMB-click move after dropping still works (re-issues intent / re-registers crowd).
