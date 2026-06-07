# BATCH-08 INSTRUCTIONS

## Tasks
- **EQS-020** — [EqsTemplate] Roslyn source generator and purity analyzer
- **EQS-021** — Hot-reload: StructureHash + hard/soft reset

## References
- Task specs: `.dev/eqs-2/TASK-DETAIL.md` § TASK-EQS-020 and TASK-EQS-021
- Design: `.dev/eqs-2/EQS_Design_v1.3_final.md` §5.6, §6.1–6.4
- Implementation details: `.dev/eqs-2/IMPLEM_DETAILS.md` L:3480–3700
- Task tracker: `.dev/eqs-2/TASK-TRACKER.md`

## Constraints (apply to all files)
- ASCII only — no Unicode in comments or strings
- Minimize diffs — do not reformat unrelated code
- Build must succeed with 0 errors before reporting
- All new unit tests go in `FDP/Toolkits/Fdp.Toolkits.Tests/`
- `[Collection("EqsIntegrationTests")]` on all EQS integration test classes

---

## EQS-020 — Roslyn Source Generator + Purity Analyzer

### 020-A: Add `IEqsTemplateBuilder` and `EqsTemplateBuilder`

**File:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsQueryTemplate.cs`

Add to the bottom of the existing namespace (after `EqsTemplateBase`):

```csharp
/// <summary>
/// Marker interface for the Roslyn source generator Build() overload.
/// Implementations may be no-ops; the generator uses this signature to call Build() at
/// registration time without injecting runtime-service dependencies.
/// </summary>
public interface IEqsTemplateBuilder { }

/// <summary>
/// No-op implementation passed by the generated EqsRegistrar class when calling Build().
/// </summary>
public sealed class EqsTemplateBuilder : IEqsTemplateBuilder { }
```

### 020-B: Add `StructureHash` field and `ComputeStructureHash()` to `EqsQueryTemplate`

**File:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsQueryTemplate.cs`

Inside `struct EqsQueryTemplate`, add after `MaxCandidates`:

```csharp
/// <summary>
/// FNV-1a 64-bit hash over the fully-qualified type names of the Generator and all Tests.
/// Compared each tick to SensorEvalState.CurrentStructureHash to detect hot-reload changes.
/// </summary>
public ulong StructureHash;
```

Add a public instance method to `EqsQueryTemplate`:

```csharp
/// <summary>
/// Computes and returns a 64-bit FNV-1a hash covering the type names of all generators
/// and tests in this template. Zero-allocation; uses stackalloc for intermediate state.
/// </summary>
public ulong ComputeStructureHash()
{
    const ulong FnvOffset = 14695981039346656037UL;
    const ulong FnvPrime  = 1099511628211UL;
    ulong hash = FnvOffset;

    void HashTypeName(System.Type? t)
    {
        if (t == null) return;
        foreach (char c in t.FullName ?? t.Name)
        {
            hash ^= (ulong)(byte)c;
            hash *= FnvPrime;
        }
        // Separator byte
        hash ^= (ulong)'|';
        hash *= FnvPrime;
    }

    HashTypeName(Generator?.GetType());
    if (FilterCheap   != null) foreach (var t in FilterCheap)   HashTypeName(t?.GetType());
    if (FilterExpensive != null) foreach (var t in FilterExpensive) HashTypeName(t?.GetType());
    if (ScoreCheap    != null) foreach (var t in ScoreCheap)    HashTypeName(t?.GetType());
    if (ScoreExpensive != null) foreach (var t in ScoreExpensive) HashTypeName(t?.GetType());
    return hash;
}
```

### 020-C: Add `Build(IEqsTemplateBuilder)` overload to `FindCoverFromTarget`

**File:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/FindCoverFromTarget.cs`

Add a second static `Build` overload (keep the existing one untouched):

```csharp
/// <summary>
/// Overload for the Roslyn source generator. Uses BlockedLosService so no runtime
/// dependencies are required. The returned template is used only for StructureHash
/// computation, not for live evaluation.
/// </summary>
public static EqsQueryTemplate Build(IEqsTemplateBuilder b)
    => Build(new BlockedLosService());
