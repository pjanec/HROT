# Blueprint / AI-Editor / Universal-Breakpoints -- Design Conformance Fixes (Task Detail)

This document lists issues found while verifying that the codebase **correctly and fully**
implements the design documents under `.dev/blueprints-1`, `.dev/blueprints-2`, and
`.dev/breakpoints-1`. It is written for an independent AI coding agent (Claude Sonnet 4.6)
to fix the issues. **Do not duplicate design content** -- each item references the relevant
design section; open that section before fixing.

> Companion tracker: [TASK-TRACKER.md](./TASK-TRACKER.md)

---

## How to read this document

Each issue has:

- **ID** -- stable `BPF-NNN` identifier (used by the tracker).
- **Confidence** -- `VERIFIED` (the exact code was read via the codebase-memory graph and
  contradicts the design) or `REPORTED` (strongly indicated by symbol shape / call sites;
  the fixing agent must re-open the cited design section and code to confirm before changing).
- **Severity** -- `Critical` / `High` / `Medium` / `Low`.
- **Design ref** -- design doc + section. **Read this first.**
- **Code** -- file and symbol (qualified name where useful), or `ABSENT`.
- **Gap** -- what the design requires vs. what exists.
- **Fix direction** -- intent only, not a patch.

**Verification method.** Symbols were located and read with the `codebase-memory-mcp`
graph tools (`search_graph`, `query_graph`, `get_code_snippet`, `trace_path`) against project
`D-WORK-IOS-IG-SimHost-FDP`. Every TASK in all three design sets is marked `[x]` (done) in the
original trackers; the issues below are deviations that survived that "done" state. Known,
already-tracked deviations are cross-referenced to the existing `DEBT-TRACKER.md` files so they
are not lost.

**Coverage.** See the "Verification Coverage" section at the end. The blueprints-1 core
(Runtime, Debug Protocol, Test Harness, the compiler debug-map) was verified in depth. The
compiler pipeline, editor windows, all of blueprints-2 (NodeEditor extensions + BT/HSM hosts),
and breakpoints-1 P1-P12 were confirmed **present and test-covered** but were sampled, not
exhaustively field-checked. Areas needing a deeper second pass are listed explicitly.

---

## A. Debug Protocol (blueprints-1)

Design: [Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md](../blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md)
Tasks: TASK-DBG-001..006. Existing debt: see `.dev/blueprints-1/DEBT-TRACKER.md` (DEBT-003/004/018-022).

### BPF-001 -- Pause-time state inspection (`GetCurrentStateSnapshot`) is a stub
- **Confidence:** VERIFIED
- **Severity:** High
- **Design ref:** Debug Protocol DD §2.2 (`BlueprintStateSnapshot`), §4.2/§4.3 (`stateLayout`), §8.4-§8.6 (`CaptureStateSnapshot` / instance / AiPrimitive paths). Task TASK-DBG-004 (marked done).
- **Code:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs:390-393`; record `BlueprintStateSnapshot` in `Hrot.Blueprints.Core/IBlueprintDebugSession.cs`.
- **Gap:** Design's `BlueprintStateSnapshot` carries `(Self, AssetId, AssetName, Dispatch, IReadOnlyDictionary<string,object> FieldValues, BlueprintLatentCursor? Cursor)` populated by reading slot bytes and projecting fields via `stateLayout`. Implemented record is only `(Self, AssetId)`, and `GetCurrentStateSnapshot()` returns `new BlueprintStateSnapshot(_pausedOnEntity.Value, Guid.Empty)` with an inline `// assetId stub until DBG-004` comment. No field values, asset name, dispatch kind, or cursor; no dispatch-kind switch (Instance / AiPrimitive / Library); no AiPrimitive structure-hash header check (§8.6). DBG-004 is marked complete but this central capability is not implemented.
- **Fix direction:** Implement field-level capture per §8.4-§8.6, sourcing field offsets/types from the debug-map `stateLayout` (blocked on BPF-002 below). Expand the `BlueprintStateSnapshot` record to the designed shape.

