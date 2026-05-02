
i would like to restructure the project according to the "Hrot editor" source. That means creating logic packs and translator packs; each pack can be a modulehost module or a set of modules. is the intention clean? ask questions what needs to be clarified before we proceed to desinign the packs.

The intention to restructure the project into "logic packs" and "translator packs" is very clean and aligns perfectly with the architectural vision outlined for the Hrot editor. By decoupling the core simulation logic from the network transport, the engine can default to running without a network layer, allowing network capabilities to be added simply as pluggable modules.

This approach ensures that the FDP-based SimHost core pack interacts exclusively with the internal shared ECS and event bus. Because the core pack will not integrate network translators directly, the brain (CGF) and muscle (SimHost) can communicate their actuator intents and environment queries via internal shared state, while translator packs can be swapped in to convert these internal ECS states and events into network messages when running in a distributed cluster. Ultimately, this allows an "all-in-one" editor to switch seamlessly between an internal SimHost logic pack and an external, networked SimHost represented purely by a translator pack.

Before we proceed to designing the specific packs, please clarify the following architectural and implementation details:

**1\. Granularity and Project Structure:** You mentioned each pack can be a `ModuleHost` module or a set of modules. Should these "packs" map 1:1 to dedicated C# projects (assemblies), or will they be conceptual groupings (e.g., multiple `IEcsModule` classes) living inside broader shared projects like `Hrot.SimHost` or `FDP.Toolkits`?

**2\. Translator Pack Grouping Strategy:** The design notes specify breaking translator packs down by data category: entity states/events, actuator intents, service queries/responses, and network ID allocation. Currently, the codebase groups translators largely by node role (e.g., `BrainPerceptionTranslatorPack`, `KinematicTranslatorPack`). Do you want to refactor the existing translator collections to strictly follow these data-category boundaries instead of node-role boundaries?

**3\. Protocol Support (HROT vs. Bagira):** The source notes mention both an "HROT demo" using an independent, simplified NED DDS data model, and "Bagira" demos using the BDC SST DDS data model. Are we designing the translator packs exclusively for the HROT NED messages right now, or do we need to design an interface/structure that supports swapping between NED translator packs and BDC SST translator packs?

**4\. The Scenario Editor Pack:** The Hrot editor requires a specific "ScenarioEditor logic pack" responsible for ImGui UI, Vis2d map, and manipulation of entities, missions, routes, and areas. Should this be extracted into a completely standalone module so it can be plugged into either the "All-in-one" editor or the networked ExCon subsystem, or will it live specifically inside the ExCon project?

**5\. Brain/Muscle ECS Boundaries:** The architecture requires that actuator intents and queries be communicated strictly through the shared ECS and event bus between the brain and muscles. Are there any existing direct method calls or tightly coupled logic between the CGF (brain) and SimHost (muscle) that we need to audit and convert into ECS intent components before isolating them into their respective logic packs?

1.  conceptual grouping
2.  not sure; should be driven by the need for flexibility in defining node roles by assembling a node (or more precisely a runner subsystem - for development and testing purposes we need to be able to run multiple subsystems in one single process) from logic packs and translator packs.
3.  Now for Hrot only. Bagira stuff will be added later. Swapping should work simply by installing different set of translators (Bagira translator instead of Hrot ones)
4.  The editor should be extractable to stnedalone module. For flexibility.
5.  I don't know. Very likely there are some parts violating CQRS rules. You will need to audit and convert.

Thank you for the clarification. This gives us a very clear roadmap for refactoring the architecture. Based on your answers and the design notes for the Hrot editor, here is the concrete plan for how we will restructure the project into pluggable "Packs," ensuring flexibility, strict CQRS boundaries, and a clean path for future protocol swapping.

1\. Conceptual Grouping & Flexible Node Assembly

Since you need the flexibility to run multiple subsystems (SimHost, IG, ExCon, CGF) within a single process for testing, we will implement the "Packs" as conceptual groupings—specifically, utilizing the `IEcsModule` interface and factory patterns.

Instead of hardcoding system registration, we will formalize the approach currently hinted at in `NodeBootstrapper`. A node (or runner subsystem) will be assembled purely by choosing which packs to install. For example, assembling a **CGF (Brain) Node** will look like this:

-   **Install Logic Packs:** `CgfLogicPack` (Behavior, BTree, HSM), `OrchestrationLogicPack` (Time sync, Cluster state).-   **Install Translator Packs (HROT):** `HrotIntentEgressPack` (Sends intents to muscle), `HrotStateIngressPack` (Receives physics states from muscle).

Assembling an **All-In-One Node (Editor)** will simply install both the Brain and Muscle logic packs _without_ the network translator packs, allowing them to communicate purely via the internal shared ECS and event bus.

2\. Translator Packs (HROT NED Only)

Since we are focusing solely on the simplified HROT NED DDS model for now, we will conceptually group the translators by their **data category** rather than by node role. This ensures that when Bagira (BDC SST) support is added later, you only need to swap the factory methods.

We will create the following Hrot Translator Packs:

-   **Entity States & Simulation Events Pack:** Translates `SimTransform`, `Health`, `HitEvent`, etc. (Ingress on Brain/IG, Egress on Muscle).-   **Actuator Intents Pack:** Translates `NavigationIntent`, `WeaponFireIntent`, etc. (Egress on Brain, Ingress on Muscle).-   **Service Queries/Responses Pack:** Translates perception and navigation requests.-   **Network ID Allocation Pack:** Translates ID requests.

3\. Extracting the Scenario Editor Pack

To keep the editor flexible and extractable, we will move all UI and manipulation logic into a standalone `ScenarioEditorModule` (or `Hrot.Editor` project).

-   This pack will encapsulate the ImGui UI, Vis2D map layers, and the tools for editing missions, routes, areas, and entities.-   Because it will be packaged as a standalone `IEcsModule`, you can plug it directly into the `ExConSubsystem` for cluster control, or plug it into an "All-in-one" single-process editor.

4\. CQRS Audit and Conversion (Brain vs. Muscle)

You noted that there are likely CQRS violations. The architectural goal is strict separation: the Brain (CGF) only issues _Intents_, and the Muscle (SimHost) only reports _States/Status_.

I will audit the codebase and enforce this boundary. We already have a good foundation with `NavigationIntent` (Command) and `NavigationStatus` (Result). The audit will target and convert the following likely violations:

-   **Direct Physics Mutations by AI:** Ensure no BTree or HSM nodes in the Brain are directly modifying `SimTransform`, `SimVelocity`, or `VehicleState`. They must only write to `LocomotionChannel`, `WeaponChannel`, or broadcast `NavigationIntent`.-   **Direct Mission Mutations by Physics:** Ensure Muscle systems (like `CarKinematicsSystem` or collision solvers) do not directly modify the `MissionPlanQueue` or `BehaviorState`. They must emit status events (like `NavigationStatus.Arrived` or `HitEvent`) which the Brain consumes to advance its own mission phases.-   **Combat Executors:** Ensure that firing a weapon on the Brain node only emits a `WeaponFireIntent`. The actual bullet spawning (`CmdSpawnVehicle` / `BallisticProjectile`) and damage calculation must happen on the Muscle node, triggered by the translated intent.

Next Steps

If you agree with this design, the best place to start is the **CQRS Audit and Conversion**. We can clean up the ECS component boundaries (moving stray Brain components out of Muscle logic and vice versa) so that the core FDP engine strictly adheres to the Intents/States split. Once the ECS is pure, wrapping the logic and translators into their respective "Packs" will be a straightforward structural refactor.

Shall we begin with the CQRS audit, or would you prefer to start by extracting the `ScenarioEditor` module?

the audit pls

Here is the audit of the current codebase. While the foundation for a strict CQRS architecture (Brain emitting Intents, Muscle emitting States/Status) is solid—especially with `NavigationIntent` and `WeaponFireIntent`—there are several critical violations where systems cross the Brain/Muscle boundary, query across domains, or bypass the intent bus entirely.

1\. Cross-Domain Query Violations (The "Split-Brain" Problem)

The most severe architectural violations occur when a single system queries for both Brain-owned and Muscle-owned components simultaneously. In a distributed cluster, these queries will return exactly zero entities because no single node holds both component types.

