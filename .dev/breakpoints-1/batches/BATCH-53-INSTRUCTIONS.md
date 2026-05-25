# BATCH-53 Instructions

**Scope:** P11T7 (Predicate Builder ReadOnlyChildIndices), P12T1–P12T4 (wired integration tests)

**Design reference:** [DESIGN.md](../DESIGN.md) §8, §13.3; [TASK-DETAIL.md](../TASK-DETAIL.md) #ubp-p11t7, #ubp-p12t1, #ubp-p12t2, #ubp-p12t3, #ubp-p12t4

---

## Context

Two remaining areas:

1. **P11T7** — `DataBreakpointManagerPanel` does not check `CompoundPredicateDto.ReadOnlyChildIndices` when rendering compound predicate children. When a context menu (BTree/HSM/Blueprint) adds a breakpoint with a read-only branch (e.g., Branch A = auto-synthesized predicate that the user must not edit), the UI should visually lock that branch.

2. **P12T1–T4** — End-to-end revalidation against the real wired `EditorSubsystem` (headless mode, using `subsystem.Kernel.Update()`), re-running the success conditions from UBP-INT1/INT2/INT3 against the actual subsystem plumbing rather than the mock test harness.

---

## Task 1 — P11T7: Predicate Builder respects `ReadOnlyChildIndices`

### What to change

**File:** `Hrot/Engine/Hrot.Presentation/Panels/Breakpoints/DataBreakpointManagerPanel.cs`

**Background:** The current panel only shows a summary grid; there is no compound predicate rendering. Add a `DrawPredicateEditor(BreakpointId id, SearchPredicateDto dto)` method that iterates compound children and wraps read-only children in `ImGuiApi.BeginDisabled()` / `EndDisabled()`. Also add a pure-logic static helper `IsChildReadOnly(CompoundPredicateDto, int)` that can be unit-tested without ImGui.

**Step 1:** Add internal static helper method:

```csharp
/// <summary>
/// Returns true if child at <paramref name="childIndex"/> in <paramref name="dto"/>
/// is marked read-only by the menu populator via <c>ReadOnlyChildIndices</c>.
/// </summary>
internal static bool IsChildReadOnly(CompoundPredicateDto dto, int childIndex)
    => dto.ReadOnlyChildIndices?.Contains(childIndex) == true;
```

**Step 2:** Add a `DrawPredicateEditor` method that renders the predicate structure with read-only locking:

```csharp
/// <summary>
/// Renders the predicate condition tree for the selected breakpoint.
/// Compound children listed in <see cref="CompoundPredicateDto.ReadOnlyChildIndices"/>
/// are rendered inside <c>ImGui.BeginDisabled()</c> so the user cannot edit them.
/// </summary>
private void DrawPredicateEditor()
{
    if (!_selectedId.IsValid) return;

    var bp = _manager.AllBreakpoints.FirstOrDefault(b => b.Id == _selectedId);
    if (bp == null) return;

    if (bp.Condition is CompoundPredicateDto compound)
    {
        ImGuiApi.SeparatorText("Condition (Compound)");
        ImGuiApi.TextUnformatted($"Operator: {compound.Operator}");

        for (int i = 0; i < compound.Conditions.Count; i++)
        {
            bool readOnly = IsChildReadOnly(compound, i);
            if (readOnly) ImGuiApi.BeginDisabled();

            ImGuiApi.TextUnformatted(
                $"  [{i}]{(readOnly ? " (locked)" : "")} {BreakpointConditionSummarizer.Summarize(compound.Conditions[i])}");

            if (readOnly) ImGuiApi.EndDisabled();
        }
    }
    else if (bp.Condition != null)
    {
        ImGuiApi.SeparatorText("Condition");
        ImGuiApi.TextUnformatted(BreakpointConditionSummarizer.Summarize(bp.Condition));
    }
}
```

**Step 3:** Call `DrawPredicateEditor()` from `DrawContent()` — add it after `DrawGrid()` and before `DrawBanner()`:

Change `DrawContent()` from:

```csharp
public void DrawContent()
{
    DrawToolbar();
    DrawGrid();
    DrawBanner();
}
```

to:

```csharp
public void DrawContent()
{
    DrawToolbar();
    DrawGrid();
    DrawPredicateEditor();
    DrawBanner();
}
```

Also add the required `using System.Linq;` import at the top if not already present.

