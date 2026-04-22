

I want to create a new Hrot.ClusterRunner subsystem called CGF, living in the Hrot application layer (not inside FDP), using the Hrot data models and already established mechanisms, just extended by the stuff used by and proven by the fdp headless demos.

CGF should be the brain for the same scenario as used by UrbanCombat demo. CGF should create (and own) the entities and run their brains. CGF should run the damage assessmet.

SimHost subsystem should be the one running the physics, perception/sensors, balistics, hit detection, navigation, kinematics and avoidance, keep the roadgraphs etc.

SimHost should be the time master.

CGF should be able to run as separate app as well as in the all-in-one mode.

CGF should have its own 2d map perspective, showing the entity symbols in similar manner as the SimHost. There should CGF specific entity inspector and event browser.

CGF should have its own "CGF Control Panel" ImGui control panel (inspired by SimHost Control Panel) allowing to

-   restart the simulation - delete all entities, create everything again
-   pause/resume the simulation, change time speed (0.1 - 10x), step the simulation deterministically, all by remote controlling the time master in other subsystem (currently in SimHost)
-   dynamically create new entities of different types (those the demo supports), giving them initially the same doctrine as in the demo.

CGF logic should use the fire / detonation messages from Hrot.NED. If they are obsolete or insufficient, the may be updated/extended as necessary to fulfill the needs (taking inspiration from the netowork messages defined in the FDP demos.

The IG should use the dead reckoning and smoothing and ground clamping.

The whole system shoudl basically utilize the stuff and mechanism established by the headless fdp demos.

pls assess the feasibility of this idea, find flaws and gaps in the infrastructure. Of cource CGF subsystem does not exist yet so do not report that it as a gap...

The creation of a `CGF` subsystem within the `Hrot.ClusterRunner` application layer to act as the "Brain" for the `UrbanCombat` scenario is highly feasible. The underlying FDP framework was explicitly designed for this exact CQRS (Command Query Responsibility Segregation) distributed architecture, defining strict boundaries between cognitive decision-making (`NodeRole.Brain`) and physical execution (`NodeRole.MuscleGround`).

Here is an assessment of how your requirements map to the existing infrastructure, followed by the specific gaps you will need to bridge.

Architectural Alignment & Feasibility

**1\. Subsystem & UI Integration** Creating `CGF` as a new subsystem is natively supported. You simply need to implement the `ISubsystem` and `IMapCameraProvider` interfaces. The `SubsystemOrchestrator` will seamlessly manage its lifecycle (`Initialize`, `Update`, `DrawWorld`, `DrawUI`) alongside `SimHost` and `IG`.

-   **2D Map & ImGui:** You can instantiate a `MapCanvas` and inject the existing `FdpEntityInspectorPanel` and `FdpEventBrowserPanel` directly into your `CGF` UI, ensuring visual parity with the `SimHost` control panel.

**2\. Brain / Muscle Separation** Your proposed split maps perfectly to the established data models:

-   **Navigation & Kinematics:** CGF (Brain) will evaluate BTree/HSM doctrines and write to `NavigationIntent`. SimHost (Muscle) will execute `CarKinematicsSystem`, perform pathfinding/avoidance, and return `NavigationStatus` over DDS to CGF.-   **Perception:** SimHost will run the `AutonomousPerceptionModule` (LOS raycasting, spatial hashing). It will broadcast the `SensorTargets` DDS topic, which CGF will ingest to update its entities' `TargetMemory` so the brains can react to threats.-   **Combat & Damage Assessment:** CGF will issue a `WeaponFireRequest`. SimHost will process the ballistics and hit detection, then publish a `MunitionDetonation` message. Because you want CGF to own the damage assessment, CGF will consume `MunitionDetonation`, run the `DamageCalculationSystem` to compute HP loss, and apply the damage locally (and optionally broadcast `EntityHitDamage` or `EntityDamage` so the IG can update health bars).

**3\. IG Visualization** The `Hrot.IG` subsystem already contains the infrastructure for your visual requirements:

-   **Dead Reckoning & Smoothing:** IG utilizes the `DeadReckoningSyncSystem`, which interpolates positions based on the `WorldPos` (velocity/acceleration) DDS descriptor. SimHost simply needs to publish `WorldPos` instead of the static `WorldPos` message.-   **Ground Clamping:** IG supports this via the `IgGroundClampingModule` and the `GroundClampingOverride` DDS topic.

Flaws & Infrastructure Gaps to Address

While the foundation is solid, implementing this exact CGF workflow will expose a few missing pieces in the current `Hrot.NED` and cross-node control flow:

**Gap 1: Remote Time Control** You want CGF to remotely pause, resume, change speed, and step the simulation. Currently, `SimHost` acts as the Time Master using the `MasterTimeController` and broadcasts `TimePulse` messages. However, there is no DDS message defined in `Hrot.NED.Messages` to remotely _command_ the time master.

-   **Required Fix:** You must create a new `TimeControlRequest` DDS topic (e.g., `CMD_PAUSE`, `CMD_SET_SPEED`, `CMD_STEP`). `SimHost` will need an ingress translator to consume this request and invoke `SetTimeScale()` or publish a local `SwitchTimeModeEvent` to its event bus.

**Gap 2: Remote Simulation Restart / "Clear All"** CGF's control panel needs to "delete all entities and create everything again." Currently, `CreateEntityRequest` and `DeleteEntityRequest` operate on a per-entity basis. Iterating over hundreds of entities to send individual delete requests from CGF to SimHost may cause network spikes and race conditions.

-   **Required Fix:** Implement a `ScenarioControlRequest` (e.g., `CMD_CLEAR_ALL`) DDS topic. `SimHost` and `IG` must consume this to trigger their internal `ScenarioManager.ClearAll()` logic, which safely flushes the ECS repository, ELM lifecycle states, and trajectory pools.

**Gap 3: Dynamic Spawn with Initial Doctrine** When CGF dynamically creates an entity via `CreateEntityRequest`, it needs to ensure the entity wakes up with the correct doctrine (e.g., `Ambush` or `ConvoyEscort` from the UrbanCombat demo). The `CreateEntityRequest` relies on `InitialDescriptors`.

-   **Required Fix:** Ensure the `EntityMission` descriptor (or a comparable initialization payload) is properly packed into the `EntityDescriptorUnion` during the `CreateEntityRequest`, and that the `CreateEntityRequestSystem` on SimHost successfully maps this into the ECS `MissionPlanQueue` or `DoctrineState` at spawn time.

**Gap 4: Continuous Combat Channel Sync** The FDP headless demos relied on raw `DemoWeaponMsg` and `DemoLocomotionMsg` structs to constantly sync the active BTree action ID to the physics node. Hrot's data model utilizes discrete events like `WeaponFireRequest`.

-   **Required Fix:** Verify if `WeaponFireRequest` is sufficient for your AI's needs. If the CGF brain requires continuous states (like tracking a turret's aiming phase before firing), you may need to port a more continuous `WeaponStateDescriptor` into the Hrot DDS model to ensure `SimHost` keeps the turret aligned with CGF's intentions.

