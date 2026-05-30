# BATCH-15: UtilityFluentEmitter + hot-reload hashes

**Batch Number:** BATCH-15
**Tasks:** TASK-UAI-P5-04 (UtilityFluentEmitter — lossless round-trip)
**Phase:** Phase 5 — Utility editor (card-table)
**Estimated Effort:** 10 hours
**Priority:** HIGH
**Dependencies:** BATCH-14 (asset model types — committed)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the deterministic C# emitter for `UtilityDecisionAsset`. The emitter is the
most critical piece of the Utility editor — it is the lossless round-trip guarantee. You must also
implement two hash-computation helpers for hot-reload classification (`StructureHash` and `ParamHash`).

**MANDATORY: Do not stop or ask for permission. Implement everything, run all tests, fix all errors,
then write the report. The batch is NOT done until all tests pass and the build is green.**

### Required Reading (IN ORDER)

1. `.dev/utility-ai/reviews/BATCH-14-REVIEW.md` — previous review
2. `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md` — read §8 (entire section) and §12 (test
   strategy) carefully
3. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs` — the closest precedent
4. `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/WeaponSelectionDecision.cs` — exact runtime format
5. `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/ThreatRankingDecision.cs` — exact runtime format
6. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` — full file; understand
   `IUtilityDecisionBuilder`, `Curve.*` presets, `In.*` factory methods, `Ctx.*` aliases
7. `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs` — base helpers
8. `Hrot/Editor/Hrot.Editor.AiShared/HotReload/HotReloadClassifier.cs` — classifier API
9. `Hrot/Editor/Hrot.Utility.Editor/Model/UtilityDecisionAsset.cs` — asset model (BATCH-14)
10. `Hrot/Editor/Hrot.Utility.Editor/Model/ConsiderationModel.cs` — note `InputName`, `Context`, `Params`
11. `Hrot/Editor/Hrot.Utility.Editor/Model/InputParamsModel.cs` — note fields

### Source Code Locations

- **Primary work area:** `Hrot/Editor/Hrot.Utility.Editor/`
- **Test project:** `Hrot/Editor/Hrot.Utility.Editor.Tests/`
- **Existing model files:** `Hrot/Editor/Hrot.Utility.Editor/Model/`

### Report Submission

`.dev/utility-ai/reports/BATCH-15-REPORT.md`

---

## Context

The emitter converts a `UtilityDecisionAsset` (in-memory editor model) into deterministic C# source
code that the runtime's source-generator and the comparison pipeline both consume. It is the only
allowed path to write `.cs` files from the editor. Round-trip correctness is the foundational
invariant: emit a known model → parse the output back → compare → must be identical.

The key challenge is that `ConsiderationModel.InputName` stores only the method name (e.g.
`"EqsTopScore"`, `"DistanceToContext"`) — but the call `In.EqsTopScore("CoverQuery")` needs the
original string `"CoverQuery"`. Because the current `InputParamsModel` only stores the FNV-1a hash
in `BlueprintId`, a lossless round-trip for EQS inputs is currently impossible.

**You must fix `InputParamsModel` first** by adding a `TemplateName` string field.

---

## Task 1: Extend `InputParamsModel` for lossless EQS round-trip

**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/InputParamsModel.cs` (MODIFY)

Add one field to `InputParamsModel`:

```csharp
// Template name string for EQS inputs (e.g., "CoverQuery").
// Stored alongside BlueprintId so the emitter can reconstruct In.EqsTopScore("CoverQuery").
// Empty for non-EQS inputs.
public string TemplateName = string.Empty;
```

No other changes to the file.

---

## Task 2: `UtilityFluentEmitter`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityFluentEmitter.cs` (NEW)

Implements `IFluentCSharpEmitter<UtilityDecisionAsset>` from `Hrot.Editor.AiShared.Emit`.

### 2.1 Output format

