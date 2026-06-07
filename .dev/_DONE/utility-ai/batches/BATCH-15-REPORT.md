# BATCH-15 Report

## Status: COMPLETE

## Tasks Completed

### T1 — InputParamsModel: Add TemplateName field
**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/InputParamsModel.cs`

Added `public string TemplateName = string.Empty;` after `MountIndex`. Comment documents
its purpose (EQS inputs, reconstructing `In.EqsTopScore("CoverQuery")`).

### T2 — Create UtilityFluentEmitter
**File:** `Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityFluentEmitter.cs`

Implements `IFluentCSharpEmitter<UtilityDecisionAsset>`.

Key behaviors:
- Constructor accepts `targetNamespace` (default `"Fdp.Toolkit.Utility"`).
- Header produced via `FluentCSharpEmitterBase.BuildHeader(asset.AssetId)`.
- Usings collected and sorted via `FluentCSharpEmitterBase.SortUsings(...)`.
- File-scoped namespace declaration.
- `[UtilityDecision(...)]` attribute emitted with `hysteresisBonus` line only when non-zero.
- Class name derived from `DisplayName` (strips non-identifier chars, appends "Decision").
- `Build(IUtilityDecisionBuilder b)` method with chained `.Option()` or `.CandidateOption()`:
  - `ThreatRanking` and `WeaponSelection` use `.CandidateOption(mode, o => ...)`.
  - All others use `.Option(id, mode, o => ...)`.
- Options and considerations sorted deterministically by `VisualId` (ordinal).
- Curve presets matched to `Curve.*` names; custom curves emit `new ResponseCurve(...)`.
- Float literals use `"R"` format + `f` suffix for round-trip fidelity.
- `[UtilityLayout]` placeholder emitted when `Layout` has non-default data.

### T3 — Create UtilityAssetHasher
**File:** `Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityAssetHasher.cs`

`ComputeStructureHash`: hashes `DecisionKind`, option `VisualId`/`Mode`, consideration
`VisualId`/`InputName`/`Context`/`Curve.Kind`. Changes here trigger `HotReloadTier.Hard`.

`ComputeParamHash`: hashes `HysteresisBonus`, consideration `Weight`/`M`/`K`/`B`/`C`.
Changes here (without structure change) trigger `HotReloadTier.Soft`.

`Classify(before, after)`: delegates to `HotReloadClassifier.Classify(...)`.

Layout data is not hashed — layout-only changes are `Cosmetic`.

### T4 — Create UtilityFluentEmitterTests
**File:** `Hrot/Editor/Hrot.Utility.Editor.Tests/UtilityFluentEmitterTests.cs`

**UtilityFluentEmitterTests (13 tests):**
- `Emit_SameModel_ByteIdentical_SecondEmit` — determinism
- `Emit_SortedByVisualId_WhenOptionsOutOfOrder` — option sort order
- `Emit_SortedByVisualId_ConsiderationsWithinOption` — consideration sort order
- `Emit_Contains_EditorGeneratedMarker` — header marker present
- `Emit_Contains_AssetId_InHeader` — AssetId in header
- `Emit_Contains_DisplayName_InAttribute` — displayName in attribute
- `Emit_Contains_DecisionKind_InAttribute` — kind in attribute
- `Emit_Contains_Category_InAttribute` — category in attribute
- `Emit_HysteresisBonus_NonZero_EmittedInAttribute` — hysteresisBonus emitted
- `Emit_HysteresisBonus_Zero_NotEmitted` — hysteresisBonus omitted when 0
- `Emit_CandidateOption_ForThreatRankingDecision` — CandidateOption for ranking decisions
- `Emit_NamedOption_ForPostureSelectDecision` — .Option() for posture decisions
- `Emit_Consideration_WithLinearCurvePreset` — Curve.Linear shorthand used
- `Emit_Consideration_WithCustomCurve_EmitsNewResponseCurve` — custom curve path
- `Emit_Consideration_Weight_UsesRFormat` — float R-format literal

**UtilityAssetHasherTests (4 tests):**
- `Classify_LayoutChangeOnly_IsCosmetic`
- `Classify_WeightChange_IsSoft`
- `Classify_AddOption_IsHard`
- `Classify_InputNameChange_IsHard`

## Build Result

```
Build succeeded.
0 Error(s)
```

(Pre-existing warnings in unrelated projects — `CS0618` on `IBlueprintTimeController`,
`CS8601` in `Hrot.Blueprints.Tests` — are not introduced by this batch.)

## Test Result

```
Total tests: 100
     Passed: 100
     0 Error(s)
```

All 19 new tests pass. No regressions in the 81 pre-existing tests.
