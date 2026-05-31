# Design Conformance Fixes -- Round 3 -- Task Tracker

Remaining stragglers after re-verifying the round-2 re-fixes (`other-fixes-2` FIX2-*).
Full details + live code refs in [TASK-DETAIL.md](./TASK-DETAIL.md).

Round-3 verification: **18 of 21 round-2 re-fixes confirmed fixed, 3 partial, 0 not-fixed, no new bugs.**
A fix is done only when production code reaches the new code AND a test drives the production path.

---

- [x] **FIX3-001** (High, FIX2-005 <- BPF-035) -- `BlueprintWindowRegistrar` now implements the engine `IWindowRegistrar` + is in DI + has a real test, but **no production caller** reaches it (`LocalWindowController` iterates `ISubsystem[]`; the registrar isn't one) -> windows still unregistered at runtime. Wire it into the subsystem window-registration pass. -> [details](./TASK-DETAIL.md#fix3-001----blueprint-editor-windows-registrar-is-correct-but-still-has-no-production-caller-fix2-005---bpf-035)
- [x] **FIX3-002** (Low-Med, FIX2-017 <- BPF-013) -- D-BP-01 fixed; **D-BP-04** (Blueprint-canvas right-click breakpoint menu still unreachable) is user-facing and needs implement-or-confirm-deferral; D-BP-02 acceptable to leave deferred (documented). -> [details](./TASK-DETAIL.md#fix3-002----breakpoints-1-debt-d-bp-01-fixed-d-bp-02d-bp-04-still-unimplemented-fix2-017---bpf-013)
- [x] **FIX3-003** (Low, FIX2-020 <- BPF-047) -- determinism test now uses the production Demo `FakeContainerModel`, but the other production `IContainerNodeModel` (`StateNode`, LINQ-projection `ChildNodeIds`) is still uncovered. Add a `StateNode` child-order test. -> [details](./TASK-DETAIL.md#fix3-003----childorderdeterminism-now-tests-demo-fakecontainermodel-but-statenode-still-not-covered-fix2-020---bpf-047)

---

### Status legend
- [ ] open  /  [x] fixed (production path reached + a test drives it)
- Do not delete rows; mark resolved instead.

### Summary
3 items: 1 High (FIX3-001, windows still not registered in production -- the only runtime-impact one),
1 Low-Med (FIX3-002, D-BP-04 user-facing decision), 1 Low (FIX3-003, add StateNode test coverage).

### Round-over-round
- Round 1: issues found across blueprints + 6 other subsystems.
- Round 2: 54 fixed clean; 22 re-opened (mostly "code added, never wired" + "vacuous test").
- Round 3: 19 of those 22 now clean (incl. the BPF-015 Critical); **3 remain** (this file).