The emitter must produce output matching the exact runtime format seen in the starter-pack decisions.
Study `WeaponSelectionDecision.cs` and `ThreatRankingDecision.cs` carefully. The format is:

```
{Header}
namespace {Namespace};

[UtilityDecision(
    assetId:     "{AssetId:D}",
    displayName: "{DisplayName}",
    kind:        DecisionKind.{Kind},
    category:    "{Category}")]
public sealed partial class {ClassName} : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        {options...}
        ;
}
```

Where `{Header}` is `FluentCSharpEmitterBase.BuildHeader(asset.AssetId)`.

If `HysteresisBonus != 0f`, add it to the attribute (after `category`):
```
    hysteresisBonus: {value:R}f)]
```

If the asset has layout data (non-empty `OptionOrder` or `Collapsed` or non-empty `PinnedFixture`),
add a `[UtilityLayout]` static method after the `Build` method (see §8.3 for the shape):

```csharp
    [UtilityLayout]
    public static void Layout(IUtilityLayoutBuilder b)
    {
        // options listed in OptionOrder (or natural order if empty)
        // collapsed flags
        // pinned fixture
    }
```

For BATCH-15, the `[UtilityLayout]` method body can emit a placeholder comment
`// layout data — full wiring deferred to BATCH-16` when layout has non-default data, just to
ensure the method is emitted and the round-trip test for layout detection passes.

### 2.2 Class name derivation

`ClassName` = `SanitizeIdentifier(DisplayName) + "Decision"`. `SanitizeIdentifier` replaces
non-alphanumeric characters with `_` and ensures the name starts with a letter. Use the same
pattern as `BTreeFluentEmitter.SanitizeIdentifier`.

If `DisplayName` is empty, use `"UnnamedDecision"`.

### 2.3 Namespace

Default namespace: `"Fdp.Toolkit.Utility"`. Make this a configurable constructor parameter
with the above default:

```csharp
public sealed class UtilityFluentEmitter : IFluentCSharpEmitter<UtilityDecisionAsset>
{
    private readonly string _targetNamespace;

    public UtilityFluentEmitter(string targetNamespace = "Fdp.Toolkit.Utility")
    {
        _targetNamespace = targetNamespace;
    }
    // ...
}
```

### 2.4 Option emission

Options are emitted sorted by `VisualId` (design §8.1 — stable ordering for deterministic diffs).
Use `StringComparer.Ordinal`.

For each option:
- If the decision's `DecisionKind` is `ThreatRanking` or `WeaponSelection` (i.e. candidate options),
  use `.CandidateOption({Mode}, o => o`
- For `PostureSelect`, use `.Option({OptionId}, {Mode}, o => o`

`Mode` emits as `ScoringMode.WeightedProduct` or `ScoringMode.WeightedSum`.

### 2.5 Consideration emission

Considerations are emitted sorted by `VisualId` within each option.

Each consideration emits as:
```
            .Consider(In.{InputName}({args}), {weight:R}f, {curve})
```

**Args for `In.*` calls:**

The editor model stores `InputName` (the method name) and `Context`. The rule for args:
- If `Context != InputContext.Self`, append `InputContext.{Context}` as the last arg
- If `Params.TemplateName` is non-empty, append it as a string literal first: `In.{InputName}("{TemplateName}")` or `In.{InputName}("{TemplateName}", InputContext.{Context})`
- If `Params.MaxRange != 0f`, append it as a float literal: `In.{InputName}({MaxRange:R}f)` (for `Constant` and `DistanceToContext`)
- If `Params.MountIndex != 0`, append it as an int literal

When `Context == InputContext.Self` and no other params, emit `In.{InputName}()`.

**Curve emission:**

