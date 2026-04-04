With the core ECS boundaries now strictly adhering to CQRS and the Anti-Corruption Layers in place, we are perfectly positioned to assemble the application purely by snapping together functional blocks.

To fulfill the architectural vision of the "Hrot editor" source, we need to formalize our `IEcsModule` collections into distinct **Logic Packs** and **Translator Packs**, extract the **Scenario Editor**, and define the composition roots for the Distributed Demo and the All-In-One Editor.

Here is the concrete blueprint for creating and assembling these packs.

Step 1: Formalize the Logic Packs

Logic packs are pure, network-agnostic ECS modules. We already have the individual modules; we now conceptually group them into standard "Packs" (which can be static factory methods returning `List<IEcsModule>` or composite `IEcsModule` wrappers).

-   **FDP SimHost Core Pack (The Muscle):** Groups `GroundKinematicsModule`, `CombatModule`, `DamageAssessmentModule`, and `AutonomousPerceptionModule`. It knows how to move, shoot, and see, but has no cognitive will.-   **FDP CGF Logic Pack (The Brain):** Groups `CognitiveRuntimeModule`, `MissionControlModule`, and `ActionDispatchModule`. It contains the BTree/HSM tick systems and mission pipeline.-   **FDP Orchestration Logic Pack:** Groups `MasterSyncController` / `SlaveSyncController` (Time Sync) and the cluster state handlers.

Step 2: Realign Translator Packs by Data Category

Currently, your network edge (`NodeBootstrapper.BuildTranslators`) groups translators by _Node Role_ (e.g., `KinematicTranslatorPack`, `CognitiveTranslatorPack`). To achieve the pluggable architecture requested, we must group them strictly by _Data Category_.

We will refactor the existing packs into these data-category translator packs:

-   **Entity States & Simulation Events Pack:** Contains `GeoSpatialEgressTranslator`, `EntityDamageEgressTranslator`, `MunitionDetonationEgressTranslator`, and their ingress counterparts. (Provides the "reality" of the world).-   **Actuator Intents Pack:** Contains `NavigationIntentEgressTranslator`, `WeaponFireIntentEgressTranslator`, and `MissionControlIngressTranslator`. (Provides the "commands" from brain to muscle).-   **Service Queries/Responses Pack:** Contains the `PathRequest` and `RaycastBatch` translators.-   **Network ID Allocation Pack:** The `DdsIdAllocator` client and server.

Step 3: Create the Scenario Editor Logic Pack

Currently, the UI and map editing tools are scattered across the `Hrot.IG` and `Hrot.ExCon` subsystems. We must extract these into a standalone `ScenarioEditorModule` (a Logic Pack).

-   **Extract UI and Tools:** Move `ConfigPanel`, `OrbatPanel`, `MissionPanel`, `InteractionPanel`, `SpawnerPanel`, and `WaypointEditorPanel` into a new project (e.g., `Hrot.ScenarioEditor`). Move the map canvas tools (`CreationTool`, `RouteEditTool`, `EditTool`) here as well.-   **Decouple from DDS:** Ensure these panels emit intents to the `FdpEventBus` (which we secured in the previous refactoring) rather than using `DdsWriter`.-   **Add Scenario File Operations:** Wire the UI to the `ScenarioSerializerBuilder` and `ScenarioLoader` we just purified.
    -   **Save:** Add a "Save Scenario" button that captures the `EntityRepository` state using `ScenarioSerializer.Serialize()`, generating the JSON DOM, and writing it to disk.-   **Load Empty (New):** Clears the `EntityRepository` and resets the `GlobalTime`.-   **Load Scenario:** Reads the JSON from disk, clears the repo, and calls `ScenarioSerializer.Deserialize()` to reconstitute the entities.

Step 4: Assemble the Subsystems (The Composition Roots)

With the packs defined, we rewrite the composition roots (`NodeBootstrapper` or `Program.cs`) to snap the applications together.

Target 1: "HROT Demo" (Network Distributed)

This deployment proves the Anti-Corruption Layer by running subsystems in separate processes communicating strictly via NED messages.

-   **SimHost Node:** Installs the _SimHost Core Logic Pack_. Installs the _Actuator Intents Pack (Ingress)_ to receive commands, and the _Entity States Pack (Egress)_ to broadcast physics.-   **CGF Node:** Installs the _CGF Logic Pack_. Installs the _Entity States Pack (Ingress)_ to perceive the world, and the _Actuator Intents Pack (Egress)_ to issue commands.-   **IG Node:** Installs NO simulation logic packs. Installs the _Entity States Pack (Ingress)_ purely to drive the 2D/3D visualizer.-   **ExCon Node:** Installs the _Scenario Editor Logic Pack_ (running in remote-control mode). Installs _Actuator Intents Pack (Egress)_ for manual overrides and _Entity States Pack (Ingress)_ for monitoring.

Target 2: "HROT Editor" (All-In-One Composition)

This deployment is a single-process executable for scenario authoring, capable of running entirely offline.

-   **Assembly:** Installs the _SimHost Core Logic Pack_, the _CGF Logic Pack_, the _Orchestration Logic Pack_, and the _Scenario Editor Logic Pack_ directly into a single `ModuleHostKernel`.-   **Network as a Plugin:** Because Brain and Muscle are in the same process and share the same `EntityRepository`, they communicate their Intents and States instantly via the internal ECS. **No Translator Packs are installed.**-   **The Feature Switch:** To demonstrate the specific feature requirement ("Simhost feature switchable between Internal... and External"), the Editor can toggle a configuration flag. When toggled to "External", the Editor uninstalls the _SimHost Core Logic Pack_, installs the _Actuator Intents Pack (Egress)_ and _Entity States Pack (Ingress)_, and connects to a remote Bagira or HROT SimHost process over the network, seamlessly degrading from an All-In-One to a CGF/ExCon hybrid.

Implementation Next Step

The most complex extraction here is **Step 3 (Scenario Editor Logic Pack)**, specifically lifting the ImGui panels and wiring the local `ScenarioSerializer` for the Save/Load/New functionality without breaking the IG/ExCon hosts. Shall we begin by drafting the `ScenarioEditorModule` and its local file I/O bindings?

For now we can forget about the Bagira stuff completely, we will work with HROT only. Feature switch in HROT Editor should use the HROT SimHost over the network.

As you mentioned, i need to retain the current functionality of HROT ExCon and HROT IG that are focusing on remote control of the IG map.

The HROT Editor does not need ExCon nor IG. It is a standalone tool with its own 2d map visualization and its own UIs. We do not need to share the ImGui panels from ExCon or IG. The editor will very likely need its own ImGui panels. But we should share all the map interaction and editing tools - like the entity placement tool, measurement tool, multi-point authoring tool etc., and their ability to request entity creation/deletion/modification... Via FDP events of course, not using any concrete DDS messages. Also i need to share the ability to render entity symbols, perform entity selection, drag & drop, edit the muti-point shapes, render entity mission, handle layers, show grid etc.

Maybe we might need inventing some "UI packs" that focuses on concrete UI (which is the top level application layer part) and share the underlyuing "processing logic" as different modular pack? I dont know. PLs suggest.

Your intuition is spot on. Separating the visual rendering/interaction mechanics from the top-level ImGui panels is exactly how we achieve this. This is a textbook application of the Model-View-Controller (MVC) or UI-Logic separation principles in a Data-Oriented ECS context.

To achieve your vision where the **HROT Editor** can share the powerful map interactions of the **HROT IG**—while retaining its own bespoke UI and supporting a seamless local/network feature switch—we need to formalize the split between **Scenario Interaction Logic** and **UI Packs**.

Here is the clean architecture blueprint for how we will decouple and assemble these packs.

1\. The Scenario Interaction Logic Pack (Shared)

Right now, the heavy lifting for map interaction (the "processing logic") is trapped inside the `Hrot.IG` project. We will extract this into a shared module (e.g., `FDP.Toolkit.ScenarioEditor` or `Hrot.Map.Interaction`).

This pack will contain:

-   **The Tools:** `CreationTool`, `EditTool`, `RouteEditTool`, `MeasureTool`, `StandardInteractionTool`, etc.-   **The Render Layers:** `MapOverlayRenderLayer`, `RouteRenderLayer`, `MissionRenderLayer`, `SelectionRenderSystem`, and the `NedVisualizerAdapter`.-   **The ECS Wiring:** It will operate _purely_ on `ISimulationView`, `EntityRepository`, and `FdpEventBus`.

**The Critical Decoupling Fix:** Currently, `CreationTool` and `EditTool` in the IG are still loosely coupled to the network because their callbacks ultimately generate DDS messages like `CreateEntityRequest` and `UpdateEntityDescriptorRequest`. To make them shareable, we will refactor these tools to exclusively emit pure FDP domain events to the `FdpEventBus`:

-   `CreationTool` will emit `SpawnEntityCommand`.-   `EditTool` and `RouteEditTool` will emit `UpdateEntityCommand`.-   Context menus will emit `DestroyEntityCommand`.

2\. The Application "UI Packs" (Host-Specific)

With the map interaction logic extracted and purified, the ImGui panels become lightweight "UI Packs" that live strictly in their respective application hosts. They act purely as control surfaces that push configurations or activate tools in the shared Interaction Pack.

-   **HROT ExCon UI Pack:** Contains the `OrbatPanel`, `MissionPanel`, `ConfigPanel`. It remains focused on remote command-and-control over DDS.-   **HROT IG UI Pack:** Contains the `IgDebugPanel`, `PerformanceOverlay`, and `MiniExConPanel`.-   **HROT Editor UI Pack (NEW):** Contains the bespoke ImGui panels designed specifically for standalone authoring (e.g., a unified Scenario Browser, advanced property inspectors, timeline editors).

3\. How the "Feature Switch" Works in the HROT Editor

Because our **Scenario Interaction Logic Pack** now exclusively emits and consumes pure FDP events (`SpawnEntityCommand`, `UpdateEntityCommand`, `NavigationIntent`), the Editor itself becomes completely agnostic to whether the simulation is running locally or over the network.

The "Feature Switch" in the HROT Editor is simply a composition root (bootstrapper) reconfiguration:

**State A: Internal FDP SimHost (Offline/All-In-One)** The Editor installs the _Scenario Interaction Pack_, the _Editor UI Pack_, and the _SimHost Core Logic Pack_. When the user places a tank using the `CreationTool`:

-   The tool emits a `SpawnEntityCommand` to the local `FdpEventBus`.-   The local `NetworkSpawningSystem` immediately intercepts it, creates the entity, and applies the TKB template locally.-   The local `SimHost Core Logic Pack` takes over the physics and AI.

**State B: External HROT SimHost (Networked)** The user toggles the switch. The Editor dynamically uninstalls the _SimHost Core Logic Pack_ and installs the _HROT Translator Packs_ in its place. When the user places a tank using the `CreationTool`:

