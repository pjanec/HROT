# BATCH-03 Report

## Implementation Summary

### Task 1 — Emit-core layout-excluding mode (BTreeEmitCore + HsmEmitCore)

**Files modified:** `Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`, `Hrot.AiEditor.Persistence/Emit/HsmEmitCore.cs`

Added `EmitTopologyCore(dto)` to both classes, plus a shared `EmitInternal(dto, includeLayout)` method that both `Emit(dto)` (full, for editor adapter + BATCH-02 gate) and `EmitTopologyCore(dto)` (layout-excluded, for the generator) call.

**BTreeEmitCore change:**
- `Emit(dto)` → delegates to `EmitInternal(dto, includeLayout: true)` — unchanged output, BATCH-02 gate still passes.
- `EmitTopologyCore(dto)` → delegates to `EmitInternal(dto, includeLayout: false)`.
- `CollectUsingsTopologyOnly(dto)` — excludes `Hrot.Editor.AiShared.Layout` (and omits `System.Numerics` is not applicable here since BTree uses `System.Numerics` only in layout via `Vector2`; omit `LayoutNamespace` only).
- `EmitInternal(dto, includeLayout)` — if `includeLayout=false`, skips `EmitLayout(sb, dto)` and does not include `LayoutNamespace` in usings; closing `}` is emitted correctly either way.

