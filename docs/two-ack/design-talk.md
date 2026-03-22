The two-ack pattern is a distributed networking strategy designed to solve the UX challenge of "half-baked" or silently disappearing entities. It provides immediate network responsiveness while safely locking user interactions until complex multi-node operations are fully confirmed. 

This pattern is modeled after the existing logic used for map authoring tools (which use `MapCommandAck` to signal intermediate vs. finished states), and it relies on a unified **`CreateUpdateDeleteEntityAck`** message that carries an explicit status code.

Here is how the pattern functions across the entity lifecycle:

**Phase 1: The Immediate "In-Progress" Acknowledgment**
When a client (like the IOS) issues a `CreateEntityRequest` or a `DeleteEntityRequest`, the authoritative node (SimHost) performs immediate validation. 
*   **Action:** If valid, the SimHost allocates the network ID (for creations) or initiates the teardown (for deletions) and immediately fires back a `CreateUpdateDeleteEntityAck`.
*   **Payload:** The acknowledgment carries the affected `EntityId` and an "In Progress" status code (e.g., `1`, denoting an intermediate result).
*   **Client Response:** The IOS receives this instant receipt. For a spawn, it knows the `EntityId` has been allocated and that the entity is currently in the **`Constructing`** lifecycle state. The UI can visually display the unit as "pending" or "loading" while **preventing the operator from assigning follow-up commands** (like routing or missions) to an entity that is not fully baked.

**Phase 2: The Final "Success or Failure" Acknowledgment**
Behind the scenes, the SimHost's Entity Lifecycle Module (ELM) continues the distributed handshake. For reliable initialization, it waits to receive an **`EntityLifecycleStatusDescriptor`** from all required peer nodes.
*   **Action (Success):** If all peers acknowledge the entity, the ELM promotes it to the **`Active`** state. It then sends the second `CreateUpdateDeleteEntityAck` with the status code set to `0` (Success). The IOS unlocks the entity, making it fully interactive for the operator.
*   **Action (Failure):** If the distributed handshake times out waiting for peers, the ELM aborts the creation and cleans up the entity. It retrieves the original `RequestId` and sends the final `CreateUpdateDeleteEntityAck` with an error status code. 
*   **Client Response:** Because the IOS receives an explicit failure ACK, it can remove the "pending" unit from the ORBAT tree and display a clear, explicit error toast to the user, completely eliminating the confusing UX of an entity silently vanishing.

**Summary of Benefits**
By splitting the acknowledgment into two phases, the architecture perfectly balances the engine's strict time-slicing rules with a safe, deterministic user interface. The **first ACK guarantees the UI never freezes** waiting for a slow 5-second network consensus, and the **second ACK guarantees the operator is never allowed to interact with an invalid or aborted simulation state**.



Here is the proposed design to implement the `DeleteEntityRequest` and the two-phase acknowledgment pattern, unifying the network message contracts.

### 1. New `DeleteEntityRequest` Struct
To allow non-owning nodes to request entity deletion safely, add this struct to **`Bagira.DDS.DataModel/GenericMessages.cs`**. It mirrors the existing request structures like `UpdateEntityDescriptorRequest`:

```csharp
/// <summary>
/// Request to delete an entity owned by another node.
/// </summary>
[DdsTopic("DeleteEntityRequest")]
[DdsIdlFile("bdc-sst-generic-msgs")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
public partial struct DeleteEntityRequest
{
    public Guid RequestId;

    // The ID of the entity to be deleted.
    public int EntityId;
}
```

### 2. Blending and Renaming `CreateUpdateDeleteEntityAck`
Remove the standalone `CreateEntityAck` and consolidate its fields into `CreateUpdateDeleteEntityAck`. We add the `EntityId` field to accommodate the newly allocated ID from creation requests (or to simply echo the affected ID for updates and deletions), and rename `ErrorCode` to `StatusCode`.