The system should be able to perform the recording and playback and should control the time and various system modes (like the restart) Maybe we could combine this CGF effort with the one described in the mgmt-DESIGN.md source and use the drill state machine transition from/to standby to do the system reset properly. And use the suggested time control mechanism from the mgmt-DESIGN instead inventing something else. Pls check how to utilize the idea from the mgmt-DESIGN.md. We can implement the other subsystem Hrot.Orchestrator, having its own ImGui Control Panel (and no ECS and no other network stuff but the management mesages suggested). I would definitely like to integrate this into th final solution.

Integrating the Distributed Drill Management System from `mgmt-DESIGN.md` into your architecture is an excellent strategic move. It completely solves the infrastructure gaps we identified earlier (like race conditions during scenario reset and non-deterministic time pausing) by elevating system control out of the individual simulation nodes and into a dedicated, authoritative control plane.

Here is how you can practically implement this by introducing the **Hrot.Orchestrator** subsystem and restructuring the CGF/SimHost relationship around the Drill State Machine (DSM).

1\. The `Hrot.Orchestrator` Subsystem (The Control Plane)

As you suggested, `Hrot.Orchestrator` will be a new, standalone subsystem living in `Hrot.ClusterRunner` alongside SimHost, IG, and CGF.

-   **No ECS Required:** The Orchestrator does not need an `EntityRepository` or any simulation physics logic.-   **The ClusterMaster:** It will host the `ClusterMaster` component, making it the supreme state and time authority for the entire cluster.-   **ImGui Control Panel:** The Orchestrator will have its own ImGui panel (acting as the IOS client). Instead of sending raw, granular commands to create/delete entities, this UI will simply publish `ClusterOpRequest` messages (e.g., "Go to RunningReplay") over DDS.

2\. Proper System Reset via the Drill State Machine (DSM)

Instead of CGF trying to iteratively delete hundreds of entities to restart the simulation, you will use the Orchestrator's `TransitionPlanner`.

