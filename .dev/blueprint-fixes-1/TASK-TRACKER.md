# Design Conformance Fixes -- Task Tracker

One line per issue. Check the box when fixed (or converted to a documented, intentional deviation).
Full descriptions, design references, and code locations are in [TASK-DETAIL.md](./TASK-DETAIL.md).

Confidence: `V` = verified against code; `R` = reported, re-confirm against the cited design section before changing.

---

## A. Debug Protocol (blueprints-1)

- [x] **BPF-001** (V, High) -- `GetCurrentStateSnapshot` is a stub; implement field-level capture per Debug DD §8.4-8.6 -> [details](./TASK-DETAIL.md#bpf-001----pause-time-state-inspection-getcurrentstatesnapshot-is-a-stub)
- [x] **BPF-002** (V, High) -- compiler debug-map omits `pins`/`graphs`/`stateLayout`/`assetName` (Debug DD §4.2-4.5); root cause of BPF-001/003 -> [details](./TASK-DETAIL.md#bpf-002----compiler-debug-map-omits-pins-graphs-statelayout-assetname)
- [x] **BPF-003** (V, High) -- breakpoint structure-hash safety, `IsStale`, and per-frame multi-entity dedup missing (Debug DD §5.2/§6.1/§9.2) -> [details](./TASK-DETAIL.md#bpf-003----breakpoint-structure-hash-safety-staleness-and-per-frame-multi-entity-dedup-missing)
- [x] **BPF-004** (V, Medium) -- peer-call probe signature diverges; asset-name matching is dead (Debug DD §2.4/§7.4) -> [details](./TASK-DETAIL.md#bpf-004----peer-call-probe-signature-diverges-asset-matching-is-dead)
- [x] **BPF-005** (R, Medium) -- StepOut tick-boundary (depth 0) + entity-death step abandonment missing (Debug DD §7.6/§9.5) -> [details](./TASK-DETAIL.md#bpf-005----stepout-tick-boundary-semantics-and-entity-death-step-abandonment-missing)

## B. Runtime (blueprints-1)

- [ ] **BPF-006** (V, Medium) -- `IReloadLogSink` reduced; restore `OnSoftReload` + entity/hash context (Runtime DD §9.7) -> [details](./TASK-DETAIL.md#bpf-006----ireloadlogsink-interface-reduced-vs-design-no-onsoftreload-no-entityhash-context)
- [ ] **BPF-007** (R, Low) -- `BlueprintRegistry.GetAll()` drops `(Id, Def)` tuple (Runtime DD §2.2/2.3) -> [details](./TASK-DETAIL.md#bpf-007----blueprintregistrygetall-drops-the-id-def-tuple)

## C. Test Harness (blueprints-1)

- [ ] **BPF-008** (V, Medium) -- fixture missing `SnapshotAllBlackboards`/`SetChannelStatus<T>`/`GetSlotEntry` (TH DD §2.4/5.4/5.6/5.7; DEBT-006/007/008) -> [details](./TASK-DETAIL.md#bpf-008----fixture-missing-snapshotallblackboards-setchannelstatust-getslotentry)
- [ ] **BPF-009** (V, Medium) -- `InvokeHsmAction`/`InvokeHsmGuard` still `NotImplementedException` stubs (TH DD §12.1-12.3) -> [details](./TASK-DETAIL.md#bpf-009----invokehsmaction--invokehsmguard-remain-notimplementedexception-stubs)

## D. AI Editor hosts (blueprints-2)

- [ ] **BPF-010** (R, Medium) -- `HsmInstanceSnapshot` active-states/events/timers/history left empty (HSM host design Slice 2; FIX1 HS-S2-01) -> [details](./TASK-DETAIL.md#bpf-010----hsminstancesnapshot-populated-with-empty-active-states--events--timers--history)

## E. Cross-cutting / already-tracked OPEN debt that diverges from design

- [ ] **BPF-011** (V, Low) -- close blueprints-1 OPEN debt (DEBT-003/004/018/021/022/023) -> [details](./TASK-DETAIL.md#bpf-011----blueprints-1-open-debt-that-diverges-from-design)
- [ ] **BPF-012** (V, Medium) -- close blueprints-2 OPEN debt (D-02 subtree resolution broken; D-01/D-03/D-04) -> [details](./TASK-DETAIL.md#bpf-012----blueprints-2-open-debt-that-diverges-from-design)
- [ ] **BPF-013** (V, Low-Med) -- close breakpoints-1 OPEN debt (D-BP-01/02/04) -> [details](./TASK-DETAIL.md#bpf-013----breakpoints-1-open-debt)

---

## Deeper second-pass verification (not yet done -- see TASK-DETAIL "Verification Coverage")

These areas were confirmed present + test-covered but not field-checked against their designs.
Open a sub-task here if/when a deeper pass runs.

- [ ] **BPF-V1** -- Compiler pipeline stages 1-8 vs Compiler_Detailed_Design (IR model, determinism, catalogs, emit) beyond debug-map (BPF-002)
- [ ] **BPF-V2** -- Editor (blueprints-1) windows/panels vs Editor_Detailed_Design
- [ ] **BPF-V3** -- Hot Reload (`AiHotReloadCoordinator`) reload sequencing / ALC swap / rollback vs Hot_Reload_Detailed_Design
- [ ] **BPF-V4** -- blueprints-2 NodeEditor extensions + BT/HSM hosts: 15-step Z-layer hit-test (FIX1 NEA-06/NEC-05/NER-04) and FIX1 stub-replacements spot-confirm
- [ ] **BPF-V5** -- breakpoints-1: 10 design Success Conditions vs P12 wired flow

---

# PART 2 -- Deep correctness audit (workflow-confirmed, 40 findings)

All adversarially re-verified (Sonnet hunt + refute, graph tools only). Severity = refuter's corrected severity.
Details + design/code refs: [TASK-DETAIL.md PART 2](./TASK-DETAIL.md#part-2----deep-correctness-audit-workflow-confirmed).

## CRITICAL
- [x] **BPF-014** (Critical, compiler) -- Instance LatentDelay resume reads `ws.__waitUntilTime` instead of `s.Cursor.WaitUntilTime` -> [details](./TASK-DETAIL.md#bpf-014----instance-latentdelay-resume-reads-workingstate-field-instead-of-the-cursor-compiler)
- [x] **BPF-015** (Critical, compiler) -- `DebugProbe.NodeEnter`/`PinValue` emitted as a comment -> all runtime breakpoints/steps/watches dead -> [details](./TASK-DETAIL.md#bpf-015----debugprobenodeenterpinvalue-emitted-as-a-c-comment-not-a-call-compiler-found-by-2-clusters)
- [x] **BPF-016** (Critical, compiler) -- event-poll call site omits payload args (+stray deltaTime) -> CS1501 uncompilable -> [details](./TASK-DETAIL.md#bpf-016----event-poll-call-site-omits-payload-args---uncompilable-generated-c-compiler)
- [x] **BPF-017** (Critical, hsm) -- `ActionNames` positional-indexed vs blob hash IDs -> all action/guard names garbled -> [details](./TASK-DETAIL.md#bpf-017----hsm-actionnames-keyed-by-positional-index-but-blob-stores-hashes---all-actionguard-names-garbled-hsm-host)
- [ ] **BPF-018** (Critical, btree) -- `SubtreeAssetIds` never populated -> projection `IndexOutOfRangeException`; emitter writes Guid not tree name -> [details](./TASK-DETAIL.md#bpf-018----btree-subtreeassetids-never-populated---projection-indexoutofrangeexception-emitter-writes-a-guid-where-a-tree-name-is-required-btree-host)

## HIGH
- [x] **BPF-019** (High, compiler) -- `BuildReturnTerminator` resolves into last-allocated block, not current -> use-before-define -> [details](./TASK-DETAIL.md#bpf-019----buildreturnterminator-resolves-return-value-into-the-last-allocated-block-not-the-current-block-compiler)
- [x] **BPF-020** (High, compiler) -- `IrOp_RaiseCustomEvent` emitted as a comment -> custom-event dispatch dropped -> [details](./TASK-DETAIL.md#bpf-020----irop_raisecustomevent-emitted-as-a-comment---custom-event-dispatch-silently-dropped-compiler)
- [x] **BPF-021** (High, compiler) -- DebugMap NodeKind/DisplayName never populated; RecordPin/GeneratedSourcePath absent (extends BPF-002) -> [details](./TASK-DETAIL.md#bpf-021----debugmap-nodekinddisplayname-never-populated-recordpin--generatedsourcepath-absent-compiler-extends-bpf-002)
- [x] **BPF-022** (High, hsm) -- `HsmFluentEmitter` never emits `DeferEvent()` -> deferred events dropped on save -> [details](./TASK-DETAIL.md#bpf-022----hsmfluentemitter-never-emits-deferevent---deferred-event-lists-dropped-every-save-hsm-host)
- [x] **BPF-023** (High, hsm) -- `HsmDebugSession.Update` hardcodes empty active-leaf/event/timer/history (localizes BPF-010) -> [details](./TASK-DETAIL.md#bpf-023----hsmdebugsessionupdate-hardcodes-empty-active-leafeventtimerhistory-arrays-hsm-host-localizes-bpf-010)
- [x] **BPF-024** (High, hsm) -- StepOver and StepOut share one predicate -> StepOut never reaches RTC quiescence -> [details](./TASK-DETAIL.md#bpf-024----hsm-stepover-and-stepout-use-an-identical-pause-predicate---stepout-never-reaches-rtc-quiescence-hsm-host)
- [x] **BPF-025** (High, hsm) -- layout `StableId` assigned by positional sort -> identity breaks on structural edit -> [details](./TASK-DETAIL.md#bpf-025----hsm-layout-stableid-assigned-by-positional-lexicographic-sort---identity-breaks-on-any-structural-edit-hsm-host)
- [ ] **BPF-026** (High, btree) -- `BTreeDebugSession.Update` never symbolicates running/stack VisualIds -> overlay blank -> [details](./TASK-DETAIL.md#bpf-026----btreedebugsessionupdate-never-symbolicates-runningelementidstack-visualids---overlay-shows-nothing-btree-host)
- [ ] **BPF-027** (High, btree) -- `EmitComposite` stray separator -> invalid C# for non-empty composites -> [details](./TASK-DETAIL.md#bpf-027----emitcomposite-emits-a-stray-separator-producing-invalid-c-for-non-empty-composites-btree-host)
- [ ] **BPF-028** (High, nodeeditor) -- drag node ops bypass undo stack (`Commands.Apply` not `Execute`) -> [details](./TASK-DETAIL.md#bpf-028----drag-based-node-ops-call-viewcommandsapply-directly-bypassing-the-undo-stack-nodeeditor)
- [ ] **BPF-029** (High, nodeeditor) -- multi-select drag emits N `ChangeParent` not one `ChangeParentMultiple` -> [details](./TASK-DETAIL.md#bpf-029----multi-selection-drag-emits-n-separate-changeparent-commands-instead-of-one-changeparentmultiple-nodeeditor)
- [ ] **BPF-030** (High, nodeeditor) -- missing ancestor-suppression -> child of selected container moves twice as far -> [details](./TASK-DETAIL.md#bpf-030----missing-ancestor-in-selection-suppression---child-of-a-selected-container-moves-twice-as-far-nodeeditor)
- [ ] **BPF-031** (High, editor) -- `HotReloadLogWindow` never subscribed to coordinator -> permanently empty -> [details](./TASK-DETAIL.md#bpf-031----hotreloadlogwindow-never-subscribed-to-coordinator-events---permanently-empty-at-runtime-editor)
- [ ] **BPF-032** (High, editor) -- HotReloadLogWindow tests call methods directly -> subscription contract untested -> [details](./TASK-DETAIL.md#bpf-032----hotreloadlogwindow-tests-call-methods-directly---subscription-contract-untested-editor)
- [ ] **BPF-033** (High, editor) -- `IsAttached` hardcoded true; no `Attach()`; editor never routes `DebugProbe.Sink` -> [details](./TASK-DETAIL.md#bpf-033----blueprintdebugsessionisattached-hardcoded-true-no-attach-editor-never-routes-debugprobesink-editor)
- [ ] **BPF-034** (High, editor) -- Debug/Watch/Callstack `DrawUI()` are empty stubs -> [details](./TASK-DETAIL.md#bpf-034----debugwatchcallstack-window-drawui-bodies-are-empty-stubs-editor)
- [ ] **BPF-035** (High, editor) -- `IWindowRegistrar` mismatch; registrar/DI absent; windows never registered -> [details](./TASK-DETAIL.md#bpf-035----iwindowregistrar-contract-mismatch-blueprintwindowregistrardi-registration-absent-windows-never-registered-editor)

## MEDIUM
- [ ] **BPF-036** (Medium, debug) -- `OnHotReloadCompleted` clears `Watch.IsStale` unconditionally -> frozen deleted-pin watches -> [details](./TASK-DETAIL.md#bpf-036----onhotreloadcompleted-clears-watchisstale-unconditionally---deleted-pin-watches-show-frozen-values-debug)
- [ ] **BPF-037** (Medium, shared-infra) -- `AtomicMultiFileWriter` mid-move rollback path untested (ACCEPTANCE Q7-03 unmet) -> [details](./TASK-DETAIL.md#bpf-037----atomicmultifilewriter-rollbackpartial-apply-path-has-no-non-vacuous-test-shared-infra)
- [ ] **BPF-038** (Medium, runtime) -- HardReload test never asserts `InstanceVersion` bump (needs BPF-008) -> [details](./TASK-DETAIL.md#bpf-038----hardreload-integration-test-never-asserts-instanceversion-bump-it-claims-to-cover-runtime)
- [x] **BPF-039** (Medium, compiler) -- `GetOrdered` appends residual fields via `dict.Values` (non-deterministic, M-1) -> [details](./TASK-DETAIL.md#bpf-039----getordered-appends-residual-fields-via-dictvalues-non-deterministic-compiler)
- [x] **BPF-040** (Medium, compiler) -- `MetadataReferenceResolver` doesn't sort references (M-9) -> [details](./TASK-DETAIL.md#bpf-040----metadatareferenceresolver-does-not-sort-references-determinism-m-9-compiler)
- [x] **BPF-041** (Medium, compiler) -- Stage8 PDB embedded-source test is a size heuristic, not content check -> [details](./TASK-DETAIL.md#bpf-041----stage8-pdb-embedded-source-test-is-a-size-heuristic-not-content-verification-compiler)
- [ ] **BPF-042** (Medium, hot-reload) -- `ApplyReload` injects live `BehaviorRegistry`; partial failure corrupts it, no rollback -> [details](./TASK-DETAIL.md#bpf-042----fdptoolkits-applyreload-injects-the-live-behaviorregistry-into-registrars-partial-failure-corrupts-it-with-no-rollback-hot-reload)
- [ ] **BPF-043** (Medium, hot-reload) -- `Hrot.Editor` `DrainPendingCallbacks` drains whole queue per frame -> [details](./TASK-DETAIL.md#bpf-043----hroteditor-drainpendingcallbacks-drains-the-whole-queue-per-frame-violating-one-reload-per-frame-bound-hot-reload)
- [ ] **BPF-044** (Medium, hot-reload) -- `DoLoadAndScan` swallows all background scan failures (no log/event) -> [details](./TASK-DETAIL.md#bpf-044----fdptoolkits-doloadandscan-silently-swallows-all-background-scan-failures-hot-reload)
- [ ] **BPF-045** (Medium, btree) -- trace events carry `Guid.Empty` NodeVisualId -> status/async overlays blank -> [details](./TASK-DETAIL.md#bpf-045----btree-trace-events-carry-guidempty-nodevisualid---status-glyphs--async-badges-never-draw-btree-host)
- [ ] **BPF-046** (Medium, test-harness) -- TierUpgrade contract test bypasses the ECB it claims to exercise -> [details](./TASK-DETAIL.md#bpf-046----tierupgrade-contract-test-bypasses-the-ecb-it-claims-to-exercise-test-harness)
- [ ] **BPF-047** (Medium, nodeeditor) -- `ChildOrderDeterminismTests` test a List stub, no production model -> [details](./TASK-DETAIL.md#bpf-047----childorderdeterminismtests-test-a-list-backed-stub-not-any-production-model-nodeeditor)
- [ ] **BPF-048** (Medium, nodeeditor) -- no test covers drag undo entries / ancestor suppression -> [details](./TASK-DETAIL.md#bpf-048----no-test-covers-drag-produced-undo-entries-or-ancestor-suppression-nodeeditor)
- [ ] **BPF-049** (Medium, runtime) -- `GetAll()` returns values only, drops id (re-confirms BPF-007) -> [details](./TASK-DETAIL.md#bpf-049----blueprintregistrygetall-returns-values-only-dropping-the-id-runtime-re-confirms-bpf-007)

## LOW
- [x] **BPF-050** (Low, compiler) -- parallel-determinism test (§17.8) not implemented -> [details](./TASK-DETAIL.md#bpf-050----parallel-determinism-compiler-test-178-not-implemented-compiler)

> Part-1 items re-confirmed by Part 2: **BPF-002** (extended by BPF-021), **BPF-006** (IReloadLogSink; now VERIFIED via runtime-allocator finding), **BPF-007** (= BPF-049), **BPF-010** (localized by BPF-023). Clusters with **zero** surviving findings: breakpoints-substrate, breakpoints-orchestration (universal-breakpoints P1-P12 held up).

---

### Status legend
- [ ] open  /  [x] fixed or converted to documented intentional deviation
- Do not delete rows; mark resolved instead.
