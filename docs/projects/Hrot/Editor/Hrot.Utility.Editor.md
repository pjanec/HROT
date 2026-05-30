# Hrot.Utility.Editor -- Utility AI Visual Editor

**Project path:** `Hrot/Editor/Hrot.Utility.Editor/`
**Project file:** `Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj`
**Test project:** `Hrot/Editor/Hrot.Utility.Editor.Tests/`
**Primary namespace:** `Hrot.Utility.Editor`
**Design references:**
- `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md`
- `.dev/utility-ai/Curve_Editor_in_StructEdit_Guide_v1_1.md`
**Date:** 2026-05-30

---

## Executive Overview

`Hrot.Utility.Editor` is the ImGui-based visual editor for Utility AI decisions. It hosts the
full authoring lifecycle for `UtilityDecisionAsset` objects: load from disk, edit via a
card-table UI, preview scores in real time, emit deterministic C# files, and integrate with
the live tuning console.

The editor is a pure editor-layer library: it has no dependency on any AI runtime assembly
other than the shared `Fdp.Toolkit.Utility` toolkit (where `UtilityDecisionDef` and
`UtilityScorer` live). All runtime scoring is delegated to `UtilityPreviewRunner`, which calls
the real `UtilityScorer.Evaluate` path.

### Sub-systems

| Sub-system | Directory | Purpose |
|---|---|---|
| **Asset model** | `Model/` | In-memory mutable representation of one decision |
| **Curve widget** | `Curve/` | Host-agnostic ImGui curve editor (shared with tuning console) |
| **Code emitter** | `Emit/` | Deterministic C# `.cs` file generator |
| **Asset loader** | `Loading/` | Text-based `.cs` -> asset round-trip reader |
| **Live preview** | `Preview/` | Calls the real runtime scorer; returns per-consideration scores |
| **Comparison** | `Comparison/` | Param-level diff between two versions; sanitizer for LLM export |
| **StructEdit field** | `FieldEdit/` | `IImGuiFieldDrawer` wrapper so StructEdit renders `UtilityCurve` |
| **Windows** | `Windows/` | `ManagedWindow` host for the card-table editor |
| **Tracing** | `Tracing/` | Trace lane descriptor for the shared debug timeline |

---

## Architecture

```
+---------------------------------------------------+
|           Hrot.Utility.Editor                     |
|                                                   |
|  UtilityDecisionWindow (ManagedWindow host)       |
|    +-- UtilityDecisionAsset  (model)              |
|    +-- UtilityFluentEmitter  (emit .cs file)      |
|    +-- UtilityAssetLoader    (load .cs file)      |
|    +-- UtilityPreviewRunner  (live preview)       |
|    +-- CurveWidget.Draw      (in Preview panel)   |
|                                                   |
|  FieldEdit/                                       |
|    +-- UtilityCurveFieldDrawer  (StructEdit hook) |
|    +-- UtilityCurveFieldEditor  (tuning hook)     |
+---------------------------------------------------+
         |                 |
         |                 | (uses real runtime scorer)
         v                 v
  Fdp.Toolkit.Utility   Hrot.Editor.AiShared
  (UtilityScorer,       (EditorSelectionStore,
   UtilityDecisionDef,   ManagedWindow, IEditableAsset,
   UtilityResultBuffer)  IFluentCSharpEmitter, ...)
```

---

## Asset Model (`Model/`)

### UtilityDecisionAsset

In-memory mutable editor representation of one utility decision. Mirrors the runtime
`UtilityDecisionDef` but carries `VisualId` strings for deterministic output ordering.

```
UtilityDecisionAsset : IEditableAsset
  AssetId          Guid        -- stable identifier; drives FNV-1a-32 BlueprintId
  DisplayName      string      -- human name, used as class name in emitted C#
  DecisionKind     DecisionKind
  Category         string      -- dot-path category ("Tactical/Posture")
  HysteresisBonus  float       -- applied in SelectPosture; 0f for ThreatRanking/WeaponSelection
  Options          List<OptionModel>
  Fixtures         List<FixtureRef>   -- named test fixtures for live preview
  Layout           UtilityLayoutData  -- card positions, pinned fixture
  IsDirty          bool         -- set true when any field changes; fires Changed event
  IsEditorOwned    bool         -- false when loaded from a hand-authored file (read-only mode)
```

`IEditableAsset` properties:
- `Name` - returns `DisplayName`
- `Kind` - returns `AssetKind.Utility`
- `Changed` event - fires once per transition to `IsDirty = true`

### OptionModel

```
OptionModel
  OptionId   ushort          -- runtime option ID byte (0-255)
  Mode       ScoringMode     -- WeightedProduct or WeightedSum
  VisualId   string          -- deterministic sort key; never changes after creation
  Considerations  List<ConsiderationModel>
```