```

And annotate with the positional AssetId constructor (the attribute already uses positional arg):

Check `FindCoverFromTarget.cs` — it already has `[EqsTemplate("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d")]`. No change needed there.

### 020-D: Implement `EqsTemplateGenerator` (IIncrementalGenerator)

**File (NEW):** `FDP/Toolkits/Fdp.Toolkits.Analyzers/EqsTemplateGenerator.cs`

Follow the exact pattern in `IMPLEM_DETAILS.md` L:3500–3585 (the generator code block).

Key points:
- Namespace: `Fdp.Toolkit.Behavior.Analyzers` (matches existing analyzers in the project)
- `[Generator]` + `IIncrementalGenerator`
- Predicate: `ClassDeclarationSyntax` with attribute lists
- Transform: look for `EqsTemplateAttribute` by name; read `AssetId` from **positional** constructor arg index 0 (not named arg): `attr.ConstructorArguments[0].Value as string`
- FNV-1a 32-bit: offset=2166136261u, prime=16777619u, cast each char to uint
- Generated namespace: `Fdp.Toolkit.Spatial.Eqs.Generated`
- Generated class name: `EqsRegistrar_{assemblyName}` (dots replaced with `_`)
- `[BlueprintRegistrar]` attribute on generated class (using `Fdp.Toolkit.Blueprints.Attributes`)
- Each template entry:
  ```csharp
  var template_{blueprintId} = {fullyQualifiedName}.Build(new EqsTemplateBuilder());
  staging.Add({blueprintId}, new BlueprintDefinition
  {
      Name = "{fullyQualifiedName}",
      Kind = BlueprintDispatchKind.Library,
      StructureHash = template_{blueprintId}.ComputeStructureHash(),
      StateSize = 0,
  });
  ```
- Output file: `$"EqsRegistrar_{assemblyName}.g.cs"`
- The `EqsTemplateInfo` record: `(string FullyQualifiedName, string AssetId, int BlueprintId)`
- If no templates found, emit nothing (early return)

Check `IMPLEM_DETAILS.md` L:3500–3585 for the exact scaffold.

**Important:** The generated `using` directives must include:
```csharp
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fdp.Toolkit.Spatial.Eqs;
```

### 020-E: Implement `EqsTemplatePurityAnalyzer` (DiagnosticAnalyzer)

**File (NEW):** `FDP/Toolkits/Fdp.Toolkits.Analyzers/EqsTemplatePurityAnalyzer.cs`

- Namespace: `Fdp.Toolkit.Behavior.Analyzers`
- `[DiagnosticAnalyzer(LanguageNames.CSharp)]`
- Diagnostic ID: `"EQS_001"`, severity: `Warning`
- Message: `"Method '{0}' in [EqsTemplate] class must be static with a single IEqsTemplateBuilder parameter"`
- Diagnostic ID: `"EQS_002"`, severity: `Warning`
- Message: `"Method '{0}' in [EqsTemplate] class reads non-constant state. Build() must be pure."`

**Analysis logic (RegisterSymbolAction on SymbolKind.Method):**

For each method named `"Build"` on a class with `EqsTemplateAttribute`:
1. Emit EQS_001 if: NOT static OR parameter count != 1 OR first param type name != `"IEqsTemplateBuilder"`
2. For EQS_002: scan method body's `IdentifierNameSyntax` nodes for accesses to instance fields or static mutable fields (simplified: flag any `static` field reads on `[EqsTemplate]` classes).

For the test, keep EQS_002 simple — only flag if the method references a field declared in the same class that is static (non-const). Use `RegisterSyntaxNodeAction` on `FieldDeclarationSyntax` within `[EqsTemplate]` classes to detect static non-const fields, then in the method analysis, flag if any are referenced.

**Simplified acceptable implementation (less complex but still correct for the defined success conditions):**
- EQS_001: check `Build()` signature in any `[EqsTemplate]` class
- EQS_002: check if any `[EqsTemplate]` class `Build()` accesses a static field in its own class

### 020-F: Unit tests for the generator

**File (NEW):** `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsTemplateGeneratorTests.cs`

Follow the exact same in-memory Roslyn test pattern used in `GizmoRegistrarGeneratorTests.cs` (same `RunGenerator` helper structure — read that file for reference).

**Test T-EGN1 — `EqsTemplateGenerator_EmitsCorrectBlueprintId_ForKnownAssetId`:**
- Input source: a class with `[EqsTemplate("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d")]` and `static EqsQueryTemplate Build(IEqsTemplateBuilder b) => default;`
- Assert generated source contains `staging.Add(` and `2011734044` (pre-computed FNV-1a 32-bit of that GUID string — compute it manually and hard-code the expected int)
- Assert generated source contains `EqsRegistrar_`

**Test T-EGN2 — `EqsTemplateGenerator_NoOutput_WhenNoEqsTemplateAttribute`:**
- Input source: a plain class with no attribute
- Assert generated source is empty string

**Test T-EGN3 — `EqsTemplateGenerator_EmitsRegisterMethod_WithCorrectStructure`:**
- Input: same as T-EGN1
- Assert generated source contains `public static void Register(BlueprintRegistryStaging staging)`
- Assert generated source contains `[BlueprintRegistrar]`
- Assert generated source contains `.ComputeStructureHash()`

**Common stubs for the in-memory compilation** (add to `CommonStubs` const string):
```csharp
namespace Fdp.Toolkit.Spatial.Eqs
{
    public sealed class EqsTemplateAttribute : System.Attribute
    {
        public EqsTemplateAttribute(string assetId) { }
    }
    public struct EqsQueryTemplate { public ulong ComputeStructureHash() => 0; }
    public interface IEqsTemplateBuilder { }
    public sealed class EqsTemplateBuilder : IEqsTemplateBuilder { }
}
namespace Fdp.Toolkit.Blueprints
{
    public sealed class BlueprintRegistryStaging
    {
        public void Add(int id, BlueprintDefinition def) { }
    }
    public struct BlueprintDefinition
    {
        public string Name;
        public BlueprintDispatchKind Kind;
        public ulong StructureHash;
        public int StateSize;
    }
    public enum BlueprintDispatchKind { Library }
}
namespace Fdp.Toolkit.Blueprints.Attributes
{
    public sealed class BlueprintRegistrarAttribute : System.Attribute { }
}
```

**Helper `RunGenerator` method** (same pattern as `GizmoRegistrarGeneratorTests`):
- Create in-memory compilation with `CSharpSyntaxTree.ParseText(userSource + CommonStubs)`
- Use trusted platform assemblies filter (CoreLib, Runtime, Collections)
- Run `EqsTemplateGenerator` via `CSharpGeneratorDriver.Create`
- Collect generated source trees (those not in original compilation)
- Return `(Diagnostics, GeneratedSource)`

**FNV-1a 32-bit of `"f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d"`:** compute in a helper method or constant. The exact value must be verified by running the same algorithm. Add a helper:
```csharp
private static int ComputeFnv1a32(string s)
{
    uint h = 2166136261u;
    foreach (char c in s) { h ^= (uint)c; h *= 16777619u; }
    return (int)h;
}
```
And in T-EGN1, assert `generatedSource.Contains(ComputeFnv1a32("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d").ToString())`.

### 020-G: Unit tests for the purity analyzer

**File (NEW):** `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsTemplatePurityAnalyzerTests.cs`

Use the same Roslyn in-memory compilation pattern. Run both generator + analyzer together OR just the analyzer separately via `CSharpCompilation.WithAnalyzers`.

**Test T-EPA1 — `PurityAnalyzer_FlagsNonStaticBuild`:**
```csharp
[EqsTemplate("some-guid")]
public class MyTemplate
{
    public EqsQueryTemplate Build(IEqsTemplateBuilder b) => default; // non-static
}
```
- Assert diagnostic EQS_001 is reported

**Test T-EPA2 — `PurityAnalyzer_AcceptsStaticBuildWithCorrectParam`:**
```csharp
[EqsTemplate("some-guid")]
public class MyTemplate
{
    public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => default;
}
```
- Assert no EQS_001 diagnostic

**Test T-EPA3 — `PurityAnalyzer_FlagsBuildWithWrongParam`:**
```csharp
[EqsTemplate("some-guid")]
public class MyTemplate
{
    public static EqsQueryTemplate Build(int x) => default; // wrong param
}
```
- Assert EQS_001 diagnostic reported

**Analyzer test helper `RunAnalyzer`:**
- Create in-memory compilation (same stubs as generator tests)
- Run `compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new EqsTemplatePurityAnalyzer()))`
- Get diagnostics from `analyzers.GetAnalyzerDiagnosticsAsync().Result`

---

## EQS-021 — Hot-reload: StructureHash Hard/Soft Reset

### 021-A: Hard-reset detection in `EqsSolverSystem.EvaluateSensor`

**File:** `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs`

After the existing epoch soft-reset check (which resets phase on epoch mismatch) and after looking up the template from `IEqsTemplateRegistry`, add the hard-reset block:

Locate the point after `registry.TryGetTemplate(...)` succeeds and after the `evalState` lazy-init. Insert:

```csharp
// Hard-reset: detect structural hot-reload by comparing template's StructureHash
// against what the SensorEvalState recorded on last evaluation.
ulong liveHash = template.ComputeStructureHash();
if (liveHash != 0 && evalState.CurrentStructureHash != liveHash)
{
    evalState.Phase                = EqsEvalPhase.Idle;
    evalState.PendingRaycastCount  = 0;
    evalState.CurrentStructureHash = liveHash;
    if (repo.HasComponent<EqsCognitiveBuffer>(entity))
    {
        ref var buffer = ref repo.GetComponentRW<EqsCognitiveBuffer>(entity);
        buffer.IsReady = false;
    }
}
```

Also, when the solver successfully completes an evaluation (before publishing the `EqsResultEvent`), store the current hash:
```csharp
evalState.CurrentStructureHash = template.ComputeStructureHash();
```

Place this immediately before writing `evalState.Phase = EqsEvalPhase.Idle` at the end of successful evaluation.

**Important:** `ComputeStructureHash()` uses reflection on the generator/test types. It is called at most twice per sensor per solver tick (once for hard-reset check, once to update after completion). This is acceptable given the 10 Hz driver.

### 021-B: Unit tests for hard-reset

**File (NEW):** `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsStructureHashTests.cs`

**Test T-SH1 — `ComputeStructureHash_DifferentGenerators_ProduceDifferentHashes`:**
- Create two `EqsQueryTemplate` instances with different `Generator` types (e.g., one with `EntitiesInRadiusGenerator`, one with `CoverPointsGenerator`)
- Assert their `ComputeStructureHash()` values differ

**Test T-SH2 — `ComputeStructureHash_SameStructure_ProducesSameHash`:**
- Create two identical templates (same generator type, same test types)
- Assert `hash1 == hash2`

**Test T-SH3 — `ComputeStructureHash_DifferentTests_ProduceDifferentHashes`:**
- Two templates with same generator but different `FilterCheap` test arrays
- Assert hashes differ

**Test T-SH4 — `EqsSolverSystem_HardReset_WhenStructureHashChanges` (integration):**

Use `EditorHarness` (in `Hrot.ClusterRunner.Integration.Tests`).

**File (NEW):** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/HotReloadTests.cs`

