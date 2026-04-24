
I want to add scenario loading concept to from networkless hrot.editor to distributed configuration. The scenario saved from hrot.editor should load to the cgf node. Entities need to be instantiated on cgf using the initial values of components as saved in the scenario. But the system needs to resolve the component ownership as needed for the distributed setup. Probably to use the saved components as initial ones. The cgf might need to send some internal spawn request with initial component valued to simhost etc. so the entities are created there while maintaining proper component ownership. Pls suggest how to approach this. Maybe the same scenario can ge loaded in parallel by simhost, instantiating the components to be owned by simhost (bug this info is not saved in the scenario)

Loading the scenario in parallel across multiple nodes is an architecturally flawed approach. It violates the Single Source of Truth for entity genesis. If both CGF and SimHost deserialize the scenario independently, they will attempt to materialize the entities in isolation. This bypasses the `DdsIdAllocator` network ID generation, breaks the `EntityLifecycleModule` (ELM) reliable initialization handshake, and would require brittle, post-load hacking of ECS authority masks.

Your first suggestion—having the CGF node load the scenario and explicitly dispatch spawn requests—is the correct architectonical path. It leverages the existing network spawning and split-authority pipelines, ensuring that all distributed guarantees (ownership routing, ghost promotion, and lifecycle ACKs) are strictly maintained.

Here is the clean architecture approach to implement this:

**1\. Staging Deserialization** During the cluster's orchestration phase (e.g., inside an `IClusterStateHandler` intercepting `LoadingLive`), the CGF node should intercept the `Hrot.Scenario` JSON file. Instead of deserializing it directly into the live `EntityRepository`, deserialize the DOM into a temporary, isolated `EntityRepository`.

**2\. Component Extraction** Iterate through the populated staging repository. For each valid entity, extract its `TkbIdentity.TkbType`. Read all other relevant saveable components (e.g., `SimTransform`, `EntityInfo`, `EditablePolyline`, `RoutePlan`) and pack them into a `List<object>`. This collection forms the `InitialComponents` payload.

**3\. Pre-Genesis Routing (Split Authority)** For each entity, determine the distributed ownership layout. Invoke the `BrainMuscleOwnershipStrategy` to identify the least-loaded Muscle node (SimHost) that should take over kinematic descriptors like `dtWorldPos` and `dtNavigationStatus`.

Publish a `DeferredTakeOwnershipCommand` onto the CGF's local event bus. This broadcasts a pre-genesis routing table to the cluster, guaranteeing that the target SimHost will claim the required ECS authority bits immediately after the entity transitions from Ghost to Constructing, effectively solving the unowned creation race condition.

**4\. Authoritative Spawning** Allocate a globally unique `NetworkId` via the CGF's `INetworkIdAllocator`. Publish a `SpawnEntityCommand` to the CGF's local event bus with the following configuration:

-   `NetworkId`: The newly allocated ID.-   `TkbType`: The extracted template type.-   `OwnerNodeId`: The CGF's local node ID (establishing the Brain as the cognitive owner).-   `InitType`: `ReliableInitType.AllPeers` (enforcing the ELM distributed handshake).-   `InitialComponents`: The list of extracted components from Step 2.

**Why this design is robust:** By publishing the `SpawnEntityCommand` locally, the CGF's `NetworkSpawningSystem` will materialize the authoritative entity. Simultaneously, the `SpawnEntityCommandEgressTranslator` will intercept the bus event and seamlessly convert it into a DDS `CreateEntityRequest` containing the initial components. The SimHost receives this request, instantiates the ghost entity, applies the initial state, and its `DeferredTakeoverSystem` automatically assumes `SimTransform` authority based on the preceding routing table.

This guarantees deterministic distributed instantiation using the existing pipeline without introducing special-case loading mechanics.

How is the createentityrequest handled by cgf? I think the same handling mechanism must be used for each entity. The CreateEntityRequest dds message should not be generated.

Your observation is correct. Generating a loopback DDS `CreateEntityRequest` from the CGF just to have the CGF read it back is an anti-pattern. The CGF should directly leverage its existing request-processing pipeline for scenario loading to ensure the ownership distribution and entity genesis logic remains centralized and consistent.

Here is how the CGF currently handles entity creation and how you should architect the scenario injection to reuse this exact mechanism:

The CGF Entity Creation Pipeline

On the CGF node, entity creation is orchestrated by the `CreateEntityRequestSystem`. Because the CGF operates as the cluster's default processor, it intercepts requests that lack a specific target owner.

When it processes an `EntityCreationRequest` DTO, the system performs the following sequence:

-   **Validation & ID Allocation:** It verifies the TKB template exists and generates a globally unique network ID via the injected `INetworkIdAllocator`.-   **Split Authority Routing:** It invokes the `IOwnershipDistributionStrategy` to determine which Muscle node should take over specific descriptors (like `dtWorldPos` and `dtNavigationStatus`).-   **Pre-Genesis Broadcast:** It publishes a `DeferredTakeOwnershipCommand` to the local event bus, which the egress translator broadcasts to the cluster so Muscle nodes know their assignments before the entity technically exists.-   **Local Spawning:** It publishes a `SpawnEntityCommand` containing the `InitialComponents` to the local event bus, which the `NetworkSpawningSystem` consumes to materialize the ECS entity and drive the reliable initialization handshake.

The Clean Architecture Solution

To reuse this exact mechanism without touching the DDS layer, you must decouple the _source_ of the requests from the _processor_.

The `CreateEntityRequestSystem` depends on the `IEntityCreationRequestSource` abstraction, which exposes a `ProcessRequests(Action<EntityCreationRequest> handler)` method. Currently, the live node injects `NedEntityCreationRequestSource` (which reads from DDS).

Instead of publishing a `SpawnEntityCommand` manually (which skips ID allocation and ownership routing), your scenario loading module should do the following:

-   **Implement an In-Memory Request Source:** Create a composite or secondary `IEntityCreationRequestSource` backed by an in-memory queue. This is identical to the `StubRequestSource` pattern already utilized in your integration tests for deterministic, DDS-free request injection.-   **Translate DOM to Neutral Requests:** As the scenario deserializer iterates through the saved DOM, extract the initial component values into a `List<object>`.-   **Enqueue the Requests:** Pack these components into protocol-neutral `EntityCreationRequest` DTOs with `OwnerAppInstanceId = 0` (targeting the default processor) and push them into the in-memory request source.

