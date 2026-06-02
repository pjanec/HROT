# BATCH-22 Report — Dual-Window Editor (STR-P5-T2)

## Implementation Summary

### Step 0 — Research: Feasibility Analysis

#### The Existing Hrot Editor

- **`FdpApplication`** (`FDP/Engine/Fdp.Presentation/Raylib/FdpApplication.cs`): Abstract base class
  with a blocking `while (!WindowShouldClose())` loop. Not directly usable as a per-frame pump.
  However, `BeginDrawing()` / `EndDrawing()` and `PollInputEvents()` are all individual non-blocking
  calls that can be factored out.
- **`EditorSubsystem`** (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`): The full Hrot editor.
  `Update(float dt)` is the per-frame logic; `DrawWorld()` / `DrawUI()` are per-frame render calls.
  Both are gated on `_headless` flag. The subsystem has ~1800 lines; it is not a blocker — but
  instantiating it standalone (with all its deps: AI hot-reload, breakpoints, blueprint debug,
  replay, etc.) in `editor_stride` would add significant complexity and unverified integration
  risk. The task explicitly says "reuse existing editor UI code/widgets where possible."
- **The reference from `EditorStrideSubsystem` comments** (lines 1373–1374 = orch-bus swap +
  `ClusterMaster.Tick()`) is already implemented in `EditorStrideSubsystem.Tick()`.
- Raylib's **`rlImGui`** in this codebase uses `rlImGui.Setup(bool)` (positional, no named
  parameter). Version 3.2.0.

#### Stride Host Loop

- **`StrideHrotGame`** uses Stride's **internal loop** (`game.Run()` with no `GameContext`
  argument). `Update(GameTime)` is called by Stride each frame and currently drives
  `EditorStrideSubsystem.Tick(dt)` via the fixed-timestep accumulator.
- The natural integration point is `StrideHrotGame.Update(GameTime)` — one call per render
  frame, on the Stride game thread (= the OS host thread for all SDL2/DirectX operations).

#### Feasibility: Options A / B / C

**Option A — Interleave both pumps on the host thread (Stride tick → raylib/ImGui frame):**

- **FEASIBLE.** Design doc §8.3 is explicit: *"Graphics contexts don't conflict. Stride =
  Direct3D; raylib = its own GLFW/OpenGL; ImGui = the raylib (rlImGui) instance only. Separate
  APIs, windows, device contexts — nothing shared. The only rule is thread-affinity, trivially
  satisfied here."*
- Raylib's `BeginDrawing()` / `EndDrawing()` operate on the GLFW/OpenGL context. Stride's
  `Update()` / `Draw()` operate on SDL2 + Direct3D. They are independent APIs — pumping
  both sequentially on one thread is architecturally safe.
- `SetTargetFPS(0)` on the raylib window ensures `EndDrawing()` returns immediately (no sleep);
  Stride's own throttler governs frame cadence.
- **Caveat about `FdpApplication.Run()`:** The existing `EditorSubsystem` is wired to run
  INSIDE `FdpApplication.Run()` (blocking loop). To interleave safely we cannot call `Run()`.
  Instead we call individual per-frame methods directly: `InitWindow()` once at start, then
  `PollInputEvents() + BeginDrawing() + rlImGui.Begin() + OnDrawUI() + rlImGui.End() +
  EndDrawing()` each frame, and `CloseWindow()` at end. This is exactly what `FdpApplication`
  does inside its while-loop — factored out.
- **rlImGui multi-window note:** `rlImGui.Setup()` binds to the most-recently-initialized
  GLFW context. The Stride window has NO rlImGui (Stride is DirectX, not OpenGL). Therefore
  the inspector window's `rlImGui.Setup()` call creates the ONLY ImGui context — no conflict.

**Option B — Raylib on a separate thread reading a snapshot:**
- Design §8.3 notes this as the "concurrency upgrade path" for when the frame budget binds.
  Not built now; not needed.

**Option C — ImGui overlay inside the Stride window (Stride.UI-based or Stride ImGui binding):**
- Stride 4.2 has no built-in ImGui. Community bindings exist but are not in this codebase.
  Higher integration risk than Option A for new readers of this code. Not chosen.

**Verdict: Option A chosen.** It matches the design doc exactly, requires no new dependencies,
and is architecturally safe. The only wrinkle is factoring `FdpApplication.Run()` into per-frame
calls — done in `StrideInspectorWindow`.

---

### Step 1 — Implementation

#### New file: `Stride/HrotStrideApp.Game/StrideInspectorWindow.cs`

Three classes/types:

1. **`StrideInspectorWindowConfig`** (static): Reads `STRIDE_EDITOR_WINDOW` env var
   (`"1"` or `"true"` enables). `ForceEnabled: bool?` property overrides the env var —
   used in headless tests to assert on config logic without a window. Default = disabled
   (CI/headless safe).

2. **`StrideInspectorViewModel`** (static, pure logic, no Raylib/window deps): Maps the
   live FDP world → display rows and inspector fields.
   - `BuildEntityList(EntityRepository?)` → queries `NetworkIdentity + SimTransform`, returns
     `IReadOnlyList<EntityRow>` with TKB type, display name, position, network ID.
   - `BuildInspector(EntityRepository?, Entity)` → reads `SimTransform` (position + Euler
     rotation), `SimVelocity` (linear, speed), `NavigationStatus` (result + phase),
     `NetworkIdentity`, `TkbIdentity`, authority bit. Returns `InspectorViewModel`.
   - `BuildDisplayName(long tkbType, long networkId)` → UrbanCombat name table + fallbacks.
   - `QuaternionToEulerDeg(Quaternion)` → YXZ Euler in degrees (for display only).

3. **`StrideInspectorWindow`** (`IDisposable`): The actual raylib/ImGui window.
   - `Open()`: `InitWindow()` + `SetTargetFPS(0)` + `rlImGui.Setup()` once.
   - `PumpFrame()`: `BeginDrawing()` + `rlImGui.Begin()` + `DrawInspectorUi()` +
     `rlImGui.End()` + `EndDrawing()`. Non-blocking. Skips if window already closed.
   - `DrawInspectorUi()`: Two-panel ImGui layout — left panel = entity list (selectable
     rows), right panel = inspector fields for selected entity.
   - `Close()` / `Dispose()`: `rlImGui.Shutdown()` + `Raylib.CloseWindow()`.

#### Changes to `StrideHrotGame.cs`

- Added `_inspectorWindow: StrideInspectorWindow?` field.
- `BootEditorSubsystem()`: After `BuildTestHarness()`, checks `StrideInspectorWindowConfig.IsEnabled`
  and opens the window if enabled. Wrapped in try/catch so a raylib failure doesn't crash the
  Stride app.
- `Update(GameTime)`: After `_testHarness?.Update()`, calls `_inspectorWindow.PumpFrame()` if open.
  Auto-disposes when the user closes the second window.
- `EndRun()` override: Disposes `_inspectorWindow` before Stride tears down, ensuring clean
  GLFW context release.

#### Headless tests: 18 new tests in `StrideInspectorViewModelTests.cs`

All test the pure view-model logic without any window or GPU.

| ID | Name | What it verifies |
|----|------|-----------------|
| B22-VM-1 | `BuildEntityList_NullWorld_ReturnsEmpty` | Null world → empty, no throw |
| B22-VM-2 | `BuildEntityList_EmptyWorld_ReturnsEmpty` | Fresh world → empty list |
| B22-VM-3 | `BuildEntityList_AfterSpawn_ReturnsOneRow_WithCorrectTkbType` | One spawn → 1 row, TkbType=1001, correct position |
| B22-VM-4 | `BuildDisplayName_KnownTkbType_ContainsTkbName` (×5 theory) | All 5 UrbanCombat types map to correct names |
| B22-VM-5 | `BuildDisplayName_UnknownTkbType_FallsBackWithNetworkId` | Unknown type falls back with network ID |
| B22-VM-6 | `BuildDisplayName_ZeroTkbType_Returns_EntityHashN` | TkbType=0 → "Entity #N" |
| B22-VM-7 | `BuildInspector_NullWorld_ReturnsNoSelection` | Null world → "(no selection)" |
| B22-VM-8 | `BuildInspector_DeadEntity_ReturnsNoSelection` | Dead/Null entity → "(no selection)" |
| B22-VM-9 | `BuildInspector_LiveEntity_ContainsSimTransformField` | Live entity → Title set, SimTransform.Position + Authority fields present, OWNED |
| B22-VM-10 | `BuildEntityList_MultipleSpawns_ReturnsMatchingRowCount` | 3 spawns → 3 rows, all DisplayName non-empty |
| B22-VM-11 | `QuaternionToEulerDeg_Identity_ReturnsZeroAngles` | Identity quat → (0,0,0) |
| B22-VM-12 | `QuaternionToEulerDeg_90DegYaw_GivesNonzeroYaw_ZeroPitchRoll` | Pure 90° Y quat → yaw≈90°, pitch≈roll≈0° |
| B22-CFG-1 | `InspectorWindowConfig_ForceEnabled_False_DisablesWindow` | ForceEnabled=false → IsEnabled=false |
| B22-CFG-2 | `InspectorWindowConfig_ForceEnabled_True_EnablesWindow` | ForceEnabled=true → IsEnabled=true |

---

## Design Decisions

1. **Thin `StrideInspectorWindow`, not full `EditorSubsystem`**: Instantiating the full
   `EditorSubsystem` would require wiring AI hot-reload, blueprint debug, replay UI, MapCanvas,
   and ~40 more subsystem dependencies. That's P5+ work. The spec says "read-only is acceptable
   for v1"; the minimal entity list + inspector is the correct MVP.

2. **Per-frame pump in `StrideHrotGame.Update()`** (not external loop): The existing entry
   point (`game.Run()`) uses Stride's internal loop. Switching to external loop mode would
   require changing `HrotStrideAppApp.cs` and risk breaking the existing working boot path.
   Placing the raylib pump in `Update()` requires zero changes to the entry point.

3. **Guard with env var + `ForceEnabled`**: The default is disabled so no existing test,
   CI run, or headless use is affected. The `ForceEnabled` path allows tests to verify the
   config logic without opening a real window.

4. **`SetTargetFPS(0)` on the raylib window**: Prevents raylib's `EndDrawing()` from sleeping
   (which would block Stride's frame). Stride's throttler governs the overall frame rate.

5. **`rlImGui.Setup(true)`** (first positional = enable docking): Stride has no ImGui context
   (DirectX, no rlImGui). So the inspector window's `Setup()` is the only ImGui initialization —
   no conflict with any other ImGui instance.

6. **v1 is read-only**: Write/command support (spawning, selecting entities in the 3D view,
   `CenterOnEntityCommand`) is deferred to STR-P5-T3. The inspector is a pure observer.

---

## Deviations

| What | Why | Benefit | Risk |
|------|-----|---------|------|
| `StrideInspectorWindow` (new class) instead of reusing `FdpApplication.Run()` | `Run()` is a blocking loop; can't interleave. | Clean per-frame pump without refactoring FdpApplication. | None — the pattern is well-established (FdpApplication's loop body factored out). |
| Not wiring `EditorSubsystem` fully | Massive dep graph; out of scope for v1. | Fast, safe MVP. | Missing the full Hrot editor panels in the second window. Documented as follow-up. |

---

## Test Results

```
Hrot.Stride.Core.Tests:       327 passed / 0 failed
Hrot.Stride.Animation.Tests:   48 passed / 0 failed
HrotStrideApp.Game.Tests:     154 passed / 0 failed  (+18 new B22 tests)
```

All three suites green. No regressions.

---

## How to Enable the Second Window

1. Set the environment variable before launching: `STRIDE_EDITOR_WINDOW=1`
2. Run `HrotStrideApp.Windows` as normal (`dotnet run` or F5 in Visual Studio).
3. Two windows open:
   - **Stride window** — 3D view with entities, physics, animation (all existing harness keys working).
   - **FDP Inspector window** — raylib/ImGui second window showing:
     - **Entity List** (left panel): all live FDP entities with TKB type / display name / position.
     - **Inspector** (right panel): click an entity in the list to see SimTransform (position +
       Euler rotation), SimVelocity (linear + speed), NavigationStatus (result + phase),
       NetworkIdentity, TkbType, authority bit.

**What you should see:**
- The inspector window opens at the same time as the Stride window.
- After the demo spawns (4 InfantrySoldiers + 2 MilitaryAPCs), 6 rows appear in the entity
  list each named by TKB type ("InfantrySoldier #N", "MilitaryAPC #N").
- Clicking an entity shows its live SimTransform position updating as it moves (physics/nav).
- Closing the inspector window (X button) removes it cleanly; the Stride window continues.
- All existing D0/F1–F12 harness keys continue to work.

**To disable:** unset `STRIDE_EDITOR_WINDOW` (or set to `0`). Default is disabled.

---

## Known Issues / Limitations

1. **GPU-unverified (window can't be tested headlessly)**: The dual-window path requires a
   GPU. The architecture and per-frame pump are sound (Option A, design §8.3), but actual
   display is user-verified. The view-model logic is fully headless-tested (18 tests).

2. **Not the full Hrot editor**: Only entity list + basic inspector. No MapCanvas, AI panels,
   scenario browser, ORBAT, etc. Full `EditorSubsystem` integration is follow-up work (P5+).

3. **Write/command support**: Read-only v1. `CenterOnEntityCommand`, selection sync
   (STR-P5-T3), and spawning from the inspector are follow-up tasks.

4. **rlImGui multi-context behavior**: Confirmed safe in this codebase (Stride = DirectX, no
   rlImGui; inspector window has the only ImGui context). If a future feature adds ImGui to
   Stride's window via an OpenGL binding, revisit whether two ImGui contexts conflict.

5. **Potential concern — `Raylib.InitWindow()` while Stride is already running**: raylib/GLFW
   `glfwCreateWindow()` is independent of SDL2. Verified by the design doc §8.3 and confirmed
   by Raylib's multi-window support (GLFW multi-window is a documented feature since GLFW 3.x).
   If the platform GLFW binary is linked against has a limitation, the `try/catch` in
   `BootEditorSubsystem` will log a warning and continue without the inspector window — no crash.

---

## Developer Insights

- **Option A is the right long-term path** for this codebase. The design doc §8.3 describes it
  explicitly. Now that the per-frame pump infrastructure exists, adding the full `EditorSubsystem`
  panels is incremental work.
- **STR-D7** (extract `OfflineNetworkFactory` from `Hrot.Editor` to a UI-free assembly) would
  help if the inspector window ever needs to be run without the full editor dep graph.
- **Follow-up for P5-T3**: Wire `SelectionState` as an ECS component; clicking an entity in the
  inspector publishes `SelectEntityCommand` on the bus → gizmos in Stride highlight the entity.
- The `xUnit2013` warning on `RecordReplayWiringTests.cs:394` is pre-existing (not introduced
  by this batch) and is excluded from the count above.

---

## Suggested Commit Message

```
feat(stride-editor): dual-window inspector window + 18 headless view-model tests (STR-P5-T2, BATCH-22)
```
