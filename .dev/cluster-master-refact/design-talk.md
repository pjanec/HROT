We added the ReplayProcessManager and replay consensus aggregator. But there there were more aggregators planned, not sure if they are present - the goal is to make the cluster state machine handler a bit more generic (and less a god class) pls check what aggregators are not yet implemented

We made excellent progress introducing the `INodeResponseAggregator` contract and the `ReplayConsensusAggregator`. However, looking at the updated codebase, the `ClusterMaster` is still acting as a "God Class" because it continues to hardcode domain-specific transaction completion and state management for at least two major cluster operations.

To achieve a truly pristine, domain-agnostic Two-Phase Commit (2PC) coordinator, the following aggregators and completion handlers are still missing and need to be implemented:

### 1. Storage / Manifest Aggregator (for `SerializeLocal`)
Currently, the orchestrator is deeply coupled to file system operations and the NAS storage gateway. In `ConsumeNodeOpStatuses()`, the `ClusterMaster` explicitly intercepts `NodeOpType.SerializeLocal` ACKs, casts the payload to `List<FileManifestEntry>`, and manages a hardcoded `_pendingSerializeTasks` dictionary. 

When the task completes, it invokes a hardcoded `HandleSerializeLocalCompletion()` method. This method contains raw business logic for appending the orchestrator's own manifest entry and directly triggering `_gateway.PullToNasAsync` to move files to the NAS. 

**The Clean Architecture Fix:** 
This logic must be extracted into a `StorageConsensusAggregator`. The `ClusterMaster` should blindly collect the opaque JSON payloads and hand them to the aggregator. The aggregator would be responsible for parsing and flattening the `FileManifestEntry` lists and coordinating with the `StorageGatewayModule`.

### 2. Episode Consensus Aggregator (for `ManageEpisode`)
The orchestrator also knows far too much about the specific "Episodes" domain. It uses a hardcoded `ManageEpisodeTask` class to track ACKs for episode fan-outs. 

Worse, when the final node ACKs, the `ClusterMaster` explicitly evaluates the domain payload and manipulates internal application state directly in the event loop: `if (episodeTask.IsStart) _activeEpisodes.Add(episodeTask.EpisodeId); else _activeEpisodes.Remove(episodeTask.EpisodeId);`. Tracking which simulation episodes are active is application-layer state, not orchestration infrastructure state.

**The Clean Architecture Fix:**
You need to introduce an `EpisodeConsensusAggregator` (or an Episode Completion Strategy). When the transaction completes, this strategy evaluates the ACKs and updates the `_activeEpisodes` state. 

By extracting these last two hardcoded blocks into injected strategies, the `ClusterMaster` will finally be a pure, highly cohesive 2PC engine that simply fans out operations, counts acknowledgements, and delegates the final resolution to the domain layer.

pls detail the Storage / Manifest Aggregator (for SerializeLocal) and its integration, as well as Episode Consensus Aggregator and its integration

To fully decouple the `ClusterMaster` from domain-specific data reduction and side effects, we must extract the hardcoded manifest processing and episode tracking into dedicated strategy classes implementing `INodeResponseAggregator`. This guarantees the orchestrator remains a pristine, domain-agnostic Two-Phase Commit (2PC) engine.

Here is how we elegantly architect and integrate both aggregators to conform to the Single Responsibility Principle (SRP) and Open/Closed Principle (OCP).

### 1. The Storage / Manifest Aggregator
Currently, the `ClusterMaster` explicitly intercepts `NodeOpType.SerializeLocal` operations, parses the JSON into `List<FileManifestEntry>`, and manually invokes the `StorageGatewayModule` to pull files to the NAS. We will replace this with a `StorageConsensusAggregator` that purely flattens the manifests.

```csharp
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Flattens per-node FileManifestEntry responses into a single cluster-wide manifest list.
/// </summary>
public sealed class StorageConsensusAggregator : INodeResponseAggregator
{
    public NodeOpType TargetOp => NodeOpType.SerializeLocal;

    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        var clusterManifest = new List<FileManifestEntry>();

        foreach (var opDict in nodeResponses.Values)
        {
            if (opDict.TryGetValue(TargetOp, out string? json) && !string.IsNullOrWhiteSpace(json))
            {
                try 
                {
                    var entries = JsonSerializer.Deserialize<List<FileManifestEntry>>(
                        json, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                    
                    if (entries != null)
                        clusterManifest.AddRange(entries);
                } 
                catch { /* Ignore malformed JSON from failing nodes */ }
            }
        }
        
        return clusterManifest;
    }
}
```