By multiplexing the network-backed request source with your new scenario-backed request source, the `CreateEntityRequestSystem` will pull the scenario entities and process them identically to network spawns. This guarantees that ID allocation, `DeferredTakeOwnership` routing, and the `SpawnEntityCommand` instantiation flow are uniformly applied without generating redundant network traffic.

How to convert the entity dom to the list of component objects? I guess we do not need a staging ecs repo, or do we?

Your assumption that a staging ECS repository can be bypassed is incorrect. You must use a staging `EntityRepository` to process the DOM.

Attempting to deserialize the JSON DOM directly into a `List<object>` would require writing a parallel, reflection-heavy deserialization pipeline, violating DRY and risking schema drift. The existing `FdpAutoSerializer` is highly optimized for the ECS: it compiles IL expression trees at runtime that bind JSON nodes directly to `EntityRepository.SetComponent<T>` to avoid boxing and reflection on the hot path. Custom `IEntityScenarioTranslator` implementations also strictly require an `EntityRepository` to resolve entity references and inject components.

The architecturally sound approach is to leverage the existing pipeline to hydrate a transient world, then extract the resulting components.

Here is the clean implementation path:

**1\. Hydrate the Staging Repository** Instantiate a transient `EntityRepository` and use the existing `ScenarioSerializer` to deserialize the DOM into it.

**2\. Extract Boxed Components** Iterate through the active entities in the staging repository. FDP's `IComponentTable` interface exposes a `GetRawObject(int index)` method specifically designed for type-erased component extraction. You can use the entity's `ComponentMask` to determine which tables to query.

```
var requests = new List<EntityCreationRequest>();
var tables = stagingRepo.GetRegisteredComponentTypes(); // Retrieves all IComponentTable instances

foreach (var entity in stagingRepo.Query().IncludeAll().Build())
{
    ref var header = ref stagingRepo.GetHeader(entity.Index);
    var initialComponents = new List<object>();

    foreach (var kvp in tables)
    {
        var table = kvp.Value;
        if (header.ComponentMask.IsSet(table.ComponentTypeId))
        {
            // Extract the boxed component value via the polymorphic table interface
            object componentValue = table.GetRawObject(entity.Index);
            initialComponents.Add(componentValue);
        }
    }

    // Extract TkbType (assuming TkbIdentity is present)
    long tkbType = stagingRepo.HasComponent<TkbIdentity>(entity) 
        ? stagingRepo.GetComponentRO<TkbIdentity>(entity).TkbType 
        : 0;

    requests.Add(new EntityCreationRequest
    {
        RequestId = Guid.NewGuid(),
        OwnerAppInstanceId = 0, // Route to default processor (CGF)
        TkbType = tkbType,
        DisType = header.DisType.Value,
        InitialComponents = initialComponents
    });
}
```

**3\. Inject and Dispose** Pass the constructed `EntityCreationRequest` objects into your in-memory request source, where the `CreateEntityRequestSystem` will process the `InitialComponents` collection natively. Finally, invoke `Dispose()` on the staging `EntityRepository` to reclaim its unmanaged memory allocations.

This guarantees that all complex scenario state—including inline arrays, unmanaged cross-entity references mapped via `IGuidResolver`, and nested DOM elements—is perfectly instantiated according to the canonical schema before being routed into the distributed network pipeline.

Ok. Staging repo and sharing the entity creation processor sound good and simple, reusing proven mechanisms. Is there any catch?

While the staging repository approach successfully reuses the network spawning pipeline, there are three critical architectural catches that must be mitigated to prevent data corruption and state invalidation.

**1\. Cross-Entity Reference Invalidation (The Mapping Problem)** When the `ScenarioSerializer` hydrates the staging `EntityRepository`, it uses an `IGuidResolver` to restore cross-entity references (e.g., `TargetMemory.EntityIds`, `PassengerBuffer.Passengers`, or `HierarchyNode`) to the temporary local indices of the staging world.

However, when you extract these components and route them through `IEntityCreationRequestSource`, the `CreateEntityRequestSystem` generates entirely new global network IDs and local entity indices for the live world. If you pass the extracted components as-is, they will contain stale `Entity` handles pointing to the disposed staging repository. You must maintain a translation map linking the staging entity handles to the newly allocated network IDs, and perform a second pass to patch these references inside the `InitialComponents` payload before dispatching the spawn command.

**2\. TKB Child Blueprint Duplication** The scenario DOM represents a flattened view of the world, meaning both parent entities and their structural child entities (e.g., tank hulls and their turrets) are saved as distinct DOM nodes.

The catch is that `CreateEntityRequestSystem` contains explicit logic to read `parentTemplate.ChildBlueprints` and automatically publish secondary `SpawnEntityCommand` events for them. If you blindly extract and inject every entity from the staging repository, you will double-spawn all sub-parts. You must filter the staging repository to only extract root entities (e.g., checking `EntityInfo.CommanderId == 0` or evaluating hierarchy components) and rely on the TKB template to spawn the children, or explicitly suppress child auto-spawning for scenario-sourced requests.

**3\. Network and Lifecycle State Contamination** The saved scenario captures entities in their terminal `Active` state, along with their previous network identities and authority masks. The `NetworkSpawningSystem` enforces a strict distributed lifecycle: it forces new entities into the `Constructing` state, generates a fresh `NetworkIdentity`, calculates `NetworkOwnership`, and expects peer ACKs.

If you pack every extracted component into the `InitialComponents` collection without filtering, you will inject stale `NetworkIdentity`, `NetworkAuthority`, and `EntityLifecycle` data. You must explicitly exclude these transient/network control components during the extraction phase so that the `NetworkSpawningSystem` can cleanly bootstrap the entity's distributed state.

Doesn't the episode loader need to do something similar? I.e. to inject same saved entities from a micro-scenario into already running exercise? It also needs to remap entity ids...

You are absolutely correct. The current implementation of `ReferenceEpisodeLoadHandler` suffers from the exact architectural flaws we just avoided for the scenario loader.

