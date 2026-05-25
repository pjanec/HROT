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

---

## Phase P10 — Production integration

**Goal:** Wire the library (manager, system, snapshot provider, UI, hot-reload, watches) into real subsystem hosts so the feature is reachable from the running editor. This phase addresses gap-analysis items G1–G9. None of these tasks add new logic; they construct, register, and inject existing pieces in the correct subsystem startup paths.

The intended host scope per the design's Brain/Muscle decomposition:

| Subsystem | Hosts manager? | Rationale |
|---|---|---|
| `EditorSubsystem` | **yes** | Single-node debugging is the supported workflow (DESIGN §11.2). |
| `CgfSubsystem` (Brain) | **yes** | Owns `BrainBlackboard`, `BehaviorState`, `BTreeTraceWorkingMemory1024`, `HsmTraceWorkingMemory1024`, and all `BlueprintBlackboard*` components. |
| `SimHostSubsystem` (Muscle) | **deferred** | Cognitive predicates would skip every chunk; only useful for spatial/lifecycle/structural BPs over kinematics. Add later if requested. |
| `IgSubsystem`, `ExConSubsystem` | **no** | No simulation memory worth pausing on. |

### UBP-P10T1 — Editor subsystem wiring

**Design:** [§5](./DESIGN.md#5-triple-buffer-snapshot-architecture-phase-p1), [§11.1](./DESIGN.md#111-per-subsystem-isolation)

**Work:**
- In `Hrot.Editor.EditorSubsystem` (and `EditorApplication` as appropriate), at subsystem init:
  - Allocate `_preTickSnapshot = new EntityRepository()` and call `RegisterAllSimComponents(...)` to mirror the live repo's schema.
  - Construct `var snapshotProvider = new DebugSnapshotProvider(_preTickSnapshot)`.
  - Construct `var manager = new DataBreakpointManager(liveRepo, _preTickSnapshot, snapshotProvider, engineDebugTimeController, predicateCompiler, eventScannerCompiler, notifier)`.
  - Construct `var bpSystem = new DataBreakpointSystem(manager, eventBus)`.
  - Register both systems with the subsystem's `ModuleHostKernel` (snapshot provider in `BeforeSync`, breakpoint system in `PostSimulation` — both via `[UpdateInPhase]` attributes already on the types).
  - Expose the manager via the subsystem's DI/service registry under both `IDataBreakpointManager` and the concrete type (the latter is needed for `IActiveViewProvider` + `IMutationInterceptor` consumers — see P10T4/T5).
- Hold the manager in a field so the editor frontend (window registration, hot-reload subscription, watches lifecycle) can reach it.

**Success conditions:**
- `EditorSubsystem_Init_RegistersManager` — boot the editor subsystem in the integration-test harness; assert `subsystem.Services.GetRequiredService<IDataBreakpointManager>()` returns a non-null, ready-to-use manager.
- `EditorSubsystem_Init_RegistersBreakpointSystems` — assert both `DebugSnapshotProvider` and `DataBreakpointSystem` appear in the kernel's registered system list under their expected phases.
- `EditorSubsystem_Boot_NoExtraCost_WhenNoBreakpoints` — boot, run 100 ticks without registering any BP; assert `_preTickSnapshot` is empty (gate never opened).

### UBP-P10T2 — CGF subsystem wiring

Same shape as P10T1 but inside `Hrot.CGF.CgfSubsystem`. Required because deep AI debugging in non-editor topologies (headless test runner, CGF-only diagnostic builds) still wants universal breakpoints.

**Success conditions:**
- `CgfSubsystem_Init_RegistersManager` — mirror of P10T1's first assertion against CGF.
- `CgfSubsystem_HeavyScenario_NoBreakpoints_ZeroOverhead` — re-run UBP-INT2's heavy scenario against the *wired* CGF subsystem; assert snapshot + system together cost < 50 µs/tick.

### UBP-P10T3 — Register `DataBreakpointManagerWindow` per perspective

**Design:** [§11.4](./DESIGN.md#114-window-scope-presentation), [§13.1](./DESIGN.md#131-data-breakpoint-manager-window)

**Work:**
- In the editor's UI bootstrap (where other `WindowScope.PerspectiveBound` windows like `FdpEntityInspectorWindow` get registered), construct a `DataBreakpointManagerPanel(manager, ..., bannerState)` and a `DataBreakpointManagerWindow(id, owningPerspective, panel, titleBarColor)` per perspective ("Editor", "CGF", and whichever others hold a manager).
- Register the windows with the engine `WindowManager`.
- Add a menu entry or default keybind to open the window (mirror the pattern used by the entity inspector).

**Success conditions:**
- `ManagerWindow_RegisteredInEditorPerspective` — boot editor; assert `WindowManager.GetWindows(perspective: "Editor")` contains a `DataBreakpointManagerWindow`.
- `ManagerWindow_NotShownInUnrelatedPerspective` — switch to a perspective where no manager exists (e.g. "IG"); assert the window is not present.
- `ManagerWindow_OpensOnMenuCommand` — invoke the registered menu action; assert `IsOpen == true`.

### UBP-P10T4 — Inject `IActiveViewProvider` into gizmo systems

**Design:** [§7](./DESIGN.md#7-virtual-snapshot--ui-rendering-during-pause-phase-p3)

**Work:** Find every construction site of `DataDrivenGizmoSystem`, `BehaviorGizmoManagerSystem`, and `GlobalGizmoManager` in editor / CGF / SimHost startup. For subsystems that own a manager, pass `manager` (cast to `IActiveViewProvider`) into the optional `breakpointManager:` ctor parameter (it currently defaults to null in every production caller).

**Success conditions:**
- `Gizmo_System_UsesManagerActiveView_WhenPaused` — boot editor with a registered manager, fire a BP to engage pause, then run one frame; assert each gizmo system's `Execute` passed `manager.ActiveView` (= `_preTickSnapshot`) into `UpdateAndDraw`, not the live `view`.
- `Gizmo_System_FallsBackWhenNoManager` — boot a subsystem without a manager (e.g. IG); assert no NRE; assert active view falls back to `view`.

### UBP-P10T5 — Inject `IMutationInterceptor` into `ComponentEditWindow`

**Design:** [§8.2](./DESIGN.md#82-staging-api)

**Work:** Find every `new ComponentEditWindow(...)` construction site in editor + CGF UI startup; pass the manager (cast to `IMutationInterceptor`) into the optional `interceptor:` ctor parameter.

**Success conditions:**
- `Inspector_EditWhilePaused_RoutesToStageMutation` — boot editor with manager wired; engage pause; commit an edit through `IEditSession.Commit()`; assert `manager.PendingMutationsCount == 1` and the live component is unchanged.
- `Inspector_EditWhileRunning_StillDirectWrites` — same setup without pausing; assert live component updates and `PendingMutationsCount == 0`.

### UBP-P10T6 — Wire `BlueprintDebugSession` ↔ manager bridge

**Design:** [§6.6](./DESIGN.md#66-blueprint-node-execution-breakpoints-slice-1-surface)

**Work:** Where `BlueprintDebugSession` is constructed in production (currently in `Hrot.Blueprints.Editor` bootstrap), follow the construction with `session.SetDataBreakpointManager(manager)`. Both must come from the same subsystem's service container.

**Success conditions:**
- `Blueprint_NodeBP_RoutesThroughManager_TripleBufferApplied` — boot editor with both wired; register a Slice 1 node BP; let `OnNodeEnter` fire; assert `_liveRepo` was rewound to `_preTickSnapshot` and `manager.IsPaused == true` (proving the bridge engaged), not the legacy direct-pause path.

### UBP-P10T7 — BTree canvas: invoke menu populator + wire gutter renderer

**Design:** [§13.3](./DESIGN.md#133-graph-editor-context-menus-phase-p7), [§7](./DESIGN.md#7-virtual-snapshot--ui-rendering-during-pause-phase-p3)

**Work:**
- In the BTree graph canvas right-click handler (where other context-menu items are added), call `BTreeBreakpointMenuPopulator.PopulateNodeMenu(nodeVisualId, builder, manager, onOpenConditionalInspector: openInspector)`.
- Where the canvas constructs/renders `BTreeBreakpointGutterRenderer`, call `renderer.SetManager(manager)` once the manager is available.

**Success conditions:**
- `BTree_ContextMenu_ShowsBreakpointItems_WhenManagerWired` — open canvas, right-click a node; assert the menu contains "Break on Activation", "Break on Completion", "Break on Interruption", "Add Conditional Data Breakpoint…".
- `BTree_GutterRenderer_DrawsDotForRegisteredBP` — register a BP with `SourceElementId = node.VisualId`; assert the canvas render produces the red gutter glyph at the node.

### UBP-P10T8 — HSM canvas: invoke menu populator + wire gutter renderer

Same shape as P10T7 but in the HSM editor (`HsmBreakpointMenuPopulator.PopulateNodeMenu`, `HsmBreakpointGutterRenderer.SetManager`).

**Success conditions:** mirror UBP-P10T7's two assertions with HSM-specific opcodes (State, Transition, Guard).

### UBP-P10T9 — Blueprint canvas: invoke menu populator

Same shape as P10T7 for the Blueprint graph editor, calling `BlueprintBreakpointMenuPopulator.PopulateNodeMenu(nodeId, assetId, builder, manager, onOpenConditionalInspector)`.

The Blueprint canvas already has its own breakpoint gutter (via Slice 1 `BlueprintDebugSession.Breakpoints`); no separate "SetManager" call is required for the gutter — it already reads from the session, which is now bridged to the manager via UBP-P10T6.

**Success conditions:**
- `Blueprint_ContextMenu_ShowsConditionalBreakpointItem` — open canvas, right-click a node; assert the menu contains "Add Conditional Data Breakpoint…".
- `Blueprint_ConditionalBPSelection_OpensPredicateBuilderForBranchB` — invoke the menu action; assert the manager window's Predicate Builder opens with the compound DTO loaded and Branch B (the variable predicate) editable.

### UBP-P10T10 — Subscribe manager to `AiHotReloadCoordinator`

**Design:** [§12.1](./DESIGN.md#121-auto-rebind-on-onreloadcompleted), [§12.2](./DESIGN.md#122-step-abandoned-preemption-on-onhotreloadbegin)

**Work:** Where `AiHotReloadCoordinator` raises `OnReloadBegin` / `OnReloadCompleted` (and `BlueprintDebugSession` already subscribes), add a second subscription that forwards to `manager.OnHotReloadBegin()` / `manager.OnHotReloadCompleted()`. Order: the manager's `OnHotReloadBegin` should run *before* the assembly swap (to flush pending mutations safely); `OnHotReloadCompleted` should run *after* the new assemblies are loaded (so recompilation hits the new layout).

**Success conditions:**
- `HotReload_WhilePaused_FlushesPendingAndContinues` — boot editor with manager wired; pause via a BP; stage 2 mutations; trigger a hot reload; assert `PendingMutationsCount == 0`, `IsPaused == false`, toast service received "Step abandoned due to reload".
- `HotReload_RebindsCompiledDelegates` — register a `PropertyMatchDto` BP; trigger a structure-compatible reload; assert the BP is still mounted, not `IsBroken`, and still fires.
- `HotReload_StructuralBreak_MarksBPIsBroken_NoCrash` — register a BP targeting a field; reload an assembly that removes the field; assert no AV, BP is `IsBroken`, manager still healthy.

### UBP-P10T11 — Watches save/load editor lifecycle integration

**Design:** [§12.3](./DESIGN.md#123-watch-persistence-watchesjson)

**Work:**
- On editor shutdown (and on manual "Save Layout" if such a command exists), call `manager.SaveWatches(Path.Combine(editorDataDir, "watches.json"))`.
- On editor init (after the manager is constructed and before the UI shows), call `manager.LoadWatches(...)` if the file exists. Catch and log; never let a malformed file block boot.

**Success conditions:**
- `Watches_RoundTripAcrossEditorRestart` — boot editor, mark 3 BPs as watches, shut down, restart; assert the 3 watches are restored with identical conditions and reach the watch panel.
- `Watches_Restore_FailsGracefullyOnDriftedSchema` — corrupt the targeted component's layout between save and load; assert the affected watches are flagged invalid, retained in the watch panel as broken, and editor still boots.

---

## Phase P11 — Hot-path & correctness hardening

**Goal:** Fix the implementation deviations identified in gap analysis G10–G24. None of these change the public contract; they tighten the implementation so the design's success conditions actually hold in practice.

### UBP-P11T1 — Zero-allocation `DataBreakpointSystem.Execute`

**Gap:** G10. `Execute` currently allocates a `List<Entity>` and a delegate closure **per breakpoint per tick**, violating Success Condition #1.

**Work:**
- Replace the per-breakpoint `var pendingHits = new List<Entity>()` with a single reusable field-level buffer on `DataBreakpointSystem` (cleared per breakpoint).
- Replace the lambda passed to `QueryDelta` with a struct-typed callback / cached delegate. Two options:
  - Option A: cache one `Action<Entity>` per breakpoint (allocated at mount, not per tick) that captures by ref the system's reusable state.
  - Option B (preferred if the API allows): add a `QueryDelta` overload taking a struct callback (`IQueryDeltaVisitor` style) and adapt `DataBreakpointSystem` to use it.
- Either way: the steady-state allocation per `Execute` must be 0 B/op when ≥1 BP is mounted.

**Success conditions:**
- `DataBreakpointSystem_Steady_ZeroAllocations` — BenchmarkDotNet: 5 mounted BPs, 5000-entity scenario, `Execute` reports `0 B/op` after warmup.
- `DataBreakpointSystem_StillFiresHits_AfterRefactor` — re-run UBP-P2T1's `DataBreakpointSystem_PropertyMatchDto_FiresWhenConditionMet` and assert it still passes.

### UBP-P11T2 — Chunk-version-aware `QueryDelta` scanning

**Gap:** G10 second bullet. `Execute` passes `sinceVersion = 0u`, defeating the whole point of `QueryDelta` (scans every entity every tick). DESIGN §6.3 / §6.7 promise sub-linear scaling with mutation delta.

**Work:**
- Track `uint LastScanVersion` per mounted predicate (a field on `CompiledComponentPredicate` or in a parallel array on the manager).
- Pass that value as `sinceVersion` to `QueryDelta`.
- After the scan completes, update `LastScanVersion` to the current repo version (`repo.GlobalVersion` or per-chunk version, whichever the engine exposes for this purpose).
- On hot-reload recompile (P10T10's `OnHotReloadCompleted`), reset `LastScanVersion = 0u` so the new delegate scans everything once.

**Success conditions:**
- `DataBreakpointSystem_SkipsUnchangedChunks` — 5000 entities, mutate only 1 entity's targeted component; assert the predicate delegate is invoked exactly once that tick (instrument the delegate with a counter).
- `Perf_HeavyScenario_OneBreakpoint_FitsBudget` — re-run UBP-INT2's second assertion; cost must now be ≪ 3 ms/tick when only a handful of entities mutate.

### UBP-P11T3 — Enforce `DataBreakpointSystem` ordering after `RecorderTickSystem`

**Gap:** G11. DESIGN §14 mandates the recorder serializes the natural tick-N state *before* the manager rewinds. The current `[UpdateInPhase(SystemPhase.PostSimulation)]` does not enforce relative order.

**Work:**
- Add the engine's relative-ordering attribute (e.g. `[ExecuteAfter(typeof(RecorderTickSystem))]`, exact name to match the engine's convention) to `DataBreakpointSystem`.
- If no such attribute exists in the engine, use whichever mechanism the engine offers (system priority constant, registration order in `RegisterSystems`, or a dedicated `SystemPhase.PostSimulationLate` sub-phase).
- Document the constraint in `DataBreakpointSystem`'s class docstring so future maintainers don't reorder.

**Success conditions:**
- `RecorderRunsBeforeBreakpointSystem_InKernel` — boot subsystem with both systems registered; inspect kernel system order; assert recorder index < breakpoint-system index in the `PostSimulation` execution list.
- `Recorder_PausedSession_ProducesLinearFdp` — re-run UBP-INT3 in the *wired* subsystem (not the manual ordering of the existing test). Assert recorded `.fdp` has linear frame indices.

### UBP-P11T4 — `OnHit` re-entrancy guard

**Gap:** G12. When `EvaluateStatefulBreakpoints` produces multiple hits in one tick, the second `OnHit` overwrites `_postTickSnapshot` with the already-rewound `_liveRepo`, destroying the post-tick state.

**Work:** At the very top of `DataBreakpointManager.OnHit`:

```csharp
if (_isPaused) return;          // already paused: ignore further hits this tick
```

The first hit wins; later same-tick hits are silently dropped. This is the correct behavior because the manager can only pause once per tick boundary; the second hit will naturally re-evaluate next tick when the simulation resumes.

Optionally: queue the additional hit entities into a "pending hit list" surfaced to the UI so the developer can see "5 other breakpoints also fired this tick", but the pause itself must engage exactly once.

**Success conditions:**
- `OnHit_SecondHitInSameTick_DoesNotOverwritePostTickSnapshot` — pause via BP A; with `_isPaused = true`, call `OnHit(bp_b, e)`; assert `_postTickSnapshot` still equals the live state at the moment of BP A's hit (not the rewound state).
- `EvaluateStatefulBreakpoints_MultipleHits_PausesOnce` — register 3 structural BPs all firing this tick; assert exactly one `RequestPause()` issued, exactly one `OnPauseStateChanged(true)` emitted.

### UBP-P11T5 — `PausedTick` uses `GlobalTime.TotalWallTicks`

**Gap:** G13. DESIGN §11.3 specifies wall-tick capture for replay-browser alignment; impl stores `_preTickSnapshot.GlobalVersion` (a repo monotonic counter).

**Work:**
- In `OnHit`, read `GlobalTime` from the live repo singleton (or pre-tick snapshot — same value at this point) and store `globalTime.TotalWallTicks` (a `long`) in a new field.
- Change `IDataBreakpointManager.PausedTick` from `uint` to `long`; update consumers (`TemporalStatusBannerState`, `DataBreakpointManagerPanel`, tests).
- Reset to `0L` on resume.

**Success conditions:**
- `PausedTick_ReflectsGlobalTimeTotalWallTicks` — set `GlobalTime.TotalWallTicks = 0xABCDEF`; trigger BP; assert `manager.PausedTick == 0xABCDEF`.
- `BannerShowsWallClockTickNotVersionCounter` — UI test: pause, capture banner string; assert it matches the wall-clock value, not the repo version.

### UBP-P11T6 — `OnExternalHit` fallback removal

**Gap:** G15. The current implementation engages the triple-buffer rewind and pauses **even when no breakpoint matched the tag**.

**Work:**
- Delete the `if (!anyFired && !_isPaused) { ... pause ... }` fallback block in `OnExternalHit` ([DataBreakpointManager.cs lines ~564–574](../../Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs)).
- Slice 1 already guarantees the probe only fires when a Blueprint breakpoint exists at that node, so a matching tag should always be present if the bridge is wired correctly. If no match is found, the call is a no-op.

**Success conditions:**
- `OnExternalHit_NoTagMatch_DoesNotPause` — call `manager.OnExternalHit("nonexistent-tag", entity)`; assert `IsPaused == false`, no `OnPauseStateChanged` fired.
- `OnExternalHit_TagMatch_StillPausesAndRewinds` — register `ExternalHitTagPredicateDto{Tag="X"}`; call `OnExternalHit("X", e)`; assert pause engaged and triple-buffer rewind happened.

### UBP-P11T7 — Predicate Builder respects `ReadOnlyChildIndices`

**Gap:** G16. Menu populators populate `CompoundPredicateDto.ReadOnlyChildIndices`, but `DataBreakpointManagerPanel` does not consult it.

**Work:** In the Predicate Builder rendering code, when iterating compound conditions, check whether the current child index is in `ReadOnlyChildIndices`; if so, render the StructEdit fields as `EditReadOnly` (mirror the `[EditReadOnly]` metadata path used elsewhere in StructEdit).

**Success conditions:**
- `PredicateBuilder_ReadOnlyChildBranch_RendersAsLocked` — load a compound DTO with `ReadOnlyChildIndices = [0]`; render the builder; assert Branch A controls are disabled / non-editable; assert Branch B controls are editable.
- `PredicateBuilder_NoReadOnlyChildren_AllEditable` — load a plain compound DTO; assert all controls editable.

### UBP-P11T8 — `StageMutation` size resolution via ECS registry

**Gap:** G17. `Marshal.SizeOf(componentType)` can differ from the engine's chunk stride for components with `fixed` buffers.

**Work:** Replace `Marshal.SizeOf(componentType)` in `DataBreakpointManager.StageMutation` with the engine's authoritative size lookup. Investigate `ComponentTypeRegistry.GetSize(typeId)` or `Unsafe.SizeOf<T>` via cached generic resolution; pick whichever matches the ECS chunk-table's stride.

**Success conditions:**
- `StageMutation_FixedBufferComponent_StoresCorrectSize` — stage an edit to a component containing `fixed byte Buffer[1016]` (`BTreeTraceWorkingMemory1024` or `Blackboard1024`); assert the stored `SizeBytes` matches the chunk stride; click Step; assert the bytes land correctly at the next tick (the test should also assert no buffer overrun / no truncation).
- Existing `Stage_UnmanagedStruct_StoresSizeAndClassification` still passes for simple POD components.

### UBP-P11T9 — Eliminate `Mounted*` accessor allocations

**Gap:** G19. Both `MountedComponentPredicates` and `MountedEventScanners` allocate fresh `List<...>` on every access; `DataBreakpointSystem.Execute` calls them every tick.

**Work:**
- Replace the property-style accessors with a field-level cached list invalidated on `Add` / `Remove` / `SetEnabled` / hot-reload rebind.
- Alternative: change the interface to expose `IEnumerable<...>` over the underlying dictionaries with manual iterator structs, or pass an explicit `ForEachMountedPredicate(Action<...>)` API (cached delegates per mount).

**Success conditions:**
- `MountedAccessors_ReturnSameInstance_BetweenMutations` — call accessor twice with no intervening mutation; assert reference equality.
- Combined with UBP-P11T1: `DataBreakpointSystem.Execute` allocates 0 B/op steady state.

### UBP-P11T10 — Reflection-free spatial position read

**Gap:** G20. `ReadPosition2D` uses `Marshal.PtrToStructure` + reflection (`FieldInfo.GetValue`) per entity per tick.

**Work:**
- At spatial-BP mount time, compile a `Func<EntityRepository, Entity, Vector2>` accessor for the configured `(PositionComponentType, PositionXPath, PositionYPath)` via expression trees (mirror what `IPredicateCompiler` already does for `PropertyMatchDto`).
- Cache the compiled accessor on the spatial tracker entry (replace the `(Breakpoint, SpatialBoundingPredicateDto, HashSet<Entity>)` tuple with `(Breakpoint, SpatialBoundingPredicateDto, HashSet<Entity>, Func<EntityRepository, Entity, Vector2>)`).
- Invoke the compiled accessor inside `EvaluateSpatialTrackers`; remove `ReadPosition2D` / `ReadFloatField`.
- Apply the same treatment to `ReadEntityName` / `ReadStringField` in `EvaluateLifecycleTrackers` (G24-adjacent).

**Success conditions:**
- `SpatialPredicate_FiresOnEntry_NotOnDwelling` — re-run UBP-P2T3's existing test; must still pass.
- `SpatialEvaluation_ZeroAllocations` — BenchmarkDotNet: spatial BP over 1000 entities; assert 0 B/op steady state.

### UBP-P11T11 — Reusable hits buffer in `EvaluateStatefulBreakpoints`

**Gap:** G21. `var hits = new List<(Breakpoint, Entity)>()` allocated every tick when any tracker mounted.

**Work:** Promote `hits` to a private field cleared at the top of `EvaluateStatefulBreakpoints`.

**Success conditions:**
- `StatefulEvaluation_ZeroAllocations` — BenchmarkDotNet: 3 structural BPs + 2 spatial BPs + 1 lifecycle BP, scenario with few transitions; assert 0 B/op steady state for the evaluation call.

### UBP-P11T12 — API / DESIGN alignment

**Gaps:** G14 (`OccurrenceThreshold` semantics), G22 (`OnPauseStateChanged` signature), G23 (`AddBreakpoint` silent coercion).

**Work:**
- Decide and document the canonical `OccurrenceThreshold` semantics:
  - **Recommended:** keep the implementation's "default 1 = first hit" because it matches every developer's mental model; update DESIGN §6.2 / §13.5 to remove the "0 = every hit" claim; remove the silent coercion in `AddBreakpoint` (let 0 mean "fire on tick 1" by treating it as 1 explicitly via a default parameter rather than runtime coercion).
- Update DESIGN §9 to spec `OnPauseStateChanged` as `Action<bool>` (the implementation) rather than parameterless. Update any consumer that expected the old shape.
- Add a XML-doc remark on `AddBreakpoint` describing the threshold parameter explicitly.

**Success conditions:**
- DESIGN.md §6.2, §9, §13.5 read consistently with the code after edits.
- No runtime coercion remains in `AddBreakpoint`; passing `occurrenceThreshold: 0` is explicitly disallowed (`ArgumentOutOfRangeException`) or the parameter default is documented as 1.
- A unit test `AddBreakpoint_ThresholdZero_ThrowsOrEqualsOne` asserts the chosen behaviour.

### UBP-P11T13 — Lifecycle `NetworkId` resolution

**Gap:** G24. `MatchesLifecycleCriteria` returns `false` silently for `EntityIdentifierType.NetworkId` because the manager has no `NetworkEntityMap` reference.

**Work:** Two options — pick one:
- **A (implement):** Add an optional `INetworkEntityMap? networkMap` ctor parameter to `DataBreakpointManager`; in `NetworkId` branch, resolve via `networkMap.TryGetEntityByNetworkId(...)` and compare. Wire in P10T1/T2 (pass the subsystem's `NetworkEntityMap` if available).
- **B (defer + document):** Add a clear `throw new NotSupportedException("LifecyclePredicateDto.EntityIdentifierType.NetworkId requires INetworkEntityMap; not wired in this subsystem.")` instead of silent false, and document the gap in DESIGN §6.8.

**Recommended:** A for Editor subsystem (which already has a map); B for headless test harness without network module.

**Success conditions:**
- `Lifecycle_NetworkId_ResolvesViaNetworkMap` — register `LifecyclePredicateDto(NetworkId, "42")` against a wired editor; spawn an entity with `NetworkIdentity.Value = 42`; assert hit.
- `Lifecycle_NetworkId_NoMapWired_ThrowsOrFailsClearly` — same DTO against a manager without map; assert either the chosen NotSupportedException or the documented graceful no-op.

---

## Phase P12 — End-to-end revalidation in a wired subsystem

**Goal:** Re-run the integration-flavoured success conditions against the *wired* engine (not the mocked test harness used for UBP-INT1/INT2/INT3). The original INT tests proved the library works in isolation; these prove the feature works in the actual editor.

### UBP-P12T1 — Wired end-to-end flow

**Re-run scope:** UBP-INT1's three assertions against an editor subsystem started via the standard `ClusterRunner` bootstrap (with `DataBreakpointManager` wired per P10T1).

**Additional success conditions on top of UBP-INT1:**
- `E2E_Wired_InspectorRendersPreTickValuesDuringPause` — pause via BP, observe entity-inspector window; assert it reads from `_preTickSnapshot` (verified via a value change that occurred in the breakpoint tick).
- `E2E_Wired_DeferredMutationAppliesAtN_Plus_1` — pause via BP, stage edit via the actual `ComponentEditWindow` (not direct API), click Step; assert next tick reflects the staged value.
- `E2E_Wired_GraphContextMenu_CreatesAndFiresBreakpoint` — open BTree canvas, right-click a node, select "Break on Activation"; let the AI run until the node enters; assert the manager pauses, gutter shows the red dot, banner appears.

### UBP-P12T2 — Wired performance budget

**Re-run scope:** UBP-INT2 against a wired subsystem.

**Success conditions:** identical to UBP-INT2, but executed against the actual subsystem kernel (not the manually-driven test fixture). Verify the chunk-version skipping from UBP-P11T2 and the zero-alloc hot path from UBP-P11T1 hold under real load.

### UBP-P12T3 — Wired Flight Recorder invariance

**Re-run scope:** UBP-INT3 against a wired subsystem with the recorder ordering enforced (UBP-P11T3).

**Success condition:** `.fdp` produced by a paused / stepped / resumed run loaded into the replay browser shows a strictly linear timeline with no duplicates and the deferred mutations appearing at the expected tick boundaries.

### UBP-P12T4 — Multi-subsystem isolation check

**Goal:** Verify Success Condition #6 (subsystem-isolated execution) once two subsystems (Editor + CGF) both host managers.

**Success conditions:**
- `MultiSubsystem_BPInCgfDoesNotPauseEditor` — register a BP in CGF; fire it; assert CGF pauses, Editor continues to advance its own clock.
- `MultiSubsystem_BPInEditorDoesNotPauseCgf` — symmetric.