```csharp
/// <summary>
/// Unified acknowledgment for entity creation, descriptor update, deletion, and attribute update.
/// </summary>
[DdsTopic("CreateUpdateDeleteEntityAck")]
[DdsIdlFile("bdc-sst-generic-msgs")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
public partial struct CreateUpdateDeleteEntityAck
{
    public Guid RequestId;

    /// <summary>
    /// The affected Entity ID. For creation requests, this holds the newly allocated ID.
    /// </summary>
    public int EntityId;

    /// <summary>
    /// 0 = Success (Final), 1 = In Progress (Intermediate), >1 = Error.
    /// Maps to SstStatusCode.
    /// </summary>
    public int StatusCode;

    /// <summary>Identifies which node is sending this acknowledgment.</summary>
    public NodeId RespondingNode;

    /// <summary>
    /// Optional 32-byte engine-specific execution receipt.
    /// </summary>
    public byte[] OpaqueData;
}
```

### 3. Renaming `SstErrorCode` to `SstStatusCode`
To avoid semantic confusion (since `1` will now mean a healthy "In Progress" state rather than an error), you should rename the `SstErrorCode` enum to `SstStatusCode` and shift the existing error values to accommodate the new intermediate state. This mirrors the existing logic used by `MapCommandAck`.

```csharp
/// <summary>
/// Strongly-typed, centralised status codes for all SST request/response protocols.
/// </summary>
public enum SstStatusCode : int
{
    /// <summary>Operation completed successfully (Final Result).</summary>
    Success = 0,

    /// <summary>Operation in progress (Intermediate Result). Entity ID allocated, waiting for ELM consensus.</summary>
    InProgress = 1,

    // --- Errors ---
    
    /// <summary>The requested descriptor type is not handled by this node.</summary>
    UnknownDescriptorType = 2,

    /// <summary>No live EntityMaster found for the requested entity ID.</summary>
    EntityNotFound = 3,

    /// <summary>The requested descriptor instance ID does not exist.</summary>
    DescriptorInstanceNotFound = 4,

    /// <summary>This node does not own the targeted descriptor.</summary>
    NotOwner = 5,

    /// <summary>The provided value fails application-level validation.</summary>
    ValidationFailed = 6,

    /// <summary>Descriptor updates are not permitted for this descriptor type.</summary>
    NotSupported = 7,

    /// <summary>The provided currentVersion does not match the live version (optimistic locking).</summary>
    VersionConflict = 8,
}
```

### How This Simplifies the Pipeline:
*   **Creation Flow:** 
    1. SimHost receives `CreateEntityRequest`.
    2. SimHost allocates the ID and immediately sends `CreateUpdateDeleteEntityAck` with `StatusCode = 1 (InProgress)` and the `EntityId`.
    3. IOS unblocks and tracks the pending creation, but knows it is not fully baked yet. 
    4. SimHost's ELM finishes the peer handshake and sends a second `CreateUpdateDeleteEntityAck` with `StatusCode = 0 (Success)`.
*   **Deletion Flow:** 
    1. IOS sends `DeleteEntityRequest`.
    2. SimHost initiates ELM teardown and can optionally send `InProgress`, followed by `Success` once the destruction handshakes complete.




A two-phase acknowledgment pattern (an immediate "in-progress" receipt followed by a terminal "success/failure" confirmation) would significantly simplify the IOS logic. It removes the need for the IOS to infer creation failures from sudden DDS `Dispose` events and safely locks the entity from operator interactions until the final ACK arrives.

Interestingly, your codebase already successfully uses this exact multi-phase ACK pattern for map tools. The `MapCommandAck` message uses a `StatusCode` field where `1` means "intermediate result" and `0` means "request finished". 

To apply this superior pattern to entity creation, you will need to adjust the SimHost pipeline. Currently, the system loses track of the client's `RequestId` after sending the first immediate ACK, meaning the Entity Lifecycle Module (ELM) has no way to reply when the distributed handshake actually finishes.

Here is exactly what you need to change in the codebase to implement your two-ACK design:

**1. Add a Status Code to `CreateEntityAck`**
Update the `CreateEntityAck` struct to include a status indicator (e.g., `StatusCode`), differentiating the "in-progress" ID allocation from the "final" result. This perfectly mirrors the existing `MapCommandAck` design.

