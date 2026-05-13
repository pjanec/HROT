# BATCH-01: Core Infrastructure — Zero-CPU Headless + UI State Infrastructure

**Batch Number:** BATCH-01
**Tasks:** GZH-001, GZH-002, GZH-003, GZH-004, GZH-005, GZH-006, GZH-007, GZH-008
**Phase:** Phase 1 (Core Infrastructure) + Phase 2 (UI State Infrastructure)
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the foundational infrastructure for zero-CPU headless gizmo operation and the
UI state multiplexing layer. Work on tasks in order — each builds on the previous ones.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — how to work with batches
2. **Design Document:** `.dev/gizmos-2-headless/DESIGN.md` — full architectural design (read all of it)
3. **Task Details:** `.dev/gizmos-2-headless/TASK-DETAILS.md` — see GZH-001 through GZH-008 details
4. **Onboarding:** `.dev/gizmos-2-headless/ONBOARDING.md` — key component locations

### Source Code Locations

- **Primary FDP Toolkits work area:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/`
- **GlobalGizmoManager:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs`
- **DataDrivenGizmoSystem:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`
- **TogglablePostSimulationGroup:** `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs`
- **IGizmoUiStatePublisher:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoUiStatePublisher.cs`
- **GizmoUiState:** `FDP/ExtDeps/GizmoMap/GizmoMap.Network/Topics/GizmoUiState.cs`
- **Composition roots to modify:**
  - `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
  - `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
  - `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`
  - `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

### Build and Test Commands

```bat
cd FDP
dotnet build FDP.sln
dotnet test Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/gizmos-2-headless/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/gizmos-2-headless/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch establishes the two foundational pillars of the Gizmos-2 Headless design:

1. **Phase 1** (GZH-001 to GZH-005): The reference-counted execution gate that allows gizmo
   systems to run at zero CPU when no terminal is watching. The `GizmoExecutionController` acts as
   the central on/off switch, and the two gizmo managers get synchronous `CancelInteractiveTools()`
   methods so teardown is instant, not deferred through the event bus.

2. **Phase 2** (GZH-006 to GZH-008): The UI state multiplexing layer. `StructInspectorProjector<T>`
   hides the dual-channel architecture from gizmo authors. `GizmoUiStateHub` broadcasts JSON updates
   to all connected terminals simultaneously. `LocalGizmoUiStateTransport` provides the in-memory
   bridge from the hub to the local render loop.

Read DESIGN.md §2 and §3 for the complete picture before writing any code.

