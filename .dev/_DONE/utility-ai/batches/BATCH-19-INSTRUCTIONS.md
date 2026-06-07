# BATCH-19 Instructions — P6-03 (editor/console bridge) + P6-04 (snapshot/restore)

**Batch ID:** BATCH-19
**Phase tasks:** TASK-UAI-P6-03, TASK-UAI-P6-04
**Design refs:**
- `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §8.2 (P6-03)
- `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §11 T-5 (P6-04)

---

## Context

BATCH-18 delivered `UtilityCurveFieldEditor`/`UtilityCurveFieldDrawer` (P6-01) and the piecewise
translate-on-apply (P6-02). BATCH-19 closes Phase 6:

- **P6-03** — Editor/console bridge: clicking a decision name in the utility overlay opens the
  tuning console focused on that decision's group (SC-P6-3).
- **P6-04** — Snapshot/restore: `TuningRegistry` captures authored defaults at registration and
  exposes `RevertGroup`/`RevertAll` (SC-P6-4).

---

## MANDATORY READS before writing any code

Read ALL of these in full:

1. `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §8.2 and §11
2. `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/UtilityDecisionOverlaySource.cs`
3. `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/OverlaySourceTests.cs` — full file
4. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Gizmos/TuningConsoleGizmo.cs` — full file
5. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/TuningConsoleGizmoTests.cs` — full file
6. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningRegistry.cs` — full file
7. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Tunable.cs`
8. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/CurveTunable.cs`
9. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/TuningRegistryTests.cs`
10. `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/Hrot.Diagnostics.Overlays.csproj`
11. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/Hrot.Diagnostics.Tuning.Tests.csproj`

---

## Task A — P6-03: Editor/console bridge

SC-P6-3: "Clicking a decision name in the utility overlay opens the tuning console focused on that
decision's group."

The implementation is split across two files: the overlay source (notifier) and the tuning console
gizmo (receiver). They communicate through a callback so no new project reference is needed.

### A.1 Modify UtilityDecisionOverlaySource

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/UtilityDecisionOverlaySource.cs`

Add an optional `Action<string>? onDecisionSelected` parameter to the constructor (after the
existing two parameters, with default `null`). Store it as `_onDecisionSelected`.

Add an `internal` method `SelectDecision(string decisionName)` that fires the callback with the
canonical group prefix:

```csharp
// Invoked when the operator selects a decision name from the overlay.
// Fires onDecisionSelected with "utility.<decisionName>" so callers can open the
// tuning console focused on the matching group.
internal void SelectDecision(string decisionName)
    => _onDecisionSelected?.Invoke("utility." + decisionName);
```

This method is `internal` (accessible to `Hrot.Diagnostics.Overlays.Tests` via the existing
`InternalsVisibleTo`).

Do NOT change any other logic in `UtilityDecisionOverlaySource`. Existing constructor callers that
pass only `repo` and `budget` continue to compile because the new parameter has a default value.

### A.2 Modify TuningConsoleGizmo

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Gizmos/TuningConsoleGizmo.cs`

1. Add a `string? _focusedGroup` field.

2. Add a public read-only property that exposes `_isEditing`:
   ```csharp
   public bool IsEditing => _isEditing;
   ```

3. Add a public read-only property for the focused group:
   ```csharp
   public string? FocusedGroup => _focusedGroup;
   ```

4. Add `OpenForGroup(string groupPrefix)`:
   ```csharp
   // Opens the tuning console and focuses it on the named group.
   // Called by the editor/console bridge (P6-03, SC-P6-3).
   public void OpenForGroup(string groupPrefix)
   {
       _isEditing    = true;
       _focusedGroup = groupPrefix;
   }
   ```

Do NOT change the existing `ToggleEditor`, `OnMenuAction`, `UpdateAndDraw`, or `OnStructUpdate`
methods.

