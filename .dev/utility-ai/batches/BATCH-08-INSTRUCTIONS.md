# BATCH-08: UtilityInputGenerator + UtilityAutoDiscovery (TASK-UAI-P2-01 + TASK-UAI-P2-04)

**Batch Number:** BATCH-08
**Tasks:** TASK-UAI-P2-01 (`UtilityInputGenerator`), TASK-UAI-P2-04 (Startup handshake)
**Phase:** Phase 2 — Source generator + analyzer
**Priority:** HIGH
**Dependencies:** BATCH-07 complete (all Phase 1 tasks done)

---

## Developer Guide & Workflow

**Developer workflow guide:** `.dev/.guides/DEV-GUIDE.md`
**Task definitions:** `.dev/utility-ai/TASK-DETAIL.md` — see TASK-UAI-P2-01, TASK-UAI-P2-04
**Design reference (MANDATORY reading):** `.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md`
**Previous review:** `.dev/utility-ai/reviews/BATCH-07-REVIEW.md` — verdict APPROVED WITH DEV-LEAD FIXES
**Debt tracker:** `.dev/utility-ai/DEBT-TRACKER.md`

**Report submission:** `.dev/utility-ai/reports/BATCH-08-REPORT.md`
**Questions:** `.dev/utility-ai/questions/BATCH-08-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in order with passing tests at each step.**

1. **Task 1** (runtime rename + UtilityAutoDiscovery): Implement → Write tests → **ALL tests pass** ✅
2. **Task 2** (`In` class cleanup + UtilityInputGenerator): Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation is complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including all previous utility AI tests)

**DO NOT stop to ask permission** for obvious steps like running tests, fixing compilation errors,
or iterating until tests pass. Complete the entire batch and write the report.

---

## Context

Phase 1 is fully complete (BATCH-01 through BATCH-07). This batch begins Phase 2 by implementing
the Roslyn source generator that eliminates manual registration boilerplate for Utility AI inputs.

The generator lives in `Fdp.Toolkits.Analyzers` (already wired as an Analyzer to `Fdp.Toolkits`
via the `OutputItemType="Analyzer"` project reference in `Fdp.Toolkits.csproj`). Because the
analyzer project is already referenced, the generator will run automatically on the `Fdp.Toolkits`
assembly as soon as it is added. This creates a naming conflict with the existing
`UtilityInputRegistrar` class — **the rename in Task 1 must come before adding the generator.**

**Key design doc sections to read before coding:**
- §1 (Scope), §2 (Attributes), §3 (UtilityInputGenerator), §5 (Startup handshake), §10 (Test strategy)

---

## 🎯 Batch Objectives

1. Rename the existing runtime input lookup table to free the name `UtilityInputRegistrar` for the generator.
2. Create `[UtilityRegistrar]` attribute and `UtilityAutoDiscovery.ScanAndRegister()`.
3. Implement `UtilityInputGenerator` that auto-generates the input registrar and `In` accessors.
4. Integrate the generator with the production project (make `In` partial, remove manual methods).
5. Full test coverage for all success conditions SC-P2-01-1 through SC-P2-01-4 and SC-P2-04-1 through SC-P2-04-2.

---

## ✅ Task 1: Runtime Rename + UtilityAutoDiscovery

### 1-A: Rename `UtilityInputRegistrar` → `UtilityInputReaderStore`

The existing `UtilityInputRegistrar` class is the runtime function-pointer lookup table. The
generator will emit a class ALSO named `UtilityInputRegistrar` (per design §3.3). To resolve this
conflict, rename the existing class **before** adding the generator.

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs`

Rename the class declaration from:
```csharp
public static unsafe class UtilityInputRegistrar
```
to:
```csharp
public static unsafe class UtilityInputReaderStore
```