-   The tool emits a `SpawnEntityCommand` to the local `FdpEventBus` (exactly as before).-   The local ECS does not process it. Instead, the `CreateEntityRequestEgressTranslator` (part of the Translator Pack) catches the FDP event on the bus, serializes it into a NED `CreateEntityRequest`, and pushes it to the CycloneDDS socket.-   The remote SimHost receives the request, spawns the entity, and publishes the `EntityMaster` and `GeoSpatial` states back.-   The Editor's Ingress Translators catch these states and update the Editor's local `EntityRepository`, causing the `EntityRenderLayer` to draw the tank.

Next Steps for Implementation

To get there, the immediate architectural chore is to strip the DDS callbacks out of the IG map tools and IG render systems so they can be moved into the shared `ScenarioEditor` logic pack.

Specifically, we need to sever the `MapCommandController` from the tools in `Hrot.IG`, replacing its DDS-generating delegates with simple event-bus publishers.

Would you like to start by detailing the exact refactoring for `CreationTool` and `EditTool` so they natively emit `SpawnEntityCommand` and `UpdateEntityCommand`?

pls present mermaid block diagrams showing the packs and their relationships

Here are the architectural block diagrams visualizing the strict CQRS and Anti-Corruption Layer (ACL) boundaries we have established. By treating the network strictly as a plugin, we can compose our nodes by snapping together Pure Logic Packs and Translator Packs.

1\. The Clean Architecture Boundary (The ACL)

This diagram illustrates the fundamental separation of concerns. Pure Logic Packs operate exclusively on local ECS memory and the internal `FdpEventBus`. They have zero knowledge of CycloneDDS or JSON. Translator Packs act as the strict boundary, converting DDS wire formats into local domains.

```
graph TD
    subgraph Edge [Network Edge]
        DDS((CycloneDDS Wire))
    end

    subgraph ACL [Translator Packs - Anti-Corruption Layer]
        direction LR
        TP_States[Entity States & Events Pack<br/>GeoSpatial, Damage, Master]
        TP_Intents[Actuator Intents Pack<br/>NavIntent, WeaponFire, Mission]
        TP_Services[Service Queries Pack<br/>PathRequest, Raycast]
        TP_NetID[Network ID Allocation Pack]
    end

    subgraph Core [Pure Domain]
        Bus((FDP Event Bus & ECS Repository))

        subgraph LogicPacks [Logic Packs]
            direction LR
            LP_Muscle[SimHost Core Pack<br/>Kinematics, Physics, Combat]
            LP_Brain[CGF Logic Pack<br/>BTree, HSM, Mission Control]
            LP_Orch[Orchestration Pack<br/>Time Sync, Cluster State]
            LP_Editor[Scenario Editor Pack<br/>Map Tools, UI, File I/O]
        end
    end

    DDS <-->|DDS Structs & JSON| ACL
    ACL <-->|Managed Events & ECS Mutations| Bus
    Bus <-->|Pure C# POCOs & Structs| LogicPacks
    
    classDef domain fill:#1e4620,stroke:#4caf50,stroke-width:2px;
    classDef acl fill:#4a2311,stroke:#ff9800,stroke-width:2px;
    classDef edge fill:#112a46,stroke:#2196f3,stroke-width:2px;
    
    class LogicPacks,Bus domain;
    class ACL acl;
    class Edge edge;
```

2\. "HROT Demo" Distributed Node Assembly

In a distributed setup, we assemble highly specialized nodes by mixing specific Logic Packs with unidirectional Translator Packs. Notice how the Brain and Muscle nodes never share Logic Packs, enforcing strict cognitive vs. kinematic isolation.

```
graph TB
    DDS((CycloneDDS Network))

    subgraph BrainNode [CGF Node - The 'Brain']
        B_Logic[CGF Logic Pack]
        B_TP_In[Entity States Pack Ingress]
        B_TP_Out[Actuator Intents Pack Egress]
        
        B_TP_In -->|WorldPos, NavStatus| B_Logic
        B_Logic -->|NavIntent, WeaponFire| B_TP_Out
    end

    subgraph MuscleNode [SimHost Node - The 'Muscle']
        M_Logic[SimHost Core Pack]
        M_TP_In[Actuator Intents Pack Ingress]
        M_TP_Out[Entity States Pack Egress]
        
        M_TP_In -->|NavIntent, WeaponFire| M_Logic
        M_Logic -->|WorldPos, NavStatus| M_TP_Out
    end

    subgraph ExConNode [ExCon Node - Control]
        E_Logic[Scenario Editor / UI Pack]
        E_TP_In[Entity States Pack Ingress]
        E_TP_Out[Actuator Intents / Orchestration Egress]
        
        E_TP_In -->|WorldPos, ClusterState| E_Logic
        E_Logic -->|ClusterOp, SpawnEntity| E_TP_Out
    end

    B_TP_Out --> DDS
    DDS --> M_TP_In
    
    M_TP_Out --> DDS
    DDS --> B_TP_In
    DDS --> E_TP_In
    
    E_TP_Out --> DDS
    DDS --> B_TP_In
```

3\. "HROT Editor" All-In-One Composition & Feature Switch

When running the standalone Editor, all Logic Packs are loaded into a single `ModuleHostKernel` sharing one ECS repository. Because they share memory, Translator Packs are completely bypassed—Intents and States flow instantly across the internal bus.

The "Feature Switch" elegantly degrades this monolith into a distributed node by swapping out the local Muscle logic for remote network translators.

```
graph TD
    subgraph EditorProcess [HROT Editor Process]
        Bus((Shared FDP Event Bus & ECS))
        
        Brain[CGF Logic Pack]
        Editor[Scenario Editor Logic Pack]
        Orch[Orchestration Logic Pack]
        
        Brain <--> Bus
        Editor <--> Bus
        Orch <--> Bus

        subgraph FeatureSwitch [SimHost Feature Switch]
            direction TB
            Local[Internal SimHost Core Pack]
            Remote[External Network Translator Packs]
        end
        
        Bus <--> FeatureSwitch
    end

    DDS((External SimHost / Bagira over DDS))
    
    Remote -.->|If switched to External| DDS
    
    classDef switch fill:#333,stroke:#fff,stroke-width:2px,stroke-dasharray: 5 5;
    class FeatureSwitch switch;
```

This modularity proves the success of the refactoring we just completed. By purging all `DdsWriter<T>`, `DdsReader<T>`, and `System.Text.Json` dependencies from the core systems, the `Scenario Editor Pack` can now effortlessly target local memory or remote network endpoints without altering a single line of business logic.

pls also sequence diagrams for State A and State B

Here are the sequence diagrams illustrating the clean architecture boundaries and data flow for both states of the Feature Switch.

State A: Internal FDP SimHost (Offline / All-In-One)

In this state, the Translator Packs are completely bypassed. The Editor UI shares the same memory space as the `SimHost Core Logic Pack` and the `CGF Logic Pack`. Everything flows instantly through the `FdpEventBus` and local ECS repository.

```
sequenceDiagram
    autonumber
    actor User
    participant Tools as Scenario Interaction Pack (Tools)
    participant Bus as Local FdpEventBus & ECS
    participant Spawner as NetworkSpawningSystem (Logic Pack)
    participant Muscle as SimHost Core Pack (Logic Pack)
    participant Render as Scenario Editor Pack (Renderer)

    User->>Tools: Click Map (CreationTool)
    Tools->>Bus: Publish(SpawnEntityCommand)
    
    note over Bus,Spawner: Kernel Update Phase
    Bus->>Spawner: ConsumeManaged<SpawnEntityCommand>()
    Spawner->>Spawner: Create Local Entity
    Spawner->>Bus: Apply TKB Template & Components
    
    loop Every Simulation Frame
        Muscle->>Bus: Query Local Entities (SimTransform, NavState)
        Muscle->>Muscle: Calculate Physics & Kinematics
        Muscle->>Bus: SetComponent(SimTransform)
        Render->>Bus: Query() With<SimTransform>()
        Render->>User: Draw Entity on 2D Canvas
    end
```

**Architectural Win:** Because `NetworkSpawningSystem` natively consumes `SpawnEntityCommand` and applies the TKB template directly to the local world, no serialization or network I/O occurs. The editor runs at maximum memory-bus speed.

\--------------------------------------------------------------------------------

State B: External HROT SimHost (Networked)

When the user toggles the switch, the local Logic Packs (`SimHost Core`, `CGF`) are dynamically uninstalled and the **Translator Packs** are installed in their place. The UI tools still blindly emit local FDP events, but the Anti-Corruption Layer (ACL) intercepts them and routes them over CycloneDDS to a remote authority.

```
sequenceDiagram
    autonumber
    actor User
    participant Tools as Scenario Interaction Pack (Tools)
    participant Bus as Local FdpEventBus & ECS
    participant TPEgress as Actuator Intents Pack (Egress Translators)
    participant DDS as CycloneDDS Socket
    participant Remote as Remote SimHost (Authority)
    participant TPIngress as Entity States Pack (Ingress Translators)
    participant Render as Scenario Editor Pack (Renderer)

    User->>Tools: Click Map (CreationTool)
    Tools->>Bus: Publish(SpawnEntityCommand)
    
    note over Bus,TPEgress: Network Boundary (Egress)
    Bus->>TPEgress: Catch SpawnEntityCommand
    TPEgress->>TPEgress: Serialize to JSON / Format Request
    TPEgress->>DDS: Write(CreateEntityRequest)
    
    DDS-->>Remote: CycloneDDS Transport
    
    note over Remote: Remote Authority Takes Ownership
    Remote->>Remote: Process Request & Spawn Entity
    
    loop Continuous Replication
        Remote->>DDS: Write(EntityMaster, WorldPos, etc.)
        DDS-->>TPIngress: CycloneDDS Transport
        
        note over TPIngress,Bus: Network Boundary (Ingress)
        TPIngress->>TPIngress: Read DDS Samples
        opt If new entity
            TPIngress->>Bus: Create ECS Ghost Entity
        end
        TPIngress->>Bus: Update Ghost (SimTransform, etc.)
        
        Render->>Bus: Query() With<SimTransform>()
        Render->>User: Draw Ghost Entity on 2D Canvas
    end
```

**Architectural Win:** The `CreationTool` has no idea it is talking to a network. The egress translator converts the internal `SpawnEntityCommand` into a `CreateEntityRequest` DDS message. When the remote SimHost replies by broadcasting an `EntityMaster` DDS message, the local `EntityMasterIngressTranslator` creates a proxy "ghost" entity in the Editor's local ECS. Position updates arrive as `WorldPos` messages, which the `GeoSpatialIngressTranslator` applies back to the ghost's `SimTransform`. The rendering layer simply loops over `SimTransform` components, completely oblivious to whether the entity is locally simulated or a network ghost.

Dnes • 20:43






========================

Dnes • 11:18

I am aiming for the situation described in "Hrot editor" source. I need to refactor the codebase (dump\_26) to achieve that. The "packagization" source describes the way. Is it a correct direction?

