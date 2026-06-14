# BATCH-S2-T — Make a selected mannequin move on right-click (character routing)

## Problem (root cause found)
A mannequin (D1 / infantry, capsule) selected in 3D does NOT move on RMB-click, even though the move
order fires. Cause: the TKB translator wrongly injects `VehicleState` onto infantry (TkbType 2002
carries VehicleParametersDto). So:
- My `IssueMoveOrder` routes by `HasComponent<VehicleState>` → treats the mannequin as a VEHICLE →
  writes `NavigationIntent` (DirectPoint). But `KinematicVehicleMotor` SKIPS capsule bodies → no drive.
- And `NavigationIntentBridgeSystem` skips crowd-enrollment for entities WITH `VehicleState` → the
  mannequin is never enrolled in the DotRecast crowd either.

The working F6 harness case ("FDP Move Order char", StridePhysicsHarnessCases.cs ~2002-2009) handles
infantry by STRIPPING the bogus `VehicleState` then calling `FdpNavigationOrders.IssueMoveTo`.

## Fix — ONE FILE
`Stride/HrotStrideApp.Game/StrideHrotGame.cs` — route the move order by the entity's PHYSICS BODY
SHAPE (reliable), not by `VehicleState` (unreliable). Character (Capsule) → strip VehicleState +
IssueMoveTo; vehicle (OrientedBox) → existing NavigationIntent DirectPoint.

1. Change `IssueMoveOrder` from `private static` to a `private` instance method (so it can read
   `_editorSubsystem.PhysicsBodyLifecycle`). The call site `IssueMoveOrder(world, sel.SelectedEntity,
   hit.PointFdp)` stays the same (instance call).

2. New body:
```csharp
private void IssueMoveOrder(EntityRepository world, Fdp.Core.Entity entity, System.Numerics.Vector3 targetFdp)
{
    const float Speed = 5f;
    const float ArrivalRadius = 2f;

    // Discriminate by physics body shape — VehicleState is wrongly injected on infantry by the
    // TKB translator, so it is NOT a reliable vehicle/character discriminator (BATCH-S2-T).
    bool isCharacter = false;
    var lifecycle = _editorSubsystem?.PhysicsBodyLifecycle;
    if (lifecycle != null && lifecycle.Bodies.TryGetValue(entity, out var bodyRef))
        isCharacter = bodyRef.ShapeKind == CollisionShapeKind.Capsule;

    if (isCharacter)
    {
        // F6 workaround: strip the bogus VehicleState so NavigationIntentBridgeSystem enrolls the
        // infantry in the DotRecast crowd (it skips entities that carry VehicleState).
        if (world.IsComponentTypeRegistered<VehicleState>() && world.HasComponent<VehicleState>(entity))
            world.RemoveComponent<VehicleState>(entity);

        FdpNavigationOrders.IssueMoveTo(world, entity, targetFdp, Speed, ArrivalRadius, NavLayerMask.Infantry);
        Log.Info("[StrideHrotGame] Move order (CHARACTER) entity #{0} → FDP ({1:F2},{2:F2},{3:F2}) via IssueMoveTo.",
            entity.Index, targetFdp.X, targetFdp.Y, targetFdp.Z);
        return;
    }

    // Vehicle path: NavigationIntent DirectPoint (existing logic — keep exactly as before).
    if (!world.IsComponentTypeRegistered<NavigationIntent>()) return;
    var intent = world.HasComponent<NavigationIntent>(entity)
        ? world.GetComponent<NavigationIntent>(entity) : default;
    intent.Mode             = NavigationMode.DirectPoint;
    intent.FinalDestination = targetFdp;
    intent.TargetSpeed      = Speed;
    intent.ArrivalRadius    = ArrivalRadius;
    intent.IntentId         = intent.IntentId + 1;
    intent.ReverseAllowed   = 0;
    if (world.HasComponent<NavigationIntent>(entity)) world.SetComponent(entity, intent);
    else world.AddComponent(entity, intent);
    if (world.IsComponentTypeRegistered<NavigationStatus>() && !world.HasComponent<NavigationStatus>(entity))
        world.AddComponent(entity, new NavigationStatus { Result = NavigationResult.InProgress });
    Log.Info("[StrideHrotGame] Move order (VEHICLE) entity #{0} → FDP ({1:F2},{2:F2},{3:F2}) via NavigationIntent.",
        entity.Index, targetFdp.X, targetFdp.Y, targetFdp.Z);
}
```
   (Preserve the existing vehicle-path logic verbatim — only wrap it under the `else` of the
   character branch. Keep the existing per-click "[StrideHrotGame] Move order: entity #N → FDP ..."
   log at the CALL SITE if present — these new logs are additional and fine; or remove the call-site
   one to avoid duplication, your choice, but keep at least one.)

3. Add the `using` for `CollisionShapeKind` — it lives in `Fdp.Toolkit.Tkb.Domain`. (`VehicleState`,
   `NavigationIntent`, `NavLayerMask`, `FdpNavigationOrders` usings are already present from S2-O.)
   Verify `EntityRepository.RemoveComponent<T>(Entity)` exists (the F6 case uses
   `ctx.World.RemoveComponent<VehicleState>(target)`), and `PhysicsBodyReference.ShapeKind` +
   `EditorStrideSubsystem.PhysicsBodyLifecycle.Bodies` (IReadOnlyDictionary<Entity, PhysicsBodyReference>).

## Constraints
- One file. Vehicle behavior unchanged (it routes to the same NavigationIntent path). Only infantry
  routing changes (shape-based + strip VehicleState + IssueMoveTo).
- Do not touch the click-detection / RMB-drag logic (BATCH-S2-S) or selection.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Select the D1 mannequin (cyan box) → clean RMB-click on ground → log
  "Move order (CHARACTER) entity #N ... via IssueMoveTo" and the mannequin walks toward the point.
- Vehicle right-click move still works (logs "Move order (VEHICLE) ...").
