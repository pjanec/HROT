# BATCH-06 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-02

## Summary
Corrective Task 0 (BTree link projection) and AIE-023/024/027 implemented with real behavioral tests. The BATCH-05 P1 is resolved; DEBT-003 fixed.

## Verification performed (ran suites myself)
- `Hrot.Editor.AiShared.Tests` **677/677** (no AV), `Hrot.BTree.Editor.Tests` **350/350**, `Hrot.Hsm.Editor.Tests` **298/298**, `NodeEditor.UI.Tests` **40/40**, `EditorSubsystemBoot` filter **10/10**. Blueprints 889/10 (DEBT-006, no new). Full integration suite still pre-existing-aborts (DEBT-008).
- **Corrective Task 0 (P1):** `BTreeGraphModel` now projects links; test (`BTreeGraphModelTests`) asserts `Links.HaveCount(3)` for Root→Sequence→{Action,Action}, `FromPin == child.OutputPinId`, `ToPin == parent.InputPinId`, `FindLink` found/null, rebuild-on-change (0→3). Gold-standard. **P1 RESOLVED.**
- **AIE-023:** `Inspector_Commit_AppliesToAsset_AndMarksDirty` modifies a facet → `ApplyFacet` → asserts asset dirty (+ value written). `IFacetDispatcher` placed in AiShared (dep-clean); subsystem mappers wired in composition root.
- **AIE-024 / DEBT-003:** `PickerRegistry.Get<TItem>` real lookup (`_adapters` → `typed.Source`, null on mismatch) with 5 tests; BTree/HSM picker drawers with headless `IPickerListSource`.
- **AIE-027:** `HsmGlobalsStripLogic` (pure, tested) + ImGui-guarded `Render`; `HsmAsset.RemoveGlobalTransition`; chip/select/remove tested over fakes.

## Issues Found
None blocking.

## Verdict
APPROVED. Phase 2 nearly complete — only AIE-025 (Blackboard per perspective) + AIE-026 (save→emit→hot-reload) remain (BATCH-07).

## Commit Message
```
feat(editor): BTree links + Inspector facet dispatch + pickers + HSM globals (BATCH-06)

Corrective Task 0: BTreeGraphModel now projects tree edges as ILinkModels
(child.OutputPinId → parent.InputPinId); FindLink + rebuild-on-change; strengthened test
asserts exact link count + endpoint pins. Resolves BATCH-05 P1.

AIE-023: Inspector facet dispatch (selection → BTree/HSM FacetMapper → StructEdit facet;
commit applies + marks dirty; IFacetDispatcher in AiShared, mappers wired in composition root).
AIE-024 + DEBT-003: custom StructEdit field pickers for BTree/HSM marker attributes;
PickerRegistry.Get<TItem> implemented (was dead null).
AIE-027: HsmGlobalsStrip finished (chips + context menu) + registered in HSM perspective.

Tests: AiShared 677, BTree 350, HSM 298, NodeEditor.UI 40, EditorSubsystemBoot 10/10,
Blueprints 889/10 (DEBT-006).
```
