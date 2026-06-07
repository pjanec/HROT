# BATCH-14: UtilityDecisionAsset model + shared-infra extensions

**Batch Number:** BATCH-14
**Tasks:** TASK-UAI-P5-06 (shared-infra extensions), TASK-UAI-P5-01 (asset model + window host)
**Phase:** Phase 5 — Utility editor (card-table)
**Estimated Effort:** 12 hours
**Priority:** HIGH
**Dependencies:** BATCH-13 (TuningRegistry, TuningConsoleGizmo — committed)

---

## Onboarding & Workflow

### Developer Instructions

This batch wires the Utility AI editor into the shared AI editor infrastructure (`Hrot.Editor.AiShared`)
and creates the in-memory model types and the `ManagedWindow` host skeleton used by the Utility card-table
editor. No UI drawing logic is required yet — that comes in later batches. The focus here is on
correct model types, the IEditableAsset implementation, the window host stub, and the four small
shared-infra extension points.

**MANDATORY WORKFLOW: Do NOT stop to ask permission. Implement everything, run all tests, fix all
errors, then submit the report. Zero build errors and all tests passing are required.**

### Required Reading (IN ORDER)

1. `.dev/utility-ai/reviews/BATCH-13-REVIEW.md` — last review, understand patterns used
2. `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md` — THE design document; read all of it before
   starting. Key sections for this batch: §1, §3, §4.1, §11
3. `.dev/utility-ai/TASK-DETAIL.md` — section `TASK-UAI-P5-01` and `TASK-UAI-P5-06`
4. `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs` — interface to implement
5. `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs` — add `Utility` here
6. `Hrot/Editor/Hrot.Editor.AiShared/References/SubElementKind.cs` — add `UtilityInput` here
7. `Hrot/Editor/Hrot.Editor.AiShared/Debug/ITraceLaneProvider.cs` — implement for utility
8. `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — the ManagedWindow
   precedent; study its constructor signature pattern
9. `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs` — base class for the window
10. `Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj` — existing project to extend
11. `Hrot/Editor/Hrot.Utility.Editor.Tests/Hrot.Utility.Editor.Tests.csproj` — existing test project

### Source Code Locations

- **Shared infra (modifications):**
  `Hrot/Editor/Hrot.Editor.AiShared/` — add enum values + new provider file
- **Primary work area (new files):**
  `Hrot/Editor/Hrot.Utility.Editor/` — asset model types, window host, lane provider
- **Test project:**
  `Hrot/Editor/Hrot.Utility.Editor.Tests/` — new test file for this batch

### Report Submission

**When done, submit report to:**
`.dev/utility-ai/reports/BATCH-14-REPORT.md`

**If you have questions:**
`.dev/utility-ai/questions/BATCH-14-QUESTIONS.md`

---

## Context

Phase 5 builds the visual Utility AI editor. Before any card-table UI can be drawn, four
prerequisites must exist:

1. **Shared-infra extension points** (P5-06): The `AssetKind` and `SubElementKind` enums in
   `Hrot.Editor.AiShared` need `Utility` and `UtilityInput` values. A `UtilityTraceLaneProvider`
   must register utility scoring lanes in the timeline window. The `InspectorWindow` needs a dispatch
   arm for the `UtilityConsiderationSelection` sub-selection type.

2. **Asset model** (P5-01): `UtilityDecisionAsset` and its contained `OptionModel`,
   `ConsiderationModel`, `ResponseCurveModel`, `InputParamsModel` form the in-memory editor model
   that mirrors the runtime `UtilityDecisionDef`. It implements `IEditableAsset` so the shared
   selection bus, emitter contract, and comparison pipeline can treat it uniformly.

3. **Window host** (P5-01): `UtilityDecisionWindow : ManagedWindow` is the empty-but-wired host.
   It subscribes to `EditorSelectionStore`, holds the active `UtilityDecisionAsset`, and calls
   `DrawClientArea` — which for now just renders a placeholder. The full card-table UI is built in
   BATCH-15 and beyond.

---

## Task 1: Shared-infra extensions (TASK-UAI-P5-06)

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §11 (all four items)

### 1a. Add `Utility` to `AssetKind`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs` (MODIFY)

