
----
What is the optimal way of saving the blueprint-to-entity assignment to scenario? I need  2 ways, one static (blueprint assigned from the beginning), other dynamic (starting blueprint as a behavior, and just assigning blueprint to entity via fdp event, that can be invoked from behavioral action for example). Can we assign multiple blueprints to the same entity?
----
Yes, you can absolutely assign multiple blueprints to the same entity, but the architectural limits and assignment methods differ entirely based on the blueprint's **dispatch kind** (`Instance` vs. `AiPrimitive`). 

Here is the exact architectural breakdown of how to optimally handle static assignments, dynamic assignments, and multi-blueprint stacking.

### 1. Multiple Blueprints on the Same Entity
*   **Instance Blueprints:** Yes, you can attach multiple Instance Blueprints to a single entity. Their state is stored in the `BlueprintBlackboard1024`, `BlueprintBlackboard4096`, or `BlueprintBlackboard16384` components. These components use a partition allocator with a slot table to host multiple distinct blueprints concurrently on the same entity. They execute sequentially in the order they were attached during the `BlueprintTickSystem` simulation phase.
*   **AiPrimitive Blueprints (Behaviors):** No, there is a strict Slice 1 architectural constraint that permits only **one** active AiPrimitive working-state blueprint per entity at a time. AiPrimitives hosted as BTree or HSM actions project their working state directly over the engine's single `Blackboard1024` component. 

### 2. Static Assignment (Assigned from the Beginning)
When authoring a scenario, the optimal way to statically assign a blueprint to an entity depends on its dispatch type:

*   **For AiPrimitive Blueprints (Behaviors):** 
    The optimal static assignment is achieved by configuring the entity's Transient Knowledge Base (TKB) template. You attach a `BehaviorProfileDto` to the TKB template and set its `DefaultBehaviorHash` property to the hash of your blueprint. When the scenario loads and the entity is materialized via the genesis pipeline, the `BehaviorTkbTranslator` automatically initializes the `BehaviorState` ECS component with this active behavior hash. This guarantees the cognitive tier starts executing the behavior on the very first frame.
*   **For Instance Blueprints:** 
    Instance Blueprints are natively serialized by the engine's scenario serializer. Because their state resides in the unmanaged `BlueprintBlackboard*` components, the exact bytes—including the partition slot table which holds the `BlueprintId`s—are saved automatically. To assign them statically, you simply attach the blueprint to the entity in the editor or staging world; when the scenario is saved and later loaded, the exact assignment and its default payload state are perfectly reconstituted from the JSON file.

### 3. Dynamic Assignment (Assigned via FDP Event)
To dynamically invoke or assign a blueprint while the simulation is running (e.g., from an action node or a tactical directive), you use the engine's event bus and integration services.

*   **For AiPrimitive Blueprints (Behaviors):**
    The intended architectural mechanism is to publish an `AssignBehaviorEvent` (a managed class event) or an `AssignBehaviorHashEvent` (an unmanaged struct event) to the `FdpEventBus`. 
    *   **How it works:** Your behavioral action or mission director pushes this event containing the target entity and the new behavior name/hash.
    *   **Execution:** The `BehaviorIngressSystem` consumes this event in the `Input` phase. It is the sole owner of `BehaviorState` mutations. It parses any accompanying JSON parameters using a zero-allocation stackalloc shadow copy, sets the new `ActiveBehaviorHash`, increments the `InstanceId` to preempt stale channel actions, and resets the underlying BTree or HSM execution pointer.
*   **For Instance Blueprints:**
    Instance blueprints are attached dynamically by invoking `BlueprintAttachService.AttachToEntity`. 
    *   **How it works:** This service is run-mode-agnostic and fully idempotent. When invoked, it identifies the smallest required blackboard tier component (1024, 4096, or 16384), ensures it exists on the entity, and uses the partition allocator to claim a free slot. 
    *   **Execution:** It then writes the `BlueprintId` into the slot table and invokes the blueprint's generated `InitDefault` delegate to initialize the payload. The `BlueprintTickSystem` will automatically detect the newly populated slot on the next simulation frame and begin ticking it. While there is no built-in generic "AssignInstanceBlueprintEvent" like there is for behaviors, any custom event or dispatcher action can easily wrap `BlueprintAttachService.AttachToEntity` to apply it dynamically.
---
regarding saving blueprint assignment to scenario, do you recommend saving the current state of the blackboards into the scenario? Blackboards are usually something volatile, OK for checkpoint/recording snapshot, but not exactly a good fit for the scenario file.
---
You are absolutely correct. Storing volatile runtime execution state in a scenario file is a fundamental anti-pattern. Scenario JSON files are declarative authoring templates meant to define the initial conditions of the world, whereas binary checkpoints and the Flight Recorder are the correct mechanisms for capturing mid-execution state snapshots.

Here is the architectural evaluation of why saving live blackboard state into scenarios is a bad fit, and how the engine is specifically designed to handle this separation.

