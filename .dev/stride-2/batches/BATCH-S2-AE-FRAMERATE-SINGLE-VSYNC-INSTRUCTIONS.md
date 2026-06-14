# BATCH-S2-AE — Fix ~20Hz: collapse the double-vsync to a single 60Hz pacer

## Root cause (PROVEN by investigation)
The hosted editor pumps TWO windows serially on ONE thread each frame:
1. Stride D3D present inside `base.Draw()` — vsync ON by default (~16 ms block).
2. The raylib editor window's `Raylib.EndDrawing()` (GLFW SwapBuffers) — GLFW swap interval defaults to 1
   on Windows, so it ALSO blocks ~16 ms (raylib's `SetTargetFPS(0)` only disables raylib's *software*
   limiter, not GLFW's vsync). The developer's own comment at StrideInspectorWindow.cs ~line 719-727
   names this exact risk.

Two ~16 ms vsync waits per frame ⇒ ~32 ms/frame ⇒ ~31 Hz, degrading to ~20 Hz with jitter. This caps
EVERYTHING (entity motion AND ImGui window dragging), since it's the shared frame loop.

## Fix strategy
We want exactly ONE vsync wait per frame. The GLFW present's vsync (raylib editor window) cannot be
reliably disabled from C# (raylib.dll does not export `glfwSwapInterval` on Windows). So instead:

**When the raylib editor window is active, turn OFF Stride's D3D vsync.** Then the Stride present no
longer blocks, the GLFW `EndDrawing` remains the single ~16 ms vsync pacer, and the shared loop runs at
~60 Hz. When the editor window is NOT active (pure 3D / selftest / headless), LEAVE Stride vsync ON
(otherwise the lone 3D window would render uncapped and spin the GPU).

Tradeoff: the 3D window present becomes unsynced (possible mild tearing on the 3D view) but is still
paced to ~60 Hz by the GLFW vsync. This is the right trade for an editor; a tear-free fix would require
moving one window to its own thread (out of scope).

## Scope — ONE FILE
`Stride/HrotStrideApp.Game/StrideHrotGame.cs`

1. Determine the flag that gates opening the raylib editor window. The investigation/summary indicates an
   env flag `STRIDE_EDITOR_WINDOW` (value "1"). VERIFY the exact env var name and how the code decides to
   create `_inspectorWindow` (grep for `_inspectorWindow = ` / `Environment.GetEnvironmentVariable` near
   the inspector-window creation). Use the SAME condition.

2. In the constructor (near `WindowMinimumUpdateRate.MinimumElapsedTime = TimeSpan.Zero`, ~line 243), or
   wherever `GraphicsDeviceManager` is first available, set vsync off ONLY when the editor window is on:
```csharp
// BATCH-S2-AE: the raylib editor window's GLFW present already vsync-blocks ~16ms/frame; if Stride's
// D3D present ALSO vsync-blocks we get two waits per frame (~32ms => ~20-31Hz). When the editor window
// is active, disable Stride's vsync so the single GLFW present paces the shared loop at ~60Hz. When the
// editor window is OFF, keep Stride vsync ON (otherwise the lone 3D window renders uncapped).
bool editorWindowActive = /* same condition used to create _inspectorWindow, e.g.:
    Environment.GetEnvironmentVariable("STRIDE_EDITOR_WINDOW") == "1" */;
if (editorWindowActive)
{
    GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
    Log.Info("[StrideHrotGame] Editor window active — disabling Stride D3D vsync (single GLFW pacer, BATCH-S2-AE).");
}
```
   VERIFY: `GraphicsDeviceManager` is the protected property on `Stride.Engine.Game` (it is). Setting
   `SynchronizeWithVerticalRetrace` in the ctor is applied when the device is created. If Stride requires
   `GraphicsDeviceManager.ApplyChanges()` for a post-init change, the ctor-time set does NOT need it (the
   device isn't created yet). If you find the device is already created at your chosen call site, move the
   set EARLIER (ctor) so no ApplyChanges is needed. Do NOT add a per-frame set.

3. Do NOT change `WindowMinimumUpdateRate`, `IsFixedTimeStep`, the raylib window config, or PumpFrame.
   Do NOT attempt a `glfwSwapInterval` P/Invoke.

## Verify-after (report; the existing timing logs make this checkable)
The code already logs `[Frame timing] ... Draw avg=...` (StrideHrotGame) and `[PumpFrame timing] ...
Present(EndDrawing)=...` (StrideInspectorWindow). After the fix, with the editor window active:
- `Draw avg` should drop well below 16 ms (Stride present no longer vsync-blocks).
- `Present(EndDrawing)` stays ~16 ms (the single pacer).
- `FrameDelta avg` should approach ~16 ms (~60 Hz) instead of ~32-50 ms.
Note this in the report (the USER will confirm by observing smoothness + the log).

## Constraints
- ONE file. The vsync change MUST be conditional on the editor window being active.
- No new threads, no sleeps, no P/Invoke.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User, editor window active) Movement and ImGui window dragging are smooth (~60 Hz, not ~20). The
  `[Frame timing]` / `[PumpFrame timing]` logs show the single-vsync profile above. Mild tearing on the
  3D view is acceptable (note it; we can revisit with a threaded present if it bothers you).
- (User, NO editor window / selftest) Behaviour unchanged (Stride vsync still on).
