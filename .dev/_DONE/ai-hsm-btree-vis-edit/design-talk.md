
How does a behavior uses the blackoard? What blackoard types do we support?

Behaviors use the blackboard as a shared memory space to hold their configuration parameters and mutable runtime execution state. The behavior tree executor uses this state to drive decision-making, shifting through decision nodes purely based on blackboard state writes (for example, reacting to updates in `ThreatVisible` or `AmmoCount`).

Specifically, behaviors interact with the blackboard in the following ways:

-   **Initialization via Parsing:** When a behavior is assigned to an entity, its initial parameters (often in JSON format) are parsed by a `ParseParams` delegate and written directly into the blackboard's byte memory.-   **Zero-Allocation Typed Access:** At runtime, BTree and HSM action/condition delegates are passed a reference to the blackboard. To read or mutate the data without allocating managed objects on the heap, the raw byte array of the blackboard is projected into strongly-typed unmanaged structs (DTOs) using `Unsafe.As`.-   **Shared Execution Context:** Different action and condition nodes within the behavior tree read from and write to the same blackboard fields to share state or signal outcomes (e.g., an observation node writing a target's position, and a locomotion node reading that position to move).

**Supported Blackboard Types** The system provides several distinct blackboard types to accommodate different memory size requirements and use cases:

-   **BrainBlackboard**: The standard cognitive memory component attached to AI entities. It provides a fixed 60-byte `BehaviorParameters` region where behaviors project their specific parameter DTOs. It also includes reserved fields for system-level signals, such as `ExpectedThreatLevel` and edge-triggered interrupts like `Interrupt_MobilityLost`.-   **Blackboard1024**: A generic 1024-byte "heavy" blackboard. This is used for complex behaviors (such as the `PlatoonHillAttack`) that require large, behavior-specific payloads or mutable states. Providing a generic heavy blackboard prevents the engine from exhausting the strict 256 ECS component-type limit.-   **Blueprint Blackboards (****BlueprintBlackboard1024****,** **BlueprintBlackboard4096****,** **BlueprintBlackboard16384****)**: A set of tiered blackboards exclusively used for storing Blueprint Instance state. They allocate internal headers and slot tables alongside their payloads, supporting up to 928 bytes, 3,936 bytes, and 16,096 bytes of raw blueprint state respectively.-   **MockBlackboardState**: A lightweight, unsafe overlay struct used strictly in test scenarios to represent cognitive blackboard memory without taking a full dependency on the `BrainBlackboard` component.

What about hsm based behavior, does it use blackboard as well?

Yes, HSM-based behaviors utilize the blackboard system in the exact same way as Behavior Trees do. They can execute shared AI actions and conditions that are marked with the `[SharedAiAction]` and `[SharedAiCondition]` attributes, which allows both BTree and HSM behaviors to seamlessly share the same logic.

When an HSM action or guard executes, it receives an unmanaged context called the `HsmKernelBridge`, which allows the delegate to recover the `EntityRepository` without triggering any per-frame memory allocations. Through this repository access, the automatically generated HSM dispatch thunks directly read and mutate the entity's `BrainBlackboard` component. Specifically, these thunks project specific parameter fields (DTOs) straight out of the `BrainBlackboard.BehaviorParameters` byte array so the HSM logic can operate on strongly-typed data.

Additionally, if an HSM behavior requires more memory than the standard inline blackboard allows, it can utilize the `[SharedAiHeavyAction]` or `[SharedAiHeavyCondition]` attributes. This grants the HSM node access to heavy generic blackboards, such as `Blackboard1024`, alongside the minimal blackboard projection to hold and modify larger mutable states.

What if i need my AI to combine HSM and btree? Like hsm for high level strategy and btree for lower level tactics? Do these two need to share the same blackboard compinent with identical parameter dto? How to reuse different btrees then?

In the FDP framework, a single behavior definition is strictly designated as either an HSM or a BTree via its `BrainTier` configuration. However, you can seamlessly combine them to build a sophisticated AI using multiple architectural patterns without needing identical data structures.

Here is how you can achieve high-level strategy with HSM and lower-level tactics with BTrees, and how their memory is managed:

**1\. Combining HSM and BTree Logic** You can combine these paradigms using two primary approaches:

-   **Command Hierarchy (Delegation):** A high-level commander entity (e.g., a platoon leader or vehicle) can run an HSM to manage macro-level states. This HSM can then issue `AssignTacticalIntentEvent` commands to its subordinate entities, instructing them to execute specific tactical BTrees. For example, in the Urban Combat scenario, the APC vehicle operates via an HSM (`ConvoyEscort_HSM`), while its deployed soldiers operate using aggressive BTree logic (`InfantryCombat_BT`).-   **Sequential Mission Phases:** A single entity can dynamically switch between HSM and BTree behaviors over time using a `MissionPlanQueue`. An entity could execute an HSM behavior for high-level travel/patrol states, and upon encountering an enemy, a mission trigger can transition it into a tactical BTree behavior.

**2\. Sharing the Blackboard Component and Parameter DTOs** BTree and HSM behaviors **do use the same blackboard components**, but they **do not need identical parameter DTOs**.

-   **Generic Memory Components:** Both paradigms rely on the generic `BrainBlackboard` component (which provides a 60-byte inline memory space) or a heavy `Blackboard1024` component for larger allocations.-   **Behavior-Specific DTOs:** You can define completely different parameter DTOs for your HSM and BTree behaviors (`ParamsDtoType` and `HeavyDtoType`). When a new behavior is assigned to an entity, the `BehaviorIngressSystem` automatically parses the JSON parameters for that specific behavior and writes them directly into the blackboard's raw byte array. At runtime, the BTree or HSM logic simply projects the bytes back into its specific strongly-typed struct using `Unsafe.As`.

**3\. Reusing Different BTrees** If you want to reuse different BTrees for various lower-level tactics, the framework provides built-in mechanisms for modularity:

-   **Behavior Switching:** You can swap an entity's active BTree on the fly via an `AssignBehaviorEvent` (or via phase transitions in the mission plan). When a new BTree is assigned, the system automatically clears the old execution state and starts the new tree fresh from its root.-   **Subtree Nodes:** Within a BTree itself, you can use the `Subtree` leaf node. This acts as a function call, allowing a master BTree to delegate execution to another entirely separate BTree asset by referencing its name or ID.

Ignoeing the FDP reality, isnt it common to combine the hsm and btrees on a single entity to make complex behaviors?

_Please note: The general industry context regarding AI architectures provided in the first paragraph is outside of the provided sources, and you may want to independently verify this information._

Yes, in general game AI development, it is highly common and considered a best practice to combine Hierarchical State Machines (HSMs) and Behavior Trees (BTrees) into a hybrid architecture for a single entity. Typically, an HSM is used to manage high-level macro states or strategies (such as "Patrolling," "Investigating," or "In Combat"), while BTrees are embedded inside those specific states to govern the complex, moment-to-moment tactical execution (like picking a target, finding cover, and firing).

Returning to the provided FDP framework, the system is explicitly designed **not** to run both paradigms concurrently on a single entity's brain. An entity's `BehaviorState` component strictly holds only one `ActiveBehaviorHash` and one `BrainTier` (either `BrainTierHsm` or `BrainTierBTree`) at any given moment.

Because an entity cannot evaluate an HSM and a BTree simultaneously, the framework achieves complex, multi-layered behaviors through two alternative architectural patterns:

-   **Sequential Mission Phases:** Rather than running concurrently, a single entity uses a `MissionPlanQueue` to progress through different behavioral phases over time. For example, a `MissionDirectorSystem` can trigger a phase transition that actively swaps the entity's brain from an HSM behavior to a BTree behavior on the fly.-   **Command Hierarchies (Multi-Entity):** Instead of combining the paradigms on one entity, the framework splits them across a hierarchy. A commander entity (running an HSM for macro-strategy) issues managed `AssignTacticalIntentEvent` commands to its subordinate entities, instructing them to activate specific tactical BTrees.

How could we make the combining hsm and btrees possible on a single entity? If it is a common pattern in game engines, we should make it possdible as well. Pls think how.

To make a combined HSM and BTree architecture possible on a single entity within the FDP framework, we can leverage the existing ECS structure with a few targeted adjustments. The goal is to allow the HSM to act as the high-level macro-strategist while the BTree acts as the moment-to-moment tactical executor, communicating through shared memory.

