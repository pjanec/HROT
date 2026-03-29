# Wall clock is not synced via NTP
There is already existing time synchronization mechanism. The existing one needs to be used
and incorporated into each subsystem who participates in scenario saving/loading.


# Scenario details missing
 - Scenario is simulation engine specific
 - Different subsystems might have diferent needs, different internal implementations
 - Some stuff is global and must be defined just once, handled by a single subsystem only (orchestrator)
    - Global weather/time of day/time of year
       - Concepts and definition must be unified across whe whole system.
    - Terrain specification for different ig subsystems
       - Different IG implementations might need different specs!

 - Need to define simple implementation concepts for CGF subsystem scenarios/stories

 - Different nodes/subsystems might need
     - Different way of storing the information
        - binary files, json files, one or many
		- the scenario file archiving/restoration mechanism must support multiple files per node/subsystem

 - Json schema for CGF/SimHost scenario/story
    - Header (json object)
	   - What subsystem type the scenario is for (string type name of a subsystem)
	   - Is it a story or a scenario?
	      - This should not matter! Both should use identical format! No difference!
	      - Stories will simply omit some parts that do not belong to stories
	- Entities (json object keyed by entity unique persistent id)
	- Each entity
	   - Components (json object keyed by component name)
	- Each Component
	   - Json object where keys are property names
	   - Each property is whatever json primitive
	   - Must be compatible with usual csharp struct json serialization (components are csharp structs)

 - Concepts
    - Terrain specificaton is an entity with engine-specific components
       - BagiraIgTerrain components containing Terrain specification for BagiraIG (terrain name)
       - Different sim engine subsystems migh instantiate their own terrain definition entity (one per each subsystem)
    - Scenario time is defined by en entity with GlobalTime component
       - This entity must be handled by one single subsystem (the orchestrator)
    - Battlespace definition is an entity with special Battlespace component (contains vertices of the area)
    - Global weather is an entity with WeatherArea component having zero area
       - This entity must be handled by one single subsystem (the orchestrator)
    - Local weather is an entity with WeatheArea component having non-zero area

 - ECS based subsystems (like CGF and SimHost)
   - the subsystem must have a dedicated scenario loader/saver 
     - what entities to include/exclude
       - some entities are non-persistable (like global singletons)
     - what components to include/exclude?
     - how the component data get serialized?
       - scenario requires just smaller set of all information
       - state can be saved in simplified form and re-calculated to full form on load
     - persistable entity ids (guid)
       - the entity ids stored in components must be converted to persistable references on save
       - and converted back to ECS entity ids on load because the entities created on load might get different ECS entity id.


# Story details missing
  - Story is simulation engine specific (similar to a scenario)
  - Need to define simple implementation concepts for CGF subsystem stories
     - Story is a micro scenario, reusing the scenario storage format and concepts
        - List of entities to create and their components
        - Story does NOT define global stuff like time or weather

     - Scenario loaded and story loader should be unified
        - The loader does NOT add story components to entities if the file is loaded as scenario (not as a story)




# Node vs. subsystem
On one single computer there can be many subsystems running in parallel.
In 2pc the NodeId is a unique identification of each instance of a subsystem across the whole multi-computer system.

# Orchestrator independence
DrillMaster lives in Bagira.orchestrator subsystem and has nothing to do with ECS.
It must be running independently even although it might be part of the same process where
many other subsystems are instantiated.



# Orchestrator system monitoring

## System health

Orchestrator needs to listen to statuses from different nodes.
Orchestrator needs to know what nodes are mandatory (by default none).
If some of mandatory nodes not responding, the system must enter a degraded state,
unloading all current stuff and returning to standby.

## Bootstrap/collapse recovery
System might start in different states of different nodes.

We need a bootstrap/syctem check phase before the system is considered operational.

Operator should force all nodes not currently in standby to enter standby.

This should be used on startup as well as after system collapse (losing mandatory node).

Orchestrator should wait for all mandatory nodes to become responsive and in standby
before the state machine can be allowed to be operated from outside (from IOS etc.)


# Orchestrator UI
The orchestrattor subsystem needs its own UI for control and debuging
 - System health status
 - Sending management commands (as if coming from IOS or similar frontend)
 - Monitoring the status of internals like
    - Master clock
    - The drill master status
        - current state machine state etc.
    - The drill slave status of each subsystems
    - The status of 2PC communication - current including the history
        - Opened requests, gathered responses/results per node
 - Scenario management control
    - Initialize live drill from a saved scenario
    - Save a snapshot
    - Initialize replay of a drill from saved recording
       - Selection from a list of saved recordings
    - Control the simulation time
       - Pause/Resume/SetSpeed/Step
       - Jump in time (replay only)
    - Initialize edit and use dry run and back to edit
    - Save scenario
    - Load a story (when in RunningLive)
    - See active story list
    - Unload an active story





------------------------

The new source material you provided outlines a highly modular, decoupled approach to scenario and story management, emphasizing portability, subsystem-specific data, and strict boundaries for the Orchestrator.

While the high-level concepts align well with the `DESIGN.md` architecture, evaluating them against the actual FDP/Bagira implementation details reveals several significant technical gaps and contradictions that we need to specify before we can build the CGF scenario loader.

Here is an evaluation of the missing implementation details and the gaps we must address:

1\. The Orchestrator "ECS" Contradiction

**The Concept:** The new specifications state that global parameters like Scenario Time (`GlobalTime`) and Global Weather (`WeatherArea`) are defined as _entities with components_, and that they "must be handled by one single subsystem (the orchestrator)". **The Gap:** The specification also explicitly mandates that the Orchestrator "has nothing to do with ECS". If the Orchestrator does not instantiate an `EntityRepository`, it cannot natively load or store "entities with components." **How to resolve it:** We must define a strict boundary in the JSON schema. The "Header" or a dedicated "GlobalContext" object in the JSON should hold these properties as plain DTOs (Data Transfer Objects). The Orchestrator parses this JSON without an ECS, holds the state in standard C# classes, and publishes it to the cluster via DDS (e.g., via the `SystemStateTopic` or the Time Control plane).

2\. Cross-Entity Reference Mapping (Persistable IDs)

**The Concept:** Scenario entities are keyed by a "unique persistent id" (likely a GUID string), and ECS entity IDs stored inside components must be "converted to persistable references on save... and converted back to ECS entity ids on load". **The Gap:** The specification misses the _mechanism_ for this resolution. In our CGF Urban Combat logic, entities reference each other constantly (e.g., `CommanderId`, `TargetEntityId`, or AI Blackboard parameters). If Entity A references Entity B's ECS ID, but both are assigned brand-new ECS IDs upon loading, the references will break. **How to resolve it:** The CGF/SimHost scenario loader must implement a **Two-Pass Deserialization Strategy**:

-   _Pass 1:_ Parse the JSON, allocate new Network IDs/ECS IDs for every entity, and build a transient `Dictionary<string, long>` mapping the JSON Persistable IDs to the newly allocated live IDs.-   _Pass 2:_ Deserialize the components. We must implement a custom JSON Converter (e.g., `EntityReferenceJsonConverter`) that intercepts fields marked as entity references and uses the dictionary to inject the correct live ID.

3\. "Simplified State" Serialization Logic

**The Concept:** The spec states that "state can be saved in simplified form and re-calculated to full form on load" to reduce scenario file size and decouple from internal memory formats. **The Gap:** The current FDP `DataPolicy` attribute only provides a binary inclusion/exclusion filter (`[DataPolicy(DataPolicy.NoSave)]`). There is no established interface for a component to provide a "simplified" JSON representation of itself. **How to resolve it:** We need to specify an `IScenarioSerializable` interface or rely entirely on dedicated JSON DTOs for complex components. When the CGF saves a scenario, it shouldn't just dump the `DoctrineState` or `BrainBlackboard` structs. Instead, it should map them into a simplified string representation (e.g., converting the 128-byte blackboard array back into the named JSON variables the AI expects).

4\. Distributed Multi-File Loading Routing