### BPF-002 -- Compiler debug-map omits `pins`, `graphs`, `stateLayout`, `assetName`
- **Confidence:** VERIFIED
- **Severity:** High
- **Design ref:** Debug Protocol DD §4.2, §4.3, §4.5 (on-disk debug-map format). Tasks TASK-DBG-002, TASK-DBG-004, TASK-CP-005.
- **Code:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/DebugMapBuilder.cs` (`DebugMap`, `DebugMapEntry`); `Hrot.Blueprints.Core/DebugMapIndex.cs`.
- **Gap:** Design format has top-level `assetName`, `blueprintIdHex`, `generatedSourcePath`, a `graphs[]` array, a `pins[]` array (`pinId`, `valueAccessExpression`, `typeFullName`), and `stateLayout.fields`. Implemented `DebugMap` is `{AssetId, BlueprintId, StructureHash, Entries[]}` and `DebugMapEntry` is `{NodeId, GraphId, StartLine, EndLine, NodeKind, DisplayName, PhaseIndex}` only. No pins, graphs, stateLayout, assetName, or source path. `DebugMapIndex` exposes node-by-string and node-by-Guid maps but no pin/graph resolution. This is the **root cause** of BPF-001 (no `stateLayout`) and BPF-003 (no pin type resolution).
- **Fix direction:** Extend the emitted map and `DebugMapIndex` to carry pins, graphs, stateLayout, and asset name per §4.2-4.5; thread through `DebugMapSerializer` / `DebugMapDto`.

### BPF-003 -- Breakpoint structure-hash safety, staleness, and per-frame multi-entity dedup missing
- **Confidence:** VERIFIED
- **Severity:** High
- **Design ref:** Debug Protocol DD §5.2, §5.3, §6.1 (hash-guarded fire + 5-state lifecycle incl. `Stale`), §9.2 (first-hit-wins / once-per-frame with hit-count accumulation across entities). Tasks TASK-DBG-003, TASK-DBG-005. Related debt DEBT-020 (RESOLVED string-match) and DEBT-021 (OPEN).
- **Code:** `Hrot.Blueprints.Editor/BlueprintDebugSession.cs:78-114` (`OnNodeEnter`), `HandleBreakpointHit` (501-543); `Breakpoint` record in `Hrot.Blueprints.Core/IBlueprintDebugSession.cs`.
- **Gap:** The `Breakpoint` record is `(Id, AssetId, GraphId, NodeId, HitCount, Enabled)` -- no `AssetStructureHashAtSetTime`, no `IsStale`. `OnNodeEnter` fires on raw `_bpByNodeString.TryGetValue(nodeId, ...)` with no structure-hash comparison and no stale check (design §5.2/§6.1 requires refusing to fire and marking stale on hash mismatch; `RegisterDebugMap` currently *clears* breakpoints on mismatch instead of marking them stale for rebind). Multi-entity semantics (§9.2): there is no `_firedThisFrame` set and no `OnNewTick` reset, only the `!_isPaused` re-entrant guard, so subsequent same-frame hits on other entities are dropped entirely (no hit-count accumulation, no event). `IsStale` exists only on `Watch`, not `Breakpoint`.
- **Fix direction:** Add hash + `IsStale` to `Breakpoint`; gate firing on `Enabled && !IsStale && hashMatches`; implement per-frame dedup set cleared at tick boundary while still accumulating `HitCount` across entities; switch reload handling from clear-to-mark-stale + rebind.

### BPF-004 -- Peer-call probe signature diverges; asset matching is dead
- **Confidence:** VERIFIED
- **Severity:** Medium
- **Design ref:** Debug Protocol DD §2.4, §7.4 (`OnPeerCallEnter(self, peerAssetId, methodName)` / `OnPeerCallExit(self, peerAssetId, methodName)`).
- **Code:** `Hrot.Blueprints.Editor/BlueprintDebugSession.cs:140-160` (`OnPeerCallEnter`), `OnPeerCallExit` (162-173); `Hrot.Blueprints.Core/IBlueprintProbeSink.cs`.
- **Gap:** Implemented `OnPeerCallEnter(Entity, string targetAssetName, string targetGraphName)` and `OnPeerCallExit(Entity)`. Enter resolves the asset by matching `targetAssetName` against `DebugMapIndex.AssetName`; since the asset name falls back to the AssetId Guid-string (see BPF-002), the match essentially never succeeds and active entities are bucketed under `Guid.Empty`. `GetActiveEntities` therefore returns entities keyed incorrectly.
- **Fix direction:** Align probe signature to pass the peer asset-id (Guid) directly per §2.4/§7.4, or fix the name resolution once BPF-002 provides real asset names; verify `GetActiveEntities` keys are correct.

### BPF-005 -- StepOut tick-boundary semantics and entity-death step abandonment missing
- **Confidence:** REPORTED
- **Severity:** Medium
- **Design ref:** Debug Protocol DD §7.6 (StepOut at depth 0 must re-pause at the next tick boundary), §9.5 (abandon step if `_stepFromEntity` is no longer `IsAlive`).
- **Code:** `Hrot.Blueprints.Editor/BlueprintDebugSession.cs:78-114` (step block in `OnNodeEnter`), `StepOut` (375-386).
- **Gap:** StepOut matches purely on `depth < _stepFromDepth`; at top-level depth 0 the condition `depth < 0` can never hold, so StepOut from a top-level node never re-pauses (no `_stepFromTick` tracked for the tick-boundary fallback). No liveness guard abandons an in-flight step when its entity dies.
- **Fix direction:** Track `_stepFromTick`; for StepOut at depth 0, re-pause when `_view.Tick > _stepFromTick`. Add an `IsAlive` check that cancels the pending step (see also breakpoints-1 UBP-P9T2 "Step abandoned").

---

## B. Runtime (blueprints-1)

Design: [Blueprint_Subsystem_Runtime_Detailed_Design.md](../blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design.md)
Tasks: TASK-RT-001..007. Existing debt: DEBT-014 (StructureHash uint, RESOLVED), DEBT-015 (StateFields dict, RESOLVED).

### BPF-006 -- `IReloadLogSink` interface reduced vs design (no `OnSoftReload`, no entity/hash context)
- **Confidence:** VERIFIED
- **Severity:** Medium
- **Design ref:** Runtime DD §9.7 (`IReloadLogSink` -- `OnSoftReload(int, Entity, ulong)` and `OnHardReset(int, Entity, ulong oldHash, ulong newHash)`). Also referenced by the editor Hot-Reload-Log panel (Editor DD).
- **Code:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/IReloadLogSink.cs` -- `void OnHardReset(int blueprintId, uint newInstanceVersion);` only.
- **Gap:** No `OnSoftReload`; `OnHardReset` lost the `Entity` and the `(oldHash,newHash)` pair, and passes the post-bump `InstanceVersion` (`uint`) instead. A Hot-Reload-Log consumer expecting the designed signature cannot report which entity/hash changed. Not recorded in DEBT-TRACKER.
- **Fix direction:** Restore the designed two-method surface (or document the reduced Slice-1 surface as a DEBT entry if intentional). Confirm the editor Hot-Reload-Log requirements first.

### BPF-007 -- `BlueprintRegistry.GetAll()` drops the `(Id, Def)` tuple
- **Confidence:** REPORTED
- **Severity:** Low
- **Design ref:** Runtime DD §2.2 / §2.3 (`GetAll()` returns `IEnumerable<(int Id, BlueprintDefinition Def)>`).
- **Code:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs` (`GetAll`).
- **Gap:** Returns `IReadOnlyCollection<BlueprintDefinition>` (values only); callers cannot recover the BlueprintId from this method. Materialization (mid-reload safety) is preserved.
- **Fix direction:** Restore the tuple shape unless no caller needs the Id (verify via call sites).

> Note: `TryGetById`/`TryGetByName` use `out BlueprintDefinition?` (nullable) vs design's non-nullable `out`. Benign annotation deviation; record only if signature parity matters to callers.

---

## C. Test Harness (blueprints-1)

Design: [Blueprint_Subsystem_Test_Harness_Detailed_Design.md](../blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design.md)
Tasks: TASK-TH-003, TASK-TH-005, TASK-TH-010. Existing debt: DEBT-006/007/008/012 (all OPEN).

### BPF-008 -- Fixture missing `SnapshotAllBlackboards`, `SetChannelStatus<T>`, `GetSlotEntry`
- **Confidence:** VERIFIED
- **Severity:** Medium
- **Design ref:** Test Harness DD §2.4, §5.4, §5.6, §5.7; TASK-TH-003 scope. Already tracked: DEBT-006, DEBT-007, DEBT-008 (all OPEN).
- **Code:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs` -- methods ABSENT (graph search finds the names only inside the design docs, not as methods). `BlueprintSlotEntry` struct itself exists in `Fdp.Toolkits/Blueprints/Partitioning/BlueprintSlotEntry.cs`.
- **Gap:** Three public fixture helpers required by the DD do not exist. Consequence: the §9.3 reload test cannot assert `GetSlotEntry(...).InstanceVersion` bumping as the design example shows; replay-determinism snapshot tests (§5.7) cannot be written; channel-status simulation (§5.6) is only achievable via `MockDispatcherSystem<TChannel>` subclasses.
- **Fix direction:** Implement the three helpers per the DD (they were deferred pending partitions, which now exist). Confirm the `MockDispatcher` path is an acceptable substitute for `SetChannelStatus` or add the helper.

