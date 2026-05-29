# BATCH-09 INSTRUCTIONS

**Target:** TASK-UAI-P2-02 (`UtilityDecisionGenerator`)
**Design reference (primary):** `.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md` §4, §5
**Design reference (secondary):** `.dev/utility-ai/TASK-DETAIL.md` — `TASK-UAI-P2-02` section
**Pattern references:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs`, `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeActionGeneratorTests.cs`
**Success conditions:** SC-P2-02-1, SC-P2-02-2, SC-P2-02-3, SC-P2-02-4

---

## Context

Phase 2, Step 2. The input generator + auto-discovery infrastructure (Phase 2 Step 1) is complete and committed. Now implement the decision generator that scans for `[UtilityDecision]`-attributed classes and emits:
1. A generated `UtilityDecisionCatalog.g.cs` that calls each decision's `Build` at startup
2. Per-decision `partial class` emitting `const int Id`
3. A best-effort `Manifest` array for editor tooling (full vs. partial)

Read the following files **before** starting any implementation:
- `.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md` §4 and §5
- `.dev/utility-ai/TASK-DETAIL.md` TASK-UAI-P2-02 section
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` — `UtilityDecisionAttribute`, `IUtilityDecisionDefinition`, `IUtilityDecisionBuilder`, `UtilityDecisionBuilder`
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs` — existing `UtilityRegistry`, `UtilityDecisionCatalog` (reflective runtime catalog that the generator will supersede)
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityRegistrarAttribute.cs` — `[UtilityRegistrar]`, `UtilityAutoDiscovery`
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityInputGenerator.cs` — generator pattern just established
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs` — shared diagnostics
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityInputGeneratorTests.cs` — test pattern (CSharpGeneratorDriver usage)
- `FDP/Toolkits/Fdp.Toolkits/Utility/Decisions/` — existing starter-pack decision classes (pattern for what `[UtilityDecision]` classes look like)
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs` — integration test pattern

---

## Task 1: Add `UtilityDecisionManifestEntry` to production code

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs` — add after the `UtilityDecisionCatalog` class.

Add a `UtilityDecisionManifestEntry` struct that the generated manifest array will use:

```csharp
/// <summary>
/// Best-effort compile-time snapshot of one decision's structure, emitted by
/// <c>UtilityDecisionGenerator</c> into <c>UtilityDecisionCatalog.g.cs</c>.
/// <para>
/// If <see cref="IsFull"/> is <c>true</c>, <see cref="OptionCount"/> and
/// <see cref="TotalConsiderationCount"/> were statically extracted from the <c>Build</c> body.
/// If <c>false</c>, the editor falls back to runtime reflection on the built
/// <see cref="UtilityDecisionDef"/>.
/// </para>
/// </summary>
public readonly struct UtilityDecisionManifestEntry
{
    public readonly int    BlueprintId;
    public readonly string DisplayName;
    /// <summary>True when the generator fully extracted the Build structure.</summary>
    public readonly bool   IsFull;
    /// <summary>Number of options extracted (0 when IsFull == false).</summary>
    public readonly int    OptionCount;
    /// <summary>Total considerations across all options (0 when IsFull == false).</summary>
    public readonly int    TotalConsiderationCount;

    public UtilityDecisionManifestEntry(int blueprintId, string displayName,
        bool isFull, int optionCount = 0, int totalConsiderationCount = 0)
    {
        BlueprintId              = blueprintId;
        DisplayName              = displayName;
        IsFull                   = isFull;
        OptionCount              = optionCount;
        TotalConsiderationCount  = totalConsiderationCount;
    }
}
```

---

## Task 2: Add `ScanAndRegisterDecisions` to `UtilityAutoDiscovery`

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityRegistrarAttribute.cs`

The existing `ScanAndRegister()` handles `[UtilityRegistrar]` types whose `RegisterAll()` takes no parameters (input registrar). Decision registrars have `RegisterAll(out UtilityRegistry)`. Add a **separate** method:

```csharp
/// <summary>
/// One-time scan for <see cref="UtilityRegistrarAttribute"/> types whose
/// <c>RegisterAll</c> has signature <c>(out UtilityRegistry)</c>.
/// Aggregates all registries from all matching types into a single merged
/// <see cref="UtilityRegistry"/> and returns it via <paramref name="registry"/>.
/// Safe to call multiple times; only the first call does work.
/// </summary>
public static void ScanAndRegisterDecisions(out UtilityRegistry registry)
```

- Use a separate `_decisionsInitialized` volatile bool + lock (do NOT share state with `ScanAndRegister()`).
- Cache the built registry in a static field `_cachedDecisionRegistry`.
- `ResetDecisionsForTesting()` internal method.
- Aggregation: instantiate one `UtilityRegistry`, pass it to each matching `RegisterAll(out ...)` call (OR collect multiple and merge — simplest is to pass a single shared registry by ref to each, since each registrar populates the same registry).

**Note on signature matching:** Look for a `RegisterAll` method with a single `out UtilityRegistry` parameter using `BindingFlags.Public | BindingFlags.Static`. Do NOT call it if only the no-param `RegisterAll` exists; that path is already handled by `ScanAndRegister()`.

---

## Task 3: `UtilityDecisionGenerator` (the main deliverable)

**File:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityDecisionGenerator.cs`

Implement `UtilityDecisionGenerator : IIncrementalGenerator` following the established pattern from `UtilityInputGenerator.cs` and `BTreeActionGenerator.cs`.

### 3.1 Recognition

Pipeline predicate: `ClassDeclarationSyntax` with `AttributeLists.Count > 0`.

Transform (`GetDecisionInfo`): resolve the class symbol and check:
- Has `[UtilityDecision]` attribute (class `UtilityDecisionAttribute`)
- Implements `IUtilityDecisionDefinition`
- Has `public static void Build(IUtilityDecisionBuilder)` method

Extract from the `[UtilityDecision]` attribute's constructor arguments:
- `AssetId` (arg 0) — string
- `DisplayName` (arg 1) — string
- `Kind` (arg 2) — enum value (emit as `global::Fdp.Toolkit.Utility.DecisionKind.<name>`)
- `Category` (named arg or arg 3, default `""`)
- `HysteresisBonus` (named arg or arg 4, default `0f`)

### 3.2 Hash

Compute `blueprintId = FNV-1a-32(AssetId)` **at generation time**, using the same function as
`UtilityDecisionBuilder.ComputeId`. Formula: basis=2166136261u, prime=16777619u, `hash ^= (uint)c`
for each char, return as `int` (bit-cast of the unsigned result). Emit as hex literal `0xXXXXXXXX`.

Note: unlike the input hash (16-bit), the decision ID uses the full 32-bit result cast to `int`.

### 3.3 Emitted files

**File 1: `UtilityDecisionCatalog.g.cs`**

```csharp
// <auto-generated/>
#nullable disable
using System;
namespace <same namespace as first found decision class>
{
    [global::Fdp.Toolkit.Utility.UtilityRegistrar]
    public static class UtilityDecisionCatalog
    {
        public static void RegisterAll(out global::Fdp.Toolkit.Utility.UtilityRegistry registry)
        {
            registry = new global::Fdp.Toolkit.Utility.UtilityRegistry();
            // one block per decision:
            {
                var b = new global::Fdp.Toolkit.Utility.UtilityDecisionBuilder();
                global::<FQN of decision class>.Build(b);
                var attr = new global::Fdp.Toolkit.Utility.UtilityDecisionAttribute(
                    "<AssetId>", "<DisplayName>",
                    global::Fdp.Toolkit.Utility.DecisionKind.<Kind>
                    // include category and hysteresisBonus only if non-default
                );
                var def = b.Build(attr);
                registry.Register(0x<blueprintId>, def, <hysteresisBonus>f);
            }
        }