### ConsiderationModel

```
ConsiderationModel
  InputName  string          -- catalog name resolved via In.Fnv1a32 at preview time
  Context    InputContext
  Weight     float           -- 0f-1f; must be in range (UT0131 diagnostic)
  Curve      ResponseCurveModel
  Params     InputParamsModel -- BlueprintId, MaxRange, MountIndex
  VisualId   string
```

### ResponseCurveModel

Editor-side curve with five params (m, k, b, c, Kind) plus optional piecewise control points.
Converts to/from `UtilityCurve` and to runtime `ResponseCurve`:

```csharp
// To runtime ResponseCurve (C is discarded; for PiecewiseLinear, registers with PiecewiseCurveCatalog)
ResponseCurve rc = model.ToRuntime();
```

### UtilityLayoutData

Visual layout hint overlay:

```
UtilityLayoutData
  PinnedFixture  string?   -- name of the fixture shown in the preview panel at startup
```

---

## Curve Widget (`Curve/`)

### CurveWidget.Draw

Host-agnostic ImGui widget. Draws:

1. A Kind dropdown (all `CurveKind` values).
2. An invisible-button plot canvas (configurable width/height).
3. A primary curve polyline (16 samples).
4. An optional grey comparison overlay from `CurveWidgetOptions.ComparisonCurve`.
5. A green vertical marker at `CurveWidgetOptions.FixtureInputX` with the output value label.
6. For `PiecewiseLinear`: draggable control-point handles (left-click canvas to add, right-click
   handle to delete); points are kept x-sorted at all times.

```csharp
// Usage (in any ImGui frame):
bool changed = CurveWidget.Draw(id: "my_curve", ref curve, CurveWidgetOptions.Default);

// With comparison overlay and fixture marker:
var opts = new CurveWidgetOptions(
    plotWidth:             240f,
    plotHeight:            80f,
    fixtureInputX:         0.6f,     // draw a marker at x=0.6
    showComparisonOverlay: true,
    comparisonCurve:       savedVersion);
bool changed = CurveWidget.Draw("preview", ref curve, in opts);
```

Returns `true` when the user changed any parameter this frame.

### CurveWidget.Evaluate

Static helper used internally and in tests:

```csharp
float y = CurveWidget.Evaluate(in curve, x);
```

Delegates to `ResponseCurve.Evaluate` for all non-piecewise kinds, adds the `C` (y-shift)
afterwards, then clamps to [0, 1]. For `PiecewiseLinear`, linearly interpolates over
`curve.Points`; clamps to first/last Y outside the control-point range.

### CurveWidget.IsParamEditable

Returns whether the given param (`"m"`, `"k"`, `"b"`, `"c"`) is meaningful for a given
`CurveKind`. Used to grey out irrelevant sliders in the inspector.

### CurveWidget.AddPiecewisePoint / RemovePiecewisePoint

Allocation-safe helpers that return a new sorted array with the point added or removed:

```csharp
PiecewisePoint[] pts = CurveWidget.AddPiecewisePoint(curve.Points, x: 0.5f, y: 0.8f);
pts = CurveWidget.RemovePiecewisePoint(pts, index: 2);
```

### CurveWidgetOptions

```csharp
public readonly struct CurveWidgetOptions
{
    float         PlotWidth;             // 0 = fill available ImGui width
    float         PlotHeight;            // default 80f
    float         FixtureInputX;         // -1 = no marker
    bool          ShowComparisonOverlay;
    UtilityCurve? ComparisonCurve;       // null = no overlay

    static CurveWidgetOptions Default;   // PlotHeight=80, no marker, no overlay
}
```

---

## Code Emitter (`Emit/`)

### UtilityFluentEmitter

Produces a deterministic `.cs` file for one `UtilityDecisionAsset`. Output is byte-identical
across repeated calls on the same model. Options and considerations are emitted in ascending
`VisualId` order, ensuring stable diffs.

```csharp
var emitter = new UtilityFluentEmitter(targetNamespace: "Fdp.Toolkit.Utility");
string csSource = emitter.Emit(asset);
```

Output structure:

```
// HROT_EDITOR_GENERATED - manual edits to this file will be overwritten ...
// AssetId: <guid>

using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Utility;

[UtilityDecision(
    assetId:     "<guid-D>",
    displayName: "Combat Posture",
    kind:        DecisionKind.PostureSelect,
    category:    "Tactical/Posture")]
public sealed partial class CombatPosture : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        .Option(1, ScoringMode.WeightedProduct, o => o
            .Consider(In.HealthFraction(), 0.8f, Curve.InverseLinear)
            ...);
}
```

**Curve notation:**

