# BATCH-S2-P — Editor-close = quit app (crash fix) + click/selection diagnostics

Two things: (1) make closing the 2D editor window cleanly EXIT the whole app (user-approved
"close 2D = close all") to avoid the native raylib teardown crash; (2) add diagnostic logging to the
3D click-to-select / selection-highlight path because it currently does nothing visible and we need
to see where the chain breaks.

## Scope — THREE FILES

### File 1: `Stride/HrotStrideApp.Game/StrideHrotGame.cs` — close = exit + click diagnostics

**1a. Crash fix (close 2D = close all).** In `Update`, the inspector-close detection (~lines 390-401)
currently does `_inspectorWindow.Dispose(); _inspectorWindow = null;`. Replace the `else` branch so it
does NOT dispose (which calls the crashing raylib `CloseWindow()` mid-process) — instead null the
reference (so the shutdown path's `if (_inspectorWindow != null)` guard skips disposal too) and exit
the app cleanly; the OS reclaims the raylib/GLFW context on process exit:
```csharp
else
{
    // BATCH-S2-P: closing the 2D editor window quits the whole app (user choice "close 2D = close all").
    // Do NOT Dispose() the inspector here — its raylib CloseWindow() teardown mid-process crashes
    // natively while Stride's D3D context is live. Null the ref (shutdown disposal is guarded by a
    // null-check, so it won't call CloseWindow either) and let process exit reclaim the GL context.
    _inspectorWindow = null;
    Log.Info("[StrideHrotGame] 2D editor window closed by user — exiting application (close 2D = close all).");
    Exit(); // Stride Game.Exit — clean shutdown
}
```

**1b. Click diagnostics.** In the BATCH-S2-O click block, add a log on EVERY left/right click (before
acting), and a log when a selection is actually made, so we can see input + raycast + selection:
```csharp
if (lmb || rmb)
{
    var ray = FdpStrideTransform.ScreenRayToFdp(cam, Input.MousePosition);
    var hit = _raycastService.Raycast(ray.Origin, ray.Origin + ray.Direction * 1000f);
    Log.Info("[ClickDiag] {0} mouse=({1:F3},{2:F3}) rayO=({3:F1},{4:F1},{5:F1}) rayD=({6:F2},{7:F2},{8:F2}) hasHit={9} hitEntity=#{10} point=({11:F2},{12:F2},{13:F2})",
        lmb ? "LMB" : "RMB",
        Input.MousePosition.X, Input.MousePosition.Y,
        ray.Origin.X, ray.Origin.Y, ray.Origin.Z,
        ray.Direction.X, ray.Direction.Y, ray.Direction.Z,
        hit.HasHit,
        (hit.HitEntity == Fdp.Core.Entity.Null ? -1 : hit.HitEntity.Index),
        hit.PointFdp.X, hit.PointFdp.Y, hit.PointFdp.Z);

    if (lmb)
    {
        if (hit.HasHit && hit.HitEntity != Fdp.Core.Entity.Null && world.IsAlive(hit.HitEntity))
        {
            _editorSubsystem.SelectionState.Select(hit.HitEntity);
            Log.Info("[ClickDiag] LMB selected entity #{0}", hit.HitEntity.Index);
        }
        else
        {
            Log.Info("[ClickDiag] LMB no live entity hit — selection unchanged.");
        }
    }
    else if (rmb && hit.HasHit)
    {
        // ... existing RMB move-order handling unchanged ...
    }
}
```
Keep the existing RMB handling. Just add the `[ClickDiag]` lines and the LMB select/else logs. (This
replaces the previous LMB block with the logged version above.)

### File 2: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` — selection-highlight diagnostics
In `EmitSelectionHighlight()` add a THROTTLED (~once per second) diagnostic that reports whether a
selection is present, the entity, and whether the box was emitted. Use a simple frame counter field
(e.g. `_selDiagFrame`) so it logs ~every 60 calls:
```csharp
// BATCH-S2-P diagnostic (throttled ~1/s): is a selection present and is the box being emitted?
if (++_selDiagFrame >= 60)
{
    _selDiagFrame = 0;
    bool has = SelectionState.HasSelection;
    int idx = has ? SelectionState.SelectedEntity.Index : -1;
    bool alive = has && World != null && World.IsAlive(SelectionState.SelectedEntity);
    Log.Info("[SelDiag] HasSelection={0} entity=#{1} alive={2} (if true, 12 box lines emitted to ProducerBuffer)",
        has, idx, alive);
}
```
Add the field near the other selection fields: `private int _selDiagFrame;`. Place the diagnostic at
the TOP of `EmitSelectionHighlight()` (before the early-return when there's no selection) so we see
`HasSelection=false` too. Do not change the emission logic.

### File 3: `Stride/HrotStrideApp.Game/StrideInspectorWindow.cs` — neutralize the crashing teardown
In `Close()` (~line 765) the native `Raylib_cs.Raylib.CloseWindow();` is the crash source. Since
"close 2D = close all" now exits the process (which reclaims the GL context), this explicit
mid-/at-shutdown CloseWindow is unnecessary and dangerous. Comment it out / remove it, leaving a note:
```csharp
// BATCH-S2-P: do NOT call Raylib.CloseWindow() here — tearing down the raylib/GLFW context while
// Stride's D3D context is live crashes natively. "Close 2D = close all" exits the process, and the
// OS reclaims the GL context on exit. (rlImGui.Shutdown + UnloadTexture above are harmless.)
// Raylib_cs.Raylib.CloseWindow();
```
Leave `rlImGui.Shutdown()` and `UnloadTexture` as-is. Keep the `[StrideInspectorWindow] Closed.` log.

## Constraints
- Three files only. No other behavior change. Don't touch the swizzle/physics/time-control/move logic.
- The diagnostics are Log.Info; the click log fires per-click (fine), the SelDiag is throttled to ~1/s.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) Closing the 2D editor window cleanly exits the app — no crash.
- (User) With STRIDE_EDITOR_WINDOW=1: run, left-click a unit in the 3D view, select a unit in the
  2D editor, then send the editor_stride.log. The `[ClickDiag]` and `[SelDiag]` lines will reveal
  whether input fires, the raycast hits, selection is set, and the box is emitted — pinpointing the
  break.