If you look at `CommitStartEpisode`, the handler directly invokes `_serializer.Deserialize(targetRepo, _pendingJson, asEpisode: true, episodeId: _pendingEpisodeId)` straight into the live simulation world. This approach violates clean distributed architecture in several critical ways:

-   **Network ID Collisions:** `ScenarioSerializer` blindly restores the components exactly as they were saved. If the episode's JSON contains `NetworkIdentity` components, they will be injected with their historical integer values. In a live, running exercise, injecting hardcoded IDs will inevitably cause catastrophic collisions with existing entities in the `NetworkEntityMap`.-   **Genesis Pipeline Bypass:** By dumping components directly into the live `EntityRepository`, the loader completely bypasses the `NetworkSpawningSystem`. The episode entities will skip the `Constructing` state, skip the `DeferredTakeOwnership` split-authority routing, and skip the ELM reliable initialization handshake.-   **Stale Network State:** The episode entities will be injected carrying whatever `NetworkAuthority` or `EntityLifecycle` state they happened to have when the episode was authored, rather than being correctly bootstrapped for the current cluster topology.

Regarding entity ID remapping: `ScenarioSerializer.Deserialize` does currently use a `LoadResolver` (implementing `IGuidResolver`) to remap volatile ECS `Entity` handles (like those in `TargetMemory` or `PassengerBuffer`). However, this only remaps the local ECS indices _within the isolated episode snapshot_. It does nothing to resolve the global network ID routing or ownership.

The Architectonical Fix

To maintain a robust system, the `ReferenceEpisodeLoadHandler` must be refactored to share the exact same staging pipeline you are building for scenario loading. The unified flow should be:

-   **Staging:** `ReferenceEpisodeLoadHandler.CommitStartEpisode` should deserialize the episode JSON into a transient, staging `EntityRepository`.-   **Extraction & Sanitization:** Iterate the staging repository, extract the components, and strip out transient network state (`NetworkIdentity`, `NetworkAuthority`, `GhostStateTracker`, etc.).-   **Tagging:** Append the `EpisodeTag` struct (containing the `EpisodeId`) to the extracted `InitialComponents` list. This ensures that when the cluster orchestrator later broadcasts a `StopEpisode` command, `ReferenceEpisodeLoadHandler` can still cleanly query and destroy the episode's entities.-   **Dispatch:** Push the extracted components through the `IEntityCreationRequestSource` pipeline you establish for the scenario loader.

By doing this, episodes become indistinguishable from normal network spawns. The CGF node will allocate fresh, collision-free Network IDs, broadcast the pre-genesis routing tables, and spawn the episode entities with guaranteed distributed determinism.

How to elegantly filter out undesired components from initialization list? Ie the elm ones, networkidentity and similar. There should be some global registry of those types.

Your suggestion to introduce a global registry for filtering "undesired" components is architecturally flawed.

Creating a global "DoNotLoad" registry or modifying the global `[DataPolicy]` attributes (like `DataPolicy.NoSave`) conflates domain definitions with context-specific pipeline requirements. Components such as `NetworkIdentity`, `NetworkAuthority`, and `LifecycleDescriptor` are strictly required to be persisted and restored during a Checkpoint or SaveGame load. If you exclude them at a global registry level, you will permanently break the engine's ability to serialize and restore live, distributed simulation states.

The divergence in persistence semantics—where a Checkpoint requires network IDs but a Scenario injection must strip them—dictates that the filtering logic must be owned exclusively by the Scenario Loader context, not a global registry.

The clean and performant approach is to leverage the existing `BitMask256` struct to define a static exclusion mask within your scenario loading module. Because FDP enforces globally stable IDs via `[ComponentId]`, you can construct this mask predictably and evaluate it in O(1) time during the extraction pass.

Here is the correct implementation:

1\. Define the Context-Specific Exclusion Mask

Define a static `BitMask256` in your scenario loader that explicitly flags the distributed lifecycle and network components that the `NetworkSpawningSystem` needs to synthesize from scratch.

```
private static readonly BitMask256 _scenarioExclusionMask = BuildExclusionMask();

private static BitMask256 BuildExclusionMask()
{
    var mask = new BitMask256();
    
    // Core distributed state that must be freshly bootstrapped
    mask.SetBit(GlobalComponentIds.NetworkIdentity);      // 50
    mask.SetBit(GlobalComponentIds.NetworkAuthority);     // 51
    mask.SetBit(GlobalComponentIds.NetworkOwnership);     // 140
    
    // Lifecycle state
    mask.SetBit(GlobalComponentIds.LifecycleDescriptor);  // 5
    mask.SetBit(GlobalComponentIds.GhostStateTracker);    // 66
    mask.SetBit(GlobalComponentIds.PendingNetworkAck);    // 141
    
    // Add any other transient scenario-specific tags (e.g. EpisodeTag)
    
    return mask;
}
```

2\. Apply the Filter During Extraction

Update the component extraction loop in your staging repository logic. By checking `!_scenarioExclusionMask.IsSet(table.ComponentTypeId)`, you elegantly filter out the stale state before it reaches the `InitialComponents` list.

```
var requests = new List<EntityCreationRequest>();
var tables = stagingRepo.GetRegisteredComponentTypes(); 

foreach (var entity in stagingRepo.Query().IncludeAll().Build())
{
    ref var header = ref stagingRepo.GetHeader(entity.Index);
    var initialComponents = new List<object>();

    foreach (var kvp in tables)
    {
        var table = kvp.Value;
        int typeId = table.ComponentTypeId;

        // Extract ONLY if the entity has the component AND it is not in the exclusion mask
        if (header.ComponentMask.IsSet(typeId) && !_scenarioExclusionMask.IsSet(typeId))
        {
            object componentValue = table.GetRawObject(entity.Index);
            initialComponents.Add(componentValue);
        }
    }

    // Extract TkbType safely
    long tkbType = stagingRepo.HasComponent<TkbIdentity>(entity) 
        ? stagingRepo.GetComponentRO<TkbIdentity>(entity).TkbType 
        : 0;

    requests.Add(new EntityCreationRequest
    {
        RequestId = Guid.NewGuid(),
        OwnerAppInstanceId = 0, 
        TkbType = tkbType,
        DisType = header.DisType.Value,
        InitialComponents = initialComponents
    });
}
```