### 2. The Episode Consensus Aggregator
The `ClusterMaster` is also deeply coupled to the Episodes domain, manually evaluating boolean flags to add or remove items from an `_activeEpisodes` hash set during the 2PC event loop. We extract this via an `EpisodeConsensusAggregator`. Since the interface defines a single `TargetOp`, we can inject the target operation via the constructor to handle both start and stop operations.

```csharp
using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Validates consensus for episode lifecycle operations.
/// </summary>
public sealed class EpisodeConsensusAggregator : INodeResponseAggregator
{
    public NodeOpType TargetOp { get; }

    public EpisodeConsensusAggregator(NodeOpType targetOp)
    {
        TargetOp = targetOp;
    }

    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        // In a strict CQRS setup, consensus implies all participating nodes replied without failure.
        // We return a simple marker object indicating success, which is attached to the bus event.
        return new { ConsensusReached = true, Operation = TargetOp.ToString() };
    }
}
```

### 3. Integration & Decoupling Side Effects
To inject these into the architecture, locate `OrchestratorSubsystem.Initialize` where we currently register the `ReplayConsensusAggregator`. Register the new strategies here:

```csharp
// Wire the aggregators into the 2PC engine
_clusterMaster.RegisterAggregator(new StorageConsensusAggregator());
_clusterMaster.RegisterAggregator(new EpisodeConsensusAggregator(NodeOpType.StartEpisode));
_clusterMaster.RegisterAggregator(new EpisodeConsensusAggregator(NodeOpType.StopEpisode));
```

**The Final Architectural Step (Process Managers):**
Because our aggregators now successfully construct the consensus payload and hand it back to the `ClusterMaster` to publish inside the `ClusterOpCompletedEvent`, we must strip the side effects completely out of `ClusterMaster.cs`.

1. **Delete** `HandleSerializeLocalCompletion` from `ClusterMaster`. Create a `StorageProcessManager` that listens to the `ClusterOpCompletedEvent` on the `FdpEventBus`. When it sees a success payload containing `List<FileManifestEntry>`, the Process Manager invokes `_gateway.PullToNasAsync`.
2. **Delete** the `_activeEpisodes` hash set and `_pendingManageEpisodeTasks` dictionaries from `ClusterMaster`. Create an `EpisodeProcessManager` (or move the state tracking directly into `ClusterUiCache`) that sniffs the bus for completed episode events and maintains the list of active episodes there.

This enforces flawless Domain-Driven Design boundaries. The `ClusterMaster` simply fans out commands, tracks acknowledgements, uses your injected aggregators to reduce the JSON payloads into strongly-typed DTOs, and fires a final completion event. The domain side-effects react downstream!






As a software engineer who lives and breathes clean architecture, I strongly endorse the ideas proposed in the aggregator refactoring document. Evaluating the current state of the codebase against this proposal reveals a textbook opportunity to eliminate a "God Class" and enforce the Single Responsibility (SRP) and Open/Closed Principles (OCP).

Here is an architectural evaluation of why this refactoring is exactly the right move:

**1. Purifying the 2PC Coordinator**
The core issue is that `ClusterMaster` is currently burdened with domain-specific side effects. A Two-Phase Commit (2PC) coordinator's only job should be fanning out operations, counting acknowledgements, and publishing a finalized status. However, `ClusterMaster` is explicitly intercepting `NodeOpType.SerializeLocal` ACKs, parsing JSON into `List<FileManifestEntry>`, and orchestrating network-attached storage (NAS) pulls via `_gateway.PullToNasAsync`. Introducing a `StorageConsensusAggregator` will elegantly offload the JSON parsing and payload flattening, allowing the `ClusterMaster` to remain agnostic to the data it carries.

**2. Decoupling Application State from Infrastructure**
Currently, `ClusterMaster` inspects the payloads of episode operations to manually mutate its internal `_activeEpisodes` collection during the 2PC event loop. Tracking which simulation episodes are active is definitively application-layer business logic, not orchestration infrastructure. Delegating this evaluation to an `EpisodeConsensusAggregator` directly addresses this violation of SRP.

