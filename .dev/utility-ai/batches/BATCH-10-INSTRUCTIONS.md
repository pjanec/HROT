# BATCH-10 INSTRUCTIONS

**Target:** TASK-UAI-P2-03 (`UtilityAuthoringAnalyzer`)
**Design reference (primary):** `.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md` §6, §6.1, §6.2
**Design reference (secondary):** `.dev/utility-ai/TASK-DETAIL.md` — `TASK-UAI-P2-03` section
**Pattern reference:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/EqsTemplatePurityAnalyzer.cs` (UT0130 pattern)
**Success conditions:** SC-P2-03-1, SC-P2-03-2, SC-P2-03-3

---

## Context

Phase 2, Step 3. The generators are complete (BATCH-08: input generator, BATCH-09: decision generator). Now implement the `UtilityAuthoringAnalyzer` — a pure `DiagnosticAnalyzer` (no source output) that enforces the `UT####` rules at authoring time.

Read the following files **before** starting:
- `.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md` §6, §6.1, §6.2
- `.dev/utility-ai/TASK-DETAIL.md` TASK-UAI-P2-03 section
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/EqsTemplatePurityAnalyzer.cs` — UT0130 purity pattern to copy
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs` — existing descriptors (UT0101-UT0112, UT0140-UT0141, UT0150)
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` — `IUtilityDecisionBuilder.Consider`, `UtilityDecisionAttribute`, `ScoringMode`
- `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/UtilityInputAttribute.cs` — `[UtilityInput]`
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` — `DecisionKind`, `ScoringMode`, `InputContext`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/EqsTemplatePurityAnalyzerTests.cs` — test pattern for `DiagnosticAnalyzer`

---

## ID mapping clarification

Some IDs from the design doc §6 table are already taken by BATCH-09 generators:
- **UT0140** = "missing IUtilityDecisionDefinition" (generator, BATCH-09)
- **UT0141** = "missing Build method" (generator, BATCH-09)

Use the following **remapped IDs** for the authoring analyzer's decision-structure checks:
- **UT0143** — `PostureSelect` decision has zero options
- **UT0144** — Warning: all options are product-mode with gates, no sum-mode fallback
- **UT0145** — Warning: duplicate `OptionId` within a decision (note: this is a generator-time issue; analyzer approximates it by scanning the fluent Build body)

New IDs needed (not yet in SharedUtilityDiagnostics):
- **UT0120** — Error: consideration references an unknown input name
- **UT0121** — Error: input used with context outside its `AllowedContexts`
- **UT0122** — Error: parameterized input missing its required param
- **UT0130** — Error: `Build` reads disallowed runtime state (purity violation)
- **UT0131** — Warning: weight outside [0, 1]
- **UT0143** — Error: `PostureSelect` decision has zero options
- **UT0144** — Warning: all options are product-mode with gates and no sum-mode fallback
- **UT0145** — Warning: duplicate `OptionId` within a decision

Add all new descriptors to `SharedUtilityDiagnostics.cs`.

---

## Task 1: Add new descriptors to `SharedUtilityDiagnostics.cs`

Add the following (UT0120–UT0131, UT0143–UT0145) to `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs`:

```csharp
// ---- Catalog-aware consideration diagnostics ----------------------------

// UT0120: consideration references unknown input name
internal static readonly DiagnosticDescriptor UT0120_UnknownInput = ...;
// UT0121: input used with context not in its allowed set
internal static readonly DiagnosticDescriptor UT0121_WrongContext = ...;
// UT0122: parameterized input missing required param
internal static readonly DiagnosticDescriptor UT0122_MissingParam = ...;

// ---- Purity --------------------------------------------------------

// UT0130: Build reads disallowed runtime state
internal static readonly DiagnosticDescriptor UT0130_ImpureBuild = ...;

// ---- Weight --------------------------------------------------------

// UT0131: weight outside [0, 1]
internal static readonly DiagnosticDescriptor UT0131_WeightOutOfRange = ...;

// ---- Decision structure --------------------------------------------