-   **RouteContextSystem****:** This system reads routing advice and writes it to the `BrainBlackboard`. However, its query strictly requires both `NavState` (Muscle tier) and `BrainBlackboard` (Brain tier). In a distributed cluster, the Muscle node has `NavState` but lacks `BrainBlackboard`, and the Brain node has `BrainBlackboard` but lacks `NavState`. This system will silently fail to execute.-   **Scenario Specific Hacks (****ApcMobilityTriggerSystem** **/** **ApcMobilitySystem****):** Used in the UrbanCombat scenarios, these systems directly strip `ActorCapabilities.CanMove` based on `Health` damage. To filter entities, they query for both `Health` (Muscle data) and `BrainHsm128` (Brain data). This tightly couples the physics damage model to the cognitive HSM model.

2\. Module Misalignment & Misplaced Systems

Even when systems respect component ownership, they are sometimes registered in the wrong deployment modules, preventing them from running where their required components exist.

-   **HsmDamageBridgeSystem****:** This system detects when an entity loses its `CanMove` capability and injects a `MobilityLost` event into the HSM queue. It correctly queries Brain components (`BrainHsm128`, `BrainHsm64`). However, it is currently registered inside the `CombatModule`. According to the `NodeBootstrapper`, the `CombatModule` is deployed to `MuscleGround` and `AllInOne` nodes, but explicitly _excluded_ from the `Brain` role. Therefore, in a distributed setup, the Brain node will never run this bridge, and HSMs will never transition to disabled states when taking damage.

3\. Bypassing the Intent Bus (Direct Physics Mutation)

Strict CQRS dictates that all AI and UI commands should be written as Intents (e.g., `NavigationIntent`), which the Muscle translates into physics state (`NavState`). Several areas bypass this and manipulate the muscle directly.

-   **VehicleCommandSystem****:** A legacy system from the standalone `CarKinem` toolkit that consumes imperative events like `CmdNavigateToPoint` or `CmdFollowTrajectory` and directly mutates `NavState` and `SimTransform`. While acceptable for "brain-dead" entities, higher-level logic still relies on this instead of the intent bus.-   **PersonalRouteAuthoringSystem****:** When an operator edits a route, this system issues a `CmdFollowTrajectory` event directly to the `VehicleCommandSystem`. It should instead write a `NavigationIntent` with `NavigationMode.FollowRoute` so the CQRS adapter (`NavigationIntentBridgeSystem`) can handle the transition.-   **UI Right-Click Routing (****SimHostVisualization****):** When the UI detects a right-click on a "brain-dead" entity, it explicitly bypasses the mission machinery and mutates the Muscle layer directly by setting waypoints or destinations.

Refactoring Action Plan

To resolve these and achieve a clean Logic/Translator Pack architecture:

-   **Migrate Cross-Domain Systems:** `RouteContextSystem` must be rewritten to observe `NavigationStatus` or a new dedicated intent, rather than querying `NavState` directly.-   **Realign Modules:** Move `HsmDamageBridgeSystem` out of `CombatModule` and into `CognitiveRuntimeModule` so it correctly executes on the Brain node where `BrainHsm128` resides.-   **Enforce Intent Usage:** Deprecate `CmdNavigateToPoint` and `CmdFollowTrajectory` usage for networked entities. Update UI tools like `PersonalRouteAuthoringSystem` to emit `NavigationIntent` commands, ensuring all movement requests flow safely across the CQRS boundary.

pls let's detail the refactoring plan point by point. Letst start with #1 Migrate Cross-Domain Systems

To fix the cross-domain violation in `RouteContextSystem`, we must strictly enforce the CQRS boundary. Currently, the system queries for both `NavState` (a physics/Muscle component) and `BrainBlackboard` (a cognitive/Brain component) on the same entity. In a distributed architecture, no single node possesses both components, rendering the system inert.

The clean architecture solution is to pipe the necessary routing progress data from the Muscle layer back to the Brain layer using the existing CQRS feedback channel: `NavigationStatus`.

Here is the step-by-step refactoring plan for **#1 Migrate Cross-Domain Systems**:

Step 1.1: Enhance the CQRS Feedback Contract

The `NavigationStatus` component acts as the Muscle-to-Brain telemetry channel. We need to expand this contract so the Muscle can report how far along a route the entity has traveled.

-   **Update the ECS Component:** Modify `FDP.Toolkit.Navigation.NavigationStatus` to include a progress tracker.-   **Update the DDS Descriptor:** Modify `Hrot.NED.Descriptors.NavigationStatus` in the `Hrot.NED` project to include `float ProgressS` so the data can cross the network.

Step 1.2: Populate Progress on the Muscle Node

The Muscle node calculates physics and trajectory progress in `CarKinematicsSystem`, which writes to `NavState`. The `NavigationExecutionSystem` is responsible for translating physics states into the `NavigationStatus` CQRS feedback component.

-   **Update** **NavigationExecutionSystem****:** Read `NavState.ProgressS` and map it into the outbound `NavigationStatus` component.

Step 1.3: Update Network Translators

We need to ensure the new progress data makes it across the CycloneDDS boundary.

-   **Egress (Muscle side):** In `NavigationStatusEgressTranslator.ScanAndPublish`, map the new ECS `ProgressS` field to the DDS message.-   **Ingress (Brain side):** In `NavigationStatusIngressTranslator.PollIngress`, map the incoming DDS `ProgressS` field back into the ECS `NavigationStatus` component.

Step 1.4: Refactor `RouteContextSystem` on the Brain Node

Now we rewrite the `RouteContextSystem` to rely purely on Brain-tier components. It will determine the active route from the `NavigationIntent` and read the vehicle's progress from the `NavigationStatus`.

-   **Fix the Query:** Remove `NavState` and replace it with `NavigationIntent` and `NavigationStatus`.-   **Update the Logic:** Instead of reading `nav.Mode` and `nav.TrajectoryId` from `NavState`, read them from `NavigationIntent`. Read progress from `NavigationStatus`.-   **Resolve Segment & Write Blackboard:** Pass `status.ProgressS` into the existing `ResolveSegmentIndex` logic to look up the `ExtensionJson` from the `RoutePlan`, then apply it to the `BrainBlackboard`.

This completely decouples the `RouteContextSystem` from physics/muscle components while maintaining all existing behavior.

Once this makes sense, we can move on to detailing **#2 Realign Modules (Moving HsmDamageBridgeSystem)**.

good let's move to #2

Let's tackle the module realignment. Clean architecture is all about putting responsibilities where they belong, and the current placement of `HsmDamageBridgeSystem` is a classic leaky boundary that breaks our distributed deployment model.

Currently, `HsmDamageBridgeSystem` explicitly queries for cognitive components (`BrainHsm128`, `BrainHsm64`) to inject the `MobilityLost` event into the state machine. However, it is registered inside the `CombatModule`. Because the `NodeBootstrapper` assigns `CombatModule` to Muscle nodes and explicitly excludes it from Brain nodes, this system is completely orphaned in a distributed setup. The Muscle node runs the system but has no Brain components to query, while the Brain node has the components but never runs the system.

Here is the step-by-step refactoring plan to fix this and enforce our node boundaries:

Step 2.1: Remove the System from the Muscle Tier (`CombatModule`)

We need to excise the cognitive dependency from the physics/combat layer.

-   Open `FDP/Toolkits/FDP.Toolkit.Combat/Modules/CombatModule.cs`.-   Locate the `RegisterSystems` method.-   Remove the line `simGroup.AddSystem(new HsmDamageBridgeSystem());`. _(Note: You will also want to remove the_ _using FDP.Toolkit.Behavior.Systems;_ _directive from this file to ensure the Combat module has no lingering behavior system dependencies__.)_

Step 2.2: Add the System to the Brain Tier (`CognitiveRuntimeModule`)

Now we place the system where the cognitive data actually lives.

-   Open `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/CognitiveRuntimeModule.cs`.-   In the `RegisterSystems` method, register the bridge system right before the HSM tick systems. It must execute beforehand so the `MobilityLost` event is processed in the exact same frame it is injected.

```
public void RegisterSystems(SystemGroup group)
{
    group.AddSystem(new ChannelArbitrationSystem());
    
    // NEW: Bridge physics capability loss to cognitive HSM events
    group.AddSystem(new HsmDamageBridgeSystem()); 
    
    group.AddSystem(new BTreeTickSystem(_registry));
    group.AddSystem(new HsmTickSystem<BrainHsm128>(_registry));
    group.AddSystem(new HsmTickSystem<BrainHsm64>(_registry));
}
```

Step 2.3: Verify the CQRS Data Flow (How the Brain knows about damage)

You might wonder: _If we moved the system to the Brain, how does the Brain node know that the vehicle was damaged and lost its_ _CanMove_ _capability?_

This is where the existing CQRS network translators elegantly save the day. The architecture already supports an authoritative damage-feedback loop:

-   The Muscle node resolves the ballistic impact and calculates damage via `DamageCalculationSystem`.-   The Muscle node publishes this over CycloneDDS as an `EntityHitDamage` message.-   The Brain node's `EntityHitDamageIngressTranslator` receives the message and publishes a `DamageAssessedEvent` on the Brain's local event bus.-   The Brain node's `HealthApplicationSystem` consumes this event, reduces the entity's health, and explicitly strips the `ActorCapabilities.CanMove` bit directly on the Brain node.

Because `HealthApplicationSystem` mutates `ActorCapabilityState` locally on the Brain node, our newly relocated `HsmDamageBridgeSystem` will detect the capability transition (`PreviousCapabilities` vs `ActorCapabilityState`) and correctly enqueue the `MobilityLost` event into the HSM.

By making this move, we have completely decoupled the `CombatModule` from the `Behavior` toolkit's runtime systems, restoring a strict Brain/Muscle boundary.

If this looks solid to you, we can move on to **#3 Enforce Intent Usage**, which tackles the legacy `VehicleCommandSystem` directly mutating physics states instead of using the intent bus.

yes #3 please

Let's tackle the final and perhaps most crucial part of this CQRS enforcement: cleaning up the intent bus. As a software engineer, there are few things more satisfying than removing leaky backdoor channels and forcing all data through a single, predictable pipeline.

Right now, the FDP CarKinem toolkit has legacy imperative events like `CmdNavigateToPoint`, `CmdFollowTrajectory`, and `CmdStop`. The `VehicleCommandSystem` consumes these and directly mutates `NavState` (a Muscle component). Furthermore, UI authoring tools like `PersonalRouteAuthoringSystem` and right-click handlers bypass the cognitive layer entirely to mutate physics states.

If we want to cleanly extract Translator Packs, we cannot have two separate ways to move a vehicle (Intents vs. Direct Commands). We must route _all_ movement requests through `NavigationIntent`.

Here is the step-by-step refactoring plan for **#3 Enforce Intent Usage**:

Step 3.1: Refactor `PersonalRouteAuthoringSystem`

Currently, when a user edits a personal route, the system emits a `CmdFollowTrajectory` event which `VehicleCommandSystem` blindly applies to the Muscle layer. We need to rewrite this to act like a proper Brain by writing a `NavigationIntent`.

Modify `PersonalRouteAuthoringSystem.cs`:

```
// OLD: Bypassing CQRS
World.Bus.Publish(new CmdFollowTrajectory
{
    Entity       = vehicle,
    TrajectoryId = cache.TrajectoryId,
    Looped       = (byte)(isLoop ? 1 : 0),
});

// NEW: Emitting a clean CQRS Intent
var intent = view.GetComponentRO<NavigationIntent>(vehicle);
intent.IntentId++; 
intent.Mode = NavigationMode.FollowRoute;
intent.TrajectoryId = cache.TrajectoryId;
// IsLooped can be handled by standard intent logic or added to the NavigationIntent contract if needed.
World.SetComponent(vehicle, intent);
```

Step 3.2: Refactor UI Right-Click Logic (`SimHostVisualization`)

In `SimHostVisualization.HandleRightClickForEntity`, there is an explicit "Brain-dead path" that bypasses the mission machinery and mutates the destination directly for entities without an active behavior. We must treat the UI as an authoritative Commander (Brain) and enforce intent usage even for these entities.

Modify the "Brain-dead path" to write a `NavigationIntent` instead of calling `setDestination` (which wraps `CmdFollowTrajectory` or direct `NavState` mutation):

```
if (!brainActive)
{
    // NEW: Uniformly use the CQRS intent bus even for manual overrides
    var intent = repo.GetComponent<NavigationIntent>(entity);
    intent.IntentId++;
    intent.Mode = NavigationMode.DirectPoint;
    intent.FinalDestination = pos;
    intent.TargetSpeed = 15f; // Default manual speed
    intent.ArrivalRadius = 3.0f;
    repo.SetComponent(entity, intent);
    return;
}
```

Step 3.3: Thin out `VehicleCommandSystem`

With the UI and authoring systems now cleanly emitting `NavigationIntent` components, the `NavigationIntentBridgeSystem` (which already exists and correctly maps Intents to `NavState`) will handle the Muscle translation.

We can now cleanly excise the legacy backdoor commands from `VehicleCommandSystem`.

-   Open `FDP.Toolkit.CarKinem/Systems/VehicleCommandSystem.cs`.-   Remove the event consumption and processing methods for:
    -   `ProcessNavigateToPointCommands()`-   `ProcessFollowTrajectoryCommands()`-   `ProcessNavigateViaRoadCommands()`-   `ProcessStopCommands()`-   `ProcessSetSpeedCommands()`-   Delete the corresponding `Cmd*` structs from `CommandEvents.cs`.

The Architectural Win

By completing this step, you achieve a massive simplification for your Translator Packs. You no longer need to write network translators for a half-dozen imperative command events. The network vocabulary between Brain and Muscle is reduced to exactly two components:

-   **Egress on Brain / Ingress on Muscle:** `NavigationIntent` (The command).-   **Egress on Muscle / Ingress on Brain:** `NavigationStatus` (The feedback).

Once these three points are refactored, your core ECS is fully CQRS compliant. We will then be in a perfect position to extract the pure logic (e.g., `CarKinematicsSystem`, `BTreeTickSystem`) into `Logic Packs`, and wrap the CycloneDDS translators into swappable `Translator Packs`.

good! pls check also the "Pluggability violations" source. It describes some flaws (detected in a bit older codebase than we have now). Pls evaluate if they are still valid and need fixing to comply with the CQRS rules.

Here is the evaluation of the "Pluggability violations" against our current codebase. You are spot on to bring this up; enforcing the Anti-Corruption Layer (ACL) is the only way our "Translator Packs" vs. "Logic Packs" architecture will actually work.

Let's review the five identified flaws, see where the current codebase stands, and define what we need to fix for strict CQRS and clean boundaries.

1\. `MissionControlRequestSystem` is a God Class (Direct DDS & JSON Coupling)

**Status: STILL VALID AND CRITICAL.** Looking at the current codebase, `MissionControlRequestSystem` is still directly instantiating `DdsReader<MissionControlRequest>` and `DdsWriter<MissionControlAck>`. Furthermore, it writes directly to `EntityMission` over DDS and parses parameters using `System.Text.Json`. This system is a massive monolith that mixes network I/O, serialization, and core domain mutations (altering `MissionPlanQueue` and `BehaviorState`).

**The Fix:** We must split this class entirely:

-   **Translator Pack (****MissionControlIngressTranslator****):** Polls the DDS topic, handles the JSON deserialization, and drops a strongly-typed, unmanaged `MissionControlIntent` onto the event bus.-   **Logic Pack (****MissionControlExecutionSystem****):** A pure ECS system that consumes `MissionControlIntent`, validates it, and mutates `MissionPlanQueue` or `BehaviorState`. It will have zero knowledge of CycloneDDS or JSON.

2\. `UpdateEntityDescriptorRequestSystem` Directly Reads DDS

**Status: ARCHITECTURALLY ACCEPTED (but must be packaged correctly).** The codebase shows this system still directly reads `UpdateEntityDescriptorRequest` from DDS. However, as the design notes highlight, because it directly unpacks `EntityDescriptorUnion` into DOD-friendly ECS components (avoiding heavy intermediate allocations), it is effectively a **Command Ingress Translator**.

**The Fix:** We don't need to rewrite its internals, but we _must_ ensure it is strictly registered inside a **Translator Pack** (e.g., `SimHostNetworkTranslatorPack`), not the core FDP logic packs. If we run the engine offline, this system must not be loaded.

3\. Physics & Combat Domains Leak Network IDs

**Status: STILL VALID.** The codebase confirms that `HitResolutionSystem` (a core physics/combat system) is still injecting `NetworkEntityMap` so that it can embed `shooterNetId` into the emitted `DetonationNotification`. This is a severe pluggability violation because the core combat engine shouldn't know what a "Network ID" is.

**The Fix:**

-   **Logic Pack (****HitResolutionSystem****):** Must only emit `DetonationNotification` containing local `Entity` handles.-   **Translator Pack (****MunitionDetonationEgressTranslator****):** This translator will listen for local `DetonationNotification`s, intercept them, query the `NetworkEntityMap` to translate the local `Entity` to a Network ID, and publish the DDS packet.

4\. `SimHostModule` is a Tangled Monolith

**Status: MOSTLY FIXED.** The monolithic `SimHostModule` mentioned in the old notes has largely been dismantled. The codebase now uses a `NodeBootstrapper` that cleanly composes node roles out of granular modules like `CombatModule`, `CognitiveRuntimeModule`, and `GroundKinematicsModule`. We also already have a `SharedTranslatorPack` for network boundaries.

**The Fix:** We just need to verify during our refactoring that stray network dependencies (like `MissionControlRequestSystem` and `CreateEntityRequestSystem`) are completely moved out of the core modules and placed into the network translator packs.

5\. `ReferenceEditLoadHandler` & Scenario Handlers Parsing JSON

**Status: FIXED.** This violation has been successfully eradicated in the current architecture. The `NodeOpSlaveTranslator` now acts as a strict Anti-Corruption Layer, intercepting DDS `NodeOpCommand`s at the network edge and deserializing `PayloadJson` into strongly-typed `DomainPayload`s. Additionally, `ClusterOpRequestAdapter` handles all JSON extraction so that the core orchestrator handlers operate cleanly without `System.Text.Json` references.

Conclusion for the Refactoring Plan

To achieve our "Network as a Plugin" vision, we must add the fixes for **#1 (MissionControlRequestSystem)** and **#3 (HitResolutionSystem)** to our immediate backlog.

If you are ready, I can detail the extraction plan for `MissionControlRequestSystem` so we can convert it into a pure Translator and a pure Logic intent handler.

pls let's detail the fixes one by one. start with #1 MissionControlRequestSystem

To completely decouple the `MissionControlRequestSystem` from network infrastructure and JSON parsing, we need to split it into a pure **Logic Pack** system and boundary **Translator Pack** components.

Currently, `MissionControlRequestSystem` directly reads `MissionControlRequest` from DDS, writes `MissionControlAck`, and manually writes `EntityMission` to DDS. We will replace this with a clean Anti-Corruption Layer (ACL) using the internal event bus.

Here is the step-by-step refactoring plan for **#1 MissionControlRequestSystem**:

Step 1.1: Define Pure Domain Events (The Intents)

First, we need strictly-typed local events that carry the command and the acknowledgment across the Brain/Translator boundary without referencing CycloneDDS attributes.

Create these in the core logic namespace (e.g., `FDP.Toolkit.Behavior.Events`):

```
// The incoming command (from Translator to Logic)
public class MissionControlIntent
{
    public Guid RequestId;
    public long TargetEntityId;
    public long BaseVersion;
    public MissionCommandUnion Payload; // Use the parsed domain payload
}

// The outgoing acknowledgment (from Logic to Translator)
public struct MissionControlAckEvent
{
    public Guid RequestId;
    public int ErrorCode;
    public string? ErrorMessage;
    public long NewVersion;
}
```

Step 1.2: Extract the Network Translators

Next, we move all DDS socket polling and network message construction to the edge of the application by creating two new translators in the `Hrot.SimHost.Network` space.

**1\.** **MissionControlIngressTranslator****:** This translator will own the `DdsReader<MissionControlRequest>`.

-   **Action:** In its `PollIngress` method, it will read `MissionControlRequest` from CycloneDDS.-   **Translation:** It unpacks the DDS struct and drops a `MissionControlIntent` onto the `FdpEventBus` using `PublishManaged`.

**2\.** **MissionControlAckEgressTranslator****:** This translator will own the `DdsWriter<MissionControlAck>`.

-   **Action:** In its `ScanAndPublish` method, it will consume `MissionControlAckEvent` structs from the `FdpEventBus`.-   **Translation:** It packs them into the `MissionControlAck` DDS message and writes them to the network socket.

Step 1.3: Purify the Core System (`MissionControlExecutionSystem`)

Now we rewrite the core logic system so that it has zero knowledge of CycloneDDS or JSON.

-   **Remove Network Dependencies:** Delete the `DdsReader<MissionControlRequest>`, `DdsWriter<MissionControlAck>`, and `DdsWriter<EntityMission>` fields from `MissionControlRequestSystem`.-   **Update the Constructor:** It should now only take the `NetworkEntityMap` and `BehaviorRegistry`.-   **Consume Intents:** Change `OnUpdate` to consume `MissionControlIntent` from the `FdpEventBus` instead of calling `_reader.Take()`.-   **Emit Acks Locally:** Instead of calling `_writer?.Write(new MissionControlAck(...))`, it will now call `World.Bus.Publish(new MissionControlAckEvent(...))`.-   **Delete** **PublishEntityMission****:** Currently, the system manually pushes the updated `EntityMission` to DDS upon success. **Delete this entirely.** The engine already possesses an `EntityMissionEgressTranslator` which listens for ECS changes to `MissionPlanQueue` and automatically publishes `EntityMission` over DDS. Relying on the existing translator enforces a single path for state replication.

The Architectural Win

By completing these steps, your core mission logic becomes a pure ECS system that can be safely unit-tested offline. The heavy lifting of JSON and DDS is pushed strictly to the Translator Packs, fulfilling the vision of "network as a plugin".

Once this is complete, we can move on to **#3 HitResolutionSystem** to fix the network ID leaks in the physics and combat domains.

ok now pls #2 UpdateEntityDescriptorRequestSystem

As noted in the design logs, this system requires a different treatment than `MissionControlRequestSystem`.

Because it directly unpacks the `EntityDescriptorUnion` into DOD-friendly ECS components (like `SimTransform` or `EditablePolyline`), introducing an intermediate `Intent` object would cause unnecessary heap allocations and degrade our Data-Oriented Design (DOD) performance. Therefore, it is already architecturally accepted as a **Command Ingress Translator**. It perfectly blends DOD performance with CQRS boundary enforcement by acting as the Anti-Corruption Layer (ACL) and closing the distributed request-response loop.

The actual pluggability violation is _how_ it is packaged and registered. Currently, it lives in the `Hrot.Map.Common.Systems` namespace and is hard-registered into the core `_kernelGroup` during `SimHostApp.InitializeEmbedded`. If we want to run the engine in a pure offline mode with only Logic Packs, this DDS-coupled system will break the build or crash.

Here is the refactoring plan for **#2 UpdateEntityDescriptorRequestSystem**:

Step 2.1: Semantic Relocation (Namespace & Folder)

We must physically move the system out of the core domain and into the network boundary.

-   Move `UpdateEntityDescriptorRequestSystem.cs` from `Hrot.Map.Common/Systems` to `Hrot.Map.Common/Replication/Ingress`.-   Update its namespace from `Hrot.Map.Common.Systems` to `Hrot.Map.Common.Replication.Ingress`. This makes it explicitly clear to any developer that this class belongs to a Translator Pack, not a Logic Pack.

Step 2.2: Extract from Core Bootstrapper (`SimHostApp.cs`)

Currently, `SimHostApp.cs` blindly registers this system into the core simulation group regardless of network availability:

```
// OLD: Hardcoded in SimHostApp.cs
_kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(ddsParticipant, entityMap, wgs84));
```

We need to remove this from the unconditional startup path.

Step 2.3: Register strictly within the Network Boundary

We must ensure this system is only instantiated when the CycloneDDS network layer is active.

Since `UpdateEntityDescriptorRequestSystem` is an `IEcsModuleSystem` (a ComponentSystem) rather than a passive `IDescriptorTranslator`, we should bundle it into the `SimHostModule` (which acts as our network-aware edge module) or conditionally register it during the network setup phase.

-   Open `Hrot.SimHost/Modules/SimHostModule.cs`.-   Inject the `UpdateEntityDescriptorRequestSystem` into the module alongside the other network-edge request systems (`CreateEntityRequestSystem`, `DeleteEntityRequestSystem`).-   Update `SimHostModule.RegisterSystems` to include it:

```
public void RegisterSystems(ISystemRegistry registry)
{
    registry.RegisterSystem(_requestSystem);
    registry.RegisterSystem(_spawnSystem);
    registry.RegisterSystem(_deleteSystem);
    registry.RegisterSystem(_finalizationSystem);
    
    // NEW: Register the Command Ingress Translator only when the network module is loaded
    if (_updateDescriptorSystem != null)
    {
        registry.RegisterSystem(_updateDescriptorSystem);
    }
}
```

The Architectural Win

By leaving the internal DOD unpacking intact but pushing the class into the `Replication.Ingress` namespace and conditionally loading it, we respect the "Network as a plugin" mandate. The core logic packs remain 100% oblivious to `UpdateEntityDescriptorRequest`, and the engine can safely run offline.

Once this is patched, we can move on to **#3 HitResolutionSystem**, which currently leaks network IDs directly into the core physics engine. Let me know when you're ready!

good now pls #3

This is a textbook Clean Architecture fix. Right now, your Physics and Behavior logic packs are contaminated by network concerns. `HitResolutionSystem` and `AimAndFireExecutor` both demand a `NetworkEntityMap` injected into their constructors just so they can stamp `long` Network IDs onto their outbound events.

If we want true pluggability where the simulation can run entirely offline or in a single-process editor without a network layer, the core engines cannot know what a "Network ID" is. They must traffic exclusively in local `Entity` handles.

Here is the step-by-step refactoring plan for **#3 HitResolutionSystem & Combat Executors**:

Step 3.1: Purify the Domain Events

We must strip the `long` network ID fields from the core domain events and replace them with standard ECS `Entity` handles.

-   **Modify** **DetonationNotification** (in `FDP.Toolkit.Combat.Contracts`): Change `ShooterEntityId` and `HitEntityId` to local handles.-   **Modify** **WeaponFireIntent** (in `FDP.Toolkit.Combat.Events`):

Step 3.2: Cleanse the Logic Packs

Now we remove the `NetworkEntityMap` dependencies entirely from the core systems. They will blindly emit events using their local context.

-   **Refactor** **HitResolutionSystem** (`FDP.Toolkit.Physics`):
    -   Remove the `NetworkEntityMap` from the constructor and fields.-   Remove the `if (_entityMap is not null)` guard. It should _always_ emit the notification locally.-   Emit the cleansed event:-   **Refactor** **AimAndFireExecutor** (`FDP.Toolkit.Combat`):
    -   Remove `NetworkEntityMap` from the constructor.-   When firing, publish the intent using local handles:

Step 3.3: Push ID Mapping to the Anti-Corruption Layer (Translators)

The network translation packs must now intercept these local events, resolve the Network IDs, and publish the CycloneDDS messages.

-   **Update** **MunitionDetonationEgressTranslator** (`Hrot.SimHost.Network.Egress`): Inject the `NetworkEntityMap` into its constructor. In `ScanAndPublish`, intercept the local notification, resolve the IDs, and drop the DDS packet:-   **Update** **WeaponFireIntentEgressTranslator** (`Hrot.SimHost.Network.Egress`): Apply the exact same mapping logic. Read `evt.Shooter` and `evt.Target`, resolve them via `_entityMap`, and publish the `WeaponFireRequest` DDS message.

The Architectural Win

By making this change, your `FDP.Toolkit.Physics` and `FDP.Toolkit.Combat` modules are now 100% agnostic of CycloneDDS and distributed ID mapping. You can now take the `CombatModule` and `PhysicsQueryModule` and run them in a completely offline Editor process. The network layer has officially become a true plug-in!

Let me know if you'd like to look at any specific part of this implementation, or if we should review the overall structure of the new "Packs" now that the CQRS boundaries are sealed.

I see few more CQRS revamp follow ups. One of them is: \[BUG\] ClusterMaster ctor exists in 2 versions - with DDS and without it. The DDS one is undersired after we revamped the one with FdpEvents. No fallback and backward compatibility please!

This is a textbook Clean Architecture cleanup. The `ClusterMaster` currently suffers from a "split personality"—trying to serve both as a pure domain orchestrator (listening to the `FdpEventBus`) and as a legacy DDS network endpoint,. By removing the DDS fallback paths, we enforce a strict Anti-Corruption Layer (ACL) where the Translators handle all CycloneDDS I/O, and the `ClusterMaster` remains a pure, testable C# domain class,.

Here is the aggressive, no-backward-compatibility refactoring plan to completely purge DDS from `ClusterMaster`:

Step 1: Nuke the DDS Fields and Constructors

First, we eliminate all CycloneDDS dependencies and the legacy ID allocator server (which is already properly handled by `OrchestratorSubsystem` in the bus-mode architecture,).

-   **Remove DDS Fields:** Delete `_systemStateWriter`, `_heartbeatReader`, `_sysOpRequestReader`, `_sysOpStatusWriter`, `_nodeOpStatusReader`, `_nodeOpWriterCache`, `_nodeOpParticipant`, and `_inventoryWriter`,.-   **Remove ID Server Fields:** Delete `_idAllocatorServer`, `_idServerCts`, and `_idServerThread`.-   **Delete the DDS Constructors:** Completely remove `public ClusterMaster(DdsParticipant participant)` and `public ClusterMaster(DdsParticipant participant, ClusterConfiguration config)`,.-   **Purify the Bus Constructor:** Keep only `public ClusterMaster(FdpEventBus eventBus, ClusterConfiguration? config = null)`. Remove any nullable suppression hacks (like `_heartbeatReader = null!`). Ensure `_eventBus` is not nullable (`private readonly FdpEventBus _eventBus;`).

Step 2: Clean Up the `Tick()` Loop and Ingress

With the DDS reader gone, we remove the `if (_eventBus != null)` branches and delete the direct DDS polling methods.

-   **Update** **Tick()****:** Remove the `else { ProcessClusterOpRequests(); }` branch,. The bus-based intent methods (`ProcessTransitionStateIntents`, `ProcessManageEpisodeIntents`, etc.) become the _only_ way operations enter the orchestrator.-   **Delete** **ProcessClusterOpRequests()****:** Remove this method and its companion `ProcessSingleClusterOpRequest(ClusterOpRequest req)` entirely. The `ClusterOpMasterTranslator` already handles this mapping.-   **Clean up** **IngestHeartbeats()****:** Remove the `_heartbeatReader.Take()` block,. Keep only the `_eventBus.ConsumeManaged<NodeHeartbeatEvent>()` loop.

Step 3: Purify the Egress/Fan-Out Methods

We must strip the legacy `else` branches that wrote directly to DDS writers.

-   **PublishOpStatus()****:** Keep only `_eventBus.PublishManaged(new ClusterOpCompletedEvent...)`. Delete the `_sysOpStatusWriter.Write(...)` path,.-   **PublishClusterState()****:** Keep only `_eventBus.PublishManaged(new ClusterStateTransitionedEvent...)`. Delete the `_systemStateWriter?.Write(...)` path.-   **FanOutNodeOp()****:** Keep only `_eventBus.PublishManaged(new ExecuteNodeOpIntent...)`. Delete the `_nodeOpWriterCache` lookup and the legacy DDS write path completely.-   **EjectNode()****:** Remove the `_nodeOpWriterCache` disposal logic since we no longer cache DDS writers per node.

Step 4: Consolidate `ConsumeNodeOpStatuses()`

The ACK processing currently has a massive split where Branch ACKs and ManageEpisode ACKs were only fully implemented in the DDS `_nodeOpStatusReader.Take()` loop,,. We need to migrate that logic up into the `_eventBus.ConsumeManaged<NodeOpCompletedEvent>()` loop and delete the DDS reader block.

-   Move the Live-from-Replay temporal interlock check (`_pendingBranchTasks.TryGetValue(ev.TransactionId...)`) into the event bus loop,.-   Move the Episode 2PC ACK check (`_pendingManageEpisodeTasks.TryGetValue(ev.TransactionId...)`) into the event bus loop,.-   Delete the entire `using var scope = _nodeOpStatusReader.Take();` block.

Step 5: Address the Asset Inventory Leak

Currently, `PublishAssetInventory()` writes directly to `_inventoryWriter`. To comply with the strict Event Bus architecture:

-   Create a new `AssetInventoryUpdateEvent` in `ClusterCqrsEvents.cs`.-   In `ClusterMaster.PublishAssetInventory()`, publish this new event to the `_eventBus` instead of writing to DDS.-   Update `ClusterOpMasterTranslator` to consume `AssetInventoryUpdateEvent` and perform the `_inventoryWriter.Write(...)`, moving the DDS dependency strictly to the edge.

Step 6: Clean up `Dispose()`

Remove all disposal calls for the deleted DDS readers, writers, and the ID allocator thread,. The `Dispose` method will now be incredibly lightweight, primarily cleaning up `_activeCancellations`.

By executing this refactoring, `ClusterMaster` loses all knowledge of `CycloneDDS.Runtime`. It will accept pure Intents from the bus and publish pure Events/Commands back to the bus, achieving perfect Domain isolation.

the next one is:

\[BUG\] ClusterUiCache is tied to network messages. After the cluster management revamp to CQRS, it should work purely with fdp events. Only the translator should work with network, everything inside the system must be communicated via FdpEvents.

This is ok for apps based on FDP kernel. I am not so sure about the ExCon which should be a lightweight subsystem. But I think it can stay lightweight even if using only FDP event bus (not whole kernel) connected to the network via translators.

You are absolutely right to question the footprint on ExCon, but your intuition is spot on: the `FdpEventBus` is an independent, extremely lightweight double-buffered queue. It does not require an `EntityRepository` or a `ModuleHostKernel` to function. ExCon can instantiate a standalone `FdpEventBus` to achieve perfect CQRS isolation without dragging in the heavy simulation ECS machinery.

Here is the aggressive refactoring plan to completely purge CycloneDDS and JSON parsing from `ClusterUiCache`, enforcing the Anti-Corruption Layer.

Step 1: Define the Missing Domain Events

The `ClusterUiCache` observes several streams of data. We already have events for operations and heartbeats, but we need pure domain events for System State and Asset Inventory so we can banish the DDS topics from the domain.

Add these to `FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`:

```
[EventId(9016)]
[DataPolicy(DataPolicy.NoRecord)]
public struct SystemStateUpdateEvent
{
    public ClusterState CurrentState;
    public Guid ExerciseId;
    public long StateStartWallTicks;
    public int TransactionEpoch;
}

[EventId(9017)]
[DataPolicy(DataPolicy.NoRecord)]
public struct AssetInventoryUpdateEvent
{
    public string[] LocalScenarios;
    public string[] LocalExercises;
    public string[] ArchivedExercises;
    public string[] UnarchivedLocalExercises;
}
```

Step 2: Purify `ClusterUiCache` (The Logic Pack)

We will completely gut `ClusterUiCache` of all DDS dependencies. It will now only accept an `FdpEventBus` and consume typed structs.

A massive architectural win here is that we no longer need to parse JSON (`PayloadJson`) to sniff out `TargetState` or `EpisodeId` for the 2PC history. We can inspect the strongly-typed `DomainPayload` directly.

**1\. Update the Constructor and Fields:**

```
// Remove all DdsReader<T> fields.
private readonly FdpEventBus _bus;

public ClusterUiCache(FdpEventBus bus, ITimeController? localTimeController = null)
{
    _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    _localTimeController = localTimeController;
    ReachableTargets = _planner.GetReachableTargets(CurrentState);
}
```

**2\. Update the** **Update()** **Loop:** Replace the `Take()` polling logic with `_bus.ConsumeManaged<T>()`.

```
public void Update()
{
    // 1. System State
    foreach (var s in _bus.ConsumeManaged<SystemStateUpdateEvent>())
    {
        var prev = CurrentState;
        CurrentState = s.CurrentState;
        IsBootstrapped = s.CurrentState != ClusterState.Degraded;
        if (CurrentState != prev) ReachableTargets = _planner.GetReachableTargets(CurrentState);
    }

    // 2. Inventory
    foreach (var inv in _bus.ConsumeManaged<AssetInventoryUpdateEvent>())
    {
        AvailableScenarios = inv.LocalScenarios;
        AvailableExercises = inv.LocalExercises;
        ArchivedExercises = inv.ArchivedExercises;
        UnarchivedLocalExercises = inv.UnarchivedLocalExercises;
    }

    // 3. Heartbeats
    foreach (var hb in _bus.ConsumeManaged<NodeHeartbeatEvent>())
    {
        _activeNodes[hb.NodeId] = new NodeHeartbeat { /* Map fields */ };
        _nodeReceivedMs[hb.NodeId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    // 4. Time Mode
    foreach (var tm in _bus.ConsumeManaged<SwitchTimeModeEvent>())
    {
        IsPaused = tm.TargetMode == TimeMode.Deterministic;
        if (tm.TimeScale > 0f) MasterTimeScale = tm.TimeScale;
        if (tm.BarrierWallTicks > 0) MasterWallTicks = tm.BarrierWallTicks;
        if (!IsPaused && tm.SimTimeSnapshot > 0.0) _networkSimTime = tm.SimTimeSnapshot;
    }

    Process2PcNetworkTraffic();
    DrainSysOpStatus();
}
```

**3\. Clean up** **Process2PcNetworkTraffic** **(No more JSON parsing):** Instead of reading `NodeOpCommand` from DDS and using `JsonDocument.Parse`, we read the `ExecuteNodeOpIntent` from the bus and check the typed payload.

```
private void Process2PcNetworkTraffic()
{
    foreach (var intent in _bus.ConsumeManaged<ExecuteNodeOpIntent>())
    {
        // Sniff ManageEpisode
        if (intent.Operation == NodeOpType.StartEpisode && intent.DomainPayload is NodeEpisodePayloadDto epStart)
        {
            if (epStart.EpisodeId.HasValue) _activeEpisodes.Add(epStart.EpisodeId.Value);
        }
        else if ((intent.Operation == NodeOpType.StopEpisode || intent.Operation == NodeOpType.ForgetEpisode) 
                 && intent.DomainPayload is NodeEpisodePayloadDto epStop)
        {
            if (epStop.EpisodeId.HasValue) _activeEpisodes.Remove(epStop.EpisodeId.Value);
        }

        var txId = intent.TransactionId;
        if (!_inFlight.ContainsKey(txId))
        {
            var targetState = ClusterState.Idle;
            // Clean, strongly-typed extraction instead of JSON parsing
            if (intent.DomainPayload is NodeTransitionPayloadDto tsPayload && Enum.TryParse(tsPayload.TargetState, out ClusterState parsedState))
            {
                targetState = parsedState;
            }

            var tx = new DistributedTransaction
            {
                TransactionId = txId,
                SourceDsmState = CurrentState,
                TargetDsmState = targetState,
                // PayloadJson is no longer needed/available as raw string here, we can store a stringified intent if strictly required by UI
            };
            _inFlight[txId] = tx;
            _txHistory.Insert(0, tx);
        }
        // ... (Keep existing logic to increment TotalSteps)
    }

    // Process NodeOpCompletedEvent similarly to advance CompletedSteps
}
```

Step 3: Create the Anti-Corruption Layer (`OrchestrationObserverTranslator`)

Because `ClusterUiCache` is used by lightweight nodes like ExCon, we need an ingress translator that promiscuously sniffs orchestration traffic off the CycloneDDS wire and pushes it to the `FdpEventBus`.

Create this in `Hrot.Common/Orchestration/OrchestrationObserverTranslator.cs`:

```
public sealed class OrchestrationObserverTranslator
{
    private readonly DdsReader<SystemStateTopic> _stateReader;
    private readonly DdsReader<AssetInventoryTopic> _inventoryReader;
    private readonly DdsReader<ClusterOpStatus> _sysOpStatusReader;
    // Reads ALL NodeOpCommands, not just ones addressed to this node
    private readonly DdsReader<NodeOpCommand> _nodeOpCmdReader; 
    private readonly DdsReader<NodeOpStatus> _nodeOpStatusReader;
    private readonly FdpEventBus _bus;

