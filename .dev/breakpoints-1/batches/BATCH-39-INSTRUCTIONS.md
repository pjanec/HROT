# BATCH-39 Instructions

**Workstream:** breakpoints-1
**Batch:** BATCH-39
**Previous batch:** BATCH-38 (APPROVED and committed)
**Responsible:** Developer

---

## Context

Read the following documents before writing any code:

- `.dev/breakpoints-1/DESIGN.md` — full design; focus on §7.1, §7.3
- `.dev/breakpoints-1/TASK-DETAIL.md` — focus on `UBP-P3T2` and `UBP-P3T3` sections
- `.dev/breakpoints-1/TASK-TRACKER.md` — current status
- `AGENTS.md` — editing invariants (non-negotiable)

---

## Tasks

### Task 1: UBP-P3T2 — Inspector adapter view repointing

**Design reference:** DESIGN.md §7.1

**Goal:** Prove that when the manager is paused, `manager.ActiveView` (which returns
`_preTickSnapshot`) correctly exposes the pre-tick values, and after a step, `ActiveView`
(which returns `_liveRepo`) exposes the post-tick values. No changes to
`EntityInspectorState` or `SimulationViewAdapter` are needed — those already accept
`ISimulationView` as a parameter. This task is primarily tests.

**Implementation:**

No production code changes required. The existing `IDataBreakpointManager.ActiveView`
property already returns the correct view. The "implementation" of P3T2 is writing tests
that prove this works correctly when wired with `EntityInspectorState`.

But wait — `EntityInspectorState` lives in `Hrot.IG` which is not referenced by
`Hrot.Diagnostics.Breakpoints.Tests`. Therefore the tests use `ISimulationView.GetComponentRO`
directly rather than going through `EntityInspectorState`. This is sufficient: the point of
P3T2 is to validate that `ActiveView` returns the right view at each pause/resume state.

**Tests to add** in new file
`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointInspectorViewTests.cs`:

**`Inspector_DuringPause_ShowsPreTickValues`:**

Setup:
1. Create separate `EntityRepository liveRepo` and `EntityRepository preTickSnapshot`.
2. Create entity in `liveRepo`; call `ComponentTypeRegistry.Register<TestHealthP3>(213)`.
3. Add `TestHealthP3 { Current = 100 }` to the entity in `liveRepo`.
4. `preTickSnapshot.SyncFrom(liveRepo)` — both repos now have Current=100.
5. Mutate `liveRepo` (call `liveRepo.Tick()` first, then `ref var h = ref liveRepo.GetComponentRW<TestHealthP3>(entity); h.Current = 50;`).
   Now liveRepo has Current=50, preTickSnapshot still has Current=100.
6. Create `DataBreakpointManager(liveRepo, preTickSnapshot, snapshotProvider, tc)`.
7. Add a breakpoint via `manager.Add(new Breakpoint { Enabled=true, OccurrenceThreshold=1, DisplayName="P3T2" })`.
8. Retrieve the registered breakpoint from `manager.AllBreakpoints.First(...)`.
9. Call `manager.OnHit(registeredBp, entity)` directly — this triggers the pause.

Assertions:
- `Assert.True(manager.IsPaused)`
- `var view = manager.ActiveView;`
- `Assert.Equal(100, view.GetComponentRO<TestHealthP3>(entity).Current)` — pre-tick value


**`Inspector_AfterStep_ShowsPostTickValues`:**

Same setup as above (both tests should share a private setup helper method to avoid
code duplication). After calling `manager.OnHit(...)` to pause:

1. `manager.RequestStep()` — restores liveRepo to postTickSnapshot (Current=50), sets IsPaused=false.

Assertions:
- `Assert.False(manager.IsPaused)`
- `var view = manager.ActiveView;`
- `Assert.Equal(50, view.GetComponentRO<TestHealthP3>(entity).Current)` — post-tick value

**Important constraint:** Test class must be `[Collection("ComponentRegistry")]` because
it calls `ComponentTypeRegistry.Register<TestHealthP3>(213)`.
`TestHealthP3` is already declared as `[ComponentId(213)] struct TestHealthP3 { public int Current; }`
in `DataBreakpointGizmoViewTests.cs` in the same project. Since both files are in the same
test project and assembly, you MUST NOT redeclare it. Import (use) the existing struct.