Add `[Collection("EqsIntegrationTests")]`.

Setup:
1. Create `EditorHarness`
2. Create a `SimpleRegistry : IEqsTemplateRegistry` that holds a mutable `EqsQueryTemplate` reference
3. Register it via `harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry)`
4. Spawn entity, attach `EqsSensor` with `BlueprintId` matching the registry's template
5. Pump until `SensorEvalState.Phase == EqsEvalPhase.Idle` and `CurrentStructureHash != 0`
6. Now swap the registry's template to one with a different generator type (different `StructureHash`)
7. Pump one EQS solver tick
8. Assert `SensorEvalState.Phase == EqsEvalPhase.Idle` (hard-reset)
9. Assert `CognitiveBuffer.IsReady == false`

For the template with no tests and a trivial no-op generator:
```csharp
private sealed class NoOpGenerator : IEqsGenerator
{
    public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        => 0;
}
```

Create two templates:
- `TemplateA`: Generator = `new EntitiesInRadiusGenerator()`
- `TemplateB`: Generator = `new CoverPointsGenerator()` (different type → different hash)

The `SimpleRegistry` simply stores and returns one template at a time.

**Test T-SH5 — `EqsSolverSystem_SoftReset_PreservesStructureHash` (integration):**

After T-SH4 pattern, once `CurrentStructureHash` is set:
1. Increment `sensor.Epoch` (soft reset condition)
2. Pump one tick
3. Assert `SensorEvalState.Phase == EqsEvalPhase.Idle` (soft reset fired)
4. Assert `SensorEvalState.CurrentStructureHash` is unchanged (not wiped by soft reset)