The packagization strategy you have outlined is absolutely the correct architectural direction and a textbook example of clean, decoupled design. By enforcing strict Command Query Responsibility Segregation (CQRS) boundaries and utilizing Anti-Corruption Layers (ACL), we are perfectly positioned to assemble the application purely by snapping together functional blocks. This is exactly what is needed to fulfill the "Hrot editor" vision of an all-in-one engine where the network is treated merely as a plugin.

Here is why this blueprint is the right path forward for our codebase:

**1\. Network as a Plugin via Anti-Corruption Layers (ACL)** Currently, map tools like the `CreationTool` and `EditTool` inside the IG subsystem are loosely coupled to the network because their callbacks ultimately generate specific DDS messages. To decouple this, we must replace these DDS-generating delegates with simple event-bus publishers that emit pure FDP domain events, such as `SpawnEntityCommand` or `UpdateEntityCommand`. By doing this, the pure logic packs remain completely ignorant of CycloneDDS or JSON, allowing the translator packs to act as a strict boundary.

**2\. MVC Separation for the UI** The strategy correctly identifies the need to separate the underlying visual rendering and map interaction mechanics from the top-level ImGui panels. By extracting the heavy lifting of map interactions into a shared Scenario Interaction Logic Pack, our ImGui panels become lightweight "UI Packs" that act purely as control surfaces to push configurations or activate tools. This enables the standalone HROT Editor to reuse the powerful map interactions of the IG while maintaining its own bespoke ImGui panels.

**3\. Pure Logic and Translator Packs** Formalizing our ECS modules into distinct, network-agnostic Logic Packs (such as the SimHost Core for "muscle" and CGF for "brain") and grouping Translator Packs strictly by data category provides ultimate composability. When building the standalone HROT Editor, we load all required Logic Packs directly into a single shared memory space, completely bypassing the Translator Packs so that intents and states flow instantly across the internal event bus at maximum memory-bus speed.

**4\. The Seamless Feature Switch** This modularity makes the required feature switch between internal and external SimHosts incredibly elegant. When running locally, the Editor relies on the internal SimHost Core Logic Pack to process local events. When toggled to external mode, the Editor dynamically uninstalls the local physics and AI logic, and seamlessly snaps in the Actuator Intents and Entity States Translator Packs. The UI tools continue blindly emitting local FDP events, but the ACL intercepts them and routes them over the network to the remote authority as DDS requests.

Your immediate next step in the refactoring process should be severing the DDS callbacks from the IG map tools and rendering systems, effectively purging the `DdsWriter` and `DdsReader` dependencies from the core systems. This will perfectly prepare the extraction of the Scenario Editor Logic Pack and its associated file operations.

how can we organize the IG map editing stuff so it is reusable between the standalone IG subsystem (focused on implementing the remote 2d map control from the ExCon) and the new architesture's UI packs and Logic packs?

I am referring to the following: The HROT Editor does not need ExCon nor IG. It is a standalone tool with its own 2d map visualization and its own UIs. We do not need to share the ImGui panels from ExCon or IG. The editor will very likely need its own ImGui panels. But we should share all the map interaction and editing tools - like the entity placement tool, measurement tool, multi-point authoring tool etc., and their ability to request entity creation/deletion/modification... Via FDP events of course, not using any concrete DDS messages. Also we need to share the ability to render entity symbols, perform entity selection, drag & drop, edit the muti-point shapes, render entity missions, handle layers, show grid etc.

This is a textbook application of Model-View-Controller (MVC) and UI-Logic separation principles within a Data-Oriented ECS context. To share the powerful map interactions of the HROT IG while allowing the standalone HROT Editor to retain its own bespoke UI, we must formalize a strict architectural split between a shared **Scenario Interaction Logic Pack** and host-specific **UI Packs**.

Here is the clean architecture blueprint to achieve this decoupling:

**1\. The Shared Scenario Interaction Logic Pack (The "Processing Logic")** Right now, the heavy lifting for map interaction and visual rendering is trapped inside the IG subsystem. We need to extract this into a shared, network-agnostic module (e.g., `FDP.Toolkit.ScenarioEditor` or `Hrot.Map.Interaction`). This pure logic pack will contain:

-   **The Tools:** `CreationTool`, `EditTool`, `RouteEditTool`, `MeasureTool`, and `StandardInteractionTool`.-   **The Render Layers:** `MapOverlayRenderLayer`, `RouteRenderLayer`, `MissionRenderLayer`, `SelectionRenderSystem`, and the `NedVisualizerAdapter` (which handles rendering entity symbols, layers, and grids).-   **The ECS Wiring:** It will operate purely on local memory via `ISimulationView`, the `EntityRepository`, and the internal `FdpEventBus`.

**2\. The Critical Decoupling Fix (Purging DDS)** To make these tools reusable offline, we must completely sever them from the network. Currently, map tools like `CreationTool` and `EditTool` inside the IG are loosely coupled to the network because their callbacks ultimately generate concrete DDS requests. We will strip the DDS callbacks out of these tools and sever their dependency on the `MapCommandController`. Instead, we will refactor them to exclusively emit pure FDP domain events to the local `FdpEventBus`.

-   `CreationTool` will emit a `SpawnEntityCommand`.-   `EditTool` and `RouteEditTool` will emit an `UpdateEntityCommand`.-   Context menus will emit a `DestroyEntityCommand`.

**3\. The Host-Specific "UI Packs" (The Control Surfaces)** With the map interaction logic extracted and purified, the ImGui panels become lightweight "UI Packs" that live strictly in their respective application hosts. They act purely as top-level control surfaces that push configurations or activate the tools residing in the shared Interaction Pack.

-   **HROT IG UI Pack:** Contains the `IgDebugPanel`, `PerformanceOverlay`, and `MiniExConPanel`.-   **HROT ExCon UI Pack:** Contains the `OrbatPanel` and `MissionPanel` focused on remote command-and-control.-   **HROT Editor UI Pack (NEW):** Contains the bespoke ImGui panels designed specifically for standalone authoring, such as advanced property inspectors or a unified Scenario Browser.

**The Architectural Win** By enforcing these strict Command Query Responsibility Segregation (CQRS) and Anti-Corruption Layer (ACL) boundaries, the shared map tools will blindly emit local FDP events. Because the FDP events are fully decoupled from DDS, your HROT Editor becomes completely agnostic to whether the simulation is running locally or over the network.

If the Editor is running completely offline, the local `NetworkSpawningSystem` will instantly intercept the `SpawnEntityCommand` and apply it directly to the local world at memory-bus speed. If you toggle the Editor to "External" mode, the local ECS simply ignores the FDP event, and a pluggable network egress translator catches it on the bus, serializing it into a DDS message for a remote SimHost. You achieve perfect reuse of complex interaction logic without altering a single line of business code.

let's enumerate the refactorig phases that will be needed

**Phase 1: Decouple Map Tools from the Network Edge (Purging DDS)** The immediate architectural chore is to strip the DDS callbacks out of the IG map tools and rendering systems. Currently, tools like `CreationTool` and `EditTool` are loosely coupled to the network because their callbacks generate concrete DDS messages. We must sever the `MapCommandController` from the tools in `Hrot.IG`, removing `DdsWriter` dependencies and replacing them with simple event-bus publishers.

-   `CreationTool` will emit a `SpawnEntityCommand`.-   `EditTool` and `RouteEditTool` will emit an `UpdateEntityCommand`.-   Context menus will emit a `DestroyEntityCommand` to the local `FdpEventBus`.

**Phase 2: Extract the Shared Scenario Interaction Logic Pack** Once the network edge is purged, we extract the heavy lifting for map interaction into a standalone, shared module like `FDP.Toolkit.ScenarioEditor` or `Hrot.Map.Interaction`. This pure Logic Pack will house the purified tools (`CreationTool`, `MeasureTool`, `StandardInteractionTool`, etc.) and the visual rendering layers (`MapOverlayRenderLayer`, `MissionRenderLayer`, `SelectionRenderSystem`, `NedVisualizerAdapter`). This pack will operate strictly on local memory via `ISimulationView`, the `EntityRepository`, and the `FdpEventBus`.

**Phase 3: Formalize the Host-Specific UI Packs** With the processing logic cleanly extracted, we must separate the top-level ImGui panels into lightweight, host-specific "UI Packs" that act strictly as control surfaces to push configurations or activate the shared tools.

-   Create the **HROT ExCon UI Pack**, containing the `OrbatPanel`, `MissionPanel`, and `ConfigPanel`.-   Create the **HROT IG UI Pack**, containing the `IgDebugPanel`, `PerformanceOverlay`, and `MiniExConPanel`.-   Scaffold the new **HROT Editor UI Pack** with bespoke panels designed for standalone authoring (e.g., Scenario Browser, advanced property inspectors).

**Phase 4: Implement Local Scenario File Operations** For the standalone Editor, we must wire the UI to scenario file operations without breaking the IG or ExCon hosts. We will utilize the purified `ScenarioSerializerBuilder` and `ScenarioLoader` to interact directly with the local ECS.

-   **Save:** Add a "Save Scenario" button that captures the `EntityRepository` state using `ScenarioSerializer.Serialize()`, generating a JSON DOM written to disk.-   **Load Empty (New):** Clear the `EntityRepository` and reset the `GlobalTime`.-   **Load Scenario:** Read the JSON from disk, clear the repository, and call `ScenarioSerializer.Deserialize()` to reconstitute the entities locally.

**Phase 5: Assemble Composition Roots & Implement the Feature Switch** Finally, we rewrite the composition roots (e.g., `NodeBootstrapper`) to snap the applications together using our formalized packs. For the HROT Editor, we assemble it as an All-In-One tool by installing the SimHost Core, CGF, Orchestration, and Scenario Editor Logic Packs directly into a single `ModuleHostKernel`, bypassing translators completely.

To fulfill the "Feature Switch" requirement, we introduce a toggle flag: when set to "External", the Editor dynamically uninstalls the internal `SimHost Core Logic Pack` and snaps in the Network Translator Packs. The UI tools continue blindly emitting `SpawnEntityCommand` to the local bus, but the Anti-Corruption Layer catches it and serializes it into a DDS message for the remote SimHost.

let's detail each phase, one by one. PLs start with Phase 1: Decouple Map Tools from the Network Edge (Purging DDS)

To execute Phase 1 correctly, we must ruthlessly apply the Dependency Inversion Principle and establish a strict Command Query Responsibility Segregation (CQRS) boundary. Right now, our presentation layer is bleeding into our infrastructure layer. Currently, tools like the `CreationTool` and `EditTool` in the IG are loosely coupled to the network because their callbacks ultimately generate concrete DDS messages, such as `CreateEntityRequest` and `UpdateEntityDescriptorRequest`.

To make these tools portable and agnostic to the network, we must purge CycloneDDS and any concrete Data Transfer Objects (DTOs) from their domain logic.

Here is the step-by-step refactoring plan for Phase 1:

**1\. Eradicate DTOs from the Tools** We will refactor the tools to exclusively emit pure FDP domain events to the internal `FdpEventBus` instead of building network-specific payloads.