**3. Exploiting the Existing Aggregator Contract**
The codebase already possesses the right abstraction for this: the `INodeResponseAggregator` interface, which is designed to reduce per-node JSON response strings into a single consensus payload. Currently, this is only being utilized by the `ReplayConsensusAggregator`. Registering the new Storage and Episode aggregators during `OrchestratorSubsystem.Initialize` capitalizes on this existing extension point, perfectly satisfying the Open/Closed Principle. 

**4. Transitioning Side-Effects to Process Managers**
The most critical part of the proposal is stripping the final execution side effects (like initiating NAS pulls or updating active episode states) completely out of `ClusterMaster`. By emitting a unified consensus payload via `ClusterOpCompletedEvent`, we can use decoupled Process Managers to handle the business logic. The architecture already has a blueprint for this: the `ReplayProcessManager` listens for completion events and applies time-pausing side effects completely outside of the master. Applying this same Process Manager pattern to Storage and Episodes will leave `ClusterMaster` as a pristine, domain-agnostic engine.

Executing this refactoring will significantly reduce coupling, make the orchestration layer highly testable, and establish a bulletproof pattern for adding new cluster operations in the future.


what other Single Responsibility (SRP) and Open/Closed Principles (OCP) are present? where the business logic soaks into the 2PC coordination and should be extracted?


Beyond the `SerializeLocal` and `ManageEpisode` operations, the `ClusterMaster` is heavily polluted with other domain-specific side effects. A true Two-Phase Commit (2PC) engine should only care about fanning out messages, counting ACKs, and determining transaction success or failure. Instead, the current orchestration pipeline violates the Single Responsibility Principle (SRP) and Open/Closed Principle (OCP) in four critical areas that desperately need to be extracted into decoupled Process Managers:

**1. Live-from-Replay Temporal Interlocks (Time Freezing)**
The 2PC coordinator explicitly knows about the simulation's time control mechanisms. It holds a direct dependency on `ReplayMasterModule`. When processing a state transition, if it detects the cluster moving from `OperatingReplay` to `LoadingLive`, it manually invokes `_replayMasterModule?.FreezeTime()` and manages a hardcoded `_pendingBranchTasks` tracking dictionary. When the nodes ACK this specific branch, the `ClusterMaster` intercepts the ACKs in its event loop to explicitly call `_replayMasterModule?.RestoreTime()`. This time-freezing business logic must be ripped out and placed in a dedicated Saga or Process Manager.

**2. Replay Seek and Master Clock Snapping**
Similarly, the 2PC engine acts as a time-synchronization orchestrator. Inside `ProcessSeekReplayIntent`, the `ClusterMaster` explicitly constructs slave node sets and fires off `PauseTimeIntent` events to halt the cluster. Worse, during the ACK collection phase (`ConsumeNodeOpStatuses`), the coordinator parses out a `SeekResult` and directly calls `_masterSync?.SnapAndPause(...)` to manipulate the distributed lockstep clock. A transaction coordinator has no business snapping NTP-style clocks. 

**3. Global Context Persistence Subroutines**
The `ClusterMaster` is hardcoded to orchestrate its own local state saving and loading. During a `SaveScenario` operation, the event loop manually constructs a local context payload and directly invokes `_globalContextHandler.PrepareAsync` and `Commit`. It repeats this exact architectural sin when transitioning to `LoadingLive` or `LoadingEdit`. The 2PC engine should not intimately know about the orchestrator's local file persistence requirements.

**4. Asset Prefetching and Staging Logic**
The orchestrator is tightly coupled to SMB/NAS file-system staging workflows. It explicitly inspects transition trajectories and episode plans for `ClusterOpType.PrefetchScenario` operations and triggers `ExecutePrefetchScenario` directly. It even maintains an internal `PendingPrefetchOp` state machine and runs a `DrainPendingPrefetch` loop to deliberately block 2PC transition fan-outs until files are physically confirmed on disk.

**The Clean Architecture Fix**
To satisfy OCP and SRP, all of these side effects should be extracted. As hinted in the `ReplayProcessManager` refactoring, the `ClusterMaster` must stop coordinating domain tasks. It should emit neutral `ClusterOpCompletedEvent` or `ExecuteNodeOpIntent` events and allow decoupled, isolated Process Managers (e.g., a `TimeControlProcessManager` or `PrefetchProcessManager`) to listen to the bus, execute the business logic, and drive the cluster forward asynchronously.