        public static readonly global::Fdp.Toolkit.Utility.UtilityDecisionManifestEntry[] Manifest =
        {
            // one entry per decision
        };
    }
}
```

**File 2: `UtilityDecisionIds.g.cs`**

For each decision class, emit a `partial class` in its own namespace with `const int Id`:

```csharp
// <auto-generated/>
#nullable disable
namespace <namespace of decision class>
{
    partial class <ClassName>
    {
        // blueprintId: FNV-1a-32("<AssetId>") == 0x<blueprintId>
        public const int Id = unchecked((int)0x<blueprintId>);
    }
}
```

### 3.4 Manifest (best-effort Build body analysis)

In `Execute`, after collecting valid decisions, perform a best-effort walk of each decision class's `Build` method body to determine if the structure can be fully extracted.

**Full extraction criteria:** The `Build` method body contains ONLY fluent `.Option(...)` and `.CandidateOption(...)` chains with immediate `.Consider(...)` calls — no `foreach`, `for`, `if`, variables, or method calls other than the builder's own API.

Walk the body using `SemanticModel`:
1. Get the `Build` `MethodDeclarationSyntax` from the class's syntax tree
2. Check the statement list: if all top-level statements are expression statements containing only invocation chains on the builder parameter, count the `.Option`/`.CandidateOption` calls and the total `.Consider` calls nested within them.
3. If any other statement kind or any non-builder method invocation is found, mark as `partial`.

Emit manifest entries accordingly:
- Full: `new UtilityDecisionManifestEntry(blueprintId, displayName, isFull: true, optionCount, totalConsiderationCount)`
- Partial: `new UtilityDecisionManifestEntry(blueprintId, displayName, isFull: false)`

**Important:** The manifest is emitted purely as documentation/tooling data. The runtime execution path (the `RegisterAll` method) never reads `Manifest`. If the best-effort walk fails or is too complex, simply mark as partial — do NOT fail the generation.

### 3.5 Diagnostics

Reuse `SharedUtilityDiagnostics` for errors. Add to `SharedUtilityDiagnostics.cs`:

```csharp
// UT0140: [UtilityDecision] class missing IUtilityDecisionDefinition
internal static readonly DiagnosticDescriptor UT0140_MissingInterface = ...;

// UT0141: [UtilityDecision] class missing static Build(IUtilityDecisionBuilder) method
internal static readonly DiagnosticDescriptor UT0141_MissingBuildMethod = ...;

// UT0150: duplicate AssetId across two [UtilityDecision] classes
internal static readonly DiagnosticDescriptor UT0150_DuplicateAssetId = ...;
```

All `DiagnosticSeverity.Error`, category `"Fdp.UtilityAI"`.

Validation in `GetDecisionInfo` (per class):
- Missing interface → UT0140
- Missing `Build` method → UT0141

Validation in `Execute` (cross-class):
- Duplicate `AssetId` → UT0150 (keep first occurrence, report later ones)

---

## Task 4: Tests (`UtilityDecisionGeneratorTests.cs`)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityDecisionGeneratorTests.cs`

Use `CSharpGeneratorDriver.Create` exactly as in `UtilityInputGeneratorTests.cs`. Define a `CommonStubs` constant that stubs:
- `UtilityDecisionAttribute`
- `IUtilityDecisionDefinition`
- `IUtilityDecisionBuilder` (with `Option` and `Consider` methods)
- `UtilityDecisionBuilder` (with `Build(UtilityDecisionAttribute)`)
- `UtilityRegistry` (with `Register(int, UtilityDecisionDef, float)`)
- `UtilityDecisionDef`
- `UtilityDecisionManifestEntry`
- `DecisionKind` enum (at minimum `PostureSelect`, `ThreatRanking`)
- `UtilityRegistrarAttribute`