### 1. The `DataPolicy.NoSave` Paradigm
The engine explicitly differentiates between declarative scenario data and volatile runtime data using the `DataPolicy` attributes. The `DataPolicy.NoSave` flag is explicitly designed to exclude components from Scenario JSON serialization. Its documented purpose is to omit "runtime execution state" from scenario files while allowing that exact same state to be safely captured by binary checkpoints and recording pipelines. 

### 2. How AiPrimitive Blackboards Handle This
The architecture for `AiPrimitive` blueprints and standard behaviors already strictly enforces your recommendation. Both the `BrainBlackboard` (inline parameters) and `Blackboard1024` (heavy working memory) components are decorated with `[DataPolicy(DataPolicy.NoSave)]`. 

Furthermore, their scenario integration pipelines (`BrainBlackboardTranslator` and `Blackboard1024Translator`) are explicitly designed so that their `Inject` (load) operations are complete no-ops. The design documentation for these translators notes that this memory is "transient execution state and must never be written back from a scenario file". They only provide an `Extract` (save) implementation to allow developers to dump readable JSON to the clipboard during active debugging sessions. 

At scenario load time, instead of deserializing old memory, the entity is assigned its behavior dynamically (e.g., via `BehaviorIngressSystem` handling an `AssignBehaviorEvent`). The system parses authored parameters, cleanly applies any editor-authored static defaults, and performs a fresh allocation.

### 3. Correcting the Instance Blueprint Strategy
In the Slice 1 Blueprint Architecture specifications, it was noted that `BlueprintBlackboard*` states would simply "save automatically via the scenario serializer". However, applying your observation to Instance Blueprints is the correct architectural move going forward. 

Treating `BlueprintBlackboard1024`, `BlueprintBlackboard4096`, and `BlueprintBlackboard16384` as standard scenario-saved components risks bloating the declarative scenario files with latent cursors, internal tick counters, and mid-execution phases. 

**The Recommended Approach for Instance Blueprints:**
Instead of serializing the blackboard components themselves into the scenario, you should persist the *intent* to attach the blueprint (e.g., a lightweight configuration DTO holding the `BlueprintId` and any explicitly overridden initial variable values). 

When the scenario loads:
1. The genesis materialization pipeline reads the intent.
2. It invokes `BlueprintAttachService.AttachToEntity`.
3. The partition allocator guarantees a fresh, zeroed slot in the blackboard tier.
4. The runtime invokes the generated `InitDefault` delegate to cleanly initialize the variables to their baseline authored defaults.

By doing this, you keep the scenario JSON clean and guarantee that entities always boot up in a deterministic, uncorrupted initial state, while allowing the Flight Recorder and checkpoints to continue seamlessly capturing the live bytes.
---
Could this DTO hold an array of blueprints as we can have more than one per entity?
To what part of the scenario file you suggest to save  this intent? scenario basicaly serializes ECS components, so some new component dedicated for blueprint instantiation intents? Could this be unified, like the same way re-used for assigning blueprints?
---
Yes, the DTO should absolutely hold an array or list of blueprint assignments to support multiple Instance Blueprints per entity. 

To cleanly integrate this into the scenario file, you should follow the engine's established **Transient Intent Component + Custom Translator** pattern. Scenarios do strictly serialize ECS components, and the engine already uses dedicated "intent" components to bridge declarative scenario JSON into live ECS state during scenario genesis.

Here is how you can unify this seamlessly:

### 1. Create a Transient Intent Component
You should create a new managed component (e.g., `InitialBlueprintsIntent`) to hold the array of blueprint assignments. Following the engine's standard practice for genesis intents like `InitialPassengersIntent` and `InitialUnitSubordinateIntent`, this component must be decorated with `[DataPolicy(DataPolicy.Transient)]` so that it acts as a temporary boot-up instruction rather than persistent runtime state.

```csharp
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialBlueprintsIntent)]
public sealed class InitialBlueprintsIntent
{
    // Holds the array of blueprints to attach, plus any overridden variables
    public List<BlueprintAssignmentDto> Blueprints { get; set; } = new();
}
```

### 2. Use an `IEntityScenarioTranslator` for the Scenario File
In the scenario JSON, this data will simply reside inside the entity's component block. To get it there, you create an `IEntityScenarioTranslator` (e.g., `BlueprintStateTranslator`). 

*   **On Save (`Extract`):** The translator's `Extract` method scans the entity's `BlueprintBlackboard1024`, `4096`, and `16384` components. It walks the partition slot table to find which blueprints are currently attached, creates an array of `BlueprintAssignmentDto`s, and serializes them into the scenario JSON. 
*   **On Load (`Inject`):** When the scenario file is loaded, the translator's `Inject` method parses the JSON array and attaches the `InitialBlueprintsIntent` managed component to the entity.

### 3. Unified Materialization
The actual blueprint assignment is unified at the genesis pipeline. The `GenesisMaterializationSystem` (which runs in the `Input` phase) or a dedicated `BlueprintMaterializationSystem` queries for all entities carrying the `InitialBlueprintsIntent`. 