Update all references within the same file (the `UtilityScorer` class uses `TryGet`).

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs`

In `StandardInputs.RegisterAll()`, change all 17 `UtilityInputRegistrar.Register(...)` calls to
`UtilityInputReaderStore.Register(...)`.

**Test files to update** (change `UtilityInputRegistrar` → `UtilityInputReaderStore` everywhere):
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Integration/UtilityTransitionArbiterTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Integration/UtilitySelectorNodeTests.cs`

After this rename, run:
```
dotnet build FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj --no-incremental -v quiet
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Utility" --verbosity quiet
```
All existing utility tests must pass before proceeding.

### 1-B: Create `[UtilityRegistrar]` attribute and `UtilityAutoDiscovery`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityRegistrarAttribute.cs`

Design reference: `Utility_AI_SourceGenerator_Design_v1_1.md` §5.

```csharp
// Attribute that marks a generated class as a Utility AI registrar.
// Used by UtilityAutoDiscovery to find and invoke RegisterAll at startup.
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class UtilityRegistrarAttribute : Attribute { }

// Scans all loaded assemblies for [UtilityRegistrar] types and calls their
// static void RegisterAll() method exactly once.
public static class UtilityAutoDiscovery
{
    private static volatile bool _initialized = false;
    private static readonly object _lock = new object();

    // One-time scan. Safe to call multiple times; only the first call does work.
    public static void ScanAndRegister()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;
            ScanInternal();
        }
    }

    private static void ScanInternal()
    {
        var attrType = typeof(UtilityRegistrarAttribute);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            foreach (var type in types)
            {
                if (type.GetCustomAttributes(attrType, false).Length == 0) continue;
                var method = type.GetMethod(
                    "RegisterAll",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                method?.Invoke(null, null);
            }
        }
    }

    // FOR TESTS ONLY. Resets the initialized flag so tests can call ScanAndRegister
    // multiple times in the same process.
    internal static void ResetForTesting() => _initialized = false;
}
```

Place in namespace `Fdp.Toolkit.Utility`.

### 1-C: Tests for UtilityAutoDiscovery

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityAutoDiscoveryTests.cs`

Design reference: `Utility_AI_SourceGenerator_Design_v1_1.md` §5, TASK-DETAIL.md SC-P2-04-1 and SC-P2-04-2.

Tests must be in namespace `Fdp.Toolkit.Tests`.

Write 3 tests:

**SC-P2-04-1: `ScanAndRegister_InvokesRegistrarInCurrentAssembly`**
- Define a `[UtilityRegistrar]` class with a static `RegisterAll()` that sets a static `bool` flag
  (use a test-local static field with unique name)
- Call `UtilityAutoDiscovery.ResetForTesting()`
- Call `UtilityAutoDiscovery.ScanAndRegister()`
- Assert the flag was set

**SC-P2-04-2: `ScanAndRegister_SecondCallDoesNotReinvoke`**
- Similar setup with a call counter (int)
- Call `ResetForTesting()`
- Call `ScanAndRegister()` twice
- Assert counter == 1 (RegisterAll called exactly once)

**`ScanAndRegister_IgnoresClassesWithoutAttribute`**
- Define a class WITHOUT `[UtilityRegistrar]` with a `RegisterAll()` that sets a flag
- Call `ResetForTesting()` + `ScanAndRegister()`
- Assert flag was NOT set

After writing these tests, run them. All 3 must pass.

---

## ✅ Task 2: `UtilityInputGenerator` + Production Integration

### 2-A: Create `SharedUtilityDiagnostics`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs`

Design reference: `Utility_AI_SourceGenerator_Design_v1_1.md` §6 (the diagnostic table).

Pattern: mirrors `SharedBhuDiagnostics.cs` in the same project.