Check if the curve matches a `Curve.*` preset:
- `Curve.Linear` when `Kind=Linear, Slope=1, Exponent=1, XShift=0`
- `Curve.InverseLinear` when `Kind=InverseLinear, Slope=1, Exponent=1, XShift=0`
- `Curve.Threshold` when `Kind=Threshold, Slope=1, Exponent=1, XShift=0.5`
- `Curve.Bell` when `Kind=Bell, Slope=1, Exponent=8, XShift=1.0`
- `Curve.Step` when `Kind=Step, Slope=1, Exponent=1, XShift=0.5`
- `Curve.Logistic` when `Kind=Logistic, Slope=1, Exponent=1, XShift=0`
- `Curve.Quadratic` when `Kind=Quadratic, Slope=1, Exponent=1, XShift=0`
- `Curve.InverseQuadratic` when `Kind=InverseQuadratic, Slope=1, Exponent=1, XShift=0`
- If none match, emit `new ResponseCurve(CurveKind.{Kind}, slope: {Slope:R}f, exponent: {Exponent:R}f, xShift: {XShift:R}f)`
- `PiecewiseLinear` without matching a preset: `new ResponseCurve(CurveKind.PiecewiseLinear)`
  (piecewise side-table registration not in scope for this batch)

Note: The curve presets above come from `UtilityDecisionBuilderInfra.cs` Curve class:
- `Bell` has `slope=1, exponent=8, xShift=1.0` (check the exact values in the file)
- `Threshold` has `xShift=0.5`
- `Step` has `slope=1, xShift=0.5`

Verify these against the actual `Curve.*` preset definitions in `UtilityDecisionBuilderInfra.cs`.

### 2.6 Float precision

All float literals use `R` format (`value.ToString("R", CultureInfo.InvariantCulture)`) followed
by `f`. This ensures round-trip precision per design §8.4.

Example: `0.8000001f` → `"0.8000001f"` (not `"0.8f"` which would truncate).

### 2.7 Usings collection

Collect the minimal set of `using` directives needed:
- Always: `"Fdp.Toolkit.Utility"` (for `DecisionKind`, `ScoringMode`, `InputContext`, `Curve`,
  `In`, `ResponseCurve`, `IUtilityDecisionDefinition`, `IUtilityDecisionBuilder`)
- If any option is non-candidate (PostureSelect): potentially `"Fdp.Toolkit.Utility"` already covers
  the option ID enum

Use `FluentCSharpEmitterBase.SortUsings(set)` for output ordering.

### 2.8 Indentation

Use 4 spaces per level. File ends with a single newline (no trailing blank lines beyond the closing `}`).

---

## Task 3: `UtilityAssetHasher`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityAssetHasher.cs` (NEW)

Computes `StructureHash` and `ParamHash` from a `UtilityDecisionAsset` for use with
`HotReloadClassifier`. These are the editor-side hashes used to classify hot-reload tier (§8.5).

```csharp
using System;
using Hrot.Editor.AiShared.HotReload;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Emit;

// Computes structure and parameter hashes for hot-reload classification.
// StructureHash covers option/consideration topology (kind, option count, consideration count,
// input names, contexts). ParamHash covers tunable parameter values (weights, curve params).
public static class UtilityAssetHasher
{
    // Computes the hash of option/consideration structure only.
    // Changes that affect StructureHash trigger HotReloadTier.Hard.
    public static int ComputeStructureHash(UtilityDecisionAsset asset)
    {
        var hc = new HashCode();
        hc.Add(asset.DecisionKind);
        foreach (var opt in SortedOptions(asset))
        {
            hc.Add(opt.VisualId);
            hc.Add(opt.Mode);
            foreach (var con in SortedConsiderations(opt))
            {
                hc.Add(con.VisualId);
                hc.Add(con.InputName);
                hc.Add(con.Context);
                hc.Add(con.Curve.Kind);
            }
        }
        return hc.ToHashCode();
    }

    // Computes the hash of tunable parameter values only.
    // Changes that affect ParamHash (but not StructureHash) trigger HotReloadTier.Soft.
    public static int ComputeParamHash(UtilityDecisionAsset asset)
    {
        var hc = new HashCode();
        hc.Add(asset.HysteresisBonus);
        foreach (var opt in SortedOptions(asset))
        foreach (var con in SortedConsiderations(opt))
        {
            hc.Add(con.Weight);
            hc.Add(con.Curve.M);
            hc.Add(con.Curve.K);
            hc.Add(con.Curve.B);
            hc.Add(con.Curve.C);
        }
        return hc.ToHashCode();
    }

    // Classifies the hot-reload tier by comparing before/after hashes.
    public static HotReloadTier Classify(
        UtilityDecisionAsset before, UtilityDecisionAsset after)
    {
        return HotReloadClassifier.Classify(
            ComputeStructureHash(before), ComputeStructureHash(after),
            ComputeParamHash(before),     ComputeParamHash(after));
    }

    private static System.Collections.Generic.IEnumerable<OptionModel> SortedOptions(
        UtilityDecisionAsset asset)
        => System.Linq.Enumerable.OrderBy(asset.Options, o => o.VisualId, StringComparer.Ordinal);

    private static System.Collections.Generic.IEnumerable<ConsiderationModel> SortedConsiderations(
        OptionModel opt)
        => System.Linq.Enumerable.OrderBy(opt.Considerations, c => c.VisualId, StringComparer.Ordinal);
}
```