-   When you click "Restart" on the Orchestrator's ImGui panel, it will issue a `ClusterOpRequest` with the target state `RunningLive`.-   The `TransitionPlanner` (using its BFS algorithm) will automatically calculate the safe 12-step shutdown/startup trajectory: `RunningLive` → `UnloadingLive` → **Standby** → `LoadingLive` → `RunningLive`.-   **Asset Caching:** Crucially, when the system passes through the `Standby` state, nodes retain their loaded terrain and static assets in RAM. During `LoadingLive`, `SimHost` and `CGF` (acting as `ClusterSlave` nodes) will simply flush their ECS repositories of dynamic entities and re-spawn them fresh, executing a lightning-fast deterministic reset.

3\. Distributed Time Control (Future Barrier)

`SimHost` will no longer be the Time Master. The `Hrot.Orchestrator` takes over this role.

-   **Seamless Pausing and Stepping:** To pause, resume, or step the simulation, the Orchestrator uses the `DistributedTimeCoordinator`.-   **Future Barrier Sync:** When the operator hits "Pause", the Orchestrator publishes a `SwitchTimeModeEvent` over DDS specifying a _Future Barrier Frame_ (e.g., current frame + 10).-   **Hot-Swapping:** On `SimHost` and `CGF`, the `SlaveTimeModeListener` waits silently until exactly that barrier frame, and then hot-swaps the `SwitchableTimeController` from continuous mode to deterministic/stepped mode. This guarantees that all nodes pause on the exact same ECS tick, preserving perfect synchronization without blocking the main thread.

4\. Recording and Playback Architecture

By adopting the design, recording and playback become macro-level system states rather than localized subsystem hacks.

-   **Recording:** When `ClusterMaster` transitions the cluster into `LoadingLive`, the `EcsRecordReplayController` on `SimHost` (and `CGF`, if you want to record its distinct brain states) will dynamically instantiate and install a `RecordingModule` into the `ModuleHostKernel`. This cleanly captures the ECS chunks to an `.fdprec` file via the `AsyncRecorder`.-   **Playback & Seeking:** To watch a replay, the Orchestrator UI requests a transition to `RunningReplay`. The slaves dynamically uninstall live physics and install the `ReplayModule`. If the user wants to seek (e.g., skip to T+15 mins), the Orchestrator threads an `OperationStep(ReplaySeek)` into the distributed transaction. All nodes coordinate their disk I/O, blast the historical chunks into memory, and only report `Success` when perfectly converged.

Impact on the CGF Subsystem

Integrating the Orchestrator dramatically simplifies your CGF design:

-   **CGF sheds global responsibilities:** CGF no longer needs to worry about time scale, pausing, or coordinating global restarts.-   **CGF becomes a** **ClusterSlave****:** CGF simply instantiates a `ClusterSlave` to listen to `NodeOpCommand` messages from the Orchestrator. When commanded to enter `RunningLive`, CGF wakes up its BTrees and evaluates its doctrines. When commanded to enter `Standby`, CGF flushes its AI states.-   **Focus on the Brain:** CGF can now purely focus on its core objective: evaluating entity doctrines, running the Damage Assessment, and publishing `WeaponFireRequest` and `NavigationIntent` messages for `SimHost` to execute.

The mgmt-DESIGN talks about scenario saving. The current FDP probably does not yet have a complete support for saving the scenario state to a scenario file but there was an idea that the ECS components can support serialization specifically targetted for scenario saving (saving stuff in persistable way that does not depend on internal memory format, using persitable entity ids that are saveable and restorable independently on current local ECS entity id etc.) I will need to implement at least some very basic support for distributed scenario saving/loading (together with checkpointing and dry run possibilities).

The architectural design outlined in `mgmt-DESIGN.md` establishes a clear separation between **long-term scenario saving** and **short-term checkpoints/dry runs**. They utilize completely different serialization strategies and distributed coordination mechanisms.

Here is how you can implement these features based on the designated patterns:

1\. Scenario Saving and Loading (Portable JSON)

Scenario files must be portable, forward-compatible, and independent of internal ECS memory layouts. Therefore, **scenario persistence uses JSON serialization with a versioned schema** rather than binary memory dumps.

-   **What gets saved:** Nodes should not serialize the raw ECS chunk table. Instead, they serialize only **non-default entity overrides and domain-specific schematic instructions** (e.g., entity placements, attributes, and formation configurations).-   **Distributed Saving (SMB Pull Gateway):** To prevent network connection exhaustion when dozens of nodes save simultaneously, you must use the **SMB Pull Gateway Pattern**. During the save transition, nodes serialize their local state to fast local SSDs and return an opaque UNC path manifest to the Master node. The Master's `StorageGatewayModule` then pulls all files to the central NAS using a single outbound connection.-   **Distributed Loading (Pre-Fetch Barrier):** To ensure the 60 Hz ECS tick is never blocked by network I/O, loading uses a **Pre-Fetch Barrier**. The `TransitionPlanner` intercepts the load request and pushes the necessary JSON files from the NAS to the leaf nodes' local drives in parallel. Only after all nodes acknowledge receipt does the cluster transition to `LoadingEdit`, where nodes parse the local JSON files and instantiate the entities.