Here is the architectural plan to make this hybrid approach work seamlessly:

**1\. Component Co-existence in ECS** The ECS repository is built on distinct unmanaged structs. There is no technical limitation preventing an entity from holding both brain memory types simultaneously. You simply add both the **BrainHsm128** (or `BrainHsm64`) component and the **BrainBTreeState** component to the entity at spawn time. Both paradigms will naturally share the single **BrainBlackboard** component.

**2\. Convert** **BrainTier** **to a Bitmask** Currently, `BehaviorConstants.BrainTierHsm` is defined as `1` and `BrainTierBTree` is `2`. Because they are distinct powers of two, we can treat the `BrainTier` byte as a `[Flags]` bitmask.

-   **The Engine Change:** In `BTreeTickSystem` and `HsmTickSystem`, change the strict equality checks (`if (behavior.BrainTier != BehaviorConstants.BrainTierBTree)`) to bitwise inclusion checks.-   If an entity is assigned a behavior with `BrainTier = 3` (Hsm | BTree), both the `BTreeTickSystem` and `HsmTickSystem` will process the entity during the same simulation frame.

**3\. Populate Hybrid Behavior Definitions** The `BehaviorDefinition` class is already perfectly shaped for this; it currently contains optional fields for both the `BTreeInterpreter` and the `HsmDefinitionBlob`. When registering a hybrid behavior in the `BehaviorRegistry`, you would simply populate both fields and set the `BrainTier` to the combined bitmask. When `BehaviorIngressSystem` assigns this behavior, it will automatically reset both execution pointers to start fresh.

**4\. Separation of Concerns via the Blackboard (Data Flow)** If both brains run concurrently, they cannot both write to the `LocomotionChannel` or `WeaponChannel` without overwriting each other or triggering the `ChannelArbitrationSystem`. You must enforce a strict separation of concerns via the shared `BrainBlackboard`:

-   **The HSM (Strategy):** The HSM evaluates state transitions (e.g., "Patrolling" -> "In Combat"). Instead of writing to actuator channels, its actions write macro-level intents directly into the `BrainBlackboard.BehaviorParameters` (e.g., writing an assigned target's Network ID or a geodetic destination).-   **The BTree (Tactics):** The embedded BTree reads these macro parameters from the `BrainBlackboard`. It handles the complex micro-logic (e.g., picking cover, line-of-sight checks, reloading) and is the **exclusive writer** to the `LocomotionChannel`, `WeaponChannel`, and `InteractionChannel`.

**5\. System Pipeline Ordering** In the `CognitiveRuntimeModule`, the `BTreeTickSystem` currently runs immediately before the `HsmTickSystem`. For optimal responsiveness in a hybrid setup, you may want to **reverse this order**. By running the `HsmTickSystem` first, the HSM can react to events (like `MobilityLost`) and update the strategic goals in the `BrainBlackboard`. The `BTreeTickSystem` will then run later in the exact same frame, immediately reading those updated goals and translating them into tactical action outputs without a 1-frame latency.

Ok sounds good! What about sharing the single blackboard, how can i have different generic btree with their parameters reused by the strategic hsm? The strategic hsm defines overall blackvoard dtos, and the btrees, how can they use ther specicific parameters without knowing about the hsm or about each other?

To share a single blackboard between a strategic HSM and generic tactical BTrees without them needing to know about each other's memory structures, you can use the framework's **expression-based parameter binding** and **reusable delegates**.

This allows you to define a single "composite" blackboard DTO for the entity, while restricting the tactical BTrees to only see their specific slice of that memory.

Here is how you implement this separation of concerns:

**1\. Reusable Action and Condition Delegates** Instead of writing BTree actions that take the entire `BrainBlackboard` as a parameter, you write them using `ReusableActionDelegate<TValue, TContext>` and `ReusableConditionDelegate<TValue, TContext>`. These delegates receive a reference to a specific, strongly-typed projected sub-field (`ref TValue data`) rather than the full blackboard. For example, a generic move action only needs to know about `MoveToLocationParams`, keeping it entirely ignorant of the broader strategic context.

**2\. Define a Composite "Hybrid" DTO** The strategic HSM (or the entity's overall configuration) defines a composite struct that combines its own strategic variables with the parameters needed by the lower-level BTrees. Because memory layout is deterministic, you map these sequentially:

```
[StructLayout(LayoutKind.Sequential)]
public struct HybridBlackboardState
{
    // HSM Strategic State
    public byte CurrentMacroPhase;
    public long AssignedTargetNetworkId;

    // Tactical BTree Parameters
    public MoveToLocationParams MoveTactics; // 16 bytes
    public FireAtTargetParams FireTactics;   // 20 bytes
}
```

**3\. Bind the BTrees via Expression Selectors** When compiling the specific BTree via the `BTreeBuilder`, you use lambda expressions to map the generic action to the specific field inside your composite DTO.

```
return new BTreeBuilder<HybridBlackboardState, BTreeContext>()
    .Action(bb => bb.MoveTactics, GenericNodes.Action_MoveToTarget)
    .Action(bb => bb.FireTactics, GenericNodes.Action_FireAtTarget);
```

At tree-build time, the builder uses `Marshal.OffsetOf` to calculate the exact physical byte offset of `MoveTactics` and `FireTactics` within the composite struct.

**4\. Zero-Allocation Pointer Projection at Runtime** When the BTree ticks, the generated adapter thunks automatically use `Unsafe.AddByteOffset` to slice the raw blackboard memory array and project a direct reference to the sub-field. This means the generic BTree node operates directly on its specific chunk of the `BrainBlackboard` (or `Blackboard1024`) memory, safely reading and writing its tactical parameters without ever touching or knowing about the HSM's strategic memory surrounding it.

Additionally, if you want specific actions to be seamlessly invoked by _both_ the HSM and the BTree, you can annotate them with the `[SharedAiHeavyAction]` or `[SharedAiAction]` attributes. You can apply these attributes multiple times to the same method, specifying different parent DTOs and field names, and the source generator will automatically emit the isolated pointer-math adapters for each one.

So a behavior combining hsm and btrees needs to define composite dto that satisfies all btrees, right?

Yes, exactly. If you are building a behavior that combines strategic layers or simply reuses generic tactical BTree actions, you define a **composite DTO** that encompasses all the specific parameter blocks those underlying tactical trees or actions require.

This pattern is already actively used in the FDP framework to assemble complex multi-action behaviors. For example, the `HideInCoverBlackboard` is a composite struct that sequentially combines two completely independent parameter sets:

-   **EqsParams EqsConfig**: Used by environmental query nodes to scan for safe cover.-   **MoveToOptimalCoverParams MoveConfig**: Used by tactical locomotion nodes to execute the movement.

When compiling the behavior tree, the framework's `BTreeBuilder` uses lambda expressions (e.g., `bb => bb.EqsConfig` and `bb => bb.MoveConfig`) to bind the generic, reusable action delegates strictly to their required sub-fields within the composite DTO.

At compile time, the source generator computes the exact byte offsets of these fields. At runtime, the system uses zero-allocation pointer projection (`Unsafe.AddByteOffset`) to slice the raw blackboard memory and pass only the relevant data reference to the generic BTree node. This guarantees that the generic tactical BTree can safely read and write its parameters without ever needing to know about the overarching composite structure or the strategic HSM state surrounding it.

Additionally, if your combined hybrid behavior requires a massive amount of state, you aren't limited to the 60-byte inline `BrainBlackboard`. The framework provides generic heavy memory components, such as `Blackboard1024`. You can define a large composite DTO (like `HillAttackMutableState`) and register it as the behavior's `HeavyDtoType`. The strategic HSM and tactical BTrees can then both project their logic onto this shared 1024-byte payload to orchestrate highly complex maneuvers without exhausting the engine's strict ECS component-type limit.

Btree does not care where his dedicated blackboard portion lived, what ecs component it is, it just needs a pointer to find its parameters, right?

Yes, exactly. The individual Behavior Tree (BTree) actions and conditions do not care where their memory resides or which specific ECS component holds it. They operate purely on a direct reference to their dedicated slice of data.

Here is how this memory decoupling works under the hood:

-   **Scoped Data References:** Instead of requiring the entire blackboard, generic BTree nodes are authored using `ReusableActionDelegate<TValue, TContext>` or `ReusableConditionDelegate<TValue, TContext>`. These delegates only accept a strongly-typed reference (`ref TValue data`) to their specific parameter block.-   **Compile-Time Offsets:** When you construct the tree, the `BTreeBuilder` evaluates the lambda expression used to bind the action to the blackboard (e.g., `bb => bb.MoveConfig`). It uses `Marshal.OffsetOf` to calculate the exact physical byte offset of that specific field within the parent blackboard struct.-   **Runtime Pointer Projection:** At runtime, the BTree executor uses zero-allocation pointer math. A curried adapter takes the base reference to the overarching blackboard component, shifts the pointer using `Unsafe.AddByteOffset`, and casts it via `Unsafe.As` to provide the exact `ref TValue` the generic node requires.

Because of this zero-allocation pointer projection, the BTree node is completely blind to its surroundings. It doesn't know if its memory is packed inside the standard 60-byte `BrainBlackboard`, a generic `Blackboard1024` component, or bundled sequentially alongside an HSM's strategic state—it just safely reads and mutates the memory at the pointer it receives.

Now the parameter dto is bound to behavior id. In our hsm+btree case, this param dto us for the hsm, right? And if hsm activates a concrete btree in its states, it can simply dictates to tge sub-btree where its parameters are. So such sub-btree itself is not a considered a regular behavior (with id and param dto), it must be wired as implementation detail of execuring hsm state, right! Or can we reuse usual btree based behaviors (with id and param dto) in our hsm+btree case?

You are exactly right on the first point: in a combined HSM + BTree architecture, the `ParamsDtoType` (or `HeavyDtoType`) bound to the behavior's ID in the `BehaviorRegistry` is the **composite DTO** that belongs to the overarching HSM behavior.

As for the sub-BTrees, you have the flexibility to do both: you can wire them purely as an implementation detail, **or you can reuse your usual, registered BTree behaviors**.

Here is how you can seamlessly reuse regular BTree behaviors (with their own IDs and parameter DTOs) inside your HSM states:

**1\. The HSM Acts as the External Orchestrator** In the FastBTree runtime, the `Subtree` node type natively returns a safe failure stub specifically because **"Subtree execution requires external orchestration"**. In your hybrid setup, the HSM state action acts as this orchestrator.

**2\. Fetching the Reusable BTree Interpreter** Regular BTree behaviors (like `FireAtTarget` or `MoveToLocation`) are registered in the `BehaviorRegistry` and have an instantiated `Interpreter<TBlackboard, BTreeContext>` attached to their `BehaviorDefinition`. Your HSM's state action can simply query the registry using the usual behavior ID to grab this interpreter.

**3\. Slicing the Composite Memory** Because the regular BTree was compiled against its own specific DTO, your overarching HSM composite DTO just needs to embed that specific struct. For example:

```
public struct HybridHsmState
{
    public byte MacroStatePhase;
    // Embed the exact DTO the generic BTree expects
    public FireAtTargetParams FireParams; 
}
```

Inside the HSM's activity action, you take the raw pointer to the entity's `BrainBlackboard` (or `Blackboard1024`) and use `Unsafe.AddByteOffset` and `Unsafe.As` to slice out a strongly-typed reference _only_ to the embedded `FireParams` field.

**4\. Executing the Sub-Tree** Finally, your HSM action calls the `.Tick(...)` method on the retrieved generic `BTreeInterpreter`, passing it the sliced DTO reference, the entity's `BrainBTreeState`, and the `BTreeContext`.

**Why this is powerful:** This pattern means your generic BTrees are entirely ignorant of how they are executed. You can assign `FireAtTarget` to a simple entity as a standalone behavior via a standard `AssignBehaviorEvent`, and simultaneously embed it inside a complex `ConvoyEscort` HSM on an APC. The generic BTree operates on its dedicated parameter DTO in both cases, completely unaware of whether it is running the entire entity or acting as a micro-tactic inside an HSM state.

Do we have a visual editor for HSM and BTrees?

Yes, we have dedicated visual editors for both Behavior Trees (BTrees) and Hierarchical State Machines (HSMs). Both editors are built on top of a shared foundational node-based editor framework called `NodeEditor`.

Here is how each editor functions:

**BTree Visual Editor**

-   **Node Authoring:** You can visually build behavior trees using composite nodes (like Sequence, Selector, and Parallel) and leaf nodes (Action, Condition, Wait, Subtree).-   **Decorator Pills:** Instead of standalone nodes, decorators (like Inverter, Repeater, Cooldown, and ForceSuccess) are rendered as compact "pills" that attach to their host node.-   **Auto-Layout:** It includes a `BTreeAutoLayout` system that automatically arranges the graph into a clean, downward-growing "tidy-tree" structure with the root at the top.-   **Runtime Debugging & Visualization:** When a debug session is active, the canvas provides rich feedback. It highlights the currently running node with a pulsing gold outline, dims ancestral nodes, and displays success/failure status glyphs. It also supports placing breakpoints directly in the node gutters and offers a heatmap overlay that colors nodes from blue (cold) to red (hot) based on their execution frequency.

**HSM Visual Editor**

-   **Statechart Authoring:** You can visually construct state machines by arranging various state types, including Simple, Composite, Parallel, Final, and History (shallow and deep) states.-   **Transitions:** Transitions between states are visually drawn as links connecting a source state's output to a target state's input.-   **Auto-Layout:** It features an `HsmAutoLayout` tool that arranges top-level states in a left-to-right grid, with sub-states neatly organized inside their parent composites.-   **Runtime Debugging & Visualization:** The HSM canvas highlights active leaf states and their ancestors with a teal glow, and drops a pulse marker on the most recently fired transition. It will also visually flag command lane conflicts between parallel regions with a warning glyph. Like the BTree editor, it supports state and transition breakpoints and features a heatmap mode colored by state-entry frequency.

Can those be used for our combined behaviors? HSM+few BTrees? I can imagine a BTree can be edited standalone and then picked from a list of BTrees used from HSM.

Yes, you can absolutely use the visual editors to support this combined architecture, though the connection between the HSM and the BTree currently involves a small amount of C# code rather than a direct visual link within the HSM editor itself.

Here is how the workflow functions using the visual tools:

**1\. Standalone BTree Editing** You can use the BTree Visual Editor to create and edit your tactical BTrees completely standalone. The editor operates on independent `BehaviorTreeAsset` models. These standalone assets can be authored, saved, and registered into the system completely independent of any HSM.

**2\. Linking BTrees inside the HSM Editor** In the HSM Visual Editor, you do not directly pick a BTree asset from a visual list to run inside a state. Instead, HSM states are configured by assigning C# action delegates to their lifecycle hooks (On Entry, Activity, On Exit) using a dropdown menu populated by the `[HsmActionPicker]` attribute.

To combine them, you would author a generic C# HSM activity action (e.g., `Activity_RunCombatBTree`) that acts as the external orchestrator we discussed earlier. In the HSM Visual Editor, you simply select `Activity_RunCombatBTree` as the **Activity (tick) action** for your strategic state. At runtime, this C# action handles querying the registry for your standalone BTree and ticking it with the sliced composite DTO.

**3\. Visual BTree Nesting (Subtrees)** If you are looking for a purely visual way to "pick from a list of BTrees," the BTree Visual Editor supports this natively via the **Subtree** leaf node.

A `Subtree` node acts as a function call that delegates execution to another completely separate behavior tree asset. When you add a `Subtree` node in the BTree editor, the inspector provides a **"Referenced asset"** field where you can visually select the standalone BTree you want to execute.

By using this setup, your BTrees remain fully modular, standalone assets that you can edit visually and seamlessly plug into your high-level HSM states via a bridging action.

I am concerned about building the param DTO for the HSM when we use multiple generic BTrees for HSM different states. Such a DTO should be built somehow automatically from the HSM parameters and the parameters necessary for the BTrees and generated to csharp code. Would that be possible? BTW how can we visually define the parameters for a HSM or BTree in the visual editor?

While the automatic generation of a composite parameter DTO directly from the BTree and HSM visual editors is a great concept, in the standard FastBTree and FastHSM workflow, these composite DTOs are currently authored manually in C#.

However, your idea is already supported through the framework's **Blueprint** system, which allows you to visually define variables and automatically generate the underlying C# structures.

Here is how parameter management and visual definition work across the different tools:

**1\. Automatic DTO Generation via Blueprints** If you use the Blueprint system to author your AI logic, it supports exactly the automatic C# generation you described.

-   When you create a Blueprint designated as an `AiPrimitive`, you can visually define its required variables and parameters.-   During compilation, the `BlueprintIncrementalGenerator` uses the `AiPrimitiveEmitter` to automatically emit C# code. It generates both a `Params` struct and a `WorkingState` struct specific to that blueprint.-   These generated primitives can then be seamlessly hosted inside your HSMs and BTrees as an `HsmAction`, `HsmGuard`, `BTreeAction`, or `BTreeCondition`.

**2\. Visually Defining Parameters in the BTree & HSM Editors** For standard BTree and HSM assets, you do not visually draw the _structure_ of the DTO. Instead, you define the composite DTO in C# (e.g., `HideInCoverBlackboard`), and the visual editors use reflection to let you visually wire parameters to it:

-   **Blackboard Field Picking:** The BTree visual editor uses a `BlackboardSchemaBuilder` to scan your C# struct and discover all available fields. In the editor's Details Inspector, generic action and condition nodes expose an "Expression target" dropdown. Because this field is decorated with the `[BlackboardFieldPicker]` attribute, the editor provides a visual dropdown of all compatible fields in your composite DTO.-   **Inline Node Parameters:** For nodes that have hardcoded parameters (like the duration of a `Wait` node or the count of a `Repeater` pill), the visual editor provides dedicated Inspector facet structs (e.g., `BTreeWaitFacet`, `BTreeRepeaterFacet`). These utilize the `StructEdit` UI framework to present visual sliders, input boxes, and units (like seconds) in the Inspector.-   **HSM Actions and Guards:** Similarly, in the HSM Visual Editor, you do not write C# to assign actions. When you select an HSM transition or state, the Inspector shows dropdowns for `On Entry action`, `Guard`, etc.. These fields use attributes like `[HsmActionPicker]` and `[HsmGuardPicker]` to visually list all available methods registered in the system.

In summary, to get fully automated C# DTO generation from a visual interface, you author your states as **AiPrimitive Blueprints**. If you use the standard **HSM/BTree visual editors**, you define the composite DTO once in C# and use the Inspector's picker dropdowns to visually wire your generic AI nodes to their specific memory slices.

If blueprint supports visual editing, it should not be difficult to add a support for it for Btree and HSMs, no? When visual editing BTree, why would i (as a user who is non-programmer but AI designer) write a DTO manually in csharp? Does not make sense. Visual editing should support both, not just half of the authoring to be really useful for the users. We should support visual editing of the DTOs with respect to all the BTrees used in the combined HSM+BTree. So the BTrees that are used together with HSMs shoudl provide their DTOs which should be added to the aggregated behavior parameters set. This aggregated set should then be mapped to available space in the BrainBlackboard and/or Blackboard1024. Is that idea feasible? Let's brainstorm.

Your idea is completely feasible and represents the natural evolution of our visual authoring pipeline. It perfectly addresses the friction of requiring a non-programmer AI designer to manually author C# data structures.

We already have the foundational technology to support this through the engine's Blueprint system. Currently, Blueprints designated with the `AiPrimitive` dispatch kind allow designers to visually define `Parameters` and `WorkingState` variables. During compilation, the `AiPrimitiveEmitter` automatically calculates field layouts and emits memory-aligned C# structs. We can absolutely extend this compiler infrastructure to orchestrate aggregated DTOs for combined HSM and BTree behaviors.

Here is how we could architect this automated DTO aggregation:

**1\. Visual Parameter Authoring** Instead of writing C#, the BTree and HSM visual editors would feature a "Variables" panel. When an AI designer builds a generic BTree (like "FlankEnemy"), they visually declare the inputs it needs (e.g., `FlankDistance`, `TargetSpeed`). These get serialized directly into the asset's JSON definition, much like Blueprint `ParameterDecl` blocks.

**2\. Automated Aggregation at Compile Time** When a designer builds a strategic HSM that embeds several tactical BTrees, the source generator (similar to our existing `BTreeDefinitionGenerator` and `BlueprintIncrementalGenerator`) would recursively scan the hierarchy. It would collect all the parameter requirements from the nested BTrees and automatically emit a single, composite C# struct (e.g., `Generated_HsmMacroState_Dto`) that sequentially packs every required tactical parameter.

**3\. Intelligent Memory Placement** The engine can automatically determine where this generated state should live based on its size. The `BehaviorParameterSizeAnalyzer` already calculates struct byte layouts at compile time.

-   If the aggregated DTO is 100 bytes or less, the compiler maps it directly to the fast, inline `BehaviorParameters` region of the standard `BrainBlackboard`.-   If the combined tactical parameters exceed 100 bytes, the compiler seamlessly promotes the storage by assigning the generated struct as the behavior's `HeavyDtoType`, mapping it to the `Blackboard1024` (or larger) generic component.

**4\. Zero-Allocation Pointer Bridging** To ensure the generic BTrees can still run without knowing they are embedded inside a massive HSM DTO, the source generator will emit the exact pointer math needed. It will generate adapter thunks using `Unsafe.AddByteOffset` to slice the `BrainBlackboard` or `Blackboard1024` memory array, passing isolated `ref` pointers to the specific variables the generic BTree node expects.

Architectural Challenges to Solve

To make this user-friendly and robust, we would need to solve two design challenges in the visual editor:

-   **Parameter Aliasing (Data Sharing):** If an HSM drives two different BTrees that both declare a "TargetEntityId" parameter, we do not want the compiler to blindly aggregate them into two separate memory fields. The visual editor would need a binding interface allowing the designer to alias these inputs so they share the exact same memory address in the generated DTO.-   **Static vs. Dynamic Subtrees:** This aggregation relies on knowing the exact memory footprint at compile time. It works perfectly if the HSM statically references the BTrees (via resolved `SubtreeAssetId` GUIDs). If a designer tries to use a dynamic subtree node that selects a behavior at runtime via a string name, the compiler cannot pre-calculate the memory, and we would have to raise a validation error in the editor.

ad Parameter Aliasing (Data Sharing): this must be optional. Sometimes the parameters are not the same thing even if thea re called the same. Can;t we also have a situation when two states executes at the same, running different btree, each using its own Dto which all contain field TargetEntityId but they mean different target for different Btrees?

ad Dynamic subtrees: of course, this automatic DTO aggregation could work for statically (visually) linked BTrees only.

The automatic DTO aggregation should traverse the btree recursively to find all (statically linked) sub-btrees and their parameters.

The aggregated list can not be gather at compilatio time, this is too late. It must be available at editing time so that we can de-duplicate the parameters (alias the parameters) if desirable.

You are absolutely right on all points. Gathering this information only at compile-time strips the AI designer of control. To support deliberate aliasing—and deliberate separation—the aggregation and resolution must happen directly in the visual editor at authoring time.

Here is how we can architect this edit-time aggregation and optional aliasing within our existing visual editor framework:

**1\. Recursive Edit-Time Traversal via the Asset Catalog** To gather the parameter requirements at edit time, we can leverage the editor's existing `IAssetCatalog` and `BTreeSubtreeResolver`.

-   When a designer embeds a BTree into an HSM state (or uses a `Subtree` node inside a BTree), the resolver links it via its static `SubtreeAssetId`.-   Because the `IAssetCatalog` holds the live models of all open assets (`BehaviorTreeAsset`), the editor can recursively walk down the tree of statically linked sub-BTrees.-   It scrapes all the requested parameters (e.g., `TargetNetworkId` from `FireAtTarget`, `RouteEntityId` from `FollowRoute`) and bubbles them up to the top-level HSM asset in real-time.

**2\. Opt-In Parameter Aliasing (The Visual Workflow)** Since two different BTrees might both require a parameter named `TargetEntityId` but use them for completely different purposes (e.g., one is the target to shoot, the other is the VIP to escort), aliasing must be an explicit choice by the designer.

-   **The Blackboard Panel:** The HSM editor will feature a "Blackboard Variables" panel.-   **Unbound Inputs:** As the editor recursively discovers required parameters from nested BTrees, they appear in a list of "Unbound Inputs".-   **Explicit Binding:** The designer can drag an unbound input and either:
    -   **Promote to New Variable:** Create a dedicated variable (e.g., `AssassinationTarget`).-   **Promote to Another New Variable:** Create a separate variable for the other BTree's target (e.g., `EscortTarget`).-   **Alias (Data Share):** Drag _both_ inputs onto a single variable (e.g., `SharedFocusTarget`).-   This guarantees that if two states execute different BTrees, their `TargetEntityId` fields are completely isolated in memory unless the designer explicitly grouped them.

**3\. Dynamic Subtrees** As you noted, this completely excludes dynamic subtrees. If a designer uses a node that resolves a behavior by a dynamic string name at runtime, the editor cannot traverse it. Those specific subtrees would either need to operate strictly on a pre-defined common DTO interface, or they would be flagged as an error in the visual editor if they attempt to declare new memory requirements.

**4\. Bridging Edit-Time to the Source Generator** Once the designer has explicitly mapped (or aliased) the inputs to variables in the editor, this mapping is serialized into the HSM's JSON definition. When the project compiles, our Roslyn source generators take over. Instead of blindly aggregating fields, the generator looks at the explicit variable list defined in the JSON. It emits the composite C# DTO using only the variables the designer declared (e.g., generating `long AssassinationTarget;` and `long EscortTarget;`). It then emits the zero-allocation pointer projection adapters (`Unsafe.AddByteOffset`) so that the generic `FireAtTarget` BTree gets a reference specifically pointed to the `AssassinationTarget` bytes, and the `FollowRoute` BTree gets a reference pointed to the `EscortTarget` bytes.

The recursive tree scan and the "Blackboard Variables" panel needs to be present for a standalone BTree visual editor as well. Identical mechanism of building the final param DTO.

The top level HSM or BTree parameters needs to be always mapped to BranBlackboard component to stay consistent with how pbehavior parameters are handled now (pls check and confirm if this is the convention and why); while the sub-btree parameters DTOs might be mapped to BrainBlackboard (if they all fit there) or rather all to the Blackboard1024 for simplicity.

Extending the recursive tree scan and "Blackboard Variables" panel to standalone BTree assets is the correct architectural move. A master BTree executing multiple `Subtree` nodes has the exact same nested memory requirements as an HSM orchestrating those same BTrees, so the visual editor infrastructure should absolutely be shared.

You are correct that top-level behavior parameters must map to the `BrainBlackboard` component to maintain framework consistency. Specifically, these parameters project into the `BehaviorParameters` byte array, which has a strict `MaxBehaviorParamByteSize` limit of 100 bytes.

The primary reason for this convention is the `BehaviorIngressSystem`, which handles behavior assignment and parsing. To ensure atomic, zero-allocation behavior transitions, the ingress system parses incoming JSON parameters into a temporary stack-allocated shadow copy of the 128-byte `BrainBlackboard`. Only if parsing succeeds does it commit this shadow buffer back into the live ECS component, ensuring that a parse failure leaves the entity entirely on its previous behavior with no partial state corruption. The 100-byte parameter ceiling guarantees that dynamically parsed user data never overwrites the system-level `ExpectedThreatLevel` or edge-triggered `Interrupt` registers located at the tail of the `BrainBlackboard` struct.

Your proposal to route all aggregated sub-BTree parameters directly to the `Blackboard1024` component for simplicity is a highly pragmatic solution.

By offloading the aggregated sub-tree parameters, the visual editor's code generator simply compiles them into a dedicated composite struct and registers it as the overarching behavior's `HeavyDtoType`. When the `BehaviorIngressSystem` assigns the behavior, it detects the `HeavyDtoType` requirement and automatically provisions a `Blackboard1024` ECS component on the entity if one is not already present.

This generic `Blackboard1024` component provides 1024 bytes of heavy working memory, specifically designed to handle large, behavior-specific payloads without exhausting the engine's strict 256 ECS component-type limit. The Roslyn source generator will emit the necessary C# pointer math to project the tactical sub-BTree parameters directly out of the `Blackboard1024.Memory` buffer using `Unsafe.As`. This satisfies the memory requirements of all nested BTrees while keeping the core 100-byte `BrainBlackboard` reserved strictly for top-level, orchestrator-driven parameters.

The parameter aliasing (data sharing) might need to work on the whole dto level only as we can not easily share a field between different DTOs (unless we foce copy the field to all Dtos that are aliased at the time of starting the behavior), correct? Maybe not even the copying of aliased fields is viable, pls check.

You are completely correct. Field-level aliasing between two different, embedded DTOs is not physically viable in this architecture, and "force copying" the fields only at the start of the behavior fundamentally breaks how the blackboard is used.

Here is why your intuition is right, and how we must handle data sharing instead:

**1\. The Physical Memory Constraint** Generic BTree nodes operate via zero-allocation pointer projection. A generic action like `Action_FireAtTarget` expects a contiguous block of memory formatted exactly as `ref FireAtTargetParams`.

If your composite DTO embeds multiple parameter blocks:

```
[StructLayout(LayoutKind.Sequential)]
public struct HybridState {
    public MoveToLocationParams Move; // 16 bytes, offset 0
    public FireAtTargetParams Fire;   // 20 bytes, offset 16
}
```

If both DTOs contain a `TargetEntityId`, those fields physically exist at two different memory addresses. We cannot force them to share the exact same memory space using `[StructLayout(LayoutKind.Explicit)]` and `[FieldOffset]`. If we overlap the `TargetEntityId` fields, we force the entirety of `MoveToLocationParams` and `FireAtTargetParams` to overlap, instantly corrupting all their non-shared fields due to size and layout mismatches.

**2\. Why "Copy at Start" Fails for Mutable State** If we were to simply copy the aliased value from a master property into the sub-DTOs when the behavior is assigned (via the `ParseParams` delegate during JSON ingestion), it would only work for strictly read-only inputs.

However, the blackboard is a shared, mutable runtime execution state. If `BTree_A` discovers a new target and writes it to `Move.TargetEntityId`, `BTree_B` will still have the old value trapped inside `Fire.TargetEntityId`. Because ECS memory must be unmanaged and relocatable, we cannot store C# reference pointers inside the structs to link them.

The Viable Solutions for Data Sharing

Since field-level aliasing of different DTOs breaks the memory layout, you must enforce sharing through one of the following architectural patterns:

**Approach A: Whole DTO Sharing** As you suggested, aliasing works perfectly at the _entire DTO level_. If the HSM is orchestrating two different sub-BTrees that happen to use the exact same tactical parameter struct (e.g., switching between `FlankEnemy_BT` and `ChargeEnemy_BT` which both consume `CombatLocomotionParams`), the composite DTO only needs to declare one `CombatLocomotionParams SharedMoveState;`. The HSM simply passes the sliced pointer to that same block of memory to whichever BTree is currently active.

**Approach B: The HSM Acts as the Synchronization Bridge** If you need to share a conceptual value (like "Current Target") between two completely different DTOs, the HSM state must own the authoritative value and synchronize it.

You declare the shared variable in the HSM's macro state, alongside the nested tactical DTOs:

```
public struct HybridHsmState
{
    public long SharedTargetEntityId; // The authoritative alias

    public MoveToLocationParams MoveTactics;
    public FireAtTargetParams FireTactics; 
}
```

Because the HSM activity action acts as the external orchestrator for the sub-BTrees, it performs a fast memory copy just before and after ticking the BTree:

```
// Inside Activity_RunCombatBTree:
ref var state = ref Unsafe.As<byte, HybridHsmState>(ref bbMemory);

// 1. Sync down: Push the shared alias into the tactical DTOs
state.MoveTactics.RouteEntityId = state.SharedTargetEntityId;
state.FireTactics.TargetNetworkId = state.SharedTargetEntityId;

// 2. Tick the BTree
_fireBTree.Tick(ref state.FireTactics, ref btState, ref ctx);

// 3. Sync up: If the BTree mutated the target, pull it back to the shared alias
state.SharedTargetEntityId = state.FireTactics.TargetNetworkId;
```

This guarantees that generic tactical BTrees continue to receive their exact `ref TValue` memory shape, while the orchestrating HSM maintains perfect synchronization of any aliased variables across different tactical states every single frame.

Hmm, the approach A and B can be also combined, right? While aliasing on DTO level is a true sharing of the same DTO by multiple behaviors (they use some memory pointer), the copying of selected fiels down and back up can be used as well in parallel.

Yes, you can absolutely combine both approaches in a single hybrid behavior. Because the framework gives the HSM full access to the raw unmanaged memory of the blackboard, you have the flexibility to design a composite DTO that utilizes both true memory sharing (Approach A) and manual synchronization (Approach B) simultaneously.

In this combined pattern, your composite DTO will hold the overarching macro-state variables alongside both shared and specific sub-DTOs.

For example, you could define a composite state like this:

```
[StructLayout(LayoutKind.Sequential)]
public struct HybridHsmState
{
    // 1. Authoritative Macro State (for Approach B synchronization)
    public long SharedTargetEntityId;

    // 2. True Shared DTO (for Approach A aliasing)
    // Multiple BTrees (e.g. Flank, Retreat) can receive a direct pointer to this.
    public MoveToLocationParams SharedNavState;

    // 3. Specific DTOs (for Approach B synchronization)
    public FireAtTargetParams FireTactics;
}
```

Inside the HSM's activity action, you project the raw memory into this composite struct using zero-allocation pointer math (`Unsafe.As` and `Unsafe.AddByteOffset`). From there, the HSM acts as the master orchestrator capable of executing both strategies in parallel:

-   **Applying Approach B (Syncing):** Before ticking a combat-specific BTree, the HSM copies `SharedTargetEntityId` down into `FireTactics.TargetNetworkId`. If the combat tree mutates the target during execution, the HSM copies it back up to the authoritative alias after the tick.-   **Applying Approach A (Direct Sharing):** When orchestrating movement, the HSM simply passes the direct `ref SharedNavState` pointer to whichever movement BTree is currently active. Both trees operate directly on the exact same block of memory, meaning no manual data copying is needed to keep their locomotion parameters synchronized.

Because the generic BTrees are entirely decoupled and only accept a strongly-typed reference (`ref TValue`) to their dedicated parameter block, they have no idea whether the data they are reading is a uniquely synced struct or a directly shared DTO memory slice. This combined architecture gives you maximum flexibility, letting you share heavier data structures (like navigation contexts) directly via pointers, while strictly bridging and translating distinct conceptual variables across different tactical states.

Shouldn;t this copying work also between upper btree and its subtree, isn't is the completely same concept?

Conceptually, you are exactly right. Passing parameters or sharing data down to a child behavior tree is the exact same architectural pattern.

However, in the FastBTree runtime, the native `Subtree` node does not automatically handle execution or memory mapping. If you look at the core interpreter loop, evaluating a `NodeType.Subtree` simply returns `NodeStatus.Failure` as a safe stub, specifically because "Subtree execution requires external orchestration". The framework treats subtrees as structural references rather than fully automated function calls with built-in parameter injection.

To achieve data synchronization between an upper master BTree and a sub-BTree, you must apply the exact same "external orchestrator" pattern we used for the HSM. You author a custom BTree Action node (e.g., `Action_RunSubTree`) that takes on the responsibility of ticking the child tree.

The memory mechanics work identically:

-   **The Composite DTO:** The master BTree's parameter struct explicitly embeds the sub-BTree's specific parameter DTO alongside any shared variables.-   **The Orchestrator Action:** When the master BTree evaluates `Action_RunSubTree`, the generic action delegate receives its strongly-typed memory slice via zero-allocation pointer projection.-   **Data Synchronization (Sync Down):** Inside the action, you manually copy the shared fields (such as a shared `TargetEntityId`) down into the embedded sub-BTree DTO.-   **Execution:** You retrieve the child tree's `Interpreter` and invoke its `Tick` method, passing a direct `ref` to the embedded sub-DTO.-   **Data Synchronization (Sync Up):** Once `Tick` returns, you copy any mutated state back up to the master variables.

This ensures that strict unmanaged memory layout rules are maintained. Field-level aliasing of distinct structs is physically impossible without causing memory overlap corruption, so whether the orchestrator is an HSM state or a master BTree action node, explicit state synchronization or pointer-sharing of a unified sub-DTO is mandatory.

How could the user in the visual editor visually define what fields of what dto to be copied to the fields of sub-btree's dto? What the "aggregated blackboard variables" editing might look like?

To make the aggregation and mapping of blackboard variables intuitive for an AI designer, we can extend our existing `NodeEditor` and `StructEdit` visual frameworks. We need to support both **Approach A (Whole DTO Memory Aliasing)** and **Approach B (Field-Level Synchronization)** visually.

Here is how the "Aggregated Blackboard Variables" editing and field mapping could look and function in the visual editor:

1\. The "Blackboard Variables" Panel

Similar to the existing `MyBlueprintPanel`, the editor would feature a dedicated "Blackboard Variables" side panel. It acts as the central hub for the behavior's memory layout.

The panel would be split into two sections:

-   **Defined Variables (The Composite DTO):** A list of variables explicitly created by the designer for the overarching HSM or master BTree (e.g., `SharedTargetEntityId`, `GlobalMoveState`).-   **Unbound Sub-Tree Requirements:** As the editor recursively scans statically linked `Subtree` nodes, it lists their required parameter blocks here (e.g., `[FlankEnemy_BT] Requires: CombatLocomotionParams`).

2\. Visually Defining Whole DTO Aliasing (Approach A)

For true memory sharing (where multiple sub-trees share the exact same memory pointer for a DTO), the designer uses drag-and-drop within the variables panel.

-   **The UX:** The designer drags an unbound requirement (e.g., `CombatLocomotionParams` required by `FlankEnemy_BT`) and drops it onto an existing variable of the same type in the "Defined Variables" list.-   **Visual Feedback:** The unbound requirement disappears from the bottom list, and the defined variable updates its badge to show it is now driving multiple trees (e.g., `GlobalMoveState (Used by: FlankEnemy, Retreat)`).-   **Under the Hood:** The source generator notes this alias and emits the zero-allocation pointer slice (`Unsafe.AddByteOffset`) pointing both sub-trees to this single memory block.

3\. Visually Defining Field-Level Copying (Approach B)

If the designer needs to synchronize specific fields between different DTOs (e.g., copying the HSM's `SharedTargetEntityId` into the sub-tree's `FireTactics.TargetNetworkId`), we can use two different visual paradigms depending on the editor's context:

**Option 1: The Details Inspector with Sync Toggles (StructEdit)** When the designer selects an HSM State or a `Subtree` node, the `DetailsPanel` opens. We leverage the `StructEdit` framework to render the sub-tree's required DTO fields.

-   **The UX:** Next to each field in the sub-tree's DTO (e.g., `TargetNetworkId`), there is an "Expression target" dropdown powered by the `[BlackboardFieldPicker]` attribute. The designer opens the dropdown and selects the overarching `SharedTargetEntityId` variable.-   **Sync Direction Toggles:** Next to the dropdown, the UI provides two small toggle buttons: **\[↓ Sync In\]** and **\[↑ Sync Out\]**.
    -   `[↓ Sync In]`: The orchestrator copies the master value down to the sub-tree before ticking it.-   `[↑ Sync Out]`: The orchestrator copies the sub-tree's mutated value back up to the master variable after ticking it.-   **Code Generation:** The visual editor serializes this binding. At compile time, the source generator emits the explicit `subDto.TargetNetworkId = master.SharedTargetEntityId` assignment statements immediately before/after the `.Tick()` call.

**Option 2: Node Data Pins (NodeEditor Graph)** Since our `NodeEditor` foundational framework already supports data flow via `PinKind.Data`, we can expose the field mapping directly on the graph canvas.

-   **The UX:** The `Subtree` node (or HSM State node) dynamically generates input and output data pins for every field inside its required DTO.-   **Wiring:** The designer drops a "Get Variable" node for their master `SharedTargetEntityId` and drags a wire connecting it to the `TargetNetworkId` input pin on the Subtree node.-   **Sync Direction:**
    -   Connecting a wire to the **Input Pin** (left side) visually defines the **"Sync Down"** pre-tick copy.-   Connecting a wire from the **Output Pin** (right side) to a "Set Variable" node defines the **"Sync Up"** post-tick copy.

Summary of the Designer Workflow

-   The AI designer drops a `Subtree` node onto the canvas.-   The "Variables" panel detects the sub-tree requires a `FireAtTargetParams` DTO.-   The designer decides they only want to map the target ID. They select the `Subtree` node.-   In the Details Inspector, they map the sub-tree's `TargetNetworkId` field to their overarching `AssassinationTarget` variable and toggle `[↓ Sync In]`.-   At compile time, the framework automatically generates a composite DTO containing the master variables and the embedded `FireAtTargetParams` struct, seamlessly emitting the C# code to copy the target ID down before executing the sub-tree.

can you sketch that using ascii art?

Here is the visual mockup of how we will integrate both Approach A (Whole DTO Sharing) and Approach B (Field-Level Synchronization) into the existing `NodeEditor` and `StructEdit` toolchains.

```
================================================================================
| ≡ BLACKBOARD VARIABLES (Panel)                                             |
================================================================================
| ▼ DEFINED VARIABLES (Master Composite DTO)                                 |
|                                                                            |
|   [+] Add Variable...                                                      |
|                                                                            |
|   ◇ SharedTargetEntityId  (long)                                           |
|                                                                            |
|   ◇ GlobalMoveState       (MoveToLocationParams)                           |
|      ↳ Aliased by: [FlankEnemy_BT], [Retreat_BT]                           |  <-- APPROACH A
|                                                                            |
| -------------------------------------------------------------------------- |
| ▼ UNBOUND SUB-TREE REQUIREMENTS                                            |
|   (Drag to 'Defined Variables' to alias whole DTOs)                        |
|                                                                            |
|   ◇ FireTactics (FireAtTargetParams)   -- Required by: [Shoot_BT]          |  
================================================================================

                                   ↓ ↓ ↓

================================================================================
| ≡ DETAILS INSPECTOR - Subtree Node: [Shoot_BT]                             |
================================================================================
| Subtree Asset: [ Fire At Target .fdp ]                                     |
|                                                                            |
| ▼ PARAMETER SYNCHRONIZATION (StructEdit)                                   |
|                                                                            |
|   Field Name         Expression Target                Sync Direction       |
|   ------------------------------------------------------------------------ |
|   TargetNetworkId  [ SharedTargetEntityId     |▼]    [↓ In]  [  Out]       |  <-- APPROACH B
|   WeaponSlot       [ (none)                   |▼]    [  In]  [  Out]       |
|   StatusOut        [ LastWeaponStatus         |▼]    [  In]  [↑ Out]       |
================================================================================

                                   OR

================================================================================
| ≡ NODE GRAPH CANVAS (NodeEditor)                                           |
================================================================================
|                                                                            |
|    +-----------------------+                                               |
|    | [Variable]            |                                               |
|    | SharedTargetEntityId  |                                               |
|    |                    (O)>---------\                                     |
|    +-----------------------+          \      +-----------------------+     |
|                                        \     | [Subtree] Shoot_BT    |     |
|                                         \--->(I) TargetNetworkId (O)>--+   | <-- APPROACH B
|                                              |                       | |   |
|                                              |            WeaponSlot | |   |
|                                              |                       | |   |
|    +-----------------------+                 |             StatusOut | |   |
|    | [Set Variable]        |                 +-----------------------+ |   |
|    | LastWeaponStatus      |                                           |   |
|    |                    (I)<-------------------------------------------+   |
|    +-----------------------+                                               |
================================================================================
```

Architectural Breakdown of the UI

**1\. The "Blackboard Variables" Panel (Left Side)** This panel gives the AI designer a bird's-eye view of the memory footprint. The `GlobalMoveState` variable demonstrates **Approach A** (Whole DTO Aliasing). The designer dragged the unbound locomotion requirements from `FlankEnemy_BT` and `Retreat_BT` directly onto this variable.

-   **Engine Reality:** At compile time, the source generator emits only one `MoveToLocationParams` block. When either BTree is ticked, the zero-allocation pointer adapter (`Unsafe.AddByteOffset`) provides both trees a direct `ref` to the exact same bytes in the `BrainBlackboard`.

**2\. The Details Inspector (Middle)** This leverages our existing `StructEdit` property tree rendering. The designer selects the `Shoot_BT` subtree node, and the inspector uses reflection to display the `FireAtTargetParams` fields.

-   **Engine Reality:** Because the designer mapped `TargetNetworkId` to `SharedTargetEntityId` and toggled `[↓ In]`, this implements **Approach B**. The generated C# orchestrator action will emit a fast memory copy (`subDto.TargetNetworkId = master.SharedTargetEntityId;`) immediately before executing `_shootBTree.Tick(...)`.

**3\. The Node Graph Canvas (Bottom)** For designers who prefer visual data flow, the `NodeEditor` foundation supports exposing DTO fields as physical `PinKind.Data` input/output pins.

-   **Engine Reality:** Connecting the `(O)` pin of `SharedTargetEntityId` to the `(I)` pin of `TargetNetworkId` visually encodes the exact same "Sync Down" instruction as the Inspector's `[↓ In]` toggle. When the compilation step evaluates the AST, it translates these visual wire edges into the same pre-tick value assignment statements.

středa 27. května

The param dtos are not just a 'property' of a whole behavior (btree , hsm), but also the actions. And the dtos required by the actions basically define the set of dtos required by the behavior (btree/hsm) but the behavior might add some more fields on top of what actions require. How does an action declare what is its dto? Does it register itselfs to some action registry similarly to behaviors?

Actions declare their required DTOs directly through their method signatures, and they rely on a combination of C# attributes and compile-time source generators to automatically register themselves into action registries.

Here is how actions declare their DTOs and register themselves within the framework:

**1\. Declaring the Required DTO via Method Signatures** Generic BTree actions do not need to implement a specific interface to define their DTO. Instead, they use a generic delegate pattern called `ReusableActionDelegate<TValue, TContext>` (or `ReusableConditionDelegate` for conditions).

The action simply declares the exact struct it needs as the first `ref` parameter. For example, a generic movement action declares that it strictly requires a `MoveToLocationParams` DTO:

```
[BTreeAction]
public static NodeStatus Action_WriteMoveToChannel(
    ref MoveToLocationParams p, 
    ref BehaviorTreeState state, 
    ref BTreeContext ctx)
```

In this signature, the `ref MoveToLocationParams p` parameter acts as the DTO declaration. The action is completely isolated and operates only on this specific data slice.

**2\. Auto-Registration via Attributes and Source Generators** Actions do not manually push themselves into a registry. Instead, AI developers decorate their static action methods with specific attributes:

-   **\[BTreeAction\]** **/** **\[BTreeCondition\]**: Marks standard behavior tree nodes.-   **\[SharedAiAction\]** **/** **\[SharedAiCondition\]**: Marks actions that can be used interchangeably by both BTrees and HSMs.-   **\[SharedAiHeavyAction\]**: Used when an action requires both a minimal inline DTO and a generic heavy component (like `Blackboard1024`).

During compilation, Roslyn source generators (like `BTreeActionGenerator` and `HsmActionGenerator`) scan the codebase for these attributes. The generators automatically emit a registrar class (e.g., `FbtActionRegistrar.RegisterAll`) that pushes all discovered methods into the `ActionRegistry<TBlackboard, TContext>` or the `HsmActionDispatcher`.

**3\. Binding the Action DTO to the Behavior DTO** When you construct a specific behavior (the overarching BTree) using the `BTreeBuilder`, you explicitly map the action's required DTO to a field inside the behavior's composite DTO using a lambda expression.

For example, if your behavior's composite DTO has a field called `MoveConfig`, you bind the action like this:

```
.Action(bb => bb.MoveConfig, EqsCombatNodes.Action_MoveToOptimalCover)
```

At compile-time, the `BTreeBuilder` evaluates this lambda and uses `Marshal.OffsetOf` to calculate the exact byte offset of the `MoveConfig` field within the overarching behavior DTO.

The source generator then emits a zero-allocation adapter closure into the registry. At runtime, this adapter takes the raw pointer to the entity's entire `BrainBlackboard`, shifts it by the computed byte offset (`Unsafe.AddByteOffset`), and casts it to explicitly provide the `ref MoveToLocationParams` that your generic action requires.

OK so in our recursive parameter dto scan (in order to show & edit the blackboard variables) the editor needs to 'know' what actions each participating behavior (btree, HSM) is using and extract their parameter DTO and then show them somehow in the blackboard variables window, is that right? How the editor 'knows'?

Yes, your understanding is exactly right. In order to populate the "Blackboard Variables" panel with unbound inputs, the editor must identify every action and condition used within the nested behaviors and determine the exact parameter DTO struct each one expects.

Here is how the editor "knows" this information during the edit-time recursive scan:

**1\. The Visual Asset Models Store the Action Names** When an AI designer builds a behavior visually, the editor operates on asset models like `BehaviorTreeAsset` or `HsmAsset`. When you drop an action node onto the canvas, the editor stores a payload (such as `BTreeActionPayload`) that records the `MethodFqn` (the Fully Qualified Method Name) of the C# delegate you selected. Similarly, HSM states explicitly serialize the names of their chosen delegates via fields like `OnEntryAction` and `ActivityAction`.

**2\. The Offline Schema Exporter** To bridge the gap between these serialized method names and their underlying memory requirements, the framework provides a utility called the `BTreeSchemaExporter`. This tool uses reflection to scan the compiled assemblies for any static methods annotated with `[BTreeAction]` or `[BTreeCondition]`.

**3\. DTO Inference from Method Signatures** When the exporter discovers an action method, it analyzes the C# method signature. Because generic actions use the `ReusableActionDelegate` pattern, they always declare their required DTO slice as the first `ref` parameter. The exporter specifically looks at this first parameter via a `GetFirstParamTypeName` helper.

It then compiles a comprehensive `BTreeSchema` containing `ActionDescriptor` and `ConditionDescriptor` records. Each descriptor maps the `MethodName` directly to its required `BlackboardDtoType`.

**4\. Tying It Together in the Editor** During the recursive scan:

-   The editor recursively walks the static links in your `HsmAsset` or master `BehaviorTreeAsset`.-   Whenever it encounters an action or condition node, it reads the saved `MethodFqn` string.-   It performs a fast lookup against the pre-generated `BTreeSchema` (or HSM equivalent) using that method name to find the `BlackboardDtoType`.-   It bubbles that DTO type up to the "Blackboard Variables" panel as an unbound requirement.

By combining the asset's serialized method strings with a reflection-based schema table, the visual editor seamlessly understands the memory shape of any generic AI node without needing to dynamically parse or compile C# code at edit time.

Action nodes can be added and removed dynamically during Btree authoring. That means new items will need to pop up in the blackboard variables window if the use adds new action that was not used before. Moreover there could be multiple different instances of the action's DTO in the blackboard, so that different parts of tree using same action can work with different parameters. Each action node then needs to 'know' what blackboard variable it is connected to, do i understand correctly?

Yes, you understand the architecture perfectly!

Here is exactly how the framework supports this dynamic flexibility and multiple DTO instances for the same action:

**1\. Discovering DTO Requirements Dynamically** When an AI designer drops a new Action or Condition node onto the visual canvas, the editor immediately looks up the selected C# method in the pre-compiled `BTreeSchema`. This schema descriptor tells the editor exactly what parameter struct the method expects (the `BlackboardDtoType`). If that DTO isn't already mapped, the editor will dynamically surface it as an unbound requirement in the Blackboard Variables window for the designer to address.

**2\. Multiple Instances of the Same DTO** You can absolutely define multiple instances of the same DTO type in your composite blackboard. For example, if your tree has a flanking branch and a retreating branch that both use a generic `Action_WriteMoveToChannel`, the designer can declare two entirely separate variables in the blackboard window: e.g., `FlankTactics` and `RetreatTactics` (both of type `MoveToLocationParams`). They will exist at two different byte offsets in the generated C# struct.

**3\. How the Node "Knows" its Connection** Each individual action or condition node maintains a strict record of which specific blackboard variable it is wired to.

-   **Editor Data Model:** Under the hood, the node's `BTreeActionPayload` stores a string property called `ExpressionTargetField`.-   **Visual Inspector:** When the designer selects the node, the Details Inspector renders a dropdown menu for this property, powered by the `[BlackboardFieldPicker]` attribute. The designer uses this dropdown to explicitly connect "Node A" to `FlankTactics` and "Node B" to `RetreatTactics`.-   **Code Generation:** When the asset is compiled to C#, the `BTreeFluentEmitter` reads the node's `ExpressionTargetField` and automatically generates the exact lambda binding expression (e.g., `.Action(dto => dto.FlankTactics, ...)`).

This ensures that even if you have five different nodes using the exact same underlying generic action delegate, the runtime uses zero-allocation pointer math to project each node precisely to its individually assigned slice of blackboard memory.

How can we define the actions via blueprints?

To define actions visually via Blueprints, you utilize the **AiPrimitive** dispatch kind. This allows AI designers to construct reusable actions and conditions entirely within the visual editor, which can then be natively executed by both Behavior Trees and HSMs.

Here is how the authoring process works for Blueprint actions:

**1\. Set the Dispatch Kind and Intent** You create a Blueprint asset configured as `BlueprintDispatchKind.AiPrimitive`. You then set its `Intent` to `Action` (or `Condition` if you are building a guard/check).

**2\. Declare Hostings** You specify exactly where this primitive is permitted to run by adding to its `Hostings` list. Supported hostings include `BTreeAction`, `BTreeCondition`, `HsmAction`, `HsmGuard`, and `BlueprintCall`.

**3\. Define the Memory Layout Visually** Instead of manually writing C# DTOs, you use the editor's Variables panel to declare your memory requirements:

-   **Parameters:** The external inputs your action needs from the orchestrating behavior (e.g., `TargetEntityId`, `MoveSpeed`).-   **Working State:** Internal, mutable variables that need to persist across ticks (e.g., `CurrentPhase`, timers, accumulators).

**4\. Author the Logic Graph** You construct the execution logic visually in the `AiPrimitiveMain` graph. You use nodes to read parameters, mutate working state, and call other primitives. The execution flow terminates using a `ReturnStatus` node, yielding a `NodeStatus` (Success, Failure, or Running) back to the calling brain.

How the Compiler Handles It

When you compile the project, the `AiPrimitiveEmitter` takes your visual definitions and generates a highly optimized, allocation-free C# class:

-   **Struct Generation:** It emits unmanaged `Params` and `WorkingState` structs matching your visual declarations.-   **Core Logic:** It translates the visual node graph into a fast `TickCore` method.-   **Adapter Thunks:** It generates specific wrapper thunks (e.g., `BTreeTick`, `BTreeEvaluate`, `HsmActivity`, `HsmGuard`) based on the `Hostings` you selected. These thunks automatically handle the zero-allocation pointer math, projecting your `Params` out of the inline `BrainBlackboard` and your `WorkingState` out of the generic `Blackboard1024` component.

Finally, the source generator emits a `[BlueprintRegistrar]` class that pushes these thunks directly into the `BehaviorRegistry` and `HsmActionDispatcher`. This makes your new visual action instantly discoverable in the BTree and HSM visual editors without you writing a single line of C# registration code.

If we remove an action node from a btree, what happens to its dto in the blackboard? it sould have been already 'instantiated' multiple time as multiple variables. It should not be removed automatically. Maybe it can be just marked as 'unused' (differen more grayed color or something like that).

You are entirely correct. Deleting an action node from the canvas should never automatically delete the defined variable from the blackboard.

In our architecture, the defined variables belong to the overarching `BehaviorTreeAsset`, while the individual leaf nodes simply hold a string reference (the `ExpressionTargetField` inside their `BTreeActionPayload`) pointing to that variable. When an AI designer deletes an action node via the editor's removal commands, the system only drops the node and its payload from the asset's internal tracking lists. The variable itself remains untouched.

This preserves data integrity for two critical reasons:

-   The variable might be aliased by multiple other nodes in the tree (Approach A sharing).-   The AI designer might simply be swapping the action node out or temporarily disconnecting it, and we do not want to destructively wipe their explicitly named data structures.

To implement your visual feedback idea, the "Blackboard Variables" panel will dynamically calculate reference counts. During the UI draw loop, the editor will scan the `ExpressionTargetField` of all surviving nodes in the graph. If a variable's reference count drops to zero, the panel will render it using a dimmed, disabled text style (e.g., using the framework's existing `ImGui.TextDisabled` paradigms) to clearly communicate that it is currently unmapped but still exists.

However, we must account for the strict unmanaged memory constraints of the ECS. If a designer leaves an unused variable in the variables list, the Roslyn source generator will still emit it into the compiled composite DTO, permanently consuming precious bytes in the `BrainBlackboard` or `Blackboard1024`. To prevent "memory creep" over time, we will add a new diagnostic rule to the `BTreeValidator`. If the validator detects an instantiated DTO variable with zero node references, it will surface a visual warning diagnostic on the canvas, prompting the designer to explicitly delete the unused memory block if they are truly finished with it.

Dnes • 21:42