### 021-C: AiHotReloadCoordinator wiring note

The generated `[BlueprintRegistrar]` class is auto-discovered by `AiHotReloadCoordinator.LoadAndScan` (existing code already scans for `[BlueprintRegistrar]` via reflection). No code change required to `AiHotReloadCoordinator` itself — the generated registrar participates automatically once the assembly is loaded.

Verify by reading `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs` — confirm it already scans `[BlueprintRegistrar]` types. Add a comment to `EqsModule.cs` noting this wiring:

```csharp
// EQS templates registered via [BlueprintRegistrar] generated by EqsTemplateGenerator
// are picked up automatically by AiHotReloadCoordinator on assembly load.
```

---

## Build and Test Verification

After implementation, verify:

```
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj --no-restore
dotnet build FDP/Toolkits/Fdp.Toolkits.Analyzers/Fdp.Toolkits.Analyzers.csproj --no-restore
dotnet build Hrot/Subsystems/Hrot.SimHost/Hrot.SimHost.csproj --no-restore
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --filter "FullyQualifiedName~Eqs"
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --no-build --filter "FullyQualifiedName~Eqs"
```

Expected results:
- All builds succeed with 0 errors
- Unit EQS tests: all pre-existing 40 + new (minimum 5 new: T-SH1/2/3 + T-EGN1/2/3 + T-EPA1/2/3) pass
- Integration EQS tests: all pre-existing 19 + new (minimum 2 new: T-SH4, T-SH5) pass

---

## Report

When done, write your report to `.dev/eqs-2/reports/BATCH-08-REPORT.md` including:
- Files created/modified
- Test counts (before and after)
- Any deviations from the plan (with justification)
- All test names and pass/fail status
