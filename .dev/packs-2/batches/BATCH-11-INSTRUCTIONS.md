# BATCH-11: EditorFileIO + FeatureSwitch RCU Tests + Distributed Brain-Muscle Tests

**Batch Number:** BATCH-11  
**Tasks:** PACK2-R005, PACK2-R006  
**Phase:** Phase 6 (Integration Tests — remaining)  
**Estimated Effort:** 5–7 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-10 (C002, C003, R004 — feature switch + EditorHarness spawn support)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the final batch. It adds the last 8+3 integration tests:

- **R005-A:** Four offline file-I/O tests (`EditorFileIOIntegrationTests`)
- **R005-B:** Four feature-switch RCU tests (`FeatureSwitchRcuIntegrationTests`) — verifying that the kernel's RCU module swap works offline, plus one test that captures a DDS egress write via an injected in-memory spy
- **R006:** Three distributed Brain-Muscle tests (`DistributedBrainMuscleIntegrationTests`) — CGF/SimHost paired via a shared CycloneDDS loopback domain

The batch also includes two prerequisite code changes:
1. Modify `ScenarioFileService` to publish `WorldResetEvent` on the bus (needed by R005-A)
2. Expose `GhostEntityMap` test-hook on `CgfSubsystem` (needed by R006)

### Required Reading (IN ORDER)

1. **Task Definitions:** `.dev/packs-2/TASK-DETAIL.md` — See PACK2-R005 and PACK2-R006  
2. **Design:** `.dev/packs-2/DESIGN.md` §6.C, §6.D  
3. **Previous Batch Instructions:** `.dev/packs-2/batches/BATCH-10-INSTRUCTIONS.md`  
4. **Previous Review:** `.dev/packs-2/reviews/BATCH-10-REVIEW.md`

### Source Code Location

- **Production changes:** `Hrot.ScenarioEditor/Services/ScenarioFileService.cs`, `Hrot.ClusterRunner/Services/CgfSubsystem.cs`  
- **Harness extension:** `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`  
- **New test files:** `Hrot.ClusterRunner.Integration.Tests/`

### Report Submission

**When done, submit your report to:**  
`.dev/packs-2/reports/BATCH-11-REPORT.md`

---

## Context

BATCH-10 added the Feature Switch methods to `EditorApplication` and the spawn-capable `EditorHarness`. BATCH-11 tests these mechanisms:

- **R005-A** exercises file I/O (`NewScenario`, `SaveScenario`, `LoadScenario`) via `EditorHarness.Editor`.  
- **R005-B** exercises the RCU hot-plug cycle: switch External (uninstall logic packs, install offline translator spy) → spawn → verify DDS writer called → switch back → verify local spawn works.  
- **R006** exercises real DDS propagation between a `HrotRunnerHarness` (SimHost role) and a `CgfHarness`, both sharing a CycloneDDS loopback domain.

---

## ✅ Tasks

---

### Task 1: Publish `WorldResetEvent` on Bus from `ScenarioFileService` (Prerequisite for R005-A)

**File:** `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` (UPDATE)

`WorldResetEvent` is doc'd as published on the bus but currently only fires via callbacks. Fix this:

#### 1a. Add optional `FdpEventBus?` constructor parameter

```csharp
public ScenarioFileService(ScenarioSerializer serializer, FdpEventBus? bus = null)
{
    _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    _bus = bus;
}
```

Add private field `private readonly FdpEventBus? _bus;` and `using Fdp.Kernel;` if needed.

#### 1b. Publish on bus in `FireWorldReset`

```csharp
private void FireWorldReset()
{
    _worldResetObservers?.Invoke();
    _bus?.PublishManaged(new WorldResetEvent());
}
```

**No callers break** — the `bus` parameter is optional; all existing `new ScenarioFileService(serializer)` calls remain valid.

---

### Task 2: Pass `Bus` to `ScenarioFileService` in `EditorHarness` (Prerequisite for R005-A)