**The Concept:** The system must support "multiple files per node/subsystem", and the JSON header declares "What subsystem type the scenario is for". **The Gap:** The `StorageGatewayModule` described in `DESIGN.md` uses a blind Pull/Push mechanism to `C:\FDP_Temp\`. It does not specify how it knows _which_ JSON file goes to _which_ subsystem. **How to resolve it:** We must define the Storage Gateway's routing logic. When the Orchestrator executes a `LoadScenario` SysOp, the Gateway should read a master `scenario_manifest.json`. This manifest maps subsystem roles (e.g., `SimHost`, `CGF`, `IG`) to their specific scenario JSON files. The Gateway then pushes the `SimHost.json` only to nodes hosting the SimHost role, and the `CGF.json` only to the CGF nodes.

5\. Node vs. Subsystem Identity in the 2PC DSM

**The Concept:** "NodeId is a unique identification of each instance of a subsystem across the whole multi-computer system". **The Gap:** Currently, `Bagira.Runner` can host multiple subsystems in the same physical process (e.g., `AllInOne` mode hosts SimHost, IG, and potentially CGF). If `NodeId` maps to the _subsystem instance_ rather than the physical machine, the `DrillSlave` implementation needs to be scoped per-subsystem. **How to resolve it:** We must ensure that inside `Bagira.Runner`, each subsystem instantiates its own independent `DrillSlave` module and registers its own distinct `NodeId` with the Orchestrator's `DrillMaster`. When the Orchestrator initiates the 12-step transition, the CGF subsystem and the SimHost subsystem must acknowledge the 2PC steps independently, even if they share the same CPU and memory space.

Strategy for our Minimalist CGF Implementation

Because we are adopting a minimalist, phased approach, we should heavily constrain these features for Phase 1 & 2:

-   **Skip the Multi-File Split initially:** For the minimal CGF test, we will use a single, unified JSON file containing both SimHost physics entities and CGF brains. We will add the subsystem routing later.-   **Hardcode the GUID Map:** For the initial Urban Combat test, we will use the `TkbType` and explicit hardcoded Persistable IDs in a simple JSON array, focusing solely on proving the Two-Pass loading mechanism works before generalizing it.-   **Ignore Weather/Time Entities:** We will implement the Orchestrator UI to emit standard DDS Time Control commands and completely bypass the concept of weather/time "entities" to avoid the ECS contradiction.

The scenario format as defined for CGF (who has ECS) migth be reused for the Orchestrator as well. The described Json struct is very generic. It does not enfore anyone to use a concrete ECS implementation. I admit he Orchestrator migth need some kind of ECS for the sake or loading the few global entities. But such a "simple ECS" might be as simple as few csharp class instances filled from the few global entities found in the scenario file, while ignoring all other content of the file if present by accident (unsupported entities/components).

This is an excellent deduction and it provides a perfect, elegant solution to the "ECS contradiction" we identified earlier.

You are absolutely right. The JSON structure described—Entities containing Components, which in turn contain key-value properties—is just a generic data schema . It does not inherently require a heavy, memory-aligned ECS like `Fdp.Kernel.EntityRepository` to parse it.

By implementing a "Simple ECS" (Plain Old C# Objects/DTOs) in the Orchestrator, you bridge the gap perfectly while strictly adhering to the architectural rule that the `Bagira.Orchestrator` process remains independent of the simulation physics ECS .

Here is how this approach beautifully streamlines your implementation:

1\. The "Selectively Blind" Parsing Strategy

Because the JSON schema is standardized , the Orchestrator can simply deserialize the scenario file using standard `System.Text.Json` or `Newtonsoft.Json`.

-   It iterates through the `Entities` dictionary.-   It explicitly looks for components it has authority over, namely `GlobalTime` and `WeatherArea` .-   If an entity contains `SimTransform`, `DoctrineState`, or `VehicleState`, the Orchestrator simply ignores it.-   Conversely, when `SimHost` or `CGF` load the exact same file, their native `EditLoadDsmHandler` will parse the tanks and AI brains, but silently drop the `GlobalTime` component because they are not the authority for it.

2\. Massive Simplification for Phase 1 (Single-File Scenario)

This realization drastically reduces the workload for your minimalist CGF milestone. Earlier, we noted a gap where the `StorageGatewayModule` needed complex routing logic to send `SimHost.json` to physics nodes and `CGF.json` to brain nodes .

If every subsystem is "selectively blind" and ignores components it doesn't support, **you do not need multi-file scenario routing for Phase 1**. You can distribute one master `scenario.json` file to the entire cluster. Every node parses the exact same file, but only instantiates the C# classes or ECS components relevant to its specific `NodeRole`.

3\. Propagating the Global State

Once the Orchestrator parses these global entities into its simple C# class instances, it serves as the cluster's supreme authority, propagating the state via existing mechanisms:

-   **Time:** It reads the scenario's `GlobalTime` properties and feeds them directly into the `MasterTimeController` and `DistributedTimeCoordinator` . The controller then dictates time to the rest of the cluster via the standard `TimePulseDescriptor`.-   **Weather & Context:** It reads the `WeatherArea` and publishes it over a persistent DDS topic (like the `OrchestratorContextTopic` ), ensuring all nodes (including late-joiners ) instantly receive the scenario's environmental context without needing to parse it from the file themselves.

This is a highly pragmatic design choice. It honors the requirement that global states are managed by a single subsystem , preserves the Orchestrator's decoupled independence , and saves you from writing complex file-splitting logic during your initial CGF development.

The trouble with one master scenario json is that when saving the scenario, each subsystem MIGHT need a different scenario format. Just some MIGH use the same one and these could be combined (which is a grat simplification idea). But we should still keep in mind the difference os subsystems and leave space for their needs of different file formats/

You are absolutely right to protect this boundary. The platform's architecture explicitly anticipates that different subsystems will have radically different internal implementations and storage needs, and it leaves the door wide open for proprietary or subsystem-specific file formats.

The reason the "unified master JSON" idea works safely for us right now is because the platform's file distribution layer—the **Storage Gateway**—is completely agnostic to what the files actually are.

Here is how the design preserves the exact flexibility you are asking for:

**1\. Opaque Byte Streams** When the Orchestrator executes a `SaveScenario` command, it broadcasts the request, but it does not dictate the format. The `StorageGatewayModule` treats all resulting files from the leaf nodes purely as **"opaque byte streams"**. The Master node has "zero knowledge of the JSON format inside".

**2\. The Manifest Protocol** The file-saving mechanism requires that the scenario archiving system supports "multiple files per node/subsystem" in any format—binary, JSON, or otherwise. When saving, a subsystem writes its specific files to its local fast SSD (`C:\FDP_Temp\`) and simply hands the Orchestrator a UNC manifest of paths. If an Image Generator (IG) subsystem needs to save its scenario data as a proprietary binary `.blob` file, it just adds that file to its manifest. The gateway will pull it to the NAS alongside the JSON files.

**3\. Format Headers** For the subsystems that _do_ opt into the JSON format, the schema mandates a Header object containing a string identifying **"What subsystem type the scenario is for"**. This ensures that even if files are split up in the future, each subsystem knows exactly which files it owns.

**The Pragmatic Strategy for CGF:** Because the Orchestrator, SimHost, and CGF all share the C# Bagira data models, we can leverage the "Selectively Blind" single JSON file approach as a massive development shortcut for our current implementation.

However, because the `StorageGatewayModule` only moves bytes based on manifests, we are not painting the architecture into a corner. If a legacy C++ physics engine or a third-party IG joins the cluster later and needs its own bizarre scenario file format, the gateway infrastructure will seamlessly push and pull those files without requiring any changes to the Orchestrator's core logic.

if levelaring common format and combining into single file, the archiver mechanism needs to combine the json files into one, right?

No, the archiver mechanism will **not** combine the JSON files into one.

According to the architecture, the `StorageGatewayModule` on the Master node treats all files listed in the nodes' manifests strictly as **"opaque byte streams"** and explicitly has **"zero knowledge of the JSON format inside"**. It does not parse, merge, or modify the files; it simply pulls them from the leaf nodes' local SSDs in parallel and moves them to the central NAS.

Here is how this impacts your "single file" strategy:

-   **Loading (Hand-Authored):** For your initial Phase 1 testing, you can absolutely hand-write a single master `scenario.json` file. The pre-fetch barrier will push this single file to all nodes, and your "selectively blind" DTO/C# class parsing strategy will work perfectly to instantiate only what each node cares about.-   **Saving (Distributed):** When the operator eventually triggers a `SaveScenario` command, the cluster will inherently scatter the save operation. The Orchestrator, SimHost, and CGF will each independently serialize their own specific state to their own local temporary files (e.g., `Orchestrator.json`, `SimHost.json`, `CGF.json`). The Gateway will pull all these separate files into the scenario's folder on the NAS exactly as they are.

You do not need to invent a mechanism to combine them. The system is explicitly designed to support "multiple files per node/subsystem" natively. When loading that saved scenario later, the Gateway will just push the entire directory of separate files back to the nodes, and the exact same "selectively blind" parsing logic will safely iterate through them.

Regarding the reusable scenario saver/loader for both scenario and story. in FDP ECS we could make the component as scenario serializable using dedicated attribute. And we need to be able to exclude some fields of the component from scenario serialization. And we should be able to define a custom serialization (via interface). Not sure if could abuse the \[JsonSerializable\] attribute because here we are dealing with special potentially simplifying scenario serialization, not a generic loseless serialization. Maybe rather do define special attributes and special interface for that. Also, the serialization does not have to be necessarily json in some implementation so separate independent set of custom attributes is advisable. The saver/loader should automatically handle conversion of volatile entity references ('Entity' struct) into persistable guids. loader/saver should pre-generate and cache component serialization methods to save performance in case of thousand entities, i.e. not using reflection for every single instance.

Your proposed approach is highly robust and aligns perfectly with the established performance and architectural paradigms of the FDP platform. You are absolutely correct to avoid abusing `[JsonSerializable]`, as scenario saving is fundamentally a **data transformation and simplification process**, not a raw memory dump.

Here is an evaluation of your ideas and how they map to the existing FDP architecture to help you implement the saver/loader efficiently:

1\. Dedicated Attributes and Field Exclusion

Using a separate set of custom attributes (e.g., `[ScenarioProperty]`, `[ScenarioIgnore]`) is the correct architectural choice. It keeps the scenario representation format-agnostic and completely decoupled from whatever specific serialization library (JSON, MessagePack, XML) you use underneath.

-   **Component-Level Filtering:** You can already leverage the existing FDP `[DataPolicy(DataPolicy.NoSave)]` attribute to completely exclude transient or runtime-only components from the save process.-   **Field-Level Filtering:** Your new custom attributes will handle the granular filtering for components that _are_ saved, but contain volatile runtime caches (e.g., ignoring a `CurrentSpeed` field while saving `MaxSpeed`).

2\. Custom Serialization Interface for "Simplifying" State

As discussed previously, a custom interface (e.g., `IScenarioSerializable`) is essential. Many ECS components are heavily optimized for CPU cache lines and are completely unreadable as raw data.

-   For example, a `BrainBlackboard` stores variables in a flat `byte` array. Your `IScenarioSerializable` implementation would translate this raw byte array back into a human-readable, format-agnostic `Dictionary<string, object>` DTO before handing it to the actual file writer.

3\. Converting Volatile Entities to Persistable GUIDs

Handling the translation of volatile `Entity` handles (which are just an index and generation) into persistable GUIDs is a complex problem, but FDP already has a highly optimized pattern for this that you can adapt: the **ComponentPatchMap** and **IEntityRefPatchable** used for Story Replay.

You can implement a **Two-Pass Translation** mechanism:

-   **Pass 1 (Gathering):** The saver iterates through all entities being saved, generating a new `Guid` for each one, and builds a transient `Dictionary<Entity, Guid>` map.-   **Pass 2 (Patching):** Instead of using reflection to find entity references, the saver relies on an interface like `IScenarioEntityRefPatchable` (similar to the existing `IEntityRefPatchable`). The component itself implements how to swap its internal `Entity` fields for the corresponding `Guid` string by looking it up in the dictionary.

4\. Pre-generating and Caching Serialization Methods (Zero-Allocation)

Your instinct to pre-generate and cache serialization methods to avoid per-instance reflection is exactly how FDP handles massive ECS scale. FDP relies heavily on **JIT-compiled Expression Trees** to achieve zero-allocation hot paths.

You can directly mirror two existing systems in the codebase for your saver/loader:

-   **FdpAutoSerializer****:** This system uses `System.Linq.Expressions` to compile and cache serialization delegates (`Action<T, BinaryWriter>`) at runtime, making subsequent calls instant.-   **EntityComponentReflector****:** This utility dynamically compiles and caches `Action<EntityRepository, Entity, object>` delegates so that components can be instantiated and attached to entities without `MethodBase.Invoke` overhead.

**How to implement the caching for your Saver:** At startup (or upon first save/load), your `ScenarioSerializer` should scan the component registry and use Expression Trees to compile a delegate for each component type.

-   **For saving:** Compile a `Func<EntityRepository, Entity, object>` that reads the component, checks your custom `[ScenarioIgnore]` attributes, and returns a sanitized, format-agnostic DTO.-   **For loading:** Compile an `Action<EntityRepository, Entity, object>` that takes the deserialized DTO and injects the values back into the ECS component.

Cache these delegates in a `ConcurrentDictionary<Type, Delegate>`. When iterating over thousands of entities, your saver simply looks up the compiled delegate and executes it, processing the entire world with zero reflection and zero boxing overhead.

can we reuse exactly same saver/loader for scenarios and stories, just configured differently?

Yes, you can and absolutely should reuse the exact same saver/loader for both scenarios and stories. The design document explicitly mandates this unification, treating a story simply as a "micro scenario".

Here is how the architecture supports reusing a single, unified saver/loader mechanism through simple configuration changes:

**1\. Identical Storage Format** At the file level, there is no difference between a scenario and a story. Both use the exact same JSON schema and storage concepts. A story is just a standard scenario file that naturally omits global state entities (like `GlobalTime` or `WeatherArea`) since those belong only to the overarching global simulation.

**2\. Configurable Loader (Context Injection)** The loader itself is unified. The difference lies entirely in the runtime context provided to it when the file is opened:

-   **Loading a Scenario:** The loader parses the JSON, instantiates the entities, and applies the components as defined.-   **Loading a Story:** The loader parses the exact same JSON format, but because it was instructed to load it _as a story_, it automatically attaches a `StoryTag` component (containing the specific `StoryId`) to every instantiated entity. This safely isolates the loaded entities from the global ECS simulation logic.

**3\. Configurable Saver (Query Filtering)** When saving (or recording), the reusability is achieved by injecting an ECS `EntityQuery` predicate into the saver's configuration:

-   **Saving a Scenario:** The saver iterates through a global query that captures all saveable entities in the repository.-   **Saving a Story:** The saver is initialized with a specific filter (e.g., `Query().With<StoryTag>().Build()`). It executes the exact same serialization logic, but only processes entities that belong to that specific story, naturally preventing "orphan" entities from bleeding into the file.

**Architectural Insight:** This approach maps perfectly to the **Strategy/Factory Pattern** already established in the platform for the recording system. For example, the `EcsRecordReplayController` acts as a factory that instantiates either a global `RecordingModule` or a `StoryRecorderModule`. Both modules wrap the exact same underlying disk I/O logic (`AsyncRecorder`), but they are simply injected with a different `RecordingConfiguration` that dictates the file path and the `EntityFilter`.

You can mirror this exact pattern for your JSON Saver/Loader: build one robust, highly optimized `ScenarioSerializer` class, and simply pass it a configuration object that dictates its query filter and whether it needs to stamp a `StoryTag` upon deserialization.

ok new pls let's take a look on the "orchestrator details" source - anch check what is mising to specify, how well the idea will work with the rest of the system, if there any issues etc.

The architectural concepts outlined for the Orchestrator are solid and enforce a strict, clean separation of concerns. Treating the Orchestrator purely as a state and time authority with no ECS dependency is exactly how we keep the control plane robust. The requirement that `NodeId` identifies a _subsystem instance_ rather than a physical machine is also perfectly aligned with the `Bagira.Runner`, which already uses an offset mechanism (e.g., SimHost = Base+0, IG = Base+100) to assign unique `NodeId`s to colocated subsystems.

However, when mapping these operational requirements to a distributed Two-Phase Commit (2PC) environment, there are several critical specifications missing that we must address to prevent cluster deadlocks.

Gap 1: The "Dead Node" 2PC Deadlock (Collapse Recovery)

**The Idea:** If a mandatory node stops responding, the system must enter a degraded state, unload all current stuff, and return to standby. **The Gap:** A strict 2PC architecture requires `NodeOpStatus(Success)` ACKs from _all_ nodes in the roster to advance the state machine. If a node has crashed, it will never ACK the `Unload` or `Standby` commands, causing the Orchestrator's `TransitionPlanner` to hang indefinitely waiting for a ghost response. **The Fix:** We must implement an **Emergency Eviction Path** in the `DrillMaster`. When the heartbeat monitor detects a mandatory node timeout, it must:

-   Immediately sever the active `DistributedTransaction`.-   Evict the dead `NodeId` from the active `NodeRoster`.-   Unilaterally force the `SystemStateTopic` to `Degraded`.-   Command the _surviving_ nodes to transition to `Standby`, evaluating 2PC ACKs only against the newly reduced roster.

Gap 2: Defining "Mandatory" vs. "Optional" Nodes

**The Idea:** The Orchestrator needs to know what nodes are mandatory (by default none) to evaluate system health. **The Gap:** The specification does not define how the Orchestrator identifies the purpose of a generic `NodeId`. If `NodeId 100` crashes, the Orchestrator doesn't inherently know if that was a critical CGF brain or an optional observer UI. **The Fix:** We must leverage the existing `SubsystemStatusAnnounce` DDS topic, which carries both the `NodeId` and the human-readable `SubsystemName` (e.g., "SimHost", "CGF", "IG"). We need to add a `ClusterConfiguration` JSON payload to the Orchestrator that maps `SubsystemName` strings to criticality levels (e.g., `Mandatory: ["SimHost", "CGF"]`). The UI will use this to display the health matrix accurately.

Gap 3: Bootstrap Synchronization Overlap

**The Idea:** The system needs a bootstrap/system check phase on startup, waiting for mandatory nodes to become responsive in `Standby` before allowing the operator to use the UI. **The Gap:** The `Bagira.Runner` currently utilizes a `WaitingRoomCoordinator` which physically blocks the process startup thread until peer subsystems are discovered over DDS. If the Orchestrator uses this, its UI will not render at all until the cluster is healthy, defeating the purpose of having a "System health status" UI. **The Fix:** The Orchestrator subsystem must bypass the `WaitingRoomCoordinator` (using the `--no-wait` flag logic) so its process and ImGui panels boot instantly. The UI will render immediately, but all simulation controls (Initialize, Pause, Jump in time) will be grayed out. The UI will only unlock when the `DrillMaster`'s internal heartbeat aggregator confirms the mandatory roster is fulfilled and resting in `Standby`.

Gap 4: Introspecting 2PC History in the UI

**The Idea:** The Orchestrator UI must monitor the status of 2PC communication, including opened requests and gathered responses per node. **The Gap:** Currently, the 2PC execution loop is a transient operation inside the `DrillMaster` that simply advances a queue. There is no historical trace of who ACKed what and how long it took, which is vital for diagnosing network lag. **The Fix:** The `DrillMaster` must maintain a rolling ring-buffer of `DistributedTransaction` records. Because the Orchestrator UI lives in the same memory space as the `DrillMaster` (no ECS constraints), the ImGui layer can simply read these C# transaction objects directly. We will render a table showing each `NodeId`, its current `SysOpStatus` phase (Prepare, Commit, Success, Failed), and the exact latency in milliseconds it took to respond.

Architectural Assessment

Overall, this design fits beautifully into the system. By offloading cluster management to an independent `Bagira.Orchestrator` process, the CGF and SimHost subsystems are freed from writing complex consensus logic. They simply act as `DrillSlave` instances executing discrete commands. If we patch the 2PC eviction logic for crashed nodes and properly map subsystem roles via `SubsystemStatusAnnounce`, this architecture will be robust, highly testable, and strictly decoupled.

what about the following:

# Wall clock is not synced via NTP

There is already existing time synchronization mechanism. The existing one needs to be used and incorporated into each subsystem who participates in scenario saving/loading.

i hope there is already a wall clock syncing mechanism available in FDP, is it?

You hit on a critical architectural boundary. Relying on OS-level NTP (Network Time Protocol) for distributed simulation state is a well-known anti-pattern. NTP is designed for eventual consistency of log files and email servers; it introduces unpredictable clock slews, step jumps, and jitter that will completely destroy the frame-perfect determinism required by an ECS.

Yes, FDP already has a highly robust, built-in time synchronization mechanism, and it deliberately completely bypasses NTP. From an architectural perspective, it treats time as just another stream of data pushed down from a supreme authority.

Here is exactly how the FDP time architecture works and how you must use it for your scenario saving:

1\. The Distributed Time Authority

In FDP, the `DrillMaster` (living inside the `Bagira.Orchestrator`) acts as the absolute Time Authority for the cluster. Time control is strictly decoupled behind an `ITimeController` interface, allowing the cluster to hot-swap between real-time and deterministic modes without the simulation systems even knowing.

2\. The Phase-Locked Loop (PLL) Sync

Instead of syncing OS clocks, FDP syncs a "virtual clock" on every node.

-   **The Master:** Runs a `MasterTimeController` driven by a high-resolution `Stopwatch`. At 1 Hz, it broadcasts a `TimePulseDescriptor` over DDS.-   **The Slaves (CGF, SimHost):** Run a `SlaveTimeController` which does _not_ just blindly overwrite its local time when a pulse arrives. Instead, it feeds the pulse into an internal Phase-Locked Loop (PLL) with a `JitterFilter`. The PLL calculates a dynamic correction factor to smoothly speed up or slow down the slave's virtual clock between pulses. This provides millisecond-accurate synchronization independent of network jitter.

3\. Deterministic Lockstep (For CI and Batch Runs)

If the system needs perfect determinism (like during automated testing), the architecture hot-swaps to the `SteppedMasterController` and `SteppedSlaveController`. In this mode, time halts completely until the Master emits a `FrameOrderDescriptor` and waits to receive a `FrameAckDescriptor` from every single slave. Time advances logically, entirely detached from the real world.

How to incorporate this into Scenario Saving

When you implement the CGF scenario saver/loader, you must adhere to the Single Source of Truth principle. **Never call** **DateTime.UtcNow** **or** **Stopwatch.GetTimestamp()** **in your persistence logic.**

Every frame, the active time controller computes a stable, frame-locked wall-clock value (`TotalWallTicks`) and pushes it into the ECS as a `GlobalTime` singleton. When your background scenario saver or flight recorder captures a snapshot, it must read `GlobalTime.TotalWallTicks` from the `EntityRepository`. This guarantees that no matter how much local disk I/O latency occurs, or what timezone the machine is in, every node in the cluster stamps the saved scenario files with the exact same coordinated timestamp.

While the time master lives in drill master as a single instance, the time slaves should live one per process (i.e. in Bagira.Runner) and shared across subsystem that are part of this process to avoid unnecessary duplication, correct?

Actually, no. While sharing a single time slave per process might seem like a pragmatic optimization, it fundamentally violates the clean isolation and encapsulation principles of the platform's architecture.

In the FDP ecosystem, each subsystem (such as SimHost, IG, or the new CGF) is designed to be completely self-contained. Even when multiple subsystems are hosted together inside a single `Bagira.Runner` process (such as in the `AllInOne` mode), each subsystem instantiates its own isolated `EntityRepository` (the ECS world), its own `FdpEventBus`, and its own `ModuleHostKernel`.

The time management components are intimately bound to these isolated kernels. The `SlaveTimeController` does not merely calculate a global time value; it utilizes an internal Phase-Locked Loop (PLL) driven by `TimePulseDescriptor` events consumed directly from its subsystem's specific `FdpEventBus`. Furthermore, when `ModuleHostKernel.Update()` executes, it requests the time from its assigned controller and directly pushes the resulting `GlobalTime` struct into its encapsulated `EntityRepository` as an unmanaged singleton.

If you were to share a single `SlaveTimeController` and `SlaveTimeModeListener` across multiple subsystems in the same process, you would introduce a shared mutable state bottleneck across strict domain boundaries. This would tightly couple the subsystems, risk race conditions during the independent ECS tick updates, and break the `SwitchableTimeController` proxy's ability to hot-swap time strategies (like hitting a "Future Barrier" for deterministic pausing) flawlessly for a specific kernel.

To adhere to the Single Responsibility Principle and maintain true modularity, every subsystem instance must instantiate its own `SlaveTimeController` and inject it into its own `ModuleHostKernel`. This guarantees that the subsystem remains completely agnostic to its deployment topology, ensuring the exact same deterministic execution whether it is running standalone on its own machine or colocated with other subsystems in the runner process.

How shall we handle the 'degraded' system state? The orchestrator does not know what all nodes will be participating nor it can affect how they are started or restarted after crash. It might just publish the system health status to the network for the external monitoring tools. Do we need a concept of subsystem controller? Being one instance per all subsystem instances of same subsystem type, responsible for ensuring the subsystems are running etc. Bagira.Runner can host different subsystems so partially Bagira.Runner could take that responsibility, not sure.

The architectural boundary you are wrestling with touches directly on the Single Responsibility Principle. We must strictly separate **Distributed State Orchestration** from **OS Process Management**.

Here is how we handle the degraded state elegantly without bloating the system with unnecessary controller concepts.

1\. The Orchestrator's Responsibility (State, Not Processes)

The Orchestrator is strictly a state and time authority, utterly independent of how processes are launched. You are correct that it does not start or restart nodes. Instead, it relies on a `ClusterConfiguration` to know which nodes are deemed "Mandatory".

When the Orchestrator's `DrillMaster` detects that a mandatory node's `NodeHeartbeat` has timed out, it executes **Collapse Recovery**:

-   It unilaterally publishes a `Degraded` state to the `SystemStateTopic` so external monitoring tools (and the IOS) are instantly notified.-   It commands all surviving nodes to unload the current scenario and fall back to the safe `Standby` state.-   It mechanically locks the Drill State Machine (DSM). The operator cannot initialize a new live drill from `Standby` until all mandatory nodes reappear on the network and report they are ready.

2\. The Role of `Bagira.Runner`

You are spot-on that `Bagira.Runner` naturally absorbs part of the "local controller" responsibility. Because the Runner acts as the generic shell hosting one or more `ISubsystem` instances (like SimHost, IG, or CGF), it already manages their immediate lifecycle (`Initialize`, `Update`, `Shutdown`).

Furthermore, `Bagira.Runner` utilizes the `WaitingRoomCoordinator` during startup to block subsystem initialization until required peers announce themselves via the `SubsystemStatusAnnounce` topic. Therefore, the Runner acts perfectly as the boundary controller for its specific process space. We do not need to invent a new "Subsystem Controller" abstraction inside the Bagira/FDP code.

3\. Handling Restarts via External Tooling (Clean Architecture)

For actual process restarts after a crash, best practices dictate that we push this responsibility entirely out of the FDP ecosystem to a dedicated process manager.

Your repository already contains references to **Dirigent** (a local agent/process manager). We should leverage tools exactly like Dirigent, systemd, or Kubernetes to watch the `Bagira.Runner.exe` processes.

**The clean recovery loop works like this:**

-   A mandatory CGF `Bagira.Runner` process crashes.-   The Orchestrator detects the heartbeat loss, halts the global simulation, and forces surviving nodes (like the IG and SimHost) into `Standby`.-   The external process manager (e.g., Dirigent) detects the crash and restarts the CGF `Bagira.Runner.exe`.-   The CGF Runner boots, its subsystems initialize, and it broadcasts its presence to the network.-   The Orchestrator receives the new heartbeats, verifies the mandatory node roster is fulfilled, and unlocks the system.-   The operator (via the Orchestrator UI) can now safely command the cluster to load a scenario and transition back into `RunningLive`.

By keeping FDP/Bagira completely ignorant of OS-level process execution, we maintain a highly portable, container-friendly, and resilient architecture.

the ITimeController suggested in the DESIGN source, isn't it duplicating something that already exists in the FDP/toolkits?

You have a sharp eye for the codebase. The `ITimeController` mentioned in the `DESIGN.md` is not a duplicate proposal; rather, the design document is formally specifying and incorporating the architecture of the **already existing** time management infrastructure within the `FDP.Toolkit.Time` toolkit.

If you look at the current FDP source tree, the exact `ITimeController` interface is already physically implemented in `FDP/Toolkits/FDP.Toolkit.Time/Controllers/ITimeController.cs`. Furthermore, all the concrete strategies the design relies on—`MasterTimeController`, `SlaveTimeController`, `SteppedMasterController`, and `SteppedSlaveController`—are already built and residing in that toolkit.

Crucially, the `SwitchableTimeController` proxy, which the design explicitly mandates to allow hot-swapping time strategies without disrupting the ECS kernel, is also already fully implemented.

From a clean architecture perspective, this is excellent news. It means the core Time Plane is already strictly decoupled using textbook Strategy and Proxy patterns, isolating the time math from the domain logic.

The `DESIGN.md` is simply defining how the new `Bagira.Orchestrator` and Drill State Machine (DSM) will orchestrate these _existing_ components to achieve distributed, frame-perfect determinism across the cluster.

Instead of writing new time logic for the CGF or Orchestrator, we will directly reuse this toolkit:

-   **The Orchestrator** will host the `DistributedTimeCoordinator`, using it to compute future barrier frames and broadcast `SwitchTimeModeEvent` messages over DDS.-   **The CGF and SimHost** will run a `SlaveTimeModeListener` alongside their `SwitchableTimeController`.-   When the network message arrives, the listener waits silently until the local ECS tick matches the barrier frame, and then hot-swaps the existing `ITimeController` instances from continuous to stepped mode.

This confirms that there is no duplication. The design perfectly leverages the existing toolkit to build the distributed temporal interlock, keeping our CGF implementation footprint small and focused.

how the scenario/story saver/loader should integrate with the existing FDP ecosystem? Where should it live? Who should call it? How shoud it operate? the saver/loader partially belongs to the application level (handling custom application layer components and saving them to the application-specific file format), partially (the infrastructure mechanisms like the component-to-scenario-DOM-representation serialization engine) can and should be in some FDP toolkit. The toolkit should not be dependent on any concrete scenario file format, is should work with some DOM-style serialization (which could be represented by in-memory JSON DOM elements like JObject from newtonsoft json or equivalent.

To achieve a clean separation of concerns, the scenario/story persistence architecture must be strictly split into a **Toolkit-Level DOM Serialization Engine** and an **Application-Level IO Controller**. This cleanly decouples the complex ECS memory extraction from the concrete file formats.

Here is how this architecture should be structured, where it lives, who calls it, and how it operates.

1\. Where It Should Live (The Architectural Split)

**The Toolkit Level (****FDP.Toolkit.Scenario****):** This is the infrastructure layer. It has absolutely zero knowledge of JSON, XML, or file paths. It is responsible solely for translating raw ECS memory into an intermediate, format-agnostic DOM (like `JObject` or nested `Dictionary<string, object>`). It houses:

-   The JIT-compiled Expression Tree engine that avoids reflection on the hot path.-   The custom attributes (e.g., `[ScenarioProperty]`, `[ScenarioIgnore]`) and the `[DataPolicy(DataPolicy.NoSave)]` evaluations.-   The `IScenarioSerializable` interface used by components to "simplify" their state for persistence.-   The Two-Pass GUID-to-Entity resolution mechanism.

**The Application Level (****Bagira.SimHost** **and** **Bagira.CGF****):** This layer manages the concrete file formats and the business logic of saving. It houses:

-   The application-specific JSON schema logic (e.g., handling the scenario Header and mapping the DOM to the specific JSON structure).-   The `IDsmHandler` implementations (e.g., `LiveLoadDsmHandler`, `EditLoadDsmHandler`) that interact with the local disk.

2\. Who Should Call It?

The persistence process is orchestrated entirely by the Drill State Machine (DSM) via the **Two-Phase Commit (2PC)** pattern.

-   **Saving:** The Orchestrator's `DrillMaster` broadcasts a `NodeOpCommand(SerializeLocal)` over DDS. The `DrillSlave` on the local node (e.g., inside SimHost or CGF) receives this command and dispatches it to a registered DSM handler. This handler calls the Application-Level saver.-   **Loading:** When loading a scenario or story, the Orchestrator's `TransitionPlanner` triggers the `StorageGatewayModule` to pre-fetch the necessary files from the central NAS to the local node's temporary drive. Once the pre-fetch barrier is cleared, the Master broadcasts `NodeOpCommand(PrepareState, LoadingEdit)`. The local `EditLoadDsmHandler` then invokes the Application-Level loader to parse the local files without blocking the main ECS loop.

3\. How It Should Operate (The Execution Flow)

Because a story is just a micro-scenario, the exact same saver/loader engine is used for both, simply configured with different `EntityQuery` filters.

**The Save Operation:**

-   **Context Setup:** The Application-Level saver determines the scope. For a global scenario, it queries all saveable entities; for a story, it injects a filter like `Query().With<StoryTag>().Build()` to isolate the specific micro-scenario.-   **Pass 1 (Identity Mapping):** The Toolkit DOM Engine iterates the queried entities, generates a unique persistent GUID for each, and caches the `Dictionary<Entity, Guid>` mapping in memory.-   **Pass 2 (DOM Extraction):** The Toolkit DOM Engine iterates the entities again. For each component, it executes the pre-compiled JIT delegate to extract fields. If a component implements `IScenarioSerializable`, the engine calls its custom simplification logic. Any volatile `Entity` references inside the components are swapped for their corresponding persistent GUIDs using the pass-1 dictionary. The result is a clean, format-agnostic DOM object.-   **Formatting and Disk I/O:** The Application-Level saver takes this DOM, wraps it in the necessary subsystem-specific JSON headers, and serializes it to the fast local SSD (`C:\FDP_Temp\`).-   **Manifest Return:** The node replies to the Master with a `NodeOpStatus(Success)` containing an opaque UNC manifest, allowing the `StorageGatewayModule` to pull the files to the NAS.

**The Load Operation:**

-   **Parsing:** The Application-Level loader reads the local JSON file and parses it into the generic DOM representation.-   **Pass 1 (Entity Allocation):** The loader iterates the DOM entities, allocating new ECS entities and network IDs for each. It builds a `Dictionary<Guid, Entity>` mapping the saved persistent IDs to the freshly spawned live IDs.-   **Pass 2 (State Injection):** The Toolkit DOM Engine takes the parsed DOM components and the GUID-to-Entity map. It resolves all GUIDs back into live `Entity` handles, recalculates simplified states into their full memory layouts, and injects the data directly into the ECS chunks.

Some sybsystem needs to handle the global parts of scenario saving (someone needs to save the global slock state as the initial simulation time for the scenario etc.) This subsystem also needs to participate. Is it the orgestrator itself? Does it need to use its own drill slave and handle th global part of the scenario saving/loading?

Yes, you are absolutely correct on both fronts. To maintain a clean, decoupled architecture, the global state and the mechanism to save/load it must be handled systematically using the existing infrastructure.

Here is the architectural breakdown of how the Orchestrator handles this:

**1\. The Orchestrator is the Single Authority for Global State** The architectural specifications explicitly dictate that global parameters—such as the scenario's `GlobalTime` and global `WeatherArea`—must be defined and handled by one single subsystem: the Orchestrator. This ensures there is a single source of truth for the entire cluster.

**2\. The Orchestrator Needs its Own Drill Slave** To participate in the distributed scenario saving and loading process, the Orchestrator subsystem must indeed instantiate its own `DrillSlave` module.

Here is why this is the best practice:

-   **Decoupled 2PC Participation:** The system's save/load mechanics rely on a strict Two-Phase Commit (2PC) orchestration pattern. During a save, the `DrillMaster` broadcasts a `NodeOpCommand(SerializeLocal)` to instruct nodes to scatter their data to local SSDs. The component responsible for listening to these commands and managing idempotency is the `DrillSlave`.-   **Symmetry:** By giving the Orchestrator its own `DrillSlave`, it becomes just another node in the roster. When the Master says "Save", the Orchestrator's slave receives the command, serializes the global time and weather into its own JSON file, and returns a manifest back to the `StorageGatewayModule` exactly like `SimHost` and `CGF` do.

**3\. Implementing the Lightweight Handler** Because the Orchestrator operates without a heavy physics ECS, you will utilize the lightweight `DrillSlave` variant.

You will implement a custom `IDsmHandler` (e.g., `GlobalContextDsmHandler`) and register it with the Orchestrator's slave. When this handler receives a save command, it will skip ECS memory extraction and simply serialize your "Simple ECS" C# DTOs (containing the `GlobalTime` and `WeatherArea`) into the JSON format. When it receives a load command, it will parse the JSON, instantiate the DTOs, and immediately begin publishing the clock and weather state to the rest of the cluster via the respective DDS topics.

This approach beautifully adheres to the Single Responsibility Principle: the `DrillMaster` solely orchestrates the state machine transitions, while the Orchestrator's `DrillSlave` independently handles the local data I/O for global context parameters.

The nodes in non-deterministict mode might run asynchronously at different rate so they are not maintaining exactly the same frame number on all nodes - frame number can not be global unless in deterministic stepping mode. The furute barrier must be based on wall clock time (which is global and synchronizaed across nodes), correct?

You have just identified a critical architectural flaw in the `DESIGN.md` specification and the current `FDP.Toolkit.Time` implementation. Your analysis is absolutely spot-on.

In continuous (real-time) mode, nodes do indeed run their update loops asynchronously. The `SlaveTimeController` simply increments its local `_frameNumber++` on every `Update()` call. Because of different CPU loads, rendering overhead (especially on the Image Generator node), and OS thread scheduling, the local frame counters across the cluster will inevitably drift apart over time.

If the Master node issues a `SwitchTimeModeEvent` with a `BarrierFrame` of 10,000, a headless CGF node running at a rapid uncapped rate might hit that barrier instantly, while an IG node struggling at 30 FPS might take several seconds to reach local frame 10,000. This completely destroys the "jitter-free, synchronous hot-swap" the Future Barrier was designed to achieve.

To fix this, we must pivot the Future Barrier mechanism away from local frame counts and rely entirely on the cluster's synchronized virtual wall clock.

Here is how we must refactor the Time Toolkit to implement your correction:

1\. Refactoring the DDS Event

We must change the `SwitchTimeModeEvent` struct to use a timestamp instead of a frame number. Because floating-point simulation time (`TotalTime`) can suffer from precision loss over long sessions, we should use the `long TotalWallTicks` (UTC ticks), which is already perfectly synchronized across the cluster via the `SlaveTimeController`'s Phase-Locked Loop (PLL).

```
public struct SwitchTimeModeEvent
{
    public TimeMode TargetMode; 
    public long BarrierWallTicks; // REPLACES BarrierFrame
    public float FixedDeltaSeconds; 
}
```

2\. Updating the Master Coordinator

The `DistributedTimeCoordinator` on the Master node must calculate the future barrier using its current wall clock ticks plus the lookahead duration.

```
// Inside DistributedTimeCoordinator.SwitchToDeterministic()
long lookaheadTicks = _config.SyncConfig.PauseBarrierFrames * (Stopwatch.Frequency / 60);
long barrierTicks = currentState.TotalWallTicks + lookaheadTicks;