### BPF-009 -- `InvokeHsmAction` / `InvokeHsmGuard` remain `NotImplementedException` stubs
- **Confidence:** VERIFIED
- **Severity:** Medium
- **Design ref:** Test Harness DD §12.1-§12.3 (full HSM invoke helpers) + InlinePatches "Patch 3 -- Use `EntityRepository.UnmanagedHandle`". Deferred by `.dev/blueprints-1/batches/BATCH-14-INSTRUCTIONS.md` to Phase 4.
- **Code:** `Hrot.Blueprints.Tests/BlueprintTestFixture.cs` -- `InvokeHsmAction`, `InvokeHsmGuard` throw `NotImplementedException("Requires compiled blueprint assembly (Phase 4)")`. `InvokeBTreeAction` is fully implemented.
- **Gap:** Phase 4 (Hot Reload) is marked complete, but the HSM invoke helpers were never un-stubbed. Any HSM-hosted AiPrimitive test that needs direct action/guard invocation is blocked.
- **Fix direction:** Implement per §12.3 `HsmKernelBridge` / `UnmanagedHandle` pattern, or explicitly re-scope and record in DEBT-TRACKER if still deferred.

---

## D. AI Editor hosts (blueprints-2)

Design: [HSM_Editor_NodeEditor_Host_Design.md](../blueprints-2/HSM_Editor_NodeEditor_Host_Design.md),
[BTree_Editor_NodeEditor_Host_Design.md](../blueprints-2/BTree_Editor_NodeEditor_Host_Design.md).
Tasks: TASK-HS-S2-01, TASK-BT-S2-01 + FIX1-TASK-DETAIL.md. Existing debt: `.dev/blueprints-2/DEBT-TRACKER.md`.