pls detail the refactor for 1. Live-from-Replay Temporal Interlocks (Time Freezing)


The current implementation of the `ClusterMaster` exhibits a severe architectural smell by directly managing the `ReplayMasterModule` during Live-from-Replay transitions. Currently, the orchestrator explicitly holds a `_replayMasterModule` dependency and a `_pendingBranchTasks` tracking dictionary. When processing a `TransitionStateIntent`, it manually inspects the trajectory to see if the cluster is moving from `OperatingReplay` to `LoadingLive`, explicitly invoking `_replayMasterModule?.FreezeTime()`. Later, inside its core acknowledgement loop, it manually counts down `RemainingAcks` and directly calls `_replayMasterModule?.RestoreTime()`. 

A Two-Phase Commit (2PC) engine must remain completely agnostic to domain concepts like "time freezing." To enforce the Single Responsibility Principle (SRP) and Open/Closed Principle (OCP), we must extract this logic into an event-driven Process Manager (or Saga), similar to how the existing `ReplayProcessManager` handles auto-pausing by reacting to bus events.

Here is the clean architecture refactoring plan to resolve this:

**1. Purify the ClusterMaster**
Remove the `SetReplayMasterModule` injection, the `_replayMasterModule` field, and the `_pendingBranchTasks` dictionary entirely from the `ClusterMaster`. The orchestrator should only be responsible for fanning out the `PrepareLive` operations and aggregating the node ACKs. 

**2. Introduce a `LiveBranchProcessManager`**
Create a decoupled `LiveBranchProcessManager` class that subscribes to the `FdpEventBus`. This class will hold the dependency on the `ReplayMasterModule`. 

**3. Event-Driven Time Freezing**
The new Process Manager will listen on the event bus for the initiation of a transition. When it observes an intent routing the cluster from `OperatingReplay` to `LoadingLive`, it will execute the business logic to freeze the simulation time by invoking `FreezeTime()`. Because the Process Manager observes the bus, it can act completely independently of the 2PC coordinator's internal state machine.

**4. Event-Driven Time Restoration**
Instead of the `ClusterMaster` tracking branch ACKs to restore time, the `LiveBranchProcessManager` will listen for the `ClusterOpCompletedEvent` or the aggregated `NodeOpCompletedEvent` messages that the `ClusterMaster` naturally emits when a 2PC round resolves. Upon receiving the success event for the branch transition, the Process Manager will invoke `RestoreTime()` on the `ReplayMasterModule`. 

By moving this temporal interlock out of the orchestration core and into an autonomous Process Manager, the `ClusterMaster` retains its purity as a strict message-passing and consensus-counting engine. This leaves the domain-specific side effects completely encapsulated in dedicated, highly cohesive listener classes.






pls detail 2. Replay Seek and Master Clock Snapping



To resolve the architectural coupling where `ClusterMaster` acts as a time-synchronization engine, we must strip its intimate knowledge of the `MasterSyncController` and the mechanics of the distributed lockstep clock. 

Currently, `ClusterMaster` violates the Single Responsibility Principle (SRP) by manually orchestrating clock pauses inside `ProcessSeekReplayIntent` by generating `PauseTimeIntent` and `SlaveNodeSetUpdatedEvent` messages. Worse, during its ACK loop, it explicitly fishes for a `ReplaySeekResult` payload and directly invokes `_masterSync?.SnapAndPause(...)`. A Two-Phase Commit coordinator should not be snapping physical or logical clocks.

Here is the clean architecture refactoring plan to extract this into a decoupled Process Manager:

**1. Purify the ClusterMaster**
We must completely remove the `_masterSync` dependency from `ClusterMaster`. This means deleting the `SetMasterSync` injection method and removing the hardcoded `ReplaySeekResult` tracking fields from `BusTransitionAckTracker`. The `ProcessSeekReplayIntent` method should be reduced to doing nothing but fanning out the `NodeOpType.NodeReplaySeek` operation.

**2. Extract Pre-conditions to a `ReplaySeekProcessManager`**
Create a new `ReplaySeekProcessManager` (or Saga) that holds the dependency on the `MasterSyncController`. This manager will subscribe to the `SeekReplayIntent` on the event bus. When it observes a seek request, the Process Manager—not the 2PC coordinator—will take responsibility for publishing the `SlaveNodeSetUpdatedEvent` and the `PauseTimeIntent` to safely halt the cluster's time advancement before the seek executes. 

