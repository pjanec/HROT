# IG-BATCH-01 Developer Report

**Batch:** IG-BATCH-01 — Core Infrastructure & MapCanvas  
**Tasks Completed:** IG.1.1, IG.1.5, IG.1.2  
**Date:** 2026-02-25

---

## Task Status

| Task | Status | Notes |
|------|--------|-------|
| IG.1.1 Create Bagira.IG Project | ✅ Done | Compiles, all refs included |
| IG.1.5 Create Bagira.IG.Tests | ✅ Done | xunit 2.5.3, refs Bagira.IG |
| IG.1.2 Setup MapCanvas / Camera | ✅ Done | IgApplication: window, pan, zoom, overlay |
| Tests | ✅ 15/15 passing | MapCameraZoomTests + MapCameraPanTests |

---

## Developer Insights

### Q1 — Issues During Project Structuring and Raylib Initialization

**CycloneDDS native build prerequisite was undocumented.**  
`Bagira.IG.csproj` includes `ModuleHost.Network.Cyclone`, which transitively depends on `CycloneDDS.Runtime`. That project runs a code-generation step at build time that requires `idlc.exe` — the native CycloneDDS IDL compiler. On a fresh clone of the repo, `artifacts/native/win-x64/` does not exist and the build fails immediately with `idlc.exe not found`.

The fix is documented in [FDP/ExtDeps/FastCycloneDds/IDL-IMPORT.md](../../FDP/ExtDeps/FastCycloneDds/IDL-IMPORT.md) and [FDP/build.bat](../../FDP/build.bat): run `ExtDeps\FastCycloneDds\build\native-win.ps1` first to compile the CycloneDDS C source and populate `artifacts/native/win-x64/` (idlc.exe + ddsc.dll + plugin DLLs). This step is a one-time per-machine prerequisite that should be noted in a workspace README or setup guide — it is not obvious from the solution alone.

**Package name ambiguity: `rlImGui` vs `rlImgui-cs`.**  
The task definition lists the NuGet package as `rlImGui`, but the actual package name in the NuGet registry (and used by other FDP projects) is `rlImgui-cs` version 3.2.0. The namespace is `rlImGui_cs`. A developer unfamiliar with the codebase would encounter a NuGet restore failure. Confirmed the correct package by inspecting `Fdp.Examples.Showcase.csproj`.

**No issues with Raylib itself.** `Raylib.InitWindow`, `Raylib.SetTargetFPS`, the draw loop, and `rlImGui.Setup`/`Shutdown` all work as expected using the patterns from `Fdp.Examples.Showcase/Program.cs`.

---

### Q2 — Weak Points in FDP.Toolkit.Vis2D APIs

**`MapCamera.ZoomSpeed` naming vs. its semantic.**  
`ZoomSpeed` is applied as `newZoom = targetZoom * (1 + ZoomSpeed * wheelMove)`. For a 1.2× zoom factor per tick you set `ZoomSpeed = 0.2` (i.e., `ZoomFactor - 1`). The name "speed" suggests a rate (e.g., metres per second), while its actual role is a fractional multiplier per discrete scroll tick. A name like `ZoomStepFraction` or `ZoomScrollMultiplier` would communicate intent more clearly.

A direct consequence is that scroll-up and scroll-down are **not symmetric inverses**: scrolling up once (`×1.2`) and then down once (`×0.8`) does not return to the starting zoom — it ends at `×0.96`. For a large number of successive scroll events this compounds into a noticeable drift. Log-scale zoom (`newZoom = targetZoom * pow(ZoomFactor, wheelMove)`) would give perfect symmetry, but would require changing the camera internals.

**`MapCamera.ProcessInput` signature includes `isInputCaptured`.**  
The parameter suppresses all camera input when ImGui has keyboard/mouse focus. This is good practice, but the naming slightly conflicts with Raylib's `IsMouseButtonDown` pattern — callers must build the boolean themselves from `rlImGui.IsMouseHovered()` or similar. A convenience overload that accepts `ImGuiIO*` directly would reduce boilerplate.

**`MapCamera.FocusOn(Vector2 position)` sets the internal target directly**, bypassing the interpolation. This is useful for programmatic camera positioning (e.g., keyboard pan logic) but it means there is no way to request a smooth animated pan to a world-space position from outside the camera. A `NavigateTo(position, durationSec)` method would be a useful addition for later tasks (e.g., "center on selected entity").

---

### Q3 — Design Decisions Beyond the Instructions

**Keyboard pan via `_keyboardPanTarget` accumulator.**  
Arrow-key pan is implemented by maintaining a separate `_keyboardPanTarget` Vector2 that accumulates displacement each frame, then calling `_camera.FocusOn(_keyboardPanTarget)`. This avoids "camera fighting" that would occur if `FocusOn` were called with a fixed world offset while the camera's own interpolation was still converging from a previous pan — the two would cancel each other out. The accumulator is initialized from `_camera.Target` on first use so it stays coherent with mouse-driven pans.

An alternative considered was calling `ProcessInput` with a synthetic wheel/drag to drive keyboard zoom, but `ProcessInput` only accepts a single-frame mouse delta and wheel tick, making multi-frame keyboard-held input harder to express correctly.

**+/- keys for zoom simulate a ProcessInput wheel tick** (wheel = +1 or -1 per key-press frame). This re-uses the same clamping and interpolation path as mouse-wheel zoom, keeping the behaviour consistent without duplicating zoom logic.

**Debug overlay uses `Raylib.GetScreenToWorld2D`** to show the mouse world position. This is computed every frame from the current camera state (which includes the interpolated `Offset` and `Zoom`), so it always reflects where the mouse is actually pointing, even mid-animation.

---

### Q4 — Performance Concerns

No performance concerns at this stage. The render loop is standard Raylib 60fps with a single `MapCanvas.Draw()` call and a minimal ImGui overlay. The interpolation (`MapCamera.Update`) and the keyboard accumulator are both O(1).

Points to watch in later batches when rendering entities:
- `MapCanvas.Draw()` will call Draw on all registered layers. If entity count grows into thousands, the number of Raylib draw calls could become a bottleneck — batching or culling by camera bounds will be needed.
- `Raylib.GetScreenToWorld2D` is called every frame for the debug overlay — this is fine now, but if it moves into hot-path entity logic (e.g., picking) it should be cached per frame.

---

## Files Created / Modified

| File | Change |
|------|--------|
| `Bagira.IG/Bagira.IG.csproj` | Created — net8.0 console app with all project refs and NuGet packages |
| `Bagira.IG/IgCameraConstants.cs` | Created — named constants for all camera/window config values |
| `Bagira.IG/IgApplication.cs` | Created — main app class (window, MapCanvas, pan/zoom, debug overlay) |
| `Bagira.IG/Program.cs` | Created — minimal entry point |
| `Bagira.IG/Components/.gitkeep` … `Adapters/.gitkeep` | Created — folder structure markers |
| `Bagira.IG.Tests/Bagira.IG.Tests.csproj` | Created — xunit 2.5.3 test project |
| `Bagira.IG.Tests/MapCameraTests.cs` | Created — 15 behavioral tests (zoom clamping, pan, input-capture suppression) |
| `IOS-IG-SimHost.sln` | Modified — added both new projects + 24 build config entries |
| `FDP/ExtDeps/FastCycloneDds/artifacts/native/win-x64/` | Created — native CycloneDDS artifacts (populated by running `build/native-win.ps1`) |