Note: `System.HashCode` is available in .NET 8.0 — no extra package needed.

---

## Task 4: Tests

**File:** `Hrot/Editor/Hrot.Utility.Editor.Tests/UtilityFluentEmitterTests.cs` (NEW)

Write at least 15 tests. Required coverage:

### Determinism tests (SC-P5-1 first half)
1. `Emit_SameModel_ByteIdentical_SecondEmit` — emit twice from the same asset, compare strings
2. `Emit_SortedByVisualId_WhenOptionsOutOfOrder` — create two options with VisualIds "z..." and "a...",
   assert "a..." option appears first in the output
3. `Emit_SortedByVisualId_ConsiderationsWithinOption` — same for considerations

### Header and attribute tests
4. `Emit_Contains_EditorGeneratedMarker`
5. `Emit_Contains_AssetId_InHeader`
6. `Emit_Contains_DisplayName_InAttribute`
7. `Emit_Contains_DecisionKind_InAttribute`
8. `Emit_Contains_Category_InAttribute`
9. `Emit_HysteresisBonus_NonZero_EmittedInAttribute`
10. `Emit_HysteresisBonus_Zero_NotEmitted`

### Build method tests
11. `Emit_CandidateOption_ForThreatRankingDecision` — `DecisionKind.ThreatRanking` → `.CandidateOption(`
12. `Emit_NamedOption_ForPostureSelectDecision` — `DecisionKind.PostureSelect` → `.Option(1,`
13. `Emit_Consideration_WithLinearCurvePreset` — curve preset name `Curve.Linear` in output
14. `Emit_Consideration_WithCustomCurve_EmitsNewResponseCurve` — non-preset curve → `new ResponseCurve(`
15. `Emit_Consideration_Weight_UsesRFormat` — weight `0.800000011920929f` emits as that exact R-format string (not truncated)

### Hot-reload classification tests (in a separate class or same file)
16. `Classify_LayoutChangeOnly_IsCosmetic` — change only `Layout.PinnedFixture` → `HotReloadTier.Cosmetic`
17. `Classify_WeightChange_IsSoft` — change a weight → `HotReloadTier.Soft`
18. `Classify_AddOption_IsHard` — add an option → `HotReloadTier.Hard`
19. `Classify_InputNameChange_IsHard` — change `InputName` → `HotReloadTier.Hard`