**HsmEmitCore change:** symmetric. `CollectUsingsTopologyOnly` also excludes `System.Numerics` (only needed for `Vector2` in `EmitLayout`'s `.Canvas(new Vector2(...))` etc.).

### Task 2 — PU-201/PU-202: Hrot.AiEditor.Generators (NEW project)

**New files:**
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/Hrot.AiEditor.Generators.csproj`
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator.cs`
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators/HsmJsonGenerator.cs`

**Project layout:**
```xml
<TargetFramework>netstandard2.0</TargetFramework>
<IsRoslynComponent>true</IsRoslynComponent>
<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
<!-- Packages: Microsoft.CodeAnalysis.CSharp 4.8.0, Analyzers 3.3.4,
     System.Collections.Immutable 8.0.0 — all PrivateAssets="all" -->
<ProjectReference Include="..\Hrot.AiEditor.Persistence\..."
    PrivateAssets="all" ExcludeAssets="runtime" />
```

Exact mirror of `Hrot.Blueprints.Generators.csproj`.

**`ExcludeAssets="runtime"` sufficiency:** Yes, same as Blueprint. The Roslyn host (MSBuild + IDE analyzer sandbox) loads `Hrot.AiEditor.Persistence.dll` as an analyzer dep. `ExcludeAssets="runtime"` prevents it from being copied to the consumer's output dir (correct for analyzer packages) and Roslyn bundles `System.Text.Json` from its own SDK path. Same pattern as Blueprint's reference to `Hrot.Blueprints.Compiler`.

**Generators:** `BTreeJsonGenerator` (consumes `*.btree.json`) and `HsmJsonGenerator` (consumes `*.hsm.json`). Both mirror `BlueprintIncrementalGenerator` control flow exactly:
- `AdditionalTextsProvider.Where(extension)` → `.Select(getTextPair)` → `RegisterSourceOutput`
- `GenerateOneAsset`: deserialize (try/catch → `MakeParseErrorDiagnostic` on failure, return) → `EmitTopologyCore` (try/catch → diagnostic on failure, return) → `AddSource("{Name}.g.cs")`
- Diagnostic descriptor created inline in `MakeParseErrorDiagnostic()` (not as `static readonly`) to avoid RS2008 (analyzer release tracking), mirroring `BlueprintIncrementalGenerator.ToRoslynDiagnostic`.
- `DiagnosticId` exposed as `public const string` for test access.

Added to `IOS-IG-SimHost.sln` (project entry + ProjectConfigurationPlatforms + NestedProjects in the AI folder `D3B7249C-3319-3F27-102C-CEC9C8633A0C`).

### Task 3 — PU-205: Migration-equivalence test harness (NEW test project)

**New files:**
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj`
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/GeneratorTestHelpers.cs` (StringAdditionalText, ImmutableArrayExtensions)
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Generator/BTreeJsonGeneratorTests.cs`
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Generator/HsmJsonGeneratorTests.cs`
- `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Equivalence/MigrationEquivalenceTests.cs`

Added to `IOS-IG-SimHost.sln`.

---

## Design Decisions

### 1. `EmitInternal(dto, includeLayout)` refactor

Rather than two fully independent emit paths, I introduced `EmitInternal(dto, bool includeLayout)` as the single shared implementation. Both `Emit()` and `EmitTopologyCore()` delegate to it. This eliminates code duplication and ensures that any fix to the topology core logic is automatically reflected in both modes. The usings differ between modes (layout needs `Hrot.Editor.AiShared.Layout` and, for BTree, `System.Numerics`), handled by two `CollectUsings` variants.

### 2. Topology core extraction method for PU-205 (unambiguous)

The "extract topology core from committed .cs" step uses `EmitTopologyCore(dto)` directly — no heuristic string-stripping, no regex. The test proceeds as follows:

```
// Reference (left side):
var dto = BTreeAssetMapper.ToDto(LoadBTree("SampleScout"));
string reference = BTreeEmitCore.EmitTopologyCore(dto);

// Generated (right side):
string json = BTreeJsonServices.Serialize(dto);
// Feed json as AdditionalText to CSharpGeneratorDriver running BTreeJsonGenerator.
// Generator internally calls: BTreeJsonServices.Deserialize(text) → BTreeEmitCore.EmitTopologyCore(dto)
string generated = <generator output>;

// Exact-string comparison:
generated.Should().Be(reference);   // fails loud on any divergence
```

This is unambiguous because:
1. No string manipulation on the committed `.cs` file — `EmitTopologyCore` **is** the strip operation, not an approximation of it.
2. Both sides call the same `EmitTopologyCore` function; the only question the test answers is whether the JSON round-trip is lossless (proven by PU-105 BATCH-01).
3. Comparison is `string.Equals` (FluentAssertions `Be()`), not a substring check — any divergence produces a diff in the failure message.

### 3. DiagnosticDescriptor inline (RS2008 avoidance)

`EnforceExtendedAnalyzerRules=true` triggers RS2008 for `static readonly DiagnosticDescriptor` fields. The Blueprint generator avoids this by creating the descriptor inside the method. I mirrored that pattern.

### 4. `DiagnosticId` as `public const string`

Exposed `DiagnosticId` as `public const` to allow test assertions (`result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.DiagnosticId)`) without requiring `InternalsVisibleTo` or string literals in tests.

### 5. Test project references generator directly (not as analyzer)

The test project references `Hrot.AiEditor.Generators` as a plain `<ProjectReference>` (no `OutputItemType="Analyzer"`) so the generator classes are visible as types and can be instantiated directly in `CSharpGeneratorDriver.Create(generator)`. This is the standard pattern for Roslyn generator unit tests.

---

## Deviations

None. All tasks implemented as specified.

---

## Test Results

### Hrot.AiEditor.Generators.Tests (26 tests, all new — net8.0)
```
Passed!  - Failed: 0, Passed: 26, Skipped: 0, Total: 26, Duration: 339 ms
```

Individual tests:
- `BTreeJsonGeneratorTests.ValidBTreeJson_ProducesGeneratedSource_ContainingCreateBuilderAndThunk` ✓
- `BTreeJsonGeneratorTests.ValidBTreeJson_GeneratedFileName_MatchesAssetName` ✓
- `BTreeJsonGeneratorTests.ValidBTreeJson_GeneratedSource_DoesNotContainLayoutNamespace` ✓
- `BTreeJsonGeneratorTests.MalformedBTreeJson_YieldsDiagnostic_DoesNotThrow` ✓
- `BTreeJsonGeneratorTests.MalformedBTreeJson_DoesNotSuppressSiblingValidAsset` ✓
- `BTreeJsonGeneratorTests.NonBTreeJsonAdditionalText_IsIgnored` ✓
- `BTreeJsonGeneratorTests.EmitTopologyCore_ContainsCreateBuilderAndThunk_NotLayout` ✓
- `BTreeJsonGeneratorTests.EmitTopologyCore_IsDeterministic` ✓
- `BTreeJsonGeneratorTests.FullEmit_IsByteIdentical_ToOriginal_AfterTopologyCoreRefactor` ✓
- `HsmJsonGeneratorTests.ValidHsmJson_ProducesGeneratedSource_ContainingCreateBuilderAndThunk` ✓
- `HsmJsonGeneratorTests.ValidHsmJson_GeneratedFileName_MatchesAssetName` ✓
- `HsmJsonGeneratorTests.ValidHsmJson_GeneratedSource_DoesNotContainLayoutNamespace` ✓
- `HsmJsonGeneratorTests.MalformedHsmJson_YieldsDiagnostic_DoesNotThrow` ✓
- `HsmJsonGeneratorTests.MalformedHsmJson_DoesNotSuppressSiblingValidAsset` ✓
- `HsmJsonGeneratorTests.NonHsmJsonAdditionalText_IsIgnored` ✓
- `HsmJsonGeneratorTests.EmitTopologyCore_ContainsCreateBuilderAndThunk_NotLayout` ✓
- `HsmJsonGeneratorTests.EmitTopologyCore_IsDeterministic` ✓
- `HsmJsonGeneratorTests.FullEmit_IsByteIdentical_ToOriginal_AfterTopologyCoreRefactor` ✓
- `MigrationEquivalenceTests.BTree_SampleScout_JsonRoundTripThroughGenerator_ByteIdentical_ToTopologyCore` ✓
- `MigrationEquivalenceTests.BTree_SampleScout_GeneratorOutput_ContainsCreateBuilderAndThunk` ✓
- `MigrationEquivalenceTests.BTree_SampleScout_GeneratorOutput_ExcludesLayoutMethod` ✓
- `MigrationEquivalenceTests.BTree_SampleScout_EquivalenceTest_FailsLoudly_WhenDiverged` ✓
- `MigrationEquivalenceTests.Hsm_SampleGuard_JsonRoundTripThroughGenerator_ByteIdentical_ToTopologyCore` ✓
- `MigrationEquivalenceTests.Hsm_SampleGuard_GeneratorOutput_ContainsCreateBuilderAndThunk` ✓
- `MigrationEquivalenceTests.Hsm_SampleGuard_GeneratorOutput_ExcludesLayoutMethod` ✓
- `MigrationEquivalenceTests.Hsm_SampleGuard_EquivalenceTest_FailsLoudly_WhenDiverged` ✓

### Hrot.AiEditor.Persistence.Tests (88 tests — BATCH-01/02 gate)
```
Passed!  - Failed: 0, Passed: 88, Skipped: 0, Total: 88, Duration: 140 ms
```
BATCH-02 byte-identical gate (10 tests in ByteIdenticalGateTests) still green.

### EditorSubsystemBoot (Hrot.ClusterRunner.Integration.Tests)
```
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 2 s
```
10/10 ✓

### Hrot.Editor.AiShared.Tests
```
Passed!  - Failed: 0, Passed: 761, Skipped: 0, Total: 761, Duration: 4 s
```
761/761 ✓

### Hrot.Blueprints.Tests
```
Failed!  - Failed: 7, Passed: 1357, Skipped: 8, Total: 1372, Duration: 31 s
```
7 failures — all pre-existing DEBT-006 (golden snapshot failures). 0 new failures.

Pre-existing failures confirmed:
- `MoveToAndFire_GeneratedSource_Snapshot` (snapshot divergence, DEBT-006)
- 6 other DEBT-006/014 tests (same category as baseline)

### `dotnet build IOS-IG-SimHost.sln`
```
0 Error(s)
26 Warning(s)  — ALL pre-existing (DEBT-BCP-004); 0 new in touched projects
```

---

## Developer Insights

1. **RS2008 trap:** `EnforceExtendedAnalyzerRules=true` triggers RS2008 for any `static readonly DiagnosticDescriptor` field, even in generators. The fix is to create the descriptor inline in the method that creates the diagnostic. BlueprintIncrementalGenerator already uses this pattern — mirroring it exactly avoids the error.

2. **`System.Numerics` in HSM topology core:** The full `HsmEmitCore.Emit()` requires `System.Numerics` for `Vector2` in `EmitLayout`. `EmitTopologyCore` excludes the layout method entirely, so `System.Numerics` is correctly omitted from the usings. This was a subtle difference from BTree (which also uses `System.Numerics` in layout, so same exclusion applies).

3. **Generator test pattern — direct project reference vs analyzer reference:** The test project references the generator as a plain project reference (not `OutputItemType="Analyzer"`). This allows instantiating the generator types directly in `CSharpGeneratorDriver.Create(...)`. If referenced as an analyzer, the generator runs automatically on the test project's compilation, which is not what we want.

4. **GeneratorDriver RunResult structure:** `GeneratorDriverRunResult.GeneratedTrees` contains one `SyntaxTree` per `AddSource` call; `GeneratorDriverRunResult.Diagnostics` aggregates all `ReportDiagnostic` calls. The `FilePath` property on a `SyntaxTree` from a generator contains the hint name passed to `AddSource`.

5. **Stable generator output:** The generator output is stable (deterministic) because `EmitTopologyCore` is deterministic (proven by the `EmitTopologyCore_IsDeterministic` tests) and the JSON deserialization is lossless (proven by PU-105).

6. **Weak point — `EnforceExtendedAnalyzerRules` + `TreatWarningsAsErrors`:** The generator project has both enabled. Any analyzer warning becomes a build error. This caught the RS2008 issue immediately but also means any new Roslyn analyzer rule upgrade could silently break the build. The Blueprint generator has the same configuration — this is by design (strictness).

7. **No build wiring into Hrot.AI.Behaviors yet:** Per constraints (PU-204 deferred), no `<AdditionalFiles>` glob added, no `.cs` decommit. The generators are tested in isolation via `CSharpGeneratorDriver`.

---

## Known Issues

None. All success criteria met.

---

## Suggested Commit Message

```
feat(persistence): BTree+HSM IncrementalGenerators + emit layout-excluding mode (BATCH-03)

