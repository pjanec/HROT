# BATCH-06 Implementation Instructions
**Tasks**: T-RMF-26, T-RMF-27  
**Phase**: Phase 6 — Verification and Tests  
**Agent**: Claude Sonnet 4.6

---

## Overview

Add and update tests to verify that **all four togglable groups** are correctly disabled/re-enabled during replay transitions.

The four groups are:
- `TogglableInputGroup` (`Fdp.ModuleHost.Scheduling`)
- `TogglableSimulationGroup` (`Fdp.ModuleHost.Scheduling`)
- `TogglablePostSimulationGroup` (`Fdp.ModuleHost.Scheduling`)
- `NetworkLifecycleSystemGroup` (`Fdp.ModuleHost.Scheduling`)

The handler `ReferenceReplayLoadHandler.Commit()` toggles all four via `SetSystemsEnabled(bool)`. Current tests only check `simGroup` and `lifecycleGroup` — the `inputGroup` and `postSimGroup` were passed as `null`.

---

## T-RMF-26: New test cases in `ReplayLoadClusterOpHandlerTests.cs`

**File**: `Hrot/Subsystems/Hrot.SimHost.Tests/ReplayLoadClusterOpHandlerTests.cs`

Read the full file first. Add three new `[Fact]` test methods after the two existing ones (`FullReplayTransition_DisablesSimGroups` and `FinalizeReplay_ReEnablesSimGroups`).

### New Test 1: `PrepareReplay_DisablesAllFourGroups`

This test verifies that after `PrepareReplay` + `Commit`, ALL four groups are disabled.

Pattern is the same as the existing `FullReplayTransition_DisablesSimGroups` test. The difference is:
- Pass `inputGroup: inputGroup` and `postSimGroup: postSimGroup` (non-null instances)
- Assert all four groups are disabled after commit

```csharp
[Fact(Timeout = 20_000)]
public async Task PrepareReplay_DisablesAllFourGroups()
{
    // ── Step 1: create a recording ──
    var exerciseId = Guid.NewGuid();
    var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

    using var cts      = new CancellationTokenSource();
    var       loopTask = RunKernelLoop(_kernel, cts.Token);

    _world.SetSingletonUnmanaged(new GlobalTime
    {
        DeltaTime      = 0.016f,
        TimeScale      = 1.0f,
        TotalWallTicks = 10_000L,
    });
    await controller.PrepareRecordingAsync(exerciseId, _tempDir);
    for (int i = 0; i < 5; i++) { await Task.Delay(20); }
    await controller.FinalizeRecordingAsync();

    // ── Step 2: build handler with all four groups ──
    var inputGroup     = new TogglableInputGroup("test-input");
    var simGroup       = new TogglableSimulationGroup("test-sim");
    var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
    var entityMap      = new NetworkEntityMap();
    var ghostSys       = new GhostCreationSystem(entityMap);
    var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

    var handler = new ReferenceReplayLoadHandler(
        controller,
        inputGroup:    inputGroup,
        simGroup:      simGroup,
        postSimGroup:  postSimGroup,
        lifecycleGroup,
        bypass => ghostSys.BypassLifecycle = bypass,
        storageDirectory: _tempDir);

    // ── Step 3: PrepareReplay → Commit ──
    var cmd = new ExecuteNodeOpIntent
    {
        TransactionId = Guid.NewGuid(),
        TargetNodeId  = 0,
        Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
        DomainPayload = exerciseId,
    };
    await handler.PrepareAsync(cmd, CancellationToken.None);
    handler.Commit(cmd, repo: null);

    cts.Cancel();
    await loopTask;

    // ── Step 4: all four groups must be disabled ──
    Assert.False(inputGroup.Enabled,
        "TogglableInputGroup.Enabled must be false during RunningReplay.");
    Assert.False(simGroup.Enabled,
        "TogglableSimulationGroup.Enabled must be false during RunningReplay.");
    Assert.False(postSimGroup.Enabled,
        "TogglablePostSimulationGroup.Enabled must be false during RunningReplay.");
    Assert.False(lifecycleGroup.Enabled,
        "NetworkLifecycleSystemGroup.Enabled must be false during RunningReplay.");
    Assert.True(ghostSys.BypassLifecycle,
        "GhostCreationSystem.BypassLifecycle must be true during RunningReplay.");
}
```