---

### Task 2: UBP-P3T3 — Temporal status banner

**Design reference:** DESIGN.md §7.3

**Goal:** Create a small state+panel pair that renders when `IDataBreakpointManager.IsPaused == true`,
showing the paused tick and the pending mutation count.

**Production code changes:**

**A. Modify `StageMutation` in `DataBreakpointManager.cs` — change from throwing to counting:**

Current stub in `DataBreakpointManager` (find it via the interface):
```csharp
public void StageMutation(Entity entity, Type componentType, object componentValue)
    => throw new NotImplementedException("P4 stub");
```

Change to:
```csharp
private int _pendingMutationsCount;

public void StageMutation(Entity entity, Type componentType, object componentValue)
{
    // P3T3: minimal stub that counts staged mutations.
    // P4T1 will add PendingDebugMutation classification and queue logic.
    _pendingMutationsCount++;
}
```

Also update `PendingMutationsCount` in `DataBreakpointManager.cs` from:
```csharp
public int PendingMutationsCount => 0; // P4 stub
```
to:
```csharp
public int PendingMutationsCount => _pendingMutationsCount;
```

And reset `_pendingMutationsCount` to 0 in `RequestStep` and `RequestContinue` (after the liveRepo restore, before the timeController call — same position as `_pausedTick = 0`).

**B. Create `TemporalStatusBannerState.cs`** in
`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/TemporalStatusBannerState.cs`:

```csharp
namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Pure-logic state for the temporal status banner.
/// Extracted from <see cref="TemporalStatusBannerPanel"/> so the rendering
/// logic can be tested without an ImGui context.
/// Call <see cref="Refresh"/> once per frame; read <see cref="ShouldRender"/>
/// and <see cref="StatusText"/> to drive the ImGui panel.
/// </summary>
public sealed class TemporalStatusBannerState
{
    /// <summary>True when the banner should be visible (manager is paused).</summary>
    public bool ShouldRender { get; private set; }

    /// <summary>
    /// Full status text to display when <see cref="ShouldRender"/> is true.
    /// Empty when <see cref="ShouldRender"/> is false.
    /// </summary>
    public string StatusText { get; private set; } = string.Empty;

    /// <summary>
    /// Updates the banner state from the current manager state.
    /// Call once per frame before rendering.
    /// </summary>
    public void Refresh(IDataBreakpointManager manager)
    {
        ShouldRender = manager.IsPaused;
        if (ShouldRender)
            StatusText = $"PAUSED -- Pre-Execution State (Tick {manager.PausedTick})" +
                         $"  [ {manager.PendingMutationsCount} Pending Mutations ]";
        else
            StatusText = string.Empty;
    }
}
```

**C. Create `TemporalStatusBannerPanel.cs`** in
`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/TemporalStatusBannerPanel.cs`:

```csharp
using ImGuiNET;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Small global ImGui overlay rendered when the simulation is paused by a
/// data breakpoint. Displays the paused tick and the number of pending mutations.
/// Call <see cref="Draw"/> each frame inside an rlImGui Begin/End block.
/// </summary>
public sealed class TemporalStatusBannerPanel
{
    private readonly TemporalStatusBannerState _state;

    public TemporalStatusBannerPanel(TemporalStatusBannerState state)
        => _state = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>
    /// Renders the banner if the manager is paused.
    /// Must be called inside a valid ImGui frame (between rlImGui.Begin/End).
    /// </summary>
    public void Draw()
    {
        if (!_state.ShouldRender) return;

        ImGui.SetNextWindowPos(new System.Numerics.Vector2(10f, 10f),
            ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.85f);
        ImGui.Begin("##BreakpointBanner",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoMove);
        ImGui.Text(_state.StatusText);
        ImGui.End();
    }
}
```