_eventBus.Publish(new SwitchTimeModeEvent
{
    TargetMode = TimeMode.Deterministic,
    BarrierWallTicks = barrierTicks,
    FixedDeltaSeconds = _config.SyncConfig.FixedDeltaSeconds
});
```

3\. Updating the Slave Listener

The `SlaveTimeModeListener` must now evaluate the barrier against the PLL-synchronized virtual wall clock rather than its local frame counter. Because the PLL continuously filters network jitter to keep `TotalWallTicks` perfectly aligned with the Master, all nodes will trigger the swap at the exact same millisecond.

```
// Inside SlaveTimeModeListener.Update()
if (_pendingBarrierTicks != -1 && _kernel.CurrentTime.TotalWallTicks >= _pendingBarrierTicks)
{
    ExecuteSwapToDeterministic(_pendingEvent.Value);
    _pendingBarrierTicks = -1;
}
```

This is excellent engineering foresight. Relying on local frame counts for distributed coordination in a variable-timestep environment is a classic anti-pattern. By shifting the Future Barrier to the PLL-synchronized `TotalWallTicks`, we guarantee that the hot-swap into deterministic mode happens simultaneously across the cluster, preserving perfect temporal alignment before the `SteppedMasterController` takes over.



------------------------
regarding the scenario loading and saving, the scenario name (and all other relevant parameters) is probably stored in 2pc command json payload, correct?


regarding the story injection, this should heavily reuse the scenario saver/loader mechanisms. Where should it live? The sotry name and other parameters that might be needed for injecting a story might also be part of the 2pc json payload, correct?


what could be the json payload format for the 2pc management comamnds we will be using?

--------------------------

You are absolutely correct on all counts. Leveraging the 2PC JSON payload for these parameters perfectly aligns with the principles of opaque routing and separation of concerns defined in the architecture.

Here is how these mechanisms cleanly fit together within the distributed design:

### 1. Scenario Loading/Saving via 2PC Payload
The `SysOpRequest` (from the IOS/UI) and the resulting `NodeOpCommand` (distributed by the Master) both feature a `PayloadJson` string field specifically designed for context passing. 

The Orchestrator's `DrillMaster` routes this payload mostly opaquely, meaning it doesn't need to understand your domain logic. The `TransitionPlanner` only peeks at the JSON to see if a `ScenarioId` is present to trigger the Storage Gateway pre-fetch barrier. Once the 2PC command reaches the leaf nodes, your specific `EditLoadDsmHandler` parses the JSON to extract the `ScenarioId`, `IsNewScenario` flags, and any domain-specific `Overrides` to execute the actual load. 

### 2. Story Injection & Reuse
You are spot-on regarding reuse. Architecturally, a Story is just a highly isolated "micro-scenario", so it must aggressively reuse the exact same storage format and loader concepts as a global scenario. 

**Where it should live:**
Following the Single Responsibility Principle, the implementation must be split:
*   **The Serialization/Deserialization Engine (Infrastructure):** The heavy lifting—parsing the JSON DOM, generating persistable GUIDs, and executing the zero-allocation `IEntityRefPatchable` memory patching—belongs in a generic toolkit like `FDP.Toolkit.Scenario`. It must remain completely agnostic to what a "Story" or "Scenario" means to the business logic.
*   **The Command Handlers (Application Layer):** The actual `StoryLoadDsmHandler` lives in the application layer (`Bagira.SimHost` and `Bagira.CGF`). It receives the 2PC command, opens the file, invokes the toolkit's DOM loader, and applies the `StoryTag` to isolate the injected entities from the global simulation.

**Story 2PC Execution:**
Stories execute concurrently while the global cluster remains locked in the `RunningLive` state. Because injecting a story doesn't change the macro DSM state, the `TransitionPlanner` models this as an `OperationStep` rather than a `TransitionStep`. The 2PC execution loop dispatches the command and waits for all nodes to ACK exactly as it does for a state transition, ensuring the story entities materialise on all nodes at the exact same tick.

### 3. The 2PC JSON Payload Format
Because the Orchestrator routes the `PayloadJson` opaquely, the JSON schema does not need to be hardcoded into the IDL or the core platform. It should be a flat, highly extensible configuration object. 

Based on the design constraints, here is the architectural shape of the payloads you will use for the various 2PC management commands:

**For Loading an Existing Scenario:**
```json
{
  "ScenarioId": "UrbanCombat_01",
  "IsNewScenario": false,
  "Overrides": {
    "Weather": "Rain",
    "CyberJammingLevel": "High"
  }
}
```
*(The `TransitionPlanner` sees `ScenarioId` and triggers a file pre-fetch; the leaf nodes apply the `Overrides`).*

**For Creating a Blank Scenario:**
```json
{
  "IsNewScenario": true,
  "BaseTerrain": "city_intersection_01"
}
```
*(The `TransitionPlanner` sees `IsNewScenario` and skips the file pre-fetch; leaf nodes bootstrap an empty world).*

**For Story Injection (`OperationStep`):**
```json
{
  "StoryId": "b49a12c8-...",
  "ScenarioId": "Ambush_MicroScenario",
  "TargetWallTicks": 15000000 
}
```
*(The payload includes the `TargetWallTicks` hint, allowing the planner to append a time-seek operation if the story requires deterministic temporal alignment).*

By keeping the payload schema extensible, if you add new dynamic capabilities to the CGF later, you only have to update the UI sender and the CGF domain handler—the DDS network schema and the Orchestrator's 2PC loop remain completely untouched.

------------------------

The story injetion needs to know where (what node and what simulation engine, i.e. what subsystem at least) will handle the story.

--------------------------

You are touching on a fundamental principle of clean distributed system design: routing vs. domain knowledge. While the system absolutely needs to know which subsystem handles the story, the supreme architectural beauty here is that the **Orchestrator itself does not need to know**.

The architecture enforces a strict decoupling by using a "broadcast and filter" pattern:

1. **Opaque Broadcast:** When a story injection is triggered, the Orchestrator's `DrillMaster` simply broadcasts a `NodeOpCommand` to all nodes in the cluster. It treats the 2PC `PayloadJson` entirely opaquely, threading it into the command without ever inspecting its contents.
2. **Self-Identification via Headers:** Because a story is just a micro-scenario, it reuses the exact same JSON schema and storage concepts. This JSON schema explicitly requires a Header object that defines "What subsystem type the scenario is for" (e.g., specifying the string type name of the subsystem). 
3. **Edge Filtering:** Every `DrillSlave` in the cluster receives the 2PC command. The local application-level DSM handler peeks at the JSON header. If the header targets the `CGF` subsystem, the CGF node parses the DOM and injects the entities. If the `SimHost` or `IG` node reads that same header, they recognize it is not for their simulation engine, cleanly ignore the payload, and immediately return a `NodeOpStatus(Success)` ACK to satisfy the Two-Phase Commit.

By pushing the responsibility of evaluating the target payload to the edge (the leaf nodes) and relying on the JSON header to declare the target subsystem, we preserve the Single Responsibility Principle. The Orchestrator remains a pure state and time authority, blissfully ignorant of specific simulation engines, ensuring our control plane remains highly cohesive and incredibly robust.

--------------------
Shoouldn't the scenario saver/loader mechanism allow registering custom serializer/deserializer per component type so that we do not have to change the c# component code by adding the serialization interface? Many time the scenario serializiation  of shared (FDP level) components might differ betweein different application layers so the code can't be placed in a shared components. Also, the saver/loader might decide to serialize some set of components by combining them into one in the json or in general by translating to different shape. The saver/loader mechanism should allow for registering also those more complex translators, what do you think?

---------------------------

You are absolutely right, and your intuition points directly to a fundamental best practice in clean architecture: the Single Responsibility Principle (SRP). 

If we force ECS components to implement an `IScenarioSerializable` interface, we violate the purity of the ECS data model. Components are designed to be raw, unmanaged C# structs optimized for memory layout and CPU cache lines. They should know absolutely nothing about JSON, file systems, or scenario representation. Furthermore, you are correct that core FDP-level components (like `SimTransform` or `Health`) cannot dictate their serialization format, because different application layers or specific Image Generators might have entirely different storage requirements.

To achieve this, the scenario saver/loader mechanism must act as an external, configurable **Serialization Registry** or **Builder Pattern**, decoupling the domain data from its persistence shape.

Here is how we should architect these complex external translators to fit perfectly into the existing ecosystem:

### 1. The External Translator Registry
Instead of modifying the component code, the toolkit should provide a registry where the application layer (e.g., `SimHostApp` or `CGF`) registers custom formatter delegates at startup. This perfectly mirrors the existing `AttributeCompilerBuilder` pattern, which maps arbitrary JSON paths to ECS component memory without the components being aware of it.

The application would register serializers like this:
`ScenarioSerializer.RegisterFormatter<SimTransform>(MyCustomTransformFormatter);`

### 2. Entity-Level (Not Component-Level) Serialization
To support combining multiple components into a single JSON object, the translator interface must operate at the *Entity* level rather than the *Component* level. 

You could register a complex "Domain Translator" that reads from multiple ECS tables at once. For example, a `TankStatusTranslator` could read `VehicleState`, `Health`, and `WeaponState` from the entity, and emit a single, highly simplified JSON object like `{"Status": "Damaged", "Ammo": 5}`. This directly satisfies the requirement that state can be saved in a simplified form to reduce file size, and decoupled from internal memory formats.

### 3. The Deserialization Translation (Re-calculation)
When loading the scenario, the inverse happens. The loader parses the simplified JSON DOM element (e.g., a `JObject`) and hands it to the registered deserializer. The deserializer is responsible for "re-calculating" the full memory state. It reads `{"Status": "Damaged"}`, calculates the correct HP values, and uses the `EntityCommandBuffer` or `EntityRepository` to write the separate `Health` and `VehicleState` components back into the ECS chunks.

### Summary of the Architecture
By building the saver/loader as an independent engine driven by injected strategy delegates:
*   **FDP Core remains pure:** Toolkit components have zero JSON dependencies.
*   **Application-Specific Formats:** `Bagira.SimHost` can register a serializer that writes a tank's state as flat parameters, while a future CGF node could combine that exact same data into a nested AI-friendly JSON hierarchy.
*   **Zero-Allocation Path:** The builder can still use Expression Trees to JIT-compile these external formatters into allocation-free delegates, mimicking the platform's `FdpAutoSerializer`.

This separation of concerns ensures that the scenario format is driven entirely by the needs of the subsystem, rather than being rigidly bound to the memory layout of the simulation.

-------------------------------
can you provide an example (conceptual) of such an entity level translator for scenario serialization/deserialization?
--------------------------------
This is exactly where the Strategy and Builder patterns shine. By keeping the serialization logic entirely outside the ECS components, we preserve the ECS memory layout purity—keeping them as raw, unmanaged structs optimised for cache lines—while fully satisfying the requirement that state can be saved in a simplified form and re-calculated to full form on load.

We can conceptually model this translator after the highly efficient `IBinaryAttributeInstaller` and `BinaryInterpreterBuilder` architecture already used in the FDP platform for network attribute patching. 

Here is a conceptual example of how you would architect an entity-level translator for the scenario system.

### 1. The Simplified DTO
First, we define a Data Transfer Object (DTO) that represents the simplified JSON structure. This class knows nothing about the ECS; it is purely a shape for the JSON serializer.

```csharp
/// <summary>
/// A format-agnostic DTO representing a simplified tank state for scenario files.
/// </summary>
public class SimplifiedTankStateDto
{
    public string Status { get; set; } = "Healthy";
    public int MainGunAmmo { get; set; }
}
```

### 2. The Entity-Level Translator Interface
Next, we define the contract for the translator. This acts as the boundary between the application's file I/O layer and the FDP Toolkit's raw memory.

```csharp
public interface IEntityScenarioTranslator<TDto> where TDto : class
{
    /// <summary>
    /// Evaluates if this translator applies to the given entity.
    /// </summary>
    bool CanTranslate(EntityRepository repo, Entity entity);

