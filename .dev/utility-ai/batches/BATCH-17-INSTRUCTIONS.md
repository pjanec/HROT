# BATCH-17 Instructions — P5-02 (asset loader + SC-P5-4) + P5-03 (live preview runner + SC-P5-2)

**Batch ID:** BATCH-17
**Phase tasks:** TASK-UAI-P5-02 (remainder), TASK-UAI-P5-03
**Design refs:** `Utility_AI_Editor_Design_v1_2.md` §5, §6, §7

---

## Context

BATCH-16 completed the `InputCatalogBrowser` (reflection-based discovery of `In.*` methods) and
the `UtilityComparisonSanitizer` + `UtilityTuningDiffEngine`. Remaining Phase-5 work is:

- **P5-02 SC-P5-4**: Asset loader that detects partial-manifest files (no HROT_EDITOR_GENERATED
  marker) and flags them read-only with `IsEditorOwned = false`.
- **P5-03 SC-P5-2**: Live preview runner: converts a `UtilityDecisionAsset` model to a runtime
  `UtilityDecisionDef`, calls `UtilityScorer.Evaluate`, reads back per-consideration scores.

All new files go in `Hrot/Editor/Hrot.Utility.Editor/` (main project) and the test project.

---

## MANDATORY READS before writing any code

Read ALL of these:

1. `.dev/utility-ai/batches/BATCH-17-INSTRUCTIONS.md` — this file (obviously)
2. `Hrot/Editor/Hrot.Utility.Editor/Model/UtilityDecisionAsset.cs` — FULL file
3. `Hrot/Editor/Hrot.Utility.Editor/Model/ConsiderationModel.cs`
4. `Hrot/Editor/Hrot.Utility.Editor/Model/OptionModel.cs`
5. `Hrot/Editor/Hrot.Utility.Editor/Model/ResponseCurveModel.cs` — note `ToRuntime()` method
6. `Hrot/Editor/Hrot.Utility.Editor/Model/InputParamsModel.cs`
7. `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs` — to get exact
   `EditorGeneratedMarker` and `AssetIdCommentPrefix` values
8. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` — `UtilityDecisionDef`,
   `UtilityOption`, `UtilityConsideration`, `InputParams` structures (read lines 100-170)
9. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` — read the static `Evaluate`
   method signature (read lines 100-180)
10. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityTraceWorkingMemory1024.cs` — FULL file;
    understand `ReadRecord`, `RecordCount`, `UtilityTraceOpCode.Consideration`
11. `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` — lines 20-55;
    understand `StandardInputIds` constants and FNV-1a-16 derivation
12. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` — lines 105-125;
    `In.Fnv1a32()` method
13. `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs` — read lines 1-100 to
    understand how `UtilityInputReaderStore.Register` + static `Evaluate` is used in tests
14. `Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityFluentEmitter.cs` — to understand how the
    emitter writes `[UtilityDecision(assetId: "...", displayName: "...", kind: ..., category: "...")]`
15. `Hrot/Editor/Hrot.Utility.Editor.Tests/Hrot.Utility.Editor.Tests.csproj`

---

## Task 1 — UtilityAssetLoader (P5-02 SC-P5-4)

### 1a. Modify `Hrot/Editor/Hrot.Utility.Editor/Model/UtilityDecisionAsset.cs`

Add the following field after the existing fields:

```csharp
/// <summary>
/// True when this asset was emitted by the editor (HROT_EDITOR_GENERATED marker present).
/// False for partial-manifest files (hand-authored Build() using loops or helpers).
/// The emit path is blocked for non-owned assets.
/// </summary>
public bool IsEditorOwned = true;
```

### 1b. Create `Hrot/Editor/Hrot.Utility.Editor/Loading/UtilityAssetLoader.cs`

This class reads a `.cs` file and produces a `UtilityDecisionAsset` from its content.

**Only text-based extraction; no Roslyn, no assembly loading.**

#### Static method: `Load(string filePath)`

Returns a `(UtilityDecisionAsset asset, string[] warnings)` tuple.

Algorithm:
1. If file does not exist → return a default `UtilityDecisionAsset { IsEditorOwned = false }` with
   a single warning `"File not found: {filePath}"`.
2. Read file text. Normalize line endings to `\n`. Split into lines.
3. Check for `HROT_EDITOR_GENERATED` marker in the first 5 lines:
   - If absent → `asset.IsEditorOwned = false`. Extract what metadata you can (see below) but
     do not attempt to populate Options (they are unavailable without the emitter structure).
     Add warning: `"File is not editor-generated; opened read-only."`.