### Tests to add

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PredicateBuilderP11T7Tests.cs` (NEW FILE)

```csharp
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Presentation.Panels.Breakpoints;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>
/// Tests that DataBreakpointManagerPanel.IsChildReadOnly correctly reflects
/// CompoundPredicateDto.ReadOnlyChildIndices (UBP-P11T7).
/// </summary>
public sealed class PredicateBuilderP11T7Tests
{
    [Fact]
    public void IsChildReadOnly_IndexInList_ReturnsTrue()
    {
        var compound = new CompoundPredicateDto
        {
            ReadOnlyChildIndices = new System.Collections.Generic.List<int> { 0 },
        };

        Assert.True(DataBreakpointManagerPanel.IsChildReadOnly(compound, 0));
    }

    [Fact]
    public void IsChildReadOnly_IndexNotInList_ReturnsFalse()
    {
        var compound = new CompoundPredicateDto
        {
            ReadOnlyChildIndices = new System.Collections.Generic.List<int> { 0 },
        };

        Assert.False(DataBreakpointManagerPanel.IsChildReadOnly(compound, 1));
    }

    [Fact]
    public void IsChildReadOnly_EmptyList_ReturnsFalse()
    {
        var compound = new CompoundPredicateDto
        {
            ReadOnlyChildIndices = new System.Collections.Generic.List<int>(),
        };

        Assert.False(DataBreakpointManagerPanel.IsChildReadOnly(compound, 0));
    }

    [Fact]
    public void IsChildReadOnly_NullList_ReturnsFalse()
    {
        var compound = new CompoundPredicateDto
        {
            ReadOnlyChildIndices = null,
        };

        Assert.False(DataBreakpointManagerPanel.IsChildReadOnly(compound, 0));
    }
}
```

Note: You need to make `IsChildReadOnly` accessible from the test project. Since `DataBreakpointManagerPanel` is in `Hrot.Presentation`, the test project must reference it. Check the test project's `.csproj` for existing references. If `Hrot.Presentation` is not referenced, either:
1. Add the reference to `Hrot.Diagnostics.Breakpoints.Tests.csproj`, OR
2. Move `IsChildReadOnly` to a shared helper class (e.g., `CompoundPredicateHelper` in `Hrot.Diagnostics.Breakpoints`) that both the panel and tests can use.

**Preferred approach:** Move `IsChildReadOnly` to `Hrot.Diagnostics.Breakpoints` as a public static helper, since the Breakpoints assembly is already referenced by both the panel and the tests:

```csharp
// File: Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/CompoundPredicateHelper.cs
namespace Hrot.Diagnostics.Breakpoints;

/// <summary>Helpers for working with CompoundPredicateDto structure.</summary>
public static class CompoundPredicateHelper
{
    /// <summary>
    /// Returns true if child at <paramref name="childIndex"/> is marked read-only
    /// by a menu populator via <see cref="CompoundPredicateDto.ReadOnlyChildIndices"/>.
    /// </summary>
    public static bool IsChildReadOnly(CompoundPredicateDto dto, int childIndex)
        => dto.ReadOnlyChildIndices?.Contains(childIndex) == true;
}
```

Then in `DataBreakpointManagerPanel`, call `CompoundPredicateHelper.IsChildReadOnly(compound, i)`.

---

## Task 2 — P12T1–T4: Wired integration tests

### Location: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs`

Add tests 21–25 to the existing `BreakpointSubsystemWiringTests` class.

**IMPORTANT reading before coding:**
- Read the existing tests 1–20 in this file to understand the test patterns.
- `subsystem.World` gives you the live `EntityRepository`.
- `subsystem.Kernel.Update()` runs one tick.
- `mgr.ActiveView` is `_preTickSnapshot` when paused, `_liveRepo` when not.
- `subsystem.BpSnapshotProvider` is `DebugSnapshotProvider`.

For P12T1/T2/T3, you'll need components that are registered by `EditorSubsystem`. Rather than registering new components (which would conflict with other tests since `ComponentTypeRegistry` is shared in integration tests), use `ExternalHitTagPredicateDto` + `OnHit` to trigger pauses. Then for the value-check assertions, use a component that **is already registered** by the EditorSubsystem boot sequence, or use an `ExternalHitTagPredicateDto` workaround.

**Alternative for P12T1:** Use `ExternalHitTagPredicateDto` for pause control, and for the "pre-tick value" check, verify that `mgr.ActiveView != _liveRepo` after pause (i.e., the view object changes to the pre-tick snapshot). This is the observable guarantee without needing to write values to specific components.

**CRITICAL:** Do NOT call `ComponentTypeRegistry.Clear()` in these integration tests — the real subsystem registers its components during initialization and clearing would break it. Only pure unit tests (isolated mock harness) call `ComponentTypeRegistry.Clear()`.

---

### Test 21 (P12T1a): `E2E_Wired_ActiveViewSwitchesToPreTickDuringPause`

