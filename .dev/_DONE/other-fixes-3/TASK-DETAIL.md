# Design Conformance Fixes -- Round 3 (remaining stragglers)

Re-verification of the round-2 re-fixes (`other-fixes-2` FIX2-*) found **18 of 21 fully fixed** and
**3 still partial** (0 not-fixed, no new bugs introduced). This is the round-3 work list.

> Companion tracker: [TASK-TRACKER.md](./TASK-TRACKER.md). Lineage: each item traces FIX2-* (round 2)
> back to the original BPF-*/OFX-* finding. Open the prior entries for full history:
> `../other-fixes-2/TASK-DETAIL.md`, then `../blueprint-fixes-1/TASK-DETAIL.md` / `../other-fixes-1/TASK-DETAIL.md`.

## Verification method
One Sonnet agent per FIX2 item, reading the **live** source (the codebase-memory graph index predates the
fixes). Agents were required to confirm, where applicable, that a real **production caller** now reaches the
new code (via `trace_path` inbound / targeted caller search) and that a **non-vacuous test** drives the
production path -- not merely that a method body changed.

## Progress across rounds
Round 1: many issues found -> Round 2: 54 clean, 22 re-opened -> Round 3: **19 of 22 now clean (incl. the
BPF-015 Critical), 3 remain**. All three are the "last wiring mile" or a residual test-coverage tail.

Each item: **FIX3-NNN** | lineage | severity | what's now DONE | what REMAINS | fix.

---

### FIX3-001 -- Blueprint editor windows: registrar is correct but still has no production caller (FIX2-005 <- BPF-035)
- **Severity:** High (feature non-functional at runtime)
- **Done (round 2):** `BlueprintWindowRegistrar` now implements the engine `Fdp.Toolkit.Runner.IWindowRegistrar` (`BlueprintWindowRegistrar.cs:15`), bridges to `WindowManager` via inner `WindowManagerRegistry` (`:85-99`); DI registers both the class and the `IWindowRegistrar` mapping (`BlueprintEditorServiceCollectionExtensions.cs:19-21`); and the test now drives the real `IWindowRegistrar -> WindowManager` path end-to-end (`BlueprintWindowRegistrarTests.cs:81-104`) -- no longer vacuous.
- **Remains (still dead at runtime):** the production entry point `LocalWindowController.cs:54-56` iterates `_subsystems` (`ISubsystem[]`) and casts each to `IWindowRegistrar`. `BlueprintWindowRegistrar` is a standalone DI service, **not** an `ISubsystem`, so it's never in that list; and neither `EditorSubsystem.RegisterWindows` (`EditorSubsystem.cs:1450`) nor `SimHostSubsystem.RegisterWindows` (`SimHostSubsystem.cs:212`) delegates to it. No production code resolves the DI `IWindowRegistrar` and calls `RegisterWindows(wm)`. So the 7 blueprint windows are still never registered in production.
- **Fix:** make the registrar reachable from the real lifecycle -- either (a) have `EditorSubsystem.RegisterWindows`/`SimHostSubsystem.RegisterWindows` resolve `BlueprintWindowRegistrar` from DI and call `RegisterWindows(wm)`, or (b) make `BlueprintWindowRegistrar` an `ISubsystem` that `LocalWindowController` already iterates, or (c) register it into `_subsystems`. Add an integration test that runs the actual `LocalWindowController` window-registration pass and asserts the blueprint windows appear in the `WindowManager`.

### FIX3-002 -- breakpoints-1 debt: D-BP-01 fixed; D-BP-02/D-BP-04 still unimplemented (FIX2-017 <- BPF-013)
- **Severity:** Low-Medium (D-BP-04 is user-facing)
- **Done (round 2):** D-BP-01 fixed -- `CgfSubsystem.cs:799-804` now has `_bpManager` + `IsPausedByDebugger => _bpManager?.IsPaused ?? false` + `SetManager(...)`; DEBT-TRACKER marks D-BP-01 RESOLVED.
- **Remains (now explicitly deferred in the DEBT-TRACKER, but not implemented):**
  - **D-BP-02** -- `CgfSubsystem.cs:522-523` `_bpPreTickSnapshot` still mirrors only `CgfComponentRegistry`. Deferral rationale recorded: "no API on `HrotNodeBuilder` to enumerate its internal component registrations beyond `CgfComponentRegistry`." This is a reasonable documented deferral.
  - **D-BP-04** -- `GraphEditorWindow.cs:63-111` `DrawUI()` is still a canvas placeholder (`ImGui.TextDisabled` asset name); no right-click handler calls `BlueprintBreakpointMenuPopulator.PopulateNodeMenu`. So the Blueprint-canvas breakpoint menu is **unreachable via the UI**. Deferral rationale: "deferred to canvas implementation batch."
- **Fix / decision needed:** D-BP-02 is acceptable to leave deferred (mark intentional). **D-BP-04 is user-facing** -- decide: implement the right-click breakpoint menu now, or formally accept the deferral until the canvas batch lands. (The round-2 instruction said "implement per design, or if still deferred confirm with the user" -- this needs that confirmation.)

### FIX3-003 -- ChildOrderDeterminism: now tests Demo `FakeContainerModel`, but `StateNode` still not covered (FIX2-020 <- BPF-047)
- **Severity:** Low (test coverage)
- **Done (round 2):** `ChildOrderDeterminismTests.cs:2` now `using NodeEditor.Demo.FakeBlueprint;` and all four tests instantiate the **production** `NodeEditor.Demo.FakeBlueprint.FakeContainerModel` (`FakeContainerModel.cs:9`, `IContainerNodeModel` backed by `List<NodeId>`), referenced via the test csproj. The primary round-1 vacuity (private test-only stub) is closed.
- **Remains:** the FIX2-020 "Remains" cited **two** production implementations. The other one, `StateNode` (`HsmAsset.cs:427`), implements `IContainerNodeModel.ChildNodeIds` as a **LINQ projection** `Children.Select(c => new NodeId(c.StableId)).ToList()` (`:557-558`) over a mutable `List<StateNode>` -- a materially different code path. No test exercises `StateNode.ChildNodeIds` for insertion-order determinism.
- **Fix:** add a determinism test asserting `StateNode.ChildNodeIds` preserves child insertion order (the design's NEC-10 canonical-order invariant) on the real `StateNode` type.

---

## Summary
3 items remain: **FIX3-001** (High -- windows still not registered in production; the only runtime-impact item),
**FIX3-002** (D-BP-04 user-facing decision + D-BP-02 acceptable deferral), **FIX3-003** (Low -- add `StateNode`
coverage). No new bugs were introduced in round 2. The other 18 round-2 re-fixes verified clean, including
FIX2-001 (the BPF-015 probe `:N`->`:D` Critical), FIX2-002/003/004/006/007 (the debug/editor wiring),
FIX2-008..014, and the round-2 test-quality items.
