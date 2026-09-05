how do we ensure the blueprint intent component is present on the entity? some tkb record or something?
---
The presence of the `InitialBlueprintsIntent` component on the entity is **not** driven by a TKB record. In our architecture, TKB descriptors (specifically `BehaviorProfileDto`) are exclusively used for statically assigning `AiPrimitive` behaviors. 

For Instance Blueprints, the intent component is instantiated and attached entirely by the scenario deserialization pipeline via a custom JSON translator.

Here is the exact mechanism of how the intent component arrives on the entity:

1. **Scenario JSON DOM:** The entity's block in the scenario JSON file contains a custom array key (e.g., `"BlueprintAssignments"`) holding the serialized `BlueprintAssignmentDto` data.
2. **The Translator's `Inject` Method:** During scenario load, the engine's `ScenarioSerializer` parses the JSON. Because your `BlueprintStateTranslator` explicitly claims the `"BlueprintAssignments"` key in its `GetOutputDomKeys()` method, the serializer routes that specific JSON payload to your translator instead of the auto-serializer.
3. **Component Attachment:** Inside the `Inject` method, your translator parses the JSON array, instantiates a new `InitialBlueprintsIntent` managed component, and attaches it directly to the entity using `repo.SetManagedComponent(entity, intent)`.

To ensure this injection process does not throw an unregistered component exception during scenario load, the engine must be made aware of the intent type at boot. This is why the design mandates calling `world.RegisterManagedComponent<InitialBlueprintsIntent>()` inside the engine's `GenesisIntentRegistry.RegisterAll` bootstrap method.

Once the translator successfully attaches the intent, the `BlueprintMaterializationSystem` takes over during the `Input` phase of genesis. It queries for the intent, pre-provisions the correct blackboard tier, invokes the core attach seam for each blueprint, and then permanently deletes the intent component.
---
during the editing process (before first time saved to a scenario) we need to instantiate the component in another way, and allow specifying (in the editor) what blueprints will be part of it. Scenario loading can not be the only way. During editing, the scenarion might not be even be loaded/saved at all (all authored just in memory) and yet the blueprint assignemnt must be working - for example for the preview run.
---
You are absolutely right to point this out. Scenario loading is only one entry point, and the `InitialBlueprintsIntent` component is strictly a deserialization vehicle for the genesis pipeline. You do **not** use the intent component during live editor authoring. 

Here is how the architecture naturally handles editor-time assignment, preview runs, and eventual saving without relying on scenario load intents:

### 1. Direct Attachment in the Editor
During the editing process, when a designer wants to assign a blueprint to an entity, the editor bypasses the intent component entirely and directly invokes the core `BlueprintAttachService.AttachToEntity` seam. 

The architecture already provisions the UX for this via the **"Run Blueprint on Selected Entity"** toolbar button (`RunBlueprintOnEntityCommand`). When the designer selects an entity in the map and clicks this button, the editor commands the attach service to:
1. Ensure the correct `BlueprintBlackboard*` unmanaged component is present on the entity.
2. Carve out a slot in the partition table.
3. Run the generated `InitDefault` delegate to establish the baseline parameters.

From that moment, the blueprint is physically attached to the entity's live ECS memory, even though the simulation is paused in authoring mode.

### 2. Seamless Preview Runs (No JSON Required)
When the designer clicks "Preview" to test the scenario, the editor invokes `EditorPreviewAdapter.EnterPreviewMode()`. 

The preview transition does not serialize to JSON or use intents. Instead, the `PreviewClusterOpHandler` performs an instantaneous **in-memory ECS snapshot** (a fast byte-copy of the ECS chunks) to create the dry-run world. 

Because the `BlueprintBlackboard1024`, `4096`, and `16384` components are purely unmanaged and blittable, the exact slot tables, blueprint IDs, and initialized parameter states are perfectly copied into the preview snapshot. As soon as the time controller unpauses, the `BlueprintTickSystem` simply iterates those existing slots and begins execution. 

### 3. Extracting to Scenario on Save
When the designer eventually decides to save the authored world to a scenario file, the pipeline works in reverse. 

The `BlueprintStateTranslator.Extract` method is invoked by the `ScenarioSerializer`. It looks at the live `BlueprintBlackboard*` components on the entity, reads the dense slot table, resolves the active `BlueprintId`s back to their stable `AssetId` GUIDs, and writes them into the scenario JSON as an array of declarative `BlueprintAssignmentDto`s. 

### Summary
The live, unmanaged `BlueprintBlackboard*` component serves as the true source of truth during authoring. 
* **Editor UI** adds to it directly via `BlueprintAttachService`.
* **Preview Mode** copies it directly via binary ECS snapshots.
* **Save/Extract** reads it to generate the JSON intent declarations for future loads.
---
wouldn't it be safer to avoid depending on the current blackboard content during editing? And rather edit the intents? And let the blueprint system to find the intent and instantiate blueprint? It's similar to how other intents work. I.e. no reverse engineering of blackboard, but providing UI for editing just the intents?
---
Your proposal contradicts the engine's established Transient Intent pattern and would break the live WYSIWYG authoring loop.

First, intent components such as `InitialBlueprintsIntent` are explicitly decorated with the `[DataPolicy(DataPolicy.Transient)]` attribute. They are strictly scenario boot-up instructions designed to be consumed and permanently destroyed by the materialization pipeline (such as `GenesisMaterializationSystem` or `BlueprintMaterializationSystem`) prior to the first simulation frame. They must never linger on a live entity during interactive authoring or gameplay.

Second, extracting data from the live component during the save cycle is not a workaround to be avoided; it is the engine's canonical serialization architecture for decoupling highly-optimized runtime memory from declarative scenario files. Custom `IEntityScenarioTranslator` implementations are explicitly designed to perform this extraction. For example, the `VisHierarchyNodeTranslator` reads the live `VisHierarchyNode` component to extract the `InitialHierarchyIntent` on save, and the `PassengerBufferTranslator` similarly extracts the `InitialPassengersIntent` from live vehicle passenger buffers. The `BlueprintStateTranslator` follows this exact established pattern: it walks the dense slot table in the live, unmanaged `BlueprintBlackboard*` components to emit a clean `BlueprintAssignmentDto` array into the scenario JSON.

Third, forcing the editor to author intent components instead of live state would cripple the editor's live debugging and preview capabilities. The FDP editor's property inspectors and `StructEdit` drawers are built to operate directly on live ECS components within the active entity repository. When the editor assigns a blueprint via the "Run Blueprint on Selected Entity" command, it directly invokes the core `BlueprintAttachService.AttachToEntity` seam. This immediately populates the live `BlueprintBlackboard*` unmanaged component, carving out a memory slot and running the generated `InitDefault` delegate. 

This direct attachment guarantees that the editor's Instance Inspector, Debug Panel, and Watch Panel can immediately observe and mutate the exact same live memory layout that the runtime `BlueprintTickSystem` will execute. If the editor merely authored a transient intent component, the blueprint's state would not be physically mounted in memory, rendering all live-preview, step-debugging, and memory inspection features completely inoperable until a full scenario materialization cycle was artificially forced.

---