**Related Tasks:**
- [GZH-001](../TASK-DETAILS.md#gzh-001--terminalconnectedevent--terminaldisconnectedevent) — Lifecycle events
- [GZH-002](../TASK-DETAILS.md#gzh-002--gizmoexecutioncontroller) — Reference-counted execution gate
- [GZH-003](../TASK-DETAILS.md#gzh-003--wire-gizmo-systems-into-togglablepostsimulationgroup) — Composition root wiring
- [GZH-004](../TASK-DETAILS.md#gzh-004--add-cancelinteractivetools-to-globalgizmomanager) — GlobalGizmoManager cleanup
- [GZH-005](../TASK-DETAILS.md#gzh-005--add-cancelinteractivetools-to-datadrivengizmosystem) — DataDrivenGizmoSystem cleanup
- [GZH-006](../TASK-DETAILS.md#gzh-006--structinspectorprojectort) — StructInspectorProjector
- [GZH-007](../TASK-DETAILS.md#gzh-007--gizmouistatehub) — GizmoUiStateHub multiplexer
- [GZH-008](../TASK-DETAILS.md#gzh-008--localgizmouistatetransport) — In-memory UI state transport

---

## Batch Objectives

After this batch:
- Gizmo systems can be disabled at zero CPU when no terminal is watching (via `GizmoExecutionController`).
- Both gizmo managers can synchronously cancel all interactive tools when the last terminal disconnects.
- All four composition roots wrap their gizmo systems in `TogglablePostSimulationGroup` with the controller.
- Gizmo authors can use `StructInspectorProjector<T>` for live DTO sync in one line.
- The hub and transport infrastructure is in place for Phase 3 (dynamic terminal modules).

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **GZH-001:** Implement → Write tests → **ALL tests pass** ✅
2. **GZH-002:** Implement → Write tests → **ALL tests pass** ✅
3. **GZH-004:** Implement → Write tests → **ALL tests pass** ✅
4. **GZH-005:** Implement → Write tests → **ALL tests pass** ✅
5. **GZH-003:** Implement → Write integration tests → **ALL tests pass** ✅
6. **GZH-006:** Implement → Write tests → **ALL tests pass** ✅
7. **GZH-007:** Implement → Write tests → **ALL tests pass** ✅
8. **GZH-008:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- Current task implementation complete
- Current task tests written
- **ALL tests passing** (including all previous task tests)

Do not stop to ask permission for obvious steps like running tests, fixing compile errors, or fixing
test failures. Fix the root cause and keep going until everything is green. Only then write the
report.

---

## Tasks

### Task 1: GZH-001 — `TerminalConnectedEvent` / `TerminalDisconnectedEvent`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/TerminalLifecycleEvents.cs` (NEW FILE)

See: [TASK-DETAILS.md GZH-001](../TASK-DETAILS.md#gzh-001--terminalconnectedevent--terminaldisconnectedevent) and DESIGN.md §2.5

**Requirements:**
- Two sealed classes in namespace `Fdp.Toolkit.Diagnostics.Gizmos.Events`.
- `TerminalConnectedEvent` with `public long TerminalId { get; init; }`.
- `TerminalDisconnectedEvent` with `public long TerminalId { get; init; }`.

**Tests Required** (in `FDP/Toolkits/Fdp.Toolkits.Tests/`):
- `GZH001_1`: Create `FdpEventBus`, `PublishManaged<TerminalConnectedEvent>` with a specific ID,
  call `bus.SwapBuffers()`, then `bus.ReadManaged<TerminalConnectedEvent>()` and verify the event
  is present with the correct `TerminalId` value.

---

### Task 2: GZH-002 — `GizmoExecutionController`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoExecutionController.cs` (NEW FILE)

See: [TASK-DETAILS.md GZH-002](../TASK-DETAILS.md#gzh-002--gizmoexecutioncontroller) and DESIGN.md §2.4

**Requirements:**
- See TASK-DETAILS.md GZH-002 for exact constructor signature and method contracts.
- `AddListener()`: Interlocked.Increment; sets `group.Enabled = true` when count goes 0→1.
- `RemoveListener()`: Interlocked.Decrement; when count reaches 0: calls `CancelInteractiveTools()`
  on both managers, then sets `group.Enabled = false`.
- No FdpEventBus involvement. No pending/deferred flags.
- Expose `public int ListenerCount` for testing.

**Tests Required:**
- `GZH002_1`: Verify the reference counting and group enable/disable transitions as described in
  TASK-DETAILS.md GZH-002 success condition 1. Use real `TogglablePostSimulationGroup`; use
  mock or minimal stubs for the two managers (they need not call through to real systems).
- `GZH002_2`: Register a gizmo that requires exclusive focus in `GlobalGizmoManager`. Call
  `RemoveListener()` to 0. Verify: `OnCancel()` was invoked immediately (no tick needed),
  `ListenerCount == 0`, `group.Enabled == false`. See TASK-DETAILS.md GZH-002 success condition 2.
  Use a real `GlobalGizmoManager` with a minimal draw builder stub.

---

### Task 3: GZH-004 — `CancelInteractiveTools()` on `GlobalGizmoManager`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs` (MODIFY)

See: [TASK-DETAILS.md GZH-004](../TASK-DETAILS.md#gzh-004--add-cancelinteractivetools-to-globalgizmomanager) and DESIGN.md §6.1

**Requirements:**
- Add `public void CancelInteractiveTools()` exactly as specified in TASK-DETAILS.md GZH-004.
- Permanent gizmos (`RequiresExclusiveFocus == false && WantsRawInput == false`) must survive.
- On-demand gizmos (`RequiresExclusiveFocus == true || WantsRawInput == true`) must be disposed.
- The focused gizmo (if any) must have `OnCancel()`, `SetFocus(false)`, `Dispose()` called in order.
- No changes to `Execute()`. No event bus reads.

**Tests Required:**
- `GZH004_1`: Per TASK-DETAILS.md GZH-004 success condition. Register one permanent gizmo and one
  on-demand gizmo with focus granted to the on-demand one. Call `CancelInteractiveTools()`.
  Verify: on-demand gizmo had `OnCancel()` called and is no longer in the manager; permanent gizmo
  still registered (`ActiveCount == 1`).

---

### Task 4: GZH-005 — `CancelInteractiveTools()` on `DataDrivenGizmoSystem`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` (MODIFY)

See: [TASK-DETAILS.md GZH-005](../TASK-DETAILS.md#gzh-005--add-cancelinteractivetools-to-datadrivengizmosystem) and DESIGN.md §6.2

**Requirements:**
- Add `public void CancelInteractiveTools()` exactly as in TASK-DETAILS.md GZH-005.
- Must call `OnCancel()` and `Dispose()` on every injected gizmo, then clear `_injectedGizmos`.
- No changes to `Execute()`.

**Tests Required:**
- `GZH005_1`: Inject a gizmo for an entity via `ActivateGizmo()`. Call `CancelInteractiveTools()`.
  Verify `OnCancel()` was called on the gizmo and the injected gizmos collection is empty.

---

### Task 5: GZH-003 — Wire Gizmo Systems into `TogglablePostSimulationGroup`

**Files:** Modify the four composition roots listed above.

See: [TASK-DETAILS.md GZH-003](../TASK-DETAILS.md#gzh-003--wire-gizmo-systems-into-togglablepostsimulationgroup) and DESIGN.md §3

**Requirements:**
- In each composition root that uses gizmo systems, wrap `GlobalGizmoManager`,
  `DataDrivenGizmoSystem`, and `StatelessGizmoSystem` into a `TogglablePostSimulationGroup`
  named `"GizmoExecution"`.
- Create a `GizmoExecutionController` for each subsystem using the group and the two managers.
- Headless-first subsystems (SimHost, CGF): `gizmoGroup.Enabled = false` at startup.
- Interactive subsystems (IG, Editor): `gizmoGroup.Enabled = true` at startup (they always have a
  window).
- The existing gizmo systems must still be registered with the kernel — only now they are grouped
  in the `TogglablePostSimulationGroup` instead of individually.
- A `GizmoSystemModule` helper class may be introduced to simplify the registration call into a
  single `kernel.RegisterModule(new GizmoSystemModule(gizmoGroup))` if it doesn't already exist.
  If introducing this class, put it in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/`.
- Expose the `GizmoExecutionController` via a property on each subsystem (e.g. `internal
  GizmoExecutionController GizmoController { get; }`) so Phase 5 perspective switching can access it.

**Note on existing code:** Before modifying composition roots, read each file fully to understand
what currently exists. Some composition roots may already have partial setup; do not duplicate or
break existing initialisation.

**Tests Required:**
- Integration test `GZH003_1`: Construct `SimHostApp` (or a mock of its initialisation path) in
  a mode where no window is opened. Verify `DataDrivenGizmoSystem.Execute` is never called
  (counter tracks calls). Due to the complexity of full subsystem bootstrapping, a focused unit
  test that directly exercises `TogglablePostSimulationGroup.Enabled = false` and verifies the
  group's inner systems are not called is acceptable if full subsystem bootstrap is impractical.
- Integration test `GZH003_2`: With the group disabled, call `GizmoExecutionController.AddListener()`.
  Verify the group becomes enabled and the inner systems are now invoked per tick.

---

### Task 6: GZH-006 — `StructInspectorProjector<T>`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UI/StructInspectorProjector.cs` (NEW FILE)

See: [TASK-DETAILS.md GZH-006](../TASK-DETAILS.md#gzh-006--structinspectorprojectort) and DESIGN.md §2.1

**Requirements:**
- See TASK-DETAILS.md GZH-006 and DESIGN.md §2.1 for the full class contract.
- `EmitAndSync(draw, networkId, schemaHash, dto, anchor, sizeMode)`: always emits the
  `MakeStructInspector` primitive on the draw builder; only calls `uiPublisher.Publish` when the
  serialised JSON string differs from `_lastPublishedJson`.
- `ApplyUpdate(payloadJson, ref T dto)`: deserialises via `IComponentEditService.TryApply` (or
  equivalent deserialization path used elsewhere in the codebase — check how `OnStructUpdate` is
  handled in existing gizmos); updates `_lastPublishedJson` cache to prevent echo-back.
- When `uiPublisher` is `null`, `EmitAndSync` still emits the primitive but never allocates JSON.
- `T` is a reference type (`where T : class`).

**Tests Required:**
- `GZH006_1`: same DTO state, called twice → publisher gets exactly 1 `Publish` call.
- `GZH006_2`: second call with modified DTO → publisher gets a second `Publish` call.
- `GZH006_3`: after `ApplyUpdate` with valid JSON, `EmitAndSync` with the same DTO state does NOT
  trigger another `Publish` call (cache set by `ApplyUpdate` prevents echo).
- `GZH006_4`: `uiPublisher = null` → no exception; draw builder still receives the primitive.

---

### Task 7: GZH-007 — `GizmoUiStateHub`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/GizmoUiStateHub.cs` (NEW FILE)

See: [TASK-DETAILS.md GZH-007](../TASK-DETAILS.md#gzh-007--gizmouistatehub) and DESIGN.md §2.2

**Requirements:**
- Implements `IGizmoUiStatePublisher`.
- `AddEndpoint(IGizmoUiStatePublisher)` and `RemoveEndpoint(IGizmoUiStatePublisher)`.
- `Publish(GizmoUiState)` broadcasts to all registered endpoints under a lock.
- Thread-safe: concurrent `AddEndpoint`/`RemoveEndpoint` during `Publish` must not throw
  `InvalidOperationException` (use a snapshot copy inside the lock, or lock+copy pattern).

**Tests Required:**
- `GZH007_1` through `GZH007_4`: per TASK-DETAILS.md GZH-007 success conditions (zero endpoints,
  two endpoints receiving, remove then no call, concurrent modification safety).

---

### Task 8: GZH-008 — `LocalGizmoUiStateTransport`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/LocalGizmoUiStateTransport.cs` (NEW FILE)

See: [TASK-DETAILS.md GZH-008](../TASK-DETAILS.md#gzh-008--localgizmouistatetransport) and DESIGN.md §2.3

**Requirements:**
- Implements `IGizmoUiStatePublisher`.
- `Publish(GizmoUiState)`: overwrites entry in `ConcurrentDictionary<uint, GizmoUiState>` keyed
  by `GizmoInstanceId`. Last write wins for any given instance ID.
- `PollAndApply(Action<GizmoUiState> handler)`: iterates all entries, calls handler for each, then
  clears the dictionary. No double-delivery.

**Tests Required:**
- `GZH008_1` through `GZH008_3`: per TASK-DETAILS.md GZH-008 success conditions (overwrite same
  ID, two distinct IDs both delivered, empty after poll).

---

## Testing Requirements

- Minimum **16 unit tests** across the batch (the task-specific ones listed above).
- Tests must verify **actual behavior**: state transitions, method call counts, output values.
- Do not write tests that only verify object construction or property assignment.
- For mock gizmos, use either `Moq` (available in the test project) or hand-written minimal stubs.
- All tests must be in `FDP/Toolkits/Fdp.Toolkits.Tests/` under a folder named
  `Diagnostics/Gizmos/` matching the production namespace structure.

---

## Quality Standards

**TEST QUALITY EXPECTATIONS:**
- NOT ACCEPTABLE: Tests that only verify "can construct this object".
- NOT ACCEPTABLE: Tests that only check that no exception is thrown without asserting outcomes.
- REQUIRED: Tests that verify state changes (e.g. `group.Enabled`, `ListenerCount`, call tracking).
- REQUIRED: Tests that verify behavior under multiple calls (idempotency, counter progression).

**CODE QUALITY:**
- All new public types must compile cleanly with no warnings.
- Match the naming conventions and coding style of adjacent code in the same files.
- Do not add XML doc comments to new types in this batch unless they are interfaces (consistency
  with existing gizmo code style).

---

## Success Criteria

This batch is DONE when:
- [ ] GZH-001: `TerminalLifecycleEvents.cs` exists with both event classes; `GZH001_1` passes.
- [ ] GZH-002: `GizmoExecutionController.cs` exists; `GZH002_1` and `GZH002_2` pass.
- [ ] GZH-004: `CancelInteractiveTools()` added to `GlobalGizmoManager`; `GZH004_1` passes.
- [ ] GZH-005: `CancelInteractiveTools()` added to `DataDrivenGizmoSystem`; `GZH005_1` passes.
- [ ] GZH-003: All four composition roots updated; `GZH003_1` and `GZH003_2` pass.
- [ ] GZH-006: `StructInspectorProjector.cs` exists; `GZH006_1` through `GZH006_4` pass.
- [ ] GZH-007: `GizmoUiStateHub.cs` exists; `GZH007_1` through `GZH007_4` pass.
- [ ] GZH-008: `LocalGizmoUiStateTransport.cs` exists; `GZH008_1` through `GZH008_3` pass.
- [ ] Solution builds with no errors.
- [ ] All tests pass: `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
- [ ] Report submitted to `.dev/gizmos-2-headless/reports/BATCH-01-REPORT.md`.

---

## Common Pitfalls to Avoid

- **GZH-002**: Do NOT use the event bus for the teardown trigger. The `CancelInteractiveTools()`
  call must be synchronous inside `RemoveListener()`. See DESIGN.md §2.4 "Why not use the event
  bus for teardown?" for the exact reasoning.
- **GZH-003**: Read each composition root fully before modifying it. The gizmo systems may already
  be partially configured; avoid re-registering them or breaking existing wiring.
- **GZH-006**: The `ApplyUpdate` cache update is critical — without it, every `OnStructUpdate`
  triggers an immediate echo back to the terminal. See DESIGN.md §2.1 key invariants.
- **GZH-007**: The `Publish` method must NOT hold the lock while calling into endpoints (risk of
  deadlock). Copy the list under the lock, then iterate the copy.

---

## Developer Insights (Report)

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points or inconsistencies in the existing codebase? What would you
improve?

**Q3:** What design decisions did you make beyond what the spec required? What alternatives did
you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the task details?

**Q5:** What is your suggested commit message for this batch?

---

## Reference Materials

- **Task Details:** `.dev/gizmos-2-headless/TASK-DETAILS.md` — GZH-001 through GZH-008
- **Design:** `.dev/gizmos-2-headless/DESIGN.md` — §2.1–2.5, §3, §6.1–6.2
- **Existing gizmo systems:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/`
- **Existing tests for reference:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/`
- **IGizmoUiStatePublisher:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoUiStatePublisher.cs`
- **GizmoUiState:** `FDP/ExtDeps/GizmoMap/GizmoMap.Network/Topics/GizmoUiState.cs`