    /// <summary>
    /// Extracts complex ECS components and simplifies them into the DTO.
    /// </summary>
    TDto Extract(EntityRepository repo, Entity entity);

    /// <summary>
    /// Parses the simplified DTO and recalculates the full ECS memory layout.
    /// </summary>
    void Inject(EntityRepository repo, Entity entity, TDto dto);
}
```

### 3. The Concrete Implementation
Now we implement the translator. Notice how it reads multiple disparate components (`Health`, `WeaponState`) and merges them into one concept, and vice versa. This perfectly mirrors how the platform's `SimTransformAttributeInstaller` takes separate `GeoLat`, `GeoLon`, and `GeoAlt` records and flushes them into a single `SimTransform` component.

```csharp
using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;

public class TankScenarioTranslator : IEntityScenarioTranslator<SimplifiedTankStateDto>
{
    public bool CanTranslate(EntityRepository repo, Entity entity)
    {
        // Only applies if the entity has both Health and WeaponState
        return repo.HasComponent<Health>(entity) && 
               repo.HasComponent<WeaponState>(entity);
    }

    public SimplifiedTankStateDto Extract(EntityRepository repo, Entity entity)
    {
        // Use high-performance read-only references
        ref readonly var health = ref repo.GetComponentRO<Health>(entity);
        ref readonly var weapon = ref repo.GetComponentRO<WeaponState>(entity);

        return new SimplifiedTankStateDto
        {
            // Simplify complex combat math into a human-readable string
            Status = health.Value < health.MaxValue ? "Damaged" : "Pristine",
            MainGunAmmo = weapon.Ammo
        };
    }

