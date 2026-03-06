# System Management, rec/plb/edit

I would like to design the distributed recording and replay system including IG and SimHost as well as the IOS. And not only that, i want to design also the overall system mode controller that maintains the **`exercise state machine`** of the whole system with states like  
 \- **`Standby`**: doing nothing, waiting for further commands  
 \- **`LoadingLive`**: initializing into live simulation \- like for example loading exercise from some kind of scenario, or from a saved snapshot, or from the current state of the replay  
 \- **`RunningLive`**: performing live simulation  
    \- here the simulation time could could be paused or running  
    \- or we could take a snapshot of the current simulation state over the whole system (which needs to happen asynchronously to NOT block the flow of the live simulation)  
 \- **`LoadingReplay`**: initializing the replay of the exercise from  previously saved recording  
 \- **`RunningReplay`**: performing the replay of exercise (movie like reconstruction of everything)  
    \- here the simulation time could could be paused or running  
 \- **`UnloadingLive`**: deinitializing live into standby (finalizing the recordings files etc.)  
 \- **`UnloadingReplay`**: deinitializing replay into standby

   
All the states and their transitions are affecting all nodes and must be synchronized across the nodes.

 

IG and SimHost, being based on FDP, can utilize the FlightRecorder, each recording their owned ECS components. Something like that was already attempted in the NetworkDemo project (not sure if fully operational there).

IOS does not maintain much of simulation state (SimHost and IG do) but it is the control node which gives commands for the rest of the system to initiate the recording or replay, to set the speed of the replay, to jump to different time in the history and start replay from there, to control whether the time flows continuously or the whole system should go to a deterministic time stepping mode and to make discrete time steps etc. It also needs  to remember some important points in time to be able to render a time line and to know what the whole live simulation duration was and be able to start replay from any given time point.

One of the nodes (not the IOS, currently the SimHost but it could be any node) is the master of the simulation time and the master (brain) of the `exercise state machine`.

I need to design how to incorporate the control of the time and system states (implementing the exercise state machine states) across individual nodes \- some based on FDP (using FlightRecordier as their recording device) as well of non-FDP nodes like IOS (storing the data in some custom way but still supporting proper exercise state machine switching).

For example the IOS needs to store all simulation time pause/restore events (each recorded with the wall-clock time stamp) to be able to incorporate these to the time line.

The events in the exercise are generally time-stamped with the wall-clock time and the whole replay control uses this wall-clock based "stamping clock". The simulation time can be paused or resumed but the system should be basically recording everything, including the pauses, because even if simulation is paused the real world continues and some systems like video surveillance still record the happening during pauses. Or the situation on the simulated battlefield can be changed during the pause \- new tactical drawings, relocation of existing entities, removal or adding of entities etc.

The system should also support a `dry run` that serves for quick previewing of the scenario being edited. This is basically a very quick switch from a scenario editing mode (which is something like a paused live mode allowing to save the current situation as a scenario file or as a snapshot)  where immediately before the dry run begins we take a snapshot of the current live state and start a live simulation from that point. This is not a fully fledged live simulation as there are no live players involved \- they could be simulated as NPC or ignored completely, but the simulation modules need to know that it is not a full live stuff, but a dry run.  Once finished, we forget the current status and re-load the snapshot taken initially, returning back to the scenario editing mode.

Note that different FDP nodes might not be perfectly synchronized \- some might run faster than others \- the slower ones might be missing some frames of the faster ones so that the system is usually not perfectly frame synchronized unless it is running in the deterministic frame stepping mode (which is more a debug and automated testing feature than a usual use case).

Every node should be largely independent in the implementation of how the stuff is done

- can use its own recording/replay infrastructure for continuous replay or taking/loading snapshot  
- can load its own part of the exercise scenario (different nodes might use completely different formats of their scenarios)

There can be multiple simulation nodes participating in the same exercise, each creating and publishing its own entities, or distributing a single entity ownership across multiple nodes (like a networkdemo example does).

I need your suggestions on how to extend the infrastructure to support every node implementing the exercise state machine states. And how to give proper acknowledgements and status reporting so the exercise state machine brain knows what is going on and to be able to handle various edge cases like some node failing to do something (reporting a failure), or some node timing out (failing to report its status in time). Each node should probably report its status periodically (what exercise state machine state or transition it is currently executing etc.)

And how the master should be handling it all. Note that the master is a background component that orchestrates the actions of the nodes. And it needs to be controlled and monitored remotely (like for IOS or some other UI) \- for example UI sends a request for loading a live simulation from a given scenario and wants to be informed about what is the current status of executing such a request. Or it gives the orchestrator node a command to pause the sim time (using the future barrier concept) etc.

how the FlightRecorder based nodes can handle the async mode during replay \- they probably store their own frames each but the replay of these frames need to be time synced with the master clock during replay so that at any given time in th replay all nodes show more or less the same situation as was during the live.

Note that the transitions like loading or unloading takes time and need to be synchronized across all nodes.

The system should also support a scenario editing mode in a distributed manner, multiple nodes cooperating similarly like during a live exercise.

During that phase the scenario can be saved (different nodes save their part of the scenario in their own custom way). This save takes some time \- it is not a simple raw snapshot as used during recording and replay \- the raw snapshot is optimized for speed and usually it can not be loaded with different versions of the software as it is using optimized binary storage formats. The scenario is usually saved in backwards/forwards compatible manner, like to a json file or other format supporting the data schema versioning and migrations between versions. Scenario also does not contain all the details of the current simulation state, but usually more schematic instructions on how to reconstruct the state from much more limited information, using lots of assumptions of the default initial values, saving just the overrides etc.

So the saving of the scenario needs to be synchronized as well across all nodes including the acks from all participating peer nodes.

This lead to the idea of creating some more generic “system-wide-operation” orchestration concept, something like the following:

* All-node operation is performed by all system nodes in parallel.  
* The operation is considered finished only when all nodes acknowledge it.  
* Example  
  * node BBroker receives a SysOpRequest.  
  * BBroker responds with SysOpStatus (InProgress or Failure).  
  * BBroker sends a NodeOpRequest to all nodes.  
  * nodes start the operation. They send back NodeOpStatus to BBroker, reporting their progress (InProgress, Success, Failure).  
  * When all nodes finish the operation, BBroker sends a SysOpStatus message, reporting the operation status Success or Failure.

Some nodes might opt NOT to be participating. Nodes report that via their NodeOpStatus message using a flag which is set if the component is actively participating in the operation (not just reporting dummy status).

Each request must have a unique GUID RequestId field for correlation with responses.  
The SysOpStatus as well as NodeOpStatus should carry a string containing the results (json object string which might be null if no results attached.). And also an error code integer.

The system also needs to support the concept of `battlespaces`.  
Battlespace is a high resolution area in the simulated world where the important part of the simulation (the training exercise) is taking place. The rest of the world is simulated with lower resolution. There can be multiple battlespace areas in the same exercise, at different world locations. The battlespace area is defined by a set of 2d vertices (GeoPosition).  
The battlespaces can be defined and modified by the user interactively before loading a scenario into the editor. So basically the battlespace definition needs to come as part of new scenario editing request. Later, when scenario editing is in progress, the battlespaces can be changed and saved to the scenario. Loading of the battlespaces includes loading big data sets (high resolution terrain etc.) so it takes time. If it happens in the middle of scenario editing, the system should be handling that asynchronously but in synchronized manner, swapping to the newly loaded hi res data once all nodes has finished loading/preparing them.

The system should also support exporting and importing of the scenario recording to/from an archive. As the system is distributed, each node might be storing its recording just locally in a temporary “hot” storage. At the end of the exercise we might want to collect all these and export them to a permanent long time “cold” storage with high capacity but usually slow. Same in the opposite direction if some older recording should be replayed we might need to import it from the cold storage back to the hot storage and then replay it. Note that during the replay the node layout might be very different so the recordings might need to be imported to different nodes (needs some mapping of what recording from original live node to go to what new target replay node). The operations are long and perfect candidates for applying the system-wide-operation concepts.

The system should also support so called `stories`. Stories are something like micro-scenarios that can be loaded and unloaded dynamically while the “big” system is already up and running live. The goal is to allow very quick starting of new little training sub-exercises in non-overlapping areas of the big world to allow independent training of different trainees without needing to re-initialize the whole heavy system as it takes too much time.  
Multiple stories can be loaded at the same time. The story contains instructions on what entities to create and what they should be doing and what are the trainee performance evaluation criteria etc. Story can be unloaded at any given time, optionally cleaning up the environment (like removing the entities it created etc.) Also all the interaction with the environment like every entity creation, every fire or detonation must be marked with a story-instance specific id (GUID) allowing to ignore these events is the performance evaluation of other parallelly running stories. The stories are only existing in the “live” space, they do not use the “big” system capability of recording everything at once. But they need their own story-instance-specific recording and replay \- maybe by using independent instances of the FligthRecorder configured to record just the story-related ECS components and events. These story recordings are not meant to be archived, they are used right after the live-story exercise was finished, then it might be replayed and then it is never needed again, deleted \- so their recording might be kept just temporarily in the memory (but better to be saved to fast local disk so save RAM).

With all that in mind, let’s design a mechanism that can manage/orchestrate/handle all these requirements. There will need to be a lot of management dds messages and corresponding internal FDP events and translations between them. There will need some new ModuleHost modules implementing the “slave” part of the exercise state machine etc etc. Pls suggest.

---

This is a highly ambitious and architectural-level evolution of the Bagira/FDP platform. What you are describing transitions the system from a simple "multiplayer game" architecture into an **Enterprise-Grade Distributed Simulation Platform** (similar in scope to systems like MAK VR-Forces or VBS4).

To achieve this elegantly without turning the codebase into spaghetti, we must introduce a **System Operations (SysOp) Coordinator Pattern**, an **Exercise State Machine (ESM)**, and a **Story/Layering System in the ECS**.

Here is the comprehensive architectural design to implement these requirements.

---

### 1\. The Distributed Orchestration Framework (SysOp)

This is the foundational layer. Every transition (Loading Live, Saving Scenario, Loading Battlespace, Archiving) takes time and requires distributed consensus. We will implement a pattern akin to a **Two-Phase Commit (2PC)**.

#### A. DDS Message Schema (`bdc-sst-orchestration`)

public enum SysOpType

{

    TransitionState,   // e.g., LoadLive, Unload

    SaveScenario,      // Saves JSON scenario

    LoadBattlespace,   // Loads high-res terrain

    TakeSnapshot,      // Fast binary memory dump (for Dry Runs)

    RestoreSnapshot,   // Rollback from Dry Run

    ExportArchive,

    ImportArchive,

    ManageStory        // Start/Stop/Eval micro-scenario

}

public enum OpStatus { Pending, InProgress, Success, Failure }

\[DdsTopic("SysOpRequest")\]

public struct SysOpRequest {

    public Guid RequestId;

    public SysOpType OperationType;

    public string PayloadJson; // Specific instructions (e.g., File paths, UUIDs)

}

\[DdsTopic("SysOpStatus")\]

public struct SysOpStatus {

    public Guid RequestId;

    public OpStatus Status;

    public int ErrorCode;

    public string ResultJson;

}\[DdsTopic("NodeOpStatus")\]

public struct NodeOpStatus {

    public Guid RequestId;

    public int NodeId;

    public OpStatus Status;

    public bool IsParticipating; // True if node actually has work to do

    public int ErrorCode;

    public string ResultJson;

}

#### B. ModuleHost Architecture (`OrchestratorModule`)