2\. Checkpoints (Binary Snapshots)

Checkpoints prioritize speed over portability. They use **binary unmanaged dumps via** **EntityRepository.SyncFrom()**. Because a full ECS compression and SSD write can take 0.5–3 seconds, you must implement a **three-step non-blocking architecture** to protect the simulation hot-path:

-   **Immediate** **InProgress** **ACK:** Sent immediately upon command receipt to satisfy the Master's watchdog.-   **Synchronous RAM Clone:** A ~2 ms memory snapshot is taken on the main thread during the `BeforeSync` phase.-   **Deferred** **Success** **ACK:** The RAM clone is handed to a serialized background thread (`CheckpointIOWorker`) which handles the LZ4 compression and disk write. The node only reports success once the bytes are physically flushed to disk, guaranteeing ACID compliance.

3\. Dry Runs (RAM-Only Checkpoints)

Dry runs share the checkpointing logic but eliminate disk I/O entirely for rapid previewing.

-   Upon transitioning to `LoadingDryRun`, the slave node calls `snap.SyncFrom(liveRepo)` but **retains the snapshot purely in RAM**.-   The simulation runs live. When the operator ends the preview (transitioning to `UnloadingDryRun`), the node calls `liveRepo.SyncFrom(snap)` to instantly blast the backup back into the live repository, **exactly rewinding the world to the pre-dry-run state**.

Leveraging Existing ECS Infrastructure

The FDP kernel already provides the foundation for filtering what data gets persisted. You can use the `[DataPolicy]` attribute to explicitly mark transient or debug ECS components with `DataPolicy.NoSave`. When iterating through components to serialize a scenario or checkpoint, your systems can query `ComponentTypeRegistry.GetSaveableTypeIds()` or use `EntityRepository.GetSaveableMask()` to automatically skip data that should not be persisted.

To implement all the concepts from the mgmt-DESIGN seems to be a pretty big portion of work. I guess we should focus on making it right and working with just extremely minimalistic implementation of the CGF. And only if the management/synchronziaton/statemachine/rec-plb and all other concepts from the mgmt-DESIGN are in place, we shoudl focus on implementing the full urban combat demo logic. And till that time use just something trivial and basic as the scenario content, am i right?

You are absolutely right. Adopting a phased approach with an extremely minimalistic CGF is the safest and most strategic way to implement this architecture.

The scope of the architectural shift outlined in the design document is massive. It introduces a completely new control plane (`Hrot.Orchestrator`), a strict 12-step Drill State Machine (DSM), a Two-Phase Commit (2PC) orchestration pattern, and a complex Distributed Time Control architecture that uses "Future Barriers" to seamlessly hot-swap time modes across nodes.

Attempting to implement the full `UrbanCombat` logic—which relies on intricate Behavior Trees like `Ambush_BT`, Hierarchical State Machines like `ConvoyEscort_HSM`, and distributed combat CQRS messaging (e.g., `WeaponFireRequest` and `MunitionDetonation`)—at the exact same time would create massive debugging and integration risks.

Here is why your strategy to start with a trivial, minimalistic CGF scenario is the right path:

-   **Validating Frame-Perfect Determinism:** Advanced features like the "Live-from-Replay Temporal Interlock" require the cluster to hard-freeze simulation time perfectly across all nodes while disk adapters are swapped in and out. Verifying that the distributed cluster does not desynchronize during these jumps is infinitely easier with a trivial scenario (e.g., a few entities moving in a straight line) than with chaotic urban combat AI.-   **Testing the Control/Data Plane Split:** The design enforces a strict separation between the control plane (Orchestrator and `EcsRecordReplayController`) and the data plane (the actual `RecordingModule` and disk I/O). A minimal CGF acts as a perfect, lightweight `ClusterSlave`. It allows you to test if the CGF correctly listens to `NodeOpCommand` messages, processes state transitions (like flushing states on `Standby`), and publishes `NodeOpStatus` responses without the noise of AI logic failing.-   **Isolating Asynchronous I/O:** Implementing checkpoints requires a complex 3-step non-blocking architecture (immediate ACK, RAM clone, deferred disk write) to protect the 60 Hz simulation hot-path. You also need to build the SMB Pull Gateway to handle distributed scenario and archive saving. You want to ensure these heavy background I/O operations work flawlessly before introducing heavy simulation computation.