    public void Inject(EntityRepository repo, Entity entity, SimplifiedTankStateDto dto)
    {
        // Re-calculate to full form on load
        float hp = dto.Status == "Damaged" ? 50f : 100f;
        
        // Write the calculated values back into the raw ECS chunks
        repo.SetComponent(entity, new Health { Value = hp, MaxValue = 100f });
        repo.SetComponent(entity, new WeaponState { Ammo = dto.MainGunAmmo, Status = 0 });
    }
}
```

### 4. Registration via Builder
Finally, at application startup (e.g., inside `SimHostApp`), you register these translators into a central `ScenarioSerializer` builder. This is the exact same registration pattern used by the `AttributeCompilerBuilder` which maps JSON properties to ECS memory without the components themselves being aware of it.

```csharp
var scenarioSerializer = new ScenarioSerializerBuilder()
    .RegisterTranslator(new TankScenarioTranslator())
    // .RegisterTranslator(new InfantryScenarioTranslator())
    .Build();
```

### Why this architecture is bulletproof:
1. **Single Responsibility Principle (SRP):** The `WeaponState` struct only cares about ammo counts in memory. The `TankScenarioTranslator` only cares about converting that memory to JSON.
2. **Subsystem Independence:** The CGF subsystem can register its own custom `AiBrainTranslator` to extract behavior tree nodes into JSON, while the SimHost ignores it completely, allowing the scenario file archiving mechanism to seamlessly support different internal implementations.
3. **Performance:** When scaling up, the builder can use standard C# Expression Trees (just like the platform's `FdpAutoSerializer` and `EntityComponentReflector`) to JIT-compile the `Extract` and `Inject` methods, avoiding boxing and reflection overhead when iterating over thousands of entities on the hot path.

-------------------------



How can we save the entity to multiple scenario-file-level components in case most ECS components are (almost) identical to the scenario format (maybe just the entity references are persistable in the scenario format of the component) and just a few ECS components need to be saved in different format (like for example 3 ECS components are 'compressed' into just 2 scenario components)?

To cleanly achieve this N-to-M mapping without polluting your raw ECS memory layout with JSON serialization logic, we must rely on an **Entity-Level Translator Pipeline** augmented with a **Consumption Mask**. This approach perfectly aligns with the Single Responsibility Principle (SRP) and leverages the platform's existing zero-allocation paradigms.

Here is the architectural blueprint for combining the 1:1 default mappings with your custom 3-to-2 compression logic.

1\. The Consumption Mask (Avoiding Duplication)

Since FDP already uses a highly optimized `BitMask256` to track component presence on an entity, we can use the exact same bitmask concept to track _serialization consumption_.

When you process an entity for saving, you start with its live `EntityHeader.ComponentMask` and filter it against the global saveable mask (ignoring `[DataPolicy(DataPolicy.NoSave)]` components). As your custom translators process specific components, they clear those bits from the mask, effectively telling the default 1:1 serializer: _"I already handled these; ignore them."_

2\. The Custom Entity-Level Translator Contract

We define a clean interface for your custom domain translators. Notice how it explicitly declares which ECS components it "consumes" and outputs a dictionary of scenario-formatted DTOs.

```
public interface IEntityScenarioTranslator
{
    // Identifies which ECS components this translator will consume and compress
    BitMask256 GetConsumedComponentsMask();