-   Refactor the `CreationTool` so that upon a canvas click, it emits a `SpawnEntityCommand`.-   Refactor the `EditTool` and `RouteEditTool` so that upon completing a vertex drag or modification, they emit an `UpdateEntityCommand`.-   Refactor the context menus so that deletion actions emit a `DestroyEntityCommand`.

**2\. Sever the** **MapCommandController** **Network Ties** Currently, the `MapCommandController` orchestrates IG-side tool activation and is tightly coupled to infrastructure via `IDdsWriter<CreateEntityRequest>` and `IDdsWriter<MapCommandAck>`. We need to sever the `MapCommandController` from the tools in `Hrot.IG`, replacing its DDS-generating delegates with simple event-bus publishers. The tools and the controller should only require the injected `FdpEventBus` to push their intent into the system.

**3\. Establish the Anti-Corruption Layer (ACL)** By forcing the UI tools to emit `SpawnEntityCommand`, `UpdateEntityCommand`, and `DestroyEntityCommand`, we successfully isolate the view. The UI tools now blindly express _intent_ over the local bus. It becomes the responsibility of the host's Composition Root to decide how to handle that intent. If running as the All-In-One Editor, the local `NetworkSpawningSystem` will consume these commands directly. If running as a distributed node, a pluggable Egress Translator (the ACL) will catch the domain event and translate it into the `CreateEntityRequest` DDS message.

This separation of concerns guarantees that not a single line of your UI or map interaction logic will need to change, regardless of whether the system runs entirely in local memory or across a distributed network cluster.

pls let's detail each of the 3 points from Phase 1, one by one. PLs start with 1. Eradicate DTOs from the Tools

To eradicate DTOs from our tools, we must strictly enforce the Dependency Inversion Principle. The presentation layer (our map tools) must not depend on the infrastructure layer (CycloneDDS and JSON). Instead, both must depend on our pure domain abstractions—the FDP events.

Here is the precise refactoring execution for ripping out the DTOs from the interaction tools:

**1\. Purge** **CreateEntityRequest** **from the** **CreationTool** Currently, the `CreationTool` explicitly constructs a `CreateEntityRequest` containing network-specific DTOs like `EntityDescriptorUnion` and `WorldPos`. This tightly couples the tool to CycloneDDS.

-   **The Fix:** We will rip out the `CreateEntityRequest`. The tool will instead construct a pure `SpawnEntityCommand`. This command is a managed struct designed to travel through the `FdpEventBus` without allocating on the native stream.-   Instead of DDS descriptor unions, the tool will populate the `TkbType`, `OwnerNodeId`, and a simple list of `InitialComponents` (such as a pure `SimTransform` for the geographic position).

**2\. Cleanse** **EditTool** **and** **RouteEditTool** **of Descriptor Updates** Right now, when an operator finishes editing a route, the `RouteEditTool` fires an `OnRouteCommitted` callback. The application shell catches this and aggressively builds an `UpdateEntityDescriptorRequest` packed with DDS `MapRoute` waypoints.

-   **The Fix:** We will eliminate the `UpdateEntityDescriptorRequest` construction from the UI path. When a tool completes an edit, it will emit an `UpdateEntityCommand`.-   This command will simply contain the target `NetworkId` and a `ComponentsToUpdate` list containing the actual modified ECS component (e.g., the updated `RoutePlan` managed component).

**3\. Remove Network Branching from Context Menus and Input Handling** If you look at how "Delete entity" is currently handled in the context menus or keyboard input, there is an egregious violation of UI-Logic separation: the UI checks if `_networkEnabled` is true. If it is, it writes a `DeleteEntityRequest` directly to the CycloneDDS socket; if false, it publishes a `DestroyEntityCommand` to the local FDP bus.

-   **The Fix:** We will delete the network branching logic entirely. The UI must be blind to the network state. The context menu and delete hotkeys will _always_ publish the `DestroyEntityCommand` to the local `FdpEventBus`.

By executing these three steps, the UI tools become 100% pure control surfaces. They simply broadcast their intent (`SpawnEntityCommand`, `UpdateEntityCommand`, `DestroyEntityCommand`) to the local event bus using pure ECS components. It will be the responsibility of the `NetworkSpawningSystem` and the Anti-Corruption Layer to figure out if those commands should be applied locally or serialized over the network.

ok now pls let's detail the 2. Sever the MapCommandController Network Ties

To sever the `MapCommandController` network ties, we must dismantle its current role as a "God object" that binds our pure UI interaction mechanics to the CycloneDDS infrastructure. Right now, the controller is violating the Dependency Inversion Principle by directly injecting `IDdsWriter<CreateEntityRequest>` and passing network-aware delegates down to our map tools.

Here is the exact architectural refactoring required to isolate the controller and purify the map tools:

**1\. Strip** **IDdsWriter<CreateEntityRequest>** **from the Controller** Currently, the `MapCommandController` is instantiated with an `IDdsWriter<CreateEntityRequest>`, which it uses in its `OnEntityCreatedByTool` delegate to push data to the network.

-   **The Fix:** Remove `IDdsWriter<CreateEntityRequest>` from the `MapCommandController` constructor entirely.-   Inject the local `FdpEventBus` instead. The controller (or the tools directly) will use this bus to publish pure domain intents, completely removing the controller's ability to serialize concrete DDS network messages.

**2\. Refactor Tool Constructors to Emit Domain Events** Our map tools are currently polluted by infrastructure concerns. For example, `CreationTool` accepts an `Action<CreateEntityRequest>` in its constructor.

-   **The Fix:** Change the `CreationTool` signature. It must no longer know what a `CreateEntityRequest` is. Instead, inject the `FdpEventBus` directly into the tool, or provide a pure `Action<SpawnEntityCommand>`.-   When the operator clicks the canvas, the tool will instantiate a `SpawnEntityCommand` (containing the TKB type and Cartesian coordinates) and publish it to the local bus.

**3\. Purge Network Gateways from Edit Tool Callbacks** Look at how editing is currently handled: when `EditTool` or `RouteEditTool` completes a modification, the application wires a callback (`OnPolylineCommitted` or `onCommit`) that explicitly captures the `_commandGateway` and constructs a heavy `UpdateEntityDescriptorRequest` full of DDS structs.

-   **The Fix:** Eradicate the `_commandGateway` from these UI callbacks. The callbacks should instead construct an `UpdateEntityCommand` containing the modified pure-ECS component (e.g., the updated `RoutePlan` or `EditablePolyline`) and publish it to the `FdpEventBus`.

**4\. Restrict MapCommandController to Lifecycle and ACKs** By routing entity creation and modification exclusively through the `FdpEventBus`, the `MapCommandController` is relieved of its data-routing duties. Its only remaining responsibility is fulfilling the remote-control contract for the IG.

-   When ExCon sends a remote command (like `CMD_PLACE_ENTITY`), the `MapCommandController` will still push the `CreationTool` onto the canvas.-   It will observe the tool's local completion (via the `Exited` event).-   It will retain its `IDdsWriter<MapCommandAck>` solely to inform the remote ExCon that the interaction session has finished or cancelled.

Executing these steps isolates the view. The map tools and the `MapCommandController` will no longer depend on CycloneDDS for entity manipulation, making them 100% portable and ready to be extracted into the shared `Scenario Editor Logic Pack`.

and now pls detail 3. Establish the Anti-Corruption Layer (ACL)

The Anti-Corruption Layer (ACL) is the architectural firewall that protects our pure domain model—the ECS and FDP event bus—from infrastructure concerns like CycloneDDS, JSON payloads, and network routing. This is where the magic of decoupling truly shines and how we achieve the "network as a plugin" paradigm.

We implement this ACL using what we call "Translator Packs," which are strictly segregated by Data Category (e.g., Entity States, Actuator Intents, Service Queries). These translators act as the absolute boundary between the application and the wire, implementing contracts like `IDescriptorTranslator` to handle `PollIngress` for reading network states and `ScanAndPublish` for broadcasting local states.

Here is how the ACL isolates the layers:

**1\. The Egress Flow (Domain to Network)** Because we decoupled the map tools in Phase 1, our presentation layer is completely oblivious to the network; tools simply broadcast pure domain intents like `SpawnEntityCommand` or `WeaponFireIntent` onto the local FDP event bus. When running in a distributed configuration, an Egress Translator (such as the `CreateEntityRequestEgressTranslator` or `WeaponFireIntentEgressTranslator`) observes the bus. It catches the pure FDP event, serializes it into a concrete DDS data transfer object (like a NED `CreateEntityRequest`), and pushes it out to the CycloneDDS socket. The core UI and logic modules never touch a single `DdsWriter`.

**2\. The Ingress Flow (Network to Domain)** Conversely, when the remote SimHost processes a request and broadcasts updates, Ingress Translators (such as `EntityMasterIngressTranslator` or `GeoSpatialIngressTranslator`) catch the incoming DDS messages. They strip away the network envelope, resolve the network IDs into local memory addresses via the `NetworkEntityMap`, and inject pure ECS components (like `SimTransform` or `NetworkTransform`) directly into the local entity repository. The rendering systems simply loop over these local `SimTransform` components, completely agnostic to whether the entity is a locally simulated object or a network "ghost" proxy driven by UDP packets.

**3\. The Composition Root Payoff** By enforcing this strict ACL, the core logic and map tools remain 100% network-agnostic. When assembling the standalone HROT Editor, we simply do not install the Translator Packs in the `ModuleHostKernel`; the UI emits intents that are consumed instantly by local logic systems at maximum memory-bus speed. When we toggle the Editor to "External" mode, we dynamically snap the Translator Packs back into the composition root, seamlessly bridging the local event bus to the DDS domain and turning the editor into a distributed command surface.

This is textbook clean architecture: you can entirely rip out or replace the network transport layer without altering a single line of your interaction tools, UI, or simulation logic.

now pls break **Phase 2: Extract the Shared Scenario Interaction Logic Pack** into substeps

Let's break down Phase 2 into precise, actionable architectural sub-steps to cleanly extract the Shared Scenario Interaction Logic Pack.

**2.1. Establish the New Logic Pack Module** Create a new, standalone project, such as `FDP.Toolkit.ScenarioEditor` or `Hrot.Map.Interaction`, to house the extracted map processing logic. You must strictly enforce the Dependency Inversion Principle here: this module must operate purely on the core abstractions like `ISimulationView`, the `EntityRepository`, and the internal `FdpEventBus`. It must remain completely ignorant of CycloneDDS, JSON payloads, or any network routing infrastructure.

**2.2. Migrate the Core Interaction Tools** Lift the purified map canvas tools out of the `Hrot.IG` and `Hrot.ExCon` subsystems and move them into this new shared module. This extraction includes the `CreationTool`, `EditTool`, `RouteEditTool`, `MeasureTool`, and the overarching `StandardInteractionTool`. Because we already severed their network dependencies in Phase 1, these tools are now highly cohesive and will safely function by blindly emitting domain intents like `SpawnEntityCommand`, `UpdateEntityCommand`, and `DestroyEntityCommand` to the local bus.