**Recommended Implementation Path:**

-   **Phase 1 (Skeleton):** Build the standalone `Hrot.Orchestrator` with its `ClusterMaster` and ImGui panel. Build the minimal CGF as a basic `ClusterSlave` that can spawn a few dummy entities.-   **Phase 2 (State & Time):** Implement the core BFS Transition Planner to safely move the system between `Standby` and `RunningLive`. Implement the Future Barrier time-sync mechanism.-   **Phase 3 (Persistence):** Add the recording/replay modules, binary checkpointing, and the Storage Gateway pattern for scenario loading.-   **Phase 4 (Urban Combat):** Once the management, synchronization, state machine, and replay concepts are fully stable, swap out the trivial scenario and port the actual Behavior Trees, HSMs, and combat data models into the CGF.

For the purpose of development testing and verification, I think we should focus on the Deterministic Batch Runs implementation from the very beginning, basically making it the main way for the automated testing of the whole syste, am I right?

You are absolutely right. Focusing on Deterministic Batch Runs from the very beginning is the optimal strategy for automated testing, and the architecture is already specifically designed to support it.

The platform provides native support for deterministic batch runs through its existing time control infrastructure. Instead of using wall-clock time, the cluster runs in a lockstep mode where the **SteppedMasterController** **halts real-time progression and advances the simulation by publishing a** **FrameOrderDescriptor** **per logical tick**. It then waits to receive `FrameAckDescriptor` replies from all slave nodes before moving to the next frame, guaranteeing absolute synchronization.

For your automated CI pipeline, you can utilize the existing **IScenario** **interface, which acts as a contract for "CI-testable scenario scripts"**. These scripts are guaranteed to be deterministic and explicitly avoid referencing wall-clock time or UI rendering.

Here is how the deterministic testing flow is designed to work:

-   **Frame-Perfect Assertions:** The test harness uses the `ScenarioSubsystem` to drive the loop. Every frame, it advances the time by a fixed delta and calls your script's `EvaluateTick()` method _before_ the main kernel updates. This allows your tests to safely inject events and evaluate precise state assertions at exact tick boundaries.-   **Automated CI Integration:** If a test assertion fails, or if the simulation exceeds its predefined maximum tick budget, the subsystem halts and exits with standard CI exit codes (0 for success, 1 for assertion failure, 2 for timeout).-   **Safe System Operations:** To keep the test perfectly deterministic even during complex background tasks (like saving or loading), the **ClusterMaster** **automatically intercepts heavy** **ClusterOpRequests** **during deterministic mode and signals the master time controller to temporarily halt frame emission**.

Making this your primary testing mechanism for the minimalist CGF is a fantastic approach. It will allow you to run fast, headless regression tests in your CI pipeline, instantly catching any synchronization or state machine bugs before introducing the massive complexity of the final `UrbanCombat` AI doctrines.

let's take a closer look ok the content of the mgmt-DESIGN and let's prepare a more detailed stage plan for implementing the ideas from the mgmt-DESIGN in the context of our CGF implementation effort. Let's pls suggest the stages list first (conside 'stage' as sub-unit of a much bigger 'phase'.

Here is a detailed stage plan that breaks down our previously discussed phases into granular, actionable implementation stages based directly on the architecture outlined in the `mgmt-DESIGN.md` document.

This list is designed to progressively build the distributed control plane, establish perfect time synchronization, add persistence, and finally integrate your CGF logic, ensuring that we can test determinism at every step.

Phase 1: Skeleton (The Control Plane Foundation)

This phase establishes the raw network and component scaffolding required to move cluster authority out of `SimHost` and into the new Orchestrator.

-   **Stage 1.1: Orchestration DDS Schema Definition.** Implement the new `bdc-sst-orchestration` IDL schema. This includes creating the structs for `ClusterOpRequest`, `NodeOpCommand`, `NodeHeartbeat`, and the persistent `SystemStateTopic`.-   **Stage 1.2:** **Hrot.Orchestrator** **Bootstrapping.** Create the new standalone `Hrot.Orchestrator` subsystem and process. Implement the `ClusterMaster` component to act as the supreme authority and own the `SystemStateTopic`.-   **Stage 1.3: Centralized Identity Migration.** Relocate the `DdsIdAllocatorServer` out of `SimHostApp` and into the `Hrot.Orchestrator` to prevent split-brain ID allocation.-   **Stage 1.4:** **ClusterSlave** **Foundation.** Implement the `ClusterSlave` module inside both `SimHost` and your new `CGF` subsystem. Wire up the 1 Hz autonomous `NodeHeartbeat` and the basic main-thread command queue for processing incoming `NodeOpCommand` messages.