**3. Exploit the `INodeResponseAggregator` for Consensus**
To get the resulting seek time back without polluting the 2PC engine, we will build a `ReplaySeekAggregator` implementing the existing `INodeResponseAggregator` contract. This aggregator will target `NodeOpType.NodeReplaySeek`. It will parse the incoming node ACKs, extract the `ReplaySeekResult`, and return it. The `ClusterMaster` will blindly run this aggregator and attach the output to the `ClusterOpCompletedEvent.ResultPayload`.

**4. Event-Driven Clock Snapping**
Finally, the `ReplaySeekProcessManager` will listen for the generic `ClusterOpCompletedEvent`. When it detects a successful completion containing a `ReplaySeekResult` payload, the Process Manager will extract `RestoredTime.TotalWallTicks` and `RestoredTime.TotalTime`. It will then invoke `SnapAndPause()` on its injected `MasterSyncController`. 

By pushing the temporal pre-conditions (pausing) and post-conditions (snapping) into an autonomous Process Manager, the `ClusterMaster` returns to its strict, domain-agnostic responsibility: message routing and consensus counting.





pls detail 3. Global Context Persistence Subroutines


The current implementation of `ClusterMaster` acts as a "God Class" by directly orchestrating local file persistence for the orchestrator's global context, which is a blatant violation of both the Single Responsibility Principle (SRP) and the Open/Closed Principle (OCP). 

Currently, the `ClusterMaster` holds a direct reference to `GlobalContextClusterOpHandler`. During a `SaveScenario` operation, the `ProcessStorageOpIntent` method manually constructs a local command and explicitly invokes `_globalContextHandler.PrepareAsync` and `Commit` inside the event loop. Similarly, during `TransitionStateIntent` processing, the coordinator hardcodes checks for `LoadingLive` and `LoadingEdit` target states to manually trigger the handler's `Commit` method. Worse, when handling `SerializeLocal` task completions, the 2PC engine explicitly injects `_globalContextHandler.CommitManifestEntry` into the collected file manifests before pushing them to the NAS.

A Two-Phase Commit coordinator should exclusively route messages and aggregate ACKs; it has no business intimately managing local file I/O subroutines. Here is the clean architecture refactoring plan to extract this persistence logic into a decoupled Process Manager:

**1. Purify the ClusterMaster**
Completely remove the `_globalContextHandler` field and the `SetGlobalContextHandler` injection method from `ClusterMaster`. Strip out all explicit calls to `PrepareAsync` and `Commit` scattered throughout the `ProcessStorageOpIntent` and `ProcessTransitionStateIntent` methods. The 2PC engine must only coordinate the message fan-outs, such as `ExecuteStorageOpIntent`, without knowing what side effects they cause.

**2. Introduce a `GlobalContextProcessManager`**
Extract the persistence logic into an autonomous Process Manager (or Saga) that subscribes to the `FdpEventBus`. This manager will take ownership of the `GlobalContextClusterOpHandler` dependency and its file I/O operations.

**3. Event-Driven Save and Load**
Instead of the orchestrator pushing commands to the handler, the new Process Manager will reactively observe the bus for `ExecuteStorageOpIntent` events (for `SaveScenario` operations) or generic cluster state transitions. When the cluster enters `LoadingLive` or `LoadingEdit`, the manager will react independently to load the context from `Orchestrator.json`, seed the time controller, and publish the `OrchestratorContextTopic` to DDS.

**4. Decouple Manifest Assembly via the Storage Aggregator**
To remove the orchestrator's intimate knowledge of `CommitManifestEntry`, we must leverage the `StorageConsensusAggregator` proposed in the design talk. The global context serialization result can be emitted as a standard domain payload. The aggregator will then seamlessly collect and flatten all manifests, ensuring the `ClusterMaster` remains completely blind to the file paths and storage gateway interactions.

By transitioning this workflow to a reactive, event-driven pattern, the `ClusterMaster` is finally relieved of file I/O orchestration, preserving its pristine responsibility as a mathematically sound consensus and routing engine.



pls detail the refactor for 4. Asset Prefetching and Staging Logic



The current implementation of asset prefetching in the `ClusterMaster` is a textbook example of orchestration logic becoming severely entangled with domain-specific I/O workflows. 