* **`SystemMasterModule` (Runs ONLY on the BBroker/Master node):** Listens to `SysOpRequest` from the IOS. When received, it broadcasts it. It maintains a dictionary of `RequestId -> HashSet<int> PendingNodes`. When all participating nodes reply with `NodeOpStatus = Success`, the Master broadcasts `SysOpStatus = Success`.  
* **`SystemSlaveModule` (Runs on ALL nodes, including Master):** Listens to `SysOpRequest`. Upon receipt, checks if it cares about this operation. If not, immediately replies `NodeOpStatus(Success, IsParticipating=False)`. If it *does* care, it replies `InProgress`, starts an asynchronous `Task`, and upon completion, replies `Success` or `Failure`.

---

### 2\. The Exercise State Machine (ESM)

The ESM is managed by the `SystemMasterModule`.

* **States:** `Standby`, `Editing`, `LoadingLive`, `RunningLive`, `LoadingReplay`, `RunningReplay`, `Unloading`.  
* **Topic:** `[DdsTopic("SystemState")] public struct SystemState { public ESMState CurrentState; }` (Uses `TransientLocal` QoS so late-joining nodes know the state instantly).  
* **Transitions:** The IOS requests a state change via a `SysOpRequest(TransitionState)`. The Master asks all nodes to prepare. Only when all nodes report `Success` does the Master change the `SystemState` topic.

---

### 3\. Recording, Replay, and Time Synchronization

Because nodes run at different speeds, **Wall-Clock Time** must be the absolute source of truth.

#### A. The "Always Recording" Principle

Currently, FDP's `FlightRecorder` writes chunks based on `GlobalVersion` (Tick). To support paused-but-recording states:

1. `GlobalTime` must be split. `SimulationTime` stops during a pause, but `WallClockTime` (UTC ticks) continues.  
2. The `FlightRecorder` must write the `WallClockTime` into the header of every Delta Frame.  
3. If the simulation is paused, the ECS won't have physics changes, but it *will* have Event changes (e.g., UI clicks, tactical graphics drawn). The recorder will capture these event streams natively.

#### B. Asynchronous Distributed Replay

During `RunningReplay`, the Master node's Time Controller acts as a **Replay Playhead**.

* Master publishes `TimePulse` containing the *Target Wall-Clock Time* of the replay.  
* The `ReplayBridgeSystem` on every slave node reads this target time.  
* Instead of just reading the "next" frame from the local `.fdp` file, the `RecordingReader` implements a `SeekToTime(long targetWallClockTicks)` method.  
* If a node ran slower during recording, it simply applies the frame that is closest to the requested timestamp. This guarantees visual synchronization across all IG screens regardless of original recording frame-drops.

#### C. Dry Runs (Fast Snapshots)

Instead of relying on JSON scenario saving (which is slow), we use the `NativeChunkTable`.

1. IOS sends `SysOpRequest(TakeSnapshot, "DryRun_Backup")`.  
2. Nodes pause simulation. The `FlightRecorder` is asked to do an in-memory `CaptureKeyframe()` into a named memory buffer, bypassing the disk.  
3. Nodes reply `Success`. System enters `DryRun` state.  
4. When Dry Run ends, IOS sends `SysOpRequest(RestoreSnapshot, "DryRun_Backup")`. Nodes blast the memory buffer back into the `EntityRepository`.

---

### 4\. Battlespaces (Asynchronous Synchronized Loading)

Battlespaces are defined in the Scenario JSON.

1. IOS edits a Battlespace boundary and triggers a load.  
2. Master sends `SysOpRequest(LoadBattlespace, "{ 'id':'bs_alpha', 'bounds':[...] }")`.  
3. IG and SimHost nodes reply `InProgress`.  
4. **Background Thread:** Nodes load the high-res terrain/navmeshes into secondary, inactive memory buffers.  
5. Once loaded, nodes reply `NodeOpStatus = Success`.  
6. **Swap:** Master sees all nodes are ready. Master sends an ECS event `CmdSwapBattlespace`. On the next `SystemPhase.BeforeSync`, all nodes synchronously swap pointers from the low-res data to the high-res data without stuttering.

---

### 5\. Stories (Micro-Scenarios)

Stories exist concurrently in the live simulation but require strict logical and recorded isolation.

#### A. ECS Tagging

Introduce a new Tier-1 component:

\[ComponentId(GlobalComponentIds.StoryTag)\]

public struct StoryTag {

    public Guid StoryId;

}

* When a Story is initiated, every entity spawned for that story gets this component.  
* If a story entity spawns a bullet, the bullet *inherits* the `StoryTag`.

#### B. Event Isolation

Events like `FireInteractionEvent` and `HitEvent` must be updated to include `Guid StoryId`. Systems generating events check the `StoryTag` of the entity and attach it to the event. Evaluator modules can safely ignore events where `StoryId` doesn't match the story they are evaluating.

#### C. Story-Specific Recording

Modify `RecorderSystem.cs`. Instead of one global `AsyncRecorder`, the `SystemOrchestrator` can spin up multiple `AsyncRecorder` instances.

* Add a `Predicate<EntityQuery>` to the recorder.  
* The Main Recorder records `Query().Build()` (Everything).  
* A Story Recorder records `Query().With<StoryTag>().Build()` (filtered by specific GUID).  
* Story recordings are saved to `temp/story_guid.fdp`. When the story is unloaded, the file can be quickly replayed for the trainee, and then deleted.

---

### 6\. Export/Import (Cold Storage Archives)

Because FDP is decentralized, there is no single "database." To export an exercise, every node must yield its `.fdp` file.

1. IOS sends `SysOpRequest(ExportArchive, "{ 'Destination': '\\\\shared_drive\\archives\\Exercise_X' }")`.  
2. Each node finalizes its `AsyncRecorder`, flushes to disk, and copies its local `node_100.fdp` to `\\shared_drive\archives\Exercise_X\node_100.fdp`.  
3. To **Import**, the IOS provides a mapping payload: `{"Node_100": "\\\\shared_drive\\...\\node_100.fdp", "Node_200": "..."}`.  
4. Nodes download their assigned files to local "hot" memory before replying `Success` to enter `LoadingReplay`.

---

### Summary of Required Code Changes (Implementation Plan)

To execute this, break it into manageable PRs/Batches:

1. **Batch 1: SysOp Framework & Master/Slave Modules**  
   * Create the DDS schemas.  
   * Implement `SystemMasterModule` and `SystemSlaveModule`.  
   * Create a mock UI in IOS to trigger a dummy "TestOp" and watch the nodes reply.  
2. **Batch 2: Exercise State Machine**  
   * Implement `SystemStateTopic`.  
   * Hook the Master to transition states via the SysOp framework.  
   * Update `SimulationControlsPanel` in SimHost and IOS to reflect/drive these states.  
3. **Batch 3: In-Memory Snapshots (Dry Run)**  
   * Extend `EntityRepository` and `NativeChunkTable` to support deep-cloning an entire repository into a backup repository in RAM.  
   * Wire this to the `TakeSnapshot`/`RestoreSnapshot` SysOp.  
4. **Batch 4: Time-Synced Replay**  
   * Update `GlobalTime` and `TimePulse` to include `WallClockTicks`.  
   * Update `RecordingReader` to seek based on binary searching chunk timestamps rather than purely sequential frame counts.  
5. **Batch 5: Stories & Filtering**  
   * Introduce `StoryTag`.  
   * Refactor `FlightRecorder` to accept an `EntityQuery` so it can record subsets of the world.

By establishing the **SysOp Request/Response loop** first, every complex distributed feature (Battlespaces, Archives, Scenario Saving) becomes trivial to implement as just another payload type through a stabilized synchronization pipeline.

---

Ad system state "Editing" \- how do we get there? when in standby, the system has to load heavy data similar to the loadinglive before the edit can start \- maybe something like `LoadingEdit`?

How is the DryRun represented in the ESM? Shouldn’t there be a new state for it? What about transiting to this state from the Editing \- something like `LoadingDryRun`? 

Ad “Master sends an ECS event `CmdSwapBattlespace” -` the nodes communicate over DDS only so this event must be dds-enabled.

Ad archiving. Every run of an exercise is identified by a GUID `DrillId` . This differentiates different exercise runs from each other. DrillId should be used as a folder in the archive for storing the recordings from participating nodes. The DrillId GUID is generated when a new exercise is about to be started.

It should be possible to take the snapshot (called `checkpoint`) also during normal live simulation in a manner that does not block the live simulation for longer than few tens of msecs, somehow into RAM with optional later async saving to the disk if desired. These snapshot could be later used to re-initialize the live simulation from that checkpoint snapshot, or to use it as the initial state for editing a scenario.

The checkpoint is important for analyzing bugs reported by users \- they simply take a snapshot at the time of seeing the bug. The system needs to support collecting the checkpoint snapshot from all nodes similarly to archiving the full exercise/drill recording. Just in this case the snapshot will be marked with guid, and needs to come with some metadata describing the context (time, scenario/exercise run, the configuration of the system etc etc.)

Ad SysOpRequest \- I consider the SysOpRequest to be a request to the master (like from the IOS for example) Shouldn’t there be also NodeOpRequest? Being a request from the SysOp master (brain) to nodes to perform an operation requested via SysOpRequest. Seems like what you described by “**`SystemMasterModule`** listens to `SysOpRequest` from the IOS. When received, it broadcasts it.” So the nodes do not need to know about SysOpRequest and work just with NodeOps? Imagine the master might reject the SysOp request for some good reason before even trying to reach out to the nodes.

The master also needs to monitor the health state of the nodes \- if they are online (node sw still running and actively responding). Each node needs to report the status of its (optional) subsystems in addition to the overall status of the node.

Recycling the SysOpRequest might seem too heavy for this purpose. Maybe the nodes can publish some kind of BIT (built-in-test) messages autonomously?

The system should also support something like a batch runs, with deterministic time stepping, i.e. slower than real time (because of the deterministic frame stepping overhead) but perfectly reproducible, for automated testing and verification.

---

These are excellent, highly precise refinements. You have correctly identified the gaps in the previous high-level proposal—particularly regarding the separation of Client-to-Master vs. Master-to-Slave communication, the necessity of loading states, and the mechanics of capturing live RAM snapshots without stalling the simulation.

Let's go through these architectural concepts one by one and formalize how they fit into the Bagira/FDP platform.

---

### 1\. The Split Orchestration API: SysOp vs. NodeOp

Your observation is 100% correct. If the IOS sends a broadcast command to everyone, the Master cannot act as a true orchestrator, and it cannot reject invalid requests. We must split the API into a **Control Plane (IOS $\\leftrightarrow$ Master)** and a **Command Plane (Master $\\leftrightarrow$ Nodes)**.

**Architectural Concept:**

* **`SysOpRequest` (IOS $\\to$ Master):** The IOS asks the Master to do something (e.g., "Start Live Simulation from Scenario A").  
* **`SysOpResponse` (Master $\\to$ IOS):** The Master immediately validates the request. If the system is currently `RunningReplay`, the Master rejects it immediately (`Status = Rejected, Reason = "Invalid State"`). No other nodes are bothered.  
* **`NodeOpCommand` (Master $\\to$ All Nodes):** If the Master accepts the request, it translates it into concrete commands for the nodes and broadcasts this. It includes a `TransactionId`.  
* **`NodeOpStatus` (All Nodes $\\to$ Master):** Nodes report their progress on the `TransactionId` (`InProgress(45%)`, `Success`, `Failed`).  
* **`SysOpUpdate` (Master $\\to$ IOS):** The Master aggregates the `NodeOpStatus` messages and updates the IOS on the overall system progress.

This ensures the Master is the absolute source of truth and the sole director of the cluster.

---

### 2\. The Expanded Exercise State Machine (ESM) & DrillId

To support editing, dry runs, and the time required to load heavy assets, the ESM must be expanded.

**Architectural Concept:**