### New Test 2: `FinalizeReplay_ReEnablesAllFourGroups`

This test verifies all four groups are re-enabled after `FinalizeReplay` + `Commit`.

Pattern is the same as `FinalizeReplay_ReEnablesSimGroups`. Difference: pass all four groups, assert all four are re-enabled.

```csharp
[Fact(Timeout = 20_000)]
public async Task FinalizeReplay_ReEnablesAllFourGroups()
{
    // ── Step 1: create a recording ──
    var exerciseId = Guid.NewGuid();
    var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

    using var cts      = new CancellationTokenSource();
    var       loopTask = RunKernelLoop(_kernel, cts.Token);

    _world.SetSingletonUnmanaged(new GlobalTime
    {
        DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 1_000L,
    });
    await controller.PrepareRecordingAsync(exerciseId, _tempDir);
    for (int i = 0; i < 5; i++) { await Task.Delay(20); }
    await controller.FinalizeRecordingAsync();

    // ── Step 2: build handler with all four groups, run PrepareReplay ──
    var inputGroup     = new TogglableInputGroup("test-input");
    var simGroup       = new TogglableSimulationGroup("test-sim");
    var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
    var entityMap      = new NetworkEntityMap();
    var ghostSys       = new GhostCreationSystem(entityMap);
    var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

    var handler = new ReferenceReplayLoadHandler(
        controller,
        inputGroup:    inputGroup,
        simGroup:      simGroup,
        postSimGroup:  postSimGroup,
        lifecycleGroup,
        bypass => ghostSys.BypassLifecycle = bypass,
        storageDirectory: _tempDir);

    var prepareCmd = new ExecuteNodeOpIntent
    {
        TransactionId = Guid.NewGuid(),
        TargetNodeId  = 0,
        Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
        DomainPayload = exerciseId,
    };
    await handler.PrepareAsync(prepareCmd, CancellationToken.None);
    handler.Commit(prepareCmd, repo: null);

    // All four groups are disabled.
    Assert.False(inputGroup.Enabled);
    Assert.False(simGroup.Enabled);
    Assert.False(postSimGroup.Enabled);
    Assert.False(lifecycleGroup.Enabled);

    // ── Step 3: FinalizeReplay → Commit ──
    var finalizeCmd = new ExecuteNodeOpIntent
    {
        TransactionId = Guid.NewGuid(),
        TargetNodeId  = 0,
        Operation     = Fdp.Toolkit.Orchestration.NodeOpType.FinalizeReplay,
        DomainPayload = null,
    };
    await handler.PrepareAsync(finalizeCmd, CancellationToken.None);
    handler.Commit(finalizeCmd, repo: null);

    cts.Cancel();
    await loopTask;

    // ── Step 4: all four groups must be re-enabled ──
    Assert.True(inputGroup.Enabled,
        "TogglableInputGroup.Enabled must be re-enabled after FinalizeReplay.");
    Assert.True(simGroup.Enabled,
        "TogglableSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
    Assert.True(postSimGroup.Enabled,
        "TogglablePostSimulationGroup.Enabled must be re-enabled after FinalizeReplay.");
    Assert.True(lifecycleGroup.Enabled,
        "NetworkLifecycleSystemGroup.Enabled must be re-enabled after FinalizeReplay.");
    Assert.False(ghostSys.BypassLifecycle,
        "GhostCreationSystem.BypassLifecycle must be reset to false after FinalizeReplay.");
}
```

### New Test 3: `PrepareLive_ReEnablesAllFourGroups`

This test verifies all four groups are re-enabled after `PrepareLive` (Live-from-Replay branch) + `Commit`.

