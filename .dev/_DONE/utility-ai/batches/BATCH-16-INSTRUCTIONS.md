# BATCH-16 Instructions — P5-02 (InputCatalogBrowser) + P5-05 (UtilityComparisonSanitizer + UtilityTuningDiffEngine)

**Batch ID:** BATCH-16
**Phase tasks:** TASK-UAI-P5-02 (partial), TASK-UAI-P5-05
**Design refs:** `Utility_AI_Editor_Design_v1_2.md` §6, §10

---

## Context

BATCH-15 landed `UtilityFluentEmitter`, `UtilityAssetHasher` (Cosmetic/Soft/Hard hot-reload tiers),
and extended `InputParamsModel` with `TemplateName`. The existing model in
`Hrot/Editor/Hrot.Utility.Editor/` now has a full emit path. This batch adds the input catalog
discovery layer (§6) and the comparison sanitizer + tuning-diff fast lane (§10).

All new files go in `Hrot/Editor/Hrot.Utility.Editor/` and its test project.

---

## MANDATORY READS before writing any code

Read ALL of these before writing any file:

1. `Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityFluentEmitter.cs` — understand the emitter that
   the sanitizer should output (for formatting reference)
2. `Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityAssetHasher.cs` — `ComputeStructureHash`,
   `ComputeParamHash`, `Classify` — all used by `UtilityTuningDiffEngine`
3. `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IAssetComparisonSanitizer.cs` — FULL file;
   understand `IAssetComparisonSanitizer`, `AssetExportRequest`, `SanitizationResult`,
   `AssetMetadataBlock`, `SanitizationWarning`
4. `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonSanitizer.cs` — FULL file;
   use as the structural template for the utility sanitizer
5. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeComparisonSanitizer.cs` — lines 1-60;
   understand how the BTree sanitizer strips `[BTreeLayout]` from the file
6. `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs` — lines
   1-60; understand the WriteTemp + RunOnText test helper pattern
7. `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs` — to find
   `EditorGeneratedMarker` constant value
8. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` — lines 70-130;
   the `In.*` partial class with `InputRef` return-type methods
9. `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/UtilityInputAttribute.cs` — attribute definition
10. `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` — lines 1-80;
    understand the `[UtilityInput("Name")]` annotated static methods pattern
11. `Hrot/Editor/Hrot.Utility.Editor/Model/InputParamsModel.cs` — current state
12. `Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj` — for project references
13. `Hrot/Editor/Hrot.Utility.Editor.Tests/Hrot.Utility.Editor.Tests.csproj` — for test references

---

## Task 1 — InputCatalogEntry + InputCatalogBrowser (P5-02 partial)

### 1a. Create `Hrot/Editor/Hrot.Utility.Editor/Catalog/InputCatalogEntry.cs`

```
namespace Hrot.Utility.Editor.Catalog;

/// <summary>
/// Metadata about a single Utility AI input accessor (a method on the In.* partial class).
/// Populated by InputCatalogBrowser from reflection over loaded assemblies.
/// </summary>
public sealed class InputCatalogEntry
{
    /// <summary>
    /// Accessor name as it appears in In.* calls (e.g., "HealthFraction", "EqsTopScore").
    /// Matches the value of [UtilityInput] when present.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Grouping label for the picker UI, inferred from [UtilityInput].Name or "Standard".
    /// </summary>
    public string Category { get; }

    /// <summary>Kind of parameter this input takes, if any.</summary>
    public InputParamKind ParameterKind { get; }

    public InputCatalogEntry(string name, string category, InputParamKind parameterKind)
    {
        Name          = name;
        Category      = category;
        ParameterKind = parameterKind;
    }
}

/// <summary>
/// Describes what additional parameter an In.* accessor requires.
/// </summary>
public enum InputParamKind
{
    /// <summary>No parameter (e.g., In.HealthFraction()).</summary>
    None,
    /// <summary>A string template name (e.g., In.EqsTopScore("CoverQuery")).</summary>
    String,
    /// <summary>A float value (e.g., In.Constant(0.5f)).</summary>
    Float,
    /// <summary>An int index (e.g., a mount index).</summary>
    Int,
}
```

### 1b. Create `Hrot/Editor/Hrot.Utility.Editor/Catalog/InputCatalogBrowser.cs`

