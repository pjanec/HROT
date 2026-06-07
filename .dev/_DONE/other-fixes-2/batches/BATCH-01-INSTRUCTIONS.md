# BATCH-01: Blueprint Debug Probes, Session Wiring & Tick Integration

**Batch Number:** BATCH-01  
**Tasks:** FIX2-001, FIX2-003, FIX2-004  
**Priority:** CRITICAL / HIGH  
**Dependencies:** None

---

## Mandatory Workflow

**Read AGENTS.md at the repo root before writing a single line of code.**

Complete tasks in strict sequence. For each task:
1. Define the **success condition** (what observable behaviour proves it works) BEFORE touching any code.
2. Implement the fix.
3. Write / update tests that drive the **production path** (not internal helpers directly).
4. Run the relevant test project and confirm all tests pass.
5. Fix any failures before moving to the next task.

Do NOT ask for permission at any step. Do NOT stop early. Finish all three tasks, make all tests green, then write the report.

---

## Onboarding & Workflow

### Required Reading (in order)
1. **Task details (this batch):** `.dev/other-fixes-2/TASK-DETAIL.md` -- sections FIX2-001, FIX2-003, FIX2-004
2. **Source finding BPF-015:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-015
3. **Source finding BPF-003:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-003
4. **Source finding BPF-033:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-033
5. **Debug Design Document:** `Hrot/Subsystems/Blueprints/Docs/Debug-Design-Document.md` (or equivalent path -- search for the Debug DD under `Hrot/Subsystems/Blueprints/Docs/`)
6. **Editor Design Document:** `Hrot/Subsystems/Blueprints/Docs/Editor-Design-Document.md`

### Source Code Areas
- **Emitter (probe format):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints/Compiler/Emit/StatementEmitter.cs`
- **Debug session:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints/Debug/BlueprintDebugSession.cs`
- **Debug session interface:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints/Debug/IBlueprintDebugSession.cs`
- **Editor module:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorModule.cs`
- **Test project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Build & Test
```
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

### Report Submission
Submit report to: `.dev/other-fixes-2/reports/BATCH-01-REPORT.md`  
Questions (only if truly blocked): `.dev/other-fixes-2/questions/BATCH-01-QUESTIONS.md`

---

## Context

All three tasks share the same root cause: the Blueprint debug plumbing was *scaffolded* (the types/methods exist) but was never *wired* into production callers. As a result, breakpoints never fire at runtime even though the emitter now emits probe calls. This batch wires the three missing connections:

1. The node-id format emitted by probes doesn't match the format stored by the matcher (`:N` vs `:D`).
2. The per-frame dedup set in `BlueprintDebugSession` is never reset because `OnNewTick()` has no production caller.
3. `DebugProbe.Sink` is never pointed at the session because `BlueprintEditorModule` never calls `Attach()/Detach()`.

---

## Tasks

### Task 1 -- FIX2-001: Fix probe node-id format mismatch (`:N` -> `:D`)

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-001`

**Success condition (define before coding):**
A test compiles a blueprint, sets a breakpoint using the node's `Guid` (`:D` format), runs a tick, and asserts the breakpoint was hit (hit count incremented OR the probe fired). If you break the format back to `:N`, the test must fail.

**What to fix:**
- In `StatementEmitter.cs` find the `NodeEnter` / `PinValueChanged` emit calls (around line 300-308 based on the task detail).
- Change the format specifier from `{op.NodeId:N}` to `{op.NodeId:D}`.
- Confirm that `BlueprintDebugSession.SetBreakpoint` stores keys with `:D` format and `OnNodeEnter` does a `TryGetValue` with the same format (no normalization gap).

**Test required:**
- Test name: `Breakpoint_FiresWhenProbeEmitsMatchingNodeId` (or similar)
- Must: compile a minimal blueprint graph (use the existing test helpers / fluent builder), attach a debug session, set a breakpoint by the emitted node's Guid, call a method that drives a tick/execution, and assert the breakpoint hit count > 0.
- Must NOT: call `OnNodeEnter` directly with a pre-built string -- the test must go through the compiled+emitted probe call.

---

### Task 2 -- FIX2-003: Wire `OnNewTick()` to the production frame loop

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-003`

**Success condition (define before coding):**
A test ticks a blueprint-running system *twice* (two separate tick calls) and asserts the breakpoint fires on both ticks (hit count == 2 after two ticks). Without `OnNewTick()` being called between ticks the dedup set suppresses the second hit and the test fails.

**What to fix:**
- Find the production frame loop / tick coordinator / ECS system that advances blueprint execution. Per Debug DD §9.2 this is where `session.OnNewTick()` must be called before (or at) each tick boundary.
- Search for usages of `IBlueprintDebugSession` or `BlueprintDebugSession` in production code (not tests) and locate the tick advancement site.
- Add the `session.OnNewTick()` call there.

**Test required:**
- Test name: `Breakpoint_FiresOnEveryTick_AfterOnNewTickCalled` (or similar)
- Must: set a breakpoint, drive two ticks via the production path (not calling `OnNodeEnter` directly), assert hit count == 2.
- Must NOT: call `OnNewTick()` from the test itself (the test must verify that production code calls it).

---

### Task 3 -- FIX2-004: Wire `Attach()/Detach()` in `BlueprintEditorModule`

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-004`

**Success condition (define before coding):**
A test creates a `BlueprintEditorModule`, calls `OnEditorActivated()`, and asserts `DebugProbe.Sink` is set to the session. A subsequent `OnEditorDeactivated()` asserts `DebugProbe.Sink` is restored to `NullProbeSink`.

**What to fix:**
- In `BlueprintEditorModule.cs` the `OnEditorActivated`/`OnEditorDeactivated` callbacks do NOT call `Attach()`/`Detach()`.
- Add a `IBlueprintDebugSession` dependency (injected via constructor or property) to `BlueprintEditorModule`.
- Call `session.Attach()` in `OnEditorActivated` and `session.Detach()` in `OnEditorDeactivated`.
- If DI registration for the session is missing, add it to the service collection extensions.

**Test required:**
- Test name: `BlueprintEditorModule_Activate_AttachesSinkToSession` (or similar)
- Must: construct the module (with a real or mock session), call activate, assert `DebugProbe.Sink` is the session instance.
- Must: call deactivate, assert `DebugProbe.Sink` is `NullProbeSink` (or equivalent null sink).
- Must NOT: call `Attach()`/`Detach()` directly from the test (defeats the purpose).

---

## Quality Standards

**PRODUCTION PATH RULE:** Every test must drive the production code path. Tests that call internal helpers (e.g. `OnNodeEnter` directly with a crafted string, or `Attach()` directly without going through the module) do NOT count as fixing the defect.

**NO VACUOUS TESTS:** If you break the production wiring and the test still passes, it is a vacuous test. Re-examine.

**ALL EXISTING TESTS MUST STAY GREEN.** Run the full test suite before submitting.

---

## Developer Insights (Report Questions)

Answer in your report:
1. What obstacles did you encounter finding the production tick site for `OnNewTick()`? How did you resolve them?
2. What design decisions did you make for injecting the session into `BlueprintEditorModule`?
3. Did you discover any additional dead-code wiring gaps while fixing these three?
4. Any edge cases or scenarios not mentioned in the spec that you found?
5. **Suggested commit message** for this batch.
