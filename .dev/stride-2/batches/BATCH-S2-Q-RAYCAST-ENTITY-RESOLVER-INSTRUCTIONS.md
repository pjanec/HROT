# BATCH-S2-Q — Fix 3D click picking (raycast hit → FDP entity resolver)

## Problem (proven by [ClickDiag] logs)
Every 3D click logs `hasHit=True` but `hitEntity=#-1` (Entity.Null) — even clicking directly on a
unit. So LMB-select never selects, and RMB-move never has a selection to move. Root cause:
`StrideRaycastService.ToFdpHit` maps the hit back to an FDP entity by parsing the Stride entity's
NAME as an integer index (`int.TryParse(hit.Collider.Entity.Name)`). But the visual factory names
entities `"Visual_Models/Box2x1x1"` / `"Visual_Models/mannequinModel"` — not the index — so the parse
always fails → Entity.Null.

## Fix
Resolve the FDP entity via the live visuals map (FDP Entity → StrideVisualReference.VisualHandle ==
the Stride entity) instead of (or in addition to) the name-parse. Inject a resolver into the raycast
service from StrideHrotGame.

## Scope — TWO FILES

### File 1: `Stride/HrotStrideApp.Game/StrideRaycastService.cs`
1. Add a resolver field + optional constructor parameter (keep the existing ctor working — default
   the param to null):
```csharp
private readonly Func<global::Stride.Engine.Entity, Fdp.Core.Entity>? _entityResolver;

public StrideRaycastService(
    Simulation simulation,
    Func<global::Stride.Engine.Entity, Fdp.Core.Entity>? entityResolver = null)
{
    _simulation     = simulation ?? throw new ArgumentNullException(nameof(simulation));
    _entityResolver = entityResolver;
}
```
   (Add `using System;` if not already present — it is.)

2. Change `ToFdpHit` from `private static` to `private` (instance) so it can use `_entityResolver`,
   and resolve the entity via the resolver first, falling back to the existing name-parse:
```csharp
Entity hitEntity = Entity.Null;
var collEntity = hit.Collider?.Entity;
if (collEntity != null)
{
    // Primary: resolve via the live visuals map (Stride entity → FDP entity).
    if (_entityResolver != null)
        hitEntity = _entityResolver(collEntity);

    // Fallback (legacy / tests): entity named with its FDP index as a decimal string.
    if (hitEntity == Entity.Null && collEntity.Name is string nm && int.TryParse(nm, out int idx))
        hitEntity = new Entity(idx, 1);
}
```
   (Both call sites of `ToFdpHit` are already instance-context — `Raycast` and `RaycastPenetrating`
   are instance methods — so making it instance compiles cleanly.)

### File 2: `Stride/HrotStrideApp.Game/StrideHrotGame.cs`
1. Where the raycast service is constructed (BATCH-S2-O, ~line 762):
```csharp
_raycastService = new StrideRaycastService(physicsProcessor.Simulation, ResolveFdpEntityFromStride);
```
2. Add the resolver method (reverse-looks-up the visuals map by reference; deferred — reads
   `_editorSubsystem` at call time, after boot):
```csharp
/// <summary>
/// Resolves the FDP <see cref="Fdp.Core.Entity"/> that owns a hit Stride visual/physics entity,
/// by reverse-looking-up the visual-binding map (FDP Entity → StrideVisualReference.VisualHandle).
/// Returns <see cref="Fdp.Core.Entity.Null"/> for static scene geometry (floor/walls). (BATCH-S2-Q)
/// </summary>
private Fdp.Core.Entity ResolveFdpEntityFromStride(global::Stride.Engine.Entity strideEntity)
{
    var visuals = _editorSubsystem?.VisualBindingSystem?.Visuals;
    if (visuals != null)
    {
        foreach (var kv in visuals)
            if (ReferenceEquals(kv.Value.VisualHandle, strideEntity))
                return kv.Key;
    }
    return Fdp.Core.Entity.Null;
}
```
   NOTE: the physics body is attached to the same Stride entity that is the visual handle, so the
   raycast collider's `Entity` equals `VisualHandle` (reference equality holds).

## Constraints
- Two files only. No change to the swizzle, the move-order logic, the [ClickDiag]/[SelDiag] logs, or
  the selection-box code. Keep the name-parse as a fallback.
- The existing single-arg `StrideRaycastService(simulation)` ctor must still work (defaulted param).

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Left-click a UNIT in the 3D view → `[ClickDiag] ... hitEntity=#<N>` (not -1) and
  `[ClickDiag] LMB selected entity #N`; `[SelDiag] HasSelection=True`. The selection box should
  appear (if it does NOT despite HasSelection=True, that's a separate gizmo-render issue we'll then
  isolate). Right-click ground with a unit selected → `[StrideHrotGame] Move order ...` and the unit
  drives toward the point + amber marker.
- Clicking the floor/empty still yields `hitEntity=#-1` and leaves selection unchanged.