    public OrchestrationObserverTranslator(DdsParticipant participant, FdpEventBus bus)
    {
        _bus = bus;
        _stateReader = new DdsReader<SystemStateTopic>(participant);
        _inventoryReader = new DdsReader<AssetInventoryTopic>(participant);
        _sysOpStatusReader = new DdsReader<ClusterOpStatus>(participant);
        _nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);
        _nodeOpStatusReader = new DdsReader<NodeOpStatus>(participant);
    }

    public void Tick()
    {
        using (var scope = _stateReader.Take())
        {
            foreach (var s in scope) if (s.IsValid) 
                _bus.PublishManaged(new SystemStateUpdateEvent { CurrentState = s.Data.CurrentState /* map fields */ });
        }

        using (var scope = _inventoryReader.Take())
        {
            foreach (var s in scope) if (s.IsValid) 
                _bus.PublishManaged(new AssetInventoryUpdateEvent { /* deserialize JSON arrays to string[] */ });
        }

        // Promiscuously forward ALL NodeOpCommands so the UI cache can build the 2PC history
        using (var scope = _nodeOpCmdReader.Take())
        {
            foreach (var s in scope) if (s.IsValid)
            {
                 // Re-use logic from NodeOpSlaveTranslator to parse payload
                 var payload = DeserializeNodePayload(s.Data.Operation, s.Data.PayloadJson);
                 _bus.PublishManaged(new ExecuteNodeOpIntent { 
                     TransactionId = s.Data.TransactionId,
                     TargetNodeId = s.Data.TargetNodeId,
                     Operation = (FDP.Toolkit.Orchestration.NodeOpType)s.Data.Operation,
                     DomainPayload = payload
                 });
            }
        }
        
        // Do the same for NodeOpStatus -> NodeOpCompletedEvent and ClusterOpStatus -> ClusterOpCompletedEvent
    }
}
```

Step 4: Wiring ExCon (`ExConSubsystem.cs`)

Finally, we assemble the pieces in the composition root. ExCon remains lightweight, utilizing the bus strictly as a local memory conduit.

```
// 1. Create the lightweight event bus
_orchestrationBus = new FdpEventBus();