This approach strictly adheres to the Single Responsibility Principle. The global schema remains agnostic to the scenario injection mechanics, and the network pipeline safely receives clean, domain-only payload structures ready for distributed genesis.

Wait shouldnt we use some attribute instead of enumerating them? Something like NoScenarioSave?

Introducing a `[NoScenarioSave]` attribute is an architectural anti-pattern. You are conflating the static domain definition of a data structure with the context-specific rules of a single execution pipeline.

If you place a scenario-specific attribute or leverage the existing `[DataPolicy(DataPolicy.NoSave)]` directly on core network components like `NetworkIdentity` or `NetworkAuthority`, you pollute the lowest-level engine domain (`Fdp.Core` and `Fdp.Toolkit.Replication`) with concepts from a higher-level tooling module (`Fdp.Toolkit.Scenario`). Core network descriptors must remain strictly ignorant of how scenarios or episodes are authored.

Furthermore, attributes are statically compiled metadata. Whether a component is "undesired" depends entirely on the operational context. When the cluster orchestrator executes a `TakeCheckpoint` operation, `NetworkIdentity` and `NetworkAuthority` are strictly mandatory to perfectly restore the distributed session state. If you mark them with an attribute to filter them out for scenario injection, you will permanently break checkpoint creation and save-game restoration because `EntityRepository.GetSaveableMask()` will globally exclude them.

Clean architecture dictates that a component struct remains a pure data container. The rules for filtering that data belong exclusively in the boundary layer that requires the filtering. Defining a context-specific `BitMask256` inside your scenario or episode loader tightly encapsulates this responsibility. It keeps the core ECS schema clean, satisfies the exact injection requirements of the live running exercise, and executes the filter via highly optimized O(1) bitwise operations during the extraction pass.

Ok. What about restoring the checkpoint in a distributed context? Is it same as scenario loading? I see a big issue wirh doctrine json parameters which can contain network id of other entities. This is not easy to automatically remap (but not impissible, we know parameter dtos for each doctrine type)

Restoring a checkpoint in a distributed context is fundamentally different from scenario loading. Your concern about remapping network IDs inside doctrine JSON parameters stems from conflating these two distinct architectural patterns.

Checkpoints are global state rollbacks. Unlike scenario loading, which acts as an injection mechanism that provisions new entities through the network spawning pipeline and allocates fresh IDs, a checkpoint restores the exact binary state of the ECS world. This includes the original `NetworkIdentity`, `NetworkAuthority`, and `EntityLifecycle` components.

Because the historical network IDs are preserved exactly as they were at the moment the snapshot was taken, there is absolutely no need to remap cross-entity references. Any `targetNetworkId` or `routeEntityId` embedded in your doctrine JSON parameters remains perfectly valid upon restoration. Attempting to build a DTO-based remapping layer for checkpoint restoration would add unnecessary complexity and violate the pure-state-restoration semantics of the snapshot pipeline.

To prevent ID collisions after a restore (for example, when branching to live execution from a restored state), the architecture manages the ID space at the cluster level. The checkpoint/recording metadata tracks the `MaxNetworkId` observed during the session. When the cluster orchestrator restores the state, it reads this value and resets the distributed ID allocator safely above the restored ID space. This guarantees that any new entities spawned after the checkpoint is loaded will receive completely fresh IDs, eliminating collisions without requiring brittle JSON mutations.

Furthermore, if you are embedding hardcoded network IDs in authored scenario files, you are introducing an anti-pattern. Scenarios represent unbound templates, not historical state. The engine solves cross-entity references in scenarios using the `IGuidResolver` pattern. Custom DOM translators (such as the `TargetMemoryTranslator` and `PassengerBufferTranslator`) explicitly convert volatile entity handles into stable GUID strings during serialization, and resolve them back to newly allocated entities during injection. This ensures the scenario DOM remains completely decoupled from the live network ID space.

Simple loading to ecs repo would need to be done by all nodes from their partial checkpoint snapshots in synchronized manner. And the network id stored as json parameter is problematic also for usual scenario which can and should contain predefined missions with doctrine tasks. The need for remapping the saved netwirk ids is objectively present. Also thera are various in-memory id translation maps used by translators. These need to be reconstructed.

You are objectively correct. My previous assessment overlooked the opacity of JSON-encoded network IDs embedded within doctrine task parameters (such as `targetNetworkId` in `FireAtTarget` and `routeEntityId` in `FollowRoute`). When treating scenarios as unbound templates rather than strict historical state, these cross-entity network references will indeed become stale and must be explicitly remapped. Furthermore, the reconstruction of in-memory translation maps is a mandatory requirement for cluster consistency.

Here is the architectonical approach to solve both the JSON remapping and the in-memory map reconstruction while maintaining clean pipeline boundaries.

1\. Two-Pass Scenario Extraction and ID Remapping

Because doctrine parameters are serialized as opaque JSON strings inside `ActiveMissionPlan` or `MissionPlanQueue`, standard ECS reflection cannot automatically patch them. You must perform a two-pass extraction from your staging repository:

-   **Pass 1 (Allocation & Mapping):** Iterate all entities in the staging repository that possess a `NetworkIdentity`. Allocate a new, globally unique network ID for each using the `INetworkIdAllocator`. Build a context-specific translation map: `Dictionary<long, long> oldToNewNetworkIds`.-   **Pass 2 (Extraction & JSON Patching):** Extract the components as previously discussed, filtering out transient network state. When extracting mission-related components (like `ActiveMissionPlan`), intercept the payload. Parse the `BehaviorParams` JSON, identify known network ID keys (e.g., `targetNetworkId`, `routeEntityId`), swap the stale IDs for the newly allocated ones using your `oldToNewNetworkIds` map, and re-serialize the JSON string before adding it to the `InitialComponents` list.

2\. Reconstructing In-Memory Translation Maps

The approach to repopulating translation maps like `NetworkEntityMap` depends strictly on the operation being performed.