Completes PU-201, PU-202, PU-205 (Task 1-3).

Task 1 — EmitTopologyCore added to BTreeEmitCore + HsmEmitCore:
  EmitInternal(dto, includeLayout) shared impl; Emit() = full (with [*Layout],
  BATCH-02 gate unchanged, 88/88); EmitTopologyCore() = CreateBuilder+thunk only,
  no [*Layout] nor layout namespace in usings. Both deterministic.

Task 2 — New netstandard2.0 Hrot.AiEditor.Generators (mirroring
  Hrot.Blueprints.Generators exactly: IsRoslynComponent, EnforceExtendedAnalyzerRules,
  CodeAnalysis 4.8.0 PrivateAssets=all, ProjectRef AiEditor.Persistence
  PrivateAssets=all ExcludeAssets=runtime):
  BTreeJsonGenerator (*.btree.json) + HsmJsonGenerator (*.hsm.json) —
  each: AdditionalTextsProvider.Where → deserialize → EmitTopologyCore →
  AddSource {Name}.g.cs; per-asset failure → BTREE0001/HSM0001 diagnostic
  (inline descriptor, RS2008-safe), never throws, never fails siblings.
  Added to IOS-IG-SimHost.sln.

Task 3 — Hrot.AiEditor.Generators.Tests (net8.0, 26 tests):
  GeneratorDriver tests: valid → source contains CreateBuilder+[*Definition],
  NOT [*Layout]; malformed → diagnostic; sibling safety (2 texts: good+bad →
  good still emits). Layout-excluding unit tests. PU-205 exact-string byte-
  identical equivalence: json→generator output == EmitTopologyCore(ToDto(model))
  for SampleScout + SampleGuard. Fails loud on divergence.

Build: 0 errors / 0 new warnings; Generators 26/26; Persistence 88/88;
EditorSubsystemBoot 10/10; AiShared 761/761; Blueprints 7 pre-existing/0 new.
```