**IMPORTANT:** The project `Hrot.Diagnostics.Breakpoints` may not currently reference
`ImGuiNET`. Check the project file. If it does not reference ImGuiNET:
- Do NOT add ImGuiNET to the project.
- Instead, create the panel with a `Draw(Action<string> textRenderer)` signature that
  accepts a delegate instead of calling ImGui directly. This keeps the project headless-safe.
  The tests can pass a capturing lambda.
  
  ```csharp
  public void Draw(Action<string> textRenderer)
  {
      if (!_state.ShouldRender) return;
      textRenderer(_state.StatusText);
  }
  ```

  Callers in UI subsystems (IgApplication, SimHostVisualization) that have ImGuiNET can
  wrap the call with their own ImGui rendering.

Check whether `Hrot.Diagnostics.Breakpoints.csproj` already has ImGuiNET:
```
grep -l "ImGuiNET" Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/Hrot.Diagnostics.Breakpoints.csproj
```

Use the delegate approach if ImGuiNET is not already present. The tests then pass a capturing
lambda.

**Tests to add** in new file
`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TemporalStatusBannerTests.cs`:

**`Banner_HiddenWhenNotPaused`:**

Setup:
1. Create manager via `ManagerFactory.Create()`.
2. Create `TemporalStatusBannerState state = new(); state.Refresh(manager);`

Assertions:
- `Assert.False(state.ShouldRender)`
- `Assert.Equal(string.Empty, state.StatusText)`

**`Banner_ShowsTickAndCount_WhenPaused`:**

Setup:
1. Create manager, liveRepo, snapshotProvider, tc via `ManagerFactory.Create()`.
2. Create entity, register component, set up breakpoint (same pattern as P3T2 tests).
3. Pause the manager: call `manager.OnHit(registeredBp, entity)`.
4. Stage 2 mutations: call `manager.StageMutation(entity, typeof(object), new object())` twice.
5. Create and refresh state: `var state = new TemporalStatusBannerState(); state.Refresh(manager);`

Assertions:
- `Assert.True(state.ShouldRender)`
- `Assert.Contains("Tick ", state.StatusText)` — any tick value is fine
- `Assert.Contains("2 Pending Mutations", state.StatusText)`

**Note on PausedTick value:** `PausedTick` is set to `_preTickSnapshot.GlobalVersion` at the
time `OnHit` fires. In the test, this will be 0 (since `preTickSnapshot` was never ticked).
That is fine — the test only checks that the tick value appears in the text, not its exact value.
The assertion `Assert.Contains("Tick ", state.StatusText)` (note trailing space) is sufficient.

If you want to test a specific tick value, call `liveRepo.Tick()` N times on both repos before
syncing, but this is not required.

---

## File Checklist

**Existing files to modify:**
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`
  — `StageMutation` stub: change from throw to counter increment
  — `PendingMutationsCount`: change from `=> 0` to `=> _pendingMutationsCount`
  — `_pendingMutationsCount` field: add
  — `RequestStep` and `RequestContinue`: reset `_pendingMutationsCount = 0`

**New files to create:**
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/TemporalStatusBannerState.cs`
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/TemporalStatusBannerPanel.cs`
  (or `TemporalStatusBannerPanel.cs` with delegate signature if no ImGuiNET)
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointInspectorViewTests.cs`
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TemporalStatusBannerTests.cs`

---

## Build and Test Requirements

1. Run: `dotnet build IOS-IG-SimHost.sln -c Debug` from `d:\Work\IOS-IG-SimHost-FDP-2\`
   - Must complete with 0 errors, 0 warnings (TreatWarningsAsErrors is active).

2. Run the test project:
   ```
   dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -c Debug
   ```
   - Must pass ALL tests (34 existing + 4 new = 38 total minimum).
   - If any existing test breaks, fix it before submitting.

---

## Report

Write the report to:
`.dev/breakpoints-1/reports/BATCH-39-REPORT.md`

The report must include:
- List of all files modified/created
- All test names and pass/fail
- Build result (0 errors)
- The exact `StageMutation` and `PendingMutationsCount` implementations used
- Confirmation of whether ImGuiNET was or was not referenced in the breakpoints project,
  and which `TemporalStatusBannerPanel` variant was implemented (direct ImGui vs. delegate)
- Any issues encountered and how they were resolved

---

## Key Rules (from AGENTS.md)
- Do NOT use Unicode characters in new comments or string literals
- Do NOT rewrite existing comments unless they are wrong
- TreatWarningsAsErrors — fix every warning
- Make sure the solution compiles before finishing