### BPF-010 -- `HsmInstanceSnapshot` populated with empty active-states / events / timers / history
- **Confidence:** REPORTED (needs confirmation against HSM host design + `HsmInstanceSnapshot` contract)
- **Severity:** Medium
- **Design ref:** HSM Editor Host Design -- runtime snapshot (Slice 2); FIX1 TASK-HS-S2-01 ("implement `GetCurrentStateSnapshot()` to extract live ECS state into `HsmInstanceSnapshot`").
- **Code:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs:45+` (`Update` building the snapshot).
- **Gap:** For `BrainHsm64`, the snapshot is built as `new HsmInstanceSnapshot(entity, Guid.Empty, Array.Empty<Guid>() /*active states*/, Array.Empty<HsmEventQueueEntry>(), Array.Empty<HsmTimerSlot>(), Array.Empty<HsmHistorySlot>(), Header.Phase, Header.MicroStep, 0, Header.Flags, Header.RngState, Header.Generation)`. Header scalar fields are extracted, but active-state ids, event queue, timers, and history slots are all empty arrays, and assetId is `Guid.Empty`. The runtime overlay / inspector that depends on active-state ids (e.g. `hsm.runtime_overlay`, LCA highlight) would render nothing.
- **Fix direction:** Decode active states / event queue / timers / history from the `BrainHsm*` component memory per the HSM host design; populate assetId. Verify the BTree counterpart (`BTreeDebugSession.Update`) fully populates its stack/snapshot.

> The blueprints-2 `DEBT-TRACKER.md` records D-01..D-04 (BTree blob `SubtreeAssetIds` never populated -> subtree resolution broken until implemented; HSM emitter/flattener action-ordering mismatch). These are pre-existing OPEN items; fold them into the fix effort -- see BPF-013.

---

## E. Cross-cutting / already-tracked deviations to clear

These are documented in the existing `DEBT-TRACKER.md` files but remain OPEN and contradict the
designs; list them here so the fixing effort closes them or converts them to intentional, commented deviations.

### BPF-011 -- blueprints-1 OPEN debt that diverges from design
- **Confidence:** VERIFIED (debt rows exist)
- **Severity:** Low
- **Design ref:** see each DEBT row in `.dev/blueprints-1/DEBT-TRACKER.md`.
- **Items:**
  - DEBT-003 -- `BreakpointKey(string NodeId)` record specified (TASK-TH-008) but never implemented; breakpoints use raw strings. (Related to BPF-003.)
  - DEBT-004 -- event named `OnPinValueChangedEvent` instead of design's `OnPinValueChanged` (deliberate C# name-clash workaround) -- ensure source comment present.
  - DEBT-018 -- core debug files placed in `Hrot.Blueprints.Core` root, not a `Debug/` subfolder (gitignore conflict); namespace is correct.
  - DEBT-021 -- per-hit firing of `OnBreakpointListChanged` (structural-change event misused for hit-count) -- add `OnBreakpointHitCountChanged` or remove the per-hit fire.
  - DEBT-022 -- `GetNodeHistory(Entity,int)` is non-interface; add to `IBlueprintDebugSession` if the editor needs per-entity history.
  - DEBT-023 -- `BuiltInChannelCommandCatalog` uses short action names ("MoveTo") vs design's hierarchical paths ("Locomotion/MoveTo"); add an explanatory comment or align.
- **Fix direction:** For each, either implement to the design or add an explicit "intentional deviation" comment and mark the DEBT row RESOLVED.

### BPF-012 -- blueprints-2 OPEN debt that diverges from design
- **Confidence:** VERIFIED (debt rows exist)
- **Severity:** Medium (D-02 functional)
- **Design ref:** `.dev/blueprints-2/DEBT-TRACKER.md`.
- **Items:**
  - D-02 -- `BehaviorTreeBlob.SubtreeAssetIds` declared but never populated by `TreeCompiler`; **subtree resolution at runtime is broken** until implemented. (Intersects TASK-BT-S1-12 subtree node visual/navigation.)
  - D-01 -- `BehaviorTreeState.InstanceFlags` overlays `AsyncHandles[2]` (documented union) -- add reserved-bytes block on next redesign.
  - D-03 / D-04 -- `HsmEmitter.BuildMachineMetadata` reconstructs transition ordering independently from `HsmFlattener`; `MachineMetadata.ActionNames` ordering diverges -- risk of wrong Guid-to-index mapping.
- **Fix direction:** D-02 is the priority (functional break). Address D-03/D-04 by passing `FlattenedData` into `BuildMachineMetadata` so orderings cannot drift.

### BPF-013 -- breakpoints-1 OPEN debt
- **Confidence:** VERIFIED (debt rows exist)
- **Severity:** Low-Medium
- **Design ref:** `.dev/breakpoints-1/DEBT-TRACKER.md` + universal-breakpoints-DESIGN.md.
- **Items:**
  - D-BP-01 -- `CgfNoOpTimeController.IsPausedByDebugger` returns false even when the manager is paused (temporal banner correctness in CGF perspective).
  - D-BP-02 -- CGF `_bpPreTickSnapshot` mirrors only `CgfComponentRegistry`; may miss HrotNodeBuilder-internal component registrations.
  - D-BP-04 -- `GraphEditorWindow.SetBreakpointManager` canvas right-click handler still a stub; `BlueprintBreakpointMenuPopulator.PopulateNodeMenu` not reached via UI (intersects UBP-P10T9 / UBP-P7T3).
- **Fix direction:** Verify against the running editor; D-BP-04 (Blueprint canvas breakpoint menu reachability) should be confirmed end-to-end since UBP-P7T3/P10T9 are marked done.

---

## Verification Coverage

**Deeply verified (code read via graph, cross-checked to design):**
- Debug Protocol core: `BlueprintDebugSession` (`OnNodeEnter`, `GetCurrentStateSnapshot`,
  `OnPeerCallEnter`), `Breakpoint`/`BlueprintStateSnapshot`/`Watch` record shapes, `DebugMap`/`DebugMapEntry`.
- Runtime: `IReloadLogSink`, `BlueprintRegistry` surface (and the RESOLVED DEBT-014/015 struct deviations).
- Test Harness: fixture method inventory (absences), HSM invoke stubs.
- BT/HSM debug-session snapshots (presence + the HSM partial population in BPF-010).

**Confirmed present + test-covered, but only sampled (recommend a deeper second pass):**
- Compiler pipeline stages 1-8 (`Stage5_Schedule`, `Stage6_Lower`, `Roslyn/InMemoryRoslynCompiler`,
  `DebugMapSerializer`) -- structurally present; **not** field-checked against
  Compiler_Detailed_Design beyond the debug-map shape (BPF-002). Verify IR data model, determinism
  guarantees, catalog validation, and emit output against the DD.
- Editor (blueprints-1) windows/panels (Asset Browser, Graph Editor, Inspector, Debug/Watch/Callstack/
  Hot-Reload-Log, StructEdit drawers) -- not individually verified; confirm against Editor_Detailed_Design.
- Hot Reload (`AiHotReloadCoordinator`) -- not field-checked; verify reload sequencing / ALC swap /
  failure rollback against Hot_Reload_Detailed_Design (note BPF-006 affects the log surface).
- blueprints-2 NodeEditor extensions (NodeAttachments, ContainerNodes, CustomCanvasRenderer) and the
  BT/HSM hosts (Slice 1-5) -- present with extensive tests and FIX1 corrections applied; the 15-step
  Z-layer hit-test (FIX1 NEA-06/NEC-05/NER-04) and the FIX1 stub-replacements should be spot-confirmed.
- breakpoints-1 P1-P12 (`DataBreakpointSystem`, snapshot provider, predicate DTOs, trace-buffer scans,
  manager UI, P11 hot-path hardening) -- present with zero-alloc / chunk-version / gate tests; the
  10 design Success Conditions (universal-breakpoints-DESIGN.md) should be re-checked against P12 wiring.

**Not in scope of this pass:** the Demo tasks (TASK-DEMO-001..005) and the planning docs
(Implementation_Roadmap, Slice2_Candidates) were not verified.

**Tooling note:** verification used the `codebase-memory-mcp` graph tools only (no `search_code`,
no text grep). The `.mcp.json` server definition added at repo root is required for those tools to load.

---

# PART 2 -- Deep Correctness Audit (workflow-confirmed)

Part 1 (BPF-001..013) was an interface-conformance pass. Part 2 is a deep **implementation-correctness**
audit run as a 14-cluster hunt + adversarial-verify workflow (Sonnet agents, codebase-memory graph only).
73 candidate findings were produced; **40 survived adversarial re-verification** (each refuter re-read the
actual code + design and defaulted to "not a defect" unless it positively re-confirmed). Severities below are
the refuter's *corrected* severity. Every item cites the design § and the exact code symbol; **open both before fixing.**

Lenses: `algorithm`, `integration-seam`, `reachability`, `invariant`, `dual-path`, `spec-drift`, `SC-anchor`
(SC-anchor = a Success-Condition test that passes but asserts against a stub / never exercises the real path).

Confirmations of Part 1: **BPF-002** (debug-map omissions) is re-confirmed and extended by BPF-021; **BPF-006**
(IReloadLogSink) re-confirmed (see BPF below, runtime-allocator); **BPF-007** (GetAll tuple) re-confirmed as BPF-049;
**BPF-010** (HSM snapshot empty) re-confirmed and localized by BPF-023.

## CRITICAL

### BPF-014 -- Instance LatentDelay resume reads WorkingState field instead of the cursor (compiler)
- **Lens:** algorithm | **Design:** Compiler DD §9.8 (Instance Delay: `if (Time < state.Cursor.WaitUntilTime) Return`)
- **Code:** `Hrot.Blueprints.Compiler/Compiler/Lowering/WaitLowering_Instance.cs` `Apply` (~L73 write, ~L186 read)
- **Gap:** Write side emits `IrOp_WriteCursorWaitUntilTime` -> `s.Cursor.WaitUntilTime`, but the resume read emits `IrOp_ReadWorkingStateWaitUntilTime` -> `ws.__waitUntilTime`. Instance graphs have no `ws` variable; there is no `IrOp_ReadCursorWaitUntilTime` op at all. Generated Instance C# references a non-existent variable -> latent delay never resumes correctly.
- **Fix:** add/emit a cursor-read op (`s.Cursor.WaitUntilTime`) on the Instance resume path.

### BPF-015 -- `DebugProbe.NodeEnter`/`PinValue` emitted as a C# comment, not a call (compiler) [found by 2 clusters]
- **Lens:** SC-anchor / integration-seam | **Design:** Compiler DD §10.7 & §15.8; Debug Protocol DD §3.1, §3.3
- **Code:** `Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs` `EmitOp` cases `IrOp_DebugProbe_NodeEnter` / `IrOp_DebugProbe_PinValue` (~L292-300)
- **Gap:** Emits `// [DebugProbe] NodeEnter {id} ...` instead of `DebugProbe.NodeEnter(self, "{id}");`. Generated code never calls the probe, so **every breakpoint/step/watch/history feature is dead at runtime**. Tests pass because they call `OnNodeEnter` directly, bypassing generated code. This is the runtime root cause behind much of the Part-1 Debug findings.
- **Fix:** emit the real call per §10.7 (and PinValue analogously); add a Stage-7 golden test asserting the call text.