**Scenario Injection (Genesis Pipeline)** By routing the patched scenario entities through `IEntityCreationRequestSource` to the `NetworkSpawningSystem`, the reconstruction of the `NetworkEntityMap` happens automatically. The `NetworkSpawningSystem` is responsible for the ECS/network boundary and explicitly registers newly created entities during local genesis (`_networkMap.Register(networkId, entity);`). No manual map reconstruction is required in the scenario loader; the pipeline guarantees it.

**Checkpoint Restoration (Strict State)** If you are performing a true distributed checkpoint restore where all nodes load their partial snapshots synchronously, the genesis pipeline is bypassed entirely. The ECS binary state is deserialized directly into the live `EntityRepository`. Because network IDs are preserved exactly as they were, you do not remap IDs, but you **must** execute a post-load reconstruction phase to repair transient lookups.

You must clear and explicitly repopulate the `NetworkEntityMap` by querying the fully loaded repository for all entities with a `NetworkIdentity`:

```
_entityMap.Clear();

// Include all lifecycle states to ensure Constructing or Ghost entities are mapped
var query = repo.Query()
    .With<NetworkIdentity>()
    .WithLifecycle(EntityLifecycle.All)
    .Build();

foreach (var entity in query)
{
    long netId = repo.GetComponentRO<NetworkIdentity>(entity).Value;
    _entityMap.Register(netId, entity);
}
```

This strictly mirrors the state reconstruction pattern already proven by `ReplayBridgeSystem` when it synchronizes its internal maps. This design isolates the network mapping logic from the serialization layer, ensuring that transient routing tables accurately reflect the restored state without polluting the core ECS schema.

Shouldnt we introduce a unique entity id (16byte guid) component that is attached on entity creation and used for every stable entity reference like those in json parameters?

Introducing a 16-byte GUID component for stable entity references is redundant and architecturally suboptimal. The system already possesses a dedicated, globally unique stable identifier: the `NetworkIdentity` component, which encapsulates a 64-bit (`long`) network ID.

Your suggestion introduces several architectural flaws:

-   **Violation of Single Source of Truth**: The cluster already guarantees globally unique identity allocation through the `INetworkIdAllocator` pipeline, backed by implementations like `DdsIdAllocator` and `BlockIdManager`. Adding a parallel GUID component fractures identity management.-   **Cache and Memory Bloat**: A 16-byte GUID doubles the identity footprint compared to the existing 8-byte `long`. In an ECS, increasing component size directly degrades cache-line density and memory bandwidth. Furthermore, checking equality on a 64-bit integer is significantly faster than comparing 16-byte structs.-   **Existing Doctrine Alignment**: Current doctrine JSON parameters already leverage the 64-bit network ID correctly. For example, `FireAtTargetParams` explicitly uses `TargetNetworkId` as a `long` to resolve target references via the `NetworkEntityMap`.

For internal cross-entity references where you need to preserve volatile local `Entity` handles (index and generation) across a scenario save/load cycle, the architecture already implements the `IGuidResolver` pattern. This interface dynamically maps volatile `Entity` structs to temporary GUID strings exclusively during the serialization DOM extraction, and resolves them back to newly allocated entities during injection. This mechanism ensures references (like those in `TargetMemory` or `PassengerBuffer`) survive JSON round-trips without permanently polluting the live ECS schema with a heavy GUID component.

You must continue using `NetworkIdentity.Value` as the stable reference for network-aware entities, and rely on the two-pass extraction with `IGuidResolver` for patching volatile local ECS handles during scenario state serialization.

These guid would be used for hard to modify stuff like json params within scenario. Instead of network ids which requires remapping on scenario load. 16bytes per entity is nothing. Lookuo by guid Would be used infrequently (json params) or at scn load time (not hot path).

Your proposal to introduce a permanent 16-byte GUID component specifically to circumvent JSON parameter remapping during scenario loads is architecturally unsound. You are attempting to solve a serialization boundary problem by polluting the runtime ECS schema.

While 16 bytes per entity may seem negligible in isolation, ECS architecture strictly demands that component data models the live simulation state. A GUID used exclusively for cross-entity references during scenario hydration has no place residing in memory during the execution loop. Furthermore, altering your runtime doctrine JSON parameters to rely on GUIDs would force `ParseParams` delegates (such as `ParseFireAtTargetParams`) to perform string-to-GUID dictionary lookups during live execution, abandoning the highly optimized `long`\-based `NetworkEntityMap`.

The framework already provides the correct infrastructure to solve this without modifying the core schema: the `IEntityScenarioTranslator` and `IGuidResolver` patterns.

The `ScenarioSerializer` already assigns a transient `Guid` to every saved entity within the DOM during the extraction pass. You must leverage this existing mechanism to unpack and patch the opaque JSON strings strictly at the I/O boundary.

Here is the clean architectural approach:

-   **Create a Custom Translator:** Implement an `IEntityScenarioTranslator` for the components carrying doctrine JSON, such as `ActiveMissionPlan` or `MissionPlanQueue`.-   **Extraction Pass (Save):** In the `Extract` method, intercept the `BehaviorParams` JSON string. Extract the `targetNetworkId` or `routeEntityId` integer. Use the live `NetworkEntityMap` to resolve that network ID to its local `Entity` handle, and immediately pass it to `IGuidResolver.Resolve(entity)` to get the stable scenario GUID string. Serialize this GUID string into your extracted DOM instead of the network ID.-   **Injection Pass (Load):** In the `Inject` method, read the DOM. Extract the saved GUID string and pass it to `IGuidResolver.Resolve(guidStr)` to obtain the newly instantiated `Entity` handle. Look up its freshly allocated `NetworkIdentity` component, reconstruct the `BehaviorParams` JSON string using the new network ID, and apply the component to the world.

This approach completely decouples the scenario serialization format from the live runtime. The JSON payload within the scenario file safely uses stable GUIDs to survive round-trips, the live simulation safely uses fast `long` network IDs, and the translation occurs exactly where it belongs: at the serialization boundary, leaving your runtime memory footprint and execution paths pristine.

If we keep parsing the json params to remap the id, we can keep using network int ids, no guid conversion is useful, no?

