# Design Conformance Fixes -- Round 2 -- Task Tracker

Re-fix list for round-1 fixes that verification found **partial / not-fixed / regressed**.
Full details + live code refs in [TASK-DETAIL.md](./TASK-DETAIL.md). Source findings:
`../blueprint-fixes-1/TASK-DETAIL.md` (BPF-*), `../other-fixes-1/TASK-DETAIL.md` (OFX-*).

Round-1 verification: 50 confirmed fixed (auto) + 4 Criticals fixed (by hand); **22 need another pass**
(below). Two dominant failure modes: (1) scaffolding added but never wired to a production caller
(dead code); (2) "fix" tests that bypass the real path. A fix is done only when production code reaches
the new code AND a test drives the production path.

---

## A. Feature still non-functional at runtime (highest priority)
- [ ] **FIX2-001** (Critical, BPF-015) -- probe emits node-id `:N`, matcher keys `:D` -> breakpoints still never fire; change probe to `:D` -> [details](./TASK-DETAIL.md#fix2-001----debug-probe-node-id-format-mismatch---breakpoints-still-never-fire-source-bpf-015)
- [ ] **FIX2-002** (High, BPF-002/021) -- DebugMap builder API added but emitter never calls it -> all fields empty at runtime; wire emit + populate NodeKind/DisplayName -> [details](./TASK-DETAIL.md#fix2-002----debugmap-fields-are-emitted-empty-builder-api-added-but-the-emitter-never-calls-it-source-bpf-002--bpf-021)
- [ ] **FIX2-003** (High, BPF-003) -- `OnNewTick()` never called in production -> per-frame dedup latches; call it at tick boundary -> [details](./TASK-DETAIL.md#fix2-003----breakpoint-per-frame-dedup-never-resets-onnewtick-has-no-production-caller-source-bpf-003)
- [ ] **FIX2-004** (High, BPF-033) -- `Attach()/Detach()` exist but `BlueprintEditorModule` never calls them -> `DebugProbe.Sink` never routed -> [details](./TASK-DETAIL.md#fix2-004----debugprobesink-never-routed-attachdetach-exist-but-blueprinteditormodule-never-calls-them-source-bpf-033)
- [ ] **FIX2-005** (High, BPF-035) -- registrar doesn't implement engine `IWindowRegistrar`, not in DI, no caller -> windows never registered -> [details](./TASK-DETAIL.md#fix2-005----blueprint-editor-windows-never-registered-registrar-doesnt-implement-the-engine-interface-and-isnt-in-di-source-bpf-035)
- [ ] **FIX2-006** (High, BPF-034) -- debug panels fetch data then discard (no rendering); Callstack uses wrong API; render + add `GetCurrentCallStack()` -> [details](./TASK-DETAIL.md#fix2-006----debugwatchcallstack-panels-fetch-data-then-discard-it-no-rendering-callstack-uses-the-wrong-api-source-bpf-034)
- [ ] **FIX2-007** (High, BPF-026) -- BTree overlay blank: `SetDebugMetadata()` no production caller; wire on asset load -> [details](./TASK-DETAIL.md#fix2-007----btree-runtime-overlay-still-blank-setdebugmetadata-has-no-production-caller-source-bpf-026)
- [ ] **FIX2-008** (High, OFX-012) -- `LookAtChannelIntentEgressTranslator` still omits ActionParams blob compare -> [details](./TASK-DETAIL.md#fix2-008----lookatchannelintentegresstranslator-still-omits-the-actionparams-blob-compare-source-ofx-012)

## B. Partial implementations (still diverge from design)
- [ ] **FIX2-009** (Medium, BPF-001) -- Instance-dispatch state inspection still a stub (`CaptureInstanceStateFromDefinition` empty) -> [details](./TASK-DETAIL.md#fix2-009----instance-dispatch-state-inspection-is-still-a-stub-source-bpf-001)
- [ ] **FIX2-010** (Medium, BPF-010) -- HSM snapshot EventQueue/TimerSlots/HistorySlots still empty; add decode helpers -> [details](./TASK-DETAIL.md#fix2-010----hsm-snapshot-eventqueue--timerslots--historyslots-still-empty-source-bpf-010)
- [ ] **FIX2-011** (Medium, BPF-022) -- HSM deferred events: projector never populates; no blob storage; vacuous test -> [details](./TASK-DETAIL.md#fix2-011----hsm-deferred-events-projector-never-populates-them-no-blob-storage-vacuous-test-source-bpf-022)
- [ ] **FIX2-012** (Medium, BPF-025) -- HSM projector transitions & regions still positional-sort; use `TransitionVisualIds` -> [details](./TASK-DETAIL.md#fix2-012----hsm-projector-transitions--regions-still-use-positional-sort-identity-source-bpf-025)
- [ ] **FIX2-013** (Medium, BPF-045) -- BTree async-badge overlay render path still missing (§12.4 step 4) -> [details](./TASK-DETAIL.md#fix2-013----btree-async-badge-overlay-still-missing-source-bpf-045)
- [ ] **FIX2-014** (Medium, OFX-003) -- FakeAnimationBackend still runs off managed Dictionary; only Generation mirrored; + `_entityIndexToEntity` leak -> [details](./TASK-DETAIL.md#fix2-014----fakeanimationbackend-still-runs-off-the-managed-dictionary-only-generation-is-mirrored-source-ofx-003)

## C. Bookkeeping / not-started / test-quality (lower priority)
- [ ] **FIX2-015** (Low, BPF-011) -- DEBT-018/022 unaddressed; addressed DEBT rows not marked RESOLVED -> [details](./TASK-DETAIL.md#fix2-015----blueprints-1-open-debt-only-partly-addressed-rows-not-marked-resolved-source-bpf-011)
- [ ] **FIX2-016** (Low, BPF-012) -- mark blueprints-2 D-03/D-04 RESOLVED (code fixed, tracker stale) -> [details](./TASK-DETAIL.md#fix2-016----blueprints-2-debt-tracker-inconsistent-with-code-source-bpf-012)
- [ ] **FIX2-017** (Low-Med, BPF-013) -- breakpoints-1 D-BP-01/02/04 not started (D-BP-04 = Blueprint canvas breakpoint menu unreachable) -> [details](./TASK-DETAIL.md#fix2-017----breakpoints-1-debt-not-started-source-bpf-013-only-not-fixed-item)
- [ ] **FIX2-018** (Low, BPF-027) -- add Roslyn compile assertion to BTree composite emitter tests -> [details](./TASK-DETAIL.md#fix2-018----btree-composite-emitter-add-the-roslyn-compile-assertion-source-bpf-027)
- [ ] **FIX2-019** (Low, BPF-037) -- AtomicMultiFileWriter partial-batch `SuccessfullyWritten` still untested (single-file test) -> [details](./TASK-DETAIL.md#fix2-019----atomicmultifilewriter-partial-batch-successfullywritten-still-untested-source-bpf-037)
- [ ] **FIX2-020** (Low, BPF-047) -- ChildOrderDeterminismTests still test a local stub, not the production model -> [details](./TASK-DETAIL.md#fix2-020----childorderdeterminismtests-still-test-a-local-stub-not-a-production-model-source-bpf-047)
- [ ] **FIX2-021** (Low-Med, OFX-015) -- emitter round-trip test skips reflect step; weight/context unchecked; sorted-away ordering -> [details](./TASK-DETAIL.md#fix2-021----utility-emitter-round-trip-test-added-but-skips-the-reflect-step-source-ofx-015)

---

### Status legend
- [ ] open  /  [x] fixed (production path reached + a test drives it)
- Do not delete rows; mark resolved instead.

### Summary
22 items: 8 High (feature non-functional at runtime), 6 Medium (partial), 8 Low (bookkeeping/test-quality).
Most are "added the code but never wired a production caller" or "test bypasses the real path".
The other 54 round-1 fixes (incl. Criticals BPF-014/016/017/018 and the OFX algorithm fixes) verified clean.