Add `Utility` as the last value in the enum. No other changes.

### 1b. Add `UtilityInput` to `SubElementKind`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/References/SubElementKind.cs` (MODIFY)

Add `UtilityInput` as the last value in the enum. No other changes.

### 1c. Add `UtilityTraceLaneProvider`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Tracing/UtilityTraceLaneProvider.cs` (NEW)

Implement `ITraceLaneProvider` from `Hrot.Editor.AiShared.Debug`. The lane provider advertises
two lanes:
- `"utility_scoring"` / `"Decision Scoring"` / `TraceLevel.Decisions`
- `"utility_values"` / `"Consideration Values"` / `TraceLevel.Values`

```csharp
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Utility.Editor.Tracing;

public sealed class UtilityTraceLaneProvider : ITraceLaneProvider
{
    public AssetKind Kind => AssetKind.Utility;

    public IReadOnlyList<TraceLaneDescriptor> Lanes { get; } = new TraceLaneDescriptor[]
    {
        new("utility_scoring",  "Decision Scoring",       TraceLevel.Decisions),
        new("utility_values",   "Consideration Values",   TraceLevel.Values),
    };
}
```

### 1d. Add `UtilityConsiderationSelection` + inspector dispatch arm

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Selection/SubSelectionRecords.cs` (MODIFY)

Add the following record at the end of the file (do not touch the existing records):

```csharp
public sealed record UtilityConsiderationSelection(
    int OptionIndex,
    int ConsiderationIndex) : IAssetSubSelection;
```

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` (MODIFY)

In `DrawClientArea`, after the `if (_store.ActiveSubSelection is BTreeNodeSelection ...)` block
(near the bottom of the method), add an analogous block:

```csharp
        // ---- Utility consideration inspector panel ----------------------------
        if (_store.ActiveSubSelection is UtilityConsiderationSelection utilSel)
        {
            ImGuiNET.ImGui.Separator();
            ImGuiNET.ImGui.Text("UTILITY CONSIDERATION");
            ImGuiNET.ImGui.TextDisabled(
                $"Option {utilSel.OptionIndex}, Consideration {utilSel.ConsiderationIndex}");
            // Curve inspector panel wired in a later phase (P5-02).
        }
```

You will need to add the `using Hrot.Editor.AiShared.Selection;` import if it is not already
present.

---

## Task 2: Asset model types (TASK-UAI-P5-01, model part)

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §3 (all sub-sections)

Create all model files in `Hrot/Editor/Hrot.Utility.Editor/Model/`.

### 2a. `ResponseCurveModel`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/ResponseCurveModel.cs` (NEW)

Editor-side representation of a response curve. Mirrors `ResponseCurve` from
`Fdp.Toolkit.Utility` but is mutable and carries piecewise points as a managed list.

```csharp
using System.Collections.Generic;
using Fdp.Toolkit.Utility;

namespace Hrot.Utility.Editor.Model;

// Editor-side mutable representation of a response curve.
// Maps directly to the runtime ResponseCurve + PiecewiseCurveCatalog side-table.
public sealed class ResponseCurveModel
{
    public CurveKind Kind  = CurveKind.Linear;
    // m / slope
    public float M  = 1f;
    // k / exponent
    public float K  = 1f;
    // b / horizontal shift
    public float B  = 0f;
    // c / vertical shift
    public float C  = 0f;
    // Points used when Kind == PiecewiseLinear; null or empty for all other kinds.
    public List<(float x, float y)>? Points;

    // Converts this model to a runtime ResponseCurve (no side-table registration).
    // Call UtilityCurveConverter.ToRuntime for full conversion including piecewise side-table.
    public ResponseCurve ToRuntime()
        => new ResponseCurve(Kind, M, K, B);
}
```

### 2b. `InputParamsModel`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/InputParamsModel.cs` (NEW)

Editor-side representation of `InputParams`. Mutable; fields named after their semantic role.

