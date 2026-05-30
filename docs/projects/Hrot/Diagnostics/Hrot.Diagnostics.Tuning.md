# Hrot.Diagnostics.Tuning -- Runtime AI Parameter Tuning Console

**Project path:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/`
**Project file:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Hrot.Diagnostics.Tuning.csproj`
**Test project:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/`
**Primary namespace:** `Hrot.Diagnostics.Tuning`
**Design references:**
- `.dev/utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md`
**Date:** 2026-05-30

---

## Executive Overview

`Hrot.Diagnostics.Tuning` provides a **live runtime parameter tuning system** for Utility AI
decisions. While a simulation is running, a designer can tweak weights, curve parameters, and
full response curves through the *AI Tuning Console* ImGui window without restarting the
process.

Changes take effect on the next simulation frame, can be reverted per decision or globally,
and the original authored values are preserved in the registry so a single call restores the
pre-tuning baseline.

### What it covers

| Feature | Description |
|---|---|
| `TuningRegistry` | Per-param read/write delegate store with change queue |
| `TuningConsoleGizmo` | ImGui window surfaced via `Tools > AI Tuning Console` |
| `UtilityTuningBinder` | Registers consideration params for one decision into the registry |
| Snapshot / restore | Default values captured at registration; `RevertGroup`/`RevertAll` re-enqueues |
| Piecewise translate-on-apply | Curve diffs applied as offset rather than absolute replace |

---

## Architecture

```
+-----------------------------------------------------+
|          TuningConsoleGizmo  (IStatefulGizmo)       |
|            menu: Tools > AI Tuning Console          |
|                                                     |
|  StructInspector panel   <--> TuningRegistry        |
|    OnStructUpdate(json)       .Apply(key, float)    |
|    TryApplyCurveProperty      .ApplyCurve(key, ..)  |
|                                                     |
|  OpenForGroup(prefix)  <-- UtilityDecisionOverlaySource |
+-----------------------------------------------------+
                    |
                    v
              TuningRegistry.BeginFrame()
                 drains change queue
                 calls Write delegates in place
```

---

## TuningKey

Value-type wrapper for a tunable parameter name. Identity is the FNV-1a-32 hash of the string
name; equality and comparison delegate to the hash value.

```csharp
TuningKey key = new TuningKey("CombatPosture.AdvanceAndAttack.HealthFraction.Weight");
// key.Id        -- uint  FNV-1a-32 hash
// key.Name      -- string  original name string
```

---

## Tunable and CurveTunable

### Tunable (float parameter)

```csharp
public sealed class Tunable
{
    TuningKey  Key;
    TunableKind Kind;         // Weight, Slope, Exponent, XShift, YShift
    float      Min;
    float      Max;
    float      Default;       // captured from initial Read() at registration time
    TunableScope Scope;       // Session (cleared on entity change) or Persistent
    string?    Owner;         // display-name of the decision that owns this param
    Func<float>   Read;       // reads current runtime value
    Action<float> Write;      // writes to runtime value
}
```

### CurveTunable (UtilityCurve parameter)

```csharp
public sealed class CurveTunable
{
    TuningKey          Key;
    UtilityCurve       DefaultCurve;
    Func<UtilityCurve>   ReadCurve;
    Action<UtilityCurve> WriteCurve;
}
```

---

## TuningRegistry

Thread-safe registry keyed by `TuningKey`. All writes are queued and applied on
`BeginFrame()` (simulation main-thread), so tuning is never concurrent with scoring.

### Registration

```csharp
// Register a float parameter:
registry.Register(
    key:      new TuningKey("CombatPosture.Flee.HealthFraction.Weight"),
    kind:     TunableKind.Weight,
    min:      0f, max: 1f,
    read:     () => consideration.Weight,
    write:    v  => consideration.Weight = v,
    scope:    TunableScope.Persistent,
    owner:    "CombatPosture");

// Register a curve parameter:
registry.RegisterCurve(
    key:       new TuningKey("CombatPosture.Flee.HealthFraction.Curve"),
    readCurve:  () => UtilityCurve.FromResponseCurve(consideration.Curve),
    writeCurve: c  => consideration.Curve = c.ToResponseCurve());
```

Default values are captured from the first `read()` / `readCurve()` call at registration time.

### Enqueueing changes

```csharp
// Enqueue a float change (applied next frame):
registry.Apply(key, value: 0.85f);

// Enqueue a curve change:
registry.ApplyCurve(key, newCurve);
```

### Frame drain

```csharp
// Call at the top of each simulation frame (before scoring):
registry.BeginFrame();
```

Drains the change queue; each write delegate is called exactly once per enqueued change. Safe
to call when the queue is empty.

### Lookups

```csharp
bool found = registry.TryGet(key, out Tunable? tunable);
bool found = registry.TryGetCurve(key, out CurveTunable? ct);

