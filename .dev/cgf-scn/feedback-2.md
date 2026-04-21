I would like to test the new distributed scenario loading. so far scenario loading is only available in hrot.editor. How to add some UI to the distributed setup? In the "Orchestrator" UI panel there is a "scenario" collapsible header with scenario related stuff but is seems disconnected from data - combo "Select Scenario" not showing the list of scenarios (saved from Hrot.Editor)

---------

The disconnection in the Orchestrator UI is caused by a missing dependency injection at the orchestration composition root. Additionally, even if the UI were populated, distributed scenario loading would silently fail due to two severe architectural regressions introduced during recent refactoring efforts.

To successfully execute a distributed scenario load from the Orchestrator, you must address these three architectural gaps.

### 1. Disconnected Orchestrator UI (Missing Storage Gateway)
The `ClusterMaster` relies on a `StorageGatewayModule` to scan the local disk for scenario files and publish the `AssetInventoryUpdateEvent` that populates the UI cache. In `OrchestratorSubsystem.Initialize`, this gateway is never instantiated or assigned, leaving the `ClusterMaster` completely blind to the disk.

**Correction in `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`:**
Immediately after constructing the `ClusterMaster`, instantiate the storage gateway and bind it to the standard staging directory:

```csharp
// Inside OrchestratorSubsystem.Initialize(SubsystemConfig config)
_bus          = new FdpEventBus();
_clusterMaster = new ClusterMaster(_bus, _config);

// FIX: Wire the storage gateway so the cluster master can scan local/NAS scenarios
// and publish AssetInventoryUpdateEvent to populate the UI combo box.
var storageGateway = new StorageGatewayModule();
_clusterMaster.SetStorageGateway(storageGateway, @"C:\FDP_Temp");
```

### 2. Silent Loading Failure on SimHost (Mismatched Subsystem Type)
The `Hrot.Editor` subsystem correctly serializes scenarios using the unified `"Hrot.Scenario"` envelope identifier. However, `SimHostApp` still hardcodes its deserialization pipeline to strictly look for `"Hrot.SimHost"`. When the Orchestrator commands the cluster to load, SimHost will silently skip the Editor's file because the header does not match.

**Correction in `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`:**
You must update the `ScenarioSerializerBuilder` to match the Editor's output format.

```csharp
// Inside SimHostApp.OnLoad()
// Change "Hrot.SimHost" to "Hrot.Scenario"
var scenarioSerializer = new ScenarioSerializerBuilder("Hrot.Scenario")
    .RegisterTranslator(new Hrot.SimHost.Serializers.TargetMemoryTranslator())
    .RegisterTranslator(new Hrot.SimHost.Serializers.PassengerBufferTranslator())
    .RegisterTranslator(new Hrot.SimHost.Serializers.WeaponChannelTranslator())
    .Build();
```

### 3. Brain-Dead Entities on CGF (Missing Load Handlers)
During the migration from `CgfApplication` to `CgfSubsystem` (`EAM-M003`), the scenario and episode load handlers (`CgfScenarioLoadHandler` and `CgfEpisodeLoadHandler`) were inadvertently dropped. As a result, the Brain node acknowledges the load command but never extracts or spawns the cognitive components (B-Trees, Mission Plans). The entities will spawn on SimHost, but they will be brain-dead.

**Correction in `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`:**
You must manually restore the extraction engine and the load handlers immediately after building the cluster slave.