```csharp
// Centralised diagnostic descriptors shared by UtilityInputGenerator and
// UtilityAuthoringAnalyzer (see §6 of the source-generator design doc).
// Centralizing avoids RS1019 duplicate-descriptor warnings when both components
// share a Roslyn host.
internal static class SharedUtilityDiagnostics
{
    // ---- Input attribute diagnostics ----------------------------------------

    // UT0101: [UtilityInput] missing Name
    public static readonly DiagnosticDescriptor UT0101_MissingName = new DiagnosticDescriptor(
        id: "UT0101", ...);

    // UT0102: duplicate input Name across compilation
    public static readonly DiagnosticDescriptor UT0102_DuplicateName = new DiagnosticDescriptor(
        id: "UT0102", ...);

    // UT0103: hash collision (two input names produce same FNV-1a-16)
    public static readonly DiagnosticDescriptor UT0103_HashCollision = new DiagnosticDescriptor(
        id: "UT0103", ...);

    // ---- Signature diagnostics -----------------------------------------------

    // UT0110: [UtilityInput] method is not static
    public static readonly DiagnosticDescriptor UT0110_NotStatic = new DiagnosticDescriptor(
        id: "UT0110", ...);

    // UT0111: [UtilityInput] does not return float
    public static readonly DiagnosticDescriptor UT0111_NotFloat = new DiagnosticDescriptor(
        id: "UT0111", ...);

    // UT0112: [UtilityInput] parameter is not (in UtilityInputCtx)
    public static readonly DiagnosticDescriptor UT0112_WrongSignature = new DiagnosticDescriptor(
        id: "UT0112", ...);
}
```

Fill in appropriate `title`, `messageFormat`, `category` (`"Fdp.UtilityAI"`), `defaultSeverity`
(`DiagnosticSeverity.Error` for all), and `isEnabledByDefault: true` for each descriptor.
Refer to `SharedBhuDiagnostics.cs` for the exact format and style.

This file must target `netstandard2.0` (no `#nullable enable`, no net8 APIs). See
`Fdp.Toolkits.Analyzers.csproj` for the target framework constraint.

### 2-B: Create `UtilityInputGenerator`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityInputGenerator.cs`

Design reference: `Utility_AI_SourceGenerator_Design_v1_1.md` §3 (all subsections), §10 (tests).

Pattern: mirrors `BTreeActionGenerator.cs` in the same project.

**Implementation requirements:**

**Pipeline (§3.2):**
```
SyntaxProvider.CreateSyntaxProvider
    predicate: node is MethodDeclarationSyntax with AttributeLists
    transform: GetUtilityInputInfo(ctx) → UtilityInputInfo? (null to filter)
        .Where(m => m != null)
        .Collect()
        .Combine(CompilationProvider)
    RegisterSourceOutput → Execute(spc, compilation, inputs)
        → AddSource("UtilityInputRegistrar.g.cs", ...)
        → AddSource("UtilityInputAccessors.g.cs", ...)
```

**`GetUtilityInputInfo` transform:**
- Get `IMethodSymbol` from context
- Check for `UtilityInputAttribute` (short name: `"UtilityInputAttribute"` or `"UtilityInput"`)
- Extract the `Name` property/argument value
- If `Name` is null or empty → emit UT0101, return null
- Validate static → UT0110 if not, return null
- Validate return type is `float` → UT0111 if not, return null
- Validate exactly 1 parameter, type name contains `UtilityInputCtx` → UT0112 if not, return null
- Return `UtilityInputInfo` with: Name, FullyQualifiedMethodName, Namespace

**`Execute` (source output):**
1. Check for duplicate names → UT0102 on each duplicate after the first
2. Check for hash collisions → UT0103 on the second method with a message naming both
3. Emit `UtilityInputRegistrar.g.cs`
4. Emit `UtilityInputAccessors.g.cs`

**Hash formula (CRITICAL — §3.3):**
This MUST match the existing `StandardInputIds` constants exactly. Do NOT use a native FNV-1a-16.
```csharp
// 32-bit FNV-1a, return low 16 bits. Matches BTree/HSM generators exactly.
static ushort Fnv1a16(string s)
{
    uint hash = 2166136261u;
    foreach (char c in s)
    {
        hash ^= (uint)c;
        hash *= 16777619u;
    }
    return (ushort)(hash & 0xFFFF);
}
```
The hash is computed at gen-time and emitted as a hex literal in the generated file.

