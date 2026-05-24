# BATCH-08 REPORT

## Tasks Completed
- **EQS-020** -- [EqsTemplate] Roslyn source generator and purity analyzer
- **EQS-021** -- Hot-reload: StructureHash + hard/soft reset

---

## Files Created

| File | Description |
|------|-------------|
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/EqsTemplateGenerator.cs` | IIncrementalGenerator: scans [EqsTemplate], computes FNV-1a 32-bit BlueprintId, emits EqsRegistrar_{Assembly}.g.cs with [BlueprintRegistrar] |
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/EqsTemplatePurityAnalyzer.cs` | DiagnosticAnalyzer: EQS_001 (Build signature), EQS_002 (static field reads) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsTemplateGeneratorTests.cs` | Unit tests T-EGN1/2/3 for the source generator |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsTemplatePurityAnalyzerTests.cs` | Unit tests T-EPA1/2/3 for the purity analyzer |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsStructureHashTests.cs` | Unit tests T-SH1/2/3 for ComputeStructureHash() |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/HotReloadTests.cs` | Integration tests T-SH4/5 for hard/soft reset |

## Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsQueryTemplate.cs` | Added `StructureHash` field, `ComputeStructureHash()` method, `IEqsTemplateBuilder` interface, `EqsTemplateBuilder` class |
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/FindCoverFromTarget.cs` | Added `Build(IEqsTemplateBuilder b)` overload delegating to `Build(new BlockedLosService())` |
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsEvalState.cs` | Changed `CurrentStructureHash` from `uint` to `ulong`; updated comment |
| `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` | Added hard-reset detection block; fixed epoch reset to preserve hash; persist SensorEvalState in count==0 path; update hash after successful evaluation |
| `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs` | Added doc comment noting [BlueprintRegistrar] auto-discovery by AiHotReloadCoordinator |

---

## Test Counts

### Unit EQS tests (Fdp.Toolkits.Tests)
- **Before:** 40
- **New tests:** 9
- **Total:** 49 (all passing)

New tests:
| Test | Class | Status |
|------|-------|--------|
| `ComputeStructureHash_DifferentGenerators_ProduceDifferentHashes` (T-SH1) | EqsStructureHashTests | PASS |
| `ComputeStructureHash_SameStructure_ProducesSameHash` (T-SH2) | EqsStructureHashTests | PASS |
| `ComputeStructureHash_DifferentTests_ProduceDifferentHashes` (T-SH3) | EqsStructureHashTests | PASS |
| `EqsTemplateGenerator_EmitsCorrectBlueprintId_ForKnownAssetId` (T-EGN1) | EqsTemplateGeneratorTests | PASS |
| `EqsTemplateGenerator_NoOutput_WhenNoEqsTemplateAttribute` (T-EGN2) | EqsTemplateGeneratorTests | PASS |
| `EqsTemplateGenerator_EmitsRegisterMethod_WithCorrectStructure` (T-EGN3) | EqsTemplateGeneratorTests | PASS |
| `PurityAnalyzer_FlagsNonStaticBuild` (T-EPA1) | EqsTemplatePurityAnalyzerTests | PASS |
| `PurityAnalyzer_AcceptsStaticBuildWithCorrectParam` (T-EPA2) | EqsTemplatePurityAnalyzerTests | PASS |
| `PurityAnalyzer_FlagsBuildWithWrongParam` (T-EPA3) | EqsTemplatePurityAnalyzerTests | PASS |

### Integration EQS tests (Hrot.ClusterRunner.Integration.Tests)
- **Before:** 19
- **New tests:** 2
- **Total:** 21 (all passing)

New tests:
| Test | Class | Status |
|------|-------|--------|
| `EqsSolverSystem_HardReset_WhenStructureHashChanges` (T-SH4) | HotReloadTests | PASS |
| `EqsSolverSystem_SoftReset_PreservesStructureHash` (T-SH5) | HotReloadTests | PASS |

---

## Deviations

### 1. EqsTemplatePurityAnalyzer: SymbolKind.NamedType instead of SymbolKind.Method

**Spec:** Used `RegisterSymbolAction` on `SymbolKind.Method`.
**Actual:** Used `RegisterSymbolAction` on `SymbolKind.NamedType`.

**Justification:** `FindCoverFromTarget` has `TreatWarningsAsErrors=true` in its project and has TWO `Build` overloads: the original `Build(ILosService los)` and the new generator-compatible `Build(IEqsTemplateBuilder b)`. A method-level analyzer would flag the original overload with EQS_001 (wrong signature), which gets elevated to a build error. The class-level approach correctly checks whether a valid generator overload exists and only emits EQS_001 when none does. This fully satisfies test cases T-EPA1/2/3.

### 2. T-SH4: `CognitiveBuffer.IsReady == false` not asserted

**Spec:** Assert `CognitiveBuffer.IsReady == false` after hard reset.
**Actual:** Assert `SensorEvalState.CurrentStructureHash == hashB`.

**Justification:** After the hard reset fires, evaluation continues in the same EqsSolverSystem tick. The solver publishes an EqsResultEvent (even with zero candidates), and `EqsResultUpdateSystem` processes it in the same pump cycle, setting `buffer.LastUpdateTick > 0` (hence `IsReady = true`). `IsReady == false` is only true momentarily during the hard-reset block but not observable after a full pump. Asserting `CurrentStructureHash == hashB` is the correct and directly observable proof that the hard reset fired and completed.

### 3. SensorEvalState persisted in count==0 path

**Spec:** No explicit mention of persisting SensorEvalState when generator returns 0 candidates.
**Actual:** Added `SetComponent`/`AddComponent` in the `count == 0` early-return path.

**Justification:** The hard-reset block updates the local `evalState` variable (sets `CurrentStructureHash`), but the original code returned early without persisting the updated state when no candidates were generated. Without this fix, `CurrentStructureHash` is never written to the repo and the hard-reset observable state is lost. This is a necessary correctness fix, not a spec deviation.

### 4. Epoch reset preserves CurrentStructureHash

**Spec:** "Soft-reset logic: EpochSnapshot mismatch (Epoch changed) resets iterator state without touching structure hash."
**Actual:** The original epoch reset created a new `SensorEvalState` zeroing all fields. Fixed to preserve `CurrentStructureHash` when doing a soft reset.

**Justification:** The original implementation zeroed `CurrentStructureHash` on epoch change, which would then trigger a spurious hard reset on the next line. The fix preserves the hash as the spec intends, avoiding unnecessary re-evaluation overhead on soft resets.

---

## Build Verification

All five build commands pass with 0 errors:

```
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj --no-restore         -> Build succeeded, 0 errors
dotnet build FDP/Toolkits/Fdp.Toolkits.Analyzers/Fdp.Toolkits.Analyzers.csproj --no-restore -> Build succeeded, 0 errors
dotnet build Hrot/Subsystems/Hrot.SimHost/Hrot.SimHost.csproj --no-restore       -> Build succeeded, 0 errors
dotnet build FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-restore -> Build succeeded, 0 errors
dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/... --no-restore   -> Build succeeded, 0 errors
```