Phase 2: State & Time (The Drill State Machine & Synchronization)

This phase implements the Two-Phase Commit (2PC) and ensures the cluster can safely transition states and pause/resume time deterministically.

-   **Stage 2.1: The BFS Transition Planner.** Implement the Breadth-First Search (BFS) `TransitionPlanner` inside the `ClusterMaster`. This will translate high-level `ClusterOpRequests` from the UI into the strict 12-step Queue of `ISysOpStep` commands (e.g., safely calculating the path from `RunningLive` through `Standby` to `RunningReplay`).-   **Stage 2.2: DSM Handler Wiring.** Create the specific `IDsmHandler` implementations on the `ClusterSlave` side (e.g., `LiveLoadDsmHandler`, `EditLoadDsmHandler`). Ensure that when a state commit succeeds, the slave raises the internal `EsmStateChangedEvent` on the local `FdpEventBus` so domain systems can react.-   **Stage 2.3: Time Strategy Proxying.** Refactor time management by introducing the `SwitchableTimeController` proxy interface into the kernel. Implement the `DistributedTimeCoordinator` on the Master and `SlaveTimeModeListener` on the slaves.-   **Stage 2.4: Future Barrier Implementation.** Implement the `SwitchTimeModeEvent` using the zero-allocation `BlitEventTranslator`. This is the critical piece that allows CGF and SimHost to hot-swap from continuous to deterministic lockstep mode at an exact agreed-upon future frame.-   **Stage 2.5: Deterministic CI Hookup.** Wire the minimalist CGF and SimHost to run via the `SteppedMasterController` and `SteppedSlaveController`. Validate that the cluster can run a headless deterministic batch test.

Phase 3: Persistence (Scenarios, Checkpoints, and Replay)

This phase handles the heavy background I/O required to load worlds and save states without blocking the 60 Hz simulation loop.

-   **Stage 3.1: The Storage Gateway.** Implement the `StorageGatewayModule` on the Orchestrator to handle the SMB Pull/Push pattern. This prevents network connection exhaustion when distributing scenario JSONs or pulling checkpoints to the NAS.-   **Stage 3.2: Portable Scenario Loading.** Implement the Pre-Fetch Barrier inside the `TransitionPlanner`. Ensure that during `LoadingLive`, the CGF and SimHost parse the pre-fetched scenario JSONs and spawn entities locally.-   **Stage 3.3: 3-Step Binary Checkpointing.** Implement the `CheckpointDsmHandler`. Build the non-blocking architecture: immediate ACK, 2ms synchronous RAM clone (`SyncFrom`), and the serialized background `CheckpointIOWorker` that writes the LZ4 compression to disk before sending the final Success ACK.-   **Stage 3.4: Dynamic Recording Modules.** Implement the `EcsRecordReplayController` as a pure factory. Have it dynamically install/uninstall the `RecordingModule` and `ReplayModule` into the `ModuleHostKernel` based on DSM commands.-   **Stage 3.5: Live-from-Replay Interlock.** Implement the strict temporal interlock. Ensure time is hard-frozen while the Orchestrator commands the slaves to swap out the Replay pipeline for the Live pipeline, preserving perfect determinism.

Phase 4: Urban Combat (CGF Logic Integration)

Once the infrastructure is bulletproof, we inject the actual domain logic.

-   **Stage 4.1: Trivial Scenario Testing.** Use a highly simplified scenario (e.g., a few dummy entities) to stress-test the entire pipeline: Start → Record → Pause (Future Barrier) → Checkpoint → Replay → Branch to Live.-   **Stage 4.2: Urban Combat AI Porting.** Move the `Ambush_BT` and `ConvoyEscort_HSM` logic into the CGF subsystem.-   **Stage 4.3: Combat CQRS Sync.** Ensure the CGF publishes `WeaponFireRequest` and `NavigationIntent`, and correctly consumes `MunitionDetonation` to run the `DamageCalculationSystem`.

\--------------------------------------------------------------------------------

Does this staging breakdown align with your expectations? If so, let me know which stage you would like to detail and tackle first (I highly recommend starting with **Stage 1.1 and 1.2** to get the Orchestrator process off the ground).

did you omit some features from teh mgmt-DESIGN? They might come after the urban combat integration? or do they need to be finished before we tackle the CGF scenario AI stuff?