Pattern is the same as the `AfterBranch_RecordingModuleIsInstalled` test in `LiveFromReplayTests.cs`, but focused on group state.

```csharp
[Fact(Timeout = 20_000)]
public async Task PrepareLive_ReEnablesAllFourGroups()
{
    // ── Step 1: create a recording ──
    var exerciseId = Guid.NewGuid();
    var controller = new EcsRecordReplayController(_kernel, nodeId: 1, _world);

    using var cts      = new CancellationTokenSource();
    var       loopTask = RunKernelLoop(_kernel, cts.Token);

    _world.SetSingletonUnmanaged(new GlobalTime
    {
        DeltaTime = 0.016f, TimeScale = 1.0f, TotalWallTicks = 5_000L,
    });
    await controller.PrepareRecordingAsync(exerciseId, _tempDir);
    for (int i = 0; i < 5; i++) { await Task.Delay(20); }
    await controller.FinalizeRecordingAsync();

    // ── Step 2: build handler with all four groups, run PrepareReplay ──
    var inputGroup     = new TogglableInputGroup("test-input");
    var simGroup       = new TogglableSimulationGroup("test-sim");
    var postSimGroup   = new TogglablePostSimulationGroup("test-postsim");
    var entityMap      = new NetworkEntityMap();
    var ghostSys       = new GhostCreationSystem(entityMap);
    var lifecycleGroup = new NetworkLifecycleSystemGroup(ghostSys);

    var handler = new ReferenceReplayLoadHandler(
        controller,
        inputGroup:    inputGroup,
        simGroup:      simGroup,
        postSimGroup:  postSimGroup,
        lifecycleGroup,
        bypass => ghostSys.BypassLifecycle = bypass,
        storageDirectory: _tempDir);

    var prepareCmd = new ExecuteNodeOpIntent
    {
        TransactionId = Guid.NewGuid(),
        TargetNodeId  = 0,
        Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay,
        DomainPayload = exerciseId,
    };
    await handler.PrepareAsync(prepareCmd, CancellationToken.None);
    handler.Commit(prepareCmd, repo: null);

    // All four groups are now disabled.
    Assert.False(inputGroup.Enabled);
    Assert.False(simGroup.Enabled);
    Assert.False(postSimGroup.Enabled);

    // ── Step 3: PrepareLive (Live-from-Replay branch) → Commit ──
    var branchCmd = new ExecuteNodeOpIntent
    {
        TransactionId = Guid.NewGuid(),
        TargetNodeId  = 0,
        Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrepareLive,
        DomainPayload = Guid.NewGuid(),  // new branched exercise ID
    };
    await handler.PrepareAsync(branchCmd, CancellationToken.None);
    handler.Commit(branchCmd, repo: null);

    cts.Cancel();
    await loopTask;

    // ── Step 4: all four groups must be re-enabled ──
    Assert.True(inputGroup.Enabled,
        "TogglableInputGroup.Enabled must be re-enabled after PrepareLive branch.");
    Assert.True(simGroup.Enabled,
        "TogglableSimulationGroup.Enabled must be re-enabled after PrepareLive branch.");
    Assert.True(postSimGroup.Enabled,
        "TogglablePostSimulationGroup.Enabled must be re-enabled after PrepareLive branch.");
    Assert.True(lifecycleGroup.Enabled,
        "NetworkLifecycleSystemGroup.Enabled must be re-enabled after PrepareLive branch.");
    Assert.False(ghostSys.BypassLifecycle,
        "GhostCreationSystem.BypassLifecycle must be reset after PrepareLive branch.");
}
```

### Required usings to add (if not already present)

At the top of `ReplayLoadClusterOpHandlerTests.cs`, ensure:
```csharp
using Fdp.ModuleHost.Scheduling;
```
(Check — this may already be present. If yes, no change needed.)

Also check that `TogglableInputGroup` and `TogglablePostSimulationGroup` are accessible. They live in `Fdp.ModuleHost.Scheduling` namespace.

---

## T-RMF-27: Update existing tests