Helper method for tests — create a minimal `UtilityDecisionAsset` with one option and one consideration:
```csharp
private static UtilityDecisionAsset MakeAsset(DecisionKind kind = DecisionKind.PostureSelect)
{
    return new UtilityDecisionAsset
    {
        AssetId     = new Guid("3c6f9e42-5d10-6f3a-ac23-000000000001"),
        DisplayName = "Combat Posture",
        DecisionKind = kind,
        Category    = "Tactical/Posture",
        Options     = new System.Collections.Generic.List<OptionModel>
        {
            new OptionModel
            {
                OptionId = 1,
                Mode     = Fdp.Toolkit.Utility.ScoringMode.WeightedProduct,
                VisualId = "aaa",
                Considerations = new System.Collections.Generic.List<ConsiderationModel>
                {
                    new ConsiderationModel
                    {
                        InputName = "HealthFraction",
                        Context   = Fdp.Toolkit.Utility.InputContext.Self,
                        Weight    = 0.8f,
                        Curve     = new ResponseCurveModel { Kind = Fdp.Toolkit.Utility.CurveKind.InverseLinear, M = 1f, K = 1f, B = 0f },
                        VisualId  = "aab",
                    }
                }
            }
        }
    };
}
```

---

## Build & Test Requirements

1. `dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln -c Debug` — **0 errors required**
2. `dotnet test Hrot\Editor\Hrot.Utility.Editor.Tests\Hrot.Utility.Editor.Tests.csproj` — **all pass**
3. `dotnet test Hrot\Editor\Hrot.Editor.AiShared.Tests\Hrot.Editor.AiShared.Tests.csproj` — **no regressions**

---

## Success Criteria

- [ ] `InputParamsModel` extended with `TemplateName` string
- [ ] `UtilityFluentEmitter` implements `IFluentCSharpEmitter<UtilityDecisionAsset>`
- [ ] Emitter output is byte-identical on second call with same input
- [ ] Emitter output matches the runtime starter-pack format (attribute style, class name, partial)
- [ ] `UtilityAssetHasher` computes `StructureHash` and `ParamHash`; `Classify` uses `HotReloadClassifier`
- [ ] >= 19 tests, all passing
- [ ] Solution builds with 0 errors

---

## Common Pitfalls

1. **`Curve.*` preset values**: Before writing the preset matching logic, read the exact values of
   each preset in `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` (the
   `Curve` static class). `Bell` has `exponent: 8f, xShift: 1.0f`. `Threshold` and `Step` have
   `xShift: 0.5f`. Match `ResponseCurveModel` fields against these exactly or you'll emit
   `new ResponseCurve(...)` for presets.

2. **Float R format**: `0.8f` in C# source IS NOT the same as `(0.8f).ToString("R")` which may
   produce `"0.8"`. Use `CultureInfo.InvariantCulture` in all float formatting.

3. **`partial` keyword**: The emitted class is `public sealed partial class`, not `public sealed class`.
   This is required for the source generator to add the `Build` thunk. Check the starter-pack files.

4. **Attribute arg syntax**: The runtime examples use `:` syntax (`assetId:`, `displayName:`),
   not `=` syntax. This is named args with colon. Emit exactly `assetId:     "..."` with the
   right alignment padding (see starter-pack for the column alignment pattern).

5. **`HashCode` struct**: `System.HashCode` is a `struct`, not sealed. Each `Add` call returns
   `void`. Call `.ToHashCode()` at the end to get the `int`. Do NOT call `.GetHashCode()`.

6. **VisualId ordering**: Sort options AND considerations within each option independently.
   The sort must be stable and deterministic: use `StringComparer.Ordinal`, not default string
   comparison.

7. **Builder invocation chain**: The `Build` method emits as a fluent chain starting with `=> b`.
   Each `.Option(...)` or `.CandidateOption(...)` is one continuation. The chain ends with `;`
   on its own line (aligned with the `b`). See the exact format in `WeaponSelectionDecision.cs`.

---

## Reference Materials

- **Design:** `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md` §8 — full emitter spec
- **Runtime examples:** `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/`
- **BTree emitter precedent:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs`
- **Shared base:** `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs`
- **Hot-reload:** `Hrot/Editor/Hrot.Editor.AiShared/HotReload/HotReloadClassifier.cs`