4. Parse the `[UtilityDecision(` attribute block (lines containing `assetId:`, `displayName:`,
   `kind:`, `category:`, `hysteresisBonus:`) to extract:
   - `AssetId` (Guid) from `assetId:     "<guid-D-format>"`
   - `DisplayName` from `displayName: "<name>"`
   - `DecisionKind` from `kind:        DecisionKind.<EnumValue>`
   - `Category` from `category:    "<category>"`
   - `HysteresisBonus` from `hysteresisBonus: <float>f` (optional; default 0f)
5. For editor-owned files, return an asset with the extracted metadata and an empty `Options` list.
   (Full options/considerations parsing is deferred to a later batch — the loader is responsible
   only for opening the asset to a usable state with correct metadata and the correct `IsEditorOwned`.)

**Extraction helpers:**

- `ParseGuid(string line)`: find a `"<...>"` substring in the line and `Guid.TryParse` it.
- `ParseString(string line)`: find a `"<...>"` substring and return the string value.
- `ParseDecisionKind(string line)`: find `DecisionKind.<Name>` and `Enum.TryParse<DecisionKind>`.
- `ParseFloat(string line, string label)`: find `<label>: <number>f` and `float.TryParse`.

All parsing is permissive — if a field is not found, leave at default. Never throw.

**Return type:** Use a simple value tuple or create a small `UtilityLoadResult` record in the same
file:

```csharp
public sealed record UtilityLoadResult(
    UtilityDecisionAsset Asset,
    IReadOnlyList<string> Warnings);
```

---

## Task 2 — UtilityPreviewRunner (P5-03 SC-P5-2)

### 2a. Create `Hrot/Editor/Hrot.Utility.Editor/Preview/UtilityPreviewConsiderationScore.cs`

```csharp
namespace Hrot.Utility.Editor.Preview;

/// <summary>
/// Per-consideration score breakdown from a single preview evaluation pass.
/// Data extracted from UtilityTraceWorkingMemory1024.
/// </summary>
public sealed class UtilityPreviewConsiderationScore
{
    /// <summary>Zero-based option index in the sorted option list.</summary>
    public int    OptionIndex     { get; }
    /// <summary>FNV-1a-16 of the input reader name.</summary>
    public ushort InputId         { get; }
    /// <summary>Raw value returned by the input reader.</summary>
    public float  RawValue        { get; }
    /// <summary>Curve output in [0,1].</summary>
    public float  CurveOutput     { get; }
    /// <summary>Consideration weight.</summary>
    public float  Weight          { get; }
    /// <summary>Running aggregate score after this consideration was applied.</summary>
    public float  RunningAggregate { get; }

    public UtilityPreviewConsiderationScore(
        int optionIndex, ushort inputId, float raw, float curveOut,
        float weight, float runningAggregate)
    {
        OptionIndex      = optionIndex;
        InputId          = inputId;
        RawValue         = raw;
        CurveOutput      = curveOut;
        Weight           = weight;
        RunningAggregate = runningAggregate;
    }
}
```

### 2b. Create `Hrot/Editor/Hrot.Utility.Editor/Preview/UtilityPreviewResult.cs`

```csharp
namespace Hrot.Utility.Editor.Preview;

/// <summary>
/// Full result from a single preview evaluation pass.
/// Contains per-consideration scores and the top-ranked option score.
/// </summary>
public sealed class UtilityPreviewResult
{
    /// <summary>Per-consideration scores, in evaluation order (as recorded by the tracer).</summary>
    public IReadOnlyList<UtilityPreviewConsiderationScore> ConsiderationScores { get; }
    /// <summary>Score of the top-ranked option in the result buffer.</summary>
    public float TopScore { get; }
    /// <summary>Number of options in the result buffer.</summary>
    public int   OptionCount { get; }

    public UtilityPreviewResult(
        IReadOnlyList<UtilityPreviewConsiderationScore> scores,
        float topScore,
        int optionCount)
    {
        ConsiderationScores = scores;
        TopScore            = topScore;
        OptionCount         = optionCount;
    }
}
```

### 2c. Create `Hrot/Editor/Hrot.Utility.Editor/Preview/UtilityPreviewRunner.cs`

This is the core of P5-03. It converts a `UtilityDecisionAsset` to a `UtilityDecisionDef` and
evaluates it using the actual `UtilityScorer`, then reads back the trace for per-consideration data.

#### Static method: `Evaluate(UtilityDecisionAsset asset, EntityRepository? repo = null, Entity self = default, Entity context = default)`

Returns `UtilityPreviewResult`.

**Important design rules:**
- Call the real `UtilityScorer.Evaluate` (static overload). This is non-negotiable (SC-P5-2):
  a separate scoring path would drift from runtime. The runner's output must be byte-identical
  to a direct `UtilityScorer.Evaluate` call on the same def.
- Use `UtilityTraceWorkingMemory1024` to capture per-consideration scores. The trace is allocated
  on the stack (`stackalloc` / `default`) and passed as an unsafe pointer.