| Test | Success Condition |
|------|-------------------|
| `DecisionClass_EmitsCatalogAndIds` | SC-P2-02-1: one `[UtilityDecision]` class produces `UtilityDecisionCatalog.g.cs` (contains `RegisterAll(out`) + `UtilityDecisionIds.g.cs` (contains `const int Id`); output has 0 errors |
| `BlueprintId_MatchesFnv1a32OfAssetId` | SC-P2-02-2: emitted `const int Id` hex literal equals `(int)Fnv1a32Ref(assetId)` |
| `SimpleFluentBuild_EmitsFullManifest` | SC-P2-02-3: `Build` using only `.Option(...).Consider(...)` → `IsFull = true` in emitted `Manifest` entry (check generated source text) |
| `ForeachBuild_EmitsPartialManifest` | SC-P2-02-4: `Build` containing `foreach` → `IsFull = false` in emitted `Manifest` entry |
| `MissingInterface_EmitsUT0140` | UT0140: class with `[UtilityDecision]` but missing `IUtilityDecisionDefinition` → UT0140 diagnostic |
| `DuplicateAssetId_EmitsUT0150` | UT0150: two classes with the same `AssetId` → UT0150 diagnostic |

Include a `Fnv1a32Ref` helper in the test class (same formula as `In.Fnv1a32` — basis 2166136261u, prime 16777619u, `hash ^= (uint)c`; return `(int)hash`).

---

## Task 5: Update `ScanAndRegisterDecisionsTests`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityAutoDiscoveryTests.cs` — append tests:

Add 2 tests that exercise `ScanAndRegisterDecisions`:

1. `ScanAndRegisterDecisions_InvokesDecisionRegistrar` — place a `[UtilityRegistrar]` class in the test assembly with `RegisterAll(out UtilityRegistry r)` that sets a flag and returns a populated registry. Assert that the flag is set and the registry is non-null.

2. `ScanAndRegisterDecisions_SecondCallDoesNotReinvoke` — counter pattern, same as the input version.

Use `UtilityAutoDiscovery.ResetDecisionsForTesting()` to isolate state.

---

## Task 6: Build verification

```
dotnet build FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj --no-incremental -v quiet
dotnet build FDP\Toolkits\Fdp.Toolkits.Analyzers\Fdp.Toolkits.Analyzers.csproj --no-incremental -v quiet
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Utility" --verbosity quiet
```

Expected: 0 errors, 120+ tests pass (114 pre-existing + 6 new generator tests + 2 new discovery tests).

---

## Key constraints

1. **Analyzer project targets netstandard2.0.** No nullable annotations (`?`), no net8+ APIs. Use `#nullable disable`.
2. **The existing `UtilityDecisionCatalog` reflective class** in `UtilityDecisionCatalog.cs` is NOT removed in this batch — the generator is additive. The reflective catalog coexists with the generated one until a future cleanup batch.
3. **The generated `UtilityDecisionCatalog` class name conflicts** with the existing static class in `UtilityDecisionCatalog.cs` ONLY if they share the same namespace. **Emit the generated class in the same namespace as the decision classes** (e.g., `Fdp.Toolkit.Utility.Decisions`), NOT in `Fdp.Toolkit.Utility` where the existing reflective class lives. This avoids the naming conflict.
4. **`const int Id`** must be `unchecked((int)0x...)` to avoid CS0266 for values above `int.MaxValue`.
5. **Do not modify the existing reflective `UtilityDecisionCatalog.RegisterAll`** method — it remains as a fallback.
6. **`UtilityDecisionBuilder` constructor is public** (parameterless) — confirm before generating code that uses `new UtilityDecisionBuilder()`. If it has a factory method instead, use that.
7. Follow the `AGENTS.md` editing invariants: preserve all existing comments, minimize diffs, no Unicode normalization.

---

## Report

Submit to `.dev/utility-ai/reports/BATCH-09-REPORT.md` with:
1. Summary of what was implemented
2. All success conditions fulfilled (SC-P2-02-1 through SC-P2-02-4)
3. Test results (total count, new tests)
4. Issues encountered and how resolved
5. Design decisions made beyond the spec
6. Suggested git commit message