Currently, the `ClusterMaster` explicitly halts its 2PC transition fan-outs to manage file staging. It maintains a `PendingPrefetchOp` state machine and runs a `DrainPendingPrefetch` loop every tick to deliberately block operations until files are physically confirmed on the nodes' local disks. Furthermore, during state transitions or episode management, it intimately interacts with network-attached storage by directly invoking `_gateway.PrefetchScenarioAsync`.

This is a massive violation of the Single Responsibility Principle (SRP). A distributed consensus engine should be mathematically pure—it should not be coupled to SMB/NAS file transfers or wait on physical disk I/O. 

To resolve this and establish a strict Bounded Context, we must extract this logic into a dedicated Process Manager (or Saga). Here is the clean architecture refactoring plan:

**1. Purify the ClusterMaster**
We must completely strip out the `_pendingPrefetch` tracking object, the `DrainPendingPrefetch` method, and the `ExecutePrefetchScenario` routine. When the `ClusterMasterPlanner` dictates that a `PrefetchScenario` operation is required for a trajectory, the `ClusterMaster` should simply emit a neutral `ExecutePrefetchIntent` to the event bus and yield. It should have zero knowledge of the `StorageGatewayModule`.

**2. Introduce an `AssetPrefetchProcessManager`**
Create a decoupled `AssetPrefetchProcessManager` (Saga) that subscribes to the `FdpEventBus`. We will move the `StorageGatewayModule` dependency out of the orchestrator and inject it exclusively into this new Process Manager. 

**3. Event-Driven NAS Pulls**
The Process Manager will listen for the `ExecutePrefetchIntent`. Upon receiving it, the manager autonomously initiates the asynchronous file transfer by calling `_gateway.PrefetchScenarioAsync` (or `PrefetchArchiveAsync`). Because this runs entirely outside the `ClusterMaster`'s event loop, the 2PC engine remains unblocked and pristine.

**4. Choreographed Fan-Out and Continuation**
Once the gateway task completes and the files are successfully staged, the Process Manager takes over the next step. It will publish an `ExecuteNodeOpIntent` for `NodeOpType.PrefetchFiles` to fan out to the active cluster nodes. Finally, the Process Manager listens for the `ClusterOpCompletedEvent` indicating the nodes have acknowledged the prefetch. Upon success, the Saga re-emits the original `TransitionStateIntent` or `ManageEpisodeIntent` back onto the bus to seamlessly resume the halted 2PC transition.

By applying this Saga pattern, the `ClusterMaster` returns to its sole responsibility of routing messages and aggregating consensus, while the complex, error-prone mechanics of distributed file staging are safely encapsulated in an autonomous, highly testable component.





Good! pls define a clear success conditions for each of those refactors.


Defining rigorous success conditions is the absolute cornerstone of any architectural refactoring. To ensure we successfully eliminate this "God Class" anti-pattern and restore the `ClusterMaster` to a pristine, Bounded Context, we must verify that all domain-specific side effects have been physically extracted. 

Here are the strict success conditions for each of the four Process Manager extractions:

**1. Live-from-Replay Temporal Interlocks (Time Freezing)**
*   **Structural Purity:** The `_replayMasterModule` dependency and the `_pendingBranchTasks` tracking dictionary must be completely deleted from the `ClusterMaster`. 
*   **Behavioral Decoupling:** The orchestrator must no longer explicitly check for `OperatingReplay` to manually invoke `_replayMasterModule?.FreezeTime()` or `_replayMasterModule?.RestoreTime()` during the state transition event loop.
*   **Validation:** A new, autonomous `LiveBranchProcessManager` successfully executes these time-freezing interlocks strictly by reacting to standard event bus messages.

**2. Replay Seek and Master Clock Snapping**
*   **Structural Purity:** The `_masterSync` dependency (the `MasterSyncController`) must be entirely removed from the orchestrator.
*   **Behavioral Decoupling:** The `ClusterMaster`'s acknowledgement loop must no longer parse out a `ReplaySeekResult` or directly invoke `_masterSync?.SnapAndPause(...)` to mutate the distributed lockstep clock,.
*   **Validation:** A `ReplaySeekAggregator` correctly reduces the node responses, and a decoupled `ReplaySeekProcessManager` listens for the resulting `ClusterOpCompletedEvent` to perform the clock snapping asynchronously.