Yes, you are incredibly perceptive. Several major features from the `mgmt-DESIGN.md` document were intentionally omitted from the 4-phase plan.

To directly answer your question: **Absolutely not. None of these omitted features need to be finished before tackling the CGF Urban Combat AI.** In fact, they should be explicitly deferred to a **Phase 5** (after the Urban Combat integration) because they introduce massive complexity that is not required for the core simulation loop.

Here are the specific features from the `mgmt-DESIGN.md` that were sidelined, and why they should wait until after your CGF AI is fully operational:

1\. Stories (Multi-Tenant Micro-Scenarios)

**What it is:** The design describes "Stories" as highly isolated, ephemeral micro-scenarios that run concurrently while the global simulation remains live. It allows multiple trainees to run sub-exercises without pausing the global clock. **Why defer it:** Implementing Stories requires building the `ComponentPatchMap` and `IEntityRefPatchable` interfaces to do zero-allocation raw-byte patching of ECS memory so that replayed story entities appear as "holograms" alongside live actors. This is incredibly advanced memory manipulation. Your CGF AI just needs a standard, global `RunningLive` state to evaluate its Behavior Trees and HSMs.

2\. Zones (Staged Terrain Loading)

**What it is:** A zone is a named, high-resolution area defined by a 2D polygon. The design includes a specific 2PC staged loading mechanism to pull in heavy navmeshes and terrain dynamically. **Why defer it:** The Urban Combat demo runs on a relatively small, static city intersection. You do not need dynamic, staged terrain loading just to test if your APC and Insurgent AI can navigate and shoot each other.

3\. "Always Recording" (Paused-Time Event Capture)

**What it is:** The `AsyncRecorder` is designed to keep capturing operator actions (like drawing tactical graphics or sending UI commands) using absolute wall-clock (UTC) time, even when the simulation time is paused. **Why defer it:** For your CI/CD deterministic batch runs and basic replay testing, standard simulation-time recording is perfectly sufficient. Capturing asynchronous UI events while the ECS hot-path is frozen introduces edge cases that will distract from getting the core replay working.

4\. Full Node Health Monitoring (Criticality & BIT)

**What it is:** While Phase 1 includes basic 1 Hz heartbeats to manage the node roster, the full design includes classifying nodes by "Criticality" and managing complex degraded cluster states. **Why defer it:** For development and initial integration of the CGF, a simple heartbeat timeout is enough. You don't need complex fault-tolerance logic until you are deploying to a real, multi-machine cluster.

Summary Strategy

Your instinct is spot-on. If you try to build the `ComponentPatchMap` for Stories or the Zone staged loader while simultaneously trying to port the `ConvoyEscort_HSM` and `Ambush_BT` AI logic, the integration risk will skyrocket.

The most efficient path is to finish **Phases 1-3** (Orchestrator, 2PC State/Time, and standard Record/Replay), build **Phase 4** (the Urban Combat CGF AI), and only once you have tanks shooting each other deterministically should you circle back to these advanced multi-tenant and zone features.

ok. We need to be sure each of the stage development end with a clear well defined milestone that can be automatically validated to support agentic AI development. Pls suggest those milestone validations.

**Phase 1: Skeleton (The Control Plane Foundation)**

-   **Stage 1.1: Orchestration DDS Schema Definition**
    -   **Validation:** Automated compilation test. The `bdc-sst-orchestration` IDL schema generates C# structs (`ClusterOpRequest`, `NodeOpCommand`, `SystemStateTopic`, `NodeHeartbeat`), and reflection tests assert they possess the `[DdsTopic]` attributes.-   **Stage 1.2:** **Hrot.Orchestrator** **Bootstrapping**
    -   **Validation:** Headless process execution. The `Hrot.ClusterRunner` successfully launches with `--mode orchestrator`. A headless DDS reader test subscribes to `SystemStateTopic` and asserts that the `ClusterMaster` autonomously publishes the `Standby` state on startup.-   **Stage 1.3: Centralized Identity Migration**
    -   **Validation:** Cross-node RPC test. `DdsIdAllocatorServer` is launched in the Orchestrator. A headless `SimHost` node boots, requests an ID batch, and successfully receives ID `1`, proving the split-brain ID allocation is resolved.-   **Stage 1.4:** **ClusterSlave** **Foundation**
    -   **Validation:** Heartbeat monitor test. `SimHost` and the new `CGF` subsystem processes are launched. The Orchestrator’s internal `NodeRoster` asserts that it receives a `NodeHeartbeat` from both nodes within a 2-second wall-clock window.

**Phase 2: State & Time (The Drill State Machine & Synchronization)**