Rules:
- Discovers `InputCatalogEntry` items from one or more `System.Reflection.Assembly` instances.
- Looks for types whose unqualified name is exactly `"In"` (the `In` partial class or any assembly
  where the source generator added accessors).
- Within such a type, collects all `public static` methods whose return type's name is `"InputRef"`.
- Infers `Name` = method name (e.g., `EqsTopScore`).
- Infers `Category` by checking whether the method has an `Attribute` whose type name is
  `"UtilityInputAttribute"` — if so, the category comes from its `Name` property. Otherwise the
  category defaults to `"Standard"`.
- Infers `ParameterKind` from the method's `MethodInfo.GetParameters()` result:
  - 0 non-context params → `None`
  - first non-context param is `System.String` → `String`
  - first non-context param is `System.Single` (float) → `Float`
  - first non-context param is `System.Int32` → `Int`
  - Ignore any parameter whose type name is `"InputContext"` — those are context, not data params.
- Deduplication: when the same `Name` appears in multiple assemblies, keep the first occurrence
  (earlier assembly in the input array wins).
- Returns an `IReadOnlyList<InputCatalogEntry>` sorted by `Name` with `StringComparer.Ordinal`.
- `Discover(params Assembly[] assemblies)` is a static method — no constructor needed.
- Must never throw. Catch reflection errors per-assembly and skip that assembly.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hrot.Utility.Editor.Catalog;

/// <summary>
/// Discovers Utility AI input accessors (In.* methods) from loaded assemblies by reflection.
/// Populates the input-picker catalog in the Utility Decision editor.
/// </summary>
public static class InputCatalogBrowser
{
    /// <summary>
    /// Reflects over <paramref name="assemblies"/> and returns one InputCatalogEntry per
    /// unique In.* method found.  Results are sorted by Name (Ordinal).
    /// </summary>
    public static IReadOnlyList<InputCatalogEntry> Discover(params Assembly[] assemblies)
    {
        // ... implementation per rules above
    }
}
```

---

## Task 2 — UtilityComparisonSanitizer (P5-05, first part)

Create `Hrot/Editor/Hrot.Utility.Editor/Comparison/UtilityComparisonSanitizer.cs`

### What the sanitizer must do

The sanitizer reads a utility decision `.cs` file (the emitter output from `UtilityFluentEmitter`)
and produces LLM-ready text by:

1. **Normalizing line endings** to `\n`.
2. **Stripping the `[UtilityLayout]` method block** (if present) — the block starts at the line
   containing `[UtilityLayout]` and ends at the matching closing `}`. Everything from that line to
   the end of the method body (inclusive) is removed. The class closing `}` is re-emitted after.
3. **Sanitizing the HROT_EDITOR_GENERATED header line**: strip the ` — managed by AI editor;
   manual edits...` suffix so only `// HROT_EDITOR_GENERATED` and the AssetId comment remain.
   Reason: the suffix is presentation noise that would differ between editor versions.
4. **No other rewriting** — preserve the `[UtilityDecision]` attribute, the class declaration,
   the `Build()` method, and all usings verbatim.

### Metadata extraction

- Parse the `// AssetId:` line to extract the `Guid`.
- Parse the class name from the line `public sealed partial class <ClassName>` for `AssetName`.
- `Kind => AssetKind.Utility`
- `LastModifiedTimestamp` from `File.GetLastWriteTimeUtc`, or `null` if file not accessible.

### Error handling

- Wrap the core pipeline in try/catch. On any exception, return:
  - `SanitizedText = string.Empty`
  - `Metadata = BuildFallbackMetadata(request)`
  - `Warnings` = single warning with exception message.
- If the file does not exist, return a file-not-found warning (no exception thrown).
- If `[UtilityLayout]` is not found, proceed without stripping (no warning — it is optional).

### Constructor

Takes no constructor arguments (unlike `BTreeComparisonSanitizer` which takes `IAssetCatalog` —
utility sanitizer does not need a catalog because `VisualId`s are already embedded in the file).

```csharp
public sealed class UtilityComparisonSanitizer : IAssetComparisonSanitizer
{
    public AssetKind TargetKind => AssetKind.Utility;
    public SanitizationResult Sanitize(AssetExportRequest request) { ... }
}
```

---

## Task 3 — UtilityTuningDiffEngine (P5-05, second part)

Create `Hrot/Editor/Hrot.Utility.Editor/Comparison/UtilityTuningDiffEngine.cs`