For every blueprint listed in the intent's array, the system invokes `BlueprintAttachService.AttachToEntity`. Once all blueprints in the array are attached, the system removes the transient intent component from the entity.

This perfectly unifies your two assignment pathways. Whether a blueprint is statically authored in the scenario file or dynamically invoked via a runtime FDP event, both paths ultimately funnel through `BlueprintAttachService.AttachToEntity`. This service handles finding the correct blackboard tier, claiming a partition slot, and invoking the generated `InitDefault` delegate idempotently.
---
what if we want to remove or replace the blueprint in the middle of runtime? Should this be also expressed as an intent?
---
No, you should not use a transient intent component for mid-runtime changes. The engine explicitly separates declarative scenario-boot instructions (like the `InitialBlueprintsIntent` we discussed, which is consumed by the genesis pipeline and then destroyed) from live runtime commands. 

The correct architectural mechanism for removing or replacing a blueprint mid-simulation depends entirely on its dispatch kind:

### 1. Removing or Replacing AiPrimitive Blueprints (Behaviors)
For behaviors, the runtime strictly manages the single active state through the event bus. You do not need a new component intent; you use the existing event signals:
*   **To Replace:** You simply publish a new `AssignBehaviorEvent` (or `AssignTacticalIntentEvent`) to the event bus. The `BehaviorIngressSystem` intercepts this during the `Input` phase, safely overwrites the old behavior, and automatically resets the BTree or HSM execution pointers so the new behavior starts fresh.
*   **To Remove:** If you want to strip the behavior entirely without replacing it, you publish a `ClearBehaviorEvent` to the event bus. The engine will consume this event, clear the active behavior hash, and halt execution.

### 2. Removing or Replacing Instance Blueprints
Because Instance Blueprints support multiple concurrent scripts per entity via the partition allocator, they are added and removed surgically from the blackboard tier components rather than broadly overwritten:
*   **To Remove:** The engine provides the `BlueprintBlackboardPartitions.TryDetach` helper method. When you pass it the target `BlueprintId`, it walks the slot table, finds the exact memory slice the blueprint occupies, releases the space back to the free list, coalesces any adjacent free memory, and dense-compacts the slot table. 
*   **To Replace:** You perform a detach of the old blueprint followed by an attach of the new one using `BlueprintAttachService.AttachToEntity`.

**How to trigger this at runtime:**
Instead of a scenario intent component, the standard engine pattern for mid-runtime Instance Blueprint detachment is to define a custom runtime event (e.g., `RemoveInstanceBlueprintEvent`) and a lightweight system that consumes it, or to invoke the detach service directly from a C# action node within another executing blueprint. This ensures the structural memory mutation is safely coordinated with the engine's tick cycle.
---
so removal is via events, but instantiation is via intent component? How to synchronize these so that removal happens BEFORE new intent-based instantiation?design-talk.md
---
There is a slight misunderstanding in your premise. You do **not** use the intent component for mid-runtime instantiation. 

The architecture strictly separates the declarative scenario boot-up phase from live, mid-simulation mutations.

### 1. The Scenario Load Pathway (Genesis Only)
The `InitialBlueprintsIntent` (along with the engine's existing `InitialPassengersIntent`, `InitialTargetsIntent`, etc.) is **exclusively for scenario deserialization**. 

When a scenario file is loaded, the JSON translators inject this transient component onto the freshly materializing entity. The `GenesisMaterializationSystem` then consumes it to attach the blueprints. Because the entity is being newly constructed from the save file, there are no existing blueprints to remove, meaning synchronization with removal is never a concern here.

### 2. The Mid-Runtime Pathway (Live Simulation)
During live gameplay, you never attach an `InitialBlueprintsIntent` to an entity. Mid-runtime changes—both removal **and** instantiation—are driven entirely by your runtime systems, event bus payloads, or direct C# action node calls.

If you need to swap, remove, or attach a new instance blueprint dynamically based on game logic, you synchronize them by performing the operations sequentially within the same event handler or system tick.

### How to Synchronize a Mid-Runtime Replacement
If you publish a custom runtime event (e.g., `ReplaceInstanceBlueprintEvent`), the system consuming that event simply executes the detach and attach calls consecutively on the same thread:

1. **Detach First:** You call `BlueprintBlackboardPartitions.TryDetach(memory, oldBlueprintId)`. This synchronously finds the memory slice, releases the space to the free list, and dense-compacts the slot table.
2. **Attach Second:** You immediately invoke `BlueprintAttachService.AttachToEntity(world, registry, newAsset, entity)`. This synchronously claims a slot (reusing the freshly freed memory) and invokes `InitDefault`.

Because both operations are synchronous memory manipulations running inside your system's `Execute` method (typically in the `Simulation` or `Input` phase), the synchronization is architecturally guaranteed. Detaching first ensures that the entity's `BlueprintBlackboard*` memory is properly coalesced, preventing the new attachment from needlessly forcing a capacity failure and triggering a tier upgrade to a larger component.

