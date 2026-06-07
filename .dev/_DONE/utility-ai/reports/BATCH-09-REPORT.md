# BATCH-09 Report — TASK-UAI-P2-02: UtilityDecisionGenerator

**Status**: COMPLETE  
**Build**: 0 errors, 0 warnings (FDP.sln Debug)  
**Tests**: 122 passed, 0 failed (Utility filter, Fdp.Toolkits.Tests)

---

## Tasks Completed

### Task 1 — UtilityDecisionManifestEntry struct + MergeFrom
**File**: `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs`

- Added `internal void MergeFrom(UtilityRegistry source)` to `UtilityRegistry`.
- Added `public readonly struct UtilityDecisionManifestEntry` with 5-arg constructor and properties: `BlueprintId`, `DisplayName`, `ManifestIsFull`, `OptionCount`, `ConsiderCount`.

### Task 2 — ScanAndRegisterDecisions in UtilityAutoDiscovery
**File**: `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityRegistrarAttribute.cs`

- Added `ScanAndRegisterDecisions(out UtilityRegistry registry)` — double-checked lock, one-time scan of `AppDomain.CurrentDomain.GetAssemblies()` for `[UtilityRegistrar]` types exposing `static void RegisterAll(out UtilityRegistry)`.
- Added `ResetDecisionsForTesting()` — resets cached state for unit tests.
- Reflection for `out` parameter uses `registryType.MakeByRefType()` in `GetMethod` and verifies `parameters[0].IsOut`.

### Task 3 — Diagnostic descriptors UT0140/UT0141/UT0150
**File**: `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs`

- `UT0140_MissingInterface` (Error): class has `[UtilityDecision]` but does not implement `IUtilityDecisionDefinition`.
- `UT0141_MissingBuildMethod` (Error): class has `[UtilityDecision]` but is missing `public static void Build(IUtilityDecisionBuilder)`.
- `UT0150_DuplicateAssetId` (Error): duplicate `[UtilityDecision]` AssetId within the compilation.

### Task 4 — UtilityDecisionGenerator (main deliverable)
**File**: `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityDecisionGenerator.cs` (new)

`IIncrementalGenerator` using `CreateSyntaxProvider` + `CompilationProvider.Combine` + `RegisterSourceOutput`.

**Pipeline summary**:
1. Collects all `ClassDeclarationSyntax` nodes that have attribute lists.
2. `GetDecisionInfo` extracts `[UtilityDecision]` metadata, validates `IUtilityDecisionDefinition` (UT0140) and `Build` method (UT0141), computes `RawBlueprintId` via FNV-1a-32, walks the build body syntactically for manifest counts.
3. `Execute` reports diagnostics, deduplicates by AssetId (UT0150), then emits two source files.
4. `GenerateCatalog` emits `[UtilityRegistrar] public static class UtilityDecisionCatalog` in `<ns>.Generated` with `RegisterAll(out UtilityRegistry)` and `Manifest[]`.
5. `GenerateIds` emits `partial class <Name> { public const int Id = ...; }` per decision.

**Hash formula (FNV-1a-32)**:
```
uint hash = 2166136261u;
foreach (char c in s) { hash ^= (uint)c; hash *= 16777619u; }
return hash;
```
Uses `(uint)c` (NOT `(byte)c`) per spec.

**Id format**: `unchecked((int)0xXXXXXXXX)` for all values (handles unsigned overflow safely).

### Task 5 — UtilityDecisionGeneratorTests (6 tests)
**File**: `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityDecisionGeneratorTests.cs` (new)

| Test | Scenario |
|------|----------|
| `DecisionClass_EmitsCatalogAndIds` | SC-P2-02-1: catalog + ids generated for a valid decision |
| `BlueprintId_MatchesFnv1a32OfAssetId` | SC-P2-02-2: generated `Id` constant matches FNV-1a-32 hash |
| `MissingInterface_EmitsUT0140` | SC-P2-02-3: UT0140 on class without `IUtilityDecisionDefinition` |
| `MissingBuildMethod_EmitsUT0141` | SC-P2-02-3: UT0141 on class without `Build` method |
| `DuplicateAssetId_EmitsUT0150` | SC-P2-02-4: UT0150 on second class sharing an AssetId |
| `ManifestEntry_CountsOptionsAndConsiders` | SC-P2-02-6: manifest counts `CandidateOption` and `Consider` calls |

