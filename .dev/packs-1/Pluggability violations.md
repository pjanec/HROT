Reviewing the codebase against the pristine "pluggable architecture" paradigm (where the core simulation is completely network-agnostic and relies entirely on translator packs to act as the Anti-Corruption Layer), I have identified several critical violations. 

Currently, infrastructure concerns like CycloneDDS and `System.Text.Json` are bleeding deeply into your core physics, combat, and command systems. To achieve a true "network as a plugin" architecture, we must ruthlessly decouple these weak points.

Here are the specific architectural flaws and the required fixes:

### 1. `MissionControlRequestSystem` is a God Class (Direct DDS & JSON Coupling)
**The Flaw:** Inside `Hrot.SimHost.Systems`, the `MissionControlRequestSystem` directly instantiates `DdsReader<MissionControlRequest>` and `DdsWriter<MissionControlAck>`. Furthermore, it parses raw JSON strings (`JsonDocument.Parse`) deep inside the domain loop. This completely violates the separation of "brain" and "translator packs".
**The Clean Fix:** 
*   **Extract an ACL Translator:** Create a `MissionControlIngressTranslator` in the network pack. It should poll the DDS topic, parse the JSON, and publish a strongly-typed, unmanaged `MissionControlIntent` to the local `FdpEventBus`.
*   **Purify the System:** `MissionControlRequestSystem` should only consume the local `MissionControlIntent` from the bus. It must have zero knowledge of DDS or `System.Text.Json`.

### 2. `UpdateEntityDescriptorRequestSystem` Directly Reads DDS
**The Flaw:** In `Hrot.Map.Common.Systems`, the `UpdateEntityDescriptorRequestSystem` explicitly declares a `DdsReader<UpdateEntityDescriptorRequest>` and an `IDdsWriter<UpdateEntityDescriptorAck>`. A core system that updates local ECS components should never poll a network socket directly.
**The Clean Fix:** Move the DDS polling into a dedicated `UpdateEntityDescriptorIngressTranslator`. The translator should decode the DDS union and drop a purely local `UpdateDescriptorIntent` onto the event bus for the system to process.

### 3. Physics & Combat Domains Leak Network IDs
**The Flaw:** To maintain a pure "muscle" layer that can be run offline or swapped, physics and combat must know nothing about the network. However:
*   `HitResolutionSystem` (in `FDP.Toolkit.Physics`) takes a `NetworkEntityMap` in its constructor so it can embed network IDs into the `DetonationNotification`. 
*   `AimAndFireExecutor` and `JoinFormationExecutor` (in the Brain/Behavior tier) are also injected with the `NetworkEntityMap`.
**The Clean Fix:** Core physics and executors must only deal with local `Entity` handles. `HitResolutionSystem` should emit a `DetonationNotification` containing only local `Entity` fields. The *Egress Translator* (e.g., `MunitionDetonationEgressTranslator`) should be the entity that intercepts this local event, looks up the network IDs via the `NetworkEntityMap`, and publishes the network packet.

### 4. `SimHostModule` is a Tangled Monolith
**The Flaw:** The `SimHostModule` currently registers core ECS logic (like `NetworkSpawningSystem`) right alongside network infrastructure like `GeoSpatialEgressTranslator` and `EntityMissionIngressTranslator`. If you want to run the SimHost core pack locally without network connectivity, you cannot safely instantiate this module because it hard-binds to CycloneDDS concepts.
**The Clean Fix:** Split this module strictly down the boundary line:
*   **`SimHostCoreModule`**: Registers `NetworkSpawningSystem`, `CreateEntityRequestSystem` (purified to use local intents), and `DeleteEntityRequestSystem`.
*   **`SimHostNetworkTranslatorPack`**: Registers all the Egress and Ingress translators (`GeoSpatialEgressTranslator`, `MapVisualOverlayEgressTranslator`, etc.). The application composition root (`NodeBootstrapper`) then decides whether to load the translator pack based on the deployment mode.

### 5. `ReferenceEditLoadHandler` & Scenario Handlers Parsing JSON
**The Flaw:** Despite our earlier designs to eliminate JSON parsing from the Orchestrator's domain, handlers like `ReferenceEditLoadHandler`, `ReferenceEpisodeLoadHandler`, and `ReferenceScenarioLoadHandler` are still manually executing `JsonDocument.Parse(cmd.PayloadJson)` to extract fields like `ScenarioId` and `TargetState`.
**The Clean Fix:** Apply the "Type-Erased Domain Payload" pattern. The `NodeOpSlaveTranslator` should intercept the JSON at the network edge, deserialize it into strongly-typed payload objects (like `ScenarioLoadPayload`), and place it into the `ExecuteNodeOpIntent.DomainPayload`. The handlers then simply cast the object: `if (cmd.DomainPayload is ScenarioLoadPayload payload) { ... }`.

### Summary
By pushing **all** `DdsReader/DdsWriter` instances and **all** `System.Text.Json` usage strictly into the Translator classes, your core `SimHost` and `CGF` logic packs will achieve perfect isolation. They will consume purely from the `FdpEventBus` using unmanaged structs, giving you the flawless plug-and-play architecture you designed.


Regarding UpdateEntityDescriptorRequestSystem. It takes union of dds descriptors  and convert each to ecs components. This avoids unnecessary intermediate data formats. It can and should be considered a translatoe.


You are absolutely right that its core responsibility is translation at the system boundary, and unpacking the `EntityDescriptorUnion` directly into ECS components like `SimTransform` or `EditablePolyline` is a massive win for Data-Oriented Design. By avoiding intermediate mapping DTOs, we eliminate unnecessary heap allocations and keep the translation path highly performant.

However, from a strict CQRS and Clean Architecture perspective, we draw a deliberate boundary between **State Replication Translators** and **Command Handlers**. 

Standard translators implementing the `IDescriptorTranslator` interface (like `GeoSpatialIngressTranslator`) reside on the **Data Plane**. Their sole responsibility is continuous, fire-and-forget state synchronization across the cluster. 

`UpdateEntityDescriptorRequestSystem`, conversely, operates on the **Control Plane**. It does not just replicate state; it processes an imperative, transactional request. Crucially, after verifying ECS authority and translating the DDS union into local ECS components, it is strictly required to write an `UpdateEntityDescriptorAck` back to the DDS wire so the originating sender can correlate the outcome. 

Because it must manage this distributed request-response handshake, it is structured as an `IEcsModuleSystem` (a `ComponentSystem`) rather than a passive `IDescriptorTranslator`. 

So, your architectural instinct is spot on: it *is* a translator. Specifically, it acts as a **Command Ingress Translator**. It serves simultaneously as the Anti-Corruption Layer (ACL) mapping DDS unions to ECS structs, and as the command handler closing the transaction loop, perfectly blending DOD performance with CQRS boundary enforcement.