// UT0143: PostureSelect decision has zero options
internal static readonly DiagnosticDescriptor UT0143_ZeroOptions = ...;
// UT0144: all options product-mode with gates, no sum-mode fallback
internal static readonly DiagnosticDescriptor UT0144_NoSumFallback = ...;
// UT0145: duplicate OptionId within a decision
internal static readonly DiagnosticDescriptor UT0145_DuplicateOptionId = ...;
```

Use `DiagnosticSeverity.Error` for UT0120/UT0121/UT0122/UT0130/UT0143; `DiagnosticSeverity.Warning` for UT0131/UT0144/UT0145.
Category: `"Fdp.UtilityAI"`, `isEnabledByDefault: true`.

---

## Task 2: `UtilityAuthoringAnalyzer`

**File:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityAuthoringAnalyzer.cs` (new)

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UtilityAuthoringAnalyzer : DiagnosticAnalyzer
```

### 2.1 Initialize

```csharp
public override void Initialize(AnalysisContext context)
{
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();
    context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
}
```

`AnalyzeNamedType` dispatches based on whether the class has `[UtilityDecision]` attribute.

### 2.2 UT0130 — Purity check (PRIORITY 1, directly copy EqsTemplatePurityAnalyzer pattern)

For each `[UtilityDecision]` class, find its `public static void Build(IUtilityDecisionBuilder)` method. Walk its body for reads of:
- Static non-const fields declared in the same class (same pattern as EQS_002)
- Types named `EntityRepository`, `ISimulationView`, `DateTime`, `Random` (simple name check)

If any found, emit UT0130 at the problematic node.

Follow `EqsTemplatePurityAnalyzer` verbatim for the static-field-read detection. For the type-name check:
```csharp
var disallowedTypes = new HashSet<string>
    { "EntityRepository", "ISimulationView", "DateTime", "Random" };
// Walk MemberAccessExpressionSyntax and IdentifierNameSyntax in the Build body.
// If the type of the resolved symbol is in disallowedTypes, emit UT0130.
```

### 2.3 UT0131 — Weight out of range (PRIORITY 2, simple)

Walk invocations of `Consider(...)` inside `Build` bodies. The second argument is `weight`. If it is a literal numeric constant and its value is outside `[0f, 1f]`, emit UT0131 at that argument location.

Note: only fire for literal constants (not variables) to avoid false positives. Use `SemanticModel.GetConstantValue(argExpression)` to check.

### 2.4 UT0120 — Unknown input name (PRIORITY 3)

Design reference: §6.2. The catalog spans referenced assemblies.

Build the input catalog once per compilation:
```csharp
// In Initialize, use RegisterCompilationStartAction:
context.RegisterCompilationStartAction(ctx =>
{
    var inputCatalog = BuildInputCatalog(ctx.Compilation);
    ctx.RegisterSymbolAction(sym => AnalyzeDecision(sym, inputCatalog), SymbolKind.NamedType);
});
```

`BuildInputCatalog(Compilation)`:
- Walk `compilation.GlobalNamespace` recursively
- Find all `IMethodSymbol` members with `[UtilityInput]` attribute
- Extract the `Name` from the attribute's first constructor argument
- Return `HashSet<string>` of valid names

For each `[UtilityDecision]` class's `Build` body:
- Find invocations of `Consider(...)` where the first argument is `In.<SomeName>(...)` 
- Resolve the member access — if `<SomeName>` is not in the catalog, emit UT0120

**Cross-assembly note:** `compilation.GlobalNamespace` includes symbols from all referenced assemblies via `GetNamespaceMembers()` recursion. This satisfies SC-P2-03-3.

### 2.5 UT0143/UT0144/UT0145 — Decision structure checks (PRIORITY 4)

These are best-effort syntactic counts over the `Build` body. Only fire when the body is a simple fluent chain (no loops/branches — if the body is complex, skip these checks to avoid false positives).

**UT0143:** After counting `Option`/`CandidateOption` calls (same walk as `AnalyzeBuildBody` in the generator): if count == 0 AND the decision's `Kind` is `PostureSelect`, emit UT0143.

**UT0144/UT0145:** These are lower-priority and can be skipped if implementation is complex. Mark as OPEN debt if not implemented.

---

## Task 3: Tests (`UtilityAuthoringAnalyzerTests.cs`)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityAuthoringAnalyzerTests.cs`

Use `CSharpAnalyzerDriver` pattern or `CSharpCompilation` + `GetDiagnostics`. See `EqsTemplatePurityAnalyzerTests.cs` for the exact pattern used in this project.

Write one test fixture per implemented diagnostic:

| Test | Success Condition |
|------|-------------------|
| `PureBuild_ProducesNoDiagnostics` | SC-P2-03-2a: clean Build body → 0 diagnostics |
| `ImpureBuild_StaticField_EmitsUT0130` | SC-P2-03-2b: Build reads static mutable field → UT0130 |
| `ImpureBuild_DateTime_EmitsUT0130` | SC-P2-03-2c: Build reads DateTime.Now → UT0130 |
| `WeightInRange_ProducesNoDiagnostics` | SC-P2-03-1/UT0131a: weight=0.5f → no UT0131 |
| `WeightOutOfRange_EmitsUT0131` | SC-P2-03-1/UT0131b: weight=1.5f → UT0131 |
| `UnknownInput_EmitsUT0120` | SC-P2-03-3: `In.NonExistentInput(...)` → UT0120 |
| `KnownInput_ProducesNoDiagnostics` | SC-P2-03-3: `In.AmmoFraction(...)` with catalog → no UT0120 |
| `PostureSelectZeroOptions_EmitsUT0143` | SC-P2-03-1/UT0143: `PostureSelect` with empty Build → UT0143 |

For UT0120 cross-assembly test (SC-P2-03-3): create a compilation with TWO projects — one defining a `[UtilityInput]` method, one consuming it in a `[UtilityDecision]`'s `Build`. Verify the analyzer recognizes the upstream input as valid and does NOT fire UT0120 for it.

---

## Task 4: Build verification

```
dotnet build FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj --no-incremental -v quiet
dotnet build FDP\Toolkits\Fdp.Toolkits.Analyzers\Fdp.Toolkits.Analyzers.csproj --no-incremental -v quiet
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Utility" --verbosity quiet
```

Expected: 0 errors, 130+ tests pass (123 pre-existing + at least 8 new analyzer tests).

---

## Key constraints

1. **Analyzer project targets netstandard2.0.** No nullable annotations, no net8+ APIs, no `#nullable enable`.
2. **`DiagnosticAnalyzer` pattern not generator pattern.** The analyzer has `[DiagnosticAnalyzer(LanguageNames.CSharp)]`, implements `DiagnosticAnalyzer`, overrides `Initialize` and `SupportedDiagnostics`. It produces no source output.
3. **UT0120 must resolve across referenced assemblies** via `compilation.GlobalNamespace` — do NOT limit to syntax trees of the current compilation only (that would be a false-positive bug on upstream inputs).
4. **UT0130 copies EqsTemplatePurityAnalyzer exactly** for the static-mutable-field detection. Do not invent a new pattern.
5. **Complex Build bodies** (loops, branches): skip UT0143/UT0144/UT0145 checks to avoid false positives. Mark the analysis as "too complex" and emit no diagnostic.
6. **Do NOT modify generators.** The generators and the analyzer are independent; both run during compilation. No coordination required.
7. Follow `AGENTS.md` editing invariants: preserve all existing comments, minimize diffs.

---

## Report

Submit to `.dev/utility-ai/reports/BATCH-10-REPORT.md` with:
1. Summary of what was implemented
2. All success conditions fulfilled (SC-P2-03-1, SC-P2-03-2, SC-P2-03-3)
3. Test results (counts)
4. Which UT#### diagnostics were implemented vs deferred (with justification)
5. Design decisions made beyond the spec
6. Suggested git commit message