**File:** `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` (UPDATE)

Currently: `var fileService = EditorBootstrap.CreateFileService();`

Change to: create the file service directly, passing `Bus` so `WorldResetEvent` is published:

```csharp
// After Bus is assigned:
var serializer  = new ScenarioSerializerBuilder("Hrot.Scenario").Build();
var fileService = new ScenarioFileService(serializer, Bus);
```

Add `using FDP.Toolkit.Scenario;` and `using Hrot.ScenarioEditor.Services;` if not already present.

Also expose a `FileService` property on `EditorHarness` (needed for test 4 of R005-A):

```csharp
public ScenarioFileService FileService { get; }
```

(Store `fileService` in a field and expose it.)

---

### Task 3: Expose `GhostEntityMap` on `CgfSubsystem` (Prerequisite for R006)

**File:** `Hrot.ClusterRunner/Services/CgfSubsystem.cs` (UPDATE)

Store the `entityMap` as a field and expose it as a test-hook property:

```csharp
private NetworkEntityMap? _entityMap;

/// <summary>TestHook: exposes the ghost entity map for integration tests.</summary>
internal NetworkEntityMap? GhostEntityMap => _entityMap;
```

In `Initialize`, change `var entityMap = new NetworkEntityMap();` to `_entityMap = new NetworkEntityMap();` and replace all usages of `entityMap` in the method body with `_entityMap`.

---

### Task 4: Add `InternalsVisibleTo` for Integration Tests

**File:** `Hrot.Map.Common/Hrot.Map.Common.csproj` (UPDATE)

Add the integration test assembly to the `InternalsVisibleTo` list so `SpawnEntityCommandEgressTranslator`'s `internal` testable constructor is accessible:

```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
  <_Parameter1>Hrot.ClusterRunner.Integration.Tests</_Parameter1>
</AssemblyAttribute>
```

---

### Task 5: Create `EditorFileIOIntegrationTests` (R005-A)

**File:** `Hrot.ClusterRunner.Integration.Tests/EditorFileIOIntegrationTests.cs` (NEW FILE)