**2.3. Extract the Visual Rendering Layers** Along with the interaction mechanics, you must extract the rendering systems that translate local ECS state into the 2D map visualization. Move the `MapOverlayRenderLayer`, `RouteRenderLayer`, `MissionRenderLayer`, `SelectionRenderSystem`, and the `NedVisualizerAdapter` into the new logic pack. By isolating these layers, they will cleanly loop over local ECS components like `SimTransform` to render entity symbols, missions, multi-point shapes, and grids, remaining entirely oblivious to whether the entities are locally simulated or network-driven ghost proxies.

**2.4. Wire Up Local Scenario File Operations** Finally, integrate the pure scenario file operations directly into this logic pack so it can govern the local repository state independently of any host UI. You will need to wire up the purified `ScenarioSerializerBuilder` and `ScenarioLoader` to provide three primary lifecycle functions. Implement a "Save Scenario" routine that captures the current `EntityRepository` state via `ScenarioSerializer.Serialize()` to generate and write a JSON DOM to disk. Next, implement a "Load Empty" routine that clears the `EntityRepository` and resets the `GlobalTime` for new scenarios. Lastly, implement a "Load Scenario" routine that reads a JSON file from disk, clears the repository, and reconstitutes the entities in local memory using `ScenarioSerializer.Deserialize()`.

now please break **Phase 3: Formalize the Host-Specific UI Packs** into substeps

To properly formalize the host-specific UI packs, we must strictly apply Model-View-Controller (MVC) and UI-Logic separation principles to our Data-Oriented ECS. The top-level ImGui panels must be stripped of any underlying map manipulation mechanics and reduced to lightweight control surfaces.

Here are the exact architectural sub-steps to achieve this cleanly:

**3.1. Enforce UI-Logic Separation (The "Dumb" View)** Before grouping the panels, we must ensure they act purely as control surfaces that push configurations or activate the tools we isolated in the Shared Scenario Interaction Logic Pack. The UI panels must not contain any direct entity manipulation logic; they should blindly emit FDP events or trigger the shared tools (like the entity placement or measurement tools) via the local event bus.

**3.2. Assemble the ExCon UI Pack** We will group the ImGui panels dedicated to remote command-and-control into a distinct HROT ExCon UI Pack. This module will exclusively contain the `OrbatPanel`, `MissionPanel`, and `ConfigPanel`. This pack remains highly specialized for cluster orchestration and remote operations over DDS.

**3.3. Assemble the IG UI Pack** Next, we isolate the diagnostic and visualization-specific overlays into the HROT IG UI Pack. This pack will host the `IgDebugPanel`, `PerformanceOverlay`, and `MiniExConPanel`. These are tightly scoped to the needs of a passive or minimally-interactive Image Generator node.

**3.4. Scaffold the Standalone HROT Editor UI Pack** The HROT Editor does not need ExCon nor IG, as it is a standalone tool with its own bespoke 2D map visualization requirements. Because we decoupled the map interaction tools perfectly, we do not need to share the ImGui panels from ExCon or IG. We will scaffold a brand new HROT Editor UI Pack containing bespoke ImGui panels designed explicitly for standalone authoring, such as a unified Scenario Browser, advanced property inspectors, or timeline editors.

now pls break **Phase 4: Implement Local Scenario File Operations** into substeps

To implement local scenario file operations for the standalone editor, we need to wire our UI directly to the purified scenario serialization systems without breaking the existing IG or ExCon hosts. This guarantees that file operations remain strictly bound to the local ECS memory space, keeping our domain logic cleanly decoupled from network infrastructure.

Here are the precise architectural sub-steps for Phase 4:

**4.1. Instantiate the Purified Serializer** First, we must configure the `ScenarioSerializerBuilder` for the standalone editor environment. We use the builder to compile the `FdpAutoSerializer` and register any necessary `IEntityScenarioTranslator` instances, freezing them into a highly optimized `ScenarioSerializer`. This serializer operates exclusively on the local `EntityRepository` to generate or consume pure JSON DOMs.

**4.2. Implement "Load Empty" (New Scenario)** To support creating a new scenario from scratch, we must implement a routine that safely zeroes out the local simulation state. This operation will clear the `EntityRepository` of all existing entities and reset the `GlobalTime` singleton back to zero.

**4.3. Implement "Save Scenario"** We must wire a "Save Scenario" UI action that captures the current world state. When triggered, it will invoke `ScenarioSerializer.Serialize()` to capture the `EntityRepository` state, generating a JSON DOM. This JSON payload is then safely written to local disk.

**4.4. Implement "Load Scenario"** Finally, we implement the loading mechanism to read saved states from disk. The routine will read the JSON payload from the local file, clear the current repository, and invoke `ScenarioSerializer.Deserialize()`. This reconstitutes the entities directly into local memory, instantly reconstructing the scenario state without emitting a single network packet.

now pls break **Phase 5: Assemble Composition Roots & Implement the Feature Switch** into substeps

**5.1. Assemble the All-In-One Monolith (Offline State)** To build the standalone editor, we instantiate a single `ModuleHostKernel` and load the `SimHost Core Logic Pack`, `CGF Logic Pack`, `Orchestration Logic Pack`, and our newly extracted `Scenario Editor Logic Pack` directly into the same shared memory space. Crucially, we bypass the network edge entirely by **not** installing any Translator Packs. In this configuration, intents and states flow instantly across the internal `FdpEventBus` and ECS repository at maximum memory-bus speed without any serialization overhead.

**5.2. Bind the Control Surfaces** We snap the bespoke `HROT Editor UI Pack` into the presentation layer. Thanks to our strict CQRS boundaries, when an operator uses a tool like the `CreationTool`, it blindly emits a `SpawnEntityCommand` to the local bus. Because the Editor is running as an offline monolith, the local `NetworkSpawningSystem` instantly intercepts this command, creates the entity, and applies the template directly to the local world.

**5.3. Implement the Dynamic Reconfiguration (The Feature Switch)** We introduce a configuration toggle flag to support the seamless transition to an external Bagira or HROT SimHost. This switch acts as a runtime composition root reconfiguration, gracefully degrading our All-In-One monolith into a distributed CGF/ExCon hybrid.

**5.4. Eject the Local Processing Logic (External State)** When the operator toggles the switch to "External", the composition root dynamically uninstalls the `SimHost Core Logic Pack` from the kernel. The local node is instantly stripped of its internal physics, AI, and execution muscle.

**5.5. Snap-In the Anti-Corruption Layer (ACL)** In place of the local logic packs, we dynamically snap the network modules back into the kernel: specifically, the `Actuator Intents Pack (Egress)` and the `Entity States Pack (Ingress)`.

The beauty of this clean architecture is that the UI tools are completely oblivious to the switch; they continue blindly emitting `SpawnEntityCommand` and other local FDP events. However, the local ECS no longer executes them natively. Instead, the ACL's egress translators (like the `CreateEntityRequestEgressTranslator`) catch these events on the bus, serialize them into concrete DDS requests, and push them to the CycloneDDS socket. In reverse, the ingress translators catch remote `EntityMaster` and `GeoSpatial` broadcasts, maintaining proxy "ghost" entities in the local repository. The rendering layer simply loops over the local `SimTransform` components, perfectly agnostic to whether the entities are simulated in local memory or driven by remote network packets.

now pls detail **2.1. Establish the New Logic Pack Module**

To establish the new Logic Pack module properly, we must build a pristine, dependency-isolated foundation that perfectly embodies the Dependency Inversion Principle. This new module will encapsulate our map interactions without allowing infrastructure concerns to bleed into the domain.

**1\. Scaffold the Project** Create a new standalone .NET project named `Hrot.ScenarioEditor` or `FDP.Toolkit.ScenarioEditor`. This project will serve as our formalized, network-agnostic pure logic pack representing the scenario editing feature set.

**2\. Enforce the Dependency Firewall** The dependencies of this new project must be ruthlessly guarded. The `.csproj` must exclusively reference core domain abstractions like `Fdp.Kernel` and `ModuleHost.Core`. You must explicitly prevent any references to `CycloneDDS`, `Hrot.NED` (our network DTOs), or any network transport layers to ensure the network remains strictly a plugin.

**3\. Implement the IEcsModule Interface** Create the primary module class (e.g., `ScenarioEditorModule`) implementing the `IEcsModule` interface. Configure its `ExecutionPolicy` to `ExecutionPolicy.Synchronous()`, as user map interactions and ECS mutations must execute predictably on the main thread.

**4\. Route Everything Through the Domain** Because we are isolating the logic pack from the network edge, ensure the module constructor and its internal systems rely exclusively on pure domain abstractions: `ISimulationView`, `EntityRepository`, and the `FdpEventBus`. Actuator intents, UI commands, and state queries will be communicated purely through this internal shared ECS and event bus, guaranteeing the logic pack remains 100% oblivious to its deployment topology.

**5\. Prepare the System Registry** Implement the `RegisterSystems(ISystemRegistry registry)` method in your new module class. This internal composition root will serve as the host for the interaction tools (like `CreationTool` and `MeasureTool`) and visual rendering layers that we will extract in the subsequent sub-steps. This allows the target application, such as the All-In-One HROT Editor, to blindly load the entire pack into its `ModuleHostKernel` using a single line of code.

pls detail **2.2. Migrate the Core Interaction Tools**

To execute the migration of the core interaction tools, we must physically lift the interaction mechanics out of the specific host applications and centralize them within our pure `Scenario Editor Logic Pack`. This guarantees maximum reusability across the IG, ExCon, and standalone Editor environments.

Here is the exact engineering breakdown for migrating these tools:

**1\. Lift and Shift the Tool Classes** We will extract the map canvas tools—specifically the `CreationTool`, `EditTool`, `RouteEditTool`, `MeasureTool`, and the overarching `StandardInteractionTool`—from the `Hrot.IG` and `Hrot.ExCon` subsystems and relocate them into the new shared module,. These tools encapsulate our core interaction mechanics, including entity placement, distance measurement, multi-point shape authoring, and drag-and-drop operations.

**2\. Enforce Strict CQRS for Tool Intents** As prepared in Phase 1, we must ensure these migrated tools remain completely decoupled from any concrete DDS message generation, such as `CreateEntityRequest` or `UpdateEntityDescriptorRequest`,. Within the new logic pack, these tools will communicate their actions exclusively by emitting pure FDP domain events to the internal `FdpEventBus`,.

-   The `CreationTool` will blindly emit a `SpawnEntityCommand`,.-   The `EditTool` and `RouteEditTool` will emit an `UpdateEntityCommand` when a multi-point shape or route is modified,.-   Interaction context menus and deletion hotkeys will emit a `DestroyEntityCommand`,.

**3\. Bind Exclusively to the Domain Abstractions** To maintain a pristine architecture, the migrated tools must not carry over any dependencies on infrastructure or networking. The internal wiring of these tools will operate purely on the core ECS abstractions: `ISimulationView`, the `EntityRepository`, and the `FdpEventBus`. By restricting the tools to querying local memory and publishing to the local bus, they become completely agnostic to whether the entities they are manipulating are locally simulated or network-driven proxies,.