**`UtilityInputRegistrar.g.cs` emitted structure:**
```csharp
// <auto-generated/>
#nullable disable
using System;
namespace {containingNamespace}
{
    [global::Fdp.Toolkit.Utility.UtilityRegistrar]
    public static unsafe class UtilityInputRegistrar
    {
        public static void RegisterAll()
        {
            global::Fdp.Toolkit.Utility.UtilityInputReaderStore.Register(
                0x{hash:X4},
                &{fullyQualifiedMethodName});
            // ... one entry per valid [UtilityInput]
        }
    }
}
```

The namespace is derived from the containing type of the first `[UtilityInput]` method found,
or falls back to the assembly name root namespace if methods come from multiple namespaces.

**`UtilityInputAccessors.g.cs` emitted structure:**
```csharp
// <auto-generated/>
#nullable disable
namespace Fdp.Toolkit.Utility
{
    public static partial class In
    {
        // Name="{Name}" hash=0x{hash:X4}
        public static global::Fdp.Toolkit.Utility.InputRef {Name}(
            global::Fdp.Toolkit.Utility.InputContext ctx = default)
            => new global::Fdp.Toolkit.Utility.InputRef(0x{hash:X4}, ctx);
        // ... one per valid [UtilityInput]
    }
}
```

The `In` partial is always emitted in `Fdp.Toolkit.Utility` namespace.

### 2-C: Make `In` class `partial` and remove conflicting methods

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs`

1. Change `public static class In` → `public static partial class In`
2. **Remove** the following accessor methods (they will be generated):
   - `AmmoFraction`
   - `WeaponHasAmmo`
   - `WeaponReadiness`
   - `HealthFraction`
   - `ContactHealthFraction`
   - `ContactThreatLevel`
   - `HasLineOfSight`
   - `HaveLiveTarget`
   - `EnemyStrengthRatio`
   - `IsAssignedTarget`
   - `AllyAdvancingNearby`
   - `WeaponRangeBandFit`
   - `WeaponEffectivenessVsTarget`

3. **Keep** these methods (they have parameterized overloads that differ from the simple generated signatures, or they provide additional params):
   - `DistanceToContext(InputContext ctx, float maxRange)` — 2-param overload
   - `EqsTopScore(string templateName, InputContext ctx)` — string-param overload
   - `EqsResultCount(string templateName, InputContext ctx)` — string-param overload
   - `Constant(float value, InputContext ctx)` — value-param overload
   - `Fnv1a32(string name)` — utility helper (keep)

After this change, the project will fail to compile until the generator runs. The generator runs
automatically when you build — verify by building and checking that `UtilityInputRegistrar.g.cs`
appears in the `obj/` folder.

**Important:** After the generator runs, you MUST verify that:
- All removed methods are now provided by the generated `UtilityInputAccessors.g.cs`
- The `WeaponRangeBandFit` delegation in the old `WeaponEffectivenessVsTarget` is no longer needed
  because the generated version creates `InputRef(StandardInputIds.WeaponEffectivenessVsTarget, ctx)`
  which dispatches to the `WeaponEffectivenessVsTarget` reader that internally delegates to
  `WeaponRangeBandFit` reader — this is correct behavior

### 2-D: Generator tests

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityInputGeneratorTests.cs`

Design reference: `Utility_AI_SourceGenerator_Design_v1_1.md` §10, TASK-DETAIL.md SC-P2-01-1 through SC-P2-01-4.

Pattern: use `CSharpGeneratorDriver` exactly as `BTreeActionGeneratorTests.cs` (at
`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeActionGeneratorTests.cs`) — study it before writing.

