# BATCH-18 Instructions — P6-01 (CurveFieldEditor + CurveFieldDrawer) + P6-02 (piecewise translate-on-apply)

**Batch ID:** BATCH-18
**Phase tasks:** TASK-UAI-P6-01, TASK-UAI-P6-02
**Design refs:**
- `Curve_Editor_in_StructEdit_Guide_v1_1.md` §3 Steps 3-5 (P6-01)
- `Curve_Editor_in_StructEdit_Guide_v1_1.md` §6 (P6-02)

---

## Context

Phase 5 is complete. Phase 6 begins with the two StructEdit plugin classes that wrap `CurveWidget`
into the StructEdit field-editor system (P6-01), followed by the piecewise translate-on-apply that
bridges managed `UtilityCurve.Points` edits to the runtime `PiecewiseCurveCatalog` (P6-02).

There are two projects involved:
- `Hrot/Editor/Hrot.Utility.Editor/` — P6-01 files
- `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/` — P6-02 files

---

## MANDATORY READS before writing any code

Read ALL of these in full before writing a single line:

1. `Curve_Editor_in_StructEdit_Guide_v1_1.md` — full file; especially §3 Steps 3-5 and §6
2. `FDP/ExtDeps/StructEdit/src/StructEdit.Core/Plugins/ICustomFieldEditor.cs`
3. `FDP/Engine/Fdp.Presentation/ImGui/Editing/IImGuiFieldDrawer.cs`
4. `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/Editors/GuidFieldEditor.cs`
5. `FDP/Engine/Fdp.Presentation/ImGui/Editing/QuaternionEulerFieldDrawer.cs`
6. `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditNodeKind.cs`
7. `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditNode.cs`
8. `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidget.cs` — FULL file; see `Draw(id, ref UtilityCurve, in CurveWidgetOptions)` signature
9. `Hrot/Editor/Hrot.Utility.Editor/Curve/CurveWidgetOptions.cs`
10. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCurve.cs` — FULL file; see `UtilityCurve` struct, `PiecewisePoint`, `ToResponseCurve()`, `FromResponseCurve()`
11. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/PiecewiseCurveCatalog.cs` — FULL file
12. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningRegistry.cs` — FULL file
13. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Tunable.cs`
14. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningKind.cs`
15. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/UtilityTuningBinder.cs` — FULL file
16. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Gizmos/TuningConsoleGizmo.cs` — FULL file
17. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/TuningConsoleGizmoTests.cs` — FULL file (to understand existing test harness)
18. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/TuningRegistryTests.cs`
19. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/UtilityTuningBinderTests.cs`

---

## Task A — P6-01: UtilityCurveFieldEditor + UtilityCurveFieldDrawer

### A.1 UtilityCurveFieldEditor

**File:** `Hrot/Editor/Hrot.Utility.Editor/FieldEdit/UtilityCurveFieldEditor.cs`

Implement `ICustomFieldEditor` for `UtilityCurve`. Follows the exact pattern of
`GuidFieldEditor` (read it first). The only difference: TargetType is `UtilityCurve` and
EditNodeKind is `Custom` (not `Guid`).

```
namespace Hrot.Utility.Editor.FieldEdit;

using Fdp.Toolkit.Utility;
using StructEdit.Core;
using StructEdit.Core.Plugins;

public sealed class UtilityCurveFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(UtilityCurve);

    public EditNode? CreateNode(
        EditNodeId id, string name, string jsonPath,
        IValueBinding binding, EditNodeMetadata metadata)
        => new EditNode(id, name, jsonPath, EditNodeKind.Custom,
                        typeof(UtilityCurve), binding, null, metadata);
}
```

No logic beyond creating the node. No StructEdit modification — this is a pure plugin.

### A.2 UtilityCurveFieldDrawer

**File:** `Hrot/Editor/Hrot.Utility.Editor/FieldEdit/UtilityCurveFieldDrawer.cs`

Implement `IImGuiFieldDrawer` for `UtilityCurve`. Follows `QuaternionEulerFieldDrawer` (read it
first). Calls `CurveWidget.Draw` with `CurveWidgetOptions.Default`.