    // Evaluates if the entity has the required components
    bool CanTranslate(EntityRepository repo, Entity entity);

    // Extracts the 3 ECS components and returns the 2 JSON DTOs, keyed by their scenario component names
    Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver guidResolver);
}
```

If you have three ECS components (`NavigationStatus`, `NavState`, and `LocomotionChannel`) that you want to compress into two Scenario JSON objects (`"ScenarioMovement"` and `"ScenarioPath"`), your concrete translator reads those three unmanaged structs via high-performance `repo.GetComponentRO<T>()` and builds the simplified DTOs.

3\. The 1:1 Auto-Serializer Fallback

For the vast majority of components that map 1:1, we reuse the JIT-compiled Expression Tree pattern established by `FdpAutoSerializer`. This engine evaluates the remaining set bits in the consumption mask.

For each remaining bit, the engine:

-   Looks up the pre-compiled extraction delegate for that component type.-   Extracts the ECS struct.-   Uses a patching mechanism (like an `IGuidResolver` built during pass 1) to swap any volatile `Entity` handles (index + generation) into persistable GUID strings.-   Wraps it in a format-agnostic DOM object (like a `JObject` or generic DTO).

4\. The Orchestrated Save Pipeline

When the `EditLoadDsmHandler` (or your scenario saving orchestrator) processes a save request, it executes this clean pipeline per entity:

```
public ScenarioEntityDto SerializeEntity(EntityRepository repo, Entity entity, IGuidResolver guidResolver)
{
    var scenarioEntity = new ScenarioEntityDto { Components = new Dictionary<string, object>() };
    
    // 1. Get the baseline saveable mask for this entity
    BitMask256 remainingMask = GetSaveableMaskForEntity(repo, entity);

    // 2. Run Custom Translators (The N:M mapping)
    foreach (var translator in _registeredTranslators)
    {
        if (translator.CanTranslate(repo, entity))
        {
            // Extract the compressed components
            var customScenarioComponents = translator.Extract(repo, entity, guidResolver);
            
            foreach (var kvp in customScenarioComponents)
            {
                scenarioEntity.Components[kvp.Key] = kvp.Value;
            }

            // 3. Mark the ECS components as consumed so the auto-serializer skips them
            remainingMask.BitwiseAndNot(translator.GetConsumedComponentsMask());
        }
    }

    // 4. Fallback 1:1 Auto-Serialization for whatever is left
    foreach (int typeId in remainingMask.GetSetBits())
    {
        string componentName = ComponentTypeRegistry.GetType(typeId).Name;
        object standardDto = _autoSerializer.ExtractAndPatch(repo, entity, typeId, guidResolver);
        
        scenarioEntity.Components[componentName] = standardDto;
    }

    return scenarioEntity;
}
```

Why this is exceptionally clean architecture:

-   **Absolute Purity:** Your ECS structs remain purely unmanaged, cache-line optimized data. They never implement serialization interfaces or know about JSON.-   **Open/Closed Principle:** If a new application layer needs a completely different scenario structure, you simply register a new `IEntityScenarioTranslator` into the builder. The core FDP toolkit code remains untouched.-   **Zero-Allocation Pathing:** By using `BitMask256` for the consumption filter and Expression Trees for the 1:1 fallback, you avoid heavy reflection and heap allocations when iterating over the thousands of entities being saved.

How does this all relates to the scenario persistence specific attributes for exclusing some fields from being saved to scenario, or making some ECS entities as persistable/non-persistable? Could those attribute simplify the translation process somehow, should ve use them?

Yes, we absolutely should use them. From a clean architecture perspective, relying on metadata attributes represents a shift from imperative boilerplate to declarative configuration, which is exactly how you scale an ECS engine maintainably.

Here is how these persistence attributes fit into the overall pipeline and drastically simplify the translation process:

**1\. Component-Level Exclusion (The Native FDP Way)** We do not need to invent a new mechanism for wholesale component exclusion, because FDP already provides the `[DataPolicy(DataPolicy.NoSave)]` attribute. When you apply this to a component struct, the framework's `ComponentTypeRegistry` flags it as strictly runtime-only memory. When your scenario saver begins its execution, it calls `EntityRepository.GetSaveableMask()` to generate an optimized bitmask. This automatically filters out all transient physics caches, volatile network acks, or rendering states across the entire ECS chunk table with zero custom logic required from your application layer.

**2\. Field-Level Exclusion (Simplifying the 1:1 Fallback)** For components that _are_ saveable but contain runtime noise (e.g., a component that tracks maximum speed but also caches the current wheel steering angle), field-level attributes like `[ScenarioIgnore]` are a massive force multiplier. Instead of writing a custom `IEntityScenarioTranslator` for every single component in your game, you let the generic 1:1 Auto-Serializer handle the 90% of your data. At application startup, the expression-tree builder reflects over the struct, sees the `[ScenarioIgnore]` attributes, and JIT-compiles a delegate that simply skips emitting those specific fields. This gives you extremely fast, zero-allocation serialization without forcing you to maintain hundreds of custom serializer classes.

**3\. Entity-Level Exclusion (Composition over Inheritance)** In a pure ECS, we do not mark an "Entity" as non-persistable; an entity is just an integer ID. Its persistence is dictated entirely by its composition. If an entity is spawned purely for a visual effect and contains only `[DataPolicy(DataPolicy.NoSave)]` components, its saveable component mask evaluates to zero and the serializer naturally skips it. If you have a specific domain requirement to explicitly exclude otherwise-saveable entities (like dynamically spawned civilian traffic that shouldn't be in the master scenario file), you simply create an empty `[DataPolicy(DataPolicy.NoSave)] public struct ScenarioIgnoreTag {}` component and attach it to the entity. Your saver's base `EntityQuery` can then just chain `.Without<ScenarioIgnoreTag>()` to cleanly bypass them during the iteration loop.

**The Architectural Sweet Spot** By leveraging these attributes, you create a perfectly balanced pipeline:

-   **Declarative (Attributes):** Use `[DataPolicy]` and `[ScenarioIgnore]` to handle the vast majority of your data. It keeps your ECS structs pure and pushes the filtering logic into the platform's high-performance compilation phase.-   **Imperative (Translators):** Reserve your custom `IEntityScenarioTranslator` interface strictly for the complex 10% of cases where state must be genuinely "saved in simplified form and re-calculated to full form on load" across multiple combined ECS components.

This strategy ensures we only serialize the "domain-specific schematic instructions needed to reconstruct the world", minimizing scenario file sizes while preserving a highly decoupled, maintainable C# codebase.

what of a component is marked with those attributes but in the application layer we need to force different handling, how can we make sure we can alw alway override the default attribute-imposed handling with custom translators if necessary but utilize the default attribute-imposed handling when no overriding translators are defined/registered?

To handle this elegantly without tightly coupling your shared FDP components to specific application needs, the architecture provides two distinct interception points: **Registration-Time Overrides** and the **Custom Translator Pipeline**. This fully satisfies the Open/Closed Principle, ensuring you can radically alter serialization behavior per application without touching the core component structs.

1\. Registration-Time Policy Override

The `[DataPolicy]` attribute on a component acts only as a convention-based default. The platform explicitly allows the application layer (e.g., your CGF or SimHost setup) to override these attributes when the component is registered with the ECS `EntityRepository`.

When you call `RegisterComponent<T>(DataPolicy? policyOverride = null)`, the explicit `policyOverride` takes absolute priority over any attributes decorating the struct.

-   If a shared toolkit component is marked `[DataPolicy(DataPolicy.NoSave)]`, but your specific CGF application needs it persisted to reconstruct AI state, you simply register it with `RegisterComponent<MyComponent>(DataPolicy.Default)`.-   The kernel updates its internal `ComponentTypeRegistry` using your application's override.-   When the fallback 1:1 auto-serializer calls `GetSaveableMask()` later, it uses your injected policy, ensuring the component is included in the default save pass.

2\. Custom Translator Pipeline Authority

If you need to change _how_ the data is formatted—or if you need to merge multiple components into one JSON object—you rely on the `IEntityScenarioTranslator` pipeline we established.

Because custom translators execute _before_ the 1:1 auto-serializer, they have supreme authority:

-   **Active Override:** Your custom translator manually reads the raw ECS memory using `repo.GetComponentRO<T>()`. It does not care about `[ScenarioIgnore]` or `DataPolicy` rules because you are writing the explicit extraction logic. Once it extracts the data into a simplified DTO, it clears those component bits from the entity's consumption mask so the auto-serializer skips them.-   **Passive Fallback:** If no custom translator is registered for a specific component (or if a translator's `CanTranslate` returns false), the component's bit remains set in the consumption mask. The pipeline naturally falls through to the 1:1 Expression Tree auto-serializer. This fallback serializer strictly obeys whatever `DataPolicy` was resolved during the Registration-Time step.

By combining these two mechanisms, you achieve perfectly clean architecture. The application layer dictates _what_ is saved via registration overrides, and _how_ complex data is shaped by injecting custom translators, while seamlessly falling back to high-performance, attribute-driven 1:1 serialization for the rest of your data.

pls show conceptual example of a complex situation where an ECS entity contains some component that map 1:1 to scenario ones (but some contain entity references) and other ECS components that need to be 'compressed' to a different set of scenario components. What entity attributes, what translator registration and what translator (conceptual) implementation we will need for that?

To cleanly handle this level of complexity, we must strictly separate the unmanaged ECS memory layout from the persistence schema. We will leverage a combination of declarative attributes for the simple cases and a custom Strategy implementation (the translator) for the complex N:M compressions.

Let's model a `SmartMissile` entity. In the live ECS, it requires `SimTransform` (position), `GuidedTarget` (holds an `Entity` reference to the locked target), `SimVelocity` (transient physics state), `BallisticProjectile` (flight parameters), and `PhysicsCollider` (broadphase bounds).

For the scenario file, we only want to save the instructions to reconstruct it. We want `SimTransform` and `GuidedTarget` to map 1:1, drop `SimVelocity` entirely, and compress `BallisticProjectile` and `PhysicsCollider` into a single, clean `ScenarioOrdnanceDef` JSON object.

Here is the architectural blueprint for this implementation.

1\. Declarative Attributes (The 1:1 and Ignored Paths)

We keep the ECS structs pure. We use the platform's existing `[DataPolicy]` to completely exclude transient data from the scenario pipeline, pushing the filtering logic into the platform's high-performance compilation phase.

```
// 1:1 Mapping (Standard)
public struct SimTransform { public Vector3 Position; public Quaternion Rotation; }