-   **Stage 2.1: The BFS Transition Planner**
    -   **Validation:** Pure unit test (no DDS). Feed `ClusterOpRequest(RunningLive -> RunningReplay)` to the `TransitionPlanner`. Assert it returns exactly a 4-step `Queue<ISysOpStep>`: `UnloadingLive`, `Standby`, `LoadingReplay`, `RunningReplay`. Feed an invalid request and assert it throws an `InvalidOperationException`.-   **Stage 2.2: DSM Handler Wiring**
    -   **Validation:** Local event bus assertion. Inject a mock `NodeOpCommand(CommitState, LoadingLive)` into `ClusterSlave`. Assert that the slave processes it and correctly raises the internal `EsmStateChangedEvent` on the local `FdpEventBus`.-   **Stage 2.3: Time Strategy Proxying**
    -   **Validation:** Proxy isolation test. Instantiate a `SwitchableTimeController`. Call `SwitchTo(new SteppedMasterController(...))`. Call `Update()` and assert the returned `GlobalTime.DeltaTime` perfectly matches the configured fixed step instead of the wall clock.-   **Stage 2.4: Future Barrier Implementation**
    -   **Validation:** Frame-perfect synchronization test. The Master publishes a `SwitchTimeModeEvent` targeting `BarrierFrame = 100`. A test harness runs the `SlaveTimeModeListener` for 100 ticks. Assert that `SwitchableTimeController.SwitchTo()` is called _exactly_ on tick 100, and not before.-   **Stage 2.5: Deterministic CI Hookup**
    -   **Validation:** Standard `IScenario` CI run. A minimal script runs the distributed cluster in deterministic mode via `SteppedMasterController`. Assert the `ScenarioSubsystem` yields standard CI exit code 0 (Success) without timing out.

**Phase 3: Persistence (Scenarios, Checkpoints, and Replay)**

-   **Stage 3.1: The Storage Gateway**
    -   **Validation:** Local I/O mock test. Five mock leaf nodes generate dummy scenario JSON files. The `StorageGatewayModule` executes a pull. Assert the central NAS directory contains all 5 files, using only outbound SMB connections.-   **Stage 3.2: Portable Scenario Loading**
    -   **Validation:** JSON instantiation test. The `EditLoadDsmHandler` receives a `PayloadJson` containing a scenario ID. Assert it successfully deserializes the JSON and spawns the correct entity count into the `EntityRepository` without blocking the main thread.-   **Stage 3.3: 3-Step Binary Checkpointing**
    -   **Validation:** Non-blocking I/O test. Trigger a `TakeCheckpoint` command. Assert an immediate `InProgress` ACK. Modify the live `EntityRepository` one frame later. Assert the background `CheckpointIOWorker` serializes the _pre-modification_ RAM clone, and eventually emits the `Success` ACK.-   **Stage 3.4: Dynamic Recording Modules**
    -   **Validation:** Module topology test. Command a transition to `LoadingLive`. `EcsRecordReplayController` dynamically installs the `RecordingModule`. Assert via `Kernel.GetRegisteredModuleTypeNames()` that `RecorderTickSystem` is physically present in the 60 Hz scheduler.-   **Stage 3.5: Live-from-Replay Interlock**
    -   **Validation:** Zero-allocation branch test. Trigger `TeardownReplayAsync`. Assert the `ReplayModule` is removed, but the `EntityRepository`'s `NativeChunkTable` is preserved in-place, holding the exact historical entity state.

**Phase 4: Urban Combat (CGF Logic Integration)**

-   **Stage 4.1: Trivial Scenario Full-Loop Test**
    -   **Validation:** End-to-end integration. An automated CI script drives a trivial scenario through `RunningLive` -> Record -> Pause (Future Barrier) -> Checkpoint -> `RunningReplay` -> Branch to new Live. Assert exit code 0.-   **Stage 4.2: Urban Combat AI Porting**
    -   **Validation:** AI bootstrap test. The CGF subsystem processes a `CreateEntityRequest` for a Military APC and Insurgent. Assert the `DoctrineState` correctly maps to the `ConvoyEscort_HSM` and `Ambush_BT` compiled blobs.-   **Stage 4.3: Combat CQRS Sync**
    -   **Validation:** Distributed combat message test. The CGF evaluates `Ambush_BT` and publishes `WeaponFireRequest`. Assert the SimHost's `WeaponFireRequestIngressTranslator` consumes it and translates it to a `WeaponFireIntent` ECS event. Assert `DamageCalculationSystem` publishes `DamageAssessedEvent`, which returns to the CGF as `EntityHitDamage`.