**2. Propagate the `RequestId` into the ELM**
Currently, `CreateEntityRequestSystem` sends the first ACK, then creates a `SpawnEntityCommand` which *does* include the `RequestId`. However, when the `NetworkSpawningSystem` consumes this command and hands the entity over to the ELM via `_elm.BeginConstruction`, the `RequestId` is dropped. 
You must update `EntityLifecycleModule.BeginConstruction` and its internal `PendingConstruction` tracking class to accept and store this `RequestId`.

**3. Send the Final ACK from the ELM**
Inject an `ICreateEntityAckSink` (or a direct DDS writer) into the `EntityLifecycleModule`. 
*   **Success:** When ELM receives all peer ACKs and promotes the entity to `Active`, retrieve the stored `RequestId` and send the final `CreateEntityAck` with a success status.
*   **Failure:** When ELM destroys the entity due to a peer timeout, retrieve the `RequestId` and send the final `CreateEntityAck` with an error status.

By making these structural changes, the IOS can cleanly track the creation intent from start to finish using explicit network responses, completely eliminating the UX risks of "half-baked" entities.





To adapt the IOS to the two-ACK pattern using the unified `CreateUpdateDeleteEntityAck` and `SstStatusCode`, you need to update the network ingress pipeline, modify how transactions are completed, and safely lock the UI for "in-progress" entities.

Here are the required changes to the IOS codebase:

**1. Update the Network Ingress Pipeline**
First, replace the old `CreateEntityAck` references with the new unified struct.
*   In **`IosSubsystem.cs`**, change the `CreateEntityAckIngressHandler` and its associated `ConcurrentEventQueue` to use `CreateUpdateDeleteEntityAck`. 
*   Pass this unified queue into the `IosLogic` constructor instead of the old `createEntityAckQueue`.

**2. Modify Acknowledgment Processing in `IosLogic`**
Currently, `IosLogic.ProcessEntityCreationAcks()` checks `ack.ErrorCode == 0` and immediately completes the transaction. You need to rewrite this to handle the three states of the new `SstStatusCode` enum:

*   **Initialize Tracking:** Add a `HashSet<int> _pendingEntities` to `IosLogic` to track entities that are currently in the middle of a distributed handshake.
*   **In Progress (`StatusCode == 1`):** 
    *   Extract the `ack.EntityId`.
    *   Add it to `_pendingEntities`.
    *   Auto-select it via `SelectEntity(ack.EntityId)` so it appears in the UI.
    *   **Do not** call `TransactionManager.CompleteRequest()`. Keep the transaction open.
*   **Success (`StatusCode == 0`):**
    *   Remove the entity from `_pendingEntities`.
    *   Call `TransactionManager.CompleteRequest(ack.RequestId, true)` to close the transaction.
*   **Failure (`StatusCode > 1`):**
    *   Remove the entity from `_pendingEntities`.
    *   Call `TransactionManager.CompleteRequest(ack.RequestId, false, $"Error {ack.StatusCode}")`.
    *   Trigger a global UI alert (see step 4).

**3. Lock the UI for "Half-Baked" Entities**
Because the IOS auto-selects the entity upon receiving the "In Progress" ACK, you must prevent the operator from issuing commands to it until the final "Success" ACK arrives.
*   Expose the `_pendingEntities` state via `IIosLogic` (e.g., `bool IsEntityPending(int entityId)`).
*   In **`MissionPanel.Draw()`**, check if the `SelectedEntityId` is pending. If it is, wrap the task assignment and commit buttons in `ImGui.BeginDisabled()` and `ImGui.EndDisabled()`. You can display a yellow text label like `"[Constructing across network...]"` to explain why the panel is locked.
*   Similarly, in **`ContextMenuLogic.cs`**, if the selected entity is pending, return an empty menu or disable all `ContextMenuItem` entries so the user cannot right-click and delete or move the entity while it is initializing.

