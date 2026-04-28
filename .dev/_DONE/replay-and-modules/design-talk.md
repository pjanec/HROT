
How the exercise playback works now?

The exercise playback architecture is designed to support high-performance state restoration, random-access seeking, and seamless transitions into live simulations. Here is how the playback system works internally:

**1\. Initialization and Schema Validation** When a replay is initiated, the `EcsRecordReplayController` installs a `ReplayModule`. Before opening the binary stream, the `PlaybackController` loads a companion `.meta.json` file to validate the component schema. This ensures that if the struct layouts have changed since the recording was made, it fails gracefully rather than causing silent memory corruption.

**2\. Indexing and File Parsing** The `PlaybackController` reads the binary `.fdprec` file and builds a `_frameIndex` in memory. To avoid repeated list growth allocations, it pre-sizes this index based on the overall file length (estimating average frame size). This index maps each frame to its byte position in the file and records whether it is a **Keyframe** or a **Delta frame**.

**3\. Execution Strategies (Strategy A vs. Strategy B)** During playback, the `PlaybackTickSystem` drives the timeline forward every frame. Depending on how many frames need to be processed (e.g., normal 1x speed vs. fast-forwarding or seeking), it intelligently chooses between two strategies:

-   **Strategy A (Sequential Stepping):** If the gap is small (3 frames or fewer), it sequentially applies the delta frames in memory without rendering the intermediate steps.-   **Strategy B (Keyframe Seeking):** For large jumps, fast-forwarding, or rewinding, it performs an O(log N) binary search on the `_frameIndex` to find the nearest preceding Keyframe. It seeks directly to that position, applies the Keyframe, and then rapidly simulates the remaining delta frames up to the exact target frame.

**4\. ECS State Restoration** The actual application of data to the simulation world is handled by the `PlaybackSystem`:

-   **Keyframes:** These trigger a full state reset (`repo.Clear()`) followed by a complete restoration of all active entities and components.-   **Deltas:** These frames only contain the changes. The system reads the LZ4-decompressed binary data, destroys any entities marked for deletion, restores events to the bus, and applies chunk data updates.-   **Memory Management:** Unmanaged components are restored using highly optimized raw memory block copies directly into chunk data, while managed components are handled via serializers.

**5\. Live-from-Replay Branching** Because the playback directly manipulates the `EntityRepository` rather than just drawing visuals on a screen, the system supports a "Live-from-Replay" feature. When the orchestrator decides to stop the replay, it uninstalls the `ReplayModule` and closes the file handles, but it intentionally leaves the historical ECS state intact within the repository. This allows the application to seamlessly unpause and branch into a new live simulation starting from that exact historical moment.

During playback now all the live systems are still active, correct?

No, that is incorrect. During playback, the engine explicitly disables key live systems to prevent their logic from interfering with the recorded historical data.

When a replay session is initiated, the orchestrator intentionally disables the following components:

-   **SimulationSystemGroup**: Its execution flag is cleared so that no active simulation logic (like physics or AI) runs on top of the replayed ECS state.-   **NetworkLifecycleSystemGroup**: This group is disabled and a bypass toggle is activated to suspend ghost-creation. This ensures that no new network entities are promoted and no unintended entity lifecycle state changes occur while the recording is driving the data.

These system groups remain dormant throughout the entire playback phase. They are only re-enabled when the replay session is finalized, or when the engine executes a transition into a **"Live-from-Replay" branch**. Once that branch is initiated, the simulation and lifecycle systems are toggled back on, allowing live execution to instantly resume from that exact historical moment.

What does the simulation group include?

The **SimulationSystemGroup** is the system group responsible for executing systems during the main simulation logic phase. Depending on the node's specific role (e.g., "Brain" or "Muscle") and how the application is configured, this group encompasses the core modules that drive the simulation's active behavior.

Specifically, the simulation group typically includes the following subsystems:

-   **Mission Control and Cognitive AI (Brain Tier):** Systems that manage entity AI states, mission planning, and decision-making. This includes the `MissionDirectorSystem`, `TrafficBrainSystem`, `ChannelArbitrationSystem`, `BTreeTickSystem`, and `HsmTickSystem`.-   **Combat, Damage, and Perception:** Systems that evaluate events and manage sensory awareness. This includes damage assessment (`DamageSystem`, `DamageCalculationSystem`, `HsmDamageBridgeSystem`), auditory tracking (`AudioPerceptionSystem`), and batching line-of-sight requests (`LosRequestBatchingSystem`).-   **Action Dispatchers:** Systems that route specific intents from the AI down to the execution layers, such as the `WeaponDispatcherSystem`, `InteractionDispatcherSystem`, and `LocomotionDispatcherSystem`.-   **Ground Kinematics and Navigation (Muscle Tier):** Systems that control spatial management, vehicle physics, and movement execution. This includes the `SpatialHashSystem`, `CarKinematicsSystem`, `FormationTargetSystem`, `VehicleCommandSystem`, `NavigationExecutionSystem`, and `LinearKinematicsSystem`. It also integrates bridging systems like the `NavigationIntentBridgeSystem` and `RouteTrajectorySyncSystem`.

As discussed regarding exercise playback, because these systems actively calculate and mutate state (like moving vehicles or making AI decisions), the entire **SimulationSystemGroup** is disabled during a replay to prevent this live logic from overriding the recorded historical data.

What other groups exist?

In addition to the **SimulationSystemGroup** and the **NetworkLifecycleSystemGroup** we previously discussed, the FDP engine defines several other standard system groups that execute in a strict pipeline.

The standard execution order for these groups is **Input → Simulation → PostSimulation → Presentation → Export**.

Here is a breakdown of the other groups and their roles:

-   **InitializationSystemGroup**: Runs at the very beginning of the frame during the initialization phase.-   **InputSystemGroup**: Responsible for processing inputs before the main simulation begins. This includes receiving network events, buffering commands, and handling doctrine/AI ingress. It is strictly ordered to run before the Simulation group so that any AI intents or state changes take effect in the same frame.-   **PostSimulationSystemGroup**: Executes immediately after the main simulation logic. This phase is typically used for resolving outcomes from the simulation step, such as integrating linear kinematics (moving entities), ballistics housekeeping, updating vehicle kinematics, and rebuilding the spatial hash grid.-   **PresentationSystemGroup**: Handles systems that run during the rendering and visual presentation phase.-   **ExportSystemGroup**: The final phase of the frame, running after all simulation and presentation logic has completed. This group is reserved for read-only observers that need to capture the fully committed frame state without mutating it, such as logging, telemetry reporters, and the flight recorder (the system responsible for creating the replays).

What all systems belong to those non simulation groups?

Based on the engine's phase attributes and group registrations, here is a breakdown of the specific systems that execute within the non-simulation pipeline groups:

**Input Phase / InputSystemGroup** This group handles input, network ingress, and early data preparation before the main simulation begins:

-   **DoctrineIngressSystem**: Parses blackboard parameters and applies doctrine transitions to entities so that the AI systems see the new behavior within the same frame.-   **FireProcessingSystem**: Consumes weapon fire intents, spawns ballistic bullet entities with velocity, and registers them into the physics pipeline.-   **RaycastSolverSystem**: Resolves pending physics raycasts using broad-phase spatial hash queries and narrow-phase geometric checks.-   **HitResolutionSystem**: Evaluates the resolved raycast batch and emits hit events and detonation notifications.-   **CycloneIngressSystem** and **OwnershipIngressSystem**: Poll all registered DDS network translators for incoming network data and apply incoming authority/ownership handovers.-   **DeleteEntityRequestSystem**: Processes requests to delete entities originating from the network.-   **TerrainQuerySubmitSystem** & **TerrainQueryInitializationSystem**: Submits terrain clamping queries by forward-predicting vehicle positions based on their velocity.-   **GeodeticSmoothingSystem**: Ingests and smooths geodetic positions for ghost entities.

**Post-Simulation Phase / PostSimulationSystemGroup** This group executes after the main simulation logic to resolve physics outcomes, synchronize rendering states, and clean up lifecycles:

-   **BallisticsSystem**: Submits swept-segment Continuous Collision Detection (CCD) raycasts for bullets exactly before they move, preventing them from tunneling through targets.-   **DeadReckoningSyncSystem**: Projects network positions forward using network velocity to smoothly blend the render transforms of remote ghost entities.-   **CoordinateTransformSystem**: Converts local Cartesian coordinates back into geodetic representations.-   **TerrainQueryResolutionSystem**: Reconciles the output of terrain queries to apply a smooth Z-axis altitude correction to entities.-   **Visual and Presentation States**:
    -   **MapCullingSystem**: Evaluates camera-frustum visibility and sets level-of-detail for rendering.-   **MapLayerAssignmentSystem**: Evaluates predicates to assign visibility layer bitmasks to entities.-   **StyleResolutionSystem**: Executes a 3-layer merge of base definitions, network overrides, and user configurations to resolve final entity rendering styles.-   **EventToEffectSystem** & **VisualEffectCleanupSystem**: Spawn and clean up ephemeral visual effect entities, such as explosion rings and weapon tracers.-   **ContextMenuSystem**: Keeps interactive UI context menu states synchronized with operator input.-   **Lifecycle Management**:
    -   **EntityRequestFinalizationSystem**: Monitors in-flight entity creation or deletion requests and sends Phase-2 network acknowledgments once the ECS lifecycle completes.-   **SubEntityCleanupSystem**: Cascades destruction signals down to child entities.-   **GhostDestructionSystem**: Purges local network ghosts when remote owners issue a network dispose command.-   **DisposalMonitoringSystem**: Prunes dead entities from the internal network entity map.-   **Recording and Replay**: **RecorderTickSystem** and **PlaybackTickSystem** run here to ensure the entity state is fully settled before capturing LZ4 snapshots or advancing playback frames.

**Presentation Phase / PresentationSystemGroup** This group is dedicated to visual output:

-   **SimMapRenderSystem**: Renders the 2-D tactical map canvas and its visual overlays, running exclusively when the active perspective is set to "Sim".

**Export Phase / ExportSystemGroup** This group executes read-only tasks to broadcast the committed frame state to outside observers:

-   **TelemetryReporterSystem**: Emits structured log lines when significant simulation events happen, such as entity hits, kills, or units fleeing.-   **CycloneEgressSystem**: Publishes all locally-owned ECS components/descriptors over the DDS network.-   **SmartEgressSystem**: Manages intelligent publishing to the network by triggering updates based on component dirty flags or periodic salted heartbeats.-   **OwnershipEgressSystem**: Publishes node authority and ownership changes to the rest of the cluster.-   **CycloneNetworkCleanupSystem**: Tracks new entities and registers them to ensure their lifecycle status is synchronized with the network.

_(Note: While the pipeline defines an_ **InitializationSystemGroup** _at the start of the frame__, most standard ECS engine setup logic is executed via explicit hooks before the groups are ticked, so no specific systems are mapped to the initialization group in the provided architecture)._

Do we records the intent components?

Whether intent components and events are recorded by the Flight Recorder depends entirely on their assigned `[DataPolicy]` attribute. The engine categorizes them into components that **are recorded** to preserve runtime state, and components that **are excluded** to keep recordings clean.

Here is how the different types of intent components are handled:

**Recorded (Included in Flight Recorder and Checkpoints):**

-   **AI Action Channels (Execution Intents):** Components like `LocomotionChannel`, `WeaponChannel`, and `InteractionChannel` act as intents from the AI Brain down to the execution layers. These are marked with `[DataPolicy(DataPolicy.NoSave)]`. This policy intentionally excludes them from declarative Scenario JSON saves, but explicitly preserves them in binary checkpoints and recordings so the runtime execution state is fully captured.-   **CQRS / Navigation Intents:** Components like `NavigationIntent` are standard unmanaged structs that carry the Brain's navigation orders. Because they do not specify a data policy, they fall under `DataPolicy.Default` and are naturally included in snapshots, recordings, and saves.

**Not Recorded (Excluded from Flight Recorder):**

-   **Orchestration and Cluster Intents:** Events such as `ExecuteNodeOpIntent`, `ClusterOpIntent`, `TransitionStateIntent`, and `TakeCheckpointIntent` are used for driving cluster operations and node state transitions. These are explicitly marked with `[DataPolicy(DataPolicy.NoRecord)]` to ensure these infrastructure events do not pollute the simulation's binary state snapshots.-   **Genesis/Scenario Intents:** Components like `InitialPassengersIntent`, `InitialVehicleIntent`, and `InitialTargetsIntent` are temporary Managed DTOs used to build entity relationships during scenario load. These are marked with `[DataPolicy(DataPolicy.Transient)]`, which completely excludes them from background snapshots, recordings, and saves.

And we should just make sure during replay there are no intent processing systems running.

You are absolutely correct to point this out, as this highlights a critical architectural gap in the current implementation.

Based on how the engine's execution pipeline is currently structured, intent processing is split across two different phases, and only one of them is protected during replay:

**1\. AI Action Intents (Currently Safe)** The systems that process AI execution intents—such as the `LocomotionDispatcherSystem`, `WeaponDispatcherSystem`, and `InteractionDispatcherSystem`—execute within the **SimulationSystemGroup**. Because the `ReferenceReplayLoadHandler` explicitly disables the `SimulationSystemGroup` during playback, these intent processors are safely suspended and will not act on the recorded channel data.

**2\. Input & Command Intents (Currently Vulnerable)** However, higher-level intent processors execute earlier in the frame within the **InputSystemGroup**. This includes:

-   **FireProcessingSystem**: Consumes `WeaponFireIntent` events to physically spawn new bullet entities into the world.-   **DoctrineIngressSystem**: Consumes `AssignDoctrineEvent` intents to mutate entity doctrines and reset Brain states.-   **CycloneNetworkIngressSystem**: Polls the network for incoming live intents.

Currently, the `ReferenceReplayLoadHandler` only toggles off the `SimulationSystemGroup` and the `NetworkLifecycleSystemGroup`. It does **not** disable the `InputSystemGroup`.

Because of this, your observation is spot on: if the `InputSystemGroup` is left running during a replay, systems like the `FireProcessingSystem` could consume a replayed `WeaponFireIntent` event and execute its logic again, spawning a duplicate bullet on top of the one already being restored from the historical recording. Furthermore, live network intents could bleed into the historical state.

To ensure pristine playback, the orchestrator must indeed be updated to disable these input-phase intent processing systems so that no new state is generated during a replay.

What everything belong to input group? Maybe something needs to be kept running in playback?

Based on the engine's architecture, the **InputSystemGroup** (and systems assigned to `SystemPhase.Input`) is heavily populated with systems designed to ingest live external stimuli and prepare data for the main simulation.

Here is the comprehensive list of everything that belongs to the input group:

**1\. AI & Operator Intent Ingress**

-   **DoctrineIngressSystem**: Consumes `AssignDoctrineEvent` messages to update AI blackboards and doctrine states.-   **FireProcessingSystem**: Consumes `WeaponFireIntent` events to spawn new bullet/projectile entities into the world.-   **PersonalRouteAuthoringSystem**: Consumes operator UI clicks (`CmdAppendPersonalWaypoint`) to add waypoints to a vehicle's route plan.

**2\. Network & Lifecycle Ingress**