### A.3 Tests for P6-03

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/OverlaySourceTests.cs`

Add 2 new test methods to the existing `OverlaySourceTests` class (do NOT create a new file):

1. **`SelectDecision_Null_Callback_DoesNotThrow`**
   ```csharp
   [Fact]
   public void SelectDecision_NullCallback_DoesNotThrow()
   {
       using var repo = CreateTestRepo();
       var arbiter = new OverlayBudgetArbiter(float.MaxValue);
       var source  = new UtilityDecisionOverlaySource(repo, arbiter); // no callback
       source.SelectDecision("CombatPosture"); // must not throw
   }
   ```

2. **`SelectDecision_InvokesCallback_WithGroupPrefix`** (SC-P6-3 core)
   ```csharp
   [Fact]
   public void SelectDecision_InvokesCallback_WithGroupPrefix()
   {
       using var repo = CreateTestRepo();
       var arbiter    = new OverlayBudgetArbiter(float.MaxValue);
       string? received = null;
       var source = new UtilityDecisionOverlaySource(repo, arbiter,
           onDecisionSelected: g => received = g);

       source.SelectDecision("CombatPosture");

       Assert.Equal("utility.CombatPosture", received);
   }
   ```

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/TuningConsoleGizmoTests.cs`

Add 3 new test methods to the existing `TuningConsoleGizmoTests` class (do NOT create a new file):

1. **`OpenForGroup_SetsIsEditingTrue`**
2. **`OpenForGroup_SetsFocusedGroup`**
3. **`OpenForGroup_OverridesPreviousFocusedGroup`**

```csharp
[Fact]
public void OpenForGroup_SetsIsEditingTrue()
{
    var gizmo = new TuningConsoleGizmo(new TuningRegistry());
    Assert.False(gizmo.IsEditing);
    gizmo.OpenForGroup("utility.CombatPosture");
    Assert.True(gizmo.IsEditing);
}

[Fact]
public void OpenForGroup_SetsFocusedGroup()
{
    var gizmo = new TuningConsoleGizmo(new TuningRegistry());
    gizmo.OpenForGroup("utility.CombatPosture");
    Assert.Equal("utility.CombatPosture", gizmo.FocusedGroup);
}

[Fact]
public void OpenForGroup_OverridesPreviousFocusedGroup()
{
    var gizmo = new TuningConsoleGizmo(new TuningRegistry());
    gizmo.OpenForGroup("utility.Alpha");
    gizmo.OpenForGroup("utility.Beta");
    Assert.Equal("utility.Beta", gizmo.FocusedGroup);
}
```

---

## Task B — P6-04: Snapshot/restore

SC-P6-4: "Revert group restores authored defaults captured at registration."

This requires capturing the default value when a tunable is registered, then exposing
`RevertGroup`/`RevertAll` that re-enqueue the defaults through the existing apply queue.

### B.1 Modify Tunable.cs

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Tunable.cs`

Add a `Default` field after `Write`:

```csharp
// Authored default captured at registration time. Used by TuningRegistry.RevertGroup/RevertAll.
public float Default;
```

No other changes.

### B.2 Modify CurveTunable.cs

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/CurveTunable.cs`

Add a `DefaultCurve` field after `Write`:

```csharp
// Authored default captured at registration time. Used by TuningRegistry.RevertGroup/RevertAll.
public UtilityCurve DefaultCurve;
```

No other changes.

### B.3 Modify TuningRegistry.cs

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningRegistry.cs`

Three changes:

**1. Capture default in `Register`:**

```csharp
public void Register(TuningKey key, Tunable tunable)
{
    tunable.Key     = key;
    tunable.Default = tunable.Read(); // capture authored default
    _tunables[key.Id] = tunable;
}
```

**2. Capture default in `RegisterCurve`:**

```csharp
public void RegisterCurve(TuningKey key, CurveTunable tunable)
{
    tunable.Key          = key;
    tunable.DefaultCurve = tunable.Read(); // capture authored default
    _curveTunables[key.Id] = tunable;
}
```

**3. Add `RevertGroup` and `RevertAll`:**

```csharp
// Enqueue restore of all float and curve tunables whose group prefix matches groupPrefix.
// Changes are applied at next BeginFrame (frame-top discipline preserved).
public void RevertGroup(string groupPrefix)
{
    lock (_queueLock)
    {
        foreach (var t in _tunables.Values)
        {
            if (GetGroupPrefix(t.Key.Name) == groupPrefix)
                _applyQueue.Enqueue((t.Key.Id, t.Default));
        }
        foreach (var ct in _curveTunables.Values)
        {
            if (GetGroupPrefix(ct.Key.Name) == groupPrefix)
                _curveApplyQueue.Enqueue((ct.Key.Id, ct.DefaultCurve));
        }
    }
}