// Iterate all tunables for a prefix:
foreach (Tunable t in registry.GetGroup(prefix: "CombatPosture."))
    Console.WriteLine($"{t.Key.Name}  {t.Read():F3}");
```

### Snapshot / restore

```csharp
// Revert all tunables whose key starts with the given prefix:
registry.RevertGroup(prefix: "CombatPosture.");

// Revert every registered tunable:
registry.RevertAll();
```

Both methods re-enqueue the `Default` / `DefaultCurve` values. The changes take effect on the
next `BeginFrame()`.

---

## TuningConsoleGizmo

`IStatefulGizmo`-derived ImGui gizmo. Adds a persistent `Tools > AI Tuning Console` menu item
and renders the StructInspector panel when visible.

### Lifecycle

```
IStatefulGizmo.Update()
  |
  +-- renders main menu item: Tools > AI Tuning Console
  +-- if _isEditing:
        render StructInspector panel
        if _currentGroup has changed: reload tunable list
```

### OpenForGroup

Entry point used by the editor-console bridge (`UtilityDecisionOverlaySource.SelectDecision`):

```csharp
// Open the console pre-filtered to a specific decision:
gizmo.OpenForGroup(prefix: "CombatPosture.");
```

Sets `_isEditing = true` and updates `_currentGroup`; the StructInspector re-populates
automatically on the next frame.

### OnStructUpdate

Callback invoked when the StructInspector JSON editor commits changes:

```csharp
gizmo.OnStructUpdate(json: "{ \"Weight\": 0.9, ... }");
```

The JSON payload is deserialized into a property batch. For each field:
- If the target type is `float` (or `double`): calls `registry.Apply(key, value)`.
- If the target type is `UtilityCurve`: calls `TryApplyCurveProperty`.

### TryApplyCurveProperty

Applies a curve diff as a translate-on-apply operation. Piecewise control points from the
incoming curve are merged with the current value; `MaxPiecewisePoints` clamping is enforced.
For non-piecewise curves all four params (m, k, b, c) are replaced directly.

---

## UtilityTuningBinder

Static helper that registers all tunable parameters for one `UtilityDecisionDef` into a
`TuningRegistry`. Typically called once at startup alongside decision registration.

```csharp
UtilityTuningBinder.RegisterDecision(registry, decisionDef);
```

For each consideration in each option it registers five tunables:

| Suffix | Kind | Range |
|---|---|---|
| `.Weight` | `TunableKind.Weight` | [0, 1] |
| `.Slope` | `TunableKind.Slope` | [-4, 4] |
| `.Exponent` | `TunableKind.Exponent` | [-10, 10] |
| `.XShift` | `TunableKind.XShift` | [-1, 1] |
| `.Curve` | `CurveTunable` | full UtilityCurve |

Key format: `"{DecisionDebugName}.{OptionId}.{InputName}.{Suffix}"`.

---

## ECS Components Required

The tuning system reads directly from `UtilityDecisionDef` structs (via captured lambda
closures over consideration fields). It has no additional ECS component requirements of its
own; it depends on the existing Utility AI ECS components being initialized (IDs 149-151).

---

## Source Structure

```
Hrot.Diagnostics.Tuning/
  CurveTunable.cs          -- CurveTunable, TunableKind enum
  Tunable.cs               -- Tunable, TunableScope enum
  TuningConsoleGizmo.cs    -- IStatefulGizmo implementation
  TuningKey.cs             -- TuningKey value type
  TuningRegistry.cs        -- TuningRegistry (thread-safe, queued writes)
  UtilityTuningBinder.cs   -- static RegisterDecision helper
```

---

## Dependencies

| Assembly | Used for |
|---|---|
| `Fdp.Core` | domain primitives |
| `Fdp.Toolkit.Utility` | `UtilityDecisionDef`, `UtilityCurve`, `ResponseCurve` |
| `Fdp.Presentation` | `IStatefulGizmo` |
| `Fdp.Presentation.Editing` | `StructInspector`, `IImGuiFieldDrawer` |
| `Hrot.Utility.Editor` | `UtilityCurveFieldDrawer` (registered as field drawer for `UtilityCurve`) |
| `ImGuiNET` | ImGui rendering |

---

## Implementation Status

All phases complete (2026-05-30):

| Phase | Content |
|---|---|
| Phase 4 Slice 1 | `TuningKey`, `Tunable`, `TuningRegistry` (Register/Apply/BeginFrame/RevertAll), `TuningConsoleGizmo` (always-on menu, StructInspector panel, `OnStructUpdate` float path) |
| Phase 4 Slice 2 | `CurveTunable`, `RegisterCurve`, `ApplyCurve`, `TryApplyCurveProperty`, `MaxPiecewisePoints` clamping, piecewise translate-on-apply |
| Phase 6 | `UtilityTuningBinder.RegisterDecision`, `TuningConsoleGizmo.OpenForGroup` (editor-console bridge entry point) |