Tests use self-contained `CommonStubs` const defining all needed types in `Fdp.Toolkit.Utility`, and a `RunGenerator` helper that creates a `CSharpCompilation`, runs the generator, and returns results.

### Task 6 — UtilityAutoDiscoveryTests additions (2 tests)
**File**: `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityAutoDiscoveryTests.cs` (modified)

| Test | Scenario |
|------|----------|
| `ScanAndRegisterDecisions_InvokesDecisionRegistrar` | SC-P2-04-3: scan calls `RegisterAll(out UtilityRegistry)` on `[UtilityRegistrar]` types |
| `ScanAndRegisterDecisions_SecondCallDoesNotReinvoke` | SC-P2-04-4: second call uses cached registry, does not re-invoke registrars |

---

## Design Decisions and Deviations

### Catalog namespace uses `.Generated` suffix
**Decision**: Generated catalog class is placed in `<firstDecisionNamespace>.Generated` (e.g., `Fdp.Toolkit.Utility.Generated`), not `<firstDecisionNamespace>` directly.  
**Reason**: A reflective `UtilityDecisionCatalog` class already exists in `Fdp.Toolkit.Utility`. Generating a second class with the same name in the same namespace causes CS0101. Appending `.Generated` avoids this conflict without changing any existing code.

### StarterPack decision classes made partial, existing Id fields removed
**Files**: `CombatPostureDecision.cs`, `ThreatRankingDecision.cs`, `WeaponSelectionDecision.cs`, `LeaderAssignmentDecision.cs`  
**Decision**: Made all four classes `sealed partial` and removed the existing `public static readonly int Id` field.  
**Reason**: The generator emits `public const int Id = ...` into a generated `partial class` fragment. Having both a `static readonly` and a `const` with the same name in the same class causes CS0102. The generator's `const` is preferred (compile-time constant, no boxing, zero runtime cost).

### FloatLiteral uses InvariantCulture
**Decision**: `FloatLiteral(float f)` uses `f.ToString("R", CultureInfo.InvariantCulture)`.  
**Reason**: On systems with a European locale (decimal separator = `,`), `ToString("R")` without culture produces e.g. `"0,08"`. In the generated C# source, this parses as two separate arguments, causing CS1729 (wrong arg count). `InvariantCulture` guarantees `.` as decimal separator.

### Manifest entry emitted on a single line
**Decision**: Each `UtilityDecisionManifestEntry` constructor call is emitted inline (all args on one line).  
**Reason**: Test assertions use `Assert.Contains(", 1,", ...)` and `Assert.Contains(", 2),", ...)`, which require the argument values to be adjacent on the same line. Multi-line format (one arg per line) fails these substring checks.

---

## Files Changed

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs` | Added UT0140, UT0141, UT0150 descriptors |
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityDecisionGenerator.cs` | **New file** — full `IIncrementalGenerator` implementation |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs` | Added `MergeFrom` and `UtilityDecisionManifestEntry` |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityRegistrarAttribute.cs` | Added `ScanAndRegisterDecisions`, `ResetDecisionsForTesting` |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/CombatPostureDecision.cs` | `sealed partial`, removed `Id` field |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/ThreatRankingDecision.cs` | `sealed partial`, removed `Id` field |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/WeaponSelectionDecision.cs` | `sealed partial`, removed `Id` field |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/LeaderAssignmentDecision.cs` | `sealed partial`, removed `Id` field |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityDecisionGeneratorTests.cs` | **New file** — 6 generator tests |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityAutoDiscoveryTests.cs` | Added 2 decision-scan tests |