* **`DrillId` (GUID):** A unique identifier generated by the Master whenever transitioning out of `Standby` into a new session (Live, Replay, or Edit). This `DrillId` is included in the `SystemState` DDS topic. All recordings, logs, and checkpoints generated by any node use this `DrillId` as their root folder/identifier.  
* **New ESM States:**  
  * `Standby`: Idle.  
  * `LoadingEdit`: Master instructs nodes to load static assets for a scenario. Nodes load terrain/models.  
  * `Editing`: System is loaded but not "running." Changes to the scenario (battlespaces, initial entity placements) are synchronized via DDS.  
  * `LoadingDryRun`: The system takes an in-memory snapshot (checkpoint) of the `Editing` state.  
  * `RunningDryRun`: Simulation time begins ticking. AI and kinematics run.  
  * `UnloadingDryRun`: Simulation stops. Nodes dump the modified ECS state and restore the in-memory snapshot taken during `LoadingDryRun`. System returns to `Editing`.  
  * `LoadingLive` / `RunningLive` / `UnloadingLive`  
  * `LoadingReplay` / `RunningReplay` / `UnloadingReplay`

---

### 3\. Checkpoints (Lightning-Fast RAM Snapshots & Bug Reporting)

You requested the ability to take a snapshot during a live simulation in under a few tens of milliseconds, storing it in RAM, with async disk flushing.

**Architectural Concept:** Because FDP uses `NativeChunkTable` (64KB blocks of unmanaged memory), taking a snapshot is trivial and blazingly fast.

1. **Freeze:** The Master sends a `NodeOpCommand(TakeCheckpoint, CheckpointId)`.  
2. **RAM Copy:** At the next `SystemPhase.BeforeSync`, each node locks its `EntityRepository`. It allocates a new set of 64KB chunks and does a raw `memcpy` of the active chunks. For 100,000 entities, this is a few megabytes of contiguous memory. A raw `memcpy` of this takes **\< 2 milliseconds**.  
3. **Unfreeze:** The `EntityRepository` is unlocked, and the live simulation continues immediately.  
4. **Async Save:** A background thread on each node takes that copied RAM buffer, compresses it via LZ4 (like the FlightRecorder), and writes it to disk as `checkpoint_[CheckpointId]_[NodeId].fdp`.  
5. **Archive Collection:** If an operator submits a bug report, the IOS sends a `SysOpRequest(CollectCheckpoint, CheckpointId)`. The Master commands all nodes to upload their local `checkpoint_[...].fdp` files to a centralized archive folder under the current `DrillId`.

---

### 4\. Node Health Monitoring (Built-In Test / BIT)

Using `SysOpRequests` for health checks is indeed too heavy.

**Architectural Concept:**

* **`NodeHeartbeat` (DDS Topic):** Every node in the system (SimHost, IG, IOS, Loggers) autonomously publishes a heartbeat at 1Hz.  
* **Payload:** Includes `NodeId`, `CurrentEsmState` (to detect if a node got stuck), `CpuUsage`, `RamUsage`, and a `Subsystems` dictionary (e.g., `{"RenderThread": "Healthy", "DdsTransport": "Degraded"}`).  
* **Master Watchdog:** The Master maintains a registry of expected nodes (based on the initial `Loading` phase). If a node misses 5 consecutive heartbeats, the Master transitions the `SystemState` to `Degraded` or `Error`, halting the simulation and notifying the IOS.

---

### 5\. Battlespace Swapping over DDS

You correctly pointed out that `CmdSwapBattlespace` must be a DDS message, not a local ECS event.

**Architectural Concept:** Loading a battlespace is a Two-Phase Commit orchestrated by the Master:

1. **Phase 1 (Prepare):** Master sends `NodeOpCommand(PrepareBattlespace, BattlespaceJson)`. Nodes spin up background threads to load the heavy NavMesh/Terrain data into passive memory. They report `NodeOpStatus = Ready` when finished.  
2. **Phase 2 (Commit):** Once all required nodes are `Ready`, the Master sends `NodeOpCommand(CommitBattlespace, BattlespaceId)`.  
3. **Execution:** Upon receiving `CommitBattlespace`, each node's network ingress layer pushes a local ECS event (`CmdSwapBattlespace`). The systems swap their pointers from the old terrain to the new terrain in the very next frame, ensuring perfect synchronization across all IGs and SimHosts.

---

### 6\. Deterministic Batch Runs

You need a perfectly reproducible, slower-than-realtime mode for automated testing and verification.

**Architectural Concept:** FDP already has the foundational infrastructure for this in `FDP.Toolkit.Time`.

* During `LoadingLive`, the IOS specifies the `TimeMode` in the `SysOpRequest` (e.g., `TimeMode = Deterministic`).  
* When transitioning to `RunningLive`, the Master initializes the `SteppedMasterController` instead of the `MasterTimeController`.  
* The Slaves initialize the `SteppedSlaveController`.  
* **Execution:** The Master publishes a `FrameOrderDescriptor`. Every node simulates exactly one frame (`fixedDeltaTime = 1/60f`), regardless of how much actual wall-clock time it takes, and replies with a `FrameAckDescriptor`. The Master waits for all ACKs before issuing the next `FrameOrderDescriptor`.  
* This results in a 100% deterministic simulation where execution speed is bound by the slowest node, ensuring perfectly identical outcomes for automated testing.

---

to be consistent, let’s rename ESM state `editing` to `RunningEdit`

Let’s pls go over each of the new necessary concepts one by one, not duplicating what was already said, but providing more details on each. 

1. **`SystemMasterModule`**  
2. **`SystemSlaveModule`**

---

Got it. `Editing` is now `RunningEdit` (which aligns perfectly with `RunningLive`, `RunningReplay`, and `RunningDryRun`).

Let’s dive deep into the internal mechanics of the orchestration modules. We will focus on *how* they manage state, handle concurrency, and recover from failures, avoiding the high-level descriptions we’ve already covered.

---

### 1\. `SystemMasterModule` (The Orchestrator)

The Master module is the ultimate source of truth for the cluster. Its primary job is to protect the Exercise State Machine (ESM) from invalid transitions and coordinate distributed transactions (Two-Phase Commit) without blocking its own simulation loop.

#### A. The Dynamic Node Roster

To know *who* to wait for during a transaction, the Master cannot rely on a hardcoded list (since nodes might crash, or dynamic loggers/evaluators might join).

* **The Roster:** The Master maintains a `Dictionary<int, NodeHealthProfile> ActiveNodes`.  
* **Maintenance:** It updates this dictionary by consuming the `NodeHeartbeat` topic.  
* **Pruning:** During `Standby` or `RunningEdit`, if a node misses 5 heartbeats, it is quietly removed from the roster. However, if a node drops out during a `Loading` or `Running` state, the Master triggers a system-wide fault.

#### B. Transaction Management (The 2PC Engine)

When a `SysOpRequest` arrives from the IOS, the Master spawns a tracked transaction.

**Conceptual Structure:**

class DistributedTransaction {

    public Guid TransactionId;

    public SysOpType Operation;

    public ESMState TargetState; // If this op results in a state change

    public HashSet\<int\> PendingNodes; // Cloned from ActiveNodes at T=0

    public float TimeoutSeconds;

    public float ElapsedTime;

}

**The Execution Flow:**

1. **Guard Check:** Master checks if the `SysOpRequest` is valid for the current ESM state (e.g., cannot `SaveScenario` while `RunningLive`). If invalid, it immediately replies to IOS with `SysOpStatus(Failed)` and stops.  
2. **Initialize Transaction:** Master creates a `DistributedTransaction`, copying the current keys from `ActiveNodes` into `PendingNodes`. It generates a unique `TransactionId`.  
3. **Broadcast:** Master publishes `NodeOpCommand`.  
4. **The Wait Loop (in `Tick()`):** Every frame, the Master evaluates active transactions. It increases `ElapsedTime`. If a `NodeOpStatus` arrives from a node in `PendingNodes`:  
   * If `Status == InProgress`: The Master forwards a `SysOpUpdate` to the IOS (useful for progress bars on heavy loads) and resets the node's individual timeout clock.  
   * If `Status == Success` (or `IsParticipating == false`): The node is removed from `PendingNodes`.  
   * If `Status == Failed`: The transaction aborts immediately (see below).  
5. **Commit:** When `PendingNodes.Count == 0`, the transaction is successful. If `TargetState` was set, the Master updates the `SystemState` topic (which includes the `DrillId`), publishes `SysOpStatus(Success)` to the IOS, and clears the transaction.

#### C. Failure & Rollback Handling

