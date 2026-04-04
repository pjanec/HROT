### The Clean Architecture Boundary (The ACL)
This diagram illustrates the fundamental separation of concerns. Pure Logic Packs operate exclusively on local ECS memory and the internal FdpEventBus. They have zero knowledge of CycloneDDS or JSON. Translator Packs act as the strict boundary, converting DDS wire formats into local domains.

``` mermaid
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
    
    classDef domain fill:#ae9620,stroke:#4caf50,stroke-width:2px;
    classDef acl fill:#aa8311,stroke:#ff9800,stroke-width:2px;
    classDef edge fill:#a12a46,stroke:#2196f3,stroke-width:2px;
    
    class LogicPacks,Bus domain;
    class ACL acl;
    class Edge edge;
```

###  "HROT Demo" Distributed Node Assembly
In a distributed setup, we assemble highly specialized nodes by mixing specific Logic Packs with unidirectional Translator Packs. Notice how the Brain and Muscle nodes never share Logic Packs, enforcing strict cognitive vs. kinematic isolation.

``` mermaid 
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

### "HROT Editor" All-In-One Composition & Feature Switch
When running the standalone Editor, all Logic Packs are loaded into a single ModuleHostKernel sharing one ECS repository. Because they share memory, Translator Packs are completely bypassed—Intents and States flow instantly across the internal bus.

The "Feature Switch" elegantly degrades this monolith into a distributed node by swapping out the local Muscle logic for remote network translators.

``` mermaid
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

    DDS((External SimHost over DDS))
    
    Remote -.->|If switched to External| DDS
    
    classDef switch fill:#333,stroke:#fff,stroke-width:2px,stroke-dasharray: 5 5;
    class FeatureSwitch switch;
```

The Scenario Editor Pack can effortlessly target local memory or remote network endpoints without altering a single line of business logic.



Here are the sequence diagrams illustrating the clean architecture boundaries and data flow for both states of the Feature Switch.

### State A: Internal SimHost (Offline / All-In-One)
In this state, the Translator Packs are completely bypassed. The Editor UI shares the same memory space as the `SimHost Core Logic Pack` and the `CGF Logic Pack`. Everything flows instantly through the `FdpEventBus` and local ECS repository. 

```mermaid
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

***

### State B: External SimHost (Networked)
When the user toggles the switch, the local Logic Packs (`SimHost Core`, `CGF`) are dynamically uninstalled and the **Translator Packs** are installed in their place. The UI tools still blindly emit local FDP events, but the Anti-Corruption Layer (ACL) intercepts them and routes them over CycloneDDS to a remote authority.

```mermaid
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

The `CreationTool` has no idea it is talking to a network. The egress translator converts the internal `SpawnEntityCommand` into a `CreateEntityRequest` DDS message. When the remote SimHost replies by broadcasting an `EntityMaster` DDS message, the local `EntityMasterIngressTranslator` creates a proxy "ghost" entity in the Editor's local ECS. Position updates arrive as `WorldPos` messages, which the `GeoSpatialIngressTranslator` applies back to the ghost's `SimTransform`. The rendering layer simply loops over `SimTransform` components, completely oblivious to whether the entity is locally simulated or a network ghost.