pls detail **2.3. Extract the Visual Rendering Layers**

To properly extract the visual rendering layers, we must treat the rendering pipeline as a pure projection of our domain state. The view must be completely "dumb" and strictly decoupled from how the data got into the system.

Here is the exact engineering breakdown for extracting the rendering systems into our shared `Scenario Editor Logic Pack`:

**1\. Lift and Shift the Rendering Components** We will extract the visual projection systems out of the `Hrot.IG` namespace and relocate them into the shared `FDP.Toolkit.ScenarioEditor` logic pack. This extraction encompasses:

-   `MapOverlayRenderLayer` (for tactical polygonal areas).-   `RouteRenderLayer` (for multi-point path authoring).-   `MissionRenderLayer` (for active AI mission trajectories).-   `SelectionRenderSystem` (for the highlighted selection rings).-   `NedVisualizerAdapter` (which handles evaluating LOD and drawing the actual MIL-STD-2525 entity symbols).

**2\. Enforce Strict Domain Binding (The** **ISimulationView** **Contract)** Inside the new logic pack, these rendering layers must be refactored to depend _exclusively_ on the `ISimulationView` abstraction. They will construct highly optimized `EntityQuery` instances to scan local memory for pure ECS components. For example:

-   The `MapOverlayRenderLayer` will query for entities carrying `EditablePolyline` and `MapOverlayStyle` along with a `SimTransform`.-   The `RouteRenderLayer` will query for entities carrying the managed `RoutePlan` component.-   The `EntityRenderLayer` (driven by the `NedVisualizerAdapter`) will loop over entities with a `SimTransform`.

**3\. Leverage the Agnostic Render Loop Payoff** This is where our clean architecture truly shines. By restricting the renderers to purely looping over local ECS components like `SimTransform` and `SelectionState`, the rendering layer becomes 100% oblivious to data provenance. The renderer simply does not care if an entity is being simulated locally by an internal `SimHost Core Logic Pack` or if it is a remote "ghost" proxy being driven by UDP packets via the network Ingress Translators. The visualizer blindly renders the truth of the local repository.

**4\. Centralize Canvas Composition** Finally, we package the setup of these layers into a single bootstrapping routine within the `Scenario Editor Logic Pack`. The logic pack will instantiate the `MapCanvas` and inject the `MapOverlayRenderLayer`, `RouteRenderLayer`, `MissionRenderLayer`, and the `NedVisualizerAdapter`\-backed `EntityRenderLayer` into it.

When the standalone HROT Editor, ExCon, or IG applications spin up, their specific UI hosts simply grab this fully composed `MapCanvas` and render it into their respective ImGui windows. You achieve perfect code reuse for complex tactical rendering across all node types without duplicating a single Raylib draw call.

pls detail **2.4. Wire Up Local Scenario File Operations**

To wire up local scenario file operations, we must connect our UI control surfaces strictly to the local `EntityRepository` using the purified `ScenarioSerializerBuilder` and `ScenarioLoader`. This guarantees that our file I/O operations are firmly decoupled from any network infrastructure, operating entirely on the local memory domain.

Here is the precise architectural breakdown for wiring these operations:

**1\. Instantiate the Purified Serializer** First, we must configure the `ScenarioSerializerBuilder` to compile our serialization pipeline. This builder freezes into a highly optimized `ScenarioSerializer` that operates exclusively on the local `EntityRepository` to generate or consume pure JSON DOMs. Because it is purified, it relies solely on the local ECS components and registered domain translators, keeping the architecture pristine.

**2\. Implement "Load Empty" (New Scenario)** To support creating a new scenario from scratch, we must implement a routine that safely zeroes out the local simulation state. This operation must clear the `EntityRepository` of all existing entities and reset the `GlobalTime` singleton. This provides a clean slate in local memory without requiring any cluster-wide state teardown.

**3\. Implement "Save Scenario"** We must wire a "Save Scenario" UI action that captures the current world state directly from local memory. When triggered, the routine invokes `ScenarioSerializer.Serialize()` to capture the `EntityRepository` state, generating a JSON DOM. This pure JSON payload is then safely written to the local disk.

**4\. Implement "Load Scenario"** Finally, we implement the loading mechanism to read saved states from disk. The routine reads the JSON payload from the local file, clears the current repository, and invokes `ScenarioSerializer.Deserialize()`. This reconstitutes the entities directly into local memory, instantly reconstructing the entire scenario state without emitting a single DDS network packet.

pls detail **3.1. Enforce UI-Logic Separation (The "Dumb" View)**

To enforce strict UI-Logic separation, we must reduce our ImGui panels to entirely passive ("dumb") control surfaces, adhering to textbook Model-View-Controller (MVC) principles within our Data-Oriented ECS architecture.

Here is the exact execution plan for this sub-step:

**1\. Strip Interaction Logic from the Presentation Layer** All underlying map interaction mechanics and visual rendering logic must be aggressively extracted from the top-level ImGui panels. The UI panels must not contain any direct entity manipulation logic or networking concerns.

**2\. Convert Panels to Pure Control Surfaces** The ImGui panels must be refactored to act purely as lightweight control surfaces. Their sole responsibility is to capture operator input to push configurations or to activate the shared interaction tools (such as the entity placement tool, measurement tool, or multi-point authoring tool) that now reside within the decoupled Scenario Interaction Logic Pack.

**3\. Enforce Event-Driven Intent** The presentation layer must be completely decoupled from concrete infrastructure, such as DDS writers or data transfer objects. When a user interacts with the UI, the panels must blindly emit pure FDP domain events (such as `SpawnEntityCommand`, `UpdateEntityCommand`, or `DestroyEntityCommand`) to the local event bus.

By treating the UI strictly as a mechanism for expressing intent, we guarantee that our bespoke UI Packs remain entirely decoupled from both the network transport layer and the underlying map processing logic, allowing them to live strictly in their respective application hosts.

pls detail the **3.2. Assemble the ExCon UI Pack**

To assemble the ExCon UI Pack correctly, we must build a highly cohesive module strictly dedicated to remote command-and-control over DDS, completely stripped of any underlying simulation or map interaction mechanics. This is pure MVC: the panels act exclusively as the "View", delegating all state mutation and network egress to a clean Facade controller.

Here is the architectural blueprint for assembling the ExCon UI Pack:

**1\. Isolate the Command-and-Control Panels** We group the specific ImGui panels required for remote cluster orchestration into this dedicated pack. This module will primarily house the `OrbatPanel` for hierarchical unit management, the `MissionPanel` for behavioral tasking, and the `ConfigPanel` for remote map configuration. We will also include supporting diagnostic views like the `InteractionPanel` and `DiagnosticsPanel`.

**2\. Enforce the** **IExConLogic** **Facade Boundary** To keep these UI panels highly testable and entirely decoupled from the DDS transport layer or Raylib window context, they must depend exclusively on the `IExConLogic` facade interface. This interface acts as the strict boundary between the "dumb" presentation layer and the application shell. The panels will read read-only state from the injected `IDerRepo` (Dynamic Entity Repository) and push operator intents strictly through the `IExConLogic` methods.

**3\. Wire the Intent-Driven Mechanics** Each panel must be engineered to blindly express operator intent without directly mutating local ECS or repository state:

-   **The ConfigPanel:** Instead of manipulating the visual map directly, it maintains simple UI state fields. When the operator hits "Send", it serializes its settings into a JSON Merge Patch (RFC 7396) and pushes it through `IExConLogic.SendConfigPatch`.-   **The MissionPanel:** It reads the selected entity's mission plan and sends imperative command intents (like Jump or Abort) by invoking methods on the injected `IMissionEditorService`.-   **The OrbatPanel:** It builds its hierarchical order-of-battle tree purely by reading `CommanderId` relationships from the DER. When an operator clicks to spawn a new unit, the panel performs absolutely zero local repository mutations; it simply expresses the intent by delegating to `IExConLogic.StartPlacementMode`.

By strictly routing all ExCon UI interactions through the `IExConLogic` interface, we ensure the ExCon UI Pack remains a pristine, lightweight control surface. It can be unit-tested entirely in isolation from the CycloneDDS infrastructure, making the architecture incredibly robust and maintainable.

pls detail **3.3. Assemble the IG UI Pack**

To cleanly assemble the HROT IG UI Pack, we must adhere strictly to the Model-View-Controller (MVC) pattern, ensuring our presentation layer is completely decoupled from the underlying ECS and network transport. This pack will serve specifically the Image Generator node, providing diagnostic overlays and rudimentary test controls without bleeding into the standalone HROT Editor's UI.

Here is the architectural breakdown for assembling the IG UI Pack:

**1\. Isolate the IG-Specific View Components** We will group the ImGui panels that are exclusively relevant to the Image Generator's role into this specialized module. This pack will encompass the `IgDebugPanel` (for FPS and render overrides), the `PerformanceOverlay` (for ECS culling and rendering metrics), and the `MiniExConPanel` (for basic local entity spawning and testing).

**2\. Enforce Strict State-View Separation** To maintain a pristine architecture, these ImGui panels must not contain any business logic or direct ECS mutation code. Instead, they will bind to pure logic state objects, ensuring they remain passive control surfaces.

-   The `IgDebugPanel` will act as a pure view over `DebugPanelState`, allowing checkbox interactions to mutate the operator configuration in a fully testable, isolated manner without ImGui dependencies.-   The `PerformanceOverlay` will read read-only data from `PerformanceMetrics`, which captures a per-frame snapshot of ECS performance counters (like visible vs. culled entity counts) independent of the rendering pass.-   The `MiniExConPanel` will bind to `MiniExConPanelState`. When an operator clicks "Spawn", the panel performs no direct mutations or network calls; it simply delegates to the state object, which publishes a `SpawnEntityCommand` onto the local event bus.

**3\. Integrate with the Window Manager** We must decouple the panels from the application shell's rendering loop. The panels themselves will only contain the ImGui layout logic via `DrawContent`, while their lifecycle and docking behavior will be managed by the application Window Manager. We will wrap each panel in a `ManagedWindow` implementation (e.g., `IgDebugWindow`, `IgPerformanceWindow`, `IgMiniExConWindow`) and register them using the `IWindowRegistrar` interface.

By setting their `WindowScope` to `PerspectiveBound`, the Window Manager will automatically handle their visibility, ensuring these IG-specific panels only appear when the IG map perspective is active. This guarantees that if the IG logic is loaded alongside other subsystems, its diagnostic UI does not pollute the other perspectives.

pls detail **3.4. Scaffold the Standalone HROT Editor UI Pack**

To scaffold the Standalone HROT Editor UI Pack, we must create a dedicated presentation module that strictly adheres to Model-View-Controller (MVC) principles, ensuring it remains entirely decoupled from the underlying map interaction mechanics.

Here is the exact architectural breakdown for scaffolding this UI pack:

**1\. Isolate the Editor-Specific View** Because the HROT Editor is a standalone tool with its own 2D map visualization requirements, it does not need to share the ImGui panels from ExCon or IG. We will create a pristine new project (e.g., `Hrot.Editor.UI`) that depends only on the core domain interfaces, keeping it completely free from cluster-orchestration or IG-diagnostic concerns.

**2\. Construct Bespoke Authoring Panels** Within this new pack, we will develop the specific ImGui panels required for local scenario authoring. This includes building a unified Scenario Browser, advanced property inspectors, and timeline editors. Crucially, we must also implement the UI control surfaces for scenario file operations, providing dedicated interactions for scenario saving, loading, and creating a "new scenario".

**3\. Bind to the Shared Interaction Tools** To maximize code reuse without violating UI-Logic separation, these bespoke panels must act purely as lightweight control surfaces. They will not contain direct map manipulation logic; instead, they will configure and activate the shared map interaction tools (such as the entity placement tool, measurement tool, and multi-point authoring tool) that we isolated in the `Scenario Interaction Logic Pack`.

**4\. Enforce Event-Driven Intent** The Editor UI panels must remain completely ignorant of whether the simulation is running offline or over the network. When the operator uses the UI to request entity creation, deletion, or modification, the panels will blindly express this intent by emitting pure FDP events to the local event bus, strictly avoiding any concrete DDS messages. This guarantees that the UI pack acts strictly as a "dumb" view, allowing the underlying Composition Root to seamlessly toggle between the internal FDP SimHost and the external networked SimHost without altering a single line of presentation code.

pls detail **4.1. Instantiate the Purified Serializer**

To instantiate the purified serializer, we leverage the `ScenarioSerializerBuilder` to construct a highly optimized, reflection-free pipeline that operates entirely on our local memory domain.

Here is the exact architectural execution for this step:

**1\. Instantiate the Builder** We begin by instantiating the `ScenarioSerializerBuilder`, passing in the target subsystem identifier (e.g., `"Hrot.Editor"`). This human-readable string is stamped into the `Header.SubsystemType` of every saved scenario file, allowing loaders to safely reject files intended for entirely different node types.

**2\. Register Custom Translators** Before finalizing the build, we register any domain-specific N:M mappings via the `RegisterTranslator(IEntityScenarioTranslator)` method. These custom translators allow us to cleanly map complex ECS component relationships into bespoke JSON DOM entries. Once a translator runs, it clears its consumed component bits from the entity's serialization mask, perfectly isolating its work from the fallback auto-serializer.

**3\. Compile the Auto-Serializer (The Performance Payoff)** Finally, we invoke `Build()` on the builder, which freezes the translator list and returns our immutable `ScenarioSerializer`.

From an architectural perspective, the true brilliance happens under the hood during this `Build()` invocation. The builder triggers the `FdpAutoSerializer` to JIT-compile strongly-typed `System.Linq.Expressions.Expression` delegates for every registered, saveable value-type component in the system. By compiling these extract and inject delegates once at startup, we completely eradicate expensive `System.Reflection.PropertyInfo.GetValue` calls and boxing allocations on the hot, per-entity serialization path.

The resulting `ScenarioSerializer` is a pristine piece of domain logic. It is completely decoupled from any network transport or infrastructure concerns, interacting exclusively with the local `EntityRepository` to produce or consume pure `JsonObject` DOMs at maximum CPU speed.

pls detail **4.2. Implement "Load Empty" (New Scenario)**

To implement the "Load Empty" routine for new scenarios, we must provide a mechanism to safely zero out the local simulation state while maintaining our strict architectural boundaries. This operation gives us a pristine memory space without necessitating any complex cluster-wide state teardown.

First, we must purge the `EntityRepository`. Rather than manually querying and destroying entities one by one, we utilize the repository's internal `Clear()` routine to instantly decommit the `EntityIndex` chunks and reset the underlying component tables. This guarantees a completely clean slate at maximum memory-bus speed, ensuring no stale component references or ghost entities leak into the new authoring session.

Second, we must reset the simulation clock. We construct a fresh `GlobalTime` struct and inject it into the repository using `SetSingletonUnmanaged`. This zeroes out the accumulated delta times and frame counters, ensuring that physics integration, time-sliced systems, and trajectory evaluations start from a predictable `t=0` baseline.

By binding our "New Scenario" UI control surface directly to these localized memory operations, the presentation layer remains completely ignorant of network state or distributed orchestration. It simply flushes the local domain, leaving it ready for the operator to immediately begin placing new entities using the shared map interaction tools.

pls detail **4.3. Implement "Save Scenario"**

To implement the "Save Scenario" action, we must bind our UI control surface directly to the purified `ScenarioSerializer` to capture the current domain state without touching the network or cluster orchestration layers.

Here is the architectural execution for saving scenarios:

**1\. Triggering the Capture** We wire a "Save Scenario" button in the bespoke Editor UI Pack. When activated, this acts as a pure command that invokes `ScenarioSerializer.Serialize(EntityRepository repo, ScenarioHeader header)` against the local repository.

**2\. Pass 1: Entity Resolution (The GuidResolver)** Before serializing component data, the serializer enumerates all live entities, strictly filtering out any entities tagged with the `ScenarioIgnoreTag` or configured with `DataPolicy.NoSave`. For each valid entity, it generates a stable GUID and populates a save-side `IGuidResolver`. This guarantees that all volatile memory addresses and raw `Entity` indices are safely converted into persistent, cross-referenced identity strings.

**3\. Pass 2: The Serialization Pipeline** For each valid entity, the serializer constructs a pure JSON node using a strict, two-tiered pipeline:

-   **Mask Calculation:** It computes the intersection of the global saveable mask and the entity's current component mask.-   **Custom Translators (N:M Mapping):** It evaluates any registered `IEntityScenarioTranslator` instances. If a translator handles the entity, it extracts the custom DOM entries and crucially clears its consumed component bits from the remaining bitmask. This prevents duplicate serialization.-   **Auto-Serialization (1:1 Fallback):** The highly optimized `FdpAutoSerializer` sweeps up the remaining bits in the mask. Because it utilizes the JIT-compiled extraction delegates we built in step 4.1, it reads the unmanaged struct data instantly without any reflection (`PropertyInfo.GetValue`) overhead.

**4\. Assembling and Persisting the DOM** Finally, the serializer assembles these entity nodes under a root `JsonObject` alongside the `ScenarioHeader`. The header stamps the payload with the target subsystem type (e.g., `"Hrot.Editor"`) and the schema version. This pure JSON DOM is then written directly to the local disk.

By operating exclusively on local ECS memory and utilizing the bitmask to aggressively cull transient or network-only data, this routine flawlessly preserves our anti-corruption boundary, persisting the exact scenario state without allocating a single CycloneDDS packet.

pls detail **4.4. Implement "Load Scenario"**

To implement the "Load Scenario" routine, we must reconstruct the complex ECS graph from a serialized JSON DOM while strictly maintaining our anti-corruption boundaries. The loading process executes a highly optimized, two-pass reconstitution directly into local memory, entirely bypassing the network transport layer.

Here is the precise architectural breakdown for the loading sequence:

**1\. File I/O and Subsystem Validation** The UI control surface triggers a read of the JSON payload from the local disk. Before any entities are created, the `ScenarioSerializer` inspects the payload's `Header.SubsystemType`. If the file was authored for a different node role (e.g., trying to load an ExCon-specific overlay into a physics node), the serializer safely aborts, protecting the domain from incompatible schema data.

**2\. Local State Cleansing** To prevent data contamination, the routine must first clear the `EntityRepository`. This drops all existing entity chunks and provides a pristine memory space for the incoming scenario.

**3\. Pass 1: Identity Restoration (The GuidResolver)** The serializer begins its first pass over the `Entities` JSON block. It validates each persistent GUID string and invokes `repo.CreateEntity()` to allocate a fresh ECS memory slot. It then binds these together in a load-side `IGuidResolver`, forming a strict mapping between the persistent file GUIDs and the new, volatile memory addresses. This guarantees that all cross-entity relationships (such as commander-subordinate links) can be re-wired correctly in the next phase.

**4\. Pass 2: Component Injection Pipeline** In the second pass, the serializer reconstitutes the component data using a strict pipeline:

-   **Custom Translators (N:M Mapping):** The serializer hands the entity's JSON node to all registered `IEntityScenarioTranslator` instances. These translators parse complex or domain-specific DOM entries, inject the corresponding ECS components, and mark those JSON keys as "consumed".-   **Auto-Serialization (1:1 Fallback):** Any remaining unconsumed JSON keys are swept up by the `FdpAutoSerializer`. Because we compiled the injection delegates during Phase 4.1 using `System.Linq.Expressions`, the auto-serializer parses the JSON and writes directly into the unmanaged component structs without a single `System.Reflection.PropertyInfo.SetValue` call or boxing allocation.

By operating exclusively on the local `EntityRepository` via the `ScenarioSerializer`, the entire world state is reconstituted at maximum CPU speed without generating a single CycloneDDS network packet.

pls detail **5.1. Assemble the All-In-One Monolith (Offline State)**

To assemble the All-In-One monolith, we configure a single-process executable capable of running entirely offline. This is where we leverage the true power of our modular ECS design to achieve maximum performance and tight cohesion without sacrificing our strict decoupling.

First, we instantiate a single `ModuleHostKernel` paired with a shared `EntityRepository` and `FdpEventBus`. Into this unified composition root, we directly install the complete suite of pure domain modules: the _SimHost Core Logic Pack_ (our physics and kinematics muscle), the _CGF Logic Pack_ (our AI brain), the _Orchestration Logic Pack_, and our newly extracted _Scenario Editor Logic Pack_.

Because the Brain, Muscle, and UI layers now reside in the exact same memory space and share the same repository, we can treat the network entirely as a plugin that we leave disabled. We achieve this by deliberately **not** installing any Translator Packs.

The architectural win here is profound. Our UI tools—completely unaware of the deployment topology—blindly emit events like `SpawnEntityCommand` onto the internal bus. The local `NetworkSpawningSystem` natively consumes these commands and applies the TKB templates directly to the local world. States and actuator intents flow between the logic packs at maximum memory-bus speed without allocating a single byte for network DTOs. We completely eliminate JSON serialization, CycloneDDS overhead, and UDP packet routing, giving us a pristine, blazing-fast standalone authoring environment while keeping the domain logic structurally pure.

pls detail **5.2. Bind the Control Surfaces**

We snap the bespoke HROT Editor UI Pack into the presentation layer, treating the ImGui panels strictly as lightweight control surfaces. When an operator interacts with the map using a tool like the `CreationTool`, the presentation layer remains completely oblivious to the deployment topology. Instead of directly mutating the simulation state or invoking infrastructure, the tool blindly emits a pure domain event, such as a `SpawnEntityCommand`, onto the internal `FdpEventBus`. Because the Editor is running in the offline All-In-One configuration, no network Translator Packs are installed within the composition root. The local `NetworkSpawningSystem` immediately intercepts this command from the event bus, creates the entity in the shared `EntityRepository`, and applies the TKB template directly to the local world. This strict enforcement of CQRS and UI-Logic separation ensures that operator intents flow instantly across the internal ECS at maximum memory-bus speed, completely eradicating JSON serialization overhead and CycloneDDS network I/O from the authoring experience.