You are absolutely correct. If you are already intercepting and parsing the JSON parameters at the scenario injection boundary to patch the references, introducing an intermediate GUID conversion for those specific fields is entirely redundant and adds unnecessary complexity.

Your deduction highlights an important architectural distinction in how the engine handles different types of cross-entity references:

**1\. Volatile Local References (****Entity** **struct)** Components like `TargetMemory` and `PassengerBuffer` store raw ECS `Entity` handles (a 32-bit index and 16-bit generation). These values are strictly tied to the volatile memory layout of the current ECS world. The `IGuidResolver` pattern exists exclusively to rescue these volatile handles during serialization, converting them to GUID strings so they survive the transition to the DOM, and resolving them back to newly allocated `Entity` handles during load.

**2\. Stable Distributed References (****NetworkIdentity****)** Doctrine JSON parameters, however, do not store volatile `Entity` handles. They store the 64-bit `NetworkIdentity` (e.g., `targetNetworkId` in `FireAtTargetParams` or `routeEntityId` in `FollowRouteParams`). Since these are already stable primitives (`long`), they do not suffer from the memory-layout volatility that requires the `IGuidResolver`.

Because your scenario loader already performs a two-pass extraction where it builds a translation map of `oldNetworkId -> newNetworkId`, the optimal and cleanest path for the doctrine JSON is exactly as you suggested:

-   **Pass 1:** Hydrate the staging repository, allocate fresh Network IDs, and build the `Dictionary<long, long>` map.-   **Pass 2:** Extract the `ActiveMissionPlan` components. Parse the `BehaviorParams` JSON, look up the old `long` ID in your dictionary, and directly replace it with the new `long` ID before pushing the component into the `EntityCreationRequest` payload.

By patching `long` to `long` directly within the JSON string during the injection pass, you avoid polluting the DOM with GUIDs, you avoid writing custom `IEntityScenarioTranslator` implementations for mission components, and you keep the doctrine parameter parsing strictly aligned with its native `long` data types. Your proposed two-pass integer remapping is the correct architectural choice for this specific boundary.

What all network id maps do we use? How to make sure all are filled on scenario or episide load?

In the FDP architecture, identity translation across the distributed network boundary is managed by three primary mapping services:

-   **NetworkEntityMap**: The canonical bidirectional registry mapping stable 64-bit `long` network IDs to local, volatile ECS `Entity` handles.-   **NodeIdMapper**: Maps external DDS `NetworkAppId` structs (Domain ID + Instance ID) to internal `int` node IDs, shielding the core ECS logic from DDS-specific transport layouts.-   **TypeIdMapper**: Maps external DIS `ulong` type values to internal `int` ECS component type IDs.

Your approach to ensuring these maps are correctly populated depends entirely on whether you are injecting unbound templates (Scenarios/Episodes) or restoring strict historical state (Checkpoints).

1\. Scenario and Episode Loading (The Genesis Pipeline)

As established previously, scenarios and episodes must be routed through the `IEntityCreationRequestSource` pipeline as `SpawnEntityCommand` events. If you follow this clean architecture, you **do not manually populate** the `NetworkEntityMap`.

The `NetworkSpawningSystem` acts as the exclusive orchestrator for genesis. When it processes a spawn command, it allocates the ECS entity, applies the initial components, and explicitly executes `_networkMap.Register(networkId, entity);` right before initiating the Entity Lifecycle Module (ELM) construction handshake. Attempting to manually inject entries into the map during scenario loading is a severe anti-pattern that violates the Single Responsibility Principle and risks race conditions against the ELM.

2\. Checkpoint Restoration (Direct Hydration)

Unlike scenarios, checkpoint restoration bypasses the genesis pipeline entirely, deserializing the binary memory dump directly into the live `EntityRepository`. Because the ECS state is hydrated in-place, transient services like `NetworkEntityMap` are left empty and must be manually reconstructed.

After the repository is deserialized, you must execute a post-load pass that queries the world for all entities possessing a `NetworkIdentity` and registers them with the map service. Crucially, you must use `.WithLifecycle(EntityLifecycle.All)` when building this query; restored entities may be in the `Constructing` or `Ghost` state, and defaulting to `Active`\-only queries will result in dropped map registrations and subsequent egress failures.

Regarding Node and Type Mappers

`NodeIdMapper` and `TypeIdMapper` are caching translation layers designed to populate dynamically upon network discovery and descriptor ingress. Scenarios and episodes do not—and should not—serialize transport-specific network topologies or lookup tables.

When your scenario-spawned entities are subsequently published to the network by egress translators (such as `EntityMasterEgressTranslator`), the translation layer will automatically query `NodeIdMapper.GetExternalId()` and populate the caches natively. You must not attempt to reconstruct these routing dictionaries during scenario or episode loads.

For checkpoint snapshot restore, this must happen on each node in parallel from node specific snapshot, is this correct?

Your assessment is correct. In this distributed ECS architecture, a checkpoint restore strictly requires each node to synchronously load its own node-specific snapshot in parallel.

When the cluster orchestrator initiates a `TakeCheckpoint` operation, the system does not serialize a monolithic global state. Instead, the `ClusterSlave` on each node captures its local `EntityRepository` and passes it to a background `CheckpointIOWorker`, which writes an LZ4-compressed binary dump tied strictly to that specific node (e.g., `{requestId}_node_{nodeId}.fdp`).

Because the checkpoint data is fundamentally decentralized, the restoration process must perfectly mirror this topology:

-   **Synchronized Orchestration (2PC):** The cluster orchestrator uses the Two-Phase Commit protocol to drive all nodes into a loading state simultaneously.-   **Node-Specific Hydration:** Each node locates its specific `node_{nodeId}.fdp` payload and deserializes it directly into its live `EntityRepository` as a keyframe, clearing the old memory and replacing it with the binary state. This deliberately bypasses the `NetworkSpawningSystem` and the genesis pipeline.-   **Topology Preservation:** Because the binary snapshot on each node preserves the exact `NetworkAuthority` masks, `NetworkIdentity` values, and `EntityLifecycle` states exactly as they were at the moment of the snapshot, this parallel hydration perfectly reconstitutes the split-authority layout. The nodes instantly know what they own without requiring any DDS negotiation, ghost promotion, or routing broadcasts.