Use a `CommonStubs` constant with all the minimal stubs needed:
```csharp
private const string CommonStubs = @"
namespace Fdp.Toolkit.Utility
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UtilityInputAttribute : System.Attribute
    {
        public string Name { get; }
        public UtilityInputAttribute(string name) { Name = name; }
    }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class UtilityRegistrarAttribute : System.Attribute { }

    public enum InputContext : byte { Self, Target, Leader, Candidate }

    public struct UtilityInputCtx { }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    public readonly struct InputRef
    {
        public readonly ushort InputId;
        public readonly InputContext Context;
        public InputRef(ushort inputId, InputContext context = default) 
        { InputId = inputId; Context = context; }
    }

    public static unsafe class UtilityInputReaderStore
    {
        public static void Register(ushort id, delegate*<in UtilityInputCtx, float> reader) { }
    }

    public static partial class In { }
}
";
```

**Helper to run the generator** (same pattern as `BTreeActionGeneratorTests`):
```csharp
private static (GeneratorDriverRunResult result, Compilation outputCompilation) RunGenerator(
    string source)
{
    // ... same MetadataReference setup as BTreeActionGeneratorTests.cs ...
    var compilation = CSharpCompilation.Create("TestAssembly",
        new[] { CSharpSyntaxTree.ParseText(CommonStubs),
                CSharpSyntaxTree.ParseText(source) },
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: true));
    var generator = new UtilityInputGenerator();
    var driver = CSharpGeneratorDriver.Create(generator);
    driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
        compilation, out var outputCompilation, out _);
    return (driver.GetRunResult(), outputCompilation);
}
```

**SC-P2-01-1: `ThreeInputMethods_EmitRegistrarAndAccessors`**
- Input: three valid `[UtilityInput]` static float methods in `Fdp.Toolkit.Utility.StandardInputs`
- Assert: exactly 2 generated files (UtilityInputRegistrar.g.cs and UtilityInputAccessors.g.cs)
- Assert: registrar contains 3 `Register(` calls
- Assert: accessors contain 3 method definitions in the `In` partial class
- Assert: output compilation has zero errors

**SC-P2-01-2: `HashParity_AmmoFraction_MatchesStandardInputIds`**
- This is the MOST IMPORTANT test (see design §10)
- Run generator on a single `[UtilityInput("AmmoFraction")]` method
- Extract the emitted hash from the registrar source text (parse the `0x????` literal)
- Assert `extractedHash == 0x2C39u` (matches `StandardInputIds.AmmoFraction`)
- Also assert the independent reference: verify `Fnv1a16("AmmoFraction") == 0x2C39`
  by computing it inline in the test
- Pin at least 3 names: `"AmmoFraction" → 0x2C39`, `"HealthFraction" → 0x13D9`,
  `"HaveLiveTarget" → 0xC20C`

  To compute these: look up `StandardInputIds.cs` for the existing constant values.

**SC-P2-01-3: `HashCollision_EmitsUT0103`**
- Craft two names with the same `Fnv1a16` hash (or simulate by having the generator
  handle it — you can find a real collision by testing different name combinations,
  OR use a mock approach: add a second `[UtilityInput]` method with a name specifically
  chosen to collide)
- Easier approach: create two input methods with names `"Foo"` and `"Bar"`, then
  manually check if they collide; if you cannot find a real collision for the test,
  you may temporarily make the hash function return the same value for both in a special
  test mode. **PREFERRED APPROACH:** Find two real strings that collide in the 16-bit
  space. You can compute this with a small helper in the test class. Add a helper
  `static ushort Fnv1a16Ref(string s)` that computes the reference hash, then iterate
  names until you find two that produce the same 16-bit result.
- Assert: exactly 1 diagnostic with ID `"UT0103"` on the second method

**SC-P2-01-4: Three separate tests for signature violations:**
- `NonStaticMethod_EmitsUT0110`: instance method with `[UtilityInput]` → UT0110
- `NonFloatReturn_EmitsUT0111`: static method returning `int` → UT0111
- `WrongSignature_EmitsUT0112`: static method taking `int` param → UT0112

Run all 8 generator tests. All must pass.

Then run the full utility test suite:
```
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Utility" --verbosity quiet
```