- If the curve matches a preset (`Curve.Linear`, `Curve.Bell`, etc.) the shorthand is used.
- Otherwise `new ResponseCurve(...)` is emitted with all four params.
- Weights use the `"R"` format specifier (full round-trip precision) followed by `f`.
- Non-zero `HysteresisBonus` is included in the attribute; zero bonus is omitted.

### UtilityAssetHasher

Computes independent `StructureHash` and `ParamHash` for hot-reload classification.

```csharp
// StructureHash: option/consideration topology (kind, VisualIds, input names, contexts)
int sh = UtilityAssetHasher.ComputeStructureHash(asset);

// ParamHash: tunable values (weights, m/k/b/c, curve kind, hysteresisBonus)
int ph = UtilityAssetHasher.ComputeParamHash(asset);

// Classification:
HotReloadTier tier = UtilityAssetHasher.Classify(before, after);
// -> Cosmetic  (neither hash changed -- layout-only)
// -> Soft      (only ParamHash changed -- weights/curves)
// -> Hard      (StructureHash changed -- options/considerations added/removed)
```

---

## Asset Loader (`Loading/`)

### UtilityAssetLoader

Text-based reader (no Roslyn, no assembly loading) that extracts metadata from a `.cs` file
produced by `UtilityFluentEmitter` or hand-authored with the `[UtilityDecision]` attribute.

```csharp
UtilityLoadResult result = UtilityAssetLoader.Load(filePath);
// result.Asset         -- populated UtilityDecisionAsset
// result.Warnings      -- list of parse warnings (missing marker, missing fields, etc.)
```

Fields extracted: `AssetId`, `DisplayName`, `DecisionKind`, `Category`, `HysteresisBonus`.

Options and considerations are **not** populated by this method (deferred; a richer Roslyn-
based pass populates the full option tree for the card-table UI).

**Editor-owned detection:**
The first five lines are scanned for `// HROT_EDITOR_GENERATED`. If the marker is absent,
`IsEditorOwned` is set to `false` and the asset is opened read-only.

---

## Live Preview (`Preview/`)

### UtilityPreviewRunner

Converts a `UtilityDecisionAsset` to a `UtilityDecisionDef` at preview time, then calls the
real runtime `UtilityScorer.Evaluate`. Output is byte-identical to a direct scorer call on
the same definition (verified by `UtilityPreviewRunnerTests`).

```csharp
UtilityPreviewResult result = UtilityPreviewRunner.Evaluate(
    asset,
    repo:    null,    // null when readers do not need ECS data
    self:    default,
    context: default);

// result.TopScore                 float    -- top-ranked option score
// result.OptionCount              int      -- number of options scored
// result.ConsiderationScores      List<UtilityPreviewConsiderationScore>
//   Each entry: InputId (ushort), RawValue, NormalizedValue, CurveOutput, Weight, RunningAggregate
```

Input IDs are derived from `InputName` via FNV-1a-32 (same hash used by `In.*` factory
methods), so any registered reader in `UtilityInputReaderStore` is automatically resolved.

---

## Comparison (`Comparison/`)

### UtilityTuningDiffEngine

Performs a parameter-level diff between two versions of a `UtilityDecisionAsset` using hash
equality for a fast-path check, then a per-field walk for parameter diffs.

```csharp
TuningDiffResult result = UtilityTuningDiffEngine.Compute(versionA, versionB);

// result.IsStructureEqual  -- true when both have the same option/consideration topology
// result.IsIdentical       -- true when structure AND params match
// result.Diffs             -- ordered list of TuningParamDiff (by VisualId)
```

`TuningParamDiff` contains `OptionVisualId`, `ConsiderationVisualId`, `ConsiderationName`,
`ParamLabel` (`"Weight"`, `"Slope"`, `"Exponent"`, `"XShift"`, or `"CurveKind"`), `OldValue`,
and `NewValue`.

### UtilityComparisonSanitizer

Strips the editor-generated suffix from the marker comment and removes the `[UtilityLayout]`
block from a `.cs` file before submitting it to the LLM comparison pipeline. This prevents
layout noise from appearing as semantic differences in the AI review.

```csharp
var sanitizer = new UtilityComparisonSanitizer();
SanitizationResult r = sanitizer.Sanitize(new AssetExportRequest(path, null, AssetKind.Utility));
// r.SanitizedText   -- cleaned text for the comparison prompt
// r.Warnings        -- parse warnings
```

---

## StructEdit Field Integration (`FieldEdit/`)

### UtilityCurveFieldDrawer

`IImGuiFieldDrawer` implementation that renders a `UtilityCurve` value using `CurveWidget.Draw`.
Registered with StructEdit so any struct field of type `UtilityCurve` is rendered as a live
curve editor (both in the Utility editor inspector and in the tuning console).