From an architectural standpoint, this decentralized approach is the most performant way to restore a distributed simulation. However, it imposes a strict reliability constraint on your orchestration layer: the orchestrator must enforce a rigid 2PC barrier. If a single node fails to load its partial snapshot (e.g., the NAS failed to distribute the file to that node's local staging directory), the orchestrator must abort the transaction. Permitting the cluster to transition to `OperatingLive` with a missing node's state would result in a catastrophically fractured simulation topology and orphaned cross-entity references.

How to elegantly remap the ids in jsin params? I would like to use dto classes with remappable fields marked with remappibg attributes. No magic strings and manual parsing. The json must be deserialized to corresponding dto class, remapped using pre compiled delegates, serialized back to json.

Your architectural suggestion is sound. Relying on magic strings and manual `JsonDocument` parsing (as currently seen in `MissionControlBehaviorParamsHelper.TryParseFireAtTargetParams`) to maintain referential integrity across network boundaries is brittle and prone to schema drift.

Transitioning to strongly-typed DTOs decorated with remapping attributes, driven by pre-compiled expression trees, perfectly aligns with the engine's existing `FdpAutoSerializer` and `EntityComponentReflector` paradigms. While a full JSON deserialize-remap-serialize roundtrip introduces heap allocations, scenario loading is an I/O boundary operation, making this a highly favorable tradeoff for maintainability and schema safety.

Here is the clean architecture implementation for this mechanism.

1\. The Attribute & DTOs

First, define a targeted marker attribute in your behavior toolkit contracts and apply it to the DTO properties that represent stable network IDs.

```
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class RemapNetworkIdAttribute : Attribute { }

public class FireAtTargetParamsJsonDto
{
    [RemapNetworkId]
    public long TargetNetworkId { get; set; }
    public int MaxRounds { get; set; }
    public float CooldownSeconds { get; set; }
}

public class FollowRouteParamsJsonDto
{
    [RemapNetworkId]
    public long RouteEntityId { get; set; }
    public float Speed { get; set; }
    public bool Loop { get; set; }
}
```

2\. The Expression Tree Compiler

To avoid reflection overhead during the hydration pass, build a compiler that generates a delegate to execute the remapping logic. This delegate accepts the deserialized object and the translation map, applying the new IDs in-place.

```
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

public delegate string ParamRemapDelegate(string json, IReadOnlyDictionary<long, long> idMap);

public static class BehaviorParamRemapperCompiler
{
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static ParamRemapDelegate Compile<TDto>() where TDto : class
    {
        var type = typeof(TDto);
        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<RemapNetworkIdAttribute>() != null)
            .ToArray();

        if (members.Length == 0)
            return (json, map) => json; // Pass-through if no IDs need remapping

        // Parameters: (TDto instance, IReadOnlyDictionary<long, long> map)
        var instanceParam = Expression.Parameter(type, "instance");
        var mapParam = Expression.Parameter(typeof(IReadOnlyDictionary<long, long>), "map");
        var outVar = Expression.Variable(typeof(long), "newId");

        var tryGetValueMethod = typeof(IReadOnlyDictionary<long, long>)
            .GetMethod("TryGetValue", new[] { typeof(long), typeof(long).MakeByRefType() })!;

        var statements = new List<Expression>();

        foreach (var member in members)
        {
            var memberAccess = Expression.MakeMemberAccess(instanceParam, member);
            
            // if (map.TryGetValue(instance.Property, out newId)) { instance.Property = newId; }
            var tryGetCall = Expression.Call(mapParam, tryGetValueMethod, memberAccess, outVar);
            var assignNewId = Expression.Assign(memberAccess, outVar);
            
            statements.Add(Expression.IfThen(tryGetCall, assignNewId));
        }

        var block = Expression.Block(new[] { outVar }, statements);
        var compiledMutator = Expression.Lambda<Action<TDto, IReadOnlyDictionary<long, long>>>(
            block, instanceParam, mapParam).Compile();

        // Wrap the mutator in the JSON serialization boundary
        return (json, idMap) =>
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            
            var dto = JsonSerializer.Deserialize<TDto>(json, _jsonOptions);
            if (dto == null) return json;

            compiledMutator(dto, idMap);
            return JsonSerializer.Serialize(dto, _jsonOptions);
        };
    }
}
```

3\. Registry and Integration

You must bind the `BehaviorId` string to the compiled delegate at startup so the scenario loader knows which DTO rules apply to which task.

```
public class ScenarioBehaviorRemapper
{
    private readonly Dictionary<string, ParamRemapDelegate> _remappers = new();

    public void Register<TDto>(string behaviorId) where TDto : class
    {
        _remappers[behaviorId] = BehaviorParamRemapperCompiler.Compile<TDto>();
    }

    public string RemapJson(string behaviorId, string json, IReadOnlyDictionary<long, long> idMap)
    {
        if (_remappers.TryGetValue(behaviorId, out var remapper))
        {
            return remapper(json, idMap);
        }
        return json; // Unrecognized behaviors pass through
    }
}
```

4\. Application in the Scenario Loader

During Pass 2 of your scenario extraction (after allocating the `oldToNewNetworkIds` map), you process the components and invoke the remapper specifically on mission data:

```
// Inside Pass 2 extraction loop...
if (componentValue is ActiveMissionPlan activePlan)
{
    foreach (var task in activePlan.Plan.Tasks)
    {
        if (!string.IsNullOrWhiteSpace(task.BehaviorParams))
        {
            task.BehaviorParams = _scenarioBehaviorRemapper.RemapJson(
                task.BehaviorId, 
                task.BehaviorParams, 
                oldToNewNetworkIds);
        }
    }
}
```

**Architectural constraint to enforce:** Make sure you register these DTO mappings at the composition root (e.g., alongside `DoctrineRegistry.Register`). By constraining the JSON deserialization/serialization to the specific type tied to the `BehaviorId`, you maintain strict schema validation while entirely abstracting the manual JSON parsing away from the scenario loader's responsibilities.

The behavior dtos mapped to doctrine type should be then used autimatically by the mission editor for type checked user parameter input. Various other attributes like \[MapPickableWorldLocation\], \[MapPickableEntity\] etc should allow for generic UI editing of these.

Your proposal to drive the mission editor UI generically from strongly-typed DTOs using attributes is architecturally sound. It eliminates the brittle, hardcoded `if/else` blocks and manual JSON parsing currently polluting `MissionPanel.cs` (e.g., `DrawMoveToLocationParams`, `DrawFollowRouteParams`).

However, there is a critical architectural constraint you must respect: **ImGui operates in immediate mode, evaluating the UI every single frame (60 Hz).**

If you use raw reflection (`Type.GetProperties()`, `PropertyInfo.GetValue()`) directly inside the `MissionPanel.DrawContent` method, you will introduce massive CPU overhead and garbage collection heap allocations on the hot path, violating the strict zero-allocation budget required for presentation layers.

To implement this cleanly, you must separate the schema extraction (cold path) from the UI rendering (hot path). You should build a compiled UI generator at application startup—similar to how `FdpAutoSerializer` compiles expression trees to bypass reflection during network serialization.

Here is the clean architecture approach to implement generic DTO-driven UI:

1\. Define the Presentation Attributes

Define the attributes in your shared contracts assembly. They act as markers for the UI compiler.

```
[AttributeUsage(AttributeTargets.Property)]
public sealed class MapPickableWorldLocationAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class MapPickableEntityAttribute : Attribute 
{
    public string[]? FilterPresets { get; }
    public MapPickableEntityAttribute(params string[] filters) => FilterPresets = filters;
}
```

2\. Apply to the DTOs

Decorate your task DTOs. This cleanly encapsulates both the network patching metadata and the presentation metadata in one place.

```
public class FireAtTargetParamsDto
{
    [RemapNetworkId]
    [MapPickableEntity] // Generic UI will render a Pick button
    public long TargetNetworkId { get; set; }
    
    public int MaxRounds { get; set; }
    public float CooldownSeconds { get; set; }
}

public class MoveToLocationParamsDto
{
    [MapPickableWorldLocation]
    public double TargetLat { get; set; }
    [MapPickableWorldLocation]
    public double TargetLon { get; set; }
    public float Speed { get; set; }
}
```

3\. Build the UI Delegate Compiler (Cold Path)

At startup, reflect over your DTOs and compile a rendering delegate for each behavior. This delegate will deserialize the JSON into the DTO, emit the specific ImGui calls (InputInt, InputFloat, Button), and serialize it back to JSON if modified.

```
public delegate string BehaviorUiDrawDelegate(
    string currentJson, 
    int taskIndex, 
    IMapPickService pickService, 
    bool isPickPending);

public static class BehaviorUiCompiler
{
    public static BehaviorUiDrawDelegate Compile<TDto>() where TDto : class, new()
    {
        var properties = typeof(TDto).GetProperties();
        
        return (currentJson, taskIndex, pickService, isPickPending) =>
        {
            // 1. Deserialize current state
            var dto = string.IsNullOrWhiteSpace(currentJson) 
                ? new TDto() 
                : JsonSerializer.Deserialize<TDto>(currentJson) ?? new TDto();

            bool changed = false;

            // 2. Execute UI components sequentially (Pre-compiled logic loop)
            foreach (var prop in properties)
            {
                var val = prop.GetValue(dto);
                string label = $"{prop.Name}##{taskIndex}_{prop.Name}";

                // Handle Pickable Entity
                var entityAttr = prop.GetCustomAttribute<MapPickableEntityAttribute>();
                if (entityAttr != null && val is long entityId)
                {
                    ImGui.Text($"{prop.Name}: {entityId}");
                    ImGui.SameLine();
                    if (isPickPending)
                    {
                        ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "[Picking...]");
                    }
                    else if (ImGui.SmallButton($"Pick##{label}"))
                    {
                        // Fire the async pick service
                        // The callback (handled in MissionPanel) will update the JSON directly
                        pickService.PickEntityAsync(entityAttr.FilterPresets);
                    }
                    continue;
                }

                // Handle Primitives
                if (prop.PropertyType == typeof(float))
                {
                    float fVal = (float)val;
                    if (ImGui.InputFloat(label, ref fVal))
                    {
                        prop.SetValue(dto, fVal);
                        changed = true;
                    }
                }
                else if (prop.PropertyType == typeof(int))
                {
                    int iVal = (int)val;
                    if (ImGui.InputInt(label, ref iVal))
                    {
                        prop.SetValue(dto, iVal);
                        changed = true;
                    }
                }
                // (Add bindings for other primitives as needed...)
            }

            // 3. Serialize and return if changes occurred
            return changed ? JsonSerializer.Serialize(dto) : currentJson;
        };
    }
}
```

_Note: For absolute peak performance, you can replace_ _prop.GetValue__/__prop.SetValue_ _inside the delegate with compiled_ _System.Linq.Expressions.Expression_ _getters and setters, mirroring the_ _FdpAutoSerializer_ _architecture__._

4\. Integrate into `MissionPanel` (Hot Path)

Register these delegates at application composition time. In `MissionPanel.cs`, remove the hardcoded `DrawFireAtTargetParams` and `DrawMoveToLocationParams` methods. Instead, do a dictionary lookup.

```
// Inside MissionPanel.DrawContent()
string paramsBuffer = task.BehaviorParams ?? string.Empty;

if (_uiDelegates.TryGetValue(task.BehaviorId, out var drawDelegate))
{
    // The delegate handles all rendering and returns a new JSON string if the user interacted.
    bool pickingThis = IsEntityPickPending && _pendingPickTaskIndex == i;
    string newJson = drawDelegate(paramsBuffer, i, pick, pickingThis);
    
    if (newJson != paramsBuffer)
    {
        HandleEditBehaviorParams(i, newJson);
    }
}
else
{
    // Fallback: Multiline raw JSON editor for unrecognized behaviors [1]
    DrawRawJsonEditor(i, ref paramsBuffer);
}
```

Conclusion

By adopting this strategy, you decouple the UI layout logic from `MissionPanel`, establishing a generic form-generation engine. The use of pre-evaluated delegates ensures that the domain schemas dictate the UI dynamically without sacrificing the 60 Hz rendering performance constraint of the IG and ExCon nodes.

Dnes • 0:33

