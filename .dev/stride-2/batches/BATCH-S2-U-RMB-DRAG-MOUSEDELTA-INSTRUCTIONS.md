# BATCH-S2-U — RMB drag detection via accumulated MouseDelta (cursor-lock safe)

## Problem (proven by log)
Every RMB-release logs `travel=0.000` even after a camera-orbit drag, so the move order fires on
every RMB (including camera orbits). Cause: the camera controller LOCKS/RECENTERS the cursor during
RMB-orbit, so `Input.MousePosition` at RMB-down ≈ at RMB-up → the down-vs-up distance is ~0 → my
click/drag check thinks every orbit is a click. The orbit-end ray then points at a far wall, yielding
off-arena move targets (FDP Y≈-20) that nothing can navigate to.

## Fix
Detect a drag by ACCUMULATING `Input.MouseDelta` magnitude while RMB is held (MouseDelta reports raw
movement even when the cursor is locked), instead of comparing down vs up positions.

## Scope — ONE FILE
`Stride/HrotStrideApp.Game/StrideHrotGame.cs`

1. Replace the RMB-drag tracking fields:
```csharp
// BATCH-S2-U: RMB click-vs-drag via accumulated mouse delta (camera orbit locks the cursor, so
// down-vs-up position is unreliable; MouseDelta reports movement even when the cursor is locked).
private bool  _rmbHeld;
private float _rmbDragAccum;
private const float RmbDragDeadzone = 0.01f; // accumulated normalized delta above which it's a drag
```
   (Remove the old `_rmbDownPos` / `RmbClickMaxTravel` fields.)

2. In the click block, replace the RMB handling. Track held-state + accumulated delta each frame;
   decide click-vs-drag on release:
```csharp
bool lmb     = Input.IsMouseButtonPressed(MouseButton.Left);
bool rmbDown = Input.IsMouseButtonPressed(MouseButton.Right);
bool rmbUp   = Input.IsMouseButtonReleased(MouseButton.Right);

// Track RMB hold + accumulate movement (camera orbit → large accum; click → ~0).
if (rmbDown) { _rmbHeld = true; _rmbDragAccum = 0f; }
if (_rmbHeld) _rmbDragAccum += Input.MouseDelta.Length();

// LMB select (on press) — unchanged.
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

// RMB move — only on release, only if it was a click (small accumulated movement), not a camera orbit.
if (rmbUp)
{
    float accum = _rmbDragAccum;
    _rmbHeld = false;
    _rmbDragAccum = 0f;
    if (accum <= RmbDragDeadzone)
    {
        var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition);
        var hit = _raycastService.Raycast(ray.Origin, ray.Origin + ray.Direction * 1000f);
        Log.Info("[ClickDiag] RMB-click accum={0:F4} hasHit={1} point=({2:F2},{3:F2},{4:F2})",
            accum, hit.HasHit, hit.PointFdp.X, hit.PointFdp.Y, hit.PointFdp.Z);
        if (hit.HasHit)
        {
            var sel = _editorSubsystem.SelectionState;
            if (sel.HasSelection && world.IsAlive(sel.SelectedEntity))
            {
                IssueMoveOrder(world, sel.SelectedEntity, hit.PointFdp);
                _editorSubsystem.ShowMoveMarker(hit.PointFdp);
            }
        }
    }
    else
    {
        Log.Info("[ClickDiag] RMB drag (accum={0:F4}) — camera orbit, no move order.", accum);
    }
}
```
   Keep the surrounding `if (_raycastService != null) { var cam=...; var world=...; if (cam!=null && world!=null) { ... } }` guards. Keep `IssueMoveOrder` and its logs.

## Constraints
- One file. LMB unchanged. No change to IssueMoveOrder routing, marker, raycast.
- Verify `Input.IsMouseButtonDown` is NOT needed (we use Pressed/Released + per-frame accum while
  `_rmbHeld`). Verify `Input.MouseDelta` exists (Stride.Input — Vector2, normalized). If the camera
  also consumes RMB, that's fine — input state is observable by both.

## Acceptance
- Builds clean.
- (User) RMB-drag to orbit → `[ClickDiag] RMB drag (accum=…) — camera orbit, no move order` and NO
  marker. A deliberate RMB-click (no orbit) on the arena floor → `RMB-click accum=~0` + one move +
  marker at an IN-ARENA point (FDP Y in ~0..15, not -20). The selected vehicle/mannequin then
  navigates to it (target is now on the navmesh).