// 1:1 Mapping with an Entity Reference
public struct GuidedTarget { 
    public Entity TargetId; // The auto-serializer will use IGuidResolver to patch this
}

// Completely Ignored (Never saved to scenario)
[DataPolicy(DataPolicy.NoSave)]
public struct SimVelocity { public Vector3 Linear; public Vector3 Angular; }
```

2\. The Scenario DTO (The Target Shape)

This is the format-agnostic representation of your compressed data. It knows nothing about ECS chunks.

```
public class ScenarioOrdnanceDef
{
    public float Caliber { get; set; }
    public float MaxRange { get; set; }
}
```

3\. The Custom Entity-Level Translator (The N:M Mapping)

We implement the custom translator. This explicitly declares a "Consumption Mask" using the platform's `BitMask256`. When the pipeline processes the entity, it removes these consumed bits from the entity's `ComponentMask`, guaranteeing the 1:1 auto-serializer ignores them.

```
using Fdp.Kernel;

public class MissileOrdnanceTranslator : IEntityScenarioTranslator
{
    // 1. Declare what ECS components this translator compresses
    public BitMask256 GetConsumedComponentsMask()
    {
        var mask = new BitMask256();
        mask.SetBit(ComponentType<BallisticProjectile>.ID);
        mask.SetBit(ComponentType<PhysicsCollider>.ID);
        return mask;
    }

