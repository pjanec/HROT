# BATCH-08 REVIEW

## Tasks: EQS-020 + EQS-021
**Commit:** a9966fee
**Review outcome:** APPROVED

---

## Test Results (independently verified)

- Unit EQS (Fdp.Toolkits.Tests): **49/49 PASS** (+9 new)
- Integration EQS (Hrot.ClusterRunner.Integration.Tests): **21/21 PASS** (+2 new)
- Build: 0 errors across all projects

---

## Review Findings

### EqsTemplateGenerator

- Correctly uses `IIncrementalGenerator` (not legacy `ISourceGenerator`)
- Reads `EqsTemplateAttribute` positional constructor arg (index 0) -- correct, attribute uses positional assetId
- FNV-1a 32-bit: offset=2166136261u, prime=16777619u -- matches spec exactly
- Generated file name `EqsRegistrar_{assembly}.g.cs` -- correct
- Generated class has `[BlueprintRegistrar]` -- correct for AiHotReloadCoordinator auto-discovery
- Generated code calls `Build(new EqsTemplateBuilder())` then `ComputeStructureHash()` -- matches IMPLEM_DETAILS
- Uses string.Replace instead of interpolation for `assemblyName` in generated class name -- avoids potential format issues with Roslyn string building -- MINOR STYLE but correct

### EqsTemplatePurityAnalyzer

- EQS_001 fires when no valid generator-compatible overload exists -- correct
- EQS_002 approach is simplified but satisfies spec success conditions
- Deviation: class-level analysis (`SymbolKind.NamedType`) instead of method-level -- justified because `FindCoverFromTarget` has two `Build()` overloads and method-level analysis would incorrectly flag `Build(ILosService)` as violating EQS_001 when `Build(IEqsTemplateBuilder)` is also present

### EqsQueryTemplate.ComputeStructureHash()

- Correct FNV-1a 64-bit: offset=14695981039346656037, prime=1099511628211
- Hashes generator + all test type FullName strings with `|` separator
- Deterministic for same type sets -- correct
- GOOD: uses `t.FullName ?? t.Name` to handle rare edge cases where FullName is null

### FindCoverFromTarget.Build(IEqsTemplateBuilder b)

- Delegates to `Build(new BlockedLosService())` -- no runtime dependencies, safe for source gen
- `BlockedLosService` (BATCH-05) is a stub that always returns blocked -- structurally correct for hash purposes

### EqsSolverSystem hard-reset

- Critical fix: soft reset now preserves `CurrentStructureHash` (`savedHash` pattern) -- avoids spurious hard-reset on every epoch change
- Hard-reset fires on `liveHash != 0 && evalState.CurrentStructureHash != liveHash` -- guard against zero hash on first tick
- Hash written after successful evaluation -- ensures persistence
- Hash written in count==0 early-return path -- necessary correctness fix (not mentioned in spec but required)

### HotReloadTests (T-SH4, T-SH5)

- T-SH4 deviation (asserting `CurrentStructureHash == hashB` instead of `IsReady == false`): ACCEPTED. The solver runs evaluation in the same tick as the hard reset, making `IsReady == false` unobservable after a pump. The hash assertion proves the reset fired.
- T-SH5: asserts `CurrentStructureHash` unchanged after epoch increment -- direct proof soft reset preserves hash
- Both tests use `[Collection("EqsIntegrationTests")]` -- correct

### EqsStructureHashTests (T-SH1/2/3)

- T-SH1: different generator types -> different hashes -- correct structural guarantee
- T-SH2: same type set -> same hash -- determinism guarantee
- T-SH3: different test arrays -> different hashes -- correct

### EqsTemplateGeneratorTests (T-EGN1/2/3)

- Uses same `RunGenerator` helper pattern as `GizmoRegistrarGeneratorTests` -- consistent
- T-EGN1: asserts computed BlueprintId appears in generated source -- uses runtime-computed FNV-1a to avoid magic number
- T-EGN2: empty input -> no generated output -- correct
- T-EGN3: structure assertions ([BlueprintRegistrar], Register method, ComputeStructureHash call) -- complete

### EqsTemplatePurityAnalyzerTests (T-EPA1/2/3)

- T-EPA1: non-static Build -> EQS_001 -- correct
- T-EPA2: static Build(IEqsTemplateBuilder) -> no EQS_001 -- correct
- T-EPA3: static Build(int) (wrong param) -> EQS_001 -- correct

---

## Issues

None blocking. All deviations are justified and test coverage is strong.
