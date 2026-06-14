# BATCH-S2-S — RMB = click-not-drag (camera orbit) + 3D→2D selection propagation

Two issues from GPU testing:
- **(C)** Right-click issues a move order + marker on RMB-DOWN, so RMB-dragging to orbit the camera
  spams move orders to the selected unit. Fix: fire the move only on RMB-RELEASE and only if the
  mouse barely moved since RMB-down (a click, not a camera drag).
- **(A)** Selecting a unit in 3D does not show in the 2D editor map. The mirror only sets the UI
  `PrimarySelected`; the 2D map overlay is driven by the ECS `SelectionState` component
  (`Hrot.IG.Components.SelectionState`, written by the editor's selection system). 3D→2D must write
  that component too.

## Scope — THREE FILES

### File 1: `Stride/HrotStrideApp.Game/StrideHrotGame.cs` — RMB click vs camera-drag
1. Add fields:
```csharp
// BATCH-S2-S: RMB click-vs-drag discrimination (RMB is also camera-orbit).
private System.Numerics.Vector2? _rmbDownPos;
private const float RmbClickMaxTravel = 0.02f; // normalized screen units; >this = camera drag, ignore
```
2. In the click block, KEEP LMB exactly as-is (select on press). REPLACE the RMB handling so it
   records the down position and only acts on release-without-drag. The block currently does the
   raycast unconditionally then branches on lmb/rmb; restructure so the RMB move happens on RELEASE:
```csharp
bool lmb = Input.IsMouseButtonPressed(MouseButton.Left);
bool rmbDown = Input.IsMouseButtonPressed(MouseButton.Right);
bool rmbUp = Input.IsMouseButtonReleased(MouseButton.Right);

// Record RMB-down position to later tell a click from a camera-orbit drag.
if (rmbDown) _rmbDownPos = Input.MousePosition;

// LMB select (on press) — unchanged behavior.
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
        Log.Info("[ClickDiag] LMB selected entity #{0}", hit.HitEntity.Index);
    }
    else Log.Info("[ClickDiag] LMB no live entity hit — selection unchanged.");
}

// RMB move — only on RELEASE and only if it was a click (not a camera-orbit drag).
if (rmbUp && _rmbDownPos is { } downPos)
{
    float travel = (Input.MousePosition - downPos).Length();
    _rmbDownPos = null;
    if (travel <= RmbClickMaxTravel)
    {
        var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition);
        var hit = _raycastService.Raycast(ray.Origin, ray.Origin + ray.Direction * 1000f);
        Log.Info("[ClickDiag] RMB-click travel={0:F3} hasHit={1} point=({2:F2},{3:F2},{4:F2})",
            travel, hit.HasHit, hit.PointFdp.X, hit.PointFdp.Y, hit.PointFdp.Z);
        if (hit.HasHit)
        {
            var sel = _editorSubsystem.SelectionState;
            if (sel.HasSelection && world.IsAlive(sel.SelectedEntity))
            {
                IssueMoveOrder(world, sel.SelectedEntity, hit.PointFdp);
                _editorSubsystem.ShowMoveMarker(hit.PointFdp);
                Log.Info("[StrideHrotGame] Move order: entity #{0} → FDP ({1:F2},{2:F2},{3:F2}).",
                    sel.SelectedEntity.Index, hit.PointFdp.X, hit.PointFdp.Y, hit.PointFdp.Z);
            }
        }
    }
    else
    {
        Log.Info("[ClickDiag] RMB drag (travel={0:F3}) — treated as camera orbit, no move order.", travel);
    }
}
```
   Remove the old combined `if (lmb || rmb) { var ray=...; var hit=...; ... }` block — the ray/hit is
   now computed inside each branch only when needed. Confirm `Input.IsMouseButtonReleased` exists in
   this Stride version (it does: Stride.Input).

### File 2: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — programmatic select that updates the 2D map overlay
Replace the simple `Selected2DEntity` setter behavior with a method that ALSO writes the ECS
`SelectionState` component (the thing the 2D map overlay reads). Add a public method:
```csharp
/// <summary>
/// Programmatically sets the 2D editor selection to <paramref name="entity"/> (or clears it when
/// null), updating BOTH the UI-level PrimarySelected AND the ECS SelectionState components that the
/// 2D map overlay renders — mirroring what an in-map click does. Used by 3D→2D sync (BATCH-S2-S).
/// </summary>
public void SetSelection2D(Fdp.Core.Entity? entity)
{
    if (_world == null) return;

    // Clear existing ECS selection flags.
    var q = _world.Query().With<Hrot.IG.Components.SelectionState>()
        .WithLifecycle(Fdp.Core.EntityLifecycle.All).Build();
    foreach (var e in q)
    {
        var s = _world.GetComponent<Hrot.IG.Components.SelectionState>(e);
        if (s.IsSelected || s.IsPrimarySelection)
            _world.SetComponent(e, new Hrot.IG.Components.SelectionState { IsSelected = false, IsPrimarySelection = false });
    }

    // Set the new primary selection (ECS component) when a live entity is given.
    if (entity.HasValue && entity.Value != Fdp.Core.Entity.Null && _world.IsAlive(entity.Value))
    {
        if (!_world.HasComponent<Hrot.IG.Components.SelectionState>(entity.Value))
            _world.AddComponent(entity.Value, new Hrot.IG.Components.SelectionState());
        _world.SetComponent(entity.Value, new Hrot.IG.Components.SelectionState { IsSelected = true, IsPrimarySelection = true });
    }

    // Keep the UI-level primary in sync (drives inspector/tools).
    if (_selectionState != null)
        _selectionState.PrimarySelected = entity;
}
```
   - Verify the exact `Query()` / `WithLifecycle` / `EntityLifecycle.All` API by mirroring
     `SelectionInteractionSystem.ClearAll`/`ApplySelection` (Hrot/Engine/Hrot.Presentation/ScenarioEditor/Systems/SelectionInteractionSystem.cs lines ~123, 160-176). Match it verbatim.
   - Use the fully-qualified `Hrot.IG.Components.SelectionState` (there are other "SelectionState"
     types around — avoid `using` ambiguity). Add a project reference to Hrot.Map.Common only if the
     type isn't already resolvable (SelectionInteractionSystem resolves it, so the editor assembly
     already references it).
   - Keep the existing `Selected2DEntity` get/set and `Selection2DVersion` from BATCH-S2-R (the
     getter is still used by the 2D→3D mirror).