This class implements the "tuning-diff fast lane" (design §10.2): given two versions of a utility
decision asset, it decides whether the change is structure-only-equal (tuning diff, no LLM) or
structural (needs LLM), and lists the param-level differences.

### Types to define in this file

```csharp
/// <summary>
/// A single parameter-level change between two versions of a consideration.
/// </summary>
public sealed class TuningParamDiff
{
    public string OptionVisualId   { get; }
    public string ConsiderationVisualId { get; }
    public string ConsiderationName { get; }   // the InputName, for display
    public string ParamLabel        { get; }   // "Weight", "Slope", "Exponent", "XShift"
    public float  OldValue          { get; }
    public float  NewValue          { get; }
    public TuningParamDiff(string optVid, string conVid, string conName,
                            string paramLabel, float old, float @new)
    { ... }
}

/// <summary>
/// Result of a fast-lane tuning diff comparison.
/// </summary>
public sealed class TuningDiffResult
{
    /// <summary>True when both versions have the same StructureHash (option/consideration topology).</summary>
    public bool IsStructureEqual  { get; }
    /// <summary>True when both structure AND params are identical (no change at all).</summary>
    public bool IsIdentical       { get; }
    /// <summary>Ordered list of per-consideration param changes.  Empty when IsIdentical.</summary>
    public IReadOnlyList<TuningParamDiff> Diffs { get; }
    public TuningDiffResult(bool structureEqual, bool identical, IReadOnlyList<TuningParamDiff> diffs)
    { ... }
}

/// <summary>
/// Performs a parameter-level diff between two UtilityDecisionAsset versions using
/// UtilityAssetHasher for fast structural equality check and per-field diff for param changes.
/// Design ref: Utility_AI_Editor_Design_v1_2.md §10.2
/// </summary>
public static class UtilityTuningDiffEngine
{
    public static TuningDiffResult Compute(UtilityDecisionAsset versionA, UtilityDecisionAsset versionB)
    { ... }
}
```

### Algorithm for `Compute`

1. Compute `structA = UtilityAssetHasher.ComputeStructureHash(versionA)` and
   `structB = UtilityAssetHasher.ComputeStructureHash(versionB)`.
2. If `structA != structB` → `new TuningDiffResult(isStructureEqual: false, isIdentical: false, [])`
   (structural change; fast lane not applicable).
3. Compute `paramA = UtilityAssetHasher.ComputeParamHash(versionA)` and
   `paramB = UtilityAssetHasher.ComputeParamHash(versionB)`.
4. If `paramA == paramB` → `new TuningDiffResult(isStructureEqual: true, isIdentical: true, [])`.
5. Otherwise: walk matching options by `VisualId` (same structure → same VisualIds), then matching
   considerations by `VisualId`. For each consideration pair, compare:
   - `Weight` → label `"Weight"`
   - `Curve.M` → label `"Slope"`
   - `Curve.K` → label `"Exponent"`
   - `Curve.B` → label `"XShift"`
   - `Curve.Kind` changes are covered by `StructureHash` (no — actually CurveKind change means param
     hash differs but struct hash is same since struct hash only covers topology). Include a
     `"CurveKind"` diff with `OldValue = (float)oldKind`, `NewValue = (float)newKind` when they
     differ.
   - Only emit a diff entry when old value != new value.
6. Return `new TuningDiffResult(isStructureEqual: true, isIdentical: false, diffs)`.

**IMPORTANT**: When building the diffs, walk options sorted by `VisualId` (StringComparer.Ordinal)
and within each option walk considerations sorted by `VisualId` (same order as the emitter). This
ensures the diff list is deterministic.

---

## Task 4 — Tests

### 4a. `InputCatalogBrowserTests.cs`

Add to `Hrot/Editor/Hrot.Utility.Editor.Tests/Catalog/InputCatalogBrowserTests.cs`

**Tests to write (at minimum 8):**

1. `Discover_EmptyAssemblyList_ReturnsEmpty`
   - `Discover()` with no args → empty list.
2. `Discover_AssemblyWithNoInClass_ReturnsEmpty`
   - Reflect over the test assembly itself (which has no `In` class) → empty.
3. `Discover_MethodsFromInClass_NamedCorrectly`
   - Load `typeof(Fdp.Toolkit.Utility.In).Assembly`; verify `HealthFraction` appears in result.
