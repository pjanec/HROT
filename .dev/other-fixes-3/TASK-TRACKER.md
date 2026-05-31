# Design Conformance Fixes -- Round 3 -- Task Tracker

Remaining stragglers after re-verifying the round-2 re-fixes (`other-fixes-2` FIX2-*).
Full details + live code refs in [TASK-DETAIL.md](./TASK-DETAIL.md).

Round-3 verification: **18 of 21 round-2 re-fixes confirmed fixed, 3 partial, 0 not-fixed, no new bugs.**
A fix is done only when production code reaches the new code AND a test drives the production path.

> **Final re-check (by Claude, after the 3 stragglers were marked done):**
> - **FIX3-001 ✓ verified fixed** — `EditorSubsystem` (an `ISubsystem` + `IWindowRegistrar`) builds the
>   registrar in `Initialize` and its `RegisterWindows` delegates to it; `LocalWindowController` reaches it.
>   Real production caller now exists.
> - **FIX3-003 ✓ fixed by Claude** — the StateNode child-order test was still missing; added
>   `State_ChildNodeIds_PreserveInsertionOrder` (HsmGraphModelTests.cs). Project builds (0 warn/0 err),
>   test passes.
> - **FIX3-002 — D-BP-04 still NOT implemented** (TODO placeholder in `GraphEditorWindow.DrawUI`); it is
>   genuinely blocked on the unimplemented canvas (no rendered/hit-testable nodes to attach a right-click
>   menu to). Left as a deferral requiring a product decision; not safe to hack. D-BP-01 fixed, D-BP-02
>   acceptable documented deferral.

---

- [x] **FIX3-001** (High, FIX2-005 <- BPF-035) -- `BlueprintWindowRegistrar` now implements the engine `IWindowRegistrar` + is in DI + has a real test, but **no production caller** reaches it (`LocalWindowController` iterates `ISubsystem[]`; the registrar isn't one) -> windows still unregistered at runtime. Wire it into the subsystem window-registration pass. -> [details](./TASK-DETAIL.md#fix3-001----blueprint-editor-windows-registrar-is-correct-but-still-has-no-production-caller-fix2-005---bpf-035)
- [ ] **FIX3-002** (Low-Med, FIX2-017 <- BPF-013) -- **DEFERRED, decision needed.** D-BP-01 fixed; D-BP-02 acceptable documented deferral. **D-BP-04** (Blueprint-canvas right-click breakpoint menu) is still a `TODO(D-BP-04)` placeholder in `GraphEditorWindow.DrawUI` -- genuinely blocked on the unimplemented canvas node-rendering/hit-testing; not hackable as a small fix. Needs a product decision: implement the canvas batch, or accept the deferral. -> [details](./TASK-DETAIL.md#fix3-002----breakpoints-1-debt-d-bp-01-fixed-d-bp-02d-bp-04-still-unimplemented-fix2-017---bpf-013)
- [x] **FIX3-003** (Low, FIX2-020 <- BPF-047) -- FIXED by Claude: added `State_ChildNodeIds_PreserveInsertionOrder` to `HsmGraphModelTests.cs` exercising production `StateNode.ChildNodeIds` insertion-order + stability. Builds clean, test passes. -> [details](./TASK-DETAIL.md#fix3-003----childorderdeterminism-now-tests-demo-fakecontainermodel-but-statenode-still-not-covered-fix2-020---bpf-047)

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
