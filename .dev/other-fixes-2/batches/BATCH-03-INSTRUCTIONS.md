# BATCH-03: Blueprint Editor Window Registration & Debug Panel Rendering

**Batch Number:** BATCH-03  
**Tasks:** FIX2-005, FIX2-006  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (FIX2-004 committed -- `BlueprintEditorModule` now wires session)

---

## Mandatory Workflow

**Read AGENTS.md at the repo root before writing a single line of code.**

Complete tasks in strict sequence. For each task:
1. Define the **success condition** BEFORE touching any code.
2. Implement the fix.
3. Write / update tests that drive the **production path**.
4. Run the relevant test project and confirm all tests pass.
5. Fix any failures before moving to the next task.

Do NOT ask for permission at any step. Do NOT stop early. Finish both tasks, make all tests green, then write the report.

---

## Onboarding & Workflow

### Required Reading (in order)
1. **Task details:** `.dev/other-fixes-2/TASK-DETAIL.md` -- sections FIX2-005, FIX2-006
2. **Source finding BPF-035:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-035
3. **Source finding BPF-034:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-034
4. **Editor Design Document (§8.2, §8.5, §8.7):** search for `Blueprint_Subsystem_Editor_Detailed_Design.md` under `Hrot/Subsystems/Blueprints/Docs/` or `docs/blueprints/`

### Source Code Areas
- **Window registrar:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintWindowRegistrar.cs`
- **Window registrar interface:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IBlueprintWindowRegistry.cs`
- **Engine IWindowRegistrar:** search for `IWindowRegistrar` interface definition in `Hrot/` or `FDP/`
- **DI service extensions:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorServiceCollectionExtensions.cs`
- **Debug panel windows:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/DebugPanelWindow.cs`, `WatchPanelWindow.cs`, `CallstackWindow.cs`
- **Debug session interface:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`
- **Test project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Build & Test
```
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

### Report Submission
Submit report to: `.dev/other-fixes-2/reports/BATCH-03-REPORT.md`

---

## Context

FIX2-005: `BlueprintWindowRegistrar` was created but uses its own local `IBlueprintWindowRegistry`, not the engine's `IWindowRegistrar.RegisterWindows(WindowManager)`. It is not added to DI and has no production caller. The engine orchestrator calls `IWindowRegistrar.RegisterWindows` on subsystems -- this must be implemented.

FIX2-006: The debug panel windows (`DebugPanelWindow`, `WatchPanelWindow`, `CallstackWindow`) call the debug session methods but discard results with `_ = x;` and render nothing. Additionally `CallstackWindow` calls `GetRecentNodeHistory()` instead of the design's `GetCurrentCallStack()`, which doesn't exist on the interface.

---

## Tasks

### Task 1 -- FIX2-005: Register blueprint editor windows via engine `IWindowRegistrar`

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-005`

**Success condition (define before coding):**
A test constructs a `BlueprintWindowRegistrar` (or the DI-resolved type), calls the engine `IWindowRegistrar.RegisterWindows(windowManager)`, and asserts all 7 blueprint windows are registered in the `WindowManager`. If you remove the `IWindowRegistrar` implementation, the test fails.

**What to fix:**
1. Make `BlueprintWindowRegistrar` implement the engine `IWindowRegistrar` interface (find the interface definition first -- it likely lives in the editor engine or `Fdp.ModuleHost`). The `RegisterWindows(WindowManager)` method should iterate the 7 blueprint windows and register each with the `WindowManager`.
2. Add `BlueprintWindowRegistrar` (or its DI-resolved equivalent) to the DI container in `BlueprintEditorServiceCollectionExtensions.cs` so the orchestrator can resolve and call it.
3. If the current local `IBlueprintWindowRegistry` is redundant after implementing `IWindowRegistrar`, remove it or keep it only if other code depends on it.

**Test required:**
- Test name: `BlueprintWindowRegistrar_RegistersAllSevenWindows_ViaEngineInterface` (or similar)
- Must: resolve the registrar through DI (or construct it directly), call `IWindowRegistrar.RegisterWindows(windowManager)`, and assert the window manager received 7 registrations.
- Must NOT: call a registration method directly; must go through the engine `IWindowRegistrar` interface.

---

### Task 2 -- FIX2-006: Implement debug panel rendering + add `GetCurrentCallStack()`

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-006`

**Success condition (define before coding):**
A test calls `DrawUI()` on each of the three panel windows while the session is in a paused state (breakpoint hit), and asserts that the rendered output (captured via a mock ImGui command sink or a render-command list) contains the expected data. Specifically:
- `DebugPanelWindow.DrawUI()` renders breakpoint info (node id, hit count).
- `WatchPanelWindow.DrawUI()` renders watch entries.
- `CallstackWindow.DrawUI()` renders call stack frames (not node history).

Additionally the NEW `GetCurrentCallStack()` method must exist on `IBlueprintDebugSession` and be called from `CallstackWindow.DrawUI()`.

**What to fix:**
1. Add `IReadOnlyList<CallFrame> GetCurrentCallStack()` to `IBlueprintDebugSession` and implement it in `BlueprintDebugSession` (per Editor DD §8.7 -- returns peer-call frame stack).
2. Replace `_ = x;` discards in all three panel windows with actual ImGui rendering calls (table rows, text labels, etc. -- use whatever headless/mockable ImGui abstraction is already in the codebase).
3. In `CallstackWindow`, replace the `GetRecentNodeHistory()` call with `GetCurrentCallStack()`.
4. Update tests in `DebugWindowsTests.cs` (or create new tests) that call `DrawUI()` and verify the panels render session state (not just that titles exist).

**Test required:**
- Test name pattern: `XxxPanel_DrawUI_RendersSessionData` for each of the three panels.
- Must: create a `BlueprintDebugSession`, set a breakpoint, fire a hit, call `DrawUI()`, and assert the rendered output includes the expected data.
- Must use headless ImGui or a render-command capture approach -- do NOT skip `DrawUI()` calls.

---

## Quality Standards

**PRODUCTION PATH:** Tests must call `DrawUI()` on the real window classes (not mock panels). The `GetCurrentCallStack()` method must be called from `CallstackWindow`, not directly from the test.

**ALL EXISTING TESTS (882) MUST STAY GREEN.**

---

## Developer Insights (Report Questions)

1. What engine `IWindowRegistrar` interface did you find, and where does it live?
2. What ImGui headless/mock approach did you use for the render-command capture tests?
3. What did you decide `GetCurrentCallStack()` should return in its implementation? (Frame struct definition, source of peer-call stack data, etc.)
4. Did you find any additional dead-code gaps while working on the panels?
5. **Suggested commit message** for this batch.