4. `Discover_EqsTopScore_HasStringParam`
   - `EqsTopScore` entry has `ParameterKind == InputParamKind.String`.
5. `Discover_Constant_HasFloatParam`
   - `Constant` entry has `ParameterKind == InputParamKind.Float`.
6. `Discover_ParameterlessInput_HasNoneParam`
   - `HealthFraction` or `HaveLiveTarget` entry has `ParameterKind == InputParamKind.None`.
7. `Discover_SortedByName`
   - Result is sorted by Name ascending (Ordinal).
8. `Discover_DuplicateAcrossAssemblies_FirstWins`
   - Pass the same assembly twice → no duplicates in result.

### 4b. `UtilityComparisonSanitizerTests.cs`

Add to `Hrot/Editor/Hrot.Utility.Editor.Tests/Comparison/UtilityComparisonSanitizerTests.cs`

Use the WriteTemp/RunOnText helper pattern from `BTreeComparisonSanitizerTests`:
- Write a temp `.cs` file with `File.WriteAllText`.
- Create `AssetExportRequest(path, null, AssetKind.Utility)`.
- Call `new UtilityComparisonSanitizer().Sanitize(request)`.
- Delete temp file in finally block.

**Tests to write (at minimum 8):**

1. `Sanitize_FileNotFound_ReturnsEmptyTextWithWarning`
2. `Sanitize_StripsSuffix_FromGeneratedMarkerLine`
   - Input header: `// HROT_EDITOR_GENERATED — managed by AI editor; manual edits...`
   - Output header: `// HROT_EDITOR_GENERATED`
3. `Sanitize_StripsLayoutBlock_WhenPresent`
   - File contains a `[UtilityLayout]` method; output must NOT contain `[UtilityLayout]`.
4. `Sanitize_PreservesDecisionAttribute`
   - Output contains `[UtilityDecision(`.
5. `Sanitize_PreservesBuildMethod`
   - Output contains `public static void Build`.
6. `Sanitize_ExtractsAssetId_FromHeader`
   - `result.Metadata.AssetId` equals the Guid in the `// AssetId:` line.
7. `Sanitize_ExtractsAssetName_FromClassDeclaration`
   - `result.Metadata.AssetName` equals the class name.
8. `Sanitize_Deterministic_SameInputTwice`
   - Sanitize same file twice → byte-identical `SanitizedText`.
9. `Sanitize_NoLayoutBlock_NoWarning`
   - File without `[UtilityLayout]` → `result.Warnings` is empty.

### 4c. `UtilityTuningDiffEngineTests.cs`

Add to `Hrot/Editor/Hrot.Utility.Editor.Tests/Comparison/UtilityTuningDiffEngineTests.cs`

Use the `MakeAsset()` helper from `UtilityFluentEmitterTests.cs` as a template for building
`UtilityDecisionAsset` instances inline.

**Tests to write (at minimum 6):**

1. `Compute_IdenticalAssets_IsIdenticalTrue`
2. `Compute_StructureDiffer_AddOption_IsStructureEqualFalse`
3. `Compute_WeightChange_IsStructureEqualTrue_OneWeightDiff`
   - Change one consideration's Weight from 0.8f to 0.5f → 1 diff with label "Weight".
4. `Compute_CurveParamChange_SlopeAndExponent_TwoDiffs`
   - Change slope and exponent on one consideration → 2 diffs.
5. `Compute_CurveKindChange_OneDiff_LabelCurveKind`
   - Change `CurveKind.Linear` to `CurveKind.Logistic` → diff with label "CurveKind".
6. `Compute_DiffsOrderedByVisualId`
   - Asset with two options (VisualId "aaa" and "zzz"); change weight in "zzz" option;
     the diff entry has `OptionVisualId == "zzz"`.

---

## Build and test requirements

After implementing all tasks:

1. `dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln -c Debug` → **0 errors**
2. `dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Editor\Hrot.Utility.Editor.Tests\Hrot.Utility.Editor.Tests.csproj`
   → all tests pass; at least **122 tests** total (100 existing + at least 22 new)
3. No regressions in `Hrot\Editor\Hrot.Editor.AiShared.Tests`

---

## Report

Write `.dev/utility-ai/reports/BATCH-16-REPORT.md` with:
- Status: APPROVED or CHANGES REQUIRED
- Files created/modified
- Exact test counts from test run output
- Design decisions
- Build result