-   **CycloneNetworkIngressSystem** **/** **CycloneIngressSystem**: Polls the CycloneDDS network to ingest remote descriptors and state updates.-   **OwnershipIngressSystem**: Processes authority handover messages from the network.-   **DeleteEntityRequestSystem**: Processes remote entity deletion commands.-   **CreateEntityRequestSystem**: Drains entity creation requests and emits spawn commands for the network spawning system.

**3\. Physics & Geographic Queries**

-   **RaycastSolverSystem**: Executes 2D segment-circle sweeps for physics raycasts.-   **HitResolutionSystem**: Iterates resolved raycasts and emits `HitEvent` (for bullets) or `TargetVisibleEvent` (for line-of-sight).-   **TerrainQueryInitializationSystem** **&** **TerrainQuerySubmitSystem**: Resets the terrain query batch and submits forward-predicted positions to clamp vehicles to the ground.

Do any of these need to be kept running during playback?

Looking at the responsibilities of these systems, **it is highly likely that the** **entire** **InputSystemGroup** **should be disabled during playback.**

Because the Flight Recorder directly restores the exact historical ECS chunk data frame-by-frame, the simulation does not need to recalculate any of this input logic:

-   **Physics/Terrain:** You do not need `RaycastSolverSystem` or `HitResolutionSystem` to run because the historical recording already contains the exact moments entities took damage or spotted targets. You do not need the terrain queries because the recorded `SimTransform` already contains the correctly clamped Z-altitude.-   **Network:** You absolutely do not want `CycloneIngressSystem` running, as it would pull live DDS data into the historical replay, corrupting the timeline.-   **Operator Intents:** You do not want `FireProcessingSystem` or `PersonalRouteAuthoringSystem` acting on stray operator clicks to spawn new bullets or waypoints on top of the replay.

Just like the `SimulationSystemGroup`, the `InputSystemGroup` represents "live" state generation. Disabling the entire group during a replay is the safest architectural approach to hermetically seal the historical state, ensuring it remains pristine until the operator intentionally transitions into a "Live-from-Replay" branch.

Does flight recordrr record just owned component?

No, the Flight Recorder does **not** restrict its recording to just owned components. It captures **all recordable components for all alive entities** currently present in the ECS world, regardless of network ownership or authority,.

Because the Flight Recorder is designed for high-performance state snapshots, it serializes data at the raw chunk level using highly optimized memory block copies (`CopyChunkToBuffer`),,. During this process, it iterates through the component tables and includes any component type that is deemed "recordable," completely ignoring the `AuthorityMask` within the `EntityHeader`,,.

This means it records the exact local state of the node, which includes both the entities the node has authority over, as well as all "ghost" entities (remote replicas owned by other nodes in the cluster).

The only factors that will exclude data from being recorded by the Flight Recorder are:

-   **Data Policy:** The component is explicitly marked with `[DataPolicy(DataPolicy.NoRecord)]` or `[DataPolicy(DataPolicy.Transient)]`,.-   **System IDs:** The entity's index falls below the `MinRecordableId` threshold, which is typically used to filter out internal system-level entities,.-   **Entity Filters:** A custom `EntityFilter` predicate is supplied to the `AsyncRecorder` (for example, the `EpisodeRecorderModule` uses this to restrict a recording to only entities tagged with a specific episode ID),,.-   **Liveness:** The entity is dead/destroyed, in which case its memory slot is sanitized and zeroed out of the chunk during the capture,.

That means every node records every recordable component. For ecample the Ig records all states of entities newer owned. Imagine many igs in the cluster, all passive receiving from net, but also recording stuff that could be received over net in playback in same manner as in live.

You have correctly identified a crucial architectural consideration, and your intuition about how passive nodes should behave during playback is exactly how the engine is designed!

While the Flight Recorder mechanism itself blindly captures all recordable components for all alive entities in its local ECS world, **Image Generator (IG) and ExCon nodes do not actually run the recorder.**

Here is how the architecture avoids the exact redundancy you described:

-   **Listener Nodes:** In the cluster topology, IG and ExCon nodes are explicitly designated as "listener" or "instructor" nodes.-   **No-Op Controllers:** Instead of using the active `EcsRecordReplayController`, these passive nodes are configured with a `ListenerRecordReplayController`. This is a "no-op" implementation; when the orchestrator broadcasts a command to start an exercise recording, these nodes acknowledge the cluster handshake (so the distributed two-phase commit doesn't stall) but they **do not record or replay any ECS frame data to disk**.-   **Restricted Recording:** The actual heavy lifting of writing `.fdprec` files is restricted exclusively to the authoritative simulation nodes—specifically those with the **Brain** (CGF) and **MuscleGround** (SimHost) roles.

Therefore, your hypothetical scenario is exactly how the system operates in practice. During a replay session, the authoritative Brain and Muscle nodes read their `.fdprec` files, restore the historical ECS state, and their network egress systems broadcast this state over the DDS network. Your dozens of passive IG nodes simply receive this data over the network and render it exactly as they would during a live simulation.

This ensures that storage space and I/O bandwidth are conserved, and passive network consumers remain completely unaware of whether the data they are receiving is live or a replay.

Ok but imilar situation is on cgf and simhost. Wondering if we shouldnt stop recording unowned stuff if it could be received through net in replay from owners like in live mide.

It is a very logical idea, and in a purely forward-playing simulation, relying on the network to populate unowned "ghost" entities during a replay would perfectly mimic live mode. However, the architecture deliberately forces every authoritative node (like CGF and SimHost) to record the _entire_ local ECS state—ghosts included—for two major architectural reasons:

**1\. Random-Access Timeline Scrubbing (Rewind & Fast-Forward)** The Playback Controller is designed to support instant, O(log N) operations like **SeekToFrame**, **Rewind**, and **FastForward**. If a CGF node only recorded what it owned, seeking to a historical point (e.g., jumping back 5 minutes) would require the CGF node to instantly restore its AI data, but it would then have to wait for the SimHost to independently seek to the same frame, broadcast its physical vehicle states over the CycloneDDS network, and wait for network ingress to process them.

Because DDS is inherently a real-time, forward-only stream, attempting to perfectly synchronize timeline scrubbing across multiple nodes via network messages would cause severe latency, tearing, and "pop-in" effects. By saving the full local snapshot, the node can instantly and perfectly reconstruct the exact historical moment from its local disk without waiting for network coordination.

**2\. Zero-Allocation Raw Memory Copies** To achieve high-frequency 60Hz recording without tanking the simulation's framerate, the `RecorderSystem` uses a raw memory block-copy strategy. Instead of iterating entity-by-entity and serializing individual fields, it directly copies entire 64KB ECS chunks of memory to a buffer (`CopyChunkToBuffer`).

Because network ownership is just a bitmask inside the `EntityHeader`, owned entities and unowned ghost entities are interleaved tightly within the exact same memory chunks. If the recorder had to check the authority mask of every entity and selectively exclude unowned data, it would destroy the performance benefits of raw memory copying. It would require fragmenting the chunks or reorganizing memory on the fly, which would cause heavy CPU overhead during the recording phase.

**Summary:** While recording unowned ghosts creates duplicate data across the cluster's `.fdprec` files, it is an intentional trade-off. Trading storage space allows the engine to maintain 60Hz zero-allocation recording performance, and guarantees that when you pause, rewind, or transition into a "Live-from-Replay" branch, the node's historical state is instantly and flawlessly reconstructed without relying on asynchronous network traffic.

We could sanitize the onowned components before saving. This is likely not an issue, is it?

It actually _would_ be a significant issue for the replay experience, even though it is technically feasible to implement.

From a technical standpoint, you are right that we _could_ sanitize the unowned data without abandoning the high-performance raw memory copies. Currently, the `RecorderSystem` copies entire memory chunks into a temporary `_scratchBuffer` and then calls `SanitizeScratchBuffer` using a boolean `_livenessBuffer` to zero out the memory slots of dead entities before writing to disk. We could adapt this logic to evaluate the `AuthorityMask` within the `EntityHeader` and zero out the unowned component bytes in that scratch buffer. Because we would be sanitizing the _copy_, it would not corrupt the live simulation memory.

However, implementing this would cause two major problems:

-   **It breaks random-access timeline scrubbing:** If you zero out the unowned components in the buffer, they are excluded from the `.fdprec` file. As we discussed earlier, if an operator rewinds the simulation, the node will instantly restore its owned entities from disk, but all the unowned "ghost" entities would suddenly vanish or be reset to zero. The simulation would have to wait for remote nodes to re-broadcast the ghost states over the network, causing severe visual pop-in and state tearing.-   **Additional CPU Overhead:** To sanitize the unowned components, the `FillLiveness` method—which currently just checks the `IsActive` flag and an optional `EntityFilter`—would have to be expanded to evaluate the `AuthorityMask` bit-by-bit for every entity. Zeroing out those specific byte offsets inside the scratch buffer adds extra CPU overhead on the hot path, which conflicts with the goal of a lightweight 60Hz background recorder.

In short, while you can safely sanitize the unowned components in the scratch buffer before saving, doing so intentionally cripples the replay system's ability to seamlessly rewind and fast-forward through time.

I understand the simplicity if recording everyrhing everywhere. It is tempting option and definitely the plan A. But lets keep the line of thinking of recoeding just owned stuff (plan B). We orchestrate the jumps in time. It should be no problem to keep this cluster state 'in progress' long enough to let each owner to restore the snapshot of owned components and send updates over dds and to let the unowners to receive those.

Exploring "Plan B" is a great architectural exercise. From a pure orchestration standpoint, your logic is sound: the cluster's 2-Phase Commit (2PC) pipeline is perfectly equipped to handle this. The `ClusterMaster` could dispatch a `ReplaySeek` command, and each node's `ClusterSlave` could return an `OrchestrationStatusCode.InProgress` to hold the cluster in a suspended state while it restores its owned entities, broadcasts them over DDS, and ingests the remote ghosts.

However, if we rely on the live network to reconstruct the unowned state during a replay, we collide with several core engine mechanisms that make this incredibly difficult to execute reliably:

**1\. The Convergence Problem (How long is "long enough"?)** To exit the 'in progress' wait state and resume playback, a node needs to definitively know it has received _all_ ghost updates for that specific historical tick. Because DDS is a decoupled publish/subscribe protocol, a receiving node has no idea how many remote entities existed at a given point in the past. Without a centralized, synchronized manifest of "who owned what at Tick X", a node cannot mathematically prove it has finished receiving updates and cannot safely signal `Success` to the orchestrator to unpause the timeline.

**2\. QoS Mismatches and Volatile Data Loss** High-frequency physical data, such as the `EntityStateTopic` or `WorldPos` descriptors, are intentionally configured with `BestEffort` reliability and `Volatile` or `TransientLocal` durability to maximize live performance. If Node A finishes loading its disk snapshot in 50ms and instantly broadcasts its owned positions, but Node B takes 150ms to finish disk I/O and spin up its readers, Node A's volatile packets will be dropped. Node B will be missing those ghosts entirely until the `SmartEgressUtil`'s rolling 10-second heartbeat finally fires to correct the drop.

**3\. The** **InputSystemGroup** **Contradiction** This brings us back to the exact vulnerability we discussed previously. For a node to receive and process these ghost updates over the network during a replay, the `CycloneIngressSystem` must be actively running. Because that system lives in the `InputSystemGroup`, we would be forced to leave the input phase enabled during playback. This destroys the hermetic seal of the replay, exposing the historical simulation to live network intents, duplicate bullet spawns, and operator clicks.

**4\. Re-introducing Live Jitter to Replays** Even after the time jump completes, as you press "play" and the timeline moves forward, relying on DDS to constantly update ghosts means the replay is now subject to actual network latency and jitter. Instead of a pristine, frame-perfect reproduction driven instantly from local disk memory blocks, the playback would visually stutter and tear exactly as it might have during a poor network connection in the live exercise.

In summary, while **Plan B** saves disk space and utilizes the orchestration pipeline elegantly, it requires fighting against the engine's network QoS, breaks the hermetic isolation of the ECS timeline, and introduces non-deterministic synchronization issues. **Plan A** (recording everything everywhere) trades storage capacity to guarantee instant, perfectly sealed, and zero-latency state restoration.

I want to be ready to switch to plan b. Meaning i need to oprionally include relevant parts of network ingress translators (those bringing entity components) operational during replay. Maybe we need extra group next to Input we could keep enabled in plan B?

To successfully transition to **Plan B**—where authoritative nodes restore only their owned components from disk and rely on the live CycloneDDS network to stream in the unowned "ghost" entities—you are exactly right: you must separate your network ingress pipeline.

Currently, all network translators are bundled into a single `CycloneNetworkIngressSystem` that executes during the input phase. If you leave this running during playback, it will ingest both harmless position updates and dangerous live intents (like fire commands).

Here is the architectural adjustment needed to support Plan B safely:

**1\. Split the Translators by Category** Instead of feeding all translators into one system, categorize your `IDescriptorTranslator` and `INetworkEventTranslator` instances into two distinct lists:

-   **State Replicators (Keep Enabled):** These are the translators that blindly synchronize declarative state. This includes everything from the `EntityStatesIngressPack` (e.g., `EntityMasterIngressTranslator`, `GeoSpatialIngressTranslator`, `EntityInfoIngressTranslator`, `MapVisualOverlayIngressTranslator`, `EntityDamageIngressTranslator`).-   **Intent & Event Injectors (Disable):** These translators inject stimuli that trigger simulation logic. This includes `WeaponFireRequestIngressTranslator`, `MissionControlIngressTranslator`, `MunitionDetonationIngressTranslator`, and `AudioTargetDetectedIngressTranslator`.

**2\. Create a Dedicated "State Ingress" Group** As you suggested, create a new system group specifically for network state synchronization.

-   Create a `NetworkStateIngressGroup` and schedule it to run right before or alongside the standard `InputSystemGroup`.-   Register a `CycloneNetworkIngressSystem` exclusively containing the **State Replicators** into this new group.-   Register a second `CycloneNetworkIngressSystem` containing the **Intent & Event Injectors** into the standard `InputSystemGroup`.

**3\. Modify the Replay Orchestrator (****ReferenceReplayLoadHandler****)** Currently, `ReferenceReplayLoadHandler` explicitly disables the `SimulationSystemGroup` and the `NetworkLifecycleSystemGroup`, and it activates the `GhostCreationSystem.BypassLifecycle` toggle to prevent live network ghosts from spawning and conflicting with the replayed disk data.

To execute Plan B, you must update `ReferenceReplayLoadHandler` to do the following during a replay:

-   **Disable** the `SimulationSystemGroup` (to stop AI and physics logic).-   **Disable** the standard `InputSystemGroup` (to block live intents, duplicate bullet spawns, and UI clicks from reaching the historical state).-   **Keep Enabled** the new `NetworkStateIngressGroup` so the node can receive DDS updates for the unowned entities.-   **Keep Enabled** the `NetworkLifecycleSystemGroup` (and do _not_ toggle the `BypassLifecycle` flag). Because Plan B relies on the network to populate unowned entities, systems like `GhostCreationSystem` and `GhostPromotionSystem` must remain active so that incoming `EntityMaster` samples are properly materialized into live ECS ghost entities.

By splitting the ingress pipeline this way, you hermetically seal the node from live _behavioral_ inputs while leaving the door open for declarative _state_ replication, giving you the foundation needed for Plan B.

I need to add a config parameter to all affected parts, switching between record-everyrhing and record-owned-only

To implement the switch between "Plan A" (record everything) and "Plan B" (record owned only, replay unowned from network), you need to thread a new configuration parameter from the top-level application configuration all the way down to the ECS recorder, and update the replay handler to adjust the system groups accordingly.

Here is the step-by-step architectural breakdown of all the affected parts you need to update:

**1\. Top-Level Node Configuration** First, add the configuration toggle to your JSON-serializable node configuration so operators can switch modes.

-   **Hrot.SimHost.NodeConfiguration**:

**2\. Recording Configuration Data Transfer Object (DTO)** Pass the flag into the Replay toolkit's configuration structure so the recording modules can read it.

-   **Fdp.Toolkit.Replay.RecordingConfiguration**:

**3\. The Flight Recorder Core** You must pass the flag down to the high-performance memory capturers where the actual `AuthorityMask` filtering will happen.

-   **Fdp.Core.FlightRecorder.AsyncRecorder**: Add a passthrough property to the underlying system.-   **Fdp.Core.FlightRecorder.RecorderSystem**: Add the configuration property here. In a future step, you will use this flag inside `SanitizeScratchBuffer` to zero-out bytes for components where the entity's `AuthorityMask` does not indicate ownership.

**4\. Recording Modules** Map the configuration from the DTO to the `AsyncRecorder` when the modules instantiate it.

-   **Fdp.Toolkit.Replay.RecordingModule** & **EpisodeRecorderModule**: Update `RegisterSystems()` where the recorder is created:

**5\. The Orchestration Controller** The controller acts as the factory for recordings. It needs to receive the configuration flag from the bootstrapper and apply it to new recordings.

-   **Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController**: Update the constructor to accept the flag:-   Then, pass it into the config object during `PrepareRecordingAsync` and `StartEpisodeRecordingAsync`:

**6\. Replay Group Orchestration (Plan B Execution)** To successfully execute Plan B during a replay, the network ingress systems must be split (as previously discussed), and the orchestrator must know which to keep alive.

-   **Fdp.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler**: Update the constructor and fields to manage the newly separated input groups.-   In the `Commit` method, modify `SetSystemsEnabled(false)` to enforce the hermetic seal while allowing Plan B ghosts:

**7\. System Bootstrapper Wiring** Finally, feed the top-level `NodeConfiguration` flag into the pipeline at startup.

-   **Hrot.SimHost.NodeBootstrapper**: Update the `BuildOrchestration` method parameters to pass the `RecordOwnedOnly` config value down to the `EcsRecordReplayController` and `ReferenceReplayLoadHandler`.

How does the replay of sanitized components work? Are these completeli ignored on replay, not overwriting the ECS space with zeros?

They are **not ignored**, and they **do completely overwrite the ECS space with zeros**.

During playback, the `PlaybackSystem` reads the binary chunk data from the recording and passes it down to `RestoreChunkFromBuffer`. For unmanaged component tables, this method uses a highly optimized `Unsafe.CopyBlock` operation to blindly blit the entire raw memory chunk (up to 64KB) directly back into the `NativeChunkTable`.

Because the engine relies on this bulk memory copy for maximum performance, the playback system does not evaluate individual entities during restoration. It does not check if an entity slot is alive, dead, or unowned—it simply copies the bytes exactly as they were recorded. Therefore, any bytes that were zeroed out (sanitized) by the recorder will physically overwrite whatever data is currently sitting in that ECS memory slot.

This is the exact reason why sanitizing unowned components before saving (Plan B) breaks timeline scrubbing. If you zero out the unowned "ghost" entities in the saved chunk, applying that historical chunk during a rewind or fast-forward will instantly overwrite those ghosts' live memory with zeros, forcing you to wait for the network to re-transmit their states to fix the visual pop-in.

Ok so this feature makes plan b unusable. Good, so lt's keep just the plan a.

That is absolutely the safest and most architecturally sound decision. Because the `PlaybackSystem` relies on raw memory block copies to blit historical data directly back into the `NativeChunkTable`, the engine fundamentally assumes that the saved memory chunks represent the complete, absolute truth of the simulation at that exact moment.

If you proceeded with Plan B and sanitized the unowned components by zeroing them out, every time the orchestrator jumped to a new frame, those saved zeros would instantly annihilate the live network states of all ghost entities. The simulation would constantly break visually and logically, forcing you to wait for the CycloneDDS network to re-transmit the missing entities.

Sticking with **Plan A** (recording all recordable components for all active entities, regardless of network ownership) perfectly aligns with the engine's design goals. While it duplicates ghost data across the cluster's disk files, it guarantees that the `PlaybackController` can perform its fast O(log N) binary search seeks, such as `SeekToWallClockTicks`, and instantly restore the exact historical state with zero allocation and zero latency.

This ensures your replay timeline remains perfectly deterministic and hermetically sealed from the live network, allowing flawless rewinding, fast-forwarding, and transitioning into live branches.

So what remains to do is disabling the input system during replay. Something else?

**1\. Threading the Input Group to the Orchestrator** Currently, `ReferenceReplayLoadHandler` and `NodeBootstrapper.BuildOrchestration` are only designed to accept and toggle the `SimulationSystemGroup` and the `NetworkLifecycleSystemGroup`. You need to update their constructors to also accept the `InputSystemGroup` so that `ReferenceReplayLoadHandler.Commit` can actually set `_inputGroup.Enabled = false` when a replay begins.

**2\. Guarding the Global Genesis Systems** While your standard input and simulation systems are neatly packed into `_inputGroup` and `_simGroup`, systems like `CreateEntityRequestSystem`, `DeleteEntityRequestSystem`, and `NetworkSpawningSystem` are registered individually via `_kernel.RegisterGlobalSystem()`. Because they bypass the standard toggleable groups, **they will remain active during playback.** If a live node broadcasts a `CreateEntityRequest` over the DDS network, these systems will intercept it and spawn a brand new entity right into your historical replay. You should bundle these systems into the `NetworkLifecycleSystemGroup` (which is already safely disabled during replay) or create a new toggleable group for them.

**3\. Purging the Event Bus Buffers** When the simulation transitions from live mode to replay mode, there may be lingering events (like a `WeaponFireIntent` or an operator click) sitting in the `FdpEventBus` pending buffers from the exact millisecond the operator hit pause. You must explicitly call `FdpEventBus.ClearCurrentBuffers()` before applying the first replay frame. If you do not clear the bus, those live events will swap into the read buffer and execute against the historical state.

**4\. Ensuring the Spatial Grid is Rebuilt** During playback, the engine blasts raw chunks back into memory. While the chunk data is restored perfectly, transient acceleration structures are not. `SpatialHashSystem` runs in the `SimulationSystemGroup`, which we are disabling. This means the `SpatialGridData` singleton will go stale, which could break features that rely on it for read-only visualization or operator interaction during playback. You should either ensure `SpatialHashSystem` remains in a group that ticks during replay, or let `PlaybackTickSystem` trigger a manual grid rebuild after restoring the delta chunks.

The current state of the systems, groups and modules wirh the adapters allowing the systems to show up in the diagnostic window seem pretty complex to manage and confusing. Is there any clean way? Refactoring opportunity? It is manatory to keep the profiling but can we make it a bit easier to understand and maintain?

You are absolutely right. The current state is highly convoluted because the architecture is stuck in a transitional phase. It attempts to bridge the legacy `SystemGroup` pipeline from `Fdp.Core` into the new `ModuleHostKernel` scheduling and diagnostic pipeline using heavy, hacky wrappers.

**Why it is currently so complex:** The root of the issue is that the kernel explicitly forbids global systems from running in `SystemPhase.Simulation` (reserving it for background module systems). To get around this, the codebase wraps legacy `SystemGroup` instances inside an `IEcsModule` (like `SimulationGroupModule`), extracts the children, registers them into a dummy `SystemPhase.Manual` to hijack the `SystemScheduler`'s profiler, and then ticks them manually in the module's `Tick()` method.

Here is the clean refactoring strategy to drastically simplify this while perfectly maintaining the profiling capabilities:

**1\. Embrace the "System-Based Module" Pattern (Short-term)** The `IEcsModule` interface is explicitly designed to handle this via `RegisterSystems(ISystemRegistry)`. Because systems registered by an `IEcsModule` are treated as module systems, the kernel _will_ automatically execute them in `SystemPhase.Simulation` and natively profile them.

-   **Action:** Stop passing `SystemGroup` objects down through `SimHostCoreLogicPack` and `CgfLogicPack`. Instead, pass the `ISystemRegistry` down to the sub-modules (`CombatModule`, `GroundKinematicsModule`, etc.) and let them register their systems directly.

**2\. Eliminate the Wrapper Modules and** **SystemPhase.Manual** Once systems are registered directly via the `ISystemRegistry`, the `SystemScheduler` takes over. It will automatically sort them based on their attributes, execute them in the correct phase, and wrap them with performance profiling timers.

-   **Action:** Delete `SimulationGroupModule`, `CgfInputGroupAdapter`, `PostSimulationGroupAdapter`, and the `ProfiledManualSystemWrapper`. You can also completely remove `SystemPhase.Manual` as the scheduler will handle the ticking.

**3\. Use a Single Lightweight Adapter (Mid-term)** Until you can rewrite every legacy system, you only need one simple adapter to bridge a legacy `ComponentSystem` to `IEcsModuleSystem` and `IProfiledSystem`.

-   **Action:** Create a single `LegacySystemAdapter : IEcsModuleSystem, IProfiledSystem`. When your logic packs register systems, they just do: `registry.RegisterSystem(new LegacySystemAdapter(new SpatialHashSystem(), SystemPhase.Simulation));` This ensures the `ArchitectureDiagnosticsWindow` displays the clean system name (via `IProfiledSystem.ProfileName`) and the scheduler profiles it automatically.

**4\. Convert to Native** **IEcsModuleSystem** **(Long-term)** The ultimate clean state is for legacy systems (like `CarKinematicsSystem`, `DamageSystem`, etc.) to stop inheriting from `Fdp.Core.ComponentSystem` and natively implement `IEcsModuleSystem`.

-   **Action:** Update the systems to implement `void Execute(ISimulationView view, float deltaTime)` instead of `void Run()`. Add the `[UpdateInPhase]` attributes directly to the system classes. Once this is done, the adapter classes can be entirely removed, and your composition roots will consist of clean, direct `registry.RegisterSystem(new CarKinematicsSystem())` calls.

Shall we do it in phases (first short term, then mid term, the long term) or can we refactor to the most clean solution right away? I am not afraid of jumping right to the most clean one right away if feasible..

You can absolutely jump straight to the cleanest solution—converting everything natively to `IEcsModuleSystem`—if you have the capacity to tackle it in one large refactoring pass. It is highly feasible and will immediately eliminate a massive amount of technical debt, completely dropping the need for legacy adapters like `SimulationGroupModule`, `CgfInputGroupAdapter`, and `ProfiledManualSystemWrapper`.

However, before doing a single-pass refactor, you must be aware of **one major behavioral shift** between the legacy systems and the new ones.

If you jump straight to the final architecture, here is exactly what the refactoring will require:

**1\. The Structural Change Shift (The Biggest Risk)** Legacy `ComponentSystem` implementations allowed direct, immediate world mutations (e.g., `World.AddComponent(...)` or `World.DestroyEntity(...)`). The new `IEcsModuleSystem` enforces thread-safe deferred structural changes. You must use `view.GetCommandBuffer()`. This means operations like `cmd.AddComponent(...)` and `cmd.DestroyEntity(...)` are queued up and only played back at the end of the module's execution. You must audit your systems to ensure none of them rely on a component being instantly available on the very next line of code after adding it.

**2\. Updating the Execution Signature** Every legacy system inheriting from `ComponentSystem` must be changed to implement `IEcsModuleSystem`.

-   Remove `protected override void OnUpdate()`.-   Implement `public void Execute(ISimulationView view, float deltaTime)`.-   Replace ambient properties like `DeltaTime` with the passed-in parameter.-   Change `World.Query()` to `view.Query()`.-   Change component access from `World.GetComponentRW<T>` to `view.GetUnmanagedComponentRW<T>` (or use `ref view.GetComponentRO<T>`) depending on whether you are using the repository implementation directly or the interface.

**3\. Attribute-Based Phase Scheduling** You will completely delete the legacy `SystemGroup` classes (`SimulationSystemGroup`, `InputSystemGroup`, etc.). Instead, you will decorate every system directly with `[UpdateInPhase]` and rely on existing dependency attributes:

-   Add `[UpdateInPhase(SystemPhase.Simulation)]`, `[UpdateInPhase(SystemPhase.Input)]`, etc., to the top of your system classes.-   Keep using `[UpdateBefore(typeof(...))]` and `[UpdateAfter(typeof(...))]` for ordering.-   The `SystemScheduler` inside the `ModuleHostKernel` will automatically parse these attributes, build a topological dependency graph, and execute them in the correct order while profiling them natively.

**4\. Deleting the Boilerplate** Once the systems are natively `IEcsModuleSystem` and decorated with phase attributes, you can drastically simplify your composition roots (`SimHostCoreLogicPack`, `CgfLogicPack`, etc.):

-   You will no longer need to pass `SystemGroup` instances into the packs.-   Inside the pack's `RegisterSystems(ISystemRegistry registry)` method, you simply call `registry.RegisterSystem(new MyConvertedSystem());`.-   You can delete all the wrapper classes: `LegacySystemAdapter`, `ProfiledManualSystemWrapper`, `CgfInputGroupAdapter`, and `SimulationGroupModule`.

**Recommendation:** If you are comfortable converting the direct `World` mutations to `IEntityCommandBuffer` commands, jumping straight to the end state is highly recommended. The engine already has several excellent examples of native module systems you can use as templates (such as `GhostPromotionSystem`, `MapLayerAssignmentSystem`, and `DeadReckoningSyncSystem`). Doing it all at once will leave your execution topology clean, perfectly profiled, and fully aligned with the modern `ModuleHostKernel`.

The command buffers is necessary fir the modules not working with the main ecs (like the background ones) But the one working with main ecs have no such technical limiration. Cant we keep using direct world mutation for those?

You are technically correct. For modules configured to run synchronously on the main thread (`DataStrategy.Direct`), the `ISimulationView` passed into the system is literally the live `EntityRepository`, so there is no hard technical barrier preventing direct structural mutation.

However, the `IEcsModuleSystem` interface intentionally abstracts structural mutations (like `CreateEntity`, `AddComponent`, or `DestroyEntity`) behind `ISimulationView.GetCommandBuffer()` for two critical architectural reasons:

-   **Module Portability (Strategy Agnosticism):** The `ISimulationView` is designed to be an abstraction over the live world, GDB replicas, and SoD snapshots. By forcing systems to use the command buffer, the module is completely oblivious to whether it is running on the main thread or in the background. If you bypass the command buffer to mutate the world directly, you **permanently lock that system to the main thread**. If you ever decide to optimize performance by moving that module to a background thread (`RunMode.Asynchronous` or `FrameSynced`), it would instantly break because background threads cannot safely mutate read-only snapshots.-   **Phase Consistency:** Even on the main thread, deferring structural changes to the command buffer ensures that other systems executing in the same phase aren't tripped up by memory chunks shifting or component masks changing mid-iteration. The commands are safely played back at the end of the execution block.

**The "Escape Hatch" (How to keep using direct mutation)** If you have a system that you are absolutely certain will _never_ run in the background, and you prefer the simplicity of immediate structural changes, **you can bypass the command buffer by downcasting the view back to** **EntityRepository**.

The engine actually uses this exact pattern internally for highly specific, main-thread-only systems (such as `BlueprintApplicationSystem` and `DisposalMonitoringSystem`) that need to apply changes instantly:

```
public void Execute(ISimulationView view, float deltaTime)
{
    // Downcast to bypass the command buffer
    if (view is not EntityRepository repo) return; 

    // Direct world mutation is now available
    var entity = repo.CreateEntity();
    repo.AddComponent(entity, new MyComponent()); 
}
```

**In summary:** You are absolutely allowed to keep using direct world mutations for your synchronous systems by downcasting the view. Just be aware that doing so trades away the ability to easily offload those systems to background threads in the future.

What systems are using setcomponent or setmanagedcomponent directly?

Several systems bypass the deferred `EntityCommandBuffer` and call `SetComponent` or `SetManagedComponent` directly on the main `EntityRepository` (often accessed via their `World` property):

-   **MapLayerAssignmentSystem**: Calls `repo.SetComponent` to apply the computed `MapDisplayComponent` layer bitmasks to entities.-   **GenesisMaterializationSystem**: Directly applies resolved structural components like `PassengerBuffer`, `IsEmbarkedTag`, `VisHierarchyNode`, `PersonalRouteRef`, and `TargetMemory` using `World.SetComponent` once their required network IDs are matched to live entities.-   **NavigationExecutionSystem**: Updates the `NavigationStatus` component directly via `World.SetComponent` to report the ongoing progress, success, or failure of a navigation intent.-   **NavigationIntentBridgeSystem**: Translates a high-level `NavigationIntent` into physical parameters and sets the `NavState` component via `repo.SetComponent`.-   **MissionControlExecutionSystem**: Assigns mission plans directly by calling `repo.SetComponent` for the unmanaged `MissionPlanQueue` and `repo.SetManagedComponent` for the managed `ActiveMissionPlan`.-   **UpdateEntityDescriptorRequestSystem**: Applies network-driven visual overlay updates by calling `World.SetManagedComponent` for `EditablePolyline` and `World.SetComponent` for `MapOverlayStyle`.-   **VehicleCommandSystem**: Updates `NavState` directly via `World.SetComponent` when processing high-level vehicle command events, such as resetting the kinematics mode.-   **StyleResolutionSystem**: Contains a direct-mutation optimization path; if it can successfully downcast its injected `ISimulationView` to the concrete `EntityRepository`, it directly calls `repo.SetComponent` to apply the `ResolvedStyle` component instead of queueing it in the command buffer.

Which of those can be safely converted to using command buffers?

Only a few of those systems can be safely converted to use deferred command buffers without breaking existing engine logic. The rest rely on immediate world mutations due to strict intra-phase dependencies, network egress timing, or lifecycle guarantees.

**Safely Convertible** These systems handle visual or presentation state where deferring the mutation to the end of the module execution block is perfectly safe:

-   **StyleResolutionSystem**: This system is already designed to be safely convertible. Its code actually includes a fallback path that uses the command buffer (`view.GetCommandBuffer()`) if it cannot directly downcast the view to the `EntityRepository`.-   **MapLayerAssignmentSystem**: This runs on a time-sliced iterator in the `PostSimulation` phase to assign `MapDisplayComponent` bitmasks for rendering. Because it is a purely declarative visual update, delaying the structural change until the module finishes executing will not negatively impact any simulation logic.

**Unsafe or Problematic to Convert** The remaining systems cannot be safely converted to command buffers without significant architectural refactoring, due to the following limitations:

-   **Intra-Phase Dependencies (****NavigationIntentBridgeSystem****,** **VehicleCommandSystem****):** These systems run in the `Simulation` phase and mutate the `NavState` component right before the physics engine runs. For example, `NavigationIntentBridgeSystem` must run before `CarKinematicsSystem` so the updated `NavState` is instantly visible to the physics layer in the exact same tick. If you used a command buffer, the mutation would be deferred until the end of the module, and the physics system would read stale, outdated data.-   **Network Egress Dirty Flagging (****MissionControlExecutionSystem****,** **UpdateEntityDescriptorRequestSystem****):** These systems immediately call `SmartEgressUtil.MarkDirty()` right after setting their components. The egress system relies on these dirty marks to publish data to the network. If you deferred the component data mutation via a command buffer but instantly flagged the entity as dirty, you would create a race condition where the network egress translator could read and publish the _old_ component data before the command buffer had a chance to apply the new data.-   **Strict Initialization Timing (****GenesisMaterializationSystem****):** This system currently instantiates its _own_ local `EntityCommandBuffer` and explicitly calls `cmd.Playback(World)` to force an immediate flush within its own update loop. Moving it to the standard module-provided command buffer would delay the materialization of cross-entity references, potentially breaking other systems that expect those entities to be fully hydrated in the same frame.

Shall we invent some attribute checking if the system is on main ecs and throwing exception if not?

**It is a very sound defensive programming idea** to catch configuration errors at boot time, but you might not actually need to invent a new attribute because the engine already has several built-in safety nets that accomplish almost exactly this.

If you _were_ to invent an attribute (e.g., `[RequireMainThread]`), the ideal place to evaluate it would be during `ModuleHostKernel.Initialize()`. When the kernel validates execution policies, it could scan the systems registered to each module. If a system requires the main ECS but the module's `ExecutionPolicy.Strategy` is not `DataStrategy.Direct` or its `Mode` is not `RunMode.Synchronous`, the kernel could throw an exception and halt the boot process.

However, before building this, consider the existing architectural guards that already prevent these systems from corrupting background snapshots:

**1\. The** **EntityRepository** **Downcast is a Hard Gate** To bypass the command buffer and mutate the world directly, your system is forced to downcast the abstracted `ISimulationView` to the concrete `EntityRepository`.

-   If you write `if (view is not EntityRepository repo) return;`, the system safely acts as a no-op if it ever finds itself running against a wrapped view (like the `PerceptionScopedView` used in background perception modules).

**2\. Built-in Phase Validation for Global Systems** The kernel already strictly polices where direct-mutation systems can run if they are registered globally. If you attempt to register a global system marked with `[UpdateInPhase(SystemPhase.Simulation)]` (the phase reserved for background thread logic), `ModuleHostKernel.RegisterGlobalSystem` explicitly intercepts it and throws an `InvalidOperationException`. It forces global systems into main-thread phases like `Input`, `BeforeSync`, `PostSimulation`, and `Export`.

**3\. Module-Level Execution Policies** The systems that rely on direct mutation are inherently bundled into modules that explicitly demand main-thread access. For example, `SimHostCoreLogicPack` and `UrbanCombatModule` hardcode their policies to `ExecutionPolicy.Synchronous()`. As long as the module itself demands `DataStrategy.Direct`, the kernel guarantees the main ECS world is passed to the system.

**Summary:** You can certainly add a custom attribute for strict, fail-fast validation at startup to ensure no one registers a direct-mutation system into an asynchronous module. However, between the required downcast to `EntityRepository` and the kernel's existing phase guards, the engine is already architecturally protected against these systems executing against background snapshots.

Ok no new attribute needed then. Now we know what all systems needs the downcast conversion and safety check. We can proceed to the refactor to iecsmodules. Are you sure we are not losing anything by the refactor? Performance? Flexibility? Simplicity? Safety?

You are actually gaining ground across all four of those metrics. Refactoring natively to `IEcsModuleSystem` is a significant architectural upgrade that perfectly aligns the systems with the modern `ModuleHostKernel`.

Here is exactly how the refactor impacts those dimensions:

**1\. Performance (Net Gain)** You are not losing any performance. By removing the legacy adapters (like `SimulationGroupModule` and `CgfInputGroupAdapter`), you strip away layers of virtual dispatch and indirection.

-   For the systems you convert to use the `IEntityCommandBuffer`, structural mutations are securely batched and played back with highly optimized, zero-allocation memory routines.-   For the systems where you keep direct mutation (using the downcast to `EntityRepository`), the performance remains identical to the legacy `ComponentSystem`.-   Additionally, the `ModuleHostKernel` runs on a zero-allocation Read-Copy-Update (RCU) execution topology, meaning the execution loop itself is heavily optimized for the 60Hz hot path.

**2\. Flexibility (Massive Gain)** This is where the refactor shines. Legacy systems were rigidly bound to hardcoded groups (`InputSystemGroup`, `SimulationSystemGroup`, etc.).

-   With `IEcsModuleSystem`, you simply decorate your classes with `[UpdateInPhase]`, `[UpdateBefore]`, and `[UpdateAfter]` attributes. The `SystemScheduler` automatically builds a topological dependency graph to resolve the perfect execution order.-   Because standard modules use the abstracted `ISimulationView`, systems that utilize the command buffer can easily be shifted from the main thread to background threads (`RunMode.Asynchronous`) using Snapshot-on-Demand (SoD) or Double-Buffered (GDB) strategies without changing a single line of the system's logic.

**3\. Simplicity (Net Gain)** Your composition roots will be vastly simplified. You can delete all the convoluted boilerplate classes used to hijack the profiler—`CgfInputGroupAdapter`, `ProfiledManualSystemWrapper`, `LegacySystemAdapterBase`, and `SimulationGroupModule`. Instead, your logic packs will consist of clean, direct `registry.RegisterSystem(new MySystem())` calls.

**4\. Safety (Significant Gain)** The architecture becomes much safer to maintain.

-   **Data Integrity:** Deferring structural changes to the command buffer ensures that chunk memory does not shift while other systems are iterating over entities in the same phase.-   **Thread Safety:** The downcast (`if (view is not EntityRepository repo) return;`) securely acts as a hard safety gate. If another developer accidentally configures a direct-mutation system to run on a background thread against a read-only snapshot, the downcast safely acts as a no-op instead of crashing the engine or corrupting memory.-   **Phase Policing:** The `ModuleHostKernel` actively policies phase registration. If you attempt to register a system in a phase that shouldn't run globally, the kernel throws an `InvalidOperationException` right at startup, catching configuration bugs immediately.

In short, jumping straight to the clean `IEcsModuleSystem` implementation eliminates technical debt, unlocks dynamic scheduling, and fully activates the engine's built-in performance profilers. You are in a great position to proceed!

If we convert systems to modules, dont we lose the groups that can be disabled in replay?

Yes, you are completely correct to spot this. If you delete the legacy `SimulationSystemGroup` and `InputSystemGroup` classes and register all systems directly using the `[UpdateInPhase]` attribute, the `ReferenceReplayLoadHandler` will lose the direct object references it currently uses to set `_simGroup.Enabled = false`.

However, moving to the new `IEcsModuleSystem` architecture does not mean you lose the ability to disable systems during playback. Instead of relying on legacy group inheritance, the modern engine provides two much cleaner ways to handle this:

**1\. Modern Togglable System Wrappers (The Direct Replacement)** You can replicate the exact toggling behavior using modern wrapper systems. The engine already uses this exact pattern for the `NetworkLifecycleSystemGroup`.

Instead of inheriting from a base class, you create a lightweight struct or class that implements `IEcsModuleSystem` and wraps an array of inner systems. It exposes a simple `Enabled` boolean flag:

```
public void ExecuteGroup(ISimulationView view, float deltaTime)
{
    if (!Enabled) return;
    foreach (var sys in _innerSystems)
        sys.Execute(view, deltaTime);
}
```

You can create a `TogglableSimulationGroup` using this pattern, register it natively into `SystemPhase.Simulation`, and pass its reference to the `ReferenceReplayLoadHandler`. When the replay starts, the handler simply flips `Enabled = false` and the inner systems are instantly suspended.

**2\. Atomic Module Hot-Plugging (The Architecture-Pure Approach)** The most powerful feature of the new `ModuleHostKernel` is its Read-Copy-Update (RCU) execution topology. Instead of leaving systems in the loop and muting them with booleans, you can physically remove them from the engine.

The kernel provides **UninstallModuleAsync** and **InstallModuleAsync**. When the orchestrator transitions into replay mode, the `ReferenceReplayLoadHandler` could simply be given a list of your behavior modules (like your new `CombatModule` or `GroundKinematicsModule`) and instruct the kernel to uninstall them.

-   The kernel will compile a brand new `KernelExecutionTopology` in the background.-   It performs a zero-allocation atomic pointer swap on the 60Hz hot path to seamlessly transition to the new topology without the simulation systems.-   When transitioning back to live mode, the orchestrator reinstalls the modules, and the kernel effortlessly wires them back into the execution loop.

**Summary:** While you do lose the legacy `.Enabled` property by deleting the old `SystemGroup` classes, you easily replace it by either creating a modern `IEcsModuleSystem` wrapper with an explicit `Enabled` flag (just like `NetworkLifecycleSystemGroup`), or by utilizing the kernel's built-in ability to cleanly hot-plug entire modules out of the simulation during replay.

The beauty of grouping was that the replay handler did not need to know exact systems to enable/disable

You are absolutely right. The decoupling provided by that pattern is incredibly valuable, as it allows the `ReferenceReplayLoadHandler` to pause the simulation without needing to know anything about the specific game logic systems it is pausing.

The great news is that moving to the modern `IEcsModuleSystem` architecture does not mean you have to give up this exact benefit. Instead of relying on the legacy `SystemGroup` base class inheritance, you can preserve the exact same facade pattern using **composition**, which is precisely how the modern `NetworkLifecycleSystemGroup` already works.

If you look at the `NetworkLifecycleSystemGroup`, it is just a lightweight wrapper that holds an array of inner `IEcsModuleSystem` instances and exposes a simple `Enabled` boolean. When the group is executed, it simply checks the flag:

```
public void ExecuteGroup(ISimulationView view, float deltaTime)
{
    if (!Enabled) return;
    foreach (var sys in _innerSystems)
        sys.Execute(view, deltaTime);
}
```

Because of this, your `ReferenceReplayLoadHandler` continues to work exactly as it did before. It still accepts the high-level group references in its constructor and simply flips their states using `SetSystemsEnabled(false)` when a replay begins.

The orchestrator remains completely oblivious to whether the group contains `CarKinematicsSystem`, `DamageSystem`, or `HitResolutionSystem`. You get to keep the exact "beauty of grouping" while completely eliminating the legacy adapter overhead and fully activating the modern `ModuleHostKernel`'s performance benefits.

Ok. So what the new state after the refactir should look like? What modules, what modulesystems, what new groups? Include the new needs of disabling the input phase systems in replay.

After the refactoring to a native `IEcsModuleSystem` architecture, the codebase will be significantly leaner. All legacy `SystemGroup` base classes and their associated hacky wrappers (like `SimulationGroupModule`, `CgfInputGroupAdapter`, and `ProfiledManualSystemWrapper`) will be completely deleted.

Here is the exact architectural blueprint of the new state, incorporating the Plan A replay requirements (record everything everywhere, hermetically sealed).

1\. The Modules (`IEcsModule`)

Your composition roots (logic packs) will no longer pass around legacy `SystemGroup` objects. Instead, they will act as pure registrars that define the execution policy and bundle systems together.

-   **Domain Modules**: Modules like `CombatModule`, `ActionDispatchModule`, and `PhysicsQueryModule` will remain, but their `RegisterSystems` methods will directly call `registry.RegisterSystem(new MySystem())` instead of adding to a passed-in group.-   **Top-Level Logic Packs**: Packs like `SimHostCoreLogicPack` and `CgfLogicPack` will define your pipeline. They will instantiate the new togglable wrappers (see below) and register them into the `ModuleHostKernel`.

2\. The Systems (`IEcsModuleSystem`)

Every individual simulation and input system will natively implement `IEcsModuleSystem`.

-   **Phase Declarations:** Systems will drop their legacy `[UpdateInGroup]` attributes and directly declare their kernel phase using `[UpdateInPhase(SystemPhase.Simulation)]`, `[UpdateInPhase(SystemPhase.Input)]`, etc..-   **Execution Signature:** `protected override void OnUpdate()` will be replaced by `public void Execute(ISimulationView view, float deltaTime)`.-   **Structural Mutations:** Systems handling background tasks will use `view.GetCommandBuffer()` for deferred structural changes. Systems requiring immediate mutation (like `GenesisMaterializationSystem` or `StyleResolutionSystem`) will utilize the `if (view is not EntityRepository repo) return;` downcast escape hatch.

3\. The New Groups (Composition Wrappers)

To maintain the "beauty of grouping" so the orchestrator can toggle systems without knowing their specific types, you will replace the legacy inheritance groups with modern **composition wrappers**. These will follow the exact pattern already established by the `NetworkLifecycleSystemGroup`.

You will create two new wrapper classes:

-   **TogglableInputGroup**: An `IEcsModuleSystem` registered in `SystemPhase.Input`.-   **TogglableSimulationGroup**: An `IEcsModuleSystem` registered in `SystemPhase.Simulation`.

Both wrappers will take an array of inner `IEcsModuleSystem` instances and expose an `Enabled` boolean flag:

```
[UpdateInPhase(SystemPhase.Input)]
public sealed class TogglableInputGroup : IEcsModuleSystem
{
    private readonly IEcsModuleSystem[] _innerSystems;
    public bool Enabled { get; set; } = true;

    public TogglableInputGroup(params IEcsModuleSystem[] innerSystems) 
        => _innerSystems = innerSystems;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!Enabled) return;
        foreach (var sys in _innerSystems) sys.Execute(view, deltaTime);
    }
}
```

4\. Disabling the Input Phase in Replay

Because we decided to stick with **Plan A** (recording everything, including unowned ghosts), the replay node must be hermetically sealed from both live operator intents and live network updates.

-   **Wiring the Input Group:** Inside your application composition roots (like `SimHostApp`), you will pack your live input systems into the `TogglableInputGroup`. This explicitly includes `CycloneNetworkIngressSystem` (which polls DDS network data), `DoctrineIngressSystem`, `FireProcessingSystem`, and any UI/operator intent bridges.-   **Updating the Replay Handler:** You will pass this `TogglableInputGroup` into the `ReferenceReplayLoadHandler` constructor, alongside the `TogglableSimulationGroup` and `NetworkLifecycleSystemGroup`.-   **The Replay State:** When the orchestrator issues a `PrepareReplay` command, `ReferenceReplayLoadHandler.Commit` will simply flip the switch:

**The Final Result:** The `SystemScheduler` natively compiles the topological execution graph using your `[UpdateBefore]` and `[UpdateAfter]` attributes, providing perfect out-of-the-box profiling for every individual system. During a replay, the `PlaybackSystem` safely overwrites ECS chunk memory from the `.fdprec` disk file at 60Hz, while the togglable wrapper groups cleanly short-circuit the execution of all live input, network, and simulation logic without needing to manipulate the kernel's execution topology directly.

After tge refactir, will there be anyone left using the old systems and groups?

**No, absolutely no one will be left using the old systems and groups once the refactor is complete.**

Because the legacy `ComponentSystem` and `SystemGroup` base classes are rooted at the very bottom of the dependency chain inside `Fdp.Core`, deleting them means every single project in the entire solution will be forcefully migrated to the modern `IEcsModuleSystem` architecture in order to compile.

This creates a clean, solution-wide break that sweeps through all layers of the codebase:

-   **Core Toolkits:** Every single game logic system across Physics, Combat, CarKinematics, and Behavior will natively implement the `IEcsModuleSystem` interface.-   **Host Applications:** The composition roots for the main executables, such as `SimHostCoreLogicPack` and `CgfLogicPack`, will stop passing around manual `SystemGroup` objects and instead act as pure registrars into the `ModuleHostKernel`. The `EditorSystemsModule` will also drop its direct `ComponentSystem` execution pattern.-   **Legacy Wrappers:** All the hacky, boilerplate adapters currently used to bridge the old groups into the new kernel—such as `SimulationGroupModule`, `CgfInputGroupAdapter`, and `PostSimulationGroupAdapter`—will be completely purged from `Hrot.Common.Infrastructure`.-   **Examples and Tests:** The refactor will automatically enforce the new standards on integration test harnesses (like `EditorHarness`, which currently manually spins up legacy groups for test phases) as well as demo scenarios (like `UrbanCombat`, which currently feeds `ComponentSystem[]` arrays into standalone test modules).

By ripping out the base classes from `Fdp.Core`, you guarantee a **100% unified execution topology** across the entire engine and all connected products, leaving no lingering technical debt or split-architecture patterns behind.