### 2-E: Build verification

After all code is written and tests pass, verify the full solution builds:
```
dotnet build FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj --no-incremental -v quiet 2>&1 | Select-String -Pattern "error|warning|Build succeeded|FAILED"
```

Check that:
- Zero build errors
- The generated files `UtilityInputRegistrar.g.cs` and `UtilityInputAccessors.g.cs` appear in the
  `obj/` folder (check `obj\Debug\net8.0\generated\Fdp.Toolkit.Behavior.Analyzers\...`)
- The `In.*` accessor calls in `CombatPostureDecision.cs`, `ThreatRankingDecision.cs`,
  `WeaponSelectionDecision.cs`, and `LeaderAssignmentDecision.cs` all resolve without errors

---

## 🧪 Testing Requirements

| Suite | Minimum | Required |
|---|---|---|
| `UtilityAutoDiscoveryTests` | 3 tests | SC-P2-04-1, SC-P2-04-2, negative test |
| `UtilityInputGeneratorTests` | 8 tests | SC-P2-01-1, SC-P2-01-2, SC-P2-01-3, SC-P2-01-4 (×3) |

All previously passing utility AI tests must continue to pass (currently 124 in `Fdp.Toolkits.Tests`
in the Utility namespace — check the exact count with `--filter "FullyQualifiedName~Utility"`).

---

## ⚠️ Quality Standards

**Test quality:**
- Generator tests MUST use `CSharpGeneratorDriver` — do not test by parsing files on disk
- SC-P2-01-2 (hash parity) is the single most important test: pin specific known-good values
  from `StandardInputIds` and assert the generator produces exactly those hashes
- Do not write tests that only check "no exception thrown" — check actual generated content

**Generated code quality:**
- The `UtilityInputRegistrar.g.cs` must have `// <auto-generated/>` at the top
- All generated type references must use fully-qualified global paths (e.g.,
  `global::Fdp.Toolkit.Utility.UtilityInputReaderStore`)
- The generated code must compile without errors (verified by `outputCompilation.GetDiagnostics()`)

**Code quality:**
- Follow the `netstandard2.0` constraint for `Fdp.Toolkits.Analyzers` — no `System.Text.Json`,
  no `net8` APIs, no `#nullable enable` (the analyzer project has `CS8632` in NoWarn)
- No new `[Obsolete]` attributes — remove the old manual methods cleanly

---

## 🎯 Success Criteria

- [ ] `UtilityInputReaderStore` compiles (rename complete, all references updated)
- [ ] `UtilityAutoDiscovery.ScanAndRegister()` works (3 tests pass)
- [ ] `UtilityInputGenerator` emits correct registrar + accessors (8 generator tests pass)
- [ ] Production `Fdp.Toolkits` project builds with zero errors using the generated files
- [ ] All 124+ utility AI tests still pass
- [ ] Report submitted to `.dev/utility-ai/reports/BATCH-08-REPORT.md`

---

## 📚 Reference Materials

- **Task definitions:** `.dev/utility-ai/TASK-DETAIL.md` — §TASK-UAI-P2-01, §TASK-UAI-P2-04
- **Design doc (primary):** `.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md` — §1–§5, §10
- **Existing analyzer patterns:**
  - `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs` — pipeline pattern
  - `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedBhuDiagnostics.cs` — shared diagnostics pattern
  - `FDP/Toolkits/Fdp.Toolkits.Analyzers/EqsTemplatePurityAnalyzer.cs` — symbol analysis pattern
- **Existing generator tests:**
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeActionGeneratorTests.cs` — CSharpGeneratorDriver usage
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoRegistrarGeneratorTests.cs` — another example
- **Runtime types to reference:**
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` — `UtilityInputReaderStore` (after rename)
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` — `[UtilityInput]` usage pattern
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` — `In` class
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputIds.cs` — canonical hash values
- **Debt tracker:** `.dev/utility-ai/DEBT-TRACKER.md`
