# BATCH-47: UBP-INT1 + INT2 + INT3 — Integration Tests

**Batch Number:** BATCH-47  
**Tasks:** UBP-INT1 (E2E flow), UBP-INT2 (Performance budget), UBP-INT3 (Recorder invariance)  
**Design Reference:** `.dev/breakpoints-1/DESIGN.md`, `.dev/breakpoints-1/TASK-DETAIL.md §INT`  
**Test project:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`  
**Prior test count:** 97

---

## Context: read before implementing

1. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointSystem.cs` — full file
2. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` — lines 1-120 and 450-500
3. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs` — lines 56-90 (ManagerFactory), lines 510-555 (RequestStep test showing how to pre-populate repos)
4. `FDP/Engine/Fdp.Core/FlightRecorder/FrameOuterHeader.cs` — binary format struct
5. `FDP/Engine/Fdp.Core/FlightRecorder/RecordingGlobalHeader.cs` — global header size
6. `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs` — module with RegisterSystems
7. `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs` — config object
8. `FDP/Engine/Fdp.ModuleHost/Abstractions/ISystemRegistry.cs` — interface for capturing systems
9. `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs` — to verify SyncFrom does NOT copy GlobalVersion
10. Grep for `[ComponentId(2` in `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/` to avoid ID conflicts

---

## What to build

Create one new test file:
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/IntegrationTests.cs`

No production code changes needed.

---

## Test file structure

```
// File-scoped namespace: namespace Hrot.Diagnostics.Breakpoints.Tests;

// Test-only components (file-scoped structs at namespace level)
[ComponentId(240)]
file struct E2EHealthComp { public int Value; }

[ComponentId(241)]
file struct E2EAmmoComp { public int Count; }

[ComponentId(242)]
file struct E2ERecorderComp { public int Data; }

[Collection("ComponentRegistry")]
public sealed class IntegrationTests
{
    // INT1 (3 tests)
    // INT2 (2 tests)
    // INT3 (1 test)
}