```csharp
public sealed class UtilityCurveFieldDrawer : IImGuiFieldDrawer
{
    public Type TargetType => typeof(UtilityCurve);

    public bool DrawInput(ref object value, EditNode node)
    {
        var curve = value is UtilityCurve c ? c : default;
        bool changed = CurveWidget.Draw(node.JsonPath, ref curve, CurveWidgetOptions.Default);
        if (changed) value = curve;
        return changed;
    }
}
```

Returns `true` when the user changed the curve; StructEdit marks the session dirty and the
tuning console commit path enqueues the change through `TuningConsoleGizmo.OnStructUpdate`.

---

## Window (`Windows/`)

### UtilityDecisionWindow

`ManagedWindow`-derived host for the card-table editor. Registers under the `"Authoring"`
window group with scope `WindowScope.PerspectiveBound`.

```csharp
// Open and display a decision asset:
window.OpenAsset(utilityDecisionAsset);

// The active asset is accessible read-only:
UtilityDecisionAsset? asset = window.ActiveAsset;
```

Selection integration: when `EditorSelectionStore.ActiveAsset` changes to a
`UtilityDecisionAsset`, the window updates its `_activeAsset` automatically via the
`OnSelectionChanged` callback.

When no asset is open, the window renders a `TextDisabled` placeholder directing the user to
the Asset Browser.

---

## Tracing (`Tracing/`)

### UtilityTraceLaneProvider

Implements `ITraceLaneProvider` with `AssetKind.Utility`. Provides two trace lanes displayed
in the shared debug timeline:

| Lane ID | Display name |
|---|---|
| `utility_scoring` | Utility scoring events (option selected, score, margin) |
| `utility_values` | Per-consideration raw/curve values from the trace ring buffer |

---

## Source Structure

```
Hrot.Utility.Editor/
  Catalog/              -- input-catalog browser (list of registered input names)
  Comparison/
    UtilityComparisonSanitizer.cs
    UtilityTuningDiffEngine.cs
  Curve/
    CurveWidget.cs        -- static Draw, Evaluate, IsParamEditable, AddPiecewisePoint, ...
    CurveWidgetOptions.cs -- CurveWidgetOptions readonly struct
  Emit/
    UtilityAssetHasher.cs -- StructureHash / ParamHash / HotReloadTier classification
    UtilityFluentEmitter.cs -- IFluentCSharpEmitter<UtilityDecisionAsset> implementation
  FieldEdit/
    UtilityCurveFieldDrawer.cs  -- IImGuiFieldDrawer for UtilityCurve (console path)
    UtilityCurveFieldEditor.cs  -- alternate IImGuiFieldDrawer for standalone editor UI
  Loading/
    UtilityAssetLoader.cs
  Model/
    UtilityDecisionAsset.cs  -- UtilityDecisionAsset, OptionModel, ConsiderationModel,
                             --   ResponseCurveModel, InputParamsModel, UtilityLayoutData, FixtureRef
  Preview/
    UtilityPreviewRunner.cs
    UtilityPreviewResult.cs  -- UtilityPreviewResult, UtilityPreviewConsiderationScore
  Tracing/
    UtilityTraceLaneProvider.cs
  Windows/
    UtilityDecisionWindow.cs
```

---

## Dependencies

| Assembly | Used for |
|---|---|
| `Fdp.Core` | `Entity`, `EntityRepository` |
| `Fdp.Toolkit.Utility` | `UtilityDecisionDef`, `UtilityScorer`, `UtilityResultBuffer`, `UtilityCurve`, `ResponseCurve` |
| `Fdp.Presentation` | `ManagedWindow`, `WindowScope` |
| `Fdp.Presentation.Editing` | `IImGuiFieldDrawer`, `EditNode` |
| `Hrot.Editor.AiShared` | `IEditableAsset`, `AssetKind`, `EditorSelectionStore`, `IFluentCSharpEmitter`, `HotReloadTier` |
| `ImGuiNET` | ImGui rendering |
| `StructEdit.Core` | `IImGuiFieldDrawer` target type registration |

---

## Implementation Status

All phases complete (2026-05-30):

| Phase | Content |
|---|---|
| Phase 3 | `CurveWidget.Draw` host-agnostic widget (shared with tuning console); `CurveWidgetOptions`; `UtilityCurve` model with `FromResponseCurve`/`ToResponseCurve` round-trip |
| Phase 5 | `UtilityDecisionAsset` model; `UtilityDecisionWindow`; `UtilityFluentEmitter`; `UtilityAssetLoader`; `UtilityPreviewRunner`; `UtilityAssetHasher`; `UtilityComparisonSanitizer`; `UtilityTuningDiffEngine`; `UtilityTraceLaneProvider` |
| Phase 6 | `UtilityCurveFieldDrawer`; `UtilityCurveFieldEditor` (piecewise translate-on-apply path via `TuningConsoleGizmo.OnStructUpdate`) |