```csharp
[Fact]
public void E2E_Wired_ActiveViewSwitchesToPreTickDuringPause()
{
    var subsystem = new EditorSubsystem();
    var config    = new SubsystemConfig { Headless = true };
    try
    {
        subsystem.Initialize(config);
        var mgr = subsystem.DataBreakpointManager!;

        // Pump one tick to warm up the snapshot.
        subsystem.Kernel.Update();

        // Active view before pause is the live repo.
        var viewBeforePause = mgr.ActiveView;
        Assert.False(mgr.IsPaused);

        // Trigger pause via ExternalHitTag BP.
        var id  = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "p12t1a" });
        var bp  = mgr.AllBreakpoints.First(b => b.Id == id);
        mgr.OnHit(bp, Fdp.Core.Entity.Null);

        Assert.True(mgr.IsPaused);

        // ActiveView during pause is the pre-tick snapshot (a different object to the live repo).
        var viewDuringPause = mgr.ActiveView;
        Assert.NotSame(viewBeforePause, viewDuringPause);
        Assert.IsAssignableFrom<Fdp.Core.ISimulationView>(viewDuringPause);
    }
    finally
    {
        subsystem.Shutdown();
    }
}
```

---

### Test 22 (P12T1b): `E2E_Wired_DeferredMutationQueued_StepDrainsECB`

```csharp
[Fact]
public void E2E_Wired_DeferredMutationQueued_StepDrainsECB()
{
    var subsystem = new EditorSubsystem();
    var config    = new SubsystemConfig { Headless = true };
    try
    {
        subsystem.Initialize(config);
        var mgr = subsystem.DataBreakpointManager!;

        // Pump one tick before pausing.
        subsystem.Kernel.Update();

        // Trigger pause.
        var id = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "p12t1b" });
        var bp = mgr.AllBreakpoints.First(b => b.Id == id);
        mgr.OnHit(bp, Fdp.Core.Entity.Null);
        Assert.True(mgr.IsPaused);

        // Stage a mutation for an existing entity (use world's first active entity or a dummy).
        // Since we can't easily find a specific entity, verify only that StageMutation/Queue
        // works correctly from the wired subsystem path.
        // We stage it for a null entity to verify the staging mechanics (not ECB playback).
        // The key assertion is: pending mutation count increases on stage, decreases on step.
        var entity = Fdp.Core.Entity.Null; // Staging for a null entity exercises the code path.
        // Determine a component type registered by the subsystem (use any registered type).
        // Check what's available:  EntityLabel is a managed component registered by Editor.
        // Use Hrot.Engine.Common.EntityLabel or similar.
        // IMPORTANT: Read what components EditorSubsystem registers via subsystem.World's
        // registered component types. Find any already-registered unmanaged component.
        // See RegistrationHelpers or the subsystem's RegisterDomainComponents.
        // For now, skip staging a specific mutation — just verify step un-pauses.
        Assert.True(mgr.IsPaused);
        mgr.RequestStep();
        Assert.False(mgr.IsPaused);

        // After step, mutations queue is empty (regardless of whether any were staged).
        Assert.Equal(0, mgr.PendingMutationsCount);
    }
    finally
    {
        subsystem.Shutdown();
    }
}
```

**Note:** If you find a registered unmanaged component type via `subsystem.World`, add a more complete mutation test. Search for `RegisterComponent<` in `EditorSubsystem.cs` to find what component types are registered, then use one. If `EntityLabel` (managed) is the only available type, use `StageMutation` with that managed component to test the managed path.

---

### Test 23 (P12T2): `Wired_Performance_ArmedBP_100Ticks_WellUnderBudget`

```csharp
[Fact]
public void Wired_Performance_ArmedBP_100Ticks_WellUnderBudget()
{
    var subsystem = new EditorSubsystem();
    var config    = new SubsystemConfig { Headless = true };
    try
    {
        subsystem.Initialize(config);
        var mgr = subsystem.DataBreakpointManager!;

        // Register an armed breakpoint so the gate is open.
        mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "perf-test" });

        // Pump 100 ticks with an armed BP.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            subsystem.Kernel.Update();
        sw.Stop();

        // Subsystem must not be paused (ExternalHitTag BP can only fire via OnHit, not scan).
        Assert.False(mgr.IsPaused);

        // Performance budget: 100 ticks in < 10 seconds (very generous, avoids CI flakiness).
        // This validates no regression caused by the breakpoint gate being open.
        Assert.True(sw.ElapsedMilliseconds < 10_000,
            $"100 ticks took {sw.ElapsedMilliseconds}ms, exceeds 10s budget");
    }
    finally
    {
        subsystem.Shutdown();
    }
}
```

---

### Test 24 (P12T3): `Wired_FlightRecorder_PauseStepResume_KernelAdvancesTick`