```csharp
namespace Hrot.Utility.Editor.Model;

// Editor-side representation of per-consideration sensor parameters.
public sealed class InputParamsModel
{
    // FNV-1a of asset GUID -- EQS sensor readers.
    public uint  BlueprintId;
    // Maximum range in metres -- DistanceToContext readers.
    public float MaxRange;
    // Zero-based weapon mount index -- per-mount weapon readers.
    public int   MountIndex;
}
```

### 2c. `ConsiderationModel`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/ConsiderationModel.cs` (NEW)

```csharp
using System;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared.Emit;

namespace Hrot.Utility.Editor.Model;

// Editor-side, mutable model for one consideration row.
public sealed class ConsiderationModel
{
    // Resolves to In.<InputName>; validated by the authoring analyzer.
    public string         InputName   = string.Empty;
    public InputContext   Context     = InputContext.Self;
    public InputParamsModel Params    = new();
    public ResponseCurveModel Curve   = new();
    public float          Weight      = 1f;
    // Stable identifier for deterministic emit and comparison annotation.
    public string         VisualId    = Guid.NewGuid().ToString("N");
}
```

### 2d. `OptionModel`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/OptionModel.cs` (NEW)

```csharp
using System;
using System.Collections.Generic;
using Fdp.Toolkit.Utility;

namespace Hrot.Utility.Editor.Model;

// Editor-side, mutable model for one option inside a utility decision.
public sealed class OptionModel
{
    public ushort         OptionId         = 0;
    public string         Name             = string.Empty;
    public ScoringMode    Mode             = ScoringMode.WeightedProduct;
    public List<ConsiderationModel> Considerations = new();
    // Stable identifier for deterministic emit.
    public string         VisualId         = Guid.NewGuid().ToString("N");
}
```

### 2e. `FixtureRef`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/FixtureRef.cs` (NEW)

```csharp
namespace Hrot.Utility.Editor.Model;

// Reference to a test fixture used by the live-preview strip and CI tests.
public sealed class FixtureRef
{
    // Human-readable fixture name (also the CI fixture file stem).
    public string Name        = string.Empty;
    // Path to the fixture file relative to the project root.
    public string FilePath    = string.Empty;
}
```

### 2f. `UtilityLayoutData`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/UtilityLayoutData.cs` (NEW)

Editor-only layout data. Emitted as `[UtilityLayout]` static method; ignored by runtime.

```csharp
using System.Collections.Generic;

namespace Hrot.Utility.Editor.Model;

// Editor-only layout state: card order, collapsed flags, pinned fixture.
// Persisted via [UtilityLayout] method in the generated .cs file.
public sealed class UtilityLayoutData
{
    // VisualId order for option cards (empty = natural insertion order).
    public List<string> OptionOrder   = new();
    // VisualIds of options that are collapsed.
    public HashSet<string> Collapsed  = new();
    // Name of the currently pinned fixture (empty = first fixture).
    public string PinnedFixture       = string.Empty;
}
```

### 2g. `UtilityDecisionAsset`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Model/UtilityDecisionAsset.cs` (NEW)

The main editor model. Implements `IEditableAsset`.

Key points from design §3.1:
- `AssetId` is stamped by the editor (a `Guid`).
- `Kind` returns `AssetKind.Utility`.
- `IsEditorOwned` is `true` iff the source file contains the `HROT_EDITOR_GENERATED` marker
  (`FluentCSharpEmitterBase.EditorGeneratedMarker`).
- `IsDirty` is manually set by the window when a mutation command is applied.
- The `Changed` event is fired whenever `IsDirty` transitions from `false` to `true`.
- `Name` is `DisplayName` (used by asset browser).
- `SourceFilePath` is the absolute path to the `.cs` file on disk.