### BPF-016 -- Event-poll call site omits payload args -> uncompilable generated C# (compiler)
- **Lens:** integration-seam | **Design:** Compiler DD §14.1, §16
- **Code:** `StatementEmitter.cs` `EmitOp` case `IrOp_PollEngineEvent` (~L151); `InstanceEmitter.cs` `EmitEventMethod` (L136-156)
- **Gap:** Call site emits `Event_{g}(ref s, view, ecb, self, time, deltaTime);` (no payload fields, and a spurious `deltaTime`), but `EmitEventMethod` generates the signature with the event's `Inputs` (e.g. OnHit: `Entity, float, Vector3`). Any event graph with inputs -> Roslyn CS1501 (wrong arg count); the design also has no `deltaTime` on event handlers.
- **Fix:** emit `__e.{Field}` args from the catalog/Inputs and drop the stray `deltaTime`.

### BPF-017 -- HSM `ActionNames` keyed by positional index but blob stores hashes -> all action/guard names garbled (hsm-host)
- **Lens:** dual-path | **Design:** HSM Host Design §3.2, §4.2
- **Code:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmEmitter.cs` `BuildMachineMetadata` (~L41-45); `HsmFlattener.cs` `BuildActionTable`; `Hrot.Hsm.Editor/Model/HsmAssetProjector.cs` `Project`
- **Gap:** `BuildMachineMetadata` keys `ActionNames[0,1,2,...]` (alphabetical positional), but the blob's `OnEntryActionId`/`GuardId`/etc. are FNV-1 **hashes** (`ComputeHash`). `GetActionName(hashId)` misses every key -> returns `"Action_<hash>"`. Every action/guard name in every projected `HsmAsset` is wrong on every load. `MachineMetadata.StateStableIds`/positional table already exists but the hash mapping is never reconciled. (Extends blueprints-2 D-03/D-04.)
- **Fix:** key `ActionNames` by the same hash IDs the flattener writes (pass `FlattenedData` into `BuildMachineMetadata`).

### BPF-018 -- BTree `SubtreeAssetIds` never populated -> projection `IndexOutOfRangeException`; emitter writes a Guid where a tree name is required (btree-host)
- **Lens:** integration-seam | **Design:** BTree Host Design §3.2 step 7, §8.2; blueprints-2 DEBT D-02
- **Code:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs` `FlattenToBlobCore`/`FlattenRecursive`; `Hrot.BTree.Editor/Model/BehaviorTreeAssetProjector.cs` `VisitNode` (~L160); `Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs` `EmitSubtree` (L413-422)
- **Gap:** `FlattenRecursive` has no `NodeType.Subtree` case, so `SubtreeAssetIds` stays `Array.Empty`. `VisitNode` does `blob.SubtreeAssetIds[nodeDef.PayloadIndex]` -> **crash for any tree containing a Subtree node**. Separately, `EmitSubtree` emits `p.SubtreeAssetId:D` (a Guid) as the first `.Subtree(...)` arg, but `BTreeBuilder.Subtree(string treeName, ...)` expects a name -> broken round-trip even if populated.
- **Fix:** populate `SubtreeAssetIds`/payload index in the flattener; emit the tree name (not Guid) in `EmitSubtree`.

## HIGH

### BPF-019 -- `BuildReturnTerminator` resolves return value into the last-allocated block, not the current block (compiler)
- **Lens:** algorithm | **Design:** Compiler DD §8.4, §8.8 | **Code:** `Stage5_Schedule.cs` `GraphScheduler.BuildReturnTerminator` (~L847)
- **Gap:** Passes `_blockBuilders[Count-1].Statements` instead of the current block's `bb.Statements`. For a Library/Function graph with a Branch/Latent node before a data-returning Return, the value's upstream statements land in the wrong block -> use-before-define in emitted C#. Untested (only AiPrimitive return-status graphs exercised).
- **Fix:** thread the current `bb` statement list into `BuildReturnTerminator`.

### BPF-020 -- `IrOp_RaiseCustomEvent` emitted as a comment -> custom-event dispatch silently dropped (compiler)
- **Lens:** SC-anchor | **Design:** Compiler DD §10.7 | **Code:** `StatementEmitter.cs` `EmitOp` case `IrOp_RaiseCustomEvent` (~L124-129)
- **Gap:** Emits `// RaiseCustomEvent: ...` instead of `Event_{name}(ref s, view, ecb, self, time, {args});`. Synchronous custom-event calls never happen at runtime.
- **Fix:** emit the real call per §10.7.

### BPF-021 -- DebugMap `NodeKind`/`DisplayName` never populated; `RecordPin` + `GeneratedSourcePath` absent (compiler; extends BPF-002)
- **Lens:** spec-drift | **Design:** Compiler DD §13.2, §13.3 | **Code:** `Compiler/Emit/DebugMapBuilder.cs`; `CSharpEmitter.cs` `EmitNodeStart/End`
- **Gap:** `Record(...)` always leaves `NodeKind`/`DisplayName` = `string.Empty` (emitter never supplies them); `RecordPin` and `GeneratedSourcePath` do not exist. Per Debug DD §13.5, pins are required to evaluate watch expressions at breakpoints. Root-cause sibling of BPF-002/BPF-001.
- **Fix:** populate node kind/name from the IR annotation; add pin recording + generated-source-path to the map and serializer.

### BPF-022 -- `HsmFluentEmitter` never emits `DeferEvent()` -> deferred-event lists dropped every save (hsm-host)
- **Lens:** spec-drift | **Design:** HSM Host Design §4.1, §4.2 rule 4 | **Code:** `Hrot.Hsm.Editor/Emit/HsmFluentEmitter.cs` `BuildStateConfig` (L272-285); `HsmAssetProjector` never fills `DeferredEventIds`
- **Gap:** Neither projector (read) nor emitter (write) handles `StateNode.DeferredEventIds`; a save+reload round-trip silently drops all deferred-event declarations.
- **Fix:** project deferred events from the blob and emit `.DeferEvent(id)` in the stable subsection order.