### Update 1: `ReplayLoadClusterOpHandlerTests.FullReplayTransition_DisablesSimGroups`

**File**: `Hrot/Subsystems/Hrot.SimHost.Tests/ReplayLoadClusterOpHandlerTests.cs`

Read the test. Currently:
- `inputGroup: null` — change to `inputGroup: inputGroup` where `inputGroup = new TogglableInputGroup("test-input");`
- `postSimGroup: null` — change to `postSimGroup: postSimGroup` where `postSimGroup = new TogglablePostSimulationGroup("test-postsim");`
- Add assertions at the end:
  ```csharp
  Assert.False(inputGroup.Enabled,
      "TogglableInputGroup.Enabled must be false during RunningReplay.");
  Assert.False(postSimGroup.Enabled,
      "TogglablePostSimulationGroup.Enabled must be false during RunningReplay.");
  ```

### Update 2: `ReplayLoadClusterOpHandlerTests.FinalizeReplay_ReEnablesSimGroups`

Same changes:
- Pass `inputGroup` and `postSimGroup` (non-null)
- Add assertions both for disabled-after-PrepareReplay and re-enabled-after-FinalizeReplay

### Update 3: `LiveFromReplayTests.AfterBranch_RecordingModuleIsInstalled`

**File**: `Hrot/Subsystems/Hrot.SimHost.Tests/LiveFromReplayTests.cs`

Read the test. It already has `var simGroup = new TogglableSimulationGroup("test");` and asserts simGroup/lifecycleGroup.

Update:
- Add `var inputGroup = new TogglableInputGroup("test-input");`
- Add `var postSimGroup = new TogglablePostSimulationGroup("test-postsim");`
- Change `inputGroup: null` → `inputGroup: inputGroup`
- Change `postSimGroup: null` → `postSimGroup: postSimGroup`
- Add assertion after `Assert.False(simGroup.Enabled, ...)`:
  ```csharp
  Assert.False(inputGroup.Enabled, "TogglableInputGroup must be disabled during replay.");
  Assert.False(postSimGroup.Enabled, "TogglablePostSimulationGroup must be disabled during replay.");
  ```
- Add assertions after the branch commit:
  ```csharp
  Assert.True(inputGroup.Enabled,
      "TogglableInputGroup.Enabled must be true after Live-from-Replay branch Commit.");
  Assert.True(postSimGroup.Enabled,
      "TogglablePostSimulationGroup.Enabled must be true after Live-from-Replay branch Commit.");
  ```

### Update 4: `NodeBootstrapperReplayTests.BuildOrchestration_WithReplayParams_RegistersReplayLoadClusterOpHandler`

**File**: `Hrot/Subsystems/Hrot.SimHost.Tests/NodeBootstrapperReplayTests.cs`

Read the test. It passes `simGroup` and `lifecycleGrp` to `BuildOrchestration` but not `inputGroup` or `postSimGroup`.

Update:
- Add `var inputGroup = new TogglableInputGroup("test-input");`
- Add `var postSimGroup = new TogglablePostSimulationGroup("test-postsim");`
- Add these to the `BuildOrchestration(...)` call:
  ```csharp
  inputGroup:  inputGroup,
  postSimGroup: postSimGroup,
  ```

The assertion in this test only checks `slave.IsHandlerRegistered<ReferenceReplayLoadHandler>()` — no need to add group state assertions here.

---

## Verify TogglableInputGroup and TogglablePostSimulationGroup constructors

Before writing the tests, read these files to confirm the constructor signatures:
- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableInputGroup.cs`  
- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs`

They should accept a `string name` parameter just like `TogglableSimulationGroup("test")`.

---

## Build and Test

After all changes:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Build
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"

# SimHost tests
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2
```

**Expected**: Build 0 errors. Test count increases by 3 (new tests). All pass.

---

## Commit after tests pass

No FDP submodule changes in this batch. Only outer repo:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
git add -A
git commit -m "T-RMF-26/27: Phase 6 - add replay isolation tests for all four togglable groups"
```