```
namespace Hrot.Utility.Editor.FieldEdit;

using Fdp.Presentation.Editing;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Curve;
using StructEdit.Core;

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

### A.3 Tests for P6-01

**File:** `Hrot/Editor/Hrot.Utility.Editor.Tests/FieldEdit/UtilityCurveFieldEditorTests.cs`

These tests must NOT call ImGui. Test only the structural properties.

Required tests (minimum 4):

1. `UtilityCurveFieldEditor_TargetType_IsUtilityCurve`
   - `new UtilityCurveFieldEditor().TargetType == typeof(UtilityCurve)`

2. `UtilityCurveFieldEditor_CreateNode_KindIsCustom`
   - Build a minimal `IValueBinding` stub (or use a `NullBinding` if it exists; otherwise create a
     simple inline implementation that returns `default` for Get and is a no-op for Set).
   - Call `CreateNode(default, "Curve", "curve", binding, default)`.
   - Assert `node != null`.
   - Assert `node!.Kind == EditNodeKind.Custom`.

3. `UtilityCurveFieldEditor_CreateNode_TypeIsUtilityCurve`
   - Same setup as above; assert `node!.FieldType == typeof(UtilityCurve)`.

4. `UtilityCurveFieldDrawer_TargetType_IsUtilityCurve`
   - `new UtilityCurveFieldDrawer().TargetType == typeof(UtilityCurve)`

**How to stub `IValueBinding`:** Check whether `StructEdit.Core` provides a null/stub binding.
If not, declare a local minimal implementation inline in the test class:

```csharp
private sealed class StubBinding : IValueBinding
{
    public object? GetValue() => default(UtilityCurve);
    public void SetValue(object? value) { }
}
```

---

## Task B — P6-02: Piecewise translate-on-apply

The guide §6 says: when a TuningChangeEvent JSON arrives containing a UtilityCurve object
(including a variable-length Points array), the apply logic must:
1. Deserialize to `UtilityCurve`.
2. Clamp `Points.Length` to `MaxPiecewisePoints = 64`. If clamped, emit a warning.
3. Call `UtilityCurve.ToResponseCurve()` which registers the points in `PiecewiseCurveCatalog`
   and returns a `ResponseCurve` with the stable `CurveId`.
4. Apply the resulting `ResponseCurve` to the consideration stored in the tunable.

This requires changes in 4 files and 1 new file.

### B.1 CurveTunable.cs

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/CurveTunable.cs`

A separate tunable record for `UtilityCurve`-typed tunables. Analogous to `Tunable` but with
`Func<UtilityCurve>` / `Action<UtilityCurve>` delegates instead of float.

```csharp
namespace Hrot.Diagnostics.Tuning
{
    using System;
    using Fdp.Toolkit.Utility;

    public sealed class CurveTunable
    {
        public TuningKey Key;
        public TuningScope Scope;
        public TuningOwner Owner;
        public string Provenance = string.Empty;
        public required Func<UtilityCurve>    Read;
        public required Action<UtilityCurve>  Write;
    }
}
```

No min/max (curve fields have varied ranges; clamping is per-sub-field).

### B.2 Extend TuningRegistry

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningRegistry.cs`

Add the following alongside the existing float infrastructure. Do NOT change the existing float
`Apply`, `Register`, `BeginFrame`, or `GetGroups` logic.

Add:
- `private readonly Dictionary<uint, CurveTunable> _curveTunables = new();`
- `private readonly Queue<(uint id, UtilityCurve value)> _curveApplyQueue = new();`
- `public const int MaxPiecewisePoints = 64;`

Add method `RegisterCurve(TuningKey key, CurveTunable tunable)`:
```csharp
public void RegisterCurve(TuningKey key, CurveTunable tunable)
{
    tunable.Key = key;
    _curveTunables[key.Id] = tunable;
}
```

Add method `ApplyCurve(TuningKey key, UtilityCurve value)`:
```csharp
public bool ApplyCurve(TuningKey key, UtilityCurve value)
{
    if (!_curveTunables.ContainsKey(key.Id)) return false;
    lock (_queueLock)
        _curveApplyQueue.Enqueue((key.Id, value));
    return true;
}
```

Extend `BeginFrame()` to also drain `_curveApplyQueue` after the existing float drain:
```csharp
// After the existing float drain:
(uint id, UtilityCurve value)[] curvePending;
lock (_queueLock)
{
    if (_curveApplyQueue.Count == 0) { curvePending = Array.Empty<(uint, UtilityCurve)>(); }
    else
    {
        curvePending = _curveApplyQueue.ToArray();
        _curveApplyQueue.Clear();
    }
}
foreach (var (id, curve) in curvePending)
{
    if (!_curveTunables.TryGetValue(id, out var tunable)) continue;
    tunable.Write(curve);
}
```

Also add a `TryGetCurve(TuningKey key, out CurveTunable? tunable)` helper:
```csharp
public bool TryGetCurve(TuningKey key, out CurveTunable? tunable)
    => _curveTunables.TryGetValue(key.Id, out tunable);
```

### B.3 Extend UtilityTuningBinder

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/UtilityTuningBinder.cs`

In `RegisterConsideration`, after the 4 existing float registrations (weight, slope, exponent,
xShift), add a 5th registration for the whole curve as a `CurveTunable`:

```csharp
// curve (whole UtilityCurve including piecewise points)
registry.RegisterCurve(new TuningKey($"{prefix}.curve"), new CurveTunable
{
    Scope      = TuningScope.Global,
    Owner      = TuningOwner.Brain,
    Provenance = $"decision:{decName}",
    Read       = () => UtilityCurve.FromResponseCurve(option.Considerations[ci].Curve),
    Write      = uc =>
    {
        var old  = option.Considerations[ci];
        option.Considerations[ci] = new UtilityConsideration(
            old.InputId, old.Context, old.Weight,
            uc.ToResponseCurve(),
            old.Params);
    },
});
```

`UtilityCurve.FromResponseCurve` and `UtilityCurve.ToResponseCurve` are already defined in
`FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCurve.cs`.

**Adding the using:**
Add `using Fdp.Toolkit.Utility;` if not already present (it already is — check the file).

### B.4 Extend TuningConsoleGizmo.OnStructUpdate

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Gizmos/TuningConsoleGizmo.cs`

The current `OnStructUpdate` only handles float-valued JSON properties. Extend it to also detect
object-valued properties (i.e., `JsonValueKind.Object`), deserialize them as `UtilityCurve`, clamp
`Points` to `TuningRegistry.MaxPiecewisePoints` with a warning, and call `_registry.ApplyCurve`.

**Required additions:**

1. At the top of the file, add `using Fdp.Toolkit.Utility;` and
   `using System.Text.Json;` (check if already present).

2. Inside `OnStructUpdate`, after the existing `if (prop.Value.TryGetSingle(out float v))` block,
   add an `else if` for object-valued properties:

```csharp
else if (prop.Value.ValueKind == JsonValueKind.Object)
    TryApplyCurveProperty(prop.Name, prop.Value);
```

3. Add private method `TryApplyCurveProperty(string keyName, JsonElement element)`:

```csharp
private void TryApplyCurveProperty(string keyName, JsonElement element)
{
    try
    {
        var curve = DeserializeUtilityCurve(element);
        if (curve.Kind == CurveKind.PiecewiseLinear && curve.Points != null
            && curve.Points.Length > TuningRegistry.MaxPiecewisePoints)
        {
            Console.Error.WriteLine(
                $"[TuningConsoleGizmo] Piecewise curve '{keyName}' has "
                + $"{curve.Points.Length} points; clamped to "
                + $"{TuningRegistry.MaxPiecewisePoints}.");
            var clamped = new PiecewisePoint[TuningRegistry.MaxPiecewisePoints];
            Array.Copy(curve.Points, clamped, TuningRegistry.MaxPiecewisePoints);
            curve.Points = clamped;
        }
        _registry.ApplyCurve(new TuningKey(keyName), curve);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"[TuningConsoleGizmo] Could not apply curve '{keyName}': {ex.Message}");
    }
}
```

4. Add private static method `DeserializeUtilityCurve(JsonElement el)`:

```csharp
private static UtilityCurve DeserializeUtilityCurve(JsonElement el)
{
    var curve = new UtilityCurve();
    if (el.TryGetProperty("Kind", out var kindEl))
        curve.Kind = Enum.Parse<CurveKind>(kindEl.GetRawText().Trim('"'));
    if (el.TryGetProperty("M", out var mEl) && mEl.TryGetSingle(out float m))
        curve.M = m;
    if (el.TryGetProperty("K", out var kEl) && kEl.TryGetSingle(out float k))
        curve.K = k;
    if (el.TryGetProperty("B", out var bEl) && bEl.TryGetSingle(out float b))
        curve.B = b;
    if (el.TryGetProperty("C", out var cEl) && cEl.TryGetSingle(out float c))
        curve.C = c;
    if (el.TryGetProperty("Points", out var ptsEl)
        && ptsEl.ValueKind == JsonValueKind.Array)
    {
        var pts = new System.Collections.Generic.List<PiecewisePoint>();
        foreach (var pt in ptsEl.EnumerateArray())
        {
            float x = 0f, y = 0f;
            if (pt.TryGetProperty("X", out var xEl)) xEl.TryGetSingle(out x);
            if (pt.TryGetProperty("Y", out var yEl)) yEl.TryGetSingle(out y);
            pts.Add(new PiecewisePoint(x, y));
        }
        curve.Points = pts.ToArray();
    }
    return curve;
}
```

`Kind` in JSON may arrive as an integer (e.g., `4`) or a string (`"PiecewiseLinear"`). Handle both:
```csharp
if (el.TryGetProperty("Kind", out var kindEl))
{
    if (kindEl.ValueKind == JsonValueKind.Number && kindEl.TryGetInt32(out int ki))
        curve.Kind = (CurveKind)ki;
    else if (kindEl.ValueKind == JsonValueKind.String)
        curve.Kind = Enum.Parse<CurveKind>(kindEl.GetString()!);
}
```

---

## Task C — Tests for P6-02

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/PiecewiseTranslateTests.cs`