```csharp
// Inside CgfSubsystem.Initialize(SubsystemConfig config)
if (bootstrapper.RecordReplayController != null)
{
    newClusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
        bootstrapper.RecordReplayController,
        simGroup:              null,
        lifecycleGroup:        null,
        bypassLifecycleToggle: null,
        storageDirectory:      @"C:\FDP_Temp"));
}

// FIX: Restore the CGF-authoritative scenario and episode load handlers dropped during migration.
var scenarioSerializer = new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Hrot.Scenario").Build();
var storageProvider    = new Fdp.Toolkit.Orchestration.LocalDiskStorageProvider(@"C:\FDP_Temp");
var scenarioLoader     = new Hrot.Map.Common.Scenario.HrotScenarioLoader(storageProvider, scenarioSerializer.SubsystemType);
var extractor          = new Hrot.CGF.Orchestration.StagingEntityExtractor();
var cgfIdAllocator     = new Hrot.Core.Network.SequentialIdAllocator();
var behaviorRemapper   = Hrot.CGF.Configuration.CgfDoctrineSetup.CreateBehaviorRemapper();

newClusterSlave.RegisterHandler(new Hrot.CGF.Orchestration.Handlers.CgfScenarioLoadHandler(
    scenarioSerializer, scenarioLoader, extractor, _scenarioSource!, cgfIdAllocator, behaviorRemapper));

newClusterSlave.RegisterHandler(new Hrot.CGF.Orchestration.Handlers.CgfEpisodeLoadHandler(
    scenarioSerializer, scenarioLoader, extractor, _scenarioSource!, cgfIdAllocator, _context.World, behaviorRemapper));
```

Applying these three fixes will perfectly align the data flow, allowing the Orchestrator to discover scenarios authored by `Hrot.Editor` and properly orchestrate the 2-Phase Commit load sequence across both the CGF and SimHost distributed boundaries.

---------------


cool! pls let's avoid magic strings like @"C:\FDP_Temp" or "Hrot.Scenario". These must be symbolic constants in some assembly accessible from all places where it is used.

------------------

The presence of magic strings like `@"C:\FDP_Temp"` and `"Hrot.Scenario"` scattered across both the FDP engine toolkits and the HROT application tier is a severe violation of the DRY principle. Hardcoding paths and schema identifiers across independent subsystems guarantees that future topology or data-contract changes will cause silent integration failures.

Architecturally, these strings belong to two distinct domains and must be centralized in their respective base assemblies.

### 1. Engine-Level Orchestration Constants
The default staging path (`@"C:\FDP_Temp"`) is a cluster deployment convention. It belongs in the FDP orchestration toolkit so that the engine's default fallbacks and the application's configuration properties share the exact same baseline.

Create a constants class in `FDP/Toolkits/Fdp.Toolkits/Orchestration/OrchestrationConstants.cs`:

```csharp
namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Global orchestration constants defining cluster-wide deployment conventions.
    /// </summary>
    public static class OrchestrationConstants
    {
        /// <summary>
        /// Default root directory for scenario staging, checkpoints, and archives.
        /// </summary>
        public const string DefaultStagingDirectory = @"C:\FDP_Temp";
    }
}
```

You must then refactor the following injection points to use this constant:
*   **`LocalDiskStorageProvider.cs`**: `private const string DefaultLocalTempRoot = OrchestrationConstants.DefaultStagingDirectory;`
*   **`HrotNodeConfig.cs`**: `public string LocalTempRoot { get; set; } = OrchestrationConstants.DefaultStagingDirectory;`
*   **`NodeConfiguration.cs`**: `public string LocalTempRoot { get; init; } = OrchestrationConstants.DefaultStagingDirectory;`

### 2. Application-Level Subsystem Identifiers
Strings like `"Hrot.Scenario"` and `"Hrot.SimHost"` define the serialized JSON schema contract (the `SubsystemType` header). These are not FDP engine concepts; they belong strictly to the HROT application layer.

Since these identifiers are consumed by the offline Editor, the CGF, and SimHost, they must be defined in the lowest-level shared application assembly, which is `Hrot.Common`.

Create a registry in `Hrot/Engine/Hrot.Common/Scenario/HrotSubsystemTypes.cs`:

```csharp
namespace Hrot.Common.Scenario
{
    /// <summary>
    /// Stable string identifiers for HROT subsystem serialization contracts.
    /// Used in the ScenarioHeader to ensure load handlers only process matching data.
    /// </summary>
    public static class HrotSubsystemTypes
    {
        /// <summary>Cross-node, engine-agnostic scenario payload.</summary>
        public const string Scenario = "Hrot.Scenario";
        
        /// <summary>SimHost-authoritative snapshot or scenario payload.</summary>
        public const string SimHost = "Hrot.SimHost";
        
        /// <summary>CGF-authoritative snapshot or scenario payload.</summary>
        public const string Cgf = "Hrot.CGF";
        
        /// <summary>IG-specific visual configuration payload.</summary>
        public const string Ig = "Hrot.IG";
    }
}
```