- The `repo` and `self`/`context` parameters can be `null`/`default` for tests (readers that do not
  need the repo will still work correctly).

**Algorithm:**

1. **Build `UtilityDecisionDef`** from the asset:
   - `BlueprintId = 0` (not used for preview)
   - `DebugName = asset.DisplayName`
   - `Kind = asset.DecisionKind`
   - For each `OptionModel` in `asset.Options` (in original list order — NOT sorted by VisualId;
     we want the option index to match the trace's OptionIndex):
     - `UtilityOption.OptionId = opt.OptionId`
     - `UtilityOption.Mode = opt.Mode`
     - For each `ConsiderationModel` in `opt.Considerations` (original order):
       - `InputId = ComputeInputId(con.InputName)` — see below
       - `Context = con.Context`
       - `Weight = con.Weight`
       - `Curve = con.Curve.ToRuntime()` (already on `ResponseCurveModel`)
       - `Params = new InputParams { BlueprintId = con.Params.BlueprintId, MaxRange = con.Params.MaxRange, MountIndex = con.Params.MountIndex }`

2. **Compute InputId** from the InputName: `(ushort)(In.Fnv1a32(inputName) & 0xFFFF)`
   The `In.Fnv1a32` method is `public static uint Fnv1a32(string name)` in
   `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs`. Import
   `using Fdp.Toolkit.Utility;` to access it.

3. **Allocate trace buffer** on the stack (unsafe context required):
   ```csharp
   var traceMem = default(UtilityTraceWorkingMemory1024);
   UtilityTraceWorkingMemory1024* tracePtr = &traceMem;
   ```

4. **Call scorer**:
   ```csharp
   var resultBuffer = default(UtilityResultBuffer);
   UtilityScorer.Evaluate(repo, self, in def, context, ref resultBuffer, tracePtr);
   ```

5. **Read trace**: iterate `traceMem.RecordCount` records via `traceMem.ReadRecord(i, out var rec)`.
   For each record with `rec.OpCode == UtilityTraceOpCode.Consideration`, build a
   `UtilityPreviewConsiderationScore`.

6. **Read top score** from `resultBuffer.GetSpanRO()[0].Score` when `resultBuffer.Count > 0`.

7. Return `new UtilityPreviewResult(scores, topScore, resultBuffer.Count)`.

**The method must be `unsafe`** due to the `stackalloc`/pointer usage.

**Namespace imports needed:**
- `using Fdp.Core;` (for `Entity`, `EntityRepository`)
- `using Fdp.Toolkit.Utility;` (for `UtilityScorer`, `UtilityDecisionDef`, `UtilityOption`,
  `UtilityConsideration`, `InputParams`, `UtilityResultBuffer`, `UtilityTraceWorkingMemory1024`,
  `UtilityTraceOpCode`, `In`)
- `using Hrot.Utility.Editor.Model;`

---

## Task 3 — Tests

### 3a. `UtilityAssetLoaderTests.cs`

Create `Hrot/Editor/Hrot.Utility.Editor.Tests/Loading/UtilityAssetLoaderTests.cs`.

**Tests (minimum 8):**

1. `Load_FileNotFound_ReturnsReadOnlyWithWarning`
2. `Load_FileWithGeneratedMarker_IsEditorOwnedTrue`
3. `Load_FileWithoutGeneratedMarker_IsEditorOwnedFalse`
4. `Load_ExtractsAssetId_FromAttribute`
5. `Load_ExtractsDisplayName_FromAttribute`
6. `Load_ExtractsDecisionKind_FromAttribute`
7. `Load_ExtractsCategory_FromAttribute`
8. `Load_ExtractsHysteresisBonus_WhenPresent`

For tests 2-8, write a temp file using `File.WriteAllText`. Use the exact attribute format that
`UtilityFluentEmitter` emits (the `:` syntax with column alignment):

```csharp
private static string MakeSampleFile(bool addMarker = true,
    string assetId = "3c6f9e42-5d10-6f3a-ac23-000000000001",
    string displayName = "Combat Posture",
    string kind = "PostureSelect",
    string category = "Tactical/Posture",
    float hysteresisBonus = 0f)
{
    var sb = new System.Text.StringBuilder();
    if (addMarker)
    {
        sb.Append("// HROT_EDITOR_GENERATED\n");
        sb.Append($"// AssetId: {assetId}\n\n");
    }
    sb.Append("[UtilityDecision(\n");
    sb.Append($"    assetId:     \"{assetId}\",\n");
    sb.Append($"    displayName: \"{displayName}\",\n");
    sb.Append($"    kind:        DecisionKind.{kind},\n");
    if (hysteresisBonus != 0f)
        sb.Append($"    hysteresisBonus: {hysteresisBonus.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}f,\n");
    sb.Append($"    category:    \"{category}\")]\n");
    sb.Append($"public sealed partial class CombatPostureDecision : IUtilityDecisionDefinition\n");
    sb.Append("{\n    public static void Build(IUtilityDecisionBuilder b) => b;\n}\n");
    return sb.ToString();
}
```

### 3b. `UtilityPreviewRunnerTests.cs`

Create `Hrot/Editor/Hrot.Utility.Editor.Tests/Preview/UtilityPreviewRunnerTests.cs`.

**Tests (minimum 6):**

The tests need to register stub input readers via `UtilityInputReaderStore.Register`. Because
`UtilityInputReaderStore` is a global static store, tests must use unique InputIds (not conflicting
with other test classes). Use the range 200-220 for BATCH-17 tests.

Use IDisposable + `UtilityInputReaderStore.Clear()` in Dispose for teardown.

**Test 1: `Evaluate_SingleConsideration_TopScoreMatchesDirectScorerCall` (SC-P5-2)**

```csharp
[Fact]
public unsafe void Evaluate_SingleConsideration_TopScoreMatchesDirectScorerCall()
{
    // Register a stub reader that returns 0.6f.
    UtilityInputReaderStore.Register(200, &Stub06);

    // Build an asset with one option and one consideration referencing input id 200.
    // The input name "B17TestInput200" must hash to 200 via FNV-1a-16.
    // BUT: We cannot guarantee arbitrary name hashes match our chosen id.
    // Instead, register the id 200 as a stub, then build the asset with
    // InputName = the string whose FNV-1a-16 hash IS 200. This is hard.
    //
    // BETTER APPROACH: 
    // Use a name we know the hash for. Check: (ushort)(In.Fnv1a32("Constant") & 0xFFFF).
    // StandardInputIds.Constant = 0xAB45 = 43845.
    // Use InputId 200 as a stub but build the asset with the name that maps to 200.
    //
    // SIMPLEST APPROACH (used here):
    // Register reader for the actual StandardInputIds.Constant (0xAB45).
    // Build an asset with InputName = "Constant" (its FNV-1a-16 is 0xAB45).
    // This avoids the hash-collision problem entirely.
    //
    // ... test body
}
```

**ACTUAL APPROACH for SC-P5-2** (do not use register+200 workaround; use real names):

Register the stub for `StandardInputIds.Constant` (or any known ID). Build the asset using
`InputName = "Constant"`. Then:
1. Call `UtilityPreviewRunner.Evaluate(asset)` → get `runnerResult.TopScore`
2. Construct an identical `UtilityDecisionDef` manually
3. Call `UtilityScorer.Evaluate(null, default, in def, default, ref directBuffer, null)` directly
4. Assert `runnerResult.TopScore == directBuffer.GetSpanRO()[0].Score`

This is the definitive SC-P5-2 test.

**Test 2: `Evaluate_SingleConsideration_ConsiderationScoreIsRecorded`**
- Verify `result.ConsiderationScores.Count == 1` and `InputId == StandardInputIds.Constant`.

**Test 3: `Evaluate_MultipleConsiderations_AllRecorded`**
- Asset with 2 considerations → `result.ConsiderationScores.Count == 2`.

**Test 4: `Evaluate_EmptyOptions_TopScoreZero`**
- Asset with no options → `result.TopScore == 0f` and `result.OptionCount == 0`.

**Test 5: `Evaluate_CurveApplied_CurveOutputMatchesExpected`**
- Use `Curve.Step` (xShift=0.5f). Stub returns 0.3f (below threshold). Expect `CurveOutput == 0f`.

**Test 6: `Evaluate_NullRepo_DoesNotThrow`**
- `UtilityPreviewRunner.Evaluate(asset, null)` does not throw and returns a result.

**Stub readers** (declare as private static unsafe methods in the test class):
```csharp
private static float Stub06(in UtilityInputCtx ctx) => 0.6f;
private static float StubZero(in UtilityInputCtx ctx) => 0.0f;
private static float StubAboveThreshold(in UtilityInputCtx ctx) => 0.7f;
private static float StubBelowThreshold(in UtilityInputCtx ctx) => 0.3f;
```

The test class must be `unsafe` and use `IDisposable` with `UtilityInputReaderStore.Clear()` in Dispose.

---

## Build and test requirements

1. `dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln -c Debug` → **0 errors**
2. `dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Editor\Hrot.Utility.Editor.Tests\Hrot.Utility.Editor.Tests.csproj`
   → all tests pass; at least **137 tests** total (123 existing + at least 14 new)
3. No regressions.

---

## Report

Write `.dev/utility-ai/reports/BATCH-17-REPORT.md` with status, files, test counts, build result.
