# BATCH-05 Review
**Status:** ⚠️ APPROVED WITH P1 (BTree link projection) — committed; P1 → BATCH-06 Corrective Task 0   **Date:** 2026-06-02

## Summary
Canvas window (AIE-020), HSM host binding (AIE-022), and BTree node/pin projection + factories (AIE-021) implemented; editor-relevant suites green. **One P1:** `BTreeGraphModel` projects no links → BTree tree edges won't render. Plus a verifies-nothing test. One pre-existing integration-suite failure (not this batch) recorded as debt.

## Verification performed (ran suites myself)
- `Hrot.Editor.AiShared.Tests` **677/677** (no AV). `Hrot.BTree.Editor.Tests` **327/327**. `Hrot.Hsm.Editor.Tests` **278/278**. `EditorSubsystemBoot` filter **10/10**. `Hrot.Blueprints.Tests` **889/10/8** (10 = DEBT-006, no new).
- **Full `Hrot.ClusterRunner.Integration.Tests` ABORTS — 11 failed/0 passed** with `InvalidOperationException: Call RegisterSystems before RegisterProviders` at `SimHostNodeBootstrapper.RegisterSpawningPipeline:294`. **Stash-tested at the Batch-04 baseline → identical failures.** Pre-existing SimHost-bootstrap breakage, NOT caused by Batch-05 → **DEBT-008**. (Coder reported "EditorSubsystemBoot 10/10 / integration 13/13" from a filtered run; the full suite aborts — flagged.)
- BTreeGraphModel reversed-pin convention verified correct (Output=child→parent, Input=parent←child).

## Issue 1 (P1 → Corrective Task 0): BTreeGraphModel projects no links
**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs:176` — `Links => Array.Empty<ILinkModel>()`, `FindLink => null`. NodeEdit renders wires from `IGraphModel.Links`; empty ⇒ **BTree nodes render with no connecting wires** (tree structure invisible). The doc-comment claims `child.OutputPin → parent.InputPin` links but none are produced from `ChildVisualIds`. HSM (pre-existing `HsmGraphModel`) correctly populates links from transitions.
**Fix (BATCH-06 Task 0):** project each parent↔child edge (from `ChildVisualIds`) as an `ILinkModel` connecting child.`OutputPinId` → parent.`InputPinId`; implement `FindLink`. Rebuild on `Changed`.

## Issue 2 (test quality, fix with Task 0): verifies-nothing test
**File:** `Hrot.BTree.Editor.Tests/Host/BTreeDocumentFactoryTests.cs:125` `..._ExposesProjectedLinks` asserts only `Links.Should().NotBeNull()` — passes on `Array.Empty`. Must assert the **actual link count + endpoint pin ids** for a known tree once Issue 1 is fixed.

## Other (good)
`AiGraphCanvasWindow` renders the active doc's `AiCanvasContext` via an `ICanvasRenderSeam` (headless-safe, `GetCurrentContext` gated); `OnFocus`→`Activate` tested. `AiDocumentManager.DocumentOpened` event added so factories populate `ViewState` before activation — sound. HSM factory + container projection good. Wire-up builds the adapter bundle from `wm.Atlas` and registers canvases via the registrar seam.

## Verdict
Committed as incremental progress. **BATCH-06 must begin with Corrective Task 0** (BTree links + strengthened test). AIE-021 stays ⚠️ until then; AIE-020, AIE-022 done.

## Commit Message
```
feat(editor): AIE-020/022 + partial AIE-021 — graph canvas + BTree/HSM host binding (BATCH-05)

- AiGraphCanvasWindow (per-perspective, renders active doc's GraphView via CanvasRenderer).
- HSM document factory + host binding (HsmGraphModel projects states+transitions+containers).
- BTree: new BTreeGraphModel/Node/Pin adapter layer (BehaviorTreeAsset lacked IGraphModel) +
  BTreeDocumentFactory + host services. KNOWN P1: BTreeGraphModel.Links empty (edges don't
  render yet) — BATCH-06 Corrective Task 0.
- EditorSubsystem: AiEditorAdapterBundle(wm.Atlas) + canvas registered into BTree/HSM perspectives;
  AiDocumentManager.DocumentOpened dispatches to factories.
Tests: AiShared 677, BTree 327, HSM 278, EditorSubsystemBoot 10/10, Blueprints 889/10 (DEBT-006).
Pre-existing integration-suite SimHost-bootstrap failures unrelated (DEBT-008).
```