### 3. Applying the Contract
Once these constants are in place, you must eradicate the raw strings from the composition roots.

In **`EditorBootstrap.cs`**:
```csharp
public static string ScenariosRoot { get; } = OrchestrationConstants.DefaultStagingDirectory;

public static ScenarioFileService CreateFileService()
{
    var serializer = new ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario).Build();
    // ...
}
```

In **`CgfApplication.cs`**:
```csharp
public CgfApplication(..., string localTempRoot = OrchestrationConstants.DefaultStagingDirectory, ...)
```

In **`SimHostApp.cs`**:
```csharp
var scenarioSerializer = new ScenarioSerializerBuilder(HrotSubsystemTypes.SimHost)
// ...
```

By establishing these two boundary constants, you ensure that any deployment path change or schema rename is enforced at compile time, eliminating the risk of a runtime `InvalidOperationException` due to a mismatched scenario loader header or a missing staging directory.

------------

to test the scenario loading autonomously during development, i would like to have a test that saves a sample scenario from hrot.editor and loads it in distributed configuration (similar as clusterrunner -m all) , checking if entities were created across the cluster and entity ids were remapper properly. pls suggest specification for such a test. It should serve as success condition of the distributed scenario loading effort.

---------------

Here is the architectural specification for an autonomous end-to-end integration test validating distributed scenario loading and stable ID remapping. 

This specification enforces strict isolation between the offline authoring environment and the live distributed cluster, mirroring the exact boundaries crossed in a production deployment.

### Test Specification: `DistributedScenarioLoadIntegrationTests`

**Objective:**
Prove that a scenario authored in the offline ECS environment can be serialized to disk, loaded by a distributed cluster via the 2-Phase Commit orchestration pipeline, instantiated across all node roles, and that cross-entity network references embedded in mission JSON are successfully patched to match the new live network IDs.

**Infrastructure Prerequisites:**
You will utilize the existing `EditorHarness` for the offline authoring phase and the `HrotRunnerHarness` for the distributed execution phase. Both must operate sequentially within the same test method to guarantee state isolation.

#### Phase 1: Authoring & Serialization (Cold Path)
This phase verifies that cross-entity relationships are correctly encoded using the staging network IDs.

1.  **Initialize Authoring:** Instantiate an `EditorHarness`. This harness runs entirely without a DDS participant, allocating IDs sequentially.
2.  **Entity Allocation:** 
    *   Spawn Entity A (e.g., an M1 Abrams). Record its offline network ID (e.g., `1001`).
    *   Spawn Entity B (e.g., a Bradley IFV). Record its offline network ID (e.g., `1002`).
3.  **Behavior Binding:** Attach an `ActiveMissionPlan` to Entity A. Add a task with the `FireAtTarget` behavior, encoding Entity B's offline ID (`1002`) into the `targetNetworkId` JSON field.
4.  **Serialization:** Invoke `harness.Editor.SaveScenarioAs("test_distributed_load")`, which writes the `Hrot.Scenario` envelope and ECS DOM to the configured `C:\FDP_Temp` directory.
5.  **Teardown:** Dispose the `EditorHarness` to guarantee zero memory leakage into the live phase.

#### Phase 2: Distributed Cluster Boot & Injection (Hot Path)
This phase proves the `ClusterMaster` orchestration, file loading, extraction, and cluster-wide spawning pipelines.