### BPF-023 -- `HsmDebugSession.Update` hardcodes empty active-leaf/event/timer/history arrays (hsm-host; localizes BPF-010)
- **Lens:** SC-anchor | **Design:** HSM Host Design §13.1, §13.4 | **Code:** `Hrot.Hsm.Editor/Debug/HsmDebugSession.cs` `Update` (L82-109)
- **Gap:** Both BrainHsm64/128 branches pass `Array.Empty<Guid>()` for `ActiveLeafStableIds` (and empty events/timers/history). The kernel `ActiveLeafIds` bitset is never read/symbolicated. `hsm.runtime_overlay` + inspector iterate an always-empty list -> no active-state glow ever.
- **Fix:** decode `ActiveLeafIds` from the component and symbolicate via `MachineMetadata`.

### BPF-024 -- HSM StepOver and StepOut use an identical pause predicate -> StepOut never reaches RTC quiescence (hsm-host)
- **Lens:** spec-drift | **Design:** HSM Host Design §13.2 | **Code:** `HsmDebugSession.cs` `Update` step block (L155-162)
- **Gap:** Both `StepMode.Over` and `StepMode.Out` use `MicroStep != _stepFromMicroStep`; StepOut should run until `Phase == InstancePhase.Activity`. `Phase` is available but never checked.
- **Fix:** gate StepOut on entering the Activity phase.

### BPF-025 -- HSM layout `StableId` assigned by positional lexicographic sort -> identity breaks on any structural edit (hsm-host)
- **Lens:** algorithm | **Design:** HSM Host Design §3.2 step 4 | **Code:** `HsmAssetProjector.cs` `Project` (~L73-105 states, ~L145 transitions, regions)
- **Gap:** Sorts layout Guid keys and assigns by array index (`stateNodes[i].StableId = layoutStateKeys[i]`) with a `TODO` comment. Adding/removing a state shifts every subsequent Guid->layout-entry mapping (wrong positions/waypoints/colors). `MachineMetadata.StateStableIds` (keyed by FlatIndex) already holds the correct mapping but is never read.
- **Fix:** resolve StableId via `metadata.StateStableIds[FlatIndex]` (and `TransitionVisualIds`), not by position.

### BPF-026 -- `BTreeDebugSession.Update` never symbolicates `RunningElementId`/stack VisualIds -> overlay shows nothing (btree-host)
- **Lens:** SC-anchor | **Design:** BTree Host Design §12.4, §12.7 | **Code:** `Hrot.BTree.Editor/Debug/BTreeDebugSession.cs` `Update` (L78-164)
- **Gap:** Snapshot built with `RunningElementId: null` and an all-null `StackElementIds`; no asset/`DebugMetadata` reference exists in `Update` to convert blob indices to VisualIds. `BTreeRuntimeOverlayRenderer` `FindNode(null)` -> running-node pulse + ancestry glow never render.
- **Fix:** pass `BehaviorTreeAsset.DebugMetadata` into the session and symbolicate index->VisualId.

### BPF-027 -- `EmitComposite` emits a stray separator producing invalid C# for non-empty composites (btree-host)
- **Lens:** algorithm | **Design:** BTree Host Design §4.2, §4.4 (round-trip) | **Code:** `BTreeFluentEmitter.cs` `EmitComposite` (~L220-234)
- **Gap:** After the last child (which ends with `;` when `isLast=true`) the emitter unconditionally appends a bare `,` line then the `visualId` arg -> `;` followed by `,` inside a lambda arg list = uncompilable. Tests only assert substring containment, never that output compiles.
- **Fix:** correct the separator/terminator logic; add a round-trip compile assertion.

### BPF-028 -- Drag-based node ops call `view.Commands.Apply` directly, bypassing the undo stack (nodeeditor)
- **Lens:** algorithm | **Design:** ContainerNodes §11.1 | **Code:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasInput.cs` `CommitNodeDrop` (L380-442)
- **Gap:** All three drop paths call `view.Commands.Apply(ChangeParent/MoveNodes)` instead of `view.Execute(forward, inverse, label)` (the only path that records undo). Drag reparent/move is not undoable. (Refuter notes the whole CanvasInput layer shares this pattern -> may be a pervasive design gap; confirm host doesn't intercept.)
- **Fix:** route drag mutations through `view.Execute` with proper inverses.

### BPF-029 -- Multi-selection drag emits N separate `ChangeParent` commands instead of one `ChangeParentMultiple` (nodeeditor)
- **Lens:** algorithm | **Design:** ContainerNodes §10.3, §11 | **Code:** `CanvasInput.cs` `CommitNodeDrop` (L384-441)
- **Gap:** Iterates selection and applies per-node commands; `GraphCommand.ChangeParentMultiple` exists but has 0 callers -> multi-node reparent is not a single undo step (and, per BPF-028, not undoable at all).
- **Fix:** emit one `ChangeParentMultiple`.

### BPF-030 -- Missing ancestor-in-selection suppression -> child of a selected container moves twice as far (nodeeditor)
- **Lens:** algorithm | **Design:** ContainerNodes §9.2 | **Code:** `CanvasInput.cs` `HandleDraggingNodes` (L307-340) + `CommitNodeDrop`
- **Gap:** No guard skips nodes whose ancestor is also selected. At commit, when a child is processed before its selected parent (HashSet order is non-deterministic), the child is stored at `local + delta` and the parent then moves too -> net `childOriginal + 2*delta`. Non-deterministic double-move.
- **Fix:** add the ancestor-membership guard the design mandates.

### BPF-031 -- `HotReloadLogWindow` never subscribed to coordinator events -> permanently empty at runtime (editor)
- **Lens:** reachability | **Design:** Editor DD §9.3 | **Code:** `Hrot.Blueprints.Editor/Debug/HotReloadLogWindow.cs`; `BlueprintEditorModule.cs`
- **Gap:** Window has no constructor/subscription; `OnReloadCompleted`/`OnReloadFailed` have only test callers. Module wires coordinator events to the output console, never to the window.
- **Fix:** subscribe the window to `coordinator.OnReloadCompleted/OnReloadFailed` at construction.

### BPF-032 -- `HotReloadLogWindow` tests call methods directly -> subscription contract untested (editor)
- **Lens:** SC-anchor | **Design:** Editor DD §9.3 | **Code:** `Hrot.Blueprints.Tests/Editor/HotReloadLogModelTests.cs` (L41-62)
- **Gap:** Tests invoke `window.OnReloadCompleted(info)` directly; they never construct a coordinator or assert the hookup -> the absent subscription (BPF-031) is invisible to the suite.
- **Fix:** add a test that fires the coordinator event and asserts the window received it.

### BPF-033 -- `BlueprintDebugSession.IsAttached` hardcoded `true`; no `Attach()`; editor never routes `DebugProbe.Sink` (editor)
- **Lens:** invariant | **Design:** Editor DD §12.2, §12.5, §3.5, §8.2 | **Code:** `BlueprintDebugSession.cs` (L177); `BlueprintEditorModule.cs` `OnEditorActivated/Deactivated`
- **Gap:** `IsAttached => true` (no field); no `Attach()` exists; `OnEditorActivated` never sets `DebugProbe.Sink = session`; `OnEditorDeactivated` never calls `Detach()`. So probes are never routed to the session, and the panel always shows "Attached". (Compounds BPF-015.)
- **Fix:** add `Attach()` setting the sink + tracked flag; wire activate/deactivate; make `IsAttached` return the field.

### BPF-034 -- Debug/Watch/Callstack window `DrawUI()` bodies are empty stubs (editor)
- **Lens:** SC-anchor | **Design:** Editor DD §8.2, §8.5, §8.7 (Slice-1 requires real rendering) | **Code:** `DebugPanelWindow.cs` (L16-20), `WatchPanelWindow.cs` (L24-27), `CallstackWindow.cs` (L18-21)
- **Gap:** All three `DrawUI()` are `// ... Stub for Slice 1.` with no rendering. "Stub for Slice 1" is the implementer's phrase, not the design's. Tests check only Title + subscription lifecycle.
- **Fix:** implement the panels per §8.2/§8.5/§8.7.