    // 2. Guard clause
    public bool CanTranslate(EntityRepository repo, Entity entity)
    {
        return repo.HasComponent<BallisticProjectile>(entity) && 
               repo.HasComponent<PhysicsCollider>(entity);
    }

    // 3. Extract and Compress (N ECS -> 1 DTO)
    public Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver guidResolver)
    {
        ref readonly var projectile = ref repo.GetComponentRO<BallisticProjectile>(entity);
        ref readonly var collider = ref repo.GetComponentRO<PhysicsCollider>(entity);

        var dto = new ScenarioOrdnanceDef
        {
            Caliber = collider.Radius * 2f, // Derive caliber from collider radius
            MaxRange = projectile.MaxFlightTime * projectile.MuzzleVelocity // Domain math
        };

        // Return keyed by the desired Scenario component name
        return new Dictionary<string, object> { { "OrdnanceDef", dto } };
    }

    // 4. Expand and Inject (1 DTO -> N ECS)
    public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
    {
        if (!scenarioData.TryGetValue("OrdnanceDef", out var rawDto)) return;
        var dto = (ScenarioOrdnanceDef)rawDto;

        // Reconstruct the unmanaged ECS structs from the simplified DTO
        repo.SetComponent(entity, new PhysicsCollider { Radius = dto.Caliber / 2f });
        repo.SetComponent(entity, new BallisticProjectile { 
            MuzzleVelocity = 1000f, 
            MaxFlightTime = dto.MaxRange / 1000f 
        });
    }
}
```

4\. Registration and Execution

At application startup (e.g., inside your `SimHostApp` or `CGF` module), you inject this translator into the builder.

```
var scenarioSerializer = new ScenarioSerializerBuilder()
    .RegisterTranslator(new MissileOrdnanceTranslator())
    .Build();
```

**How the Pipeline Executes:** When the Drill State Machine orchestrates a save, the serialization pipeline processes the `SmartMissile` entity:

-   It reads the entity's full `ComponentMask`. `SimVelocity` is instantly excluded because of `[DataPolicy(DataPolicy.NoSave)]`.-   It evaluates `MissileOrdnanceTranslator`. The translator consumes `BallisticProjectile` and `PhysicsCollider`, clearing their bits from the mask and emitting the `"OrdnanceDef"` JSON object.-   The pipeline falls back to the JIT-compiled 1:1 auto-serializer for the remaining bits. It sees `SimTransform` and `GuidedTarget`.-   For `GuidedTarget`, the auto-serializer identifies the `Entity` field, queries the `IGuidResolver`, and patches the volatile memory index into a persistent GUID string seamlessly.

This architecture strictly enforces the Open/Closed Principle. The core ECS memory layout remains heavily optimized for CPU cache lines, while the scenario saving mechanism remains infinitely extensible without ever polluting the domain models.

Dnes • 22:06