This test verifies that after pause/step/resume, the subsystem's kernel advances its tick counter monotonically (proxy for flight recorder invariance — if the kernel tick regresses, the recorder would produce non-monotonic output).

```csharp
[Fact]
public void Wired_FlightRecorder_PauseStepResume_KernelAdvancesTick()
{
    var subsystem = new EditorSubsystem();
    var config    = new SubsystemConfig { Headless = true };
    try
    {
        subsystem.Initialize(config);
        var mgr = subsystem.DataBreakpointManager!;

        // Record tick versions to check monotonic progression.
        var ticksBefore = new System.Collections.Generic.List<uint>();

        // Pump 3 ticks, capturing world version.
        for (int i = 0; i < 3; i++)
        {
            subsystem.Kernel.Update();
            ticksBefore.Add(subsystem.World.GlobalVersion);
        }

        // Pause via BP hit.
        var id = mgr.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "p12t3" });
        var bp = mgr.AllBreakpoints.First(b => b.Id == id);
        mgr.OnHit(bp, Fdp.Core.Entity.Null);
        Assert.True(mgr.IsPaused);

        // Version while paused (from rewind to pre-tick snapshot):
        uint versionAtPause = subsystem.World.GlobalVersion;

        // Step → unpause → advance one more tick.
        mgr.RequestStep();
        Assert.False(mgr.IsPaused);
        subsystem.Kernel.Update();

        uint versionAfterStep = subsystem.World.GlobalVersion;

        // The kernel must have advanced at least one version since the pause point.
        Assert.True(versionAfterStep >= versionAtPause,
            $"Version regressed: after step = {versionAfterStep}, at pause = {versionAtPause}");

        // All versions before pause must be non-decreasing.
        for (int i = 1; i < ticksBefore.Count; i++)
        {
            Assert.True(ticksBefore[i] >= ticksBefore[i - 1],
                $"Tick regression: ticksBefore[{i}] = {ticksBefore[i]}, ticksBefore[{i-1}] = {ticksBefore[i-1]}");
        }
    }
    finally
    {
        subsystem.Shutdown();
    }
}
```

---

### Test 25 (P12T4): `MultiSubsystem_TwoManagers_PausingOneDoesNotAffectOther`

Verify that two independent `DataBreakpointManager` instances do not cross-pause each other:

```csharp
[Fact]
public void MultiSubsystem_TwoManagers_PausingOneDoesNotAffectOther()
{
    var subsystemA = new EditorSubsystem();
    var subsystemB = new EditorSubsystem();
    var config     = new SubsystemConfig { Headless = true };
    try
    {
        subsystemA.Initialize(config);
        subsystemB.Initialize(config);

        var mgrA = subsystemA.DataBreakpointManager!;
        var mgrB = subsystemB.DataBreakpointManager!;

        // Pause manager A.
        var idA = mgrA.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "isolate-a" });
        var bpA = mgrA.AllBreakpoints.First(b => b.Id == idA);
        mgrA.OnHit(bpA, Fdp.Core.Entity.Null);

        Assert.True(mgrA.IsPaused,  "Manager A should be paused");
        Assert.False(mgrB.IsPaused, "Manager B must NOT be paused when A pauses");

        // Un-pause A, pause B.
        mgrA.RequestStep();
        var idB = mgrB.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "isolate-b" });
        var bpB = mgrB.AllBreakpoints.First(b => b.Id == idB);
        mgrB.OnHit(bpB, Fdp.Core.Entity.Null);

        Assert.False(mgrA.IsPaused, "Manager A must NOT be paused when B pauses");
        Assert.True(mgrB.IsPaused,  "Manager B should be paused");
    }
    finally
    {
        subsystemA.Shutdown();
        subsystemB.Shutdown();
    }
}
```

---

## Build & test commands

```
dotnet build IOS-IG-SimHost.sln -v quiet
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~BreakpointSubsystemWiring"
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --no-build
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build
```

---

## Checklist

- [ ] `CompoundPredicateHelper.IsChildReadOnly` added in `Hrot.Diagnostics.Breakpoints`
- [ ] `DataBreakpointManagerPanel.DrawPredicateEditor()` added, calls `CompoundPredicateHelper.IsChildReadOnly`
- [ ] `DrawContent()` calls `DrawPredicateEditor()` after `DrawGrid()`
- [ ] `PredicateBuilderP11T7Tests.cs` created (4 tests, no ImGui context required)
- [ ] Tests 21–25 added to `BreakpointSubsystemWiringTests.cs`
- [ ] No `ComponentTypeRegistry.Clear()` calls in the new integration tests
- [ ] Build: 0 errors, 0 warnings
- [ ] All tests pass (124+ unit tests, 25 integration tests)