### BPF-035 -- `IWindowRegistrar` contract mismatch; `BlueprintWindowRegistrar`/DI registration absent; windows never registered (editor)
- **Lens:** integration-seam | **Design:** Editor DD §3.1, §3.2 | **Code:** `Hrot.Blueprints.Editor/IWindowRegistrar.cs`; `BlueprintEditorModule.cs` `RegisterWindow` (L57)
- **Gap:** The Blueprint-local `IWindowRegistrar` exposes `RegisterMenuEntry/ToolbarEntry/Shortcut` — not the engine's `RegisterWindows(...)`. No `BlueprintWindowRegistrar`/`BlueprintEditorModuleRegistration` exists; `RegisterWindow` and `AddBlueprintEditor` have zero production callers -> all 8 editor windows are never registered/drawn in production.
- **Fix:** implement the engine `IWindowRegistrar` pattern and wire the DI registration so windows actually load.

## MEDIUM

### BPF-036 -- `OnHotReloadCompleted` clears `Watch.IsStale` unconditionally -> deleted-pin watches show frozen values (debug)
- **Lens:** invariant | **Design:** Debug DD §11.3 | **Code:** `BlueprintDebugSession.cs` `OnHotReloadCompleted` (L483-494)
- **Gap:** Un-stales every watch on a reloaded asset without checking the pin still exists in the new map; a watch on a removed pin appears live but never updates. (Refuter: the `RebuildWatchStringIndex`/`TryResolvePinFromGuid` the design names don't exist; fix must check pin survival via the new map's StructureHash + pin set.)
- **Fix:** only clear stale for watches whose pin still resolves post-reload.

### BPF-037 -- `AtomicMultiFileWriter` rollback/partial-apply path has no non-vacuous test (shared-infra)
- **Lens:** SC-anchor | **Design:** AI Editor Shared §16.5; ACCEPTANCE-CRITERIA Q7-03 | **Code:** `Hrot.Editor.AiShared/Refactor/AtomicMultiFileWriter.cs` `Write` (L27-36) + its tests
- **Gap:** Production move-phase handling is correct, but all failure tests inject a bad *directory* (fails in the temp-write phase), so the mid-`File.Move` partial-batch path and `SuccessfullyWritten` population are never exercised. ACCEPTANCE Q7-03 (file-lock mid-batch) is unmet.
- **Fix:** add a mid-move failure test (lock/readonly second target).

### BPF-038 -- HardReload integration test never asserts `InstanceVersion` bump it claims to cover (runtime)
- **Lens:** SC-anchor | **Design:** Runtime DD §9.1, §5.7, §11.4 SC4 | **Code:** `Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs` (L21-69)
- **Gap:** `HardReload_..._BumpsVersion` asserts only payload reset (`TickCount==1`), never reads `InstanceVersion`. The design's `GetSlotEntry`-based assertion can't run because that fixture method is absent (BPF-008). Unit-level `ResetSlot` test does cover the increment.
- **Fix:** add `GetSlotEntry` (BPF-008) and assert the version bump at integration level.

### BPF-039 -- `GetOrdered` appends residual fields via `dict.Values` (non-deterministic) (compiler)
- **Lens:** invariant | **Design:** Compiler DD §12.2 M-1, §12.3 | **Code:** `Stage5_Schedule.cs` `GetOrdered` (~L125-126)
- **Gap:** `result.AddRange(dict.Values)` over a `Dictionary<Guid,IrField>` violates the "OrderBy before any emit-path Dictionary iteration" rule; cross-process Guid hash order can break byte-identical output (and Roslyn incremental caching). A `DeterministicEnumerable` helper exists but has 0 callers.
- **Fix:** `.OrderBy(f => f.Id)` the residual append.

### BPF-040 -- `MetadataReferenceResolver` does not sort references (determinism M-9) (compiler)
- **Lens:** invariant | **Design:** Compiler DD §12.2 M-9 | **Code:** `Compiler/Roslyn/MetadataReferenceResolver.cs` `ForRuntimeAssemblies`; `InMemoryRoslynCompiler.cs` `Compile`
- **Gap:** References built from `AppDomain.GetAssemblies()` with no `.OrderBy(r => r.Display)` before `CSharpCompilation.Create`; `deterministic:true` doesn't normalize input order. Cross-restart non-determinism. (Note: the design's own §11.3 sample also omits the sort — internal doc inconsistency; M-9 is authoritative.)
- **Fix:** sort references by `Display`.

### BPF-041 -- Stage8 PDB embedded-source test is a size heuristic, not content verification (compiler)
- **Lens:** SC-anchor | **Design:** Compiler DD §17.7 | **Code:** `Hrot.Blueprints.Tests/Stage8Tests.cs` `Stage8_PdbContainsEmbeddedSource`
- **Gap:** Asserts only `PortablePdb.Length > 500`; the design's `PortablePdbInspector.ExtractEmbeddedSource(...)==source` check (and the inspector helper) don't exist. A PDB with no/mismatched embedded source still passes.
- **Fix:** implement the inspector and assert extracted source equals generated source.

