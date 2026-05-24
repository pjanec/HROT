# Universal Breakpoints — Task Detail

**Reference:** Architecture and rationale live in [DESIGN.md](./DESIGN.md). This document specifies per-task work and success conditions (unit-test specifications). Use [TASK-TRACKER.md](./TASK-TRACKER.md) for status.

ID convention: `UBP-PnTm` — Phase n, Task m.

---

## Phase P0 — Foundation rename

### UBP-P0T1 — Rename `IBlueprintTimeController` → `IEngineDebugTimeController`

**Design:** [§4 Time-control surface](./DESIGN.md#4-time-control-surface-phase-p0)

**Work:**
- Add new interface `IEngineDebugTimeController` in `Fdp.ModuleHost.Time.Debug` (preferred) or `Hrot.Blueprints.Core.Debug` (acceptable if a clean move is non-trivial in one batch).
- Make existing `IBlueprintTimeController` inherit the new interface (no member additions); mark `[Obsolete]` with one-batch sunset note.
- Re-target `MasterSyncTimeControllerAdapter` to implement `IEngineDebugTimeController` directly; the `IBlueprintTimeController` alias inheritance preserves Slice 1 compilation.
- Update Slice 1 `BlueprintDebugSession` to take `IEngineDebugTimeController` in ctor.

**Success conditions (tests):**
- `IEngineDebugTimeController_Implements_PauseResumeStepContract` — pause toggles `IsPausedByDebugger`, resume clears it, step advances exactly one 60Hz tick (`MasterSyncController.GetTime()` delta ≈ 1/60).
- `IBlueprintTimeController_Still_Resolves_Through_Inheritance` — DI container resolving `IBlueprintTimeController` returns the same adapter instance; Slice 1 `BlueprintDebugSession` tests pass unchanged.

---

## Phase P1 — Snapshot orchestration

### UBP-P1T1 — `DebugSnapshotProvider` system

**Design:** [§5.2](./DESIGN.md#52-debugsnapshotprovider)

**Work:**
- Implement `DebugSnapshotProvider : IEcsModuleSystem` in `Hrot.Diagnostics.Breakpoints` (new project or existing `Hrot.Blueprints.Core.Debug`).
- Allocate `EntityRepository _preTickSnapshot` once at construction.
- Schedule in `SystemPhase.BeforeSync`.
- `volatile int _isEnabled`; `SetEnabled(bool)` flips atomically.

**Success conditions:**
- `DebugSnapshotProvider_GateOff_DoesNoWork` — with `_isEnabled = 0`, `Execute(view, dt)` does not call `SyncFrom` (verified via mocked repo recording calls).
- `DebugSnapshotProvider_GateOn_SyncsEveryTick` — with `_isEnabled = 1`, `Execute` calls `SyncFrom(view)` exactly once per call.
- `DebugSnapshotProvider_ZeroAllocationsHotPath` — `BenchmarkDotNet` test: with gate off, `Execute` reports 0 B/op and < 50 ns/op.

### UBP-P1T2 — `IDataBreakpointManager` skeleton + reference-counted gate

**Design:** [§5.3](./DESIGN.md#53-idatabreakpointmanager-reference-counted-gate), [§9](./DESIGN.md#9-manager-api-idatabreakpointmanager)

**Work:**
- Define interface `IDataBreakpointManager` with the full member list of §9 (Add/Remove/SetEnabled/UpdateCondition/StageMutation/RequestStep/RequestContinue/OnExternalHit + events + properties).
- Implement `DataBreakpointManager` (concrete) with empty compile pipeline stubs (will be filled in P2/P4/P5/P6). Active-count tracking + `_snapshotProvider.SetEnabled(...)` calls must work end-to-end.
- Allocate `_postTickSnapshot` once at construction.

**Success conditions:**
- `Manager_FirstBreakpointEnabled_MountsSnapshotProvider` — adding first enabled breakpoint flips `DebugSnapshotProvider._isEnabled` to 1.
- `Manager_LastBreakpointDisabled_UnmountsSnapshotProvider` — disabling the last enabled breakpoint flips it back to 0.
- `Manager_DisableThenReenable_KeepsCount` — toggling a single breakpoint via `SetEnabled` adjusts the count symmetrically.

### UBP-P1T3 — Triple-buffer pause primitives

**Design:** [§5.4](./DESIGN.md#54-on-demand-_posttick-snapshot), [§5.5](./DESIGN.md#55-clean-step-observation-only-fast-path)

**Work:**
- In `DataBreakpointManager`, implement `OnHit(Breakpoint, Entity)`:
  - capture `_postTickSnapshot.SyncFrom(_liveRepo)`,
  - rewind `_liveRepo.SyncFrom(_preTickSnapshot)`,
  - call `_timeController.RequestPause()`,
  - emit `OnBreakpointHit` event,
  - emit `OnPauseStateChanged`.
- Implement `RequestStep()` / `RequestContinue()` per §9.1 (clean step: restore `_postTickSnapshot`, advance one tick).

**Success conditions:**
- `Manager_OnHit_PerformsTripleBufferRewind` — pre-state stub, mid-tick state stub, fire hit; verify (a) `_postTickSnapshot` matches `_liveRepo` at fire time, (b) `_liveRepo` after the call equals `_preTickSnapshot`, (c) `_timeController.IsPausedByDebugger == true`.
- `Manager_CleanStep_RestoresPostTickThenAdvances` — after a hit, `RequestStep()` produces `_liveRepo == _postTickSnapshot` and advances the clock by exactly one tick.
- `Manager_CleanStep_NeverInjectsEvents` — assert that no `EventAccumulator` injection method is called between pause and step (test via mocked EventAccumulator).

---

## Phase P2 — Universal substrate

### UBP-P2T1 — `DataBreakpointSystem` (component-data path)

**Design:** [§6.3](./DESIGN.md#63-databreakpointsystem)

**Work:**
- Implement `DataBreakpointSystem : IEcsModuleSystem` scheduled in `SystemPhase.PostSimulation`.
- On Execute: iterate manager's mounted component predicates; for each, call `repo.QueryDelta(mandatoryComponents, action)` where action evaluates the compiled delegate; on `true`, signal `_manager.OnHit(bp, entity)`.
- Register relative ordering: must execute **before** the recorder's tick-system if both live in PostSimulation (use existing system-priority constants or `[ExecuteAfter(RecorderTickSystem)]` if available — see §14).

Wait — design states recorder runs **before** breakpoint evaluation so it captures natural tick-N state before rewind. Verify by reading the actual scheduling order and use `[ExecuteAfter(RecorderTickSystem)]` on `DataBreakpointSystem`.

**Success conditions:**
- `DataBreakpointSystem_NoBreakpoints_DoesNoWork` — empty manager, system Execute returns under 200 ns and 0 B/op (BenchmarkDotNet).
- `DataBreakpointSystem_PropertyMatchDto_FiresWhenConditionMet` — register `Health.Current < 10` breakpoint; mutate component to 5; assert `OnBreakpointHit` fires with the correct entity.
- `DataBreakpointSystem_FilterEntity_ScopesPredicateToOneEntity` — register breakpoint with `FilterEntity = e1`; mutate condition on e2; assert no hit.
- `DataBreakpointSystem_OccurrenceThreshold_PausesOnNthHit` — threshold 3, fire 5 times; assert pause engaged at hit 3, `HitCount == 3`.

### UBP-P2T2 — `DataBreakpointSystem` (event path)

**Design:** [§6.3](./DESIGN.md#63-databreakpointsystem)

**Work:** Add the event-scanner loop. For each mounted `TransientEventPredicateDto`-derived breakpoint, call its compiled `EventScannerDelegate(bus)`; on `true`, signal hit.

**Success conditions:**
- `Bus_AnyOccurrence_Predicate_FiresOnAnyEventOfType` — register `HitEvent` AnyOccurrence breakpoint; publish one `HitEvent`; assert hit.
- `Bus_PayloadConstraint_FiresOnlyWhenPayloadMatches` — register `HitEvent.Damage > 50`; publish two events (40, 80); assert single hit for the 80-event.

### UBP-P2T3 — Structural / Spatial / Lifecycle scanners

**Design:** [§6.8](./DESIGN.md#68-structural--spatial--lifecycle-paths)

**Work:** Implement the per-tick state-tracking machinery (mirror of replay-browser's structural / spatial / lifecycle scanners). State stored inside the manager keyed by `BreakpointId`.

**Success conditions:**
- `StructuralPredicate_FiresOnComponentAdded` — register `StructuralPredicateDto(WeaponState, Added)`; add component to entity; assert hit.
- `SpatialPredicate_FiresOnEntry_NotOnDwelling` — entity sits inside box for 3 ticks; assert exactly 1 hit (at the entry tick).
- `LifecyclePredicate_FiresOnBirth_AndOnDeath` — `LifecyclePredicateDto(NameSubstring, "EnemyTank")`; spawn matching entity → hit, destroy it → second hit.
- `AuthorityRequirement_RequireAuthority_FiltersGhostMutations` — structural breakpoint with `RequireAuthority`; add component to a ghost entity (non-authoritative); assert no hit.

---

## Phase P3 — Virtual snapshot UI swap

### UBP-P3T1 — `IEntityStatefulGizmo` signature change

**Design:** [§7.2](./DESIGN.md#72-ientitystatefulgizmo-signature-change)

**Work:**
- Change `IEntityStatefulGizmo.UpdateAndDraw(float, IDebugDrawBuilder)` → `UpdateAndDraw(ISimulationView, float, IDebugDrawBuilder)`.
- Strip cached `_view` / `_entity` fields from concrete gizmo constructors where they exist; keep `Entity` if it's a stable identity (it is), but no longer cache `ISimulationView`.
- Update `DataDrivenGizmoSystem` and `BehaviorGizmoManagerSystem` execution loops to obtain the active view from `IDataBreakpointManager.ActiveView` (or a fall-back to `repo` when no manager is registered).

**Success conditions:**
- `Gizmo_RendersAgainstActiveView_ReflectsPauseState` — pause the manager (forced via test seam), assert each gizmo's `UpdateAndDraw` received `_preTickSnapshot`; resume, assert received `_liveRepo`.
- All existing gizmo tests recompile + pass after one-line ctor / signature touch-ups (compile-only check + run).

### UBP-P3T2 — Inspector adapter view repointing

**Design:** [§7.1](./DESIGN.md#71-the-view-pointer-swap)

**Work:** `EntityInspectorPanel` (and any other `SimulationViewAdapter`-consuming widgets) reads its source from `IDataBreakpointManager.ActiveView`. When the manager is paused, all inspector widgets render the snapshot; on resume, live.

**Success conditions:**
- `Inspector_DuringPause_ShowsPreTickValues` — set Health=100 at tick N, system mutates it to 50 in tick N, predicate fires; assert inspector reads 100.
- `Inspector_AfterStep_ShowsPostTickValues` — same scenario; click Step; assert inspector reads 50.

### UBP-P3T3 — Temporal status banner

**Design:** [§7.3](./DESIGN.md#73-temporal-status-banner)

**Work:** Small global ImGui panel that renders when `IDataBreakpointManager.IsPaused == true`, showing the paused tick and pending-mutation count.

**Success conditions:**
- `Banner_HiddenWhenNotPaused` — manager not paused, banner widget not rendered (no draw calls captured).
- `Banner_ShowsTickAndCount_WhenPaused` — pause manager with `PausedTick=4502`, queue 2 mutations; assert text contains "Tick 4502" and "2 Pending Mutations".

---

## Phase P4 — Deferred mutation

### UBP-P4T1 — `PendingDebugMutation` envelope + `StageMutation` API

**Design:** [§8.1](./DESIGN.md#81-the-data-envelope), [§8.2](./DESIGN.md#82-staging-api)

**Work:**
- Add `PendingDebugMutation` struct (readonly).
- Implement `DataBreakpointManager.StageMutation(...)`: resolve `ComponentTypeId` via `ComponentTypeRegistry`, classify managed vs. unmanaged via reflection (or registry metadata), capture `Marshal.SizeOf` for unmanaged, enqueue.

**Success conditions:**
- `Stage_UnmangedStruct_StoresSizeAndClassification` — stage a `Health` (unmanaged struct), assert `IsManaged == false`, `SizeBytes == Marshal.SizeOf<Health>()`.
- `Stage_ManagedRef_StoresClassificationOnly` — stage an `EntityInfo` (managed), assert `IsManaged == true`, `SizeBytes == 0`.

### UBP-P4T2 — `StructEdit` commit interception

**Design:** [§8.2](./DESIGN.md#82-staging-api)

**Work:** Where `StructEdit`'s commit pipeline writes the boxed component to the repo, add a check: if `_manager.IsPaused`, route to `_manager.StageMutation(...)` instead.

**Success conditions:**
- `StructEdit_WhilePaused_RoutesToStageMutation` — pause the manager; commit an edit through `IEditSession.Commit()`; assert `_manager.PendingMutationsCount == 1` and the entity's live component is unchanged.
- `StructEdit_WhileRunning_StillWritesDirect` — manager not paused; commit edit; assert live component updates and `PendingMutationsCount == 0`.

### UBP-P4T3 — ECB drain pipeline

**Design:** [§8.3](./DESIGN.md#83-drain-on-step--continue), [§8.4](./DESIGN.md#84-full-sequence)

**Work:** Implement `DrainPendingMutations(EntityRepository)` exactly per §8.3, hooked into `RequestStep` and `RequestContinue` after the `_postTickSnapshot` restore.

**Success conditions:**
- `Drain_UnmanagedPayload_PinnedAndCopiedToECB` — stage a `Health{Current=999}` mutation; click Step; advance one tick; assert the live `Health.Current == 999`.
- `Drain_ManagedPayload_RoutedViaSetManagedRaw` — stage an `EntityInfo` change; step; assert live managed component reference updated.
- `Drain_AppliesAtN_Plus_1_BoundaryNotN` — pause at tick N, stage mutation, step; assert the mutation is visible at tick N+1 but tick N's recorded delta contains the **original** value (verified by reading the recorded delta).

---

## Phase P5 — Trace-buffer integration (BTree / HSM)

### UBP-P5T1 — Compiler extension for trace buffer scans

**Design:** [§6.4](./DESIGN.md#64-trace-buffer-execution-breakpoints-btree--hsm)

**Work:** Extend `IPredicateCompiler` to recognise `PropertyMatchDto.ComponentType ∈ {BTreeTraceWorkingMemory1024, HsmTraceWorkingMemory1024}` and emit IL that loops `i = 0 .. RecordCount` with 16-byte stride pointer arithmetic, evaluating `(OpCode, NodeIndex/StateIndex, Status/Result/TriggerEventId)` fields.

A small new path syntax is acceptable, e.g. `Records[*].OpCode==NodeEvaluated AND Records[*].NodeIndex==3 AND Records[*].Status==Running` — implemented as a custom DTO sub-shape or a new `PropertyPath` extension recognised by the compiler.

**Success conditions:**
- `Compile_TraceBufferScan_ReturnsTrueWhenAnyRecordMatches` — populate a `BTreeTraceWorkingMemory1024` with 3 records; one matches OpCode=NodeEvaluated/NodeIndex=5/Status=Running; compiled predicate returns true.
- `Compile_TraceBufferScan_ReturnsFalseWhenNoRecordMatches` — populate with 3 non-matching records; predicate returns false.
- `Compile_TraceBufferScan_ZeroAllocations` — BenchmarkDotNet: 0 B/op evaluating against a full 63-record buffer.

### UBP-P5T2 — BTree breakpoints end-to-end

**Design:** [§6.4](./DESIGN.md#64-trace-buffer-execution-breakpoints-btree--hsm)

**Work:** Wire the compiler extension into the `DataBreakpointSystem` evaluation path (no system code change — same predicate evaluation).

**Success conditions:**
- `BTree_BreakOnActivation_FiresWhenNodeEntersRunning` — register a synthetic breakpoint scanning for `NodeEvaluated`/`NodeIndex=N`/`Status=Running`; trace buffer writes that record; assert hit.
- `BTree_BreakOnAbort_FiresOnScopePopped` — scope-popped record present; assert hit.

### UBP-P5T3 — HSM breakpoints end-to-end

Same shape as P5T2 but targeting `HsmTraceWorkingMemory1024` records (`StateEnter`, `StateExit`, `Transition`, `GuardEvaluated`).

**Success conditions:**
- `HSM_BreakOnEnter_FiresOnStateEnter` — `StateEnter` record present for the target state; assert hit.
- `HSM_BreakOnTransition_MatchesTriggerEventId` — `Transition` record with `TriggerEventId=42`; predicate keyed on 42; assert hit.

---

## Phase P6 — Blueprint variable integration

### UBP-P6T1 — `BlueprintVariablePredicateDto` + JSON registration

**Design:** [§6.5](./DESIGN.md#65-blueprint-variable-breakpoints)

**Work:** Add the DTO subclass in `Fdp.Toolkit/ReplayBrowser/Search/SearchPredicateDto.cs`. Add `[JsonDerivedType(typeof(BlueprintVariablePredicateDto), "BlueprintVariable")]` on the base.

**Success conditions:**
- `BlueprintVariablePredicate_SerializesRoundTrip` — JSON serialize → deserialize preserves all fields including nested `Predicate`.

### UBP-P6T2 — Slot-table-aware IL emission

**Design:** [§6.5](./DESIGN.md#65-blueprint-variable-breakpoints)

**Work:** Extend `IPredicateCompiler` with a branch handling `BlueprintVariablePredicateDto`. Emit IL that probes the three tier components, calls `BlueprintBlackboardPartitions.TryGetSlotOffset`, short-circuits on miss, otherwise reads `memory + payloadOffset + fieldOffset` (where `fieldOffset` is baked from `BlueprintDefinition.StateFields[VariableName].OffsetBytes` at compile time), casts via `Unsafe.AsRef<T>` to the field type.

**Success conditions:**
- `Compile_BlueprintVariable_NoSlotPresent_ReturnsFalse` — entity has the tier component but no instance of the target blueprint; predicate returns false.
- `Compile_BlueprintVariable_SlotPresent_EvaluatesField` — entity has the blueprint instance with `AmmoCount=0`; predicate `AmmoCount == 0` returns true.
- `Compile_BlueprintVariable_TierUpgrade_StillWorks` — instance starts in 1024 tier, upgrade to 4096; predicate continues to evaluate correctly (delegate re-runs slot lookup each call).

---

## Phase P7 — Graph-editor synthesis

### UBP-P7T1 — BTree context menu

**Design:** [§13.3](./DESIGN.md#133-graph-editor-context-menus-phase-p7)

**Work:** Extend BTree graph editor's `IContextMenuBuilder` integration with:
- `Add Breakpoint > Break on Activation (Enter)` → synthesize predicate, push to manager.
- `Add Breakpoint > Break on Completion (Exit)` → synthesize.
- `Add Breakpoint > Break on Interruption (Abort)` → synthesize.
- `Add Conditional Data Breakpoint...` → synthesize `CompoundPredicateDto[And]` with `[EditReadOnly]` Branch A (trace-buffer scan) + empty Branch B (`BehaviorParamPredicateDto`/`PropertyMatchDto`), open Details Inspector.

Auto-synthesised breakpoints carry `SourceElementId = node.VisualId` so the gutter renderer can locate them.

**Success conditions:**
- `BTreeContextMenu_AddBreakOnActivation_RegistersWithManager` — invoke menu action programmatically; assert manager contains a breakpoint with the synthesised condition shape.
- `BTreeContextMenu_AddConditional_OpensDetailsInspectorWithEditReadOnlyA` — invoke; assert the inspector session is opened, Branch A fields are `EditReadOnly`, Branch B is editable.
- `BTreeGutterRenderer_ReadsManagerForBreakpoints` — register breakpoint with `SourceElementId = X`; render canvas; assert red gutter glyph drawn at the node with `VisualId == X`.

### UBP-P7T2 — HSM context menu

Same pattern as P7T1 over states / transitions / guards, synthesising `HsmTraceWorkingMemory1024` scans.

**Success conditions:** mirror UBP-P7T1 with HSM-specific opcodes.

### UBP-P7T3 — Blueprint context menu integration

**Design:** [§6.6](./DESIGN.md#66-blueprint-node-execution-breakpoints-slice-1-surface), [§13.3](./DESIGN.md#133-graph-editor-context-menus-phase-p7)

**Work:**
- Existing `Add Breakpoint` in Blueprint canvas continues to register a Slice 1 probe-driven breakpoint via `BlueprintDebugSession.AddBreakpoint(nodeId)` — unchanged path.
- New `Add Conditional Data Breakpoint...` synthesises a `CompoundPredicateDto[And]` whose Branch A is a *probe-tag* condition (see UBP-P7T4 below for the bridge type) and Branch B is an empty `BlueprintVariablePredicateDto`.
- Wire `BlueprintDebugSession` so that, when its probe-driven node BP fires, it routes the hit through `IDataBreakpointManager.OnExternalHit(tag, entity)` to trigger the triple-buffer rewind.

**Success conditions:**
- `Blueprint_NodeBP_RoutesToManager_TripleBufferRewindApplied` — register node BP via Slice 1 path; let probe fire; assert `_liveRepo` was rewound to `_preTickSnapshot` and inspector shows pre-execution state.
- `Blueprint_AddConditional_SynthesizesCompoundWithReadOnlyA` — invoke menu; inspector opens with read-only probe-tag branch and editable variable branch.

### UBP-P7T4 — Probe-tag predicate bridge

**Design:** [§6.6](./DESIGN.md#66-blueprint-node-execution-breakpoints-slice-1-surface), [§9 `OnExternalHit`](./DESIGN.md#9-manager-api-idatabreakpointmanager)

**Work:** Add a lightweight `ExternalHitTagPredicateDto` (or equivalent — a synthetic DTO that the compiler treats as a *false* predicate but the manager pairs with an external-hit tag). When the orchestrator receives `OnExternalHit(tag, entity)`, it scans all enabled breakpoints whose `Condition` (or a nested `Compound[And]` child) carries this tag with matching value; if found, it OR-evaluates the remaining children of the compound against the entity and triggers `OnHit` if they pass.

This is the only mechanism by which Slice 1 Blueprint probes plug into the otherwise data-driven substrate; documented as such.

**Success conditions:**
- `ExternalHitTag_Standalone_TriggersOnTagMatch` — register a breakpoint with `ExternalHitTagPredicateDto{Tag="BP:node-guid"}`; call `OnExternalHit("BP:node-guid", e)`; assert hit.
- `ExternalHitTag_InCompoundAnd_EvaluatesRemainingChildrenAgainstEntity` — register compound: `[ExternalHitTag(BP:X), BlueprintVariable(Ammo==0)]`; fire probe with entity having Ammo=0 → hit; fire probe with entity having Ammo=5 → no hit.

---

## Phase P8 — Manager UI

### UBP-P8T1 — Data Breakpoint Manager window shell

**Design:** [§13.1](./DESIGN.md#131-data-breakpoint-manager-window)

**Work:** New per-perspective window (`WindowScope.PerspectiveBound`) showing the data grid: Enabled checkbox, Target Scope, Type, Condition Summary, Hits. Toolbar with Add/Remove/EnableAll/DisableAll/JSON.

**Success conditions:**
- `ManagerWindow_PerspectiveBound_RegistersInWindowManager` — register window in SimHost perspective; switch to CGF perspective; assert window not displayed; switch back; assert displayed.
- `ManagerWindow_AddRow_AppendsBreakpointToManager` — click Add → opens empty Predicate Builder; commit → manager contains the new breakpoint with the entered condition.
- `ManagerWindow_EnableCheckbox_TogglesManagerSetEnabled` — toggle row checkbox; assert `_manager.AllBreakpoints[id].Enabled` flips and active-count gate updates.

### UBP-P8T2 — Predicate Builder (StructEdit host)

**Design:** [§13.2](./DESIGN.md#132-predicate-builder-details-inspector)

**Work:** Right-side / popup panel hosting an `IEditSession`. Mode dropdown (Component / Event / Behavior Param / Compound / Structural / Spatial / Lifecycle / Blueprint Variable) discards and re-opens the session against the appropriate root DTO. Specialized drawers (`PropertyPathFieldDrawer`, `BehaviorHashFieldDrawer`, `BoundingBoxFieldDrawer`, new `BlueprintPickerDrawer`) used as specified.

**Success conditions:**
- `PredicateBuilder_SwitchingMode_DiscardsAndOpensNewSession` — start in Component mode, switch to Behavior Param; assert the inspector rebinds to a `BehaviorParamPredicateDto` (with all fields blank).
- `PredicateBuilder_PropertyPathPicker_OnlyAllowsValidPaths` — select `Health` component; assert path dropdown contains exactly the paths reflectable from `Health`'s unmanaged layout.
- `PredicateBuilder_CompileAndApply_RemountsDelegate` — edit a working breakpoint's threshold; click Apply; assert compiled delegate changed (test seam exposes pointer hash).

### UBP-P8T3 — JSON clipboard

**Work:** "Copy to Clipboard" / "Paste from Clipboard" buttons (reuses the ReplaySearchPanel preset serializer).

**Success conditions:**
- `JSON_CopyPaste_RoundTrip_PreservesAllFields` — copy a Compound breakpoint with mixed children; paste; assert structural and value equality.

### UBP-P8T4 — Temporal status banner integration

(Already covered in UBP-P3T3; this task simply wires the banner into the manager window's bottom strip and the global editor frame.)

---

## Phase P9 — Resilience polish

### UBP-P9T1 — Hot-reload auto-rebind

**Design:** [§12.1](./DESIGN.md#121-auto-rebind-on-onreloadcompleted)

**Work:** Subscribe `DataBreakpointManager` to `AiHotReloadCoordinator.OnReloadCompleted`. For affected breakpoints: drop cached delegate, recompile from retained DTO, mount on success, flag `IsBroken` on failure.

**Success conditions:**
- `HotReload_StructureCompatible_PreservesBreakpoint` — register `PropertyMatchDto(Health.Current<10)`; reload an unrelated assembly; assert breakpoint still mounted and still fires correctly.
- `HotReload_RemovesTargetedField_MarksBreakpointBroken` — register breakpoint targeting field X; reload an assembly that removed X; assert breakpoint marked `IsBroken == true`, not crashed.
- `HotReload_NoAccessViolation_DuringActiveBreakpoint` — fuzz: 100 rebuild cycles with random small struct edits while 5 breakpoints are armed; assert no access violations.

### UBP-P9T2 — "Step abandoned" preemption

**Design:** [§12.2](./DESIGN.md#122-step-abandoned-preemption-on-onhotreloadbegin)

**Work:** Subscribe to `OnHotReloadBegin`. If `IsPaused`: force `RequestContinue()`, flush `_pendingDebugMutations`, emit `IEditorIndicators.Notify("Step abandoned due to reload")`, flag watches `IsStale = true`. On `OnReloadCompleted`, re-validate watches and clear `IsStale` for structurally matching ones.

**Success conditions:**
- `HotReloadBegin_DuringPause_ForcesContinueAndFlushesMutations` — pause, stage 3 mutations, trigger hot reload; assert `_pendingMutations.Count == 0`, `IsPaused == false`, time advances.
- `Notification_StepAbandoned_Emitted` — same scenario; assert toast service received the message.

### UBP-P9T3 — Watch persistence (`watches.json`)

**Design:** [§12.3](./DESIGN.md#123-watch-persistence-watchesjson)

**Work:** Distinguish `Breakpoint.IsWatch` (or a separate `Watch` collection — design choice; recommend a separate collection for cleaner UI semantics). Serialize the watch collection to `<editor-data>/watches.json` on shutdown + on manual Save; deserialize on init with `StructureHash`-style validation.

**Success conditions:**
- `Watches_PersistAcrossRestart_StructureCompatible` — register 3 watches, shut down editor, restart; assert 3 watches restored with identical conditions.
- `Watches_Restore_FailsGracefullyOnDriftedSchema` — write a watches.json then mutate the targeted component's layout; restart; assert the affected watches are flagged invalid, not loaded into the active set, and not crashing the editor.

---

## Cross-phase / integration tests

### UBP-INT1 — End-to-end Universal Breakpoint flow

**Scope:** Full simulation, no mocks. Headless test harness running CGF subsystem with a small scenario.

**Success conditions:**
- `E2E_PropertyMatchBreakpoint_PausesAndStepsCleanly` — start simulation, register `Health < 10`, run until hit, assert pause engaged at correct tick, inspector reads pre-tick state, click Step, simulation advances, no recorder corruption.
- `E2E_CompoundBTreeAndBlackboard_FiresOnlyWhenBothConditionsMet` — register Compound[And: BTree-node-running, AmmoCount==0]; run scenario where node enters with Ammo=5 (no hit), then with Ammo=0 (hit).
- `E2E_DeferredMutation_AppliedAtNplus1` — pause, stage Health=1000, step; assert next-tick Health=1000.

### UBP-INT2 — Performance budget integration

**Success conditions:**
- `Perf_HeavyScenario_NoBreakpoints_ZeroOverhead` — 5000-entity scenario, 60 Hz, no breakpoints; assert `DebugSnapshotProvider` + `DataBreakpointSystem` together cost < 50 µs per tick (effectively zero).
- `Perf_HeavyScenario_OneActiveBreakpoint_FitsBudget` — same scenario with one armed `Health < 10` breakpoint; assert total breakpoint-related cost < 3 ms per tick (snapshot ~2 ms + scan).

### UBP-INT3 — Flight Recorder invariance

**Success conditions:**
- `Recorder_PausedSession_ProducesLinearFdp` — record a scenario, pause partway, step several times with deferred mutations, resume, finish; load the `.fdp` and assert (a) monotonic frame indices, (b) no duplicates, (c) the deferred mutations appear as standard ECB deltas at the expected ticks.