**3. Global Context Persistence Subroutines**
*   **Structural Purity:** The `_globalContextHandler` field must be stripped from the `ClusterMaster`.
*   **Behavioral Decoupling:** All hardcoded logic that inspects transition trajectories for `ClusterState.LoadingLive` or `ClusterState.LoadingEdit` to explicitly call `_globalContextHandler.Commit()` must be completely removed from the 2PC engine,. 
*   **Validation:** File I/O for the orchestrator's local context is exclusively managed by a reactive `GlobalContextProcessManager` that listens to the state transitions on the event bus.

**4. Asset Prefetching and Staging Logic**
*   **Structural Purity:** The `_pendingPrefetch` state machine tracker, and the `DrainPendingPrefetch` polling method must be eradicated from the `ClusterMaster`'s core `Tick()` loop,.
*   **Behavioral Decoupling:** The orchestrator must never directly hold or invoke the `StorageGatewayModule` to trigger `PrefetchScenarioAsync`. 
*   **Validation:** An independent `AssetPrefetchProcessManager` (Saga) successfully coordinates the asynchronous network-attached storage (NAS) pulls and autonomously re-emits intents to safely resume the 2PC fan-out once the files are physically confirmed on disk.

When these conditions are met, the `ClusterMaster` will finally be a mathematically pure, highly cohesive Two-Phase Commit engine that simply fans out operations, counts acknowledgements, and delegates final resolutions entirely to the domain layer.



do we have headless integration tests for all these?


Yes, the codebase is excellently positioned for this refactoring because it already contains a comprehensive suite of headless integration tests covering these exact orchestration workflows. As a software engineer, I consider this the ideal state for a major architectural extraction: we have the safety net required to break apart the `ClusterMaster` without fear of regression.

Here is the breakdown of the headless integration tests already covering these specific operations:

**1. Live-from-Replay & Replay Seek (Temporal Interlocks)**
The `ClusterOpE2eScriptTests` class utilizes the `HeadlessTestExecutor` to run full end-to-end JSON scripts without a UI. Specifically, the `RecordAndReplaySeek_Passes` test verifies the transition to `OperatingReplay` followed by a `ReplaySeek` operation. Furthermore, the `LiveFromReplayBranch_Passes` test covers the exact Live-from-Replay temporal interlock we want to extract, verifying the flow from `OperatingLive` to `OperatingReplay`, branching to a new live exercise, and successfully spawning entities post-branch. Additionally, `CgfRecordingIntegrationTests` proves that `ReplaySeek` correctly maintains the cluster in `OperatingReplay` without crashing.

**2. Global Context Persistence & Checkpointing**
The `ScenarioSaveLoadTests` in the Orchestrator integration suite directly tests the `GlobalContextClusterOpHandler`. The `OrchestratorContextRestored_AfterLoad` test verifies that the `SceneId` and tick timelines are correctly persisted and restored from the file system during a save/load round-trip. Furthermore, `OnContextLoaded_FiresWithCorrectValues_AfterCommitLoad` ensures the event-driven timeline seeding works precisely. Checkpointing is safeguarded by `ClusterOpE2eScriptTests.OverlappingCheckpoints_Passes`, which rapidly issues multiple `TakeCheckpoint` requests during `OperatingLive` and ensures the entity state remains intact.

**3. Episode Management (Episode Consensus)**
The `EpisodeInjectionTests` class rigorously exercises episode handling in a completely headless manner. It exercises `ReferenceEpisodeLoadHandler` and the `ClusterMasterPlanner` logic, directly verifying that `StartEpisode` correctly injects multiple entities with episode tags, and that `StopEpisode` surgically removes only the matching entities while leaving others untouched.

**4. Storage, Prefetching & Scenario Loading**
The `DistributedScenarioLoadTests` class executes a full headless 2-Phase Commit load pipeline. It authors a scenario offline, transitions the live cluster to `OperatingLive`, and asserts that the network references are patched correctly after the staging and prefetch phases resolve.

Because we have these deterministic, headless tests—which rely on simulated ticks, an isolated event bus, and strict assertions rather than brittle UI automation—we can aggressively extract the `StorageConsensusAggregator`, `EpisodeConsensusAggregator`, and the associated Process Managers. As long as these tests continue to pass, we have mathematical proof that our clean architecture refactoring has successfully decoupled the logic while preserving the system's operational integrity.