// File-scoped helper classes (namespace level, after the test class)
file sealed class CapturingSystemRegistry : ISystemRegistry { ... }
```

---

## INT1: E2E tests

### Test 1: `E2E_PropertyMatchBreakpoint_PausesAndStepsCleanly`

**Full sequence:**

```csharp
[Fact]
public void E2E_PropertyMatchBreakpoint_PausesAndStepsCleanly()
{
    // ── Arrange ──────────────────────────────────────────────────────────────
    ComponentTypeRegistry.Clear();

    var liveRepo = new EntityRepository();
    liveRepo.RegisterComponent<E2EHealthComp>();
    var preTickSnapshot = new EntityRepository();
    preTickSnapshot.RegisterComponent<E2EHealthComp>();

    var tc               = new MockDebugTimeController();
    var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
    var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
    var mgr              = new DataBreakpointManager(
        liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
    var system           = new DataBreakpointSystem(mgr);

    // Register breakpoint: Health.Value < 10
    mgr.AddBreakpoint(
        new PropertyMatchDto
        {
            ComponentType = typeof(E2EHealthComp),
            PropertyPath  = "Value",
            Predicate     = new NumericPredicateDto { MaxValue = 9.999 },
        },
        displayName: "E2E-HealthBP");

    // Create entity with Health = 20 (above threshold)
    var entity = liveRepo.CreateEntity();
    liveRepo.AddComponent(entity, new E2EHealthComp { Value = 20 });

    // ── Pre-tick snapshot (gate is open because breakpoint is enabled) ──────
    snapshotProvider.Execute(liveRepo, 0.016f);  // captures Health = 20
    Assert.Equal(20, preTickSnapshot.GetComponent<E2EHealthComp>(entity).Value);

    // ── Advance version + mutate to trigger breakpoint ──────────────────────
    liveRepo.Tick();
    ref var h = ref liveRepo.GetComponentRW<E2EHealthComp>(entity);
    h.Value = 5; // < 10 → triggers predicate

    // ── Run system → fires OnHit → pause ────────────────────────────────────
    system.Execute(liveRepo, 0.016f);

    // ── Assert: paused; ActiveView shows pre-tick state (Health = 20) ────────
    Assert.True(mgr.IsPaused);
    var snapshot = (EntityRepository)mgr.ActiveView;
    Assert.Equal(20, snapshot.GetComponent<E2EHealthComp>(entity).Value);

    // Live repo was rewound to pre-tick (Health = 20)
    Assert.Equal(20, liveRepo.GetComponent<E2EHealthComp>(entity).Value);

    // ── Step → unpauses, restores post-tick (Health = 5) ────────────────────
    mgr.RequestStep();
    Assert.False(mgr.IsPaused);
    Assert.Equal(5, liveRepo.GetComponent<E2EHealthComp>(entity).Value);
}
```

### Test 2: `E2E_CompoundPredicate_FiresOnlyWhenBothConditionsMet`

Use compound `And[Health < 10, Ammo == 0]` against an entity that has both components:

```csharp
[Fact]
public void E2E_CompoundPredicate_FiresOnlyWhenBothConditionsMet()
{
    ComponentTypeRegistry.Clear();

    var liveRepo = new EntityRepository();
    liveRepo.RegisterComponent<E2EHealthComp>();
    liveRepo.RegisterComponent<E2EAmmoComp>();
    var preTickSnapshot = new EntityRepository();
    preTickSnapshot.RegisterComponent<E2EHealthComp>();
    preTickSnapshot.RegisterComponent<E2EAmmoComp>();

    var tc               = new MockDebugTimeController();
    var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
    var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
    var mgr              = new DataBreakpointManager(
        liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
    var system           = new DataBreakpointSystem(mgr);

    // Compound: Health < 10 AND Ammo == 0
    mgr.AddBreakpoint(
        new CompoundPredicateDto
        {
            Operator   = LogicalOperator.And,
            Conditions = new System.Collections.Generic.List<SearchPredicateDto>
            {
                new PropertyMatchDto
                {
                    ComponentType = typeof(E2EHealthComp),
                    PropertyPath  = "Value",
                    Predicate     = new NumericPredicateDto { MaxValue = 9.999 },
                },
                new PropertyMatchDto
                {
                    ComponentType = typeof(E2EAmmoComp),
                    PropertyPath  = "Count",
                    Predicate     = new NumericPredicateDto { MinValue = -0.001, MaxValue = 0.001 },
                },
            },
        },
        displayName: "CompoundBP");

    var entity = liveRepo.CreateEntity();
    liveRepo.AddComponent(entity, new E2EHealthComp { Value = 20 });
    liveRepo.AddComponent(entity, new E2EAmmoComp { Count = 5 });

    // ── Tick 1: Health=20, Ammo=5 → both conditions false → no hit ──────────
    snapshotProvider.Execute(liveRepo, 0.016f);
    liveRepo.Tick();
    system.Execute(liveRepo, 0.016f);
    Assert.False(mgr.IsPaused);

    // ── Tick 2: Health=5, Ammo=5 → only health condition met → no hit ───────
    snapshotProvider.Execute(liveRepo, 0.016f);
    liveRepo.Tick();
    liveRepo.GetComponentRW<E2EHealthComp>(entity).Value = 5;
    system.Execute(liveRepo, 0.016f);
    Assert.False(mgr.IsPaused);

    // ── Tick 3: Health=5 (still), Ammo=0 → both conditions met → HIT ────────
    snapshotProvider.Execute(liveRepo, 0.016f);
    liveRepo.Tick();
    liveRepo.GetComponentRW<E2EAmmoComp>(entity).Count = 0;
    system.Execute(liveRepo, 0.016f);
    Assert.True(mgr.IsPaused);
}
```

### Test 3: `E2E_DeferredMutation_AppliedAtNplus1`

Stage a mutation, step, apply ECB, verify new value:

```csharp
[Fact]
public void E2E_DeferredMutation_AppliedAtNplus1()
{
    ComponentTypeRegistry.Clear();

    var liveRepo = new EntityRepository();
    liveRepo.RegisterComponent<E2EHealthComp>();
    var preTickSnapshot = new EntityRepository();
    preTickSnapshot.RegisterComponent<E2EHealthComp>();

    var tc               = new MockDebugTimeController();
    var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
    var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
    var mgr              = new DataBreakpointManager(
        liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
    var system           = new DataBreakpointSystem(mgr);

    mgr.AddBreakpoint(
        new PropertyMatchDto
        {
            ComponentType = typeof(E2EHealthComp),
            PropertyPath  = "Value",
            Predicate     = new NumericPredicateDto { MaxValue = 9.999 },
        });

    var entity = liveRepo.CreateEntity();
    liveRepo.AddComponent(entity, new E2EHealthComp { Value = 20 });

    // Trigger breakpoint to reach paused state
    snapshotProvider.Execute(liveRepo, 0.016f);
    liveRepo.Tick();
    liveRepo.GetComponentRW<E2EHealthComp>(entity).Value = 5;
    system.Execute(liveRepo, 0.016f);
    Assert.True(mgr.IsPaused);

    // Stage mutation: Health = 1000
    mgr.StageMutation(entity, typeof(E2EHealthComp), new E2EHealthComp { Value = 1000 });
    Assert.Equal(1, mgr.PendingMutationsCount);

    // Step: drains ECB, restores post-tick, advances time
    mgr.RequestStep();
    Assert.False(mgr.IsPaused);
    Assert.Equal(0, mgr.PendingMutationsCount);

    // Apply the ECB to liveRepo (simulates kernel's ECB flush at N+1 tick boundary)
    ISimulationView view = liveRepo;
    var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
    ecb.Playback(liveRepo);

    // Assert: deferred mutation applied
    Assert.Equal(1000, liveRepo.GetComponent<E2EHealthComp>(entity).Value);
}
```

**Required using:** `using Fdp.Core; // for EntityCommandBuffer`

---

## INT2: Performance tests

```csharp
[Fact]
public void Perf_HeavyScenario_NoBreakpoints_FastPath()
{
    // 1000 entities, no breakpoints. DebugSnapshotProvider gate is CLOSED
    // (no enabled BPs), so snapshotProvider.Execute() is a no-op.
    // DataBreakpointSystem.Execute() early-returns (HasMountedDelegates == false).
    // 100 ticks must complete in < 500ms on any reasonable CI machine.
    ComponentTypeRegistry.Clear();

    var liveRepo         = new EntityRepository();
    liveRepo.RegisterComponent<E2EHealthComp>();
    var preTickSnapshot  = new EntityRepository();
    preTickSnapshot.RegisterComponent<E2EHealthComp>();
    var tc               = new MockDebugTimeController();
    var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
    var mgr              = new DataBreakpointManager(
        liveRepo, preTickSnapshot, snapshotProvider, tc);
    var system           = new DataBreakpointSystem(mgr);

    // Spawn 1000 entities with Health = 20 each
    for (int i = 0; i < 1000; i++)
    {
        var e = liveRepo.CreateEntity();
        liveRepo.AddComponent(e, new E2EHealthComp { Value = 20 });
    }

    // No breakpoints registered → gate closed
    var sw = System.Diagnostics.Stopwatch.StartNew();
    for (int tick = 0; tick < 100; tick++)
    {
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        system.Execute(liveRepo, 0.016f);
    }
    sw.Stop();

    // < 500ms for 100 ticks of 1000 entities with zero work
    Assert.True(sw.ElapsedMilliseconds < 500,
        $"Expected < 500ms but took {sw.ElapsedMilliseconds}ms");
}

[Fact]
public void Perf_HeavyScenario_OneActiveBreakpoint_FitsBudget()
{
    // 1000 entities, 1 armed breakpoint scanning Health < 10.
    // All entities have Health = 20 (no hits), but the scan still runs.
    // 100 ticks must complete in < 5000ms on any reasonable CI machine.
    ComponentTypeRegistry.Clear();

    var liveRepo         = new EntityRepository();
    liveRepo.RegisterComponent<E2EHealthComp>();
    var preTickSnapshot  = new EntityRepository();
    preTickSnapshot.RegisterComponent<E2EHealthComp>();
    var tc               = new MockDebugTimeController();
    var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
    var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
    var mgr              = new DataBreakpointManager(
        liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
    var system           = new DataBreakpointSystem(mgr);

    mgr.AddBreakpoint(
        new PropertyMatchDto
        {
            ComponentType = typeof(E2EHealthComp),
            PropertyPath  = "Value",
            Predicate     = new NumericPredicateDto { MaxValue = 9.999 },
        });

    // Spawn 1000 entities with Health = 20 (above threshold, so no breakpoint fires)
    for (int i = 0; i < 1000; i++)
    {
        var e = liveRepo.CreateEntity();
        liveRepo.AddComponent(e, new E2EHealthComp { Value = 20 });
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    for (int tick = 0; tick < 100; tick++)
    {
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        system.Execute(liveRepo, 0.016f);
    }
    sw.Stop();

    // < 5000ms for 100 ticks of 1000 entities with one active scan
    Assert.True(sw.ElapsedMilliseconds < 5000,
        $"Expected < 5000ms but took {sw.ElapsedMilliseconds}ms");
}
```

---

## INT3: Recorder invariance

This test verifies that recording + breakpoint activity produces frames with monotonically non-decreasing tick values.

```csharp
[Fact]
public void Recorder_PausedSession_ProducesMonotonicTicks()
{
    ComponentTypeRegistry.Clear();

    var tempFile = Path.Combine(Path.GetTempPath(), $"int3_recorder_{Guid.NewGuid():N}.fdp");
    try
    {
        // ── Arrange ─────────────────────────────────────────────────────────────
        var liveRepo = new EntityRepository();
        liveRepo.RegisterComponent<E2ERecorderComp>();
        var preTickSnapshot = new EntityRepository();
        preTickSnapshot.RegisterComponent<E2ERecorderComp>();

        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var compiler         = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var mgr              = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc, predicateCompiler: compiler);
        var bpSystem         = new DataBreakpointSystem(mgr);

        // Set up recorder (blocking = true for deterministic tests)
        var config   = new RecordingConfiguration
        {
            FilePath    = tempFile,
            ExerciseId  = Guid.NewGuid(),
            Blocking    = true,
        };
        using var module   = new RecordingModule(config);
        var captured       = new CapturingSystemRegistry();
        module.RegisterSystems(captured);
        var recorderSystem = captured.Systems[0]; // RecorderTickSystem

        // Create entity with Data = 10 (above threshold, no breakpoint yet)
        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new E2ERecorderComp { Data = 10 });

        // Register breakpoint: Data < 5
        mgr.AddBreakpoint(
            new PropertyMatchDto
            {
                ComponentType = typeof(E2ERecorderComp),
                PropertyPath  = "Data",
                Predicate     = new NumericPredicateDto { MaxValue = 4.999 },
            });

        // ── Tick 1: normal frame (no breakpoint) ─────────────────────────────
        liveRepo.SetSingletonUnmanaged(new GlobalTime
        {
            DeltaTime = 0.016f, TimeScale = 1.0f,
            TotalWallTicks = DateTime.UtcNow.Ticks
        });
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        recorderSystem.Execute(liveRepo, 0.016f); // Frame recorded at GlobalVersion = 1
        Assert.False(mgr.IsPaused);

        // ── Tick 2: trigger breakpoint, pause ────────────────────────────────
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        liveRepo.GetComponentRW<E2ERecorderComp>(entity).Data = 3; // < 5 → fires
        bpSystem.Execute(liveRepo, 0.016f);
        Assert.True(mgr.IsPaused);
        // Recorder does NOT capture while paused (kernel stops calling it in real engine).
        // In this test, we simply don't call recorderSystem.Execute while paused.

        // ── Step: unpause ─────────────────────────────────────────────────────
        mgr.RequestStep();
        Assert.False(mgr.IsPaused);

        // ── Tick 3: resume, record another frame ─────────────────────────────
        liveRepo.SetSingletonUnmanaged(new GlobalTime
        {
            DeltaTime = 0.016f, TimeScale = 1.0f,
            TotalWallTicks = DateTime.UtcNow.Ticks + 1000
        });
        snapshotProvider.Execute(liveRepo, 0.016f);
        liveRepo.Tick();
        recorderSystem.Execute(liveRepo, 0.016f); // Frame recorded at GlobalVersion = 3
        // ── Flush recorder ───────────────────────────────────────────────────
        module.Dispose(); // blocks until LZ4 buffers flushed

        // ── Assert: .fdp exists ───────────────────────────────────────────────
        Assert.True(File.Exists(tempFile), $"Expected .fdp file at {tempFile}");

        // ── Assert: frame tick values are monotonically non-decreasing ────────
        var ticks = ReadFrameTicks(tempFile);
        Assert.True(ticks.Count >= 2, $"Expected at least 2 frames, got {ticks.Count}");
        for (int i = 1; i < ticks.Count; i++)
        {
            Assert.True(ticks[i] >= ticks[i - 1],
                $"Frame {i} tick {ticks[i]} < frame {i-1} tick {ticks[i-1]} (not monotonic)");
        }
    }
    finally
    {
        if (File.Exists(tempFile)) File.Delete(tempFile);
        if (File.Exists(tempFile + ".meta.json")) File.Delete(tempFile + ".meta.json");
    }
}
```

---

## Helper types (namespace-level file classes)

Add these after the closing `}` of `IntegrationTests`:

```csharp
/// <summary>Captures systems registered by modules (for extracting RecorderTickSystem in INT3).</summary>
file sealed class CapturingSystemRegistry : ISystemRegistry
{
    public readonly List<IEcsModuleSystem> Systems = new();

    public void RegisterSystem<T>(T system) where T : IEcsModuleSystem
        => Systems.Add(system);

    public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
    {
        Systems.Add(system);
        return system;
    }
}
```

**AND** a static helper method `ReadFrameTicks`. Add this as a `file static class` or add it as a `private static` inside `IntegrationTests`:

```csharp
/// <summary>
/// Reads all frame tick values from a .fdp recording file.
/// Format: [GlobalHeader: 18 bytes] [FrameOuterHeader: 25 bytes + CompressedSize bytes]...
/// </summary>
private static List<ulong> ReadFrameTicks(string path)
{
    var ticks = new List<ulong>();
    using var fs = File.OpenRead(path);
    using var br = new BinaryReader(fs);

    // Skip global header (18 bytes: Magic[6] + Version[4] + Timestamp[8])
    int globalHeaderSize = RecordingGlobalHeader.Size;
    br.ReadBytes(globalHeaderSize);

    // Read each FrameOuterHeader + skip payload
    int frameHeaderSize = FrameOuterHeader.Size; // 25 bytes
    while (fs.Position + frameHeaderSize <= fs.Length)
    {
        int  compressedSize   = br.ReadInt32();
        int  uncompressedSize = br.ReadInt32();
        ulong tick            = br.ReadUInt64();
        byte frameType        = br.ReadByte();
        long wallClockTicks   = br.ReadInt64();

        ticks.Add(tick);

        // Skip compressed payload
        if (compressedSize > 0)
            br.ReadBytes(compressedSize);
    }

    return ticks;
}
```

---

## Required usings

At the top of `IntegrationTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
```

---

## Critical implementation notes

### 1. `ComponentTypeRegistry.Clear()` in every test
Each test must call `ComponentTypeRegistry.Clear()` at the top. Every test registers `E2EHealthComp` (240), `E2EAmmoComp` (241), or `E2ERecorderComp` (242) — these IDs must not conflict with other test files.

### 2. `GetComponentRW<T>` for writing
Use `liveRepo.GetComponentRW<E2EHealthComp>(entity).Value = 5` (ref return assignment). Make sure to call `liveRepo.Tick()` BEFORE calling `GetComponentRW` — this advances the global version so the chunk version is updated, allowing `SyncDirtyChunks` to detect the mutation. This is the same pattern used in the existing `RequestStep_RestoresLiveRepoToPostTickState` test.

### 3. `snapshotProvider.Execute` gate
`DebugSnapshotProvider.Execute` only runs if `_isEnabled == 1`. The gate opens when the first enabled breakpoint is added. So in INT2 `Perf_HeavyScenario_NoBreakpoints_FastPath`, since no breakpoints are added, the gate is CLOSED and `Execute` is a no-op. ✓

### 4. ECB playback in `E2E_DeferredMutation_AppliedAtNplus1`
After `mgr.RequestStep()`, the ECB has the `SetComponentRaw` command buffered. To see the mutation in `liveRepo`, call:
```csharp
ISimulationView view = liveRepo;
var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
ecb.Playback(liveRepo);
```
`EntityCommandBuffer` is `public` in `Fdp.Core`. `Playback(EntityRepository)` is also public. The `GetCommandBuffer()` returns `EntityCommandBuffer` (thread-local), so the cast is safe.

### 5. RecordingModule.Dispose is `using`
In INT3, `module.Dispose()` is called explicitly (not relying on `using` to call it at end of scope) because we need it to happen BEFORE reading the `.fdp` file. Either use `using var module = ... ; ... ; module.Dispose();` (calling Dispose explicitly is fine since it checks `_disposed`) OR just use `using var module` and ensure the file read happens after the `using` block.

Actually, use `module.Dispose()` explicitly before reading the file to ensure flush completes:
```csharp
module.Dispose(); // must happen before File.Exists check
Assert.True(File.Exists(tempFile));
var ticks = ReadFrameTicks(tempFile);
```
But since `module` was declared with `using var`, the `Dispose()` in the finally block will call it again — but the `Dispose()` checks `if (_disposed) return;` so the second call is safe.

**Better approach:** Don't use `using var module`. Dispose manually before reading:
```csharp
var module = new RecordingModule(config);
// ...
module.Dispose();  // flushes
Assert.True(File.Exists(tempFile));
var ticks = ReadFrameTicks(tempFile);
```

### 6. `GlobalTime` in INT3
`RecorderTickSystem.Execute` calls `repo.GetSingletonUnmanaged<GlobalTime>()`. If `GlobalTime` is not set as a singleton, this will likely return a default (all zeros), which is OK for testing (it falls back to `DateTime.UtcNow.Ticks`).

Actually, looking at the `RecorderTickSystem.Execute`:
```csharp
var globalTime = repo.GetSingletonUnmanaged<GlobalTime>();
long wallClockTicks = globalTime.TotalWallTicks != 0
    ? globalTime.TotalWallTicks
    : DateTime.UtcNow.Ticks;
```

So `GlobalTime` must be registered as a singleton component OR the fallback `DateTime.UtcNow.Ticks` is used. For the test, use the fallback by NOT setting `GlobalTime` — it works fine. No need to call `liveRepo.SetSingletonUnmanaged(new GlobalTime { ... })`.

But wait — does `liveRepo.GetSingletonUnmanaged<GlobalTime>()` throw if `GlobalTime` is not registered? Let me be safe and call `liveRepo.RegisterComponent<GlobalTime>()` and set it, OR just rely on the fallback. If `GlobalTime` is not in the registry, `GetSingletonUnmanaged` might throw.

**Safe approach:** Don't use `liveRepo.SetSingletonUnmanaged`. Instead, just set `GlobalTime` explicitly:
```csharp
// Register GlobalTime if not already registered (it may be auto-registered)
// The fallback path in RecorderTickSystem uses DateTime.UtcNow.Ticks if TotalWallTicks == 0.
// Don't set GlobalTime; let it use the fallback.
```

Actually, look at `GetSingletonUnmanaged` — it probably returns `default(T)` if not set (for value types). Since `GlobalTime.TotalWallTicks` defaults to 0, the fallback is used. This is safe.

However, `RecorderTickSystem` might call `repo.GetSingletonUnmanaged<GlobalTime>()` which might call `GetComponent` on a special singleton entity. If the component type isn't registered, it might fail.

**Safest approach:** Register `GlobalTime` and set it:
```csharp
liveRepo.RegisterComponent<GlobalTime>();  // if needed
liveRepo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
```

But `GlobalTime` might already be auto-registered via `[ComponentId]` attribute. Check by looking at `GlobalTime` definition.

**Simplest approach:** Just call `liveRepo.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f })` — this will auto-register the component if not already registered. `EntityRepository.SetSingletonUnmanaged` creates the singleton entity and adds the component.

### 7. ComponentId values — check before using
Before implementing, grep:
```powershell
Select-String -Path "Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.cs" -Pattern "\[ComponentId\(" | Select-Object Line
```
If 240, 241, 242 are taken, use 250, 251, 252.

### 8. `CapturingSystemRegistry` namespace import
Needs: `using Fdp.ModuleHost.Abstractions;` for `ISystemRegistry` and `IEcsModuleSystem`.

### 9. `RecordingModule.Dispose()` double-call safety
`AsyncRecorder.Dispose()` checks `if (_disposed) return;` before doing work. Calling it twice is safe.

### 10. `FrameOuterHeader.Size` is `sizeof(FrameOuterHeader)` which is 25
Layout: `CompressedSize(4) + UncompressedSize(4) + Tick(8) + FrameType(1) + WallClockTicks(8) = 25`. The `Size` property is `unsafe int Size => sizeof(FrameOuterHeader)` — it's `public static unsafe int Size`. To call it without an unsafe context in tests, wrap in an unsafe block or hardcode 25. But `FrameOuterHeader.Size` should be accessible from a non-unsafe method if you use it as `var size = FrameOuterHeader.Size;` since it's a static property.

Actually, `public static unsafe int Size => sizeof(FrameOuterHeader)` — calling `sizeof` on an unmanaged struct requires unsafe. But reading the static property `FrameOuterHeader.Size` from safe code should be fine since the unsafe is inside the property getter, not at the call site.

### 11. Preemptive note about `module.Dispose()` call after `using`
If you use `using var module = new RecordingModule(config);` AND also call `module.Dispose()` explicitly, the `Dispose` from the `using` will be called again at end of scope — but that's safe because `AsyncRecorder._disposed` guard prevents double work.

---

## Build and test

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
```

All 97 existing tests must still pass. 6 new tests added. Total must be ≥ 103.

---

## Report

Provide a detailed report with:
1. All files created/modified
2. Test count: before (97) and after (must be ≥ 103)
3. Any deviations from instructions with justifications
4. Full build output (zero errors, zero new warnings)
5. List of all 6 new test names
6. Any issues encountered with ECB playback, RecorderTickSystem, or GlobalTime