### BPF-042 -- `Fdp.Toolkits` `ApplyReload` injects the live `BehaviorRegistry` into registrars; partial failure corrupts it with no rollback (hot-reload)
- **Lens:** algorithm | **Design:** Hot Reload DD §4.4, §6.2-6.3 | **Code:** `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs` `ResolveRegistrarArgument` (L258), `ApplyReload` (L221-240)
- **Gap:** Returns the live `_behaviorRegistry` (not a staging copy) to registrars; a throwing registrar leaves it partially mutated with no rollback. The `Hrot.Editor` path and `ApplyQuickReload` correctly use a staging `BehaviorRegistry`+`MergeFrom`; the file-watcher `ApplyReload` does not.
- **Fix:** stage into a fresh `BehaviorRegistry` and merge only on full success.

### BPF-043 -- `Hrot.Editor` `DrainPendingCallbacks` drains the whole queue per frame, violating one-reload-per-frame bound (hot-reload)
- **Lens:** spec-drift | **Design:** Hot Reload DD §4.2 | **Code:** `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` `DrainPendingCallbacks` (L230)
- **Gap:** `while (_pendingReloads.TryDequeue(...))` processes all queued reloads in one call (unbounded per-frame spike under rapid saves). The `Fdp.Toolkits` variant correctly uses `if (!TryDequeue) return;`.
- **Fix:** dequeue at most one per frame.

### BPF-044 -- `Fdp.Toolkits` `DoLoadAndScan` silently swallows all background scan failures (hot-reload)
- **Lens:** integration-seam | **Design:** Hot Reload DD §3.3, §3.6, §6.1 | **Code:** `Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs` `DoLoadAndScan` (L354-359)
- **Gap:** `catch (Exception) { }` with a "logged by caller" comment, but the `ThreadPool` lambda caller logs nothing; the options type has no `Logger`, and `OnReloadFailed` is never fired from the scan path. Corrupt PE / missing dep -> silent no-op. The `Hrot.Editor` path calls `EnqueueFailure`.
- **Fix:** log and raise `OnReloadFailed` from the background scan.

### BPF-045 -- BTree trace events carry `Guid.Empty` `NodeVisualId` -> status glyphs / async badges never draw (btree-host)
- **Lens:** integration-seam | **Design:** BTree Host Design §12.4 steps 3-4 | **Code:** `BTreeDebugSession.cs` `Update` (L113-132)
- **Gap:** `BTreeNodeExecuted`/`BTreeAsyncEvent` constructed with `Guid.Empty` for AssetId and NodeVisualId (raw int index never symbolicated); overlay `FindNode(Guid.Empty)` -> null. Also the async-badge render block is entirely absent from the renderer. Same root cause as BPF-026.
- **Fix:** symbolicate trace records to VisualIds; add the async-badge render path.

### BPF-046 -- TierUpgrade contract test bypasses the ECB it claims to exercise (test-harness)
- **Lens:** SC-anchor | **Design:** Test Harness DD §4.8, §8.2 row 6, §8.3 | **Code:** `Hrot.Blueprints.Tests/Mocks/MockContractTests.cs` (L108-136)
- **Gap:** `TierUpgrade_HappensInBeforeSync_NotInSimulation` calls `World.AddComponent(...)` directly instead of `Ecb.AddEmptyComponent<BB4096>` — BB4096 is present before TickFrame, so the ECB-deferral / "not observable during Simulation" invariant is never tested.
- **Fix:** queue via the ECB and assert non-observability during Simulation.

### BPF-047 -- `ChildOrderDeterminismTests` test a List-backed stub, not any production model (nodeeditor)
- **Lens:** SC-anchor | **Design:** ContainerNodes §15.1 (NEC-10) | **Code:** `NodeEditor.Core.Tests/Serialization/ChildOrderDeterminismTests.cs`
- **Gap:** Tests a private `StubContainer` wrapping `new List<NodeId>` (order-preserving by construction); no production `IContainerNodeModel` (`FakeContainerModel`, HSM `StateNode`) is exercised. A Dictionary/Set-backed impl would pass the suite while breaking determinism.
- **Fix:** assert order on the real model types.

### BPF-048 -- No test covers drag-produced undo entries or ancestor suppression (nodeeditor)
- **Lens:** SC-anchor | **Design:** ContainerNodes §19.1, §11.1 | **Code:** `ContainerCommandsTests.cs`; missing `ContainerDragTests`
- **Gap:** `ContainerDragTests` does not exist; `ContainerCommandsTests` only asserts record field values (no command application, no UndoStack). So BPF-028/BPF-030 are invisible to the suite.
- **Fix:** add a GraphView-level drag test asserting undo entries + ancestor suppression.

### BPF-049 -- `BlueprintRegistry.GetAll()` returns values only, dropping the id (runtime; re-confirms BPF-007)
- **Lens:** spec-drift | **Design:** Runtime DD §2.2/§2.3 | **Code:** `Fdp.Toolkits/Blueprints/BlueprintRegistry.cs` (L72-76)
- **Gap:** Returns `IReadOnlyCollection<BlueprintDefinition>` (`.Values.ToArray()`); design returns `IEnumerable<(int Id, BlueprintDefinition Def)>`. Both callers are tests asserting only `.Count`. Future design-conformant callers break.
- **Fix:** return the `(Id, Def)` tuple shape.

## LOW

### BPF-050 -- Parallel-determinism compiler test (§17.8) not implemented (compiler)
- **Lens:** reachability | **Design:** Compiler DD §17.8 | **Code:** `Hrot.Blueprints.Tests/Compiler/Determinism/CompilerDeterminismTests.cs`
- **Gap:** Only sequential two-run determinism tests exist; the required `Compile_ParallelCalls_AllProduceSameOutput` (16x `AsParallel()`) that would catch `EmissionContext`/static-field races is absent.
- **Fix:** add the parallel determinism test.

---

## Verification Coverage (updated after Part 2)

The deep workflow audited all 14 risk-ranked clusters. **breakpoints-substrate** and **breakpoints-orchestration**
produced **zero** surviving findings — the universal-breakpoints P1-P12 implementation (DataBreakpointSystem
predicate eval/IL emission, triple-buffer gate, ECB drain, hot-path hardening) held up to adversarial scrutiny.
The compiler **emit** path is the highest-defect area (7 confirmed, incl. 3 Critical: probes-as-comments,
event-arg mismatch, custom-event-as-comment). The blueprint **editor** windows (Part-1 BPF + BPF-031..035) are
largely non-functional/unwired in production. The bug density strongly suggests the generated-code path
(`StatementEmitter`) and the editor window-registration path were never executed end-to-end — only unit-tested
around stubs. A follow-up pass should add end-to-end "compile a blueprint, run it under a debug session, hit a
breakpoint" and "open the editor, see windows + hot-reload log" integration tests, which would have caught
BPF-015/031/033/034/035 immediately.
