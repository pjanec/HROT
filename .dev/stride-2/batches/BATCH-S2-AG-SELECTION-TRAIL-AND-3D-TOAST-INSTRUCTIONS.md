# BATCH-S2-AG — Fix 3D selection-box trail + show paused toast in the 3D window

Two independent fixes in the Stride 3D view. TWO files.

## FIX 1 — 3D selection-box trail (emit selection/marker BEFORE the gizmo render)
### Root cause
`EditorStrideSubsystem` emits the selection highlight + move marker in "Step 7" AFTER the gizmo render
"Step 6" (BeginFrame/Render/EndFrame/ProducerBuffer.EndFrame). The comment at ~line 1193-1195 says this
is intentional so the box renders on the NEXT tick — i.e. it is ALWAYS one sim-step behind the entity.
When dragging fast (and especially at high FPS), the box lags the entity and smears into a trail of
boxes. The move marker doesn't trail only because it sits at a fixed world point.

### Fix — reorder Step 7 BEFORE Step 6 in BOTH tick paths (so the box is rendered the SAME tick it's emitted)
File: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs`

**OFF path (~lines 1184-1199):** move the Step 7 block above the Step 6 block. Result:
```csharp
// ── Step 7 (MOVED EARLIER, BATCH-S2-AG): emit selection/marker into THIS frame's buffer ──
// (was after Step 6, which rendered them one tick late → trail when dragging fast)
SelectionState.ClearIfDead(World);
EmitSelectionHighlight();
EmitMoveMarker(dt); // BATCH-S2-O: destination marker

// ── Step 6: 3D gizmo render — now renders the selection/marker emitted just above (same tick) ──
GizmoRenderer3D.Sink.BeginFrame();
GizmoRenderer3D.Render(ProducerBuffer.GetFrame());
GizmoRenderer3D.Sink.EndFrame();
ProducerBuffer.EndFrame(dt);
```

**Hosted path (~lines 1260-1274):** same reorder; KEEP `SyncSelection2D3D()` first and the `_selectionSw` /
`_gizmoSw` stopwatch wrapping correct (the Sel timer wraps the emit block, the Gizmo timer wraps the
render block — keep each Restart/Stop around its own block after the swap). Result order:
```csharp
// ── Step 7 (MOVED EARLIER): selection sync + alive-guard + emit ──
_selectionSw.Restart();
SyncSelection2D3D();
SelectionState.ClearIfDead(World);
EmitSelectionHighlight();
EmitMoveMarker(dt);
_selectionSw.Stop();

// ── Step 6: gizmo render (renders what Step 7 just emitted) ──
_gizmoSw.Restart();
GizmoRenderer3D.Sink.BeginFrame();
GizmoRenderer3D.Render(ProducerBuffer.GetFrame());
GizmoRenderer3D.Sink.EndFrame();
ProducerBuffer.EndFrame(dt);
_gizmoSw.Stop();
```
- Update the now-stale comment at ~1193-1195 (the "must run AFTER ... NEXT frame's buffer" rationale is
  reversed by this fix).
- VERIFY there is no other consumer that depends on the selection being emitted post-render. The other
  gizmos (nav/debug) are emitted during the kernel/bracket steps EARLIER in the tick, so they are already
  in the buffer before Step 6 either way — reordering only Step 7 does not affect them.

## FIX 2 — Paused toast in the 3D window (Stride DebugTextSystem)
### Goal
The paused-nav toast (BATCH-S2-AD) currently shows only in the 2D editor window. The operator clicks in
the 3D Stride window, so show it there too (primary). Keep the 2D one (harmless).

### Fix
File: `Stride/HrotStrideApp.Game/StrideHrotGame.cs` — in `Update(GameTime gameTime)`, after the
`_testHarness?.Update(wallDt)` call (~line 416), draw the toast via Stride's built-in debug text
(no SpriteFont/UI stage needed — `StrideTestHarness.DrawStatus` already uses this exact API):
```csharp
// BATCH-S2-AG: mirror the paused-nav toast (BATCH-S2-AD) into the 3D Stride viewport (where the
// operator is clicking). DebugTextSystem uses Stride's built-in font; auto-expiry already handled by
// EditorStrideSubsystem.ToastSecondsRemaining (decremented in EmitMoveMarker).
if (_editorSubsystem != null && _editorSubsystem.ToastSecondsRemaining > 0f && DebugTextSystem != null)
{
    string msg = _editorSubsystem.ToastMessage;
    // Center-top of the back buffer (virtual pixels). ~8px per char is a fine estimate for centering.
    int bw = GraphicsDevice?.Presenter?.BackBuffer?.Width ?? 1280;
    int x = Math.Max(10, (bw / 2) - (msg.Length * 4));
    DebugTextSystem.Print(msg, new Stride.Core.Mathematics.Int2(x, 30),
        new Stride.Core.Mathematics.Color4(1f, 0.85f, 0.2f, 1f)); // amber
}
```
- VERIFY `DebugTextSystem` is the `Game.DebugTextSystem` property (Stride.Profiling.DebugTextSystem) and
  `Print(string, Int2, Color4)` is the signature used by StrideTestHarness.DrawStatus (~line 254). Match
  that call exactly. `Int2`/`Color4` are in `Stride.Core.Mathematics`. Add usings only if needed.
- Must be called from `Update` (DebugTextSystem accumulates and flushes at compositor draw), per the
  harness pattern. Do NOT add it to Draw.

## Constraints
- TWO files. FIX 1 only reorders existing calls (no logic change). FIX 2 is additive (don't change the
  2D toast in StrideInspectorWindow).
- Build the Stride solution; if file lock, kill HrotStrideApp and rebuild.

## Acceptance
- Builds clean.
- (User) Dragging a selected entity fast in 3D shows ONE selection box tracking it (no trail).
- (User) RMB-move while paused shows the amber toast in the 3D window (and still in 2D).
