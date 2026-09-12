# BATCH-04 REPORT — Validators → Diagnostics Window (TASK-BT-04)

**Date:** 2026-06-12
**Status:** ✅ COMPLETE

## Summary

Wired `BTreeAssetValidator` into the BTree `PerspectiveWorkspaceRegistrar` so the Diagnostics window populates with BTree-specific validation results. Previously the BTree registrar was constructed with no `validators:` argument (defaulting to empty), so it showed nothing. Added four behavioral tests covering the key diagnostic codes.

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Added `validators:` named argument at the BTree `PerspectiveWorkspaceRegistrar` construction site (≈line 1944) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Validation/BTreeAssetValidatorTests.cs` | Added 4 behavioral test methods + helper factory (`MakeAsset`/`EmptyBlob`) |

## Implementation Details

### Wiring (`EditorSubsystem.cs:1944-1953`)

Added a `validators:` argument to the existing BTree `new PerspectiveWorkspaceRegistrar("BTree", ...)` call, passing a single-element array containing `new BTreeAssetValidator(new BTreeValidator())`. No changes were made to the HSM or Blueprint registrars. No surrounding code was restructured.

```csharp
validators: new Hrot.Editor.AiShared.Validation.IAssetValidator[]
{
    new Hrot.BTree.Editor.Validation.BTreeAssetValidator(
        new Hrot.BTree.Editor.Validation.BTreeValidator()),
},
```

### Behavioral Tests (`BTreeAssetValidatorTests.cs`)

Four new `[Fact]` methods added (existing `SupportedKind_IsBTree` and `Validate_WithWrongAssetKind_ReturnsEmpty` preserved):

| Test | What it asserts |
|------|-----------------|
| `Validate_EmptyComposite_YieldsEmptyCompositeDiagnostic` | Root → empty Sequence yields an `AssetDiagnostic` with `Code == "EmptyComposite"` |
| `Validate_UnboundAction_YieldsUnboundActionError` | Root → Action (null payload → empty MethodFqn) yields `Code == "UnboundActionMethod"` with `Severity == Error` |
| `Validate_ValidTree_NoEmptyCompositeOrUnboundError` | Root → Sequence → Action (`MethodFqn = "Hrot.Test.DoSomething"`) contains **no** `EmptyComposite` and **no** `UnboundActionMethod` |
| `Validate_PopulatesAssetIdAndName` | Any non-empty diagnostic result carries `AssetId == asset.AssetId` and `AssetName == asset.Name` |

Tests use the real model API (`BehaviorTreeAsset` constructor + internal `AddNode`, accessible via `InternalsVisibleTo`) to build minimal valid/invalid trees, then assert the exact diagnostic codes and severities produced by `BTreeValidator` → `BTreeAssetValidator`.

## Verification

- `dotnet build IOS-IG-SimHost.sln` — **0 errors**, 0 new warnings in touched projects
- `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0**, Passed: 473 (incl. 4 new tests)
- `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests` — **Failed: 0**, Passed: 1059 (DiagnosticsWindow tests still green)
- BTree registrar is now constructed with a non-empty `validators:` list containing `BTreeAssetValidator`