pls detail **5.3. Implement the Dynamic Reconfiguration (The Feature Switch)**

To implement the dynamic reconfiguration—the Feature Switch—we leverage the true power of our composition root and the `ModuleHostKernel`'s Read-Copy-Update (RCU) hot-plugging infrastructure. This switch elegantly degrades our offline All-In-One monolith into a distributed CGF/ExCon hybrid at runtime without halting the simulation.

Here is the precise architectural execution for building this feature switch:

**1\. Expose the Configuration Toggle** We introduce a configuration flag in the Editor UI that allows the operator to toggle the underlying simulation engine between the "Internal" FDP SimHost and an "External" networked SimHost. Because our UI tools were purified to blindly emit pure FDP events, they remain completely agnostic to this toggle's state. The switch is strictly a composition root reconfiguration.

**2\. Execute the Background Topology Recompilation** When the operator toggles the switch to "External", we initiate an asynchronous reconfiguration using the kernel's batch hot-plugging API (`InstallModulesAsync` and `UninstallModulesAsync`). The heavy lifting, such as memory provider allocation and dependency graph compilation, is performed entirely on a background thread to prevent any stalls on the 60Hz hot path.

**3\. Uninstall the Local Logic Packs** Within this background operation, we dynamically instruct the kernel to uninstall the `SimHost Core Logic Pack` and the `CGF Logic Pack`. The main thread's native harvest loop manages this safely, ensuring that all in-flight background tasks finish, leased views are returned, and resources are fully drained and disposed. This instantly strips the local node of its internal physics and AI muscle.

**4\. Install the Anti-Corruption Layer (Translator Packs)** Simultaneously, we instruct the kernel to install our network boundary modules: specifically, the Actuator Intents Pack for network egress and the Entity States Pack for network ingress.

**5\. Perform the Atomic Pointer Swap** Once the background thread finishes compiling the new `KernelExecutionTopology` containing the translator packs instead of the logic packs, the main thread performs a single O(1) atomic pointer swap during the next `SystemPhase.BeforeSync` boundary.

In a single frame, the local execution muscle is seamlessly replaced by the Anti-Corruption Layer. When the user interacts with the map, the `CreateEntityRequestEgressTranslator` instantly catches the local `SpawnEntityCommand` events on the bus, serializes them into concrete DDS requests, and pushes them out to the CycloneDDS socket. Conversely, the ingress translators catch remote broadcasts from the external SimHost and maintain proxy "ghost" entities in the local repository.

This is the pinnacle of clean architecture: seamlessly hot-swapping our entire execution tier for a network transport layer at runtime, without dropping a single frame or modifying a single line of presentation code.

pls detail **5.4. Eject the Local Processing Logic (External State)**

To execute the ejection of the local processing logic, we rely on the kernel's Read-Copy-Update (RCU) hot-plugging infrastructure to gracefully strip the application of its internal muscle and brain without stalling the main 60Hz simulation loop.

Here is the precise architectural execution for ejecting the local state:

**1\. Trigger the Dynamic Uninstallation** When the operator toggles the feature switch to the "External" configuration, the composition root initiates a background topology recompilation. We dynamically instruct the kernel to uninstall the `SimHost Core Logic Pack` (which handles locomotion, perception, and physics) and the `CGF Logic Pack` (which handles the BTree and HSM AI routines),,.

**2\. Drain and Dispose via the Harvest Loop** We do not just brutally sever memory. The kernel's main-thread harvest loop takes over the uninstalled modules, moving them to a draining state. It ensures that all in-flight asynchronous tasks finish, leased `ISimulationView` snapshots are safely returned to their providers, and unmanaged resources are fully drained and disposed without memory leaks. This guarantees a safe teardown of the local execution muscle.

**3\. Reduce the ECS to a Passive Data Store** Once the logic packs are completely uninstalled, the local node is instantly stripped of its internal execution authority,. The local `EntityRepository` is reduced to a purely passive data store, lacking the systems required to integrate physics or evaluate cognitive behavior trees.

**4\. Maintain the CQRS Illusion for the Presentation Layer** Because we previously established strict CQRS boundaries, the bespoke UI tools remain completely oblivious to this ejection. The operator can still use the `CreationTool` or `RouteEditTool`, and these presentation surfaces will continue blindly emitting pure FDP domain events (such as `SpawnEntityCommand`) onto the internal `FdpEventBus`,. However, because the local logic packs are gone, the local ECS will simply ignore them.

By cleanly severing the local processing logic, we have perfectly prepared the composition root for the final step: snapping in the Anti-Corruption Layer (the Translator Packs) to intercept those orphaned intents and seamlessly transform the application into a networked CGF/ExCon hybrid,.

pls detail **5.5. Snap-In the Anti-Corruption Layer (ACL)**

To complete the dynamic reconfiguration into a distributed hybrid, we snap our network boundary—the Anti-Corruption Layer (ACL)—directly into the `ModuleHostKernel` to replace the ejected local processing logic. This is the ultimate payoff of our strict CQRS and Model-View-Controller separation, as it transforms the application's entire execution tier at runtime without altering a single line of domain or UI code.

Here is the precise architectural execution for snapping in the Anti-Corruption Layer:

**1\. Install the Egress Translators (Actuator Intents Pack)** We dynamically install the `Actuator Intents Pack (Egress)` into the composition root. These translators act as our strict architectural boundary, operating exclusively on the internal `FdpEventBus` to catch outgoing commands. Because our UI tools remain completely oblivious to the network switch, a tool like the `CreationTool` will continue to blindly emit a pure `SpawnEntityCommand` onto the local event bus. The `CreateEntityRequestEgressTranslator` immediately intercepts this local intent, serializes it into a concrete NED `CreateEntityRequest` DDS message, and pushes it out to the CycloneDDS socket. The UI retains the illusion of local control while the ACL silently handles the distributed orchestration.

**2\. Install the Ingress Translators (Entity States Pack)** Simultaneously, we install the `Entity States Pack (Ingress)` to perceive the reality of the external cluster. When the remote authoritative SimHost processes our creation request, it broadcasts network state messages like `EntityMaster` and `WorldPos`. Our ingress translators catch these DDS wire formats and translate them directly back into our local domain language.

**3\. Materialize and Maintain "Ghost" Proxies** The `EntityMasterIngressTranslator` intercepts the remote announcement and allocates a proxy "ghost" entity in the Editor's local `EntityRepository`. As the remote entity moves, the `GeoSpatialIngressTranslator` catches the incoming `WorldPos` messages and applies the spatial updates directly to the ghost's local `SimTransform` component.

**4\. The Agnostic Render Loop Payoff** By translating external network states into pure local ECS components, the presentation layer requires zero branching logic. The local `EntityRenderLayer` simply loops over the `SimTransform` components in memory, perfectly oblivious to whether the entity is being driven by local physics integration or by remote UDP network packets. The Anti-Corruption Layer has completely decoupled our data provenance from our visualization and interaction mechanics.



===========================

We did extract the cognitive routines into the `CgfLogicPack`, and the `ClusterRunner` already contains a scaffolding for the `CgfSubsystem`. However, to properly enforce our clean architecture, we must explicitly formalise its standalone execution profile. In the distributed "HROT Demo" topology, the CGF node must act strictly as the "Brain", installing only the `CgfLogicPack` alongside the Entity States Ingress pack (to perceive the world) and the Actuator Intents Egress pack (to issue commands). This completely decouples cognitive processing from the "Muscle" (SimHost), meaning the CGF evaluates behavior trees and emits `NavigationIntent` commands over DDS without ever touching physics integration.

To validate this modularity and support the dual-nature of our deployment, extending the `ClusterRunner` and enforcing coverage via the `HeadlessTestExecutor` is the exact right engineering move. We must prove that our Read-Copy-Update (RCU) hot-plugging and Anti-Corruption Layers function flawlessly under load.

Here is the architectural plan for extending the runner and building the integration test suite:

**1. Extend ClusterRunner Configurations**
We will extend the `RunMode` flags to explicitly represent our two target architectures. 
*   **`RunMode.Editor`**: Boots the All-In-One monolith. It will load the `SimHostCoreLogicPack`, `CgfLogicPack`, `OrchestrationLogicPack`, and the `ScenarioEditor` into a single shared `ModuleHostKernel` without installing any DDS translator packs by default. 
*   **`RunMode.Demo`**: A macro that orchestrates the pure distributed setup. It spins up highly decoupled instances of `SimHostSubsystem`, `CgfSubsystem`, `IgSubsystem`, and `ExConSubsystem`, forcing all state and intent to flow exclusively through the network Anti-Corruption Layer.

**2. Implement Offline Editor Integration Tests**
Using the `HeadlessTestExecutor`, we will construct JSON-driven `TestScript` scenarios to prove the offline monolith operates securely.
*   **Test Case: Local Memory-Bus Intent Routing.** Boot the `RunMode.Editor` configuration headlessly. Inject a `SpawnEntityCommand` into the local `FdpEventBus` using a custom `ITestActionHandler`. Assert that the local `NetworkSpawningSystem` consumes the event and materialises the entity in the `EntityRepository` instantly, verifying that no DDS `CreateEntityRequest` packets were allocated.
*   **Test Case: Local Scenario File I/O.** Invoke the purified `ScenarioSerializer` directly against the local repository and assert that the full JSON DOM is persisted and reloaded cleanly without touching the network layer.

**3. Implement Feature Switch Integration Tests**
We must rigorously test the RCU topology swap to ensure memory safety and zero-downtime execution.
*   **Test Case: Dynamic Ejection and ACL Snap-In.** Boot the Editor headlessly. At $t=2.0s$, trigger the "External" Feature Switch. Assert via the `ModuleHostKernel` test hooks that the `SimHostCoreLogicPack` and `CgfLogicPack` are uninstalled and drained cleanly. Assert that the `ActuatorIntentsEgressPack` and `EntityStatesIngressPack` are successfully installed. 
*   **Test Case: Orphaned Intent Translation.** Immediately following the swap, inject an `UpdateEntityCommand` (simulating a map edit) onto the local bus. Assert that the newly installed egress translator catches it and writes an `UpdateEntityDescriptorRequest` to the mock DDS writer.

**4. Implement Distributed Brain/Muscle Integration Tests**
To prove the CGF and SimHost can operate independently, we will run both subsystems in parallel within the same test harness on isolated domain IDs.
*   **Test Case: Cognitive Dispatch to Kinematic Execution.** Boot `CgfSubsystem` and `SimHostSubsystem`. Assign a `MoveTo` mission to an entity via the CGF. Assert that the CGF evaluates its behavior tree and publishes a `NavigationIntent` over DDS. Assert that the SimHost receives this intent, the `CarKinematicsSystem` integrates the position, and the SimHost publishes updated `WorldPos` coordinates back to the cluster.