### File 3: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` — 3D→2D mirror calls SetSelection2D
In `SyncSelection2D3D()`, the 3D→2D branch currently does
`_editor.Selected2DEntity = SelectionState.HasSelection ? SelectionState.SelectedEntity : (Entity?)null;`.
Replace that single assignment with a call that also writes the ECS component:
```csharp
_editor.SetSelection2D(SelectionState.HasSelection ? SelectionState.SelectedEntity : (Fdp.Core.Entity?)null);
```
Leave the version-tracker updates and the 2D→3D branch unchanged.

## Constraints
- Three files only. LMB behavior unchanged (select on press). 2D→3D sync unchanged.
- RMB move fires ONLY on release-without-drag; camera-orbit RMB drags must NOT issue move orders.
- No change to the move-order routing, marker, raycast, or box-emit logic.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) RMB-drag to orbit the camera → NO move order / marker (log shows "RMB drag ... camera orbit").
  A clean RMB-click on the ground (no drag) → one move order + one marker.
- (User) Left-click a unit in 3D → the 2D editor map shows it selected (overlay), and the inspector
  reflects it. (2D→3D still works.)
- (User) With the mannequin (D1) selected, a clean RMB-click → `Move order: entity #<mannequin>`
  appears (then we can see whether the character actually moves — separate follow-up if not).