If a transaction times out, or a node explicitly reports `Failed`, the Master must act decisively to prevent a split-brain cluster (where half the nodes loaded a new battlespace and the other half didn't).

* **Abort Broadcast:** Master broadcasts `NodeOpCommand(Abort, TransactionId)`.  
* **State Reversion:** If the system was in `LoadingLive` and failed, the Master reverts the ESM to `Standby` or `RunningEdit` and alerts the IOS.

---

### 2\. `SystemSlaveModule` (The Executor)

The Slave module runs on every node (IG, SimHost, Loggers). Its job is to interpret `NodeOpCommand`s, execute them without freezing the local application, and report back to the Master.

#### A. The Command Dispatcher & Idempotency

Because DDS guarantees delivery but can sometimes result in duplicates (depending on QoS and late-joining logic), the Slave must be idempotent.

* **History Cache:** The Slave maintains a bounded `HashSet<Guid>` of recently seen `TransactionId`s. If it receives a duplicate, it re-transmits its last known `NodeOpStatus` but does not restart the work.

#### B. Asynchronous Task Execution (The Background Worker)

The ECS `Tick()` must run at 60Hz. Operations like `LoadBattlespace` or `SaveScenario` take seconds. The Slave cannot block the main thread.

**Conceptual Structure:**

class ActiveNodeOperation {

    public Guid TransactionId;

    public Task BackgroundTask;

    public Action\<EntityRepository\> MainThreadCommitAction; 

}

**The Execution Flow:**

1. **Receive Command:** Slave reads `NodeOpCommand`.  
2. **Participation Check:** If the command is `LoadBattlespace`, but this node is a pure headless logger that doesn't need terrain, it immediately publishes `NodeOpStatus(Success, IsParticipating = false)`.  
3. **Start Async Work:** If it *does* need to act, it publishes `NodeOpStatus(InProgress)` to appease the Master's timeout watchdog. It spins up a `Task.Run(...)`.  
4. **The Monitor Loop (in `Tick()`):** Every ECS frame, the Slave module checks the status of `ActiveNodeOperation.BackgroundTask`.  
   * If the task throws an exception, catch it, log it, and publish `NodeOpStatus(Failed)`.  
   * If the task completes successfully, publish `NodeOpStatus(Success)`.

#### C. Bridging Async Work back to the Synchronous ECS

This is the trickiest part of the Slave module. A background thread cannot directly mutate the ECS `EntityRepository`.

Let's use the **TakeCheckpoint (Fast RAM Snapshot)** as an example of how the Slave handles synchronous orchestration:

1. Master says "Take Checkpoint 123".  
2. Slave's `SystemSlaveModule` receives it on the network thread.  
3. Because a snapshot requires the world to be frozen, it cannot be done on a background thread. Instead, the Slave queues an *Instruction* for the main thread.  
4. On the very next `SystemPhase.BeforeSync`, the Slave module's `Tick()` method executes the RAM copy (`NativeChunkTable.Clone()`). This takes \~2ms.  
5. The Slave immediately publishes `NodeOpStatus(Success)` and hands the cloned memory buffer to a *Background Task* to compress and write to disk (`checkpoint_123_node.fdp`).

Let's use **LoadBattlespace** as an example of async-to-sync:

1. Master says "Prepare Battlespace XYZ".  
2. Slave publishes `InProgress` and starts a background `Task` to parse NavMesh JSONs and load textures.  
3. Task completes. Slave publishes `Success`.  
4. Master eventually replies "Commit Battlespace XYZ".  
5. Slave's network thread pushes a local ECS Event (`CmdSwapBattlespace`) onto the `FdpEventBus`.  
6. Next frame, the local `PhysicsSystem` and `RenderingSystem` consume the event and update their pointers to the new, fully-loaded data.

#### D. The Autonomous Heartbeat

The `SystemSlaveModule` owns a simple, self-resetting timer (independent of the ECS simulation time, using `System.Diagnostics.Stopwatch`). Every 1 second of wall-clock time, it grabs the process's working set memory (RAM), CPU time, and checks if the ECS `Tick()` is still advancing. It packs this into a `NodeHeartbeat` and pushes it to DDS. This ensures the Master knows the node is alive even if the simulation is currently paused.

---

Pls detail the following:

1. How should the replay slave module of FDP-enabled nodes using the FligthRecorder work. How it connects to the flight recorder.  
2. How the exercise state machine is encoded and handled. How the state change request to change the state and the response machine might look like (examples).  
3. Detail the flow and actions taken when user performs the following sequence. For each step detail what each of the affected nodes/subsystems (both on master as well as on the slave side) need to do  
   1. start from standby  
   2. start editing a new scenario  
   3. use dry run to see what his scenario might look like at run time  
   4. save scenario  
   5. load a full live exercise from the scenario, with full recording enabled  
   6. make few pause/resume and create a  snapshot  
   7. finish live exercise by unloading to standby  
   8. initialize the replay mode from the recordings saved during the previous live run.  
   9. start replaying, perform some time jumps  
   10. switch from replay into live exercise (live-from-replay).  
   11. finish the live exercise  
   12. start editing a new scenario from the snapshot saved during the live run

   

   

---

Here is the detailed architectural breakdown for each of the requested concepts, aligning the new orchestration framework with the FDP Kernel’s existing capabilities (like `PlaybackController`, `NativeChunkTable`, and `TimePulse`).

---

### A. Replay Slave Module & Flight Recorder Integration

During `RunningReplay`, the normal simulation pipeline (AI, physics, kinematics) is **disabled**. The state of the ECS world is driven entirely by the network-synchronized disk recordings.

**1\. The Components:**

* **`ReplayMasterModule` (on the Time Master node):** Takes control of the `MasterTimeController`. Instead of advancing normal simulation time, it acts as the "Playhead." It dictates the current Replay Time and broadcasts it via DDS (`ReplayTimePulse`).  
* **`ReplaySlaveModule` (on every FDP node, e.g., IG, SimHost):** Owns an instance of `PlaybackController` (from `Fdp.Kernel.FlightRecorder`). It listens to the `ReplayTimePulse`.

**2\. How it works per-frame:**

1. **Network Ingress:** The `ReplaySlaveModule` receives a `ReplayTimePulse` from the Master, which specifies `TargetTick = 4500` (the exact frame to render).  
2. **Bypass Simulation:** The `SystemScheduler` dynamically disables the `SimulationSystemGroup` and `PostSimulationSystemGroup`.  
3. **Seek and Apply:** During the `BeforeSync` phase, the `ReplaySlaveModule` calls `PlaybackController.SeekToTick(repo, TargetTick)`.  
   * *Under the hood:* The `PlaybackController` does a binary search through its `FrameMetadata` index to find the closest recorded frame.  
   * It reads the LZ4 compressed payload, decompresses it, and calls `PlaybackSystem.ApplyFrame()`.  
   * `ApplyFrame()` uses `memcpy` to blast the recorded unmanaged chunks directly into the `NativeChunkTable`, instantly restoring the exact positions, health, and state of all entities at that tick.  
4. **Render:** The `PresentationSystemGroup` runs normally, rendering the injected state.

**3\. Handling Async/Dropped Frames:** Because the `PlaybackController` seeks based on `TargetTick` rather than just calling `StepForward()`, it is immune to node lag. If an IG node stalls for 500ms, it will receive a new `ReplayTimePulse` that is 30 ticks ahead. It simply seeks to the new tick, skipping the missed frames, ensuring all monitors across the network show the exact same moment in time.

---

### B. ESM Encoding and Handling

The Exercise State Machine (ESM) ensures all nodes agree on what the system is doing.

**1\. The Data Structures (bdc-sst-orchestration)**

public enum ESMState : int 

{

    Standby,

    LoadingEdit,

    RunningEdit,

    UnloadingEdit,

    LoadingDryRun,

    RunningDryRun,

    UnloadingDryRun,

    LoadingLive,

    RunningLive,

    UnloadingLive,

    LoadingReplay,

    RunningReplay,

    UnloadingReplay,

    Degraded \= 99 // Health failure

}

\[DdsTopic("SystemState")\]

public struct SystemStateTopic 

{

    public ESMState CurrentState;

    public Guid DrillId;           // Unique ID for the current live/replay session

    public long StateStartTime;    // Wall-clock time the state was entered

}

**2\. Example: Requesting a State Change (IOS $\\to$ Master $\\to$ Slaves)** Let's say the IOS wants to transition from `Standby` to `LoadingLive`.

* **Step 1: IOS Request** IOS publishes: `SysOpRequest { RequestId = GUID_1, OperationType = TransitionState, PayloadJson = "{ 'TargetState': 'LoadingLive', 'ScenarioId': 'Desert_01' }" }`  
* **Step 2: Master Validates & Commands** Master checks if `Standby -> LoadingLive` is a valid transition. It is. It generates a new `DrillId`. Master publishes: `NodeOpCommand { TransactionId = GUID_1, Operation = PrepareState, PayloadJson = "{ 'TargetState': 'LoadingLive', 'DrillId': '...', 'ScenarioId': 'Desert_01' }" }`  
* **Step 3: Slaves Execute & Respond** SimHost and IG receive the command. They load the terrain/assets for `Desert_01`. While doing this, they publish: `NodeOpStatus { TransactionId = GUID_1, NodeId = 100, Status = InProgress }` When finished loading, they publish: `NodeOpStatus { TransactionId = GUID_1, NodeId = 100, Status = Success }`  
* **Step 4: Master Commits** Master sees all nodes report `Success`. It updates the globally visible state: `SystemStateTopic { CurrentState = LoadingLive, DrillId = '...' }` Master notifies IOS: `SysOpStatus { RequestId = GUID_1, Status = Success }`

---

### C. 12-Step Sequence Analysis

Here is the exact orchestration flow for your scenario.

#### 1\. Start from Standby

* **Master:** Initializes. Broadcasts `SystemState(Standby)`. Watchdog monitors `NodeHeartbeat`s to build the active cluster roster.  
* **Slaves (SimHost/IG):** Idle. `EntityRepository` is empty. Publishing 1Hz health heartbeats.  
* **IOS:** Boots up, reads `SystemState`, and enables the "New Scenario" / "Load Exercise" UI.

#### 2\. Start Editing a new Scenario

* **IOS:** Sends `SysOpRequest` to transition to `LoadingEdit`, providing initial scenario metadata (e.g., base map).  
* **Master:** Issues 2PC `NodeOpCommand(PrepareState, LoadingEdit)`.  
* **Slaves:** Load terrain and static boundaries into memory. Return `Success`.  
* **Master:** Commits state to `RunningEdit`.  
* **All Nodes:** In `RunningEdit`, time is frozen. Entities can be placed via IOS/IG clicks. `NetworkSpawningSystem` creates entities, but simulation brains (BTrees/HSMs) do not tick.

#### 3\. Use Dry Run

* **IOS:** Clicks "Dry Run". Sends `SysOpRequest(TransitionState, LoadingDryRun)`.  
* **Master:** Commands `TakeCheckpoint` (in-memory snapshot).  
* **Slaves:** The `SystemSlaveModule` creates a new `EntityRepository` pool instance. It uses `NativeChunkTable.SyncDirtyChunks()` to clone the current world state into a backup RAM buffer (takes \~2ms). Reports `Success`.  
* **Master:** Commits state to `RunningDryRun`. Commands Time Master to start advancing simulation time.  
* **Slaves:** Simulation brains engage. Vehicles drive, entities shoot.

#### 4\. Save Scenario

* **IOS:** Clicks "Stop Dry Run". Sends `SysOpRequest(TransitionState, UnloadingDryRun)`.  
* **Master:** Pauses time. Commands slaves to restore the RAM snapshot.  
* **Slaves:** Blast the backup `NativeChunkTable` back into the live repository. The world instantly rewinds to the pre-dry-run state. Reports `Success`.  
* **Master:** Commits state back to `RunningEdit`.  
* **IOS:** Clicks "Save". Sends `SysOpRequest(SaveScenario, "Scenario_Alpha")`.  
* **Slaves:** Extract non-default entity overrides and serialize them to JSON. Save to disk.

#### 5\. Load full Live Exercise with Recording

* **IOS:** Sends `SysOpRequest(TransitionState, LoadingLive)`.  
* **Master:** Generates a new `DrillId` (e.g., `Drill_999`). Commands `NodeOpCommand` to prepare for live.  
* **Slaves:** Initialize `AsyncRecorder` pointing to `/archives/Drill_999/node_[id].fdp`. Write the global file header.  
* **Master:** Commits state to `RunningLive`. Unpauses time.

#### 6\. Pause/Resume and Checkpoint

* **IOS:** Clicks "Pause". Sends `SysOpRequest` to pause time.  
* **Master:** Broadcasts `SwitchTimeModeEvent(Deterministic/Paused)`.  
* **Slaves:** Freeze physics. `AsyncRecorder` continues capturing *events* (e.g., a user draws a new tactical graphic on the map), appending them to delta frames marked with the continuing Wall-Clock time.  
* **IOS:** Clicks "Snapshot". Sends `SysOpRequest(TakeCheckpoint, "Checkpoint_Bug01")`.  
* **Slaves:** Perform a 2ms RAM clone of the `EntityRepository`. A background Task compresses this clone and writes it to `/archives/Drill_999/checkpoint_Bug01_node[id].fdp`.  
* **IOS:** Clicks "Play". Master broadcasts `SwitchTimeModeEvent(Continuous)`. Physics resume.

#### 7\. Finish Live Exercise

* **IOS:** Sends `SysOpRequest(TransitionState, UnloadingLive)`.  
* **Master:** Pauses time. Commands slaves to finalize.  
* **Slaves:** Flush `AsyncRecorder` buffers to disk. Close `.fdp` file handles. Clear the `EntityRepository`.  
* **Master:** Commits state to `Standby`.

#### 8\. Initialize Replay Mode

* **IOS:** Selects `Drill_999` and clicks "Replay". Sends `SysOpRequest(TransitionState, LoadingReplay)`.  
* **Master:** Commands nodes to prepare replay for `Drill_999`.  
* **Slaves:** Instantiate `PlaybackController`, point it to `/archives/Drill_999/node_[id].fdp`. Load the schema manifest. Report `Success`.  
* **Master:** Transitions to `RunningReplay` (time initially paused at frame 0).

#### 9\. Start Replaying, Time Jumps

* **IOS:** Clicks Play (1x speed).  
* **Master:** Advances replay clock. Publishes `ReplayTimePulse`.  
* **Slaves:** `ReplaySlaveModule` reads the pulse, tells `PlaybackController` to advance one frame, overwriting the ECS. Renderers draw the state.  
* **IOS:** Scrubs the timeline to T+5:00.  
* **Master:** Broadcasts target tick 18000\.  
* **Slaves:** `PlaybackController.SeekToTick(18000)` executes binary search in the `.fdp` file, restores the nearest keyframe, fast-forwards deltas, and updates the ECS. Total time: \~5-15ms.

#### 10\. Switch from Replay into Live (Live-from-Replay)

* **IOS:** Clicks "Take Control" (Live-from-Replay). Sends `SysOpRequest(TransitionState, LoadingLive)`.  
* **Master:** Generates a *new* `DrillId` (e.g., `Drill_999_Branch1`). Commands slaves to prepare for live.  
* **Slaves:**  
  * Dispose the `PlaybackController` (stop reading disk).  
  * Keep the *current* ECS state exactly as it was at the paused replay moment.  
  * Initialize a *new* `AsyncRecorder` for `Drill_999_Branch1`.  
* **Master:** Commits state to `RunningLive`. Unpauses standard simulation time. Entities seamlessly "wake up" and begin running AI/Physics from that exact historical state.

#### 11\. Finish the Live Exercise

* **IOS:** Sends `SysOpRequest(TransitionState, UnloadingLive)`.  
* **Master/Slaves:** Exact same as Step 7\. Flushes the branched recording to disk. System returns to `Standby`.

#### 12\. Edit Scenario from Checkpoint

* **IOS:** In Standby, selects the checkpoint from Step 6\. Sends `SysOpRequest(TransitionState, LoadingEdit, Payload: Checkpoint_Bug01)`.  
* **Master:** Commands nodes to load the checkpoint.  
* **Slaves:** Open `checkpoint_Bug01_node[id].fdp`. Because it's a full keyframe, `PlaybackSystem.ApplyFrame()` blasts the exact state into the `EntityRepository`.  
* **Master:** Commits state to `RunningEdit`. The system is now ready for the scenario editor to inspect, modify, and re-save the situation exactly as it looked when the bug occurred.

\`\`\`  
sequenceDiagram  
    autonumber  
    participant IOS  
    participant Master as SystemMasterModule  
    participant Slave as SystemSlaveModule  
    participant ECS as NativeChunkTable / Recorder

    %% 1\. Start from Standby  
    Note over IOS, ECS: 1\. STANDBY  
    Master-\>\>Master: SystemState(Standby) & Monitor Heartbeats  
      
    %% 2\. Start Editing a new Scenario  
    Note over IOS, ECS: 2\. START EDITING NEW SCENARIO  
    IOS-\>\>Master: SysOpRequest(LoadingEdit)  
    Master-\>\>Slave: NodeOpCommand(PrepareState, LoadingEdit)  
    Slave-\>\>Master: NodeOpStatus(Success)  
    Master-\>\>Master: Commit: RunningEdit

    %% 3\. Use Dry Run  
    Note over IOS, ECS: 3\. USE DRY RUN  
    IOS-\>\>Master: SysOpRequest(TransitionState, LoadingDryRun)  
    Master-\>\>Slave: NodeOpCommand(TakeCheckpoint)  
    Slave-\>\>ECS: Clone ChunkTable to RAM (2ms)  
    Slave-\>\>Master: NodeOpStatus(Success)  
    Master-\>\>Master: Commit: RunningDryRun (Simulation Ticks)

    %% 4\. Save Scenario  
    Note over IOS, ECS: 4\. SAVE SCENARIO  
    IOS-\>\>Master: SysOpRequest(TransitionState, UnloadingDryRun)  
    Master-\>\>Slave: NodeOpCommand(RestoreSnapshot)  
    Slave-\>\>ECS: Blast RAM backup to active ChunkTable  
    Master-\>\>Master: Commit: RunningEdit (Rewound)  
    IOS-\>\>Master: SysOpRequest(SaveScenario)  
    Slave-\>\>Slave: Save JSON to Disk

    %% 5\. Load full Live Exercise with Recording  
    Note over IOS, ECS: 5\. LOAD LIVE EXERCISE (RECORDING ENABLED)  
    IOS-\>\>Master: SysOpRequest(TransitionState, LoadingLive)  
    Master-\>\>Master: Generate Drill\_999  
    Master-\>\>Slave: NodeOpCommand(PrepareLive)  
    Slave-\>\>ECS: Init AsyncRecorder (Drill\_999/node.fdp)  
    Master-\>\>Master: Commit: RunningLive

    %% 6\. Pause/Resume and Checkpoint  
    Note over IOS, ECS: 6\. PAUSE/RESUME AND CHECKPOINT  
    IOS-\>\>Master: SysOpRequest(PauseTime)  
    Master-\>\>Slave: SwitchTimeMode(Paused)  
    IOS-\>\>Master: SysOpRequest(TakeCheckpoint, "Bug01")  
    Slave-\>\>ECS: Clone ChunkTable to RAM & Compress to Disk  
    IOS-\>\>Master: SysOpRequest(ResumeTime)

    %% 7\. Finish Live Exercise  
    Note over IOS, ECS: 7\. FINISH LIVE EXERCISE  
    IOS-\>\>Master: SysOpRequest(TransitionState, UnloadingLive)  
    Master-\>\>Slave: NodeOpCommand(FinalizeLive)  
    Slave-\>\>ECS: Flush Recorder, Close .fdp  
    Master-\>\>Master: Commit: Standby

    %% 8\. Initialize Replay Mode  
    Note over IOS, ECS: 8\. INITIALIZE REPLAY MODE  
    IOS-\>\>Master: SysOpRequest(TransitionState, LoadingReplay)  
    Master-\>\>Slave: NodeOpCommand(PrepareReplay, Drill\_999)  
    Slave-\>\>ECS: Init PlaybackController  
    Master-\>\>Master: Commit: RunningReplay (Time Paused)

    %% 9\. Start Replaying, Time Jumps  
    Note over IOS, ECS: 9\. REPLAY & TIME JUMPS  
    IOS-\>\>Master: Click Play \-\> Advance Replay Clock  
    Master-\>\>Slave: ReplayTimePulse(TargetTick)  
    IOS-\>\>Master: Scrub Timeline (ReplaySeek, TargetTick: 18000\)  
    Slave-\>\>ECS: PlaybackController.SeekToTick(18000)

    %% 10\. Switch from Replay into Live (Live-from-Replay)  
    Note over IOS, ECS: 10\. SWITCH TO LIVE (LIVE-FROM-REPLAY)  
    IOS-\>\>Master: SysOpRequest(TransitionState, LoadingLive)  
    Master-\>\>Master: Generate NEW DrillId (Branch)  
    Master-\>\>Slave: NodeOpCommand(PrepareLiveFromReplay)  
    Slave-\>\>ECS: Dispose PlaybackController, Keep ECS State, Init new AsyncRecorder  
    Master-\>\>Master: Commit: RunningLive

    %% 11\. Finish the Live Exercise  
    Note over IOS, ECS: 11\. FINISH LIVE EXERCISE  
    IOS-\>\>Master: SysOpRequest(TransitionState, UnloadingLive)  
    Master-\>\>Slave: NodeOpCommand(FinalizeLive)  
    Slave-\>\>ECS: Flush branched recording to disk  
    Master-\>\>Master: Commit: Standby

    %% 12\. Edit Scenario from Checkpoint  
    Note over IOS, ECS: 12\. EDIT SCENARIO FROM CHECKPOINT  
    IOS-\>\>Master: SysOpRequest(LoadingEdit, Payload: Checkpoint\_Bug01)  
    Master-\>\>Slave: NodeOpCommand(PrepareEdit, Checkpoint\_Bug01)  
    Slave-\>\>ECS: Load checkpoint.fdp directly into EntityRepository  
    Master-\>\>Master: Commit: RunningEdit  
\`\`\`

---

Pls details the working with stories. Assume the system is initialized to live exercise with empty scenario so world is empty; no recording enabled, just waiting for the stories to happen. Not that neither of the following should block the system as that would negatively affect the other parallelly running stories:  
Sequence

1. load a battlefield (preparation for a story run)  
2. start a story (with story-specific recording enabled)  
3. keep running the live story till the end  
   1. no story-pause possible, pause is system-wide and would affect other stories  
4. stop a live story (finalize the recordings)  
5. replay a story (no pauses possible)  
6. forget about that story run

Detail how the PlaybackController works. And how nodes prepare to replay.  
The seek to tick operation will for sure take lots of time (not all nodes are using lighting fast Flight Recorder but a custom solutions that might iteratively reconstruct some very complex state \- like particles of a smoke clouds \- might easily take seconds or more) and needs to be synced across nodes and will block the whole system until finished. Should it be just another system wide operation and not a new ESM state like `ReplayJumpToTime`?

---

This is a fascinating architectural challenge. You are essentially building a **Multi-Tenant Simulation Engine**, where the global world is the "host" and Stories are isolated "containers" running inside it.

To achieve this without blocking the global simulation or corrupting the global ECS memory, we must rethink how recording and playback work for Stories versus the Global System.

Here is the detailed design for handling Stories, followed by the solution for the heavy `SeekToTick` synchronization.

---

### Part 1: The "Story" Paradigm (Multi-Tenant ECS)

A Story is a micro-scenario. Because multiple stories run concurrently in the `RunningLive` state, **we cannot use the raw `NativeChunkTable` memory-blast for Story Replay.** Blasting 64KB chunks into RAM would overwrite and destroy entities belonging to other active stories.

Instead, Story Recording/Replay uses **Entity Remapping**.

#### The Story Data Structures

* **`StoryTag` Component:** `[ComponentId(...)] public struct StoryTag { public Guid StoryId; }`  
* **`StoryReplayTag` Component:** Marks an entity as a "ghost" from a past recording so live AI systems ignore it.  
* **`StoryRecorder`:** A targeted version of the `AsyncRecorder` that only iterates entities matching `Query().With<StoryTag>().Build()`.  
* **`StoryPlaybackController`:** Reads a story `.fdp` file. It maintains a mapping `Dictionary<int, Entity> RecordedToLiveId`. When playing back, it translates recorded component data and injects it into newly allocated "Replay Ghost" entities.

---

### Part 2: The Story Sequence (Step-by-Step)

Assume the global ESM is in `RunningLive`. Time is ticking.

#### 1\. Load a Battlefield (Preparation)

* **IOS:** Sends `SysOpRequest(LoadStoryAssets, "{ 'StoryId': 'A1', 'Region': 'Sector7' }")`.  
* **Master:** Issues `NodeOpCommand(LoadStoryAssets)`. (This does *not* pause the global clock).  
* **Slaves (IG/SimHost/Loggers):**  
  * A background `Task` loads the specific 3D models, textures, or nav-meshes required for Sector 7 into a background memory pool.  
  * This is purely additive. It does not block the main ECS thread.  
  * Once loaded into RAM, the slave replies `NodeOpStatus(Success)`.  
* **Master:** Acknowledges to IOS that the story is ready to instantiate.

#### 2\. Start a Story (Record Enabled)

* **IOS:** Sends `SysOpRequest(StartStory, 'A1')`.  
* **Master:** Issues `NodeOpCommand(StartStory)`.  
* **Slaves:**  
  * Instantiate a new `StoryRecorder` targeting `StoryId: A1`. It opens a file stream: `temp/story_A1_node100.fdp`.  
  * The `NetworkSpawningSystem` creates the required entities, attaching the `StoryTag { StoryId = A1 }`.  
* **Simulation Loop:** As the ECS ticks, the `StoryRecorder` captures only entities with this tag and events marked with this ID.

#### 3\. Keep Running Live (No Pauses)

* The global clock continues.  
* If IOS clicks "Pause" for a specific story, **the global clock does not stop.** Instead, the Master sends a targeted command to strip the `ActorCapabilityState.CanMove` and `CanShoot` from all entities with `StoryTag: A1`. To the trainee, the story appears paused, but other stories continue unaffected. The `StoryRecorder` continues recording these "frozen" entities.

#### 4\. Stop a Live Story (Finalize)

* **IOS:** Sends `SysOpRequest(StopStory, 'A1')`.  
* **Master:** Issues `NodeOpCommand(StopStory)`.  
* **Slaves:**  
  * The `StoryRecorder` flushes its remaining buffers to disk and closes the `.fdp` file.  
  * The slave destroys all live entities carrying `StoryTag: A1`.  
  * Replies `Success`.

#### 5\. Replay a Story (Isolated Playback)

* **IOS:** Sends `SysOpRequest(ReplayStory, 'A1')`.  
* **Master:** Issues `NodeOpCommand(ReplayStory)`.  
* **Slaves:**  
  * Instantiate a `StoryPlaybackController` pointing to `temp/story_A1_node100.fdp`.  
  * **Crucial Difference:** The controller reads the first frame. For every recorded `EntityId`, it allocates a *brand new* entity in the live ECS.  
  * It attaches `StoryReplayTag` to these entities.  
  * Live Physics/AI systems are hard-coded to **ignore** entities with `StoryReplayTag`.  
  * Every frame, the `StoryPlaybackController` reads the delta from the file, maps the recorded ID to the new live ID, and calls `SetComponent` to forcefully update their positions, rotations, and visual states.  
  * *Result:* The trainee sees a 3D hologram movie of their run happening inside the live world, without it physically interacting with other live stories.

#### 6\. Forget About That Story Run

* **IOS:** Sends `SysOpRequest(ForgetStory, 'A1')`.  
* **Master:** Issues `NodeOpCommand(ForgetStory)`.  
* **Slaves:**  
  * Destroy all entities with `StoryReplayTag` linked to `A1`.  
  * Dispose the `StoryPlaybackController`.  
  * Delete `temp/story_A1_node100.fdp` from the disk.  
  * Free the background memory pool containing Sector 7 assets.

---

### Part 3: The `PlaybackController` and the Heavy `SeekToTick`

You asked a very insightful question: *Should seeking be a system-wide operation (SysOp) rather than a new ESM state?*

**Yes, absolutely.** `ReplaySeek` is an *operation* that occurs while the system is already residing in the `RunningReplay` ESM state. Because seeking can take seconds for complex non-FDP nodes (like reconstructing volumetric smoke, particle systems, or legacy C++ physics engines), it perfectly fits the Two-Phase Commit (2PC) SysOp architecture.

#### How `PlaybackController` works during Global Replay

Unlike Story Replay (which uses entity remapping), **Global Replay** (the whole exercise) has absolute ownership of the `NativeChunkTable`. When playing forward normally, the `PlaybackController` reads the LZ4 compressed delta from the `.fdp` file, decompresses it, and uses unsafe pointers (`memcpy`) to overwrite the exact memory addresses in the ECS chunks. This takes less than 1 millisecond.

#### The Heavy Seek Flow (Using SysOp)

Assume the system is in `RunningReplay` and the time is currently `T=01:00`. The IOS user drags the timeline to `T=15:00`.

1. **IOS:** Sends `SysOpRequest(ReplaySeek, "{ TargetTick: 54000 }")`.  
2. **Master:**  
   * Validates the request.  
   * *Freezes the Replay Playhead* (stops publishing `ReplayTimePulse` so time stops advancing).  
   * Issues 2PC `NodeOpCommand(ReplaySeek, 54000)`.  
3. **Slaves (FDP Nodes \- e.g., SimHost):**  
   * `PlaybackController.SeekToTick(54000)` is called on a background thread.  
   * It performs a binary search in the `.meta.json` index to find the nearest keyframe (e.g., tick `53900`).  
   * It blasts the keyframe into RAM. It then fast-forwards the next 100 delta frames in memory.  
   * This takes \~5-10ms.  
   * SimHost publishes `NodeOpStatus(Success)`.  
4. **Slaves (Heavy Custom Nodes \- e.g., IG doing smoke reconstruction):**  
   * Receives `ReplaySeek`.  
   * Immediately publishes `NodeOpStatus(InProgress)` so the Master knows it is working.  
   * Begins a heavy CPU operation: tearing down the old scene graph, recalculating 15 minutes of particle decay, rebuilding volumetric clouds.  
   * This takes 2.5 seconds.  
   * When finished, it publishes `NodeOpStatus(Success)`.  
5. **Master:**  
   * Watches the `NodeOpStatus` messages. It knows the IG is taking a long time, so it forwards `SysOpStatus(InProgress)` to the IOS (which can show a "Buffering..." spinner on the UI).  
   * Once the heavy IG reports `Success`, all nodes are perfectly synchronized at `T=15:00`.  
   * Master broadcasts `SysOpStatus(Success)` to the IOS.  
   * Master resumes the `ReplayTimePulse` from tick 54000\. The visual replay resumes playing smoothly for the user.

### Summary of the Design Choice

By treating `Seek` as a `SysOpRequest` rather than a state change, you leverage the exact same distributed synchronization mechanics used for loading scenarios. The Master ensures that **no node moves forward in time until the slowest node has finished reconstructing its state**, eliminating visual tearing, desyncs, and crashing.

---

i would like to discuss all that in a new chat thread where there are not all the source codes available. I would like you to describe architecturally/conceptually the parts of our system that are important for this discussion, including the relevant interfaces and data structures (mentioning from what source file they come from for traceability), so that the discussion can continue.

---

Here is a comprehensive architectural and conceptual brief of the **Bagira/FDP (Fast Data Plane) Platform**, incorporating both the existing codebase and the newly designed orchestration layer.

You can copy and paste this entire response into a new chat thread to instantly provide the LLM with the exact architectural context, data structures, and file traceability needed to continue development.

---

# Bagira/FDP Platform: Architecture & Context Brief

## 1\. High-Level System Overview

Bagira is an enterprise-grade, distributed simulation platform built on a custom, high-performance Entity Component System (ECS) called **FDP (Fast Data Plane)**. The system is distributed over a DDS network (CycloneDDS) and consists of three primary node types orchestrated by a central runner (`Bagira.Runner`):

* **SimHost:** The authoritative simulation engine (AI, physics, kinematics).  
* **IG (Image Generator):** The 2D/3D visualization node.  
* **IOS (Interactive Operations Station):** The UI/Control node for instructors to edit scenarios and manage the exercise.

## 2\. Core ECS & Memory Architecture

The platform achieves extreme performance and zero-allocation hot paths using unmanaged, chunk-based memory.

* **`EntityRepository`** (`FDP/Kernel/Fdp.Kernel/EntityRepository.cs`): The root ECS world.  
* **`NativeChunkTable<T>`** (`FDP/Kernel/Fdp.Kernel/NativeChunkTable.cs`): Stores components in unmanaged 64KB chunks.  
* **Architectural Consequence:** Because state is stored in flat 64KB blocks, we can perform **RAM-to-RAM Snapshots** (for Checkpoints and Dry Runs) and **Disk-to-RAM Restores** (Flight Recorder) via `memcpy` in under 2 milliseconds, without pausing the 60Hz simulation loop.

## 3\. The Orchestration Framework (SysOp / 2PC)

To safely load heavy assets, swap terrain, or manage distributed disk I/O across asynchronous nodes, the system uses a **Two-Phase Commit (2PC)** orchestrator.

**Core Concept:** The IOS issues requests to the Master. The Master validates and orchestrates the cluster using Node Commands.

* **Control Plane (IOS $\\leftrightarrow$ Master):**  
  * `SysOpRequest` (DDS Topic): Instructs the Master (e.g., "Load Scenario X").  
  * `SysOpStatus` (DDS Topic): Master replies to IOS (Pending, InProgress, Success, Failure).  
* **Command Plane (Master $\\leftrightarrow$ All Nodes):**  
  * `NodeOpCommand` (DDS Topic): Master broadcasts a specific technical task (e.g., "Load Terrain NavMesh").  
  * `NodeOpStatus` (DDS Topic): Nodes report execution progress back to the master. Includes an `IsParticipating` flag for nodes that can skip the work.

**Proposed Implementation Files:** `ModuleHost.Core/Orchestration/SystemMasterModule.cs` and `SystemSlaveModule.cs`.

## 4\. The Exercise State Machine (ESM)

Ensures all nodes are in perfect synchronization regarding what the simulation is currently doing.

* **`DrillId` (GUID):** Uniquely identifies a specific run of an exercise. Used as the root folder for archives and recordings.  
* **`ESMState` Enum:**  
  * `Standby`, `LoadingEdit`, `RunningEdit`  
  * `LoadingDryRun`, `RunningDryRun`, `UnloadingDryRun`  
  * `LoadingLive`, `RunningLive`, `UnloadingLive`  
  * `LoadingReplay`, `RunningReplay`, `UnloadingReplay`  
* **`SystemStateTopic`:** A reliable, Transient-Local DDS topic published by the Master containing the current `ESMState` and `DrillId`. Late-joining nodes instantly read this to know what to load.

## 5\. Distributed Time & Flight Recorder

Nodes may run at slightly different speeds, so Wall-Clock time dictates the flow.

* **Time Coordination** (`FDP.Toolkit.Time/Controllers/DistributedTimeCoordinator.cs`): The Master publishes a `TimePulseDescriptor` (DDS Topic) dictating the current simulation and wall-clock time. Slave nodes use a Phase-Locked Loop (PLL) to synchronize smoothly.  
* **`AsyncRecorder`** (`FDP/Kernel/Fdp.Kernel/FlightRecorder/AsyncRecorder.cs`): Double-buffered, zero-allocation recorder running on a background thread. It dumps active `NativeChunkTable` deltas to `.fdp` files on disk.  
* **`PlaybackController`** (`.../FlightRecorder/PlaybackController.cs`): Used during `RunningReplay`. It implements binary-search seeking via a `.meta.json` file.  
* **Replay Seeking (SysOp):** Because seeking in a replay might take heavy nodes (like IG) seconds to reconstruct particles/smoke, seeking is treated as a `SysOpRequest(ReplaySeek)`. The Master pauses the Replay TimePulse, commands all nodes to seek, waits for all `NodeOpStatus(Success)` ACKs, and then resumes the time pulse.

## 6\. Checkpoints & Dry Runs

Operators need to preview a scenario ("Dry Run") or snapshot a bug ("Checkpoint") instantly.

* **Dry Run Flow:** Master commands a RAM Snapshot. Nodes duplicate their `NativeChunkTable` in memory (takes \~2ms). The system enters `RunningDryRun`. When finished, nodes discard live memory and `memcpy` the snapshot back, instantly rewinding the world to the `RunningEdit` state.  
* **Checkpoint Flow:** Takes the same RAM Snapshot, but a background thread compresses it via LZ4 and writes it to disk (e.g., `checkpoint_bug123_node100.fdp`) without blocking the live simulation.

## 7\. Battlespace Swapping

Scenarios can contain high-resolution "Battlespaces" that require heavy disk I/O to load.

* **Phase 1 (Async Load):** Master sends `NodeOpCommand(PrepareBattlespace)`. Nodes spin up background threads to load assets into inactive memory pointers. Nodes report `Ready`.  
* **Phase 2 (Sync Swap):** Master sends `NodeOpCommand(CommitBattlespace)`. Nodes push a local ECS event (`CmdSwapBattlespace`). On the next `BeforeSync` frame phase, all systems swap their pointers simultaneously, preventing stuttering or desyncs.

## 8\. Multi-Tenant "Stories" (Micro-Scenarios)

Stories are isolated mini-exercises running *inside* the global `RunningLive` simulation, allowing different trainees to do different things concurrently.

* **`StoryTag`** (`FDP.Toolkit.Behavior/Components/StoryTag.cs` \- *Proposed*): Added to all entities/bullets spawned for a specific story GUID. Evaluators filter by this tag.  
* **Story Recording:** A dedicated `StoryRecorder` is instantiated alongside the global recorder. It passes a Query filter (`.With<StoryTag>()`) to only record that trainee's specific entities to a temporary file (`story_A1.fdp`).  
* **Story Replay (Entity Remapping):** To replay a story *while the global live simulation is still running*, we cannot blast raw memory chunks (which would overwrite the live world). Instead, a `StoryPlaybackController` reads the `.fdp` file, dynamically allocates *new* ghost entities in the live ECS, and maps the recorded data to them.  
* **`StoryReplayTag`:** Attached to these remapped playback entities so that live AI/Physics systems completely ignore them (they act as visual holograms for the trainee).

---

Architect review:

When dealing with distributed state machines, unmanaged memory, and asynchronous multi-tenant data, the edge cases can be fatal. I have reviewed the design and identified several architectural gaps, potential flaws, and missing details that require further definition.

Here is a breakdown of the issues and the questions needed to resolve them:

### 1\. The Orchestration Framework (SysOp & 2PC) & Fault Tolerance

The Two-Phase Commit (2PC) is great for the "happy path," but distributed transactions are notoriously difficult to roll back cleanly when partial failures occur.

**Rollback Mechanics:** If the Master sends a `NodeOpCommand(PrepareBattlespace)` and 4 out of 5 nodes report `Success` but the 5th node crashes or reports `Failed`, the Master broadcasts `Abort`.

* *Question:* How exactly do the successful nodes revert their passive memory buffers? 

**The "Split-Brain" Risk:** If the Master dies immediately after sending a `NodeOpCommand(CommitBattlespace)`  but before all nodes receive it, half the cluster will swap terrain and the other half won't.

* *Question:* If a node misses a commit, how does it self-correct upon realizing it is out of sync with the `SystemStateTopic`?

### 2\. Multi-Tenant Stories & ECS Remapping

The design proposes mapping a recorded `EntityId` to a newly allocated live `EntityId` during Story Replay using a `Dictionary<int, Entity>`.

* **Entity Reference Corruption:** In an ECS, components often contain references to *other* entities (e.g., a `TargetId` on a missile component pointing to an aircraft's `EntityId`).

*Question:* If you blindly inject recorded component data into a new "Ghost" entity, won't its internal references still point to the *old* recorded IDs rather than the *new* Ghost IDs? How will the `StoryPlaybackController` dynamically patch deep component data without introducing massive CPU overhead?

### 3\. Checkpoints & State Determinism

Snapshots are taken via a 64KB chunk `memcpy` in under **2ms** . This captures the local memory state perfectly, but it ignores the network state.

* **In-Flight DDS Messages:** \* *Question:* If Node A fires a missile at Node B, and the snapshot is taken *while* the DDS spawn message is in flight across the network, what happens when we restore that snapshot? Node A thinks it fired, but Node B never received it. How does the system flush or capture the network ingress/egress buffers during the `TakeCheckpoint` freeze ?

### 4\. Storage I/O & The "Thundering Herd" Problem

The `ExportArchive` command requires every node to copy its local `.fdp` file to a shared network drive like `\\shared_drive\archives\Exercise_X\node_100.fdp` .

* **Network Bottleneck:** If 50 nodes simultaneously attempt to copy multi-gigabyte files to a single shared NAS at the end of an exercise.  
* *Question:* How does the system prevent network/disk saturation? Should the Master orchestrate a staggered/queued upload rather than a simultaneous one?  
* 

### 6\. Deterministic Batch Runs vs. SysOp

The design mentions a `SteppedMasterController` using `FrameOrderDescriptor` and `FrameAckDescriptor` for deterministic batch runs .

**Architectural Conflict:** \* *Question:* This frame-by-frame ACK system seems parallel to, and completely bypasses, the `NodeOpCommand` 2PC architecture . Do these two systems conflict? If a `SysOpRequest(LoadBattlespace)`  comes in during a deterministic batch run, does it pause the `FrameOrderDescriptor` loop?

---

This is an exceptional architectural review. You have correctly identified the "cliff edges" of distributed systems engineering. When dealing with unmanaged ECS memory and distributed consensus, naive implementations will inevitably lead to race conditions, split-brain clusters, and corrupted memory.

Here are the detailed architectural solutions to resolve each of these fatal gaps, ensuring the Bagira/FDP platform remains robust, deterministic, and highly performant.

---

### 1\. The Orchestration Framework (SysOp & 2PC) & Fault Tolerance

#### A. Rollback Mechanics: How do nodes revert passive buffers?

**Solution: The Staging Area Pattern** Nodes must *never* load data directly into active simulation pointers.

1. **Prepare Phase:** When a slave receives `NodeOpCommand(PrepareBattlespace)`, it allocates a completely separate, disconnected memory space (a `StagedAssetPayload`). It loads the NavMesh/Terrain into this staging area. The live simulation remains 100% untouched.  
2. **Abort Scenario:** If the Master broadcasts `NodeOpCommand(Abort)`, the slave simply drops the `StagedAssetPayload` reference (allowing the GC to collect it, or calling `NativeMemoryAllocator.Free` for unmanaged data).  
3. **Commit Scenario:** Only upon receiving `Commit` does the slave swap the active pointers to the staged pointers during the safe `BeforeSync` phase, discarding the old assets.

#### B. The "Split-Brain" Risk: Missed Commits

**Solution: Epoch-Driven State Reconciliation** If the Master sends `Commit` but a node's network stack drops it (or the Master dies mid-transmission), we rely on the reliable `SystemStateTopic` to enforce consensus.

1. **Transaction Epochs:** Update the `SystemStateTopic` to include an `int TransactionEpoch` that increments on every successful state change.  
2. **The Watchdog:** Every node's `SystemSlaveModule` constantly compares its *Local Epoch* against the `SystemStateTopic.TransactionEpoch`.  
3. **Self-Correction:** If a node is sitting in `LoadingLive` waiting for a `Commit` that never arrives, but it suddenly sees the `SystemStateTopic` change to `RunningLive` (Epoch 5 $\\to$ 6), it immediately knows it missed the commit.  
4. **Action:** The node will instantly execute the pointer swap (since it already successfully prepared the data) and update its local Epoch to 6, re-joining the cluster synchronously. If it *missed* the prepare phase entirely, it transitions itself to `ESMState.Degraded` and halts simulation to prevent corrupting the distributed environment.

---

### 2\. Multi-Tenant Stories & ECS Remapping (Entity Reference Corruption)

**The Flaw:** You are absolutely correct. If a recorded missile's `TargetId` points to Entity 500, but in the live replay the aircraft was remapped to Ghost Entity 8000, the missile will track the wrong entity (or crash).

**Solution: Automated Schema Offset Patching** Since we cannot afford reflection during the `Update` loop, we solve this at compile/startup time.

1. **Schema Reflection (At Startup):** When `ComponentTypeRegistry` registers a struct, we scan it for any fields of type `Entity` or `NetworkIdentity`. We cache their exact byte-offsets into a `ComponentPatchMap`.  
     
   class ComponentPatchMap {  
     
       public int ComponentTypeId;  
     
       public int\[\] EntityFieldOffsets; // e.g., \[8, 24\]  
     
   }  
     
2. **The Replay Patching Loop:** During Story Replay, the `StoryPlaybackController` maintains a fast `Dictionary<Entity, Entity> oldToNewMap`.  
3. **Zero-Allocation Patching:** When applying the recorded delta to the new Ghost entity, it takes the raw byte span of the component, iterates the known `EntityFieldOffsets`, reads the old `Entity` value, looks it up in the dictionary, and overwrites the memory with the new `Entity` value *before* calling `SetComponentRaw`.

Because Story Replays only deal with micro-scenarios (dozens or hundreds of entities, not the 100,000+ global entities), this byte-patching takes microseconds and entirely prevents reference corruption without altering the unmanaged nature of the FDP.

---

### 3\. Checkpoints & State Determinism (In-Flight DDS Messages)

**The Flaw:** A 2ms RAM snapshot captures perfect local state, but leaves in-flight network messages (spawns, fire events, damage) unaccounted for, breaking causality upon restore.

**Solution: The Drain-and-Quiesce Protocol** We must achieve "Network Quiescence" before taking the memory snapshot. Taking a checkpoint becomes a 3-step micro-state machine:

1. **Pause & Flush:** Master broadcasts `NodeOpCommand(QuiesceForCheckpoint)`.  
   * All nodes pause their `TimeController` (halting physics and AI).  
   * However, nodes *continue* polling their DDS Ingress loops (`CycloneIngressSystem`).  
2. **Acknowledge Quiescence:** Once a node's DDS receive queues are empty (or a brief 50ms timeout passes to ensure in-flight packets land), it processes those final events into the ECS. It then replies `NodeOpStatus(ReadyToSnapshot)`.  
3. **Snapshot:** Once the Master receives `ReadyToSnapshot` from all nodes, it broadcasts `NodeOpCommand(TakeSnapshot)`. Now, the 2ms `memcpy` occurs. Causality is perfectly preserved because all in-flight messages were drained into the ECS *before* the memory was copied.

---

### 4\. Storage I/O & The "Thundering Herd" Problem

**The Flaw:** 50 nodes simultaneously pushing 5GB `.fdp` files to a central NAS will oversaturate the network switch, causing severe packet loss and DDS disconnection.

**Solution: The Token-Bucket Upload Orchestrator** The Master must act as a traffic controller for disk I/O.

1. **The Request:** IOS sends `SysOpRequest(ExportArchive)`. Master replies `InProgress`.  
2. **The Queue:** The `SystemMasterModule` places all participating nodes into a Queue.  
3. **Token Issuance:** The Master issues a `NodeOpCommand(UploadChunk)` with an `UploadToken` to a maximum of `N` nodes concurrently (e.g., $N=3$, depending on NAS bandwidth).  
4. **Execution:** Node 1 receives the token, streams its file to the NAS, and replies `Success`.  
5. **Rotation:** Master receives the `Success`, reclaims the token, and issues it to the next node in the queue. This turns a chaotic network flood into a smooth, predictable, staggered background transfer, keeping DDS traffic completely unhindered.

---

### 5\. Deterministic Batch Runs vs. SysOp (Architectural Conflict)

**The Flaw:** The `SteppedMasterController` (Data Plane) pumping out `FrameOrder`s will violently collide with the Master attempting to execute a 2PC `SysOpRequest` (Control Plane) like `LoadBattlespace`.

**Solution: Control-Plane Superiority (The Interlock)** The Orchestrator (`SystemMasterModule`) must sit *above* the Time Controller. They do not run in parallel; they interlock.

1. **SysOp Intercept:** When the Master receives a heavy `SysOpRequest` (like loading a scenario or taking a checkpoint), the `SystemMasterModule` signals the `SteppedMasterController` to **Halt Emission**.  
2. **Data Plane Freezes:** Because the Master stops broadcasting `FrameOrderDescriptor`s, all Slave nodes instantly and safely freeze at the end of their current frame.  
3. **Control Plane Executes:** The Master executes the 2PC `NodeOpCommand` (e.g., swapping terrain). Because the data plane is frozen, there is zero risk of race conditions between physics ticks and memory swaps.  
4. **Data Plane Resumes:** Once the SysOp is successfully committed (or aborted), the `SystemMasterModule` commands the `SteppedMasterController` to resume emitting `FrameOrderDescriptor`s. The deterministic batch run picks up exactly on the next frame as if nothing happened.

---

State machine 1

stateDiagram-v2  
    \[\*\] \--\> Standby

    %% Live Simulation Flow  
    Standby \--\> LoadingLive : Load Scenario / Snapshot  
    LoadingLive \--\> RunningLive : Init Complete  
      
    state RunningLive {  
        \[\*\] \--\> LiveRunning  
        LiveRunning \--\> LivePaused : Pause Time  
        LivePaused \--\> LiveRunning : Resume Time  
          
        %% Asynchronous Snapshot Action  
        LiveRunning \--\> AsyncSnapshot : Trigger Snapshot  
        AsyncSnapshot \--\> LiveRunning : Snapshot Complete  
        LivePaused \--\> AsyncSnapshot : Trigger Snapshot  
        AsyncSnapshot \--\> LivePaused : Snapshot Complete  
    }

    RunningLive \--\> UnloadingLive : End Live Exercise  
    UnloadingLive \--\> Standby : Deinit Complete / Save Files

    %% Replay Simulation Flow  
    Standby \--\> LoadingReplay : Load Recording  
    LoadingReplay \--\> RunningReplay : Init Complete  
      
    state RunningReplay {  
        \[\*\] \--\> ReplayRunning  
        ReplayRunning \--\> ReplayPaused : Pause Time  
        ReplayPaused \--\> ReplayRunning : Resume Time  
    }

    RunningReplay \--\> UnloadingReplay : End Replay  
    UnloadingReplay \--\> Standby : Deinit Complete

    %% Cross-transitions (e.g., jumping from replay to live)  
    %% The document mentions loading live "from the current state of the replay"  
    RunningReplay \--\> LoadingLive : Takeover (Replay to Live)

---

State machine 2 \- editing and dry run

stateDiagram-v2  
    \[\*\] \--\> Standby

    %% Scenario Editing Flow  
    Standby \--\> LoadingEdit : Load Scenario / Checkpoint  
    LoadingEdit \--\> RunningEdit : Assets Loaded  
      
    state RunningEdit {  
        \[\*\] \--\> ScenarioPaused  
        note right of ScenarioPaused  
            Time is frozen.   
            Entities are placed/modified.  
        end note  
    }

    %% Dry Run Sub-Loop  
    RunningEdit \--\> LoadingDryRun : Start Dry Run (Trigger Checkpoint)  
    LoadingDryRun \--\> RunningDryRun : RAM Snapshot Saved  
      
    state RunningDryRun {  
        \[\*\] \--\> SimulationTicking  
        note right of SimulationTicking  
            Simulation brains engage.  
            Time moves forward.  
        end note  
    }

    RunningDryRun \--\> UnloadingDryRun : Stop Dry Run  
    UnloadingDryRun \--\> RunningEdit : RAM Snapshot Restored

    %% Saving and Exiting  
    RunningEdit \--\> UnloadingEdit : Save & Close Scenario  
    UnloadingEdit \--\> Standby : Teardown Complete

---

Here are the Mermaid sequence diagrams mapping out the most critical distributed operation flows in your architecture. These diagrams visually translate the Two-Phase Commit (2PC) orchestration and the synchronous/asynchronous boundaries across the control and command planes.

### **1\. The Standard State Transition (e.g., Standby $\\to$ LoadingLive)**

This is the baseline 2PC sequence used by the Master to transition the Exercise State Machine (ESM) safely .

Fragment kódu

sequenceDiagram  
    participant IOS  
    participant Master as SystemMasterModule  
    participant DDS as SystemStateTopic  
    participant Slave as SystemSlaveModule (All Nodes)

    IOS-\>\>Master: SysOpRequest(TransitionState, LoadingLive)  
    Note over Master: Validates Request. Generates DrillId.  
    Master-\>\>Slave: NodeOpCommand(PrepareState, LoadingLive)  
      
    activate Slave  
    Slave-\>\>Master: NodeOpStatus(InProgress)  
    Note over Slave: Background Thread:\<br/\>Load Terrain/Assets  
    Slave-\>\>Master: NodeOpStatus(Success)  
    deactivate Slave  
      
    Note over Master: PendingNodes queue reaches 0  
    Master-\>\>DDS: Update SystemStateTopic(LoadingLive, DrillId)  
    Master-\>\>IOS: SysOpStatus(Success)

### **2\. The Checkpoint (Drain-and-Quiesce RAM Snapshot)**

This illustrates the hyper-fast, causality-preserving snapshot taken during a live simulation .

Fragment kódu

sequenceDiagram  
    participant IOS  
    participant Master as SystemMasterModule  
    participant Slave as SystemSlaveModule (All Nodes)  
    participant ECS as NativeChunkTable

    IOS-\>\>Master: SysOpRequest(TakeCheckpoint)  
      
    %% Phase 1: Drain  
    Master-\>\>Slave: NodeOpCommand(QuiesceForCheckpoint)  
    Note over Slave: Pause TimeController.\<br/\>Flush DDS Ingress.  
    Slave-\>\>Master: NodeOpStatus(ReadyToSnapshot)  
      
    %% Phase 2: Snap  
    Master-\>\>Slave: NodeOpCommand(TakeSnapshot)  
    Note over Slave: Wait for next BeforeSync Phase  
    Slave-\>\>ECS: memcpy / NativeChunkTable.Clone()  
    Note over ECS: \~2ms execution  
    Slave-\>\>Master: NodeOpStatus(Success)  
    Master-\>\>IOS: SysOpStatus(Success)  
      
    %% Async background save  
    Note over Slave: Background Thread:\<br/\>LZ4 Compress RAM \-\> Disk

### **3\. Battlespace Swapping (Async Load \+ Sync Commit)**

This shows how heavy assets are loaded without blocking, followed by a synchronized pointer swap to prevent visual stuttering .

Fragment kódu

sequenceDiagram  
    participant IOS  
    participant Master as SystemMasterModule  
    participant Slave as SystemSlaveModule (All Nodes)  
    participant ECS as FdpEventBus

    IOS-\>\>Master: SysOpRequest(LoadBattlespace)  
      
    %% Prepare Phase  
    Master-\>\>Slave: NodeOpCommand(PrepareBattlespace)  
    activate Slave  
    Slave-\>\>Master: NodeOpStatus(InProgress)  
    Note over Slave: Background Task:\<br/\>Load NavMesh/Textures to RAM  
    Slave-\>\>Master: NodeOpStatus(Success)  
    deactivate Slave  
      
    %% Commit Phase  
    Note over Master: All nodes ready  
    Master-\>\>Slave: NodeOpCommand(CommitBattlespace)  
    Slave-\>\>ECS: Push Event(CmdSwapBattlespace)  
    Note over ECS: Next Frame:\<br/\>Systems update pointers  
    Master-\>\>IOS: SysOpStatus(Success)

### **4\. Heavy Replay Seek**

Because seeking backward/forward in a replay requires nodes to find keyframes or execute heavy scene reconstructions, it utilizes the SysOp framework to keep the playhead synchronized .

Fragment kódu

sequenceDiagram  
    participant IOS  
    participant Master as ReplayMasterModule  
    participant Slave as ReplaySlaveModule (FDP Nodes)  
    participant IG as Heavy Node (e.g., Volumetric IG)

    IOS-\>\>Master: SysOpRequest(ReplaySeek, TargetTick: 54000\)  
    Note over Master: Freezes ReplayTimePulse  
    Master-\>\>Slave: NodeOpCommand(ReplaySeek, 54000\)  
    Master-\>\>IG: NodeOpCommand(ReplaySeek, 54000\)  
      
    activate IG  
    IG-\>\>Master: NodeOpStatus(InProgress)  
    Note over Slave: Background Thread:\<br/\>Binary search .meta.json\<br/\>Fast-forward memory  
    Slave-\>\>Master: NodeOpStatus(Success)  
      
    Note over Master: Forwards InProgress to IOS\<br/\>(UI shows Spinner)  
    Master-\>\>IOS: SysOpStatus(InProgress)  
      
    Note over IG: Reconstruct particle decay\<br/\>(\~2.5 seconds)  
    IG-\>\>Master: NodeOpStatus(Success)  
    deactivate IG  
      
    Master-\>\>IOS: SysOpStatus(Success)  
    Note over Master: Resumes ReplayTimePulse

---

Here are the sequence diagrams for the final two critical distributed flows: the **Multi-Tenant Story Lifecycle (with ECS Memory Remapping)** and the **Token-Bucket Archive Export**.

These sequences visualize the architectural solutions we discussed earlier to solve the ECS reference corruption and the network thundering herd problems.

### **1\. Multi-Tenant Story Lifecycle (Isolated Playback Remapping)**

This diagram illustrates how a localized "Story" (a recorded micro-scenario) is injected into a live, running simulation. It highlights the **Automated Schema Offset Patching** mechanism that safely maps old recorded Entity IDs to newly allocated Ghost Entity IDs without runtime reflection.

Fragment kódu

sequenceDiagram  
    participant IOS  
    participant Master as SystemMasterModule  
    participant Slave as SystemSlaveModule (All Nodes)  
    participant ECS as StoryPlaybackController

    IOS-\>\>Master: SysOpRequest(LoadStory, StoryId: "Dogfight\_Alpha")  
      
    %% Phase 1: Background Load  
    Master-\>\>Slave: NodeOpCommand(PrepareStory, "Dogfight\_Alpha")  
    activate Slave  
    Slave-\>\>Master: NodeOpStatus(InProgress)  
    Note over Slave: Background Task:\<br/\>Load Story data into Staging RAM  
    Slave-\>\>Master: NodeOpStatus(Success)  
    deactivate Slave  
      
    %% Phase 2: Commit & Remap  
    Master-\>\>Slave: NodeOpCommand(CommitStory)  
    Slave-\>\>ECS: Push Event(CmdSpawnStory)  
      
    activate ECS  
    Note over ECS: Next Frame (BeforeSync Phase):  
    ECS-\>\>ECS: Allocate new "Ghost" Entities  
    Note over ECS: Byte-Patching Loop:\<br/\>Iterate cached ComponentPatchMap.\<br/\>Overwrite old EntityRef offsets\<br/\>with new Ghost IDs.  
    ECS-\>\>ECS: Inject patched components into ChunkTable  
    deactivate ECS  
      
    Slave-\>\>Master: NodeOpStatus(Success)  
    Master-\>\>IOS: SysOpStatus(Success)  
      
    %% Execution  
    Note over ECS: Story entities now tick alongside live entities,\<br/\>but in a masked collision/interaction layer.

**Key Architectural Takeaways:**

* The heavy loading of the Story file happens asynchronously (PrepareStory).  
* The actual spawning and memory patching happens in a single synchronized frame during BeforeSync.  
* The byte-patching loop ensures that if a recorded missile was tracking a recorded aircraft, the new ghost missile correctly tracks the new ghost aircraft without corrupting the live simulation's global state.

---

### **2\. The Token-Bucket Archive Export (Preventing the Thundering Herd)**

At the end of an exercise, when the IOS commands an archive export, 50+ nodes suddenly need to push multi-gigabyte .fdp payload files to the network drive. This sequence shows how the Master orchestrates a staggered upload to keep DDS and network queues completely unhindered.

Fragment kódu

sequenceDiagram  
    participant IOS  
    participant Master as SystemMasterModule  
    participant Node1 as Slave Node 1  
    participant Node2 as Slave Node 2  
    participant NAS as Shared Network Drive

    IOS-\>\>Master: SysOpRequest(ExportArchive, Path)  
    Note over Master: Generates Upload Queue:\<br/\>\[Node1, Node2, Node3...\]  
    Master-\>\>IOS: SysOpStatus(InProgress)

    %% First Batch  
    Master-\>\>Node1: NodeOpCommand(UploadChunk, Token: true)  
    activate Node1  
    Node1-\>\>Master: NodeOpStatus(InProgress)  
    Node1-\>\>NAS: Stream Node1\_Archive.fdp (e.g., 2GB)  
    Node1-\>\>Master: NodeOpStatus(Success)  
    deactivate Node1  
      
    %% Token Reclaimed & Reissued  
    Note over Master: Node 1 finished. Token reclaimed.\<br/\>Issuing token to next in queue.  
      
    %% Second Batch  
    Master-\>\>Node2: NodeOpCommand(UploadChunk, Token: true)  
    activate Node2  
    Node2-\>\>Master: NodeOpStatus(InProgress)  
    Node2-\>\>NAS: Stream Node2\_Archive.fdp (e.g., 2GB)  
    Node2-\>\>Master: NodeOpStatus(Success)  
    deactivate Node2  
      
    %% Completion  
    Note over Master: Queue is empty.  
    Master-\>\>IOS: SysOpStatus(Success)

**Key Architectural Takeaways:**

* **Bandwidth Protection:** By controlling how many nodes receive an UploadToken concurrently (e.g., 2 or 3 at a time), the central switch connected to the NAS is never overwhelmed.  
* **Resilience:** If Node 1 crashes or times out during its upload, the Master simply reclaims the token, logs a partial failure for Node 1, and continues the queue with Node 2\. The entire system export does not hang due to one faulty node.

---