Four tests using `EditorHarness`. Read [TASK-DETAIL.md PACK2-R005 Part A](../TASK-DETAIL.md#pack2-r005--editorfileiointegrationtests-it-2-and-featureswitchrcuintegrationtests-it-3) for full spec.

```csharp
using System;
using System.IO;
using System.Text.Json;
using Fdp.Kernel;
using Hrot.ScenarioEditor.Events;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>PACK2-R005 Part A — IT-2: Editor file I/O integration tests.</summary>
public sealed class EditorFileIOIntegrationTests
{
    private const int PumpMs = 5_000;

    // ── IT-2a ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NewScenario_FiresWorldResetEventBeforeClear()
    {
        using var harness = new EditorHarness();

        // Register a subscriber BEFORE calling NewScenario
        bool eventFired = false;
        harness.Bus.Subscribe<WorldResetEvent>(_ => eventFired = true);

        // Spawn an entity so repo is non-empty
        harness.Bus.PublishManaged(new FDP.Toolkit.NetworkSpawning.Events.SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = 1L, OwnerNodeId = 0,
            InitType = ModuleHost.Core.Network.Interfaces.ReliableInitType.None,
        });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpMs));

        harness.Editor.NewScenario();
        harness.PumpFrames(1);  // flush any pending command buffer entries

        Assert.True(eventFired,        "WorldResetEvent must fire on NewScenario");
        Assert.Equal(0, harness.Repo.EntityCount);
        var gt = harness.Repo.GetSingletonUnmanaged<GlobalTime>();
        Assert.Equal(0.0, gt.TotalTime, precision: 6);
    }

    // ── IT-2b ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveScenario_SubsystemTypeIsHrotScenario()
    {
        using var harness = new EditorHarness();
        var tempPath = Path.GetTempFileName();
        try
        {
            harness.Editor.SaveScenario(tempPath);

            var json    = File.ReadAllText(tempPath);
            using var doc = JsonDocument.Parse(json);
            var subsysType = doc.RootElement
                               .GetProperty("Header")
                               .GetProperty("SubsystemType")
                               .GetString();

            Assert.Equal("Hrot.Scenario", subsysType);
        }
        finally { File.Delete(tempPath); }
    }

    // ── IT-2c ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadScenario_AcceptsHrotSimHostFile()
    {
        using var harness = new EditorHarness();
        var tempPath = Path.GetTempFileName();
        try
        {
            // Construct a minimal valid file with Hrot.SimHost header and no entities
            var minimalJson = """
                {
                  "Header": { "SubsystemType": "Hrot.SimHost", "Version": "1.0" },
                  "Entities": {}
                }
                """;
            File.WriteAllText(tempPath, minimalJson);

            var ex = Record.Exception(() => harness.Editor.LoadScenario(tempPath));
            Assert.Null(ex);
        }
        finally { File.Delete(tempPath); }
    }

    // ── IT-2d ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadScenario_RejectsUnknownSubsystemType()
    {
        using var harness = new EditorHarness();
        var tempPath = Path.GetTempFileName();
        try
        {
            var badJson = """
                {
                  "Header": { "SubsystemType": "UnknownApp", "Version": "1.0" },
                  "Entities": {}
                }
                """;
            File.WriteAllText(tempPath, badJson);

            Assert.Throws<InvalidOperationException>(() => harness.Editor.LoadScenario(tempPath));
            // Repo should remain empty (validation happens before clear)
            Assert.Equal(0, harness.Repo.EntityCount);
        }
        finally { File.Delete(tempPath); }
    }
}
```

**Note on `GlobalTime.TotalTime`:** `SimTransform` time tracking is via `GlobalTime` singleton.  
Use `harness.Repo.GetSingletonUnmanaged<GlobalTime>()` to read it. Assert `TotalTime == 0` (or `T == 0` if that's the actual field name — verify in `Fdp.Kernel.GlobalTime`).

---

### Task 6: Create `FeatureSwitchRcuIntegrationTests` (R005-B)

**File:** `Hrot.ClusterRunner.Integration.Tests/FeatureSwitchRcuIntegrationTests.cs` (NEW FILE)

Four tests verifying RCU mode switching. Read [TASK-DETAIL.md PACK2-R005 Part B](../TASK-DETAIL.md#pack2-r005--editorfileiointegrationtests-it-2-and-featureswitchrcuintegrationtests-it-3) for the full spec.

#### Key design: Offline translator pack with recording spy

To test that `SpawnEntityCommand` reaches the DDS writer in External mode, you need an `ActuatorIntentsEgressPack` whose `SpawnEntityCommandEgressTranslator` writes to a `RecordingDdsWriter` instead of a real DDS participant.

**Use the `internal` testable constructor** (accessible via the `InternalsVisibleTo` added in Task 4):

```csharp
// Inline in the test class:
private sealed class RecordingDdsWriter : IDdsWriter<CreateEntityRequest>
{
    public int CallCount { get; private set; }
    public void Write(CreateEntityRequest sample) => CallCount++;
    public void DisposeInstance(CreateEntityRequest key) { }
}

private static (IEcsModule pack, RecordingDdsWriter spy) BuildOfflineEgressPack(FdpEventBus bus)
{
    // Using the internal constructor accessible via InternalsVisibleTo
    var spy        = new RecordingDdsWriter();
    var geoTx      = HrotEnvironment.CreateGeoTransform();
    var entityMap  = new NetworkEntityMap();
    var translator = new SpawnEntityCommandEgressTranslator(spy, bus, geoTx);
    var pack       = new CycloneEgressOnlyPack(translator);
    return (pack, spy);
}
```

But `CycloneEgressOnlyPack` doesn't exist. Instead, create a minimal inline `IEcsModule` that registers the translator via a `CycloneEgressSystem`:

```csharp
private sealed class SpyEgressPack : IEcsModule
{
    private readonly SpawnEntityCommandEgressTranslator _translator;

    public string Name => "SpyEgressPack";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public SpyEgressPack(SpawnEntityCommandEgressTranslator translator)
        => _translator = translator;

    public void RegisterSystems(ISystemRegistry registry)
        => registry.RegisterSystem(new ModuleHost.Network.Cyclone.Systems.CycloneEgressSystem(
               new IDescriptorTranslator[] { _translator }));

    public void Tick(ISimulationView view, float deltaTime) { }
}
```

**Alternative (simpler) approach:** If `CycloneEgressSystem` constructor is internal or not directly usable, use a simpler approach: create a minimal `IEcsModule` whose `Tick` manually calls `_translator.PollIngress(view)` each frame. Check how `CycloneEgressSystem` calls translators and replicate for offline testing.

Even simpler: for Tests 1, 3, 4 (no DDS write assertion), just pass `null` as the translator pack. For Test 2, if constructing the spy pack is too complex, use a simpler behavioral assertion: the entity count stays 0 in external mode (which proves SpawnEntityCommand was not locally processed).

#### Extended `EditorHarness` for feature switch tests

The `EditorHarness` constructor installs `SimHostCoreLogicPack` + `CgfLogicPack` as `logicPacks` in `EditorApplication`. For tests 1, 3, 4, calling `await Editor.SwitchToExternalAsync()` uninstalls those packs; `await Editor.SwitchToInternalAsync()` restores them.

For `SwitchToExternalAsync` to actually complete, `kernel.Update()` must be called while the Task is pending. Use:

```csharp
var switchTask = harness.Editor.SwitchToExternalAsync();
bool completed = harness.PumpUntil(() => switchTask.IsCompleted, timeoutMs: 5000);
Assert.True(completed, "SwitchToExternalAsync should complete within 5 s");
if (switchTask.IsFaulted) throw switchTask.Exception!;
```

#### Full test class

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;   // SpawnEntityCommandEgressTranslator (now internal-visible)
using Hrot.NED.Messages;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>PACK2-R005 Part B — IT-3: Feature Switch RCU integration tests.</summary>
public sealed class FeatureSwitchRcuIntegrationTests
{
    private const int    PumpMs        = 5_000;
    private const long   TestTkbType   = 1L;
    private const long   TestNetworkId = 99L;

    // ── Spy types ────────────────────────────────────────────────────────────

    private sealed class RecordingDdsWriter : IDdsWriter<CreateEntityRequest>
    {
        public int CallCount { get; private set; }
        public void Write(CreateEntityRequest _) => CallCount++;
        public void DisposeInstance(CreateEntityRequest _) { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Task SwitchExternalAndWait(EditorHarness harness)
    {
        var t = harness.Editor.SwitchToExternalAsync();
        bool done = harness.PumpUntil(() => t.IsCompleted, PumpMs);
        if (!done || t.IsFaulted) throw t.Exception ?? new TimeoutException("SwitchToExternalAsync timed out");
        return t;
    }

    private static Task SwitchInternalAndWait(EditorHarness harness)
    {
        var t = harness.Editor.SwitchToInternalAsync();
        bool done = harness.PumpUntil(() => t.IsCompleted, PumpMs);
        if (!done || t.IsFaulted) throw t.Exception ?? new TimeoutException("SwitchToInternalAsync timed out");
        return t;
    }

    // ── IT-3a ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SwitchToExternal_EjectsLogicPacks_SpawnNoLongerLocal()
    {
        using var harness = new EditorHarness();

        // Pre-condition: spawn works in Internal mode
        harness.Bus.PublishManaged(new SpawnEntityCommand
            { TkbType = TestTkbType, NetworkId = TestNetworkId, OwnerNodeId = 0, InitType = ReliableInitType.None });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpMs));

        // Reset
        harness.Editor.NewScenario();
        harness.PumpFrames(2);

        // Switch to External
        SwitchExternalAndWait(harness);
        Assert.Equal(Hrot.Editor.SimHostMode.External, harness.Editor.CurrentMode);

        // Spawn command should NOT create an entity (SimHostCoreLogicPack is ejected — no NetworkSpawningSystem)
        harness.Bus.PublishManaged(new SpawnEntityCommand
            { TkbType = TestTkbType, NetworkId = TestNetworkId + 1, OwnerNodeId = 0, InitType = ReliableInitType.None });
        harness.PumpFrames(5);

        Assert.Equal(0, harness.Repo.EntityCount);
    }

    // ── IT-3b ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SwitchToInternal_RestoresLogicPacks_SpawnWorksAgain()
    {
        using var harness = new EditorHarness();

        SwitchExternalAndWait(harness);
        SwitchInternalAndWait(harness);

        Assert.Equal(Hrot.Editor.SimHostMode.Internal, harness.Editor.CurrentMode);

        // Spawn should work again
        harness.Bus.PublishManaged(new SpawnEntityCommand
            { TkbType = TestTkbType, NetworkId = TestNetworkId, OwnerNodeId = 0, InitType = ReliableInitType.None });

        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpMs),
            "After restoring Internal mode, spawn should create an entity");
    }

    // ── IT-3c ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RapidToggle_NoRaceCondition()
    {
        using var harness = new EditorHarness();

        for (int i = 0; i < 5; i++)
        {
            SwitchExternalAndWait(harness);
            Assert.Equal(Hrot.Editor.SimHostMode.External, harness.Editor.CurrentMode);

            SwitchInternalAndWait(harness);
            Assert.Equal(Hrot.Editor.SimHostMode.Internal, harness.Editor.CurrentMode);
        }

        // After 5 round-trips, spawn should still work
        harness.Bus.PublishManaged(new SpawnEntityCommand
            { TkbType = TestTkbType, NetworkId = TestNetworkId, OwnerNodeId = 0, InitType = ReliableInitType.None });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpMs));
    }

    // ── IT-3d (DDS spy) ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that in External mode, SpawnEntityCommand is forwarded to the
    /// DDS egress layer (mock writer). Uses the internal SpawnEntityCommandEgressTranslator
    /// constructor (accessible via InternalsVisibleTo).
    ///
    /// See BATCH-11-INSTRUCTIONS Task 4 for InternalsVisibleTo setup.
    /// </summary>
    [Fact]
    public void SwitchToExternal_SpawnCommand_ReachesDdsWriter()
    {
        var spy       = new RecordingDdsWriter();
        var geoTx     = HrotEnvironment.CreateGeoTransform();
        
        using var harness = new EditorHarness();

        // Build an offline translator spy pack
        var translator = new SpawnEntityCommandEgressTranslator(spy, harness.Bus, geoTx);
        var spyPack    = new SpyEgressPack(translator);

        // Provide the spy pack to EditorHarness so SwitchToExternalAsync installs it
        harness.SetTranslatorPacks(new List<ModuleHost.Core.Abstractions.IEcsModule> { spyPack });

        SwitchExternalAndWait(harness);

        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
        });
        harness.PumpFrames(3);

        Assert.Equal(1, spy.CallCount);
    }
}
```

**Note on `SpyEgressPack`:** Define it as a private nested class or a file-scoped internal class inside `FeatureSwitchRcuIntegrationTests.cs`. It needs `CycloneEgressSystem` from `ModuleHost.Network.Cyclone.Systems`.

If `CycloneEgressSystem` is not accessible, implement `SpyEgressPack.Tick` instead:

```csharp
public void Tick(ISimulationView view, float deltaTime)
{
    _translator.PollIngress(view);
}
public void RegisterSystems(ISystemRegistry registry) { }
```

**Note on `SetTranslatorPacks`:** Add this method to `EditorHarness`:

```csharp
/// <summary>
/// For feature-switch tests: provides translator packs to install on SwitchToExternalAsync.
/// Must be called before the first SwitchToExternalAsync call.
/// </summary>
public void SetTranslatorPacks(IReadOnlyList<IEcsModule> packs)
{
    // Re-create the EditorApplication capturing the translator packs
    Editor = new EditorApplication(
        _fileService, Bus, Repo, Kernel, _logicPacks, translatorPacks: packs);
}
```

This requires `EditorHarness` to store its `_fileService` and `_logicPacks` fields (add them alongside existing fields). Change `Editor` from `{ get; }` to a mutable `{ get; private set; }`.

---

### Task 7: Create `DistributedBrainMuscleIntegrationTests` (R006)

**File:** `Hrot.ClusterRunner.Integration.Tests/DistributedBrainMuscleIntegrationTests.cs` (NEW FILE)

Read [TASK-DETAIL.md PACK2-R006](../TASK-DETAIL.md#pack2-r006--distributedbrainmuscleintegrationtests-it-4) for full spec.

```csharp
using System.Threading;
using Hrot.ClusterRunner.Configuration;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK2-R006 — IT-4: Distributed Brain-Muscle integration tests.
/// Pairs one SimHost harness with one CGF harness sharing the same CycloneDDS loopback domain.
/// </summary>
public sealed class DistributedBrainMuscleIntegrationTests
{
    private static int _domainCounter = 299; // after the CgfHarness range (200–299)

