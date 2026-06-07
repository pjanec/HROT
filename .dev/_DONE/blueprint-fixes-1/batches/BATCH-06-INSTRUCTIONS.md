# BATCH-06: Hot Reload + Medium fixes + Cross-cutting Debt

**Batch Number:** BATCH-06  
**Tasks:** BPF-042, BPF-043, BPF-044, BPF-036, BPF-037, BPF-038, BPF-046, BPF-049, BPF-010, BPF-011, BPF-012, BPF-013  
**Source:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`  
**Tracker:** `.dev/blueprint-fixes-1/TASK-TRACKER.md`  
**Priority:** BPF-042 Critical hot-reload corruption; BPF-043/044 Medium hot-reload; BPF-036 debug; BPF-038/046 test quality; BPF-049 runtime  
**Dependencies:** BATCH-05 (done)

---

## Onboarding & Workflow

This is the final batch for `blueprint-fixes-1`. It closes all remaining open tasks:

1. **Hot Reload** (BPF-042, BPF-043, BPF-044): registry corruption on partial reload failure; frame-draining policy; background scan failure swallowed.
2. **Debug medium** (BPF-036): `OnHotReloadCompleted` clears watch staleness unconditionally.
3. **Shared infra** (BPF-037): `AtomicMultiFileWriter` rollback path untested.
4. **Test quality** (BPF-038, BPF-046): HardReload test missing `InstanceVersion` assertion; TierUpgrade test bypasses ECB.
5. **Runtime** (BPF-049): `GetAll()` also known as BPF-007 re-confirm (already done); but BPF-049 is listed as a re-confirm.
6. **Cross-cutting debt** (BPF-010, BPF-011, BPF-012, BPF-013): Close out known OPEN debt items that diverge from design.

### Required Reading (IN ORDER)
1. **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-042, BPF-043, BPF-044, BPF-036, BPF-037, BPF-038, BPF-046, BPF-049, BPF-010, BPF-011, BPF-012, BPF-013
2. **Hot Reload DD:** `.dev/blueprints-1/Blueprint_Subsystem_Hot_Reload_Detailed_Design.md`
3. **Debug DD:** sections for Watch staleness
4. **DEBT-TRACKER.md:** `.dev/blueprints-1/DEBT-TRACKER.md` and `.dev/blueprints-2/DEBT-TRACKER.md` (for BPF-011/012/013 cross-cutting debt closures)
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

## Hot Reload Tasks

### Task 1: BPF-042 -- ApplyReload corrupts live registry on partial failure

**Task Definition:** [BPF-042](../TASK-DETAIL.md#bpf-042----fdptoolkits-applyreload-injects-the-live-behaviorregistry-into-registrars-partial-failure-corrupts-it-with-no-rollback-hot-reload)

**Success Condition:** If a registrar throws during `ApplyReload`, the live `BehaviorRegistry` is left in its pre-reload state (no partial registration). The rollback must actually revert the registry to its prior state.

**What to do:**
1. Read `ApplyReload` in `FDP/Toolkits/Fdp.Toolkits`
2. Snapshot the registry before reload; on exception, restore the snapshot
3. Write a test: inject a registrar that throws; assert registry unchanged after the failed reload

**Tests Required:**
- Registry unchanged after partial-failure reload
- Successful reload applies all registrations

---

### Task 2: BPF-043 -- DrainPendingCallbacks drains whole queue per frame

**Task Definition:** [BPF-043](../TASK-DETAIL.md#bpf-043----hroteditor-drainpendingcallbacks-drains-the-whole-queue-per-frame-violating-one-reload-per-frame-bound-hot-reload)

**Success Condition:** `DrainPendingCallbacks` applies at most 1 reload per frame call. Tests must verify that two enqueued reloads result in 1 applied per call, with the second applied on the next call.

**What to do:**
1. Find `DrainPendingCallbacks` in `Hrot.Editor` or similar
2. Add a `limit = 1` guard (drain at most 1 per call)
3. Write a test: enqueue 2 reloads; call drain once; assert 1 applied; call drain again; assert 2 total applied

**Tests Required:**
- At most 1 reload applied per drain call
- Second reload applied on subsequent drain call

---

### Task 3: BPF-044 -- DoLoadAndScan swallows background scan failures

**Task Definition:** [BPF-044](../TASK-DETAIL.md#bpf-044----fdptoolkits-doloadandscan-silently-swallows-all-background-scan-failures-hot-reload)

**Success Condition:** When `DoLoadAndScan` catches an exception during background scan, it must log/report the failure via `IReloadLogSink` (not silently swallow). Tests must verify the sink receives an error notification.

**What to do:**
1. Find `DoLoadAndScan` in the hot reload pipeline
2. Add a `catch` block that calls the sink (or a logger)
3. Write a test: inject a scan that throws; assert sink received a failure notification

**Tests Required:**
- Scan failure propagated to `IReloadLogSink` (not silently swallowed)

---

## Debug Medium Tasks

### Task 4: BPF-036 -- OnHotReloadCompleted clears Watch.IsStale unconditionally

**Task Definition:** [BPF-036](../TASK-DETAIL.md#bpf-036----onhotreloadcompleted-clears-watchisstale-unconditionally---deleted-pin-watches-show-frozen-values-debug)

**Success Condition:** `OnHotReloadCompleted` must clear `IsStale` only for watches whose pin still exists in the new debug map. Watches for deleted pins remain stale.

**What to do:**
1. Find `OnHotReloadCompleted` in `BlueprintDebugSession.cs`
2. Fix to check new debug map for pin presence before clearing `IsStale`
3. Write a test: create a watch for pin A and pin B; reload without pin B; assert pin A watch is not stale, pin B watch is still stale

**Tests Required:**
- Watch for existing pin cleared after reload
- Watch for deleted pin remains stale after reload

---

## Shared Infra Tasks

### Task 5: BPF-037 -- AtomicMultiFileWriter rollback path untested

**Task Definition:** [BPF-037](../TASK-DETAIL.md#bpf-037----atomicmultifilewriter-rollbackpartial-apply-path-has-no-non-vacuous-test-shared-infra)

**Success Condition:** A test that injects a mid-write failure and verifies the rollback leaves the file system in the pre-write state (no partial writes). The test must exercise the actual rollback code path.

**What to do:**
1. Find `AtomicMultiFileWriter`
2. Inject a failure mid-write (e.g., second file write throws)
3. Assert first file was not left on disk (rollback completed)
4. Write a test with an actual temp directory

**Tests Required:**
- Mid-write failure causes rollback; no partial files left on disk

---

## Test Quality Tasks

### Task 6: BPF-038 -- HardReload test missing InstanceVersion assertion

**Task Definition:** [BPF-038](../TASK-DETAIL.md#bpf-038----hardreload-integration-test-never-asserts-instanceversion-bump-it-claims-to-cover-runtime)

**Success Condition:** The HardReload test must assert `entity.InstanceVersion` (or equivalent) is bumped after a hard reload.

**What to do:**
1. Find the HardReload integration test
2. Read the current assertions
3. Add assertion that `InstanceVersion` increased after hard reload

**Tests Required:**
- HardReload test asserts `InstanceVersion` bumped

---

### Task 7: BPF-046 -- TierUpgrade test bypasses ECB it claims to exercise

**Task Definition:** [BPF-046](../TASK-DETAIL.md#bpf-046----tierupgrade-contract-test-bypasses-the-ecb-it-claims-to-exercise-test-harness)

**Success Condition:** The TierUpgrade test must invoke the actual `EntityCommandBuffer` path (not stub it out). The ECB must be applied and the result verified.

**What to do:**
1. Find the TierUpgrade test
2. Read what ECB operations are being bypassed
3. Fix to use the real ECB and assert the upgrade result

**Tests Required:**
- TierUpgrade applies via ECB and component is visible after upgrade

---

## Runtime Task

### Task 8: BPF-049 -- GetAll() drops Id (re-confirm BPF-007)

**Task Definition:** [BPF-049](../TASK-DETAIL.md#bpf-049----blueprintregistrygetall-returns-values-only-dropping-the-id-runtime-re-confirms-bpf-007)

This task re-confirms BPF-007 which was completed in BATCH-05. Verify `BlueprintRegistry.GetAll()` now returns tuples. If already done, mark as verified and move on. No additional work needed if BPF-007 is confirmed.

---

## Cross-cutting Debt Tasks

### Task 9: BPF-010 -- HsmInstanceSnapshot populated with empty arrays (localized by BPF-023)

**Task Definition:** [BPF-010](../TASK-DETAIL.md#bpf-010----hsminstancesnapshot-populated-with-empty-active-states--events--timers--history)

BPF-023 (BATCH-03) fixed `HsmDebugSession.Update` to decode active leaves. Verify that `HsmInstanceSnapshot` now has populated `ActiveStateIds` and `ActiveEventIds` arrays from the session snapshot. If BPF-023 fully addressed this, mark as resolved.

---

### Task 10: BPF-011 -- Close blueprints-1 OPEN debt (DEBT-003/004/018/021/022/023)

**Task Definition:** [BPF-011](../TASK-DETAIL.md#bpf-011----blueprints-1-open-debt-that-diverges-from-design)

Read each DEBT item. For each one:
- If already fixed: add a comment in `DEBT-TRACKER.md` referencing the BPF that fixed it
- If not yet addressed: implement the fix or document as intentional deviation

---

### Task 11: BPF-012 -- Close blueprints-2 OPEN debt (D-02 subtree resolution)

**Task Definition:** [BPF-012](../TASK-DETAIL.md#bpf-012----blueprints-2-open-debt-that-diverges-from-design)

D-02 subtree resolution was the main debt item. BPF-018 (BATCH-04) fixed `SubtreeAssetIds`. Verify the debt tracker is updated accordingly for D-01/D-03/D-04.

---

### Task 12: BPF-013 -- Close breakpoints-1 OPEN debt (D-BP-01/02/04)

**Task Definition:** [BPF-013](../TASK-DETAIL.md#bpf-013----breakpoints-1-open-debt)

Review D-BP-01/02/04 debt items. For each one:
- Verify if prior batches fixed it
- Update `DEBT-TRACKER.md` to reflect status

---

## Quality Standards

- **BPF-042**: Test must inject a throwing registrar and assert registry is unmodified afterward
- **BPF-043**: Test must call drain twice and assert counts (1, then 2), not just "does not throw"
- **BPF-036**: Test must have a watch for an ABSENT pin remaining stale (not just a present pin being unstaled)

## Report

Write report to:
`d:\WORK\IOS-IG-SimHost-FDP\.dev\blueprint-fixes-1\reports\BATCH-06-REPORT.md`

Use the same format as previous reports.

## Success Criteria

This batch is DONE when:
- [ ] BPF-042: Registry rollback on partial failure; test passes
- [ ] BPF-043: Drain at most 1 per frame; test passes
- [ ] BPF-044: Scan failure reported to sink; test passes
- [ ] BPF-036: Stale watches for deleted pins remain stale; test passes
- [ ] BPF-037: Rollback leaves no partial files; test passes
- [ ] BPF-038: HardReload test asserts InstanceVersion bump
- [ ] BPF-046: TierUpgrade test uses real ECB; test passes
- [ ] BPF-049: GetAll() confirmed to return tuples (BPF-007 done)
- [ ] BPF-010: HsmInstanceSnapshot arrays confirmed populated (BPF-023 done)
- [ ] BPF-011: blueprints-1 DEBT items reviewed and updated
- [ ] BPF-012: blueprints-2 DEBT D-02 confirmed fixed; others updated
- [ ] BPF-013: breakpoints-1 DEBT items reviewed and updated
- [ ] All pre-existing tests still pass
- [ ] Report submitted

## Reference Materials
- **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`
- **Hot Reload DD:** `.dev/blueprints-1/Blueprint_Subsystem_Hot_Reload_Detailed_Design.md`
- **BATCH-05 Review:** `.dev/blueprint-fixes-1/reviews/BATCH-05-REVIEW.md`