// 2. Wire the translators (ACL)
_nodeOpSlaveTranslator = new NodeOpSlaveTranslator(...);
_orchestrationObserverTranslator = new OrchestrationObserverTranslator(_participant, _orchestrationBus);

// 3. Wire the pure domain logic
_uiCache = new ClusterUiCache(_orchestrationBus, _slaveSyncController);
```

By completing this refactoring, you ensure that every visual panel, caching mechanism, and business rule within the FDP bounds operates exclusively on strongly-typed POCOs over the `FdpEventBus`. CycloneDDS acts purely as a pluggable transport medium at the outermost edge.









I did another deep dive into the core FDP toolkits and the SimHost/IG subsystems to hunt down any lingering CQRS and Anti-Corruption Layer (ACL) violations.

I found **three critical architectural leaks** that were missed in the original design document. They violate our rule that Logic Packs must have zero knowledge of CycloneDDS and that Brain/Muscle boundaries must remain strictly decoupled.

Here is what needs to be fixed:

1\. The Other Half of the Combat Network Leak (`DamageAssessedEvent`)

We previously identified that `DetonationNotification` and `WeaponFireIntent` were leaking network IDs, which Phase 4.C in the design doc addresses. However, we completely missed the second half of the combat loop!

**The Flaw:** The `DamageAssessedEvent` still holds a `public long HitEntityId`. Because of this, both `DamageCalculationSystem` (which emits it) and `HealthApplicationSystem` (which consumes it) are forced to inject a `NetworkEntityMap` into their constructors just to resolve network IDs to local handles. Core logic systems shouldn't know what a network ID is.

**The Clean Fix:**

-   Change `DamageAssessedEvent` to use a local `Entity` handle instead of `long HitEntityId`.-   Remove the `NetworkEntityMap` dependencies from `DamageCalculationSystem` and `HealthApplicationSystem` entirely.-   Shift the ID mapping responsibility strictly to the network edge: `DamageAssessedEgressTranslator` will convert the local `Entity` to a network ID, and `EntityHitDamageIngressTranslator` will resolve the incoming network ID to a local `Entity` before publishing the event to the bus.

2\. Audio Perception "Split-Brain" (CQRS Violation)

We fixed the CQRS violation for `ApcMobilityTriggerSystem`, but there is another cross-domain query lurking in the perception toolkit.

**The Flaw:** `AudioPerceptionSystem` requires `SpatialGridData` and `SimTransform` to calculate distance (which are Muscle-tier physics components). However, when it finds a listener, it directly mutates `TargetMemory`. `TargetMemory` is a Brain-tier cognitive component used by behaviors (like the BTree Ambush). In a distributed cluster, the Muscle node has no `TargetMemory`, so this system will silently fail.

**The Clean Fix:**

-   `AudioPerceptionSystem` must stop mutating `TargetMemory` directly.-   Instead, when it detects a valid listener, it should publish a `TargetVisibleEvent` (or a new `AudioTargetDetectedEvent`).-   The `ThreatEvaluationSystem` (which already safely resides on the Brain/Perception tier and owns `TargetMemory` mutations) will consume this event and apply the threat score.

3\. DDS Structs Hoarded inside ECS Components (ACL Leak)

One of the core mandates of the new architecture is that Logic Packs and internal ECS state must have _zero_ knowledge of `Hrot.NED` (CycloneDDS messages) so we can eventually swap to Bagira.

**The Flaw:** Currently, `Hrot.SimHost.Components.EntityMissionHolder` and `Hrot.IG.Components.IgMissionHolder` directly embed the `Hrot.NED.Descriptors.EntityMission` DDS struct as an ECS component property. This tightly couples the ECS memory layout of the simulation and presentation tiers directly to the CycloneDDS IDL code generator.

**The Clean Fix:**

-   These holder components should be deleted.-   The internal ECS should exclusively use the pure domain component `MissionPlanQueue`.-   If the IG needs mission data for rendering, the `EntityMissionIngressTranslator` should unpack the DDS struct into a pure C# domain object (like `RoutePlan` or a custom UI model) rather than dumping the raw DDS payload into the ECS.

\--------------------------------------------------------------------------------

These three fixes will seal the remaining leaks in the logic packs. Shall we start by detailing the fix for the **Combat Network Leak** or the **Audio Perception CQRS Violation**?

let's pls go in more detail one by one. pls start with 1. The Other Half of the Combat Network Leak (DamageAssessedEvent)

To completely seal the combat network leak, we must eradicate the `long HitEntityId` from the `DamageAssessedEvent` and push the `NetworkEntityMap` dependency strictly out of the core logic systems into the ingress and egress translators.

Here is the step-by-step refactoring plan to enforce strict CQRS and Anti-Corruption Layer (ACL) boundaries for the Damage Assessment pipeline:

Step 1.1: Purify the Domain Event (`DamageAssessedEvent`)

Currently, `DamageAssessedEvent` holds a network ID, which pollutes the internal event bus with network protocol details. We must change this to use a standard local `Entity` handle.

-   Open `FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs`.-   Modify the struct to replace the `long` network ID with a local `Entity`:

```
[EventId(CombatConstants.DamageAssessedEventId)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DamageAssessedEvent
{
    public Entity HitEntity; // FIXED: Pure local handle instead of long HitEntityId
    public float TotalDamage;
}
```

Step 1.2: Cleanse `DamageCalculationSystem` (Logic Pack)

This system consumes `DetonationNotification` and computes damage. Because we previously purified `DetonationNotification` in Phase 4.C to use local handles, `DamageCalculationSystem` no longer needs to map IDs.

-   Open `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageCalculationSystem.cs`.-   Remove `NetworkEntityMap` from the constructor and class fields entirely.-   Update the `OnUpdate` loop to blindly publish the local handle:

```
// Remove the _entityMap.TryGetEntity lookup.
// Assuming DetonationNotification now has 'Target' (from Phase 4.C)
World.Bus.Publish(new DamageAssessedEvent
{
    HitEntity   = evt.Target, // Use local handle directly
    TotalDamage = CombatConstants.DefaultBulletDamage,
});
```

Step 1.3: Cleanse `HealthApplicationSystem` (Logic Pack)

This system consumes `DamageAssessedEvent` and applies the HP loss to the entity. It currently injects `NetworkEntityMap` just to resolve the `long` ID back to an entity.

-   Open `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs`.-   Remove `NetworkEntityMap` from the constructor and fields.-   Simplify the `OnUpdate` loop to use the provided `Entity` handle directly:

```
// Remove the _entityMap.TryGetEntity lookup.
if (!World.IsAlive(evt.HitEntity))
    continue;

// Check authority and apply damage using evt.HitEntity directly...
ref var health = ref World.GetComponentRW<Health>(evt.HitEntity);
```

Step 1.4: Push ID Mapping to the Translators (Anti-Corruption Layer)

Now that the core logic packs are pure, the network boundary translators must handle the translation between local `Entity` handles and DDS `long` Network IDs.

**1\. Update** **DamageAssessedEgressTranslator** **(Authority Node):** This translator intercepts the local `DamageAssessedEvent` and sends the DDS message.

-   Inject `NetworkEntityMap` into its constructor.-   In `ScanAndPublish`, intercept the event, resolve the ID, and write to DDS:

```
var events = view.ConsumeEvents<DamageAssessedEvent>();
foreach (ref readonly var evt in events)
{
    if (_entityMap.TryGetNetworkId(evt.HitEntity, out long netId))
    {
        _writer.Write(new EntityHitDamage
        {
            HitEntityId = netId,
            TotalDamage = evt.TotalDamage,
        });
    }
}
```

**2\. Update** **EntityHitDamageIngressTranslator** **(Authority Node):** This translator receives the DDS message and drops the local event onto the bus.

-   In `ProcessSample`, resolve the network ID to the local entity before publishing:

```
if (!_entityMap.TryGetEntity(msg.HitEntityId, out var targetEntity)) 
    return;

cmd.PublishEvent(new DamageAssessedEvent
{
    HitEntity = targetEntity, // Map to local ECS handle
    TotalDamage = msg.TotalDamage,
});
```

By making these changes, the `FDP.Toolkit.Combat` assembly achieves 100% decoupling from network identity and transport concepts, making it a pure, testable Logic Pack.

Let me know when you are ready to tackle the **Audio Perception CQRS Violation**.

pls now Audio Perception CQRS Violation

This is a classic "Split-Brain" CQRS violation. Right now, `AudioPerceptionSystem` is trying to play both sides of the fence. It performs spatial distance math using `SimTransform` and `SpatialGridData` (which belong to the Muscle/Perception tier), but then it reaches right across the architectural boundary to directly mutate `TargetMemory` (which is a cognitive component owned by the Brain tier).

In a distributed deployment, no single node holds both the physical spatial grid and the cognitive target memory, meaning this system will silently fail to execute.

To enforce our Anti-Corruption Layer and strict CQRS boundaries, we must extract the mutation logic out of the perception solver and pipe it through the event bus. Here is the step-by-step refactoring plan to fix this:

Step 1: Define the Perception Event

We already have `TargetVisibleEvent` for line-of-sight detection. We need a distinct event for audio detection so the Brain can apply the correct threat weighting.

Create a new event in `FDP.Toolkit.Perception.Events`:

```
[EventId(PerceptionConstants.TargetHeardEventId)] // You'll need to define this ID
[StructLayout(LayoutKind.Sequential)]
public struct TargetHeardEvent
{
    public Entity Listener;
    public int SourceEntityIndex;
    public Vector3 Origin;
}
```

Step 2: Purify `AudioPerceptionSystem` (Muscle/Perception Tier)

We must strip all knowledge of `TargetMemory` out of `AudioPerceptionSystem`. Its only job should be to calculate acoustics and emit an event when a listener is within range.

-   Open `FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs`.-   Remove the `World.HasComponent<TargetMemory>(listener)` guard from the neighbor evaluation loop.-   Instead of calling `TargetMemory.AddOrUpdateTarget(...)`, publish our new event to the bus:

```
// OLD: Bypassing CQRS boundary
// ref var mem = ref World.GetComponentRW<TargetMemory>(listener);
// TargetMemory.AddOrUpdateTarget(ref mem, evt.SourceEntityIndex, evt.Origin.X, evt.Origin.Y, 20f, tick);

// NEW: Clean CQRS event emission
World.Bus.Publish(new TargetHeardEvent 
{
    Listener = listener,
    SourceEntityIndex = evt.SourceEntityIndex,
    Origin = evt.Origin
});
```

Step 3: Consolidate Mutations in `ThreatEvaluationSystem` (Brain Tier)

The `ThreatEvaluationSystem` already acts as the authoritative owner for all `TargetMemory` mutations, consuming `TargetVisibleEvent`s and applying a `50f` score boost. We simply need to extend it to also consume our new audio events.

-   Open `FDP.Toolkit.Perception/Systems/ThreatEvaluationSystem.cs`.-   Add a new consumption loop for `TargetHeardEvent` right after the visual events are processed:

```
var heardEvents = view.ConsumeEvents<TargetHeardEvent>();
foreach (ref readonly var evt in heardEvents)
{
    if (!view.IsAlive(evt.Listener)) continue;
    if (!view.HasComponent<TargetMemory>(evt.Listener)) continue;

    ref readonly var memRO = ref view.GetComponentRO<TargetMemory>(evt.Listener);
    TargetMemory mem = memRO;

    // Apply the acoustic threat boost (20f)
    TargetMemory.AddOrUpdateTarget(
        ref mem,
        entityId:   evt.SourceEntityIndex, // Reconstruct packed value if necessary
        posX:       evt.Origin.X,
        posY:       evt.Origin.Y,
        scoreBoost: 20f, 
        tick:       tick,
        modality:   SensorModality.Acoustic); // If you are tracking modality

    ecb.SetComponent(evt.Listener, mem);
}
```

Step 4: Network Translators (The Anti-Corruption Layer)

Because `AudioPerceptionSystem` now emits `TargetHeardEvent`, we easily bridge this across the CycloneDDS network exactly like we do for other events.

-   **Perception Node (Egress):** Create a translator to catch `TargetHeardEvent` and publish it over DDS (e.g., `AudioTargetDetected`).-   **Brain Node (Ingress):** Create a translator to catch the DDS message and drop the `TargetHeardEvent` onto the Brain's local event bus, where `ThreatEvaluationSystem` will seamlessly pick it up and mutate `TargetMemory`.

By routing this through the event bus, we restore a pure Single Source of Truth. `ThreatEvaluationSystem` becomes the exclusive writer of `TargetMemory`, and our Perception and Brain tiers are completely decoupled!

ok and now pls 3. DDS Structs Hoarded inside ECS Components (ACL Leak)

This is the final nail in the coffin for our network coupling. Right now, `Hrot.SimHost.Components.EntityMissionHolder` and `Hrot.IG.Components.IgMissionHolder` are directly embedding the `Hrot.NED.Descriptors.EntityMission` CycloneDDS struct. Because these components live in the ECS memory space, any system that touches mission data is forced to reference the network IDL layer.

To achieve a true Anti-Corruption Layer, we must banish all DDS data types from the ECS and use pure C# Plain Old CLR Objects (POCOs).

Here is the step-by-step clean architecture fix for **#3 DDS Structs Hoarded inside ECS Components**:

Step 3.1: Define a Pure Domain Mission Model

We need a clean, network-agnostic representation of the mission plan to replace the CycloneDDS struct.

Create a new set of POCOs in `FDP.Toolkit.Behavior.Components`:

```
public class DomainMissionTask
{
    public Guid TaskId;
    public string ExecutingEngine = string.Empty;
    public string BehaviorId = string.Empty;
    public string BehaviorParams = string.Empty; 
    // Triggers and state can be mapped as well
}

public class DomainMissionPlan
{
    public Guid ActiveTaskId;
    public List<DomainMissionTask> Tasks = new();
}
```

Step 3.2: Consolidate and Purify the ECS Component

Instead of having duplicate, DDS-coupled holders in the IG and SimHost (`IgMissionHolder` and `EntityMissionHolder`), we will create a single, unified managed component in the Behavior toolkit.

-   Delete `Hrot.SimHost.Components.EntityMissionHolder`.-   Delete `Hrot.IG.Components.IgMissionHolder`.-   Create a new `ActiveMissionPlan` managed component in `FDP.Toolkit.Behavior.Components`:

Step 3.3: Cleanse the Logic Packs

Now we update our core systems to rely entirely on the pure domain model, stripping away all `Hrot.NED` using directives.

-   **Refactor** **MissionAdapterSystem** **(SimHost):** This system currently extracts JSON behavior parameters by reaching into `EntityMissionHolder.Mission.Plan`. Update it to query `ActiveMissionPlan` instead.-   **Refactor** **MissionRenderLayer** **(IG):** The IG uses the mission holder to draw waypoint lines on the map. Change it to read the new `ActiveMissionPlan` and iterate over `DomainMissionTask`.

Step 3.4: Enforce the Anti-Corruption Layer (Translators)

The network edge is the only place that should know about `Hrot.NED.Descriptors.EntityMission`. We will update the ingress translators to act as a strict factory boundary.

-   **Update** **IgMissionIngressTranslator****:** When a DDS sample arrives, map the properties into our pure POCOs before attaching them to the ECS.-   **Update** **EntityMissionIngressTranslator****:** Apply the exact same mapping logic here. The ECS receives only `ActiveMissionPlan`.-   **Update** **EntityMissionEgressTranslator****:** On egress, read the pure `ActiveMissionPlan` and pack it into the outgoing `Hrot.NED.Descriptors.EntityMission` DDS message.

The Architectural Win

By completing this final step, your ECS memory space is entirely scrubbed of CycloneDDS dependencies. Your AI, Physics, and Rendering logic packs operate exclusively on pure C# structures. You have successfully implemented the Hrot Editor's vision: the network is now a 100% pluggable detail handled exclusively by the Translator Packs.

Dnes • 20:43