    private const int SpawnPropagationTimeoutMs = 5_000;
    private const int MissionAssignmentTimeoutMs = 10_000;

    [Fact]
    public void SpawnedEntity_ReachesToCgf_ViaDds()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness(RunMode.SimHost, domainId);
        using var cgf     = new CgfHarness(domainId);

        simHost.WarmUp();
        cgf.PumpFrames(20);

        // Use the pre-built TKB type registered in HrotEnvironment
        long tkbType  = HrotEnvironment.CreateTkb().GetAll().First().TkbType;
        var  spawnPos = new Fdp.Modules.Geographic.GeoPoint { Latitude = 0.0, Longitude = 0.0 };

        long networkId = simHost.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        bool reached = PumpBothUntil(
            simHost, cgf,
            () => cgf.CgfSvc.GhostEntityMap?.ContainsKey(networkId) == true,
            SpawnPropagationTimeoutMs);

        Assert.True(reached, $"Entity {networkId} should appear in CGF ghost map within {SpawnPropagationTimeoutMs} ms");
    }

    [Fact]
    public void DestroyedEntity_PurgedFromCgfGhostRepo()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness(RunMode.SimHost, domainId);
        using var cgf     = new CgfHarness(domainId);

        simHost.WarmUp();
        cgf.PumpFrames(20);

        long tkbType  = HrotEnvironment.CreateTkb().GetAll().First().TkbType;
        var  spawnPos = new Fdp.Modules.Geographic.GeoPoint { Latitude = 0.0, Longitude = 0.0 };

        long networkId = simHost.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        // Wait until entity appears in CGF
        bool appeared = PumpBothUntil(simHost, cgf,
            () => cgf.CgfSvc.GhostEntityMap?.ContainsKey(networkId) == true,
            SpawnPropagationTimeoutMs);
        Assert.True(appeared, "Entity must appear in CGF before we can test its removal");

        // Destroy via SimHost bus
        simHost.SimHost.App.World.Bus.PublishManaged(
            new FDP.Toolkit.NetworkSpawning.Events.DestroyEntityCommand
            {
                NetworkId = networkId, Reason = "test-destroy"
            });

        bool purged = PumpBothUntil(simHost, cgf,
            () => cgf.CgfSvc.GhostEntityMap?.ContainsKey(networkId) == false,
            SpawnPropagationTimeoutMs);

        Assert.True(purged, $"Entity {networkId} must be purged from CGF ghost map within {SpawnPropagationTimeoutMs} ms");
    }

    [Fact]
    public void CgfAiIntent_ReachesSimHost_ViaDds()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness(RunMode.SimHost, domainId);
        using var cgf     = new CgfHarness(domainId);

        simHost.WarmUp();
        cgf.PumpFrames(20);

        long tkbType  = HrotEnvironment.CreateTkb().GetAll().First().TkbType;
        var  spawnPos = new Fdp.Modules.Geographic.GeoPoint { Latitude = 0.0, Longitude = 0.0 };
        long networkId = simHost.SimHost.TestHook_SpawnEntity(tkbType, spawnPos);

        // Wait for CGF to receive the entity
        bool appeared = PumpBothUntil(simHost, cgf,
            () => cgf.CgfSvc.GhostEntityMap?.ContainsKey(networkId) == true,
            SpawnPropagationTimeoutMs);
        Assert.True(appeared, "Entity must appear in CGF before waiting for AI intent");

        // Wait for CGF AI to assign a mission and that mission to propagate back to SimHost
        bool missionSet = PumpBothUntil(simHost, cgf,
            () =>
            {
                if (!simHost.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var e)) return false;
                // Check if entity has received an AI mission assignment (any non-null mission state)
                return simHost.SimHost.App.World.HasComponent<Hrot.Map.Common.EcsComponents.NavigationIntent>(e);
            },
            MissionAssignmentTimeoutMs);

        Assert.True(missionSet, $"CGF AI mission intent should reach SimHost within {MissionAssignmentTimeoutMs} ms");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static bool PumpBothUntil(
        HrotRunnerHarness simHost, CgfHarness cgf,
        System.Func<bool> condition, int timeoutMs)
    {
        if (condition()) return true;
        var deadline = System.DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (System.DateTime.UtcNow < deadline)
        {
            simHost.PumpFrames(1);
            cgf.PumpFrames(1);
            if (condition()) return true;
            System.Threading.Thread.Sleep(5);
        }
        return false;
    }
}
```

**Designer notes:**

1. `cgf.CgfSvc.GhostEntityMap` requires Task 3 (CgfSubsystem.GhostEntityMap property).  
2. `simHost.SimHost.TestHook_EntityMap.TryGetEntity(...)` already exists on `SimHostSubsystem`.  
3. `simHost.SimHost.App.World.HasComponent<NavigationIntent>(e)` — verify actual component type and method name. IF `NavigationIntent` doesn't exist, substitute with any CGF-driven intent component (check `Hrot.Map.Common.EcsComponents` namespace). If none applies, simplify test 3 to assert that the entity's ghost record has been processed by CGF (e.g., `cgf.CgfSvc.GhostEntityMap` has the entity promoted to Active state).  
4. If `HrotRunnerHarness.PumpFrames` doesn't exist (only `WarmUp` and background DDS), drive both harnesses alternately with `Thread.Sleep(10)` between iterations.

---

## 🔎 File Change Summary

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` | UPDATE — optional `FdpEventBus?` ctor param; publish `WorldResetEvent` on bus |
| `Hrot.Map.Common/Hrot.Map.Common.csproj` | UPDATE — add `InternalsVisibleTo` for `Hrot.ClusterRunner.Integration.Tests` |
| `Hrot.ClusterRunner/Services/CgfSubsystem.cs` | UPDATE — store `_entityMap` field; expose `GhostEntityMap` internal property |
| `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | UPDATE — pass Bus to ScenarioFileService; expose FileService; add SetTranslatorPacks |
| `Hrot.ClusterRunner.Integration.Tests/EditorFileIOIntegrationTests.cs` | NEW — 4 offline file I/O tests |
| `Hrot.ClusterRunner.Integration.Tests/FeatureSwitchRcuIntegrationTests.cs` | NEW — 4 feature switch tests (3 offline + 1 spy) |
| `Hrot.ClusterRunner.Integration.Tests/DistributedBrainMuscleIntegrationTests.cs` | NEW — 3 DDS brain-muscle tests |

---

## ⚠️ Known Pitfalls and Notes

1. **`PumpUntil` for feature switch tasks.** `SwitchToExternalAsync` returns a `Task` that completes on the NEXT `kernel.Update()` call after the RCU drain. Use `PumpUntil(() => switchTask.IsCompleted, 5000)` after calling `SwitchToExternalAsync()`.

2. **`SpyEgressPack` / `CycloneEgressSystem`.** If `CycloneEgressSystem` constructor is not accessible, implement `SpyEgressPack.Tick` to call `_translator.PollIngress(view)` instead — check what method `IDescriptorTranslator` exposes. The translator's `PollIngress` reads managed events from the bus and writes to the `IDdsWriter`. If `PollIngress` doesn't exist, check `ScanAndPublish` or equivalent — look at `CycloneEgressSystem.Execute` source for the pattern.

3. **`SimHostMode` check in feature switch tests.** After `SwitchToExternalAsync`, `harness.Editor.CurrentMode` should be `SimHostMode.External` (the mode is set AFTER the in async method after `await`s complete). The `Task` is only complete after the RCU drain AND after `_currentMode = External` is set.

4. **R006 CgfAiIntent test may need simplification.** If `NavigationIntent` or an equivalent component that CGF sends back doesn't clearly exist, simplify IT-3c to just check that CGF has received and processed the entity (alternative: skip IT-3c with `[Fact(Skip = "AI mission assignment not yet addressable")]` and note in report).

5. **R006 tests require CycloneDDS.** They will fail if CycloneDDS native libraries are not present. This is acceptable — they match the existing DDS-dependent test pattern in the codebase (HarnessSmoke/SpawnMovingVehicle tests skip on machines without DDS).

6. **GlobalTime field name.** `GlobalTime` is in `Fdp.Kernel`. The field may be `TotalTime` (double) or `T` (float). Check the actual struct definition: `Fdp.Kernel/CoreComponents/SimComponents.cs` or equivalent. Use the correct field name in IT-2a.

---

## 🧪 Testing Requirements

**Minimum test counts:**

| Project | Tests Before | Tests Added | Expected |
|---------|-------------|------------|---------|
| `Hrot.Editor.Tests` | 20 | 0 | 20 |
| `Hrot.ClusterRunner.Integration.Tests` | ~10 | +11 (4+4+3) | ~21 |

**Quality:** PumpUntil for all async-style assertions; no `Thread.Sleep` except in `PumpBothUntil` helper.

**Acceptable skips:** DDS-dependent R006 tests may skip/fail in environments without CycloneDDS native library.

---

## 📊 Report Requirements

Submit `.dev/packs-2/reports/BATCH-11-REPORT.md` with:

**Q1:** Was IT-3d (DDS spy test) implementable with `InternalsVisibleTo`? Did `SpawnEntityCommandEgressTranslator` get the write?

**Q2:** Did the `WorldResetEvent` bus publish work for IT-2a?

**Q3:** Was IT-3c (AI intent round-trip) implementable, or did you need to simplify/skip it?

**Q4:** Did R006 DDS tests pass or skip? Any issues with domain counter conflicts?

**Q5:** Suggested commit message for this final batch?

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] `ScenarioFileService` publishes `WorldResetEvent` on the bus (when one is configured)
- [ ] `CgfSubsystem.GhostEntityMap` internal property exists
- [ ] `EditorHarness` passes `Bus` to `ScenarioFileService`; has `SetTranslatorPacks` method
- [ ] `EditorFileIOIntegrationTests`: 4/4 pass
- [ ] `FeatureSwitchRcuIntegrationTests`: 3/4 pass (IT-3d optional if spy pack is too complex — note in report)
- [ ] `DistributedBrainMuscleIntegrationTests`: 3/3 pass OR skip gracefully on machines without DDS
- [ ] `dotnet test Hrot.Editor.Tests` passes (20/20)
- [ ] `dotnet test Hrot.ClusterRunner.Integration.Tests` (offline tests green, DDS tests skip/pass)
- [ ] All files compile with zero errors

---
