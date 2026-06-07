# BATCH-05: Editor Windows + Runtime + Test Harness

**Batch Number:** BATCH-05  
**Tasks:** BPF-031, BPF-032, BPF-033, BPF-034, BPF-035, BPF-006, BPF-007, BPF-008, BPF-009  
**Source:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`  
**Tracker:** `.dev/blueprint-fixes-1/TASK-TRACKER.md`  
**Priority:** HIGH -- BPF-033 (IsAttached hardcoded), BPF-031/035 (editor windows unreachable), BPF-034 (DrawUI stubs)  
**Dependencies:** BATCH-04 (done)

---

## Onboarding & Workflow

This batch covers three focused areas:

1. **Editor Windows** (BPF-031..035): `HotReloadLogWindow` event subscription, `BlueprintDebugSession.IsAttached`, empty `DrawUI()` stubs, `IWindowRegistrar` wiring -- all editor windows are currently never registered/drawn in production.
2. **Runtime** (BPF-006, BPF-007): `IReloadLogSink` interface shape, `BlueprintRegistry.GetAll()` tuple shape.
3. **Test Harness** (BPF-008, BPF-009): Missing fixture helpers and `InvokeHsmAction`/`InvokeHsmGuard` stubs.

Work in order: editor windows first (highest priority), then runtime, then test harness.

### Required Reading (IN ORDER)
1. **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-031, BPF-032, BPF-033, BPF-034, BPF-035, BPF-006, BPF-007, BPF-008, BPF-009
2. **Editor DD:** `.dev/blueprints-1/Blueprint_Subsystem_Editor_Detailed_Design.md` (§3.1-§3.2, §9.3)
3. **Runtime DD:** `.dev/blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design.md` (§2.2, §2.3, §9.7)
4. **Test Harness DD:** `.dev/blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design.md` (§2.4, §5.4-5.7, §12.1-12.3)
5. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
6. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Codebase Memory MCP (MANDATORY)
Use `mcp_codebase-memo_list_projects` then `mcp_codebase-memo_get_architecture`. Find symbols with `mcp_codebase-memo_search_graph`.

---

## MANDATORY WORKFLOW (per task, in order)

For **each task**:
1. **Define success condition** before implementing
2. **Implement the fix**
3. **Write tests** -- behavioral verification
4. **Run all tests** -- ALL must pass
5. **Fix failures at root cause**
6. Only then move to next task

---

## Editor Window Tasks

### Task 1: BPF-031 -- HotReloadLogWindow never subscribed

**Task Definition:** [BPF-031](../TASK-DETAIL.md#bpf-031----hotreloadlogwindow-never-subscribed-to-coordinator-events---permanently-empty-at-runtime-editor)

**Design:** Editor DD §9.3 -- `HotReloadLogWindow` subscribes to `IAiHotReloadCoordinator.OnReloadCompleted` during construction.

**Success Condition:** After constructing `HotReloadLogWindow` with a coordinator, firing `coordinator.OnReloadCompleted(info)` must cause the window to receive the event. Tests must verify via coordinator (not direct call).

**What to do:**
1. Read `HotReloadLogWindow.cs` and `BlueprintEditorModule.cs`
2. In `HotReloadLogWindow` constructor (or `Initialize` method), subscribe to `coordinator.OnReloadCompleted`
3. Ensure the subscription is properly removed on dispose
4. Write a test: create coordinator + window; fire event via coordinator; assert window received it

**Tests Required:**
- Firing coordinator event reaches the window handler (not direct method call)
- Window disposed before event fires does not throw

---

### Task 2: BPF-032 -- HotReloadLogWindow tests call methods directly

**Task Definition:** [BPF-032](../TASK-DETAIL.md#bpf-032----hotreloadlogwindow-tests-call-methods-directly---subscription-contract-untested-editor)

**Success Condition:** After BPF-031 is fixed, update existing tests to test via coordinator subscription (not direct `window.OnReloadCompleted(info)` calls). The subscription contract must be exercised.

**What to do:**
1. After BPF-031 is done, update `HotReloadLogWindowTests` to use the coordinator-based path

---

### Task 3: BPF-033 -- BlueprintDebugSession.IsAttached hardcoded true

**Task Definition:** [BPF-033](../TASK-DETAIL.md#bpf-033----blueprintdebugsessionisattached-hardcoded-true-no-attach-editor-never-routes-debugprobesink-editor)

**Design:** Debug DD §7.1 -- `Attach()` sets `DebugProbe.Sink = session`; `IsAttached` returns a tracked bool field; `OnEditorActivated/Deactivated` calls Attach/Detach.

**Success Condition:** After `Attach()` is called, `IsAttached == true` and `DebugProbe.Sink` points to the session. After `Detach()`, `IsAttached == false` and `DebugProbe.Sink` is null. Tests must verify this directly.

**What to do:**
1. Read `BlueprintDebugSession.cs` -- `IsAttached` is currently `=> true` (no field)
2. Add `bool _isAttached` field; change `IsAttached => _isAttached`
3. Add `Attach()` setting `_isAttached = true` and routing `DebugProbe.Sink = this`
4. Add `Detach()` setting `_isAttached = false` and clearing `DebugProbe.Sink = null`
5. Wire `OnEditorActivated` to call `Attach()` and `OnEditorDeactivated` to call `Detach()`
6. Write tests: `IsAttached` false before Attach, true after, false after Detach; `DebugProbe.Sink` routes correctly

**Tests Required:**
- `IsAttached` false before `Attach()`
- `IsAttached` true after `Attach()`; `DebugProbe.Sink == session`
- `IsAttached` false after `Detach()`; `DebugProbe.Sink == null`

---

### Task 4: BPF-034 -- Debug/Watch/Callstack DrawUI() are empty stubs

**Task Definition:** [BPF-034](../TASK-DETAIL.md#bpf-034----debugwatchcallstack-window-drawui-bodies-are-empty-stubs-editor)

**Design:** Editor DD -- Debug window renders breakpoint list; Watch window renders watched pins; Callstack window renders call stack.

**Success Condition:** Each `DrawUI()` must call into the debug session to retrieve and render data (not be empty). The bodies must at minimum iterate the data and produce ImGui calls. Tests can assert the method completes without throwing and queries the session.

**What to do:**
1. Find `DebugWindow.DrawUI()`, `WatchWindow.DrawUI()`, `CallstackWindow.DrawUI()`
2. Read the Editor DD design for each window
3. Implement per-design (render breakpoint list, watched pin values, callstack respectively)
4. Write a test for each: verify `DrawUI()` queries the session for data (use a stub session)

**Tests Required:**
- `DebugWindow.DrawUI()` iterates breakpoints from session
- `WatchWindow.DrawUI()` iterates watches from session
- `CallstackWindow.DrawUI()` queries callstack from session

---

### Task 5: BPF-035 -- IWindowRegistrar contract mismatch; windows never registered

**Task Definition:** [BPF-035](../TASK-DETAIL.md#bpf-035----iwindowregistrar-contract-mismatch-blueprintwindowregistrardi-registration-absent-windows-never-registered-editor)

**Design:** Editor DD §3.1, §3.2 -- `BlueprintWindowRegistrar` implements the engine `IWindowRegistrar` pattern; `AddBlueprintEditor` is called from DI; all 8 windows are registered.

**Success Condition:** All blueprint editor windows are registered via a `BlueprintWindowRegistrar` that matches the engine's `IWindowRegistrar` contract. Tests must verify `RegisterWindows` adds the expected window entries.

**What to do:**
1. Read `IWindowRegistrar.cs` and `BlueprintEditorModule.cs`
2. Fix the local `IWindowRegistrar` interface to match the engine's contract (or add a `BlueprintWindowRegistrar` that bridges to the engine)
3. Wire `AddBlueprintEditor` / `RegisterWindows` so windows are actually registered in DI
4. Write a test: call `RegisterWindows`; assert all expected windows appear in the registered set

**Tests Required:**
- `RegisterWindows` registers all expected blueprint editor windows

---

## Runtime Tasks

### Task 6: BPF-006 -- IReloadLogSink interface reduced

**Task Definition:** [BPF-006](../TASK-DETAIL.md#bpf-006----ireloadlogsink-interface-reduced-vs-design-no-onsoftreload-no-entityhash-context)

**Design:** Runtime DD §9.7 -- `IReloadLogSink` has `OnSoftReload(int blueprintId, Entity entity, ulong structureHash)` AND `OnHardReset(int blueprintId, Entity entity, ulong oldHash, ulong newHash)`.

**Success Condition:** `IReloadLogSink` exposes both `OnSoftReload` and `OnHardReset` with the designed signatures including `Entity` and hash context. All existing callers still compile.

**What to do:**
1. Read `IReloadLogSink.cs` and all callers
2. Add `OnSoftReload(int, Entity, ulong)` method
3. Update `OnHardReset` to add `Entity entity, ulong oldHash, ulong newHash` parameters
4. Update all callers accordingly
5. Write a test: create a spy sink; trigger soft reload + hard reset; assert both methods called with correct args

**Tests Required:**
- Spy sink receives `OnSoftReload` with correct entity and hash
- Spy sink receives `OnHardReset` with correct old/new hashes

---

### Task 7: BPF-007 -- BlueprintRegistry.GetAll() drops (Id, Def) tuple

**Task Definition:** [BPF-007](../TASK-DETAIL.md#bpf-007----blueprintregistrygetall-drops-the-id-def-tuple)

**Design:** Runtime DD §2.2/§2.3 -- `GetAll()` returns `IEnumerable<(int Id, BlueprintDefinition Def)>`.

**Success Condition:** `GetAll()` returns tuples containing both `Id` and `Def`. Tests must verify a registered definition can be retrieved with its Id via `GetAll()`.

**What to do:**
1. Read `BlueprintRegistry.GetAll()` -- currently returns values only
2. Change return type to `IEnumerable<(int Id, BlueprintDefinition Def)>`
3. Update callers
4. Write a test: register a definition; call `GetAll()`; assert the tuple Id matches the registered Id

**Tests Required:**
- `GetAll()` returns tuple with correct `Id` for registered definition

---

## Test Harness Tasks

### Task 8: BPF-008 -- Fixture missing SnapshotAllBlackboards / SetChannelStatus / GetSlotEntry

**Task Definition:** [BPF-008](../TASK-DETAIL.md#bpf-008----fixture-missing-snapshotallblackboards-setchannelstatust-getslotentry)

**Design:** Test Harness DD §2.4, §5.4-5.7 -- `BlueprintTestFixture` exposes `SnapshotAllBlackboards()`, `SetChannelStatus<T>(Entity, ChannelStatus)`, and `GetSlotEntry(Entity, slotId)`.

**Success Condition:** All three methods exist on the fixture with the designed signatures. Tests must exercise each method with a real fixture instance.

**What to do:**
1. Read the Test Harness DD sections
2. Read `BlueprintTestFixture` -- find what is missing
3. Add the three methods per their designed signatures
4. Write a test for each: exercise via fixture, assert expected behavior

**Tests Required:**
- `SnapshotAllBlackboards()` returns non-empty snapshot for a running entity
- `SetChannelStatus<T>` changes the channel status for the given entity
- `GetSlotEntry` returns the correct slot entry

---

### Task 9: BPF-009 -- InvokeHsmAction / InvokeHsmGuard are stubs

**Task Definition:** [BPF-009](../TASK-DETAIL.md#bpf-009----invokehsmaction--invokehsmguard-remain-notimplementedexception-stubs)

**Design:** Test Harness DD §12.1-12.3 -- `InvokeHsmAction(entity, actionName)` and `InvokeHsmGuard(entity, guardName)` invoke the action/guard directly on the entity's HSM state.

**Success Condition:** Both methods execute the named action/guard and return a result (not throw `NotImplementedException`). Tests must verify the action/guard runs and the return value is correct.

**What to do:**
1. Read the Test Harness DD for HSM invocation contract
2. Implement `InvokeHsmAction` and `InvokeHsmGuard` per design
3. Write a test: create an entity with a known HSM; call `InvokeHsmAction("MyAction")`; assert the action ran

**Tests Required:**
- `InvokeHsmAction` executes the named action and returns expected result
- `InvokeHsmGuard` evaluates the named guard and returns expected bool

---

## Quality Standards

- **NOT ACCEPTABLE:** `Assert.True(IsAttached)` -- must test the full lifecycle: false before, true after Attach, false after Detach
- **REQUIRED:** Every test uses real seam (coordinator for BPF-031/032, sink for BPF-006)
- All tests must exercise actual wiring, not mock everything

## Report

Write report to:
`d:\WORK\IOS-IG-SimHost-FDP\.dev\blueprint-fixes-1\reports\BATCH-05-REPORT.md`

## Success Criteria

This batch is DONE when:
- [ ] BPF-031: HotReloadLogWindow subscribes via coordinator; test passes
- [ ] BPF-032: Tests updated to use coordinator path
- [ ] BPF-033: IsAttached backed by field; Attach/Detach implemented; DebugProbe.Sink routed; tests pass
- [ ] BPF-034: DrawUI() bodies render data from session; tests pass
- [ ] BPF-035: BlueprintWindowRegistrar wires all windows; test passes
- [ ] BPF-006: IReloadLogSink has both methods with correct signatures; tests pass
- [ ] BPF-007: GetAll() returns (Id, Def) tuples; test passes
- [ ] BPF-008: Fixture has SnapshotAllBlackboards/SetChannelStatus/GetSlotEntry; tests pass
- [ ] BPF-009: InvokeHsmAction/InvokeHsmGuard implemented; tests pass
- [ ] All pre-existing tests still pass
- [ ] Report submitted

## Reference Materials
- **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`
- **Editor DD:** `.dev/blueprints-1/Blueprint_Subsystem_Editor_Detailed_Design.md`
- **Runtime DD:** `.dev/blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design.md`
- **Test Harness DD:** `.dev/blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design.md`
- **BATCH-04 Review:** `.dev/blueprint-fixes-1/reviews/BATCH-04-REVIEW.md`