**4. Surface Explicit Errors to the Operator**
When the final ACK contains an error (e.g., ELM timeout or validation failure), the entity will be destroyed by the SimHost. The IOS needs to explain this to the user rather than letting the entity silently vanish.
*   The `MissionPanel` already implements a perfect UX pattern for this: the `HasConflictAlert` flag and `ConflictMessage` string, which trigger an immediate-mode ImGui warning modal. 
*   Move this alert pattern up to **`IosLogic`** or **`IosMock.DrawUI()`** so it can be used globally. When a failure ACK is processed in Step 2, set the global alert message (e.g., *"Failed to spawn entity: Network Timeout"*), forcing the user to acknowledge the failure.



You are absolutely correct. Your intuition is spot-on: **the FDP engine itself does not need any changes.** 

The FDP `EntityLifecycleModule` (ELM) and `NetworkSpawningSystem` are pure, generic ECS systems. They manage the internal distributed handshakes (`Constructing` → `Active`, or `TearDown` → Destroyed) but they know nothing about the application-layer SST REST-like network contracts. 

You can implement the entire two-ACK pattern purely within the `Bagira.SimHost` application layer by creating a system that simply "watches" the FDP lifecycle states.

Here is how you adapt the SimHost without touching FDP:

### 1. Introduce an Application-Layer Tracking System
Create a new system in SimHost (e.g., `SstRequestFinalizationSystem`) that runs in the `PostSimulation` phase. This system will maintain an internal dictionary of pending requests (mapping `NetworkId` to `RequestId`) and watch the ECS world to see when FDP finishes its work.

### 2. Update the Creation Flow (`CreateEntityRequestSystem`)
Modify the existing `CreateEntityRequestSystem`:
*   **Phase 1 ACK:** When it validates the request and allocates the network ID, it sends the unified `CreateUpdateDeleteEntityAck` with **`StatusCode = 1` (In Progress)**.
*   **Track:** It registers the `NetworkId` and `RequestId` into the `SstRequestFinalizationSystem`'s tracking dictionary.
*   **Hand off to FDP:** It publishes the `SpawnEntityCommand` to the event bus exactly as it does today.

### 3. Implement the Deletion Flow (`DeleteEntityRequestSystem`)
Create a new system matching the creation flow to handle the new `DeleteEntityRequest`:
*   **Phase 1 ACK:** Validates the request, sends the `CreateUpdateDeleteEntityAck` with **`StatusCode = 1` (In Progress)**.
*   **Track:** Registers the deletion intent into the tracking dictionary.
*   **Hand off to FDP:** Publishes a `DestroyEntityCommand` to the ECS event bus. FDP's ELM will automatically catch this, place the entity into `EntityLifecycle.TearDown`, and execute the destruction handshake across the network.

### 4. The Finalization System (The Watcher)
The `SstRequestFinalizationSystem` checks its tracked requests every frame. Because it is just an ECS system, it can easily query the FDP world state:

**For tracked Creations:**
*   Check if the entity is still alive (`world.IsAlive(entity)`). If it returns **false**, it means FDP's ELM timed out waiting for peers and automatically aborted/destroyed the entity. Send the final ACK with **`StatusCode = 2` (Failure)** and remove it from tracking.
*   If it is alive, check its lifecycle state (`world.GetHeader(entity.Index).LifecycleState`). If the state has transitioned to **`EntityLifecycle.Active`**, FDP has successfully completed the peer handshake. Send the final ACK with **`StatusCode = 0` (Success)** and remove it from tracking.

**For tracked Deletions:**
*   Simply check `world.IsAlive(entity)`. 
*   While FDP is performing the teardown handshake, the entity remains alive in the `TearDown` state.
*   The exact frame `IsAlive` evaluates to **false**, you know FDP has fully completed the distributed deletion. Send the final ACK with **`StatusCode = 0` (Success)** and remove it from tracking.

### Summary
By keeping the request/ACK state machine entirely within `Bagira.SimHost` and just observing the native FDP `EntityLifecycle` transitions, you maintain perfect separation of concerns. FDP remains a clean, generic physics and replication engine, while the BDC SST API contracts are handled strictly at the application boundary.