1.  **Cluster Boot:** Instantiate an `HrotRunnerHarness("simhost,ig,excon,cgf", domainId)`.
2.  **Discovery Settle:** Execute a warmup pump loop until the `ClusterMaster` node roster registers all expected active subsystems.
3.  **State Transition:** Dispatch a `ClusterOpRequest` to the Orchestrator with `OperationType = ClusterOpType.TransitionState` targeting `OperatingLive` (State 31) and passing the `ScenarioId` created in Phase 1.
4.  **Synchronization:** Use `harness.PumpUntil` to advance simulation frames until all nodes report `OperatingLive`.

#### Phase 3: Architectural Assertions
Once the scenario has loaded, enforce the following validation gates across the subsystem boundaries.

**Assertion A: Cluster-Wide Instantiation**
Query the respective `EntityRepository` or `NetworkEntityMap` for each subsystem to ensure exactly 2 entities were spawned and promoted.
*   `SimHost` (Muscle) must contain the entities with `SimTransform` and `NetworkAuthority`.
*   `IG` (Presentation) must contain the entities with `ResolvedStyle` and `EntityLifecycle.Active`.
*   `CGF` (Brain) must contain the ghost entities with the cognitive components (e.g., `DoctrineState`).

**Assertion B: ID Translation & Schema Drift Protection**
This is the critical success condition for the remapping effort.
1.  Extract the *new* network IDs from the live `SimHost` map. Assert that `NewEntityId_A != 1001` and `NewEntityId_B != 1002`. This proves the `SequentialIdAllocator` (or `DdsIdAllocator`) generated fresh IDs for the live session.
2.  Query the `CGF` node for Entity A's `ActiveMissionPlan`.
3.  Extract the `BehaviorParams` JSON string from the active task.
4.  Deserialize the JSON and assert that the `TargetNetworkId` field exactly equals `NewEntityId_B`. 

### Implementation Draft

Here is the structural outline for your test class:

```csharp
[Collection("HeavyE2ETests")]
public sealed class DistributedScenarioLoadTests : IDisposable
{
    private readonly string _scenarioId = "test_distributed_load_" + Guid.NewGuid().ToString("N");
    
    [Fact(Timeout = 60_000)]
    public async Task DistributedLoad_TranslatesNetworkIds_AndSpawnsAcrossCluster()
    {
        long offlineAttackerId, offlineTargetId;
        
        // Phase 1: Offline Authoring
        using (var editorHarness = new EditorHarness())
        {
            // Spawn entities and assign TargetNetworkId = offlineTargetId in ActiveMissionPlan
            // ...
            editorHarness.Editor.SaveScenarioAs(_scenarioId);
        }
        
        // Phase 2: Live Cluster Injection
        int domainId = NextDomainId();
        using var liveHarness = new HrotRunnerHarness("simhost,ig,excon,cgf", domainId);
        liveHarness.PumpFrames(20); // Discovery warmup
        
        var master = liveHarness.OrchestratorSvc.TestHook_ClusterMaster!;
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson = JsonSerializer.Serialize(new { TargetState = 31, ScenarioId = _scenarioId })
        });
        
        liveHarness.PumpUntil(() => (int)master.CurrentSystemState == 31, 15_000);
        
        // Phase 3: Assertions
        var cgfMap = liveHarness.Cgf!.GhostEntityMap!;
        var simMap = liveHarness.SimHost.TestHook_EntityMap;
        
        // Assert maps have exactly 2 entities, obtain newAttackerId and newTargetId
        // ...
        
        Assert.NotEqual(offlineAttackerId, newAttackerId);
        Assert.NotEqual(offlineTargetId, newTargetId);
        
        var cgfWorld = liveHarness.Cgf.World!;
        cgfMap.TryGetEntity(newAttackerId, out var cgfAttacker);
        var missionPlan = cgfWorld.GetComponent<ActiveMissionPlan>(cgfAttacker);
        
        // Deserialize missionPlan.Plan.Tasks.BehaviorParams and assert
        // dto.TargetNetworkId == newTargetId
    }
}
```

This specification rigorously tests the ID extraction (`Pass 1`) and the remapping mutation (`Pass 2`) occurring within the `StagingEntityExtractor` without coupling to the brittle magic strings or tight `ImGui` loops we previously refactored out.

--------------