```csharp
using System;
using System.Collections.Generic;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Emit;

namespace Hrot.Utility.Editor.Model;

// In-memory editor model for one utility AI decision.
// Mirrors the runtime UtilityDecisionDef but is mutable and carries VisualIds.
public sealed class UtilityDecisionAsset : IEditableAsset
{
    // ---- IEditableAsset -------------------------------------------------

    public Guid   AssetId         { get; set; } = Guid.NewGuid();
    public string Name            => DisplayName;
    public AssetKind Kind         => AssetKind.Utility;
    public string SourceFilePath  { get; set; } = string.Empty;

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            if (_isDirty) Changed?.Invoke();
        }
    }

    // True iff the source file contains the HROT_EDITOR_GENERATED marker.
    public bool IsEditorOwned { get; set; }

    public event Action? Changed;

    // ---- Decision-specific fields ---------------------------------------

    public string         DisplayName      = string.Empty;
    public DecisionKind   DecisionKind     = DecisionKind.PostureSelect;
    public string         Category         = string.Empty;
    public float          HysteresisBonus  = 0f;
    public List<OptionModel>  Options      = new();
    public List<FixtureRef>   Fixtures     = new();
    public UtilityLayoutData  Layout       = new();
}
```

---

## Task 3: `UtilityDecisionWindow` host (TASK-UAI-P5-01, window part)

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §4 (full section, especially §4.1 and §4.2)

**File:** `Hrot/Editor/Hrot.Utility.Editor/Windows/UtilityDecisionWindow.cs` (NEW)

The `ManagedWindow` host for the utility card-table editor. For this batch, `DrawClientArea` renders
a placeholder text ("Utility AI Decision Editor — card table coming in a later phase"). Full UI is
added in BATCH-15.

Key wiring required NOW:
- Constructor takes `EditorSelectionStore store` and subscribes to `store.Changed`.
- `OnSelectionChanged` sets `_activeAsset` when the store's `ActiveAsset` is a `UtilityDecisionAsset`.
- `OpenAsset(UtilityDecisionAsset asset)` sets `_activeAsset`, sets `IsOpen = true`, requests focus.

```csharp
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Selection;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Windows;

// ManagedWindow host for the Utility AI card-table editor.
// Card-table UI rendered in later batches; this batch wires selection and asset activation.
public sealed class UtilityDecisionWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private UtilityDecisionAsset?         _activeAsset;

    public UtilityDecisionAsset? ActiveAsset => _activeAsset;

    public UtilityDecisionWindow(EditorSelectionStore store)
        : base("utility_decision_editor", "Utility Decision Editor", "Authoring",
               WindowScope.PerspectiveBound)
    {
        _store = store;
        _store.Changed += OnSelectionChanged;
    }

    // Opens the given asset and brings the window to front.
    public void OpenAsset(UtilityDecisionAsset asset)
    {
        _activeAsset = asset;
        IsOpen = true;
        RequestFocus();
    }

    private void OnSelectionChanged()
    {
        if (_store.ActiveAsset is UtilityDecisionAsset utilAsset)
            _activeAsset = utilAsset;
    }

    protected override void DrawClientArea()
    {
        if (_activeAsset is null)
        {
            ImGuiNET.ImGui.TextDisabled("No utility decision open. Use the Asset Browser.");
            return;
        }

        ImGuiNET.ImGui.Text($"Decision: {_activeAsset.DisplayName}");
        ImGuiNET.ImGui.TextDisabled("Card-table UI coming in a later batch.");
    }
}
```

**Important:** `ManagedWindow.RequestFocus()` may or may not exist. Search the base class:
`FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs` for the exact method name.
If it is `RequestFocus()`, use it. If it is something else (e.g. `Focus()`), use that instead.
If no focus method exists, just set `IsOpen = true`.

---

## Task 4: Update `Hrot.Utility.Editor.csproj`

**File:** `Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj` (MODIFY)

Add a project reference to `Hrot.Editor.AiShared`:

```xml
<ProjectReference Include="..\Hrot.Editor.AiShared\Hrot.Editor.AiShared.csproj" />
```

Check whether `Hrot.Editor.AiShared` already has a project reference from `Fdp.Presentation`.
If not, you may need to add it too. Inspect `Hrot.Editor.AiShared.csproj` to verify.

---

## Task 5: Tests

**File:** `Hrot/Editor/Hrot.Utility.Editor.Tests/UtilityDecisionAssetTests.cs` (NEW)

Write xUnit tests. Minimum 10 tests covering:

1. `UtilityDecisionAsset` — `Kind` returns `AssetKind.Utility`
2. `UtilityDecisionAsset` — `Name` returns `DisplayName`
3. `UtilityDecisionAsset` — setting `IsDirty = true` fires `Changed` event
4. `UtilityDecisionAsset` — setting `IsDirty = true` twice fires `Changed` only once
5. `UtilityDecisionAsset` — setting `IsDirty = false` after `true` does NOT re-fire `Changed`
6. `UtilityDecisionAsset` — `IsEditorOwned` reflects what was set
7. `ResponseCurveModel.ToRuntime` — returns `ResponseCurve` with correct `Kind`, `M`, `K`, `B`
8. `UtilityTraceLaneProvider` — `Kind` is `AssetKind.Utility`
9. `UtilityTraceLaneProvider` — `Lanes` has exactly 2 entries
10. `UtilityTraceLaneProvider` — lane ids are `"utility_scoring"` and `"utility_values"`
11. `UtilityDecisionWindow` — `ActiveAsset` is null before `OpenAsset` is called
12. `UtilityDecisionWindow` — after `OpenAsset`, `ActiveAsset` equals the opened asset and `IsOpen`
    is `true`

For `UtilityDecisionWindow` tests, construct it with a `new EditorSelectionStore()`. Check that
`EditorSelectionStore` has a no-arg constructor; if not, use a minimal stub.

---

## Build & Test Requirements

1. Run `dotnet build IOS-IG-SimHost.sln -c Debug` — must produce **0 errors**.
2. Run `dotnet test Hrot\Editor\Hrot.Utility.Editor.Tests\Hrot.Utility.Editor.Tests.csproj` —
   all tests (old + new) must pass.
3. Run `dotnet test Hrot\Editor\Hrot.Editor.AiShared.Tests\Hrot.Editor.AiShared.Tests.csproj` —
   verify the shared-infra tests still pass after the enum additions.

---

## Success Criteria

- [ ] `AssetKind.Utility` added to `Hrot.Editor.AiShared/Identity/AssetKind.cs`
- [ ] `SubElementKind.UtilityInput` added to `Hrot.Editor.AiShared/References/SubElementKind.cs`
- [ ] `UtilityConsiderationSelection` record added to `SubSelectionRecords.cs`
- [ ] Inspector dispatch arm for `UtilityConsiderationSelection` added to `InspectorWindow.cs`
- [ ] `UtilityTraceLaneProvider` implemented and compiling
- [ ] All model types in `Hrot.Utility.Editor/Model/` created
- [ ] `UtilityDecisionWindow` created and compiling
- [ ] `Hrot.Utility.Editor.csproj` references `Hrot.Editor.AiShared`
- [ ] >= 12 tests, all passing
- [ ] Solution builds with 0 errors

---

## Common Pitfalls

1. **`ManagedWindow` constructor**: The constructor takes `(string id, string title,
   string owningPerspective, WindowScope scope)`. Check the exact signature in
   `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs`.

2. **`ImGuiNET` not imported**: `ManagedWindow` uses ImGui. You may need
   `using ImGuiNET;` or use the fully qualified name. Look at `BlackboardAuthoringWindow.cs` for
   the exact import pattern used.

3. **`EditorSelectionStore.Changed` event**: Check `EditorSelectionStore.cs` for the exact event
   name and signature before using it.

4. **Circular references**: `Hrot.Utility.Editor` must NOT reference `Hrot.Editor.AiShared.Tests`.
   Only add a reference to `Hrot.Editor.AiShared` (the main project).

5. **`global using Gui = ImGuiNET.ImGui`**: The `Fdp.Presentation` project uses this global alias.
   Check if it is available in `Hrot.Utility.Editor` or use the full `ImGuiNET.ImGui.*` prefix.

---

## Reference Materials

- **Task Defs:** `.dev/utility-ai/TASK-DETAIL.md` — TASK-UAI-P5-01, TASK-UAI-P5-06
- **Design:** `.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md` — §3, §4, §11
- **Shared editor base:** `Hrot/Editor/Hrot.Editor.AiShared/`
- **Existing Utility editor:** `Hrot/Editor/Hrot.Utility.Editor/`
- **Existing test pattern:** `Hrot/Editor/Hrot.Utility.Editor.Tests/CurveWidgetTests.cs`
