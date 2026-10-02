# BATCH-04 — Validators → Diagnostics window (BTree)

**Task:** TASK-BT-04 (`.dev/_DONE/ai-hsm-btree-vis-edit-2/TASK-DETAIL.md#task-bt-04--validators--diagnostics-window`)
**Phase:** A · **One objective only.**

## 🔒 Working agreement (MANDATORY)
Same as prior batches: one task; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Design: `docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md` §5 (EB-D part 1).
- Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-04-REPORT.md`.

## 🎯 Objective
The per-perspective Diagnostics window runs `IAssetValidator`s, but the BTree `PerspectiveWorkspaceRegistrar` is constructed with **no** `validators:` argument, so it shows nothing for BTree. Wire a `BTreeAssetValidator` into the BTree registrar so the Diagnostics window populates. (`BTreeAssetValidator`/`BTreeValidator` already exist; `BTreeValidator` is stateless / parameterless.)

## Files (exact)
1. `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — at the **BTree** `new PerspectiveWorkspaceRegistrar("BTree", ...)` construction (≈ line 1904), add the named argument:
   ```csharp
   validators: new Hrot.Editor.AiShared.Validation.IAssetValidator[]
   {
       new Hrot.BTree.Editor.Validation.BTreeAssetValidator(
           new Hrot.BTree.Editor.Validation.BTreeValidator()),
   },
   ```
   (Use a `using` if you prefer; fully-qualified is fine. **Wiring only** — do NOT touch the HSM or Blueprint registrar, and do NOT restructure surrounding code.)
2. `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Validation/BTreeAssetValidatorTests.cs` — ADD the behavioral tests below (the file currently only covers `SupportedKind` + wrong-kind→empty).

## 🧪 Tests (ADD these; assert real codes/severities)
Use the real model API to build assets (see `Host/BTreeDynamicCatalogTests.cs` for `EmptyBlob()` + `new BehaviorTreeAsset(...)` + `asset.AddNode`). `AssetDiagnostic` has `Code` (string == the `BTreeDiagnosticCode` name) and `Severity` (`AssetDiagnosticSeverity`). `BTreeDiagnosticCode` includes `EmptyComposite`, `UnboundActionMethod`.

- `Validate_EmptyComposite_YieldsEmptyCompositeDiagnostic`: build an asset with a **reachable** empty composite (e.g. Root → empty Sequence: add a Root node and a Sequence node, set the Root's `ChildVisualIds` to the Sequence; Sequence has no children). Assert the result contains an `AssetDiagnostic` with `Code == "EmptyComposite"`. (Run the validator to confirm the exact arrange that triggers it — adjust the tree shape until that code appears; do NOT change the validator.)
- `Validate_UnboundAction_YieldsUnboundActionError`: build an asset with a reachable Action node whose method is unbound (no `Action` payload / empty `MethodFqn`). Assert the result contains an `AssetDiagnostic` with `Code == "UnboundActionMethod"` and `Severity == AssetDiagnosticSeverity.Error`.
- `Validate_ValidTree_NoEmptyCompositeOrUnboundError`: build a valid small tree (Root → Sequence → an Action with a non-empty `MethodFqn`, plus enough to be valid). Assert the result contains **no** diagnostic with `Code == "EmptyComposite"` and **no** `Code == "UnboundActionMethod"`.
- `Validate_PopulatesAssetIdAndName`: for any non-empty result, the diagnostics carry `AssetId == asset.AssetId` and `AssetName == asset.Name`.

(Do NOT duplicate the existing `SupportedKind`/wrong-kind tests.)

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in touched projects.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0** (incl. the new validator tests).
- [ ] `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests` — **Failed: 0** (DiagnosticsWindow tests still green).
- [ ] BTree registrar now constructed with a non-empty `validators:` list (BTreeAssetValidator).
- [ ] Report written (note: the composition-root wiring itself is build-verified; behavioral coverage is via the validator tests + existing DiagnosticsWindow tests).

## Notes
- Do NOT change validator RULES (`BTreeValidator`/`BTreeAssetValidator` logic) — they are owned/tested. This batch is wiring + behavioral coverage of the adapter only.
- This is NOT a `[VISUAL GATE]` task — it is headless-verifiable (the window populating is the next, inline-canvas task BT-05's visual concern; here we only prove diagnostics flow into the validator list).