// Enqueue restore of ALL registered float and curve tunables to their authored defaults.
public void RevertAll()
{
    lock (_queueLock)
    {
        foreach (var t in _tunables.Values)
            _applyQueue.Enqueue((t.Key.Id, t.Default));
        foreach (var ct in _curveTunables.Values)
            _curveApplyQueue.Enqueue((ct.Key.Id, ct.DefaultCurve));
    }
}
```

Note: both methods lock once and batch all enqueues under the same lock to keep the operation
atomic from other threads' perspectives. Do not copy-then-enqueue outside the lock.

### B.4 Tests for P6-04

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/SnapshotRestoreTests.cs`

Required tests (minimum 5):

1. **`DefaultCapturedAtRegistration`**
   Register a tunable with initial value 3.0f. Assert `tunable.Default == 3.0f` after registration
   by reading it back via `registry.TryGet(key, out var t)`.

2. **`RevertGroup_RestoresDefaultValue`** (SC-P6-4 core)
   - Register `utility.Alpha.0.0.weight` with initial value 1.0f.
   - Apply 5.0f via `registry.Apply`, then `BeginFrame` (now 5.0f).
   - Call `registry.RevertGroup("utility.Alpha")`.
   - Call `registry.BeginFrame()`.
   - Assert the consideration weight is back to 1.0f.

3. **`RevertGroup_DoesNotAffectOtherGroup`**
   - Register two tunables in different groups (`utility.Alpha.*`, `utility.Beta.*`).
   - Apply different values to both, BeginFrame.
   - `RevertGroup("utility.Alpha")`, BeginFrame.
   - Assert `utility.Alpha` tunable is restored to default.
   - Assert `utility.Beta` tunable still has its modified value.

4. **`RevertAll_RestoresAllTunables`**
   - Register two float tunables in different groups.
   - Apply different values to both, BeginFrame.
   - `RevertAll()`, BeginFrame.
   - Assert both are back to defaults.

5. **`DefaultCaptured_CurveTunable`**
   - Register a `CurveTunable` where `Read` returns a specific `UtilityCurve`.
   - After `RegisterCurve`, retrieve via `TryGetCurve` and assert `DefaultCurve.Kind` equals the
     initial curve's Kind.

For tests 2 and 3, the simplest approach: use a plain `float` backing field and `Tunable` with
`Read = () => field` and `Write = v => field = v`. After `BeginFrame`, read `field` to verify.

Example helper:
```csharp
private static (TuningRegistry registry, TuningKey key, Func<float> readField)
    RegisterWithField(float initialValue, string keyName = "utility.Alpha.0.0.weight",
                      float min = 0f, float max = 10f)
{
    float field  = initialValue;
    var registry = new TuningRegistry();
    var key      = new TuningKey(keyName);
    registry.Register(key, new Tunable
    {
        Kind  = TuningKind.Float,
        Min   = min,
        Max   = max,
        Scope = TuningScope.Global,
        Owner = TuningOwner.Brain,
        Read  = () => field,
        Write = v => field = v,
    });
    return (registry, key, () => field);
}
```

---

## Build & test

After all changes:

```
dotnet build Hrot/Diagnostics/Hrot.Diagnostics.Overlays/Hrot.Diagnostics.Overlays.csproj
dotnet build Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Hrot.Diagnostics.Tuning.csproj
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/Hrot.Diagnostics.Overlays.Tests.csproj
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/Hrot.Diagnostics.Tuning.Tests.csproj
```

Also verify the existing Hrot.Utility.Editor.Tests still pass:
```
dotnet test Hrot/Editor/Hrot.Utility.Editor.Tests/Hrot.Utility.Editor.Tests.csproj
```

Fix all build errors and test failures before reporting.

Expected additional tests: ~10 new (2 + 3 + 5). All existing tests must continue to pass.

---

## Report format

Write `.dev/utility-ai/reports/BATCH-19-REPORT.md` with:
- Files created/modified
- Build result (0 errors required)
- Test result by project (total passed/failed)
- Any deviations from instructions