These are the SC-P6-2 tests. All tests use `TuningConsoleGizmo` + `TuningRegistry` headlessly
(no network, no DDS). Follow the pattern in `TuningConsoleGizmoTests.cs`.

Required tests (minimum 6):

1. **`RegisterCurve_ThenBeginFrame_WriteIsInvoked`**
   Register a CurveTunable. Call `ApplyCurve`. Call `BeginFrame`. Assert the Write delegate was
   called with the right curve.

2. **`OnStructUpdate_ObjectProperty_CallsApplyCurve`** (SC-P6-2 core test)
   - Register a UtilityDecisionDef with a PiecewiseLinear consideration via `UtilityTuningBinder`.
   - Build the JSON for a curve-typed property: `{"utility.TestDec.0.0.curve": {"Kind": 4,
     "M": 1.0, "K": 1.0, "B": 0.0, "C": 0.0, "Points": [{"X": 0.0, "Y": 0.0},
     {"X": 1.0, "Y": 1.0}]}}` — CurveKind.PiecewiseLinear = 4.
   - Call `gizmo.OnStructUpdate(json)`.
   - Call `registry.BeginFrame()`.
   - Read the consideration's `Curve` from the `UtilityDecisionDef`; assert `Kind == PiecewiseLinear`.
   - Assert `PiecewiseCurveCatalog.Evaluate(curveId, 0.5f)` is approximately 0.5f (linear interp).

3. **`OnStructUpdate_PointsClamped_EmitsWarning`**
   - Build a JSON object with `MaxPiecewisePoints + 5` points in the Points array.
   - Capture `Console.Error` output (redirect `Console.Error` to a `StringWriter` before the call).
   - Call `gizmo.OnStructUpdate(json)`.
   - Assert the warning message was written (contains "clamped").
   - After `BeginFrame()`, assert the consideration's piecewise curve has at most `MaxPiecewisePoints`
     points registered.
   - Restore `Console.Error` in cleanup.

4. **`OnStructUpdate_NonCurveObject_IsIgnoredGracefully`**
   - Pass a JSON property with an object value that is NOT a valid curve (e.g.,
     `{"some.key": {"Foo": 1}}`).
   - Call `OnStructUpdate` — must not throw.

5. **`OnStructUpdate_FloatAndCurveInSameBatch_BothApplied`**
   - Register a decision via `UtilityTuningBinder`.
   - Build JSON with both a float property (`utility.TestDec.0.0.weight: 2.5`) and a curve object
     property (`utility.TestDec.0.0.curve: { ... }`).
   - `OnStructUpdate` → `BeginFrame()`.
   - Assert weight == 2.5f AND curve Kind == PiecewiseLinear.

6. **`DeserializeUtilityCurve_KindAsInteger_IsHandled`**
   - Pass a JSON with `"Kind": 4` (integer, not string).
   - After translate-on-apply, assert curve.Kind == CurveKind.PiecewiseLinear.
   - (Can reuse the setup from test 2 with integer Kind instead of string.)

**For test 2 and 3, after calling BeginFrame, get the curveId from the consideration's Curve field
and look it up in PiecewiseCurveCatalog:**
```csharp
short curveId = def.Options[0].Considerations[0].Curve.CurveId;
float midVal = PiecewiseCurveCatalog.Evaluate(curveId, 0.5f);
Assert.Equal(0.5f, midVal, precision: 4);
```

**Also clean up PiecewiseCurveCatalog between tests by calling `PiecewiseCurveCatalog.ClearAll()`
in Dispose (see `CurveEvaluationTests.cs` for precedent).**

Check whether `PiecewiseCurveCatalog.ClearAll()` exists — if it does not, use a `try/finally` or
just accept test pollution (the catalog uses stable IDs derived from content hash, so re-registering
with the same content is idempotent).

---

## Naming conventions

- Namespace for `FieldEdit/`: `Hrot.Utility.Editor.FieldEdit`
- Namespace for tests: `Hrot.Utility.Editor.Tests.FieldEdit`
- Tuning test namespace: `Hrot.Diagnostics.Tuning.Tests`

---

## Build & test

After all files are in place:

```
dotnet build Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj
dotnet build Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Hrot.Diagnostics.Tuning.csproj
dotnet test Hrot/Editor/Hrot.Utility.Editor.Tests/Hrot.Utility.Editor.Tests.csproj
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/Hrot.Diagnostics.Tuning.Tests.csproj
```

Fix any build errors before reporting.

Expected additional tests: ~10 new (4 for P6-01 + 6 for P6-02). Existing 137 must still pass.

---

## Report format

Return a BATCH-18-REPORT.md (save to `.dev/utility-ai/reports/BATCH-18-REPORT.md`) with:
- Files created/modified
- Build errors (must be zero)
- Test results by project
- Any deviations from these instructions (explain why)
