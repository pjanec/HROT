
The codebase actively enforces the architectural constraints and paradigms defined in the AI Development Guide.

**CQRS Boundary and Channel Actuators** The rigid CQRS boundary separating the cognitive brain from the muscle executors is fully intact. Behavior state machines and trees do not mutate physics transforms directly; instead, they write intents to `LocomotionChannel`, `WeaponChannel`, and `InteractionChannel`, which are mapped to global component IDs. For instance, behavior nodes like `Action_AimAndFire` push their respective `ActionId` to the `WeaponChannel`, leaving execution to downstream systems like `WeaponDispatcherSystem`.

**Tiered Execution Paradigms** The engine integrates FastBTree (Tier 2) and FastHSM (Tier 1) as external dependencies alongside Tier 0 hardcoded scripts. The `CognitiveRuntimeModule` manages the simulation step sequence, registering `BTreeTickSystem` and generic `HsmTickSystem` instances for both `BrainHsm64` and `BrainHsm128`.

**Memory Safety and the Universal Cognitive Bus** The unmanaged `BrainBlackboard` acts as the universal 128-byte cognitive bus. The engine respects the strict byte-offset layout: parameters are mapped to the first 60 bytes, the soft-advice region occupies bytes 60–125, and hardware interrupts utilize bytes 126–127. The `CognitiveInterruptSystem` implements edge-triggered capability loss detection by writing to `Interrupt_MobilityLost` at offset 126, which is safely zeroed at the end of the frame by the `CognitiveCleanupSystem`. Behaviors exceeding the 60-byte parameter limit properly isolate their memory into separate ECS components like `Blackboard1024`, preserving cache locality on the hot path.

**Unified AI Building Blocks and Source Generators** The `[SharedAiAction]`, `[SharedAiCondition]`, and their heavy variants are implemented in `Fbt.Kernel`. The Roslyn source generators, `Fbt.SourceGen` and `Fhsm.SourceGen`, compute deterministic memory offsets using the semantic model at compile time, eliminating magic strings and reflection overhead. The generators also capture the `[WritesChannel]` attribute to emit failure-reset wrappers and mandate exit-cleanups, mathematically preventing zombie actions during HSM state transitions.

**Unmanaged Bridging and Pure ECS Mutation** The architecture cleanly resolves the impedance mismatch between the generic FastHSM library and the FDP ECS by intentionally discarding the `HsmCommandWriter` to avoid competing mutation queues. The execution pipeline injects the `HsmKernelBridge`, which holds the unmanaged `GCHandle` target of the live `EntityRepository`, directly into the HSM context. This forces all structural mutations to route through FDP's native `EntityCommandBuffer` and ensures the ECS remains the singular authoritative source of truth.

how the engine now supports commander-subordinate relationships between entities?

The engine implements commander-subordinate relationships using a pure data-oriented Entity Component System (ECS) architecture, relying on strict separation between bottom-up persistent state and top-down derived state.

**Data Structures** The hierarchy is modeled using two unmanaged components:

-   `UnitSubordinate`: Assigned to child entities, this 12-byte component stores a generation-safe `Entity` handle to the commander alongside the subordinate's `TacticalDesignation` (e.g., Wingman, SquadLeader).-   `UnitRoster`: Assigned to commanding entities, this 168-byte component acts as a fixed-capacity inline array supporting up to 16 subordinates. To prevent conflicting sources of truth during serialization, `UnitRoster` is decorated with `[DataPolicy(DataPolicy.NoSave)]`. It is completely transient and is rebuilt dynamically from bottom-up `UnitSubordinate` records upon scenario load.

**Event-Driven Mutations** Structural ECS modifications are never performed directly by game logic. Instead, behaviors and external systems publish unmanaged `CmdAssignSubordinate` and `CmdRemoveSubordinate` events.

The `UnitHierarchySystem` processes these events during the simulation phase in a strict, deterministic sequence: it cascades destruction events, handles explicit removals, and finally applies new assignments. If a `CmdAssignSubordinate` request exceeds the commander's 16-slot capacity, the system safely aborts the operation and emits a `CmdAssignSubordinateRejected` event to unblock waiting AI executors. On a valid assignment, the system performs an atomic write to both the `UnitSubordinate` and `UnitRoster` components to guarantee referential integrity.

**Network and Scenario Genesis Lifecycle** To bridge the gap between volatile local memory pointers and distributed architecture, network boundaries utilize a deferred materialization pattern.

-   **Scenario Loading**: Relationships are instantiated as transient `InitialUnitSubordinateIntent` DTOs containing network IDs rather than memory handles. The `GenesisMaterializationSystem` polls these intents and executes the atomic ECS linkage only when both the commander and subordinate entities are confirmed alive in the local repository.-   **Live Network Ingress**: The `EntityInfoIngressTranslator` buffers incoming hierarchy updates into pending queues if either entity has not yet spawned locally. Furthermore, if the local node possesses ownership authority over the hierarchy descriptor, it strictly discards incoming payloads to prevent loopback corruption of the authoritative local ECS state.

I would like to design a group behavior for a hill attack, see the rough ideas in "initial-idea". To be compliant with FDP AI mechanisms and existing command-subordinate handling. Let's discuss it.

The proposed architecture in the initial idea introduces redundant components and custom channels that violate the current ECS and AI pipelines. We will refactor this to adhere strictly to the engine's CQRS boundaries, the universal cognitive bus, and the native hierarchical structures.

**1\. Commander-Subordinate Hierarchy** Discard the proposed `PlatoonMemberList` and `GroupMember` components. The engine natively enforces commander-subordinate relationships via two unmanaged ECS components: `UnitRoster` and `UnitSubordinate`. The `UnitRoster` component is assigned to the commanding entity and maintains a fixed-capacity inline array of up to 16 subordinate entity handles and their `TacticalDesignation`. The child tanks will carry the `UnitSubordinate` component, which stores a generation-safe handle back to the commander. The commander's BTree will simply query its own `UnitRoster` to iterate over available tanks.

**2\. Commander Behavior (PlatoonHillAttack\_BT)** Because the platoon commander executes a multi-phase, sequential planning routine, `FastBTree` is the correct paradigm.

-   **Heavy Data Allocation:** Computing a firing line and resolving spatial slots for multiple tanks requires significant working memory. Do not pollute the 128-byte `BrainBlackboard` or attempt to fit this into the strict 60-byte parameter limit. You must define a dedicated unmanaged ECS component (e.g., `HillAttackContext`) to store the computed firing positions, and access it within your BTree delegates using the `[SharedAiHeavyAction]` attribute.-   **Dispatching Orders:** Do not invent a custom `TacticalOrderChannel` or `TacticalOrderDispatcherSystem`. Top-down cognitive dispatch is already solved by `AssignTacticalIntentEvent`. The commander's BTree action iterates the `UnitRoster` and publishes this managed event for each tank, passing a generic string intent like `"HullDownAttack"` and the specific firing slot coordinates in the JSON parameters.

**3\. Intent Resolution and Ingress** To bridge the generic intent to a concrete tank behavior, implement an `ITacticalOrderMapper`. The `TacticalIntentResolutionSystem` will intercept the `"HullDownAttack"` intent, evaluate the entity's capabilities, and output an `AssignBehaviorEvent`. This guarantees the transition goes through the standard `BehaviorIngressSystem`, which atomically updates the tank's `BehaviorState`, resets its BTree execution pointer, and safely parses the incoming JSON parameters into the 60-byte blackboard memory.

**4\. Subordinate Tank Behavior (HullDownAttackRun\_BT)** The tank's execution loop is also heavily sequential and must run as a `FastBTree`. The nodes will write intents to the standard actuator channels:

-   **Movement:** `Action_MoveTo` writes to the `LocomotionChannel` to drive the tank to the assigned firing slot.-   **Target Acquisition:** The tank creeps forward under a sequence guarded by a `Condition_HasTarget` node. This node evaluates `NodeStatus.Success` by checking the entity's `TargetMemory` component to see if a valid threat is visible.-   **Engagement:** Once visible, `Action_AimAndFire` writes `ActionIdAimAndFire` to the `WeaponChannel`, relying on the `WeaponDispatcherSystem` (Muscle tier) to handle the physical execution. Ensure this action is decorated with `[WritesChannel(ChannelKind.Weapon)]` so the Roslyn source generator emits the failure-reset wrappers, mathematically preventing zombie firing actions when the branch aborts.-   **Retreat:** The tank writes a reverse intent to the `LocomotionChannel` to return to cover.

**5\. Completion Feedback Loop** You do not need a custom `TacticalReport` component ticking every frame. The cognitive tier is built to publish a `BehaviorFinishedEvent` exactly once when a BTree root evaluates to a terminal state (`NodeStatus.Success` or `NodeStatus.Failure`). The platoon commander can observe these events, or query the `BehaviorState` of the entities in its `UnitRoster`, to deterministically track the progress of the attack run and trigger the next tactical phase.

instead of a new specific compoent like HillAttackContext i want to define just HillAttackBlackboardDto and use it with standard BrainBlackboard1024 component (to avoid consuming precious component id space - max 256)

Using `Blackboard1024` is the correct architectural decision to conserve the strict 256 component-type limit. The architecture provides the `Blackboard1024` component precisely for heavy, behavior-specific payloads like pre-computed firing lines or spatial slots.

To integrate this cleanly, you define a dedicated unmanaged struct DTO and project the raw bytes of `Blackboard1024.Memory` into it using the 5-argument form of the `[SharedAiHeavyAction]` attribute. The source generator will automatically emit a `GetComponentRW<Blackboard1024>` call followed by an `Unsafe.As` cast, giving your domain logic a zero-copy `ref` to the heavy data.

Here is how you define and wire the DTOs and actions:

```
// 1. Define the heavy unmanaged DTO
[StructLayout(LayoutKind.Sequential)]
public struct HillAttackBlackboardDto
{
    public int CalculatedSlotsCount;
    // Ensure this struct does not exceed 1024 bytes
    public fixed float FiringLineX[8];
    public fixed float FiringLineY[8];
}

// 2. Define the minimal parameter DTO for the 128-byte bus
[StructLayout(LayoutKind.Sequential)]
public struct HillAttackParams
{
    public float EngagementDistance;
}

[StructLayout(LayoutKind.Sequential)]
public struct HillAttackBlackboard
{
    public HillAttackParams Params;
}

// 3. Write the Action using the 5-argument SharedAiHeavyAction attribute
[SharedAiHeavyAction(
    typeof(HillAttackBlackboard), 
    nameof(HillAttackBlackboard.Params), 
    typeof(Blackboard1024), 
    nameof(Blackboard1024.Memory), 
    typeof(HillAttackBlackboardDto))]
public static NodeStatus Action_CalculateFiringLine(
    ref HillAttackParams minimalParams, 
    ref HillAttackBlackboardDto heavyData, 
    Entity self, 
    EntityRepository repo)
{
    // Write directly to heavyData; the memory maps to the Blackboard1024 component chunk.
    heavyData.CalculatedSlotsCount = 4;
    return NodeStatus.Success;
}
```

When registering the behavior, you must assign `HeavyDtoType` in the `BehaviorDefinition` so the `Blackboard1024Renderer` and entity inspector can project the raw bytes at runtime for debugging.

```
registry.Register(HillAttackBehaviorId, "HillAttack", new BehaviorDefinition
{
    Name = "HillAttack",
    BrainTier = BehaviorConstants.BrainTierBTree,
    BTreeInterpreter = interpreter,
    ParamsDtoType = typeof(HillAttackParams),
    HeavyDtoType = typeof(HillAttackBlackboardDto)
});
```

Finally, ensure the commander entity's TKB blueprint includes the `Blackboard1024` component alongside the standard `BrainBlackboard` so the memory space exists on the entity.

If there are more than 3 tanks, they attack in pairs - two attack waves - first tanks (1,3) then tanks (2, 4)

To enforce wave-based attack sequencing within the pure ECS boundaries, we must expand the `Blackboard1024` unmanaged payload to track wave state and partition the `UnitRoster` during the dispatch phase. You must not introduce managed collections, lists, or custom state-tracking components.

The `UnitRoster` on the commander guarantees a strict maximum of 16 subordinates, preserving insertion order. This constraint makes a 16-bit mask mathematically perfect for tracking the execution state of child entities without heap allocations.

**1\. Expand the Heavy DTO** Modify the unmanaged `HillAttackBlackboardDto` to track the current wave index and a bitmask of the tanks currently executing the attack run.

```
[StructLayout(LayoutKind.Sequential)]
public struct HillAttackBlackboardDto
{
    public int CalculatedSlotsCount;
    public byte CurrentWave;          // 0 or 1
    public ushort ActiveAttackersMask; // Bitmask mapping to UnitRoster indices
    // Pre-computed spatial slots
    public fixed float FiringLineX[3];
    public fixed float FiringLineY[3];
}
```

**2\. Wave Dispatch Action** Implement a BTree action to partition the roster and dispatch the tactical intents. You read the commander's `UnitRoster` and iterate up to its `Count`.

If `roster.Count > 3`, partition the subordinates using a modulo operator against the roster index: `i % 2 == CurrentWave`. For each matching subordinate, set the corresponding bit `(1 << i)` in `ActiveAttackersMask`. Calculate the target firing slot, and publish an `AssignTacticalIntentEvent` with the `"HullDownAttack"` payload to the subordinate. If `roster.Count <= 3`, dispatch the intent to all valid subordinates and set all their respective bits in the mask.

**3\. Wave Completion Evaluation** Because `FastBTree` is a polling interpreter that executes every frame, the commander must evaluate the progress of the dispatched wave using a condition node.

Define a `Condition_IsWaveCompleted` delegate using `[SharedAiHeavyCondition]`. This method iterates the `UnitRoster` and checks the `ActiveAttackersMask`. For each set bit, query the child entity's `BehaviorState` from the `EntityRepository`. If the child has transitioned out of the attack behavior (e.g., returned to an Idle hash) or the entity is no longer alive, clear that bit using bitwise AND NOT `&= ~(1 << i)`.

When `ActiveAttackersMask == 0`, the wave is finished, and the condition returns `NodeStatus.Success`. If bits remain set, it returns `NodeStatus.Running` to block the BTree sequence.

**4\. BTree Topology** The commander's compiled BTree will use a `Sequence` to alternate the waves and wait for completion.

```
{
  "Type": "Sequence",
  "Children": [
    { "Type": "Action", "Action": "Action_CalculateFiringLine" },
    
    // Wave 0
    { "Type": "Action", "Action": "Action_SetWaveZero" },
    { "Type": "Action", "Action": "Action_DispatchWave" },
    { "Type": "Condition", "Action": "Condition_IsWaveCompleted" }, // Blocks until Success
    
    // Wave 1
    { "Type": "Action", "Action": "Action_SetWaveOne" },
    { "Type": "Action", "Action": "Action_DispatchWave" },
    { "Type": "Condition", "Action": "Condition_IsWaveCompleted" }
  ]
}
```

This design strictly isolates working memory in the `Blackboard1024` chunk, relies entirely on ECS component queries for state verification, and safely delegates structural behavior assignment to the `TacticalIntentResolutionSystem` and `BehaviorIngressSystem` without bypassing the engine's CQRS boundaries.

The slots coordinates are rougly precomputed along the rough firing line (actually a segment, not infinite) definition from behavior parameters, using some fixed spacing between tanks. The slots for each attack wave must be selected randomly from these precomputed slots to make the tank location unpredictable. All stots share the same attack direction (perpendicular to the rough firing line). When attackin, tank needs to drive towards the slot, when close, it switches to creeping mode and start checking if target is already visible and keep going until target in sight. Tank can overshoot the slot position because it is rough only. But it should drive trhough it (unless target spotted earlier) in given direction until target visible.

The design requires coordinating spatial math in the commander's heavy memory and leveraging the inherent execution state of the `FastBTree` interpreter to manage the tank's creeping logic.

Here is the architectural implementation for the precomputed line, randomized slot assignment, and the sequence-selector BTree topology required for the subordinate tanks.

**1\. Commander: Firing Line and Slot Precomputation** The commander must precompute the exact spatial slots and the shared attack direction once, storing them in the `Blackboard1024` component to avoid polluting the 128-byte `BrainBlackboard`.

Expand the unmanaged heavy DTO to store the geometry and a tracking bitmask for random assignment:

```
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HillAttackBlackboardDto
{
    public int CalculatedSlotsCount;
    public byte CurrentWave;
    public ushort ActiveAttackersMask;
    
    // Shared attack direction (perpendicular to the firing line)
    public float AttackDirX;
    public float AttackDirY;
    
    // Pre-computed spatial slots along the line
    public fixed float SlotX[3];
    public fixed float SlotY[3];
    
    // Tracks which slots have been assigned in the current wave
    public ushort UsedSlotsMask; 
}
```

In `Action_CalculateFiringLine`, you compute the segment vector from the behavior parameters, derive the perpendicular vector for `AttackDirX/Y`, and populate the `SlotX` and `SlotY` arrays.

**2\. Commander: Randomized Wave Dispatch** During `Action_DispatchWave`, you iterate the `UnitRoster` to dispatch the current wave. For each active tank, select a random bit from `~UsedSlotsMask` (limited by `CalculatedSlotsCount`). Mark the bit as used, read the corresponding `SlotX` and `SlotY`, and serialize the payload.

You publish the `AssignTacticalIntentEvent` for each subordinate, passing the target slot and the shared attack direction in the JSON parameters.

**3\. Tank: Behavior Parameters and Ingress** The tank's behavior definition must map the incoming JSON into an unmanaged DTO that fits within the first 60 bytes of the `BrainBlackboard`.

```
[StructLayout(LayoutKind.Sequential)]
public struct HullDownAttackParams
{
    public float SlotX;
    public float SlotY;
    public float AttackDirX;
    public float AttackDirY;
    public float ApproachSpeed;
    public float CreepSpeed;
}
```

**4\. Tank: BTree Topology for Creeping and Engagement** The tank's behavior must drive to the slot, creep forward until the target is visible, shoot, and retreat. The `FastBTree` interpreter inherently supports this via a `Sequence` containing a blocking `Selector`.

```
{
  "Type": "Sequence",
  "Children": [
    {
      "Type": "Selector",
      "Children": [
        { "Type": "Condition", "Action": "Condition_HasTarget" },
        { "Type": "Action",    "Action": "Action_CreepToAndBeyondSlot" }
      ]
    },
    { "Type": "Action", "Action": "Action_AimAndFire" },
    { "Type": "Action", "Action": "Action_ReverseToCover" }
  ]
}
```

_Execution Flow:_

-   The BTree evaluates the `Selector`. It first checks `Condition_HasTarget`.-   If the target is not visible, it evaluates `Action_CreepToAndBeyondSlot`, which returns `NodeStatus.Running`. The tree halts evaluation for this frame, persisting its execution pointer in `BrainBTreeState`.-   The moment `Condition_HasTarget` returns `NodeStatus.Success` (e.g., the target enters the FOV), the `Selector` short-circuits and returns `Success`.-   The `Sequence` advances to `Action_AimAndFire`. Because `Action_CreepToAndBeyondSlot` is decorated with `[WritesChannel(ChannelKind.Locomotion)]`, the compiler-generated preemption wrapper automatically resets the `LocomotionChannel` when the action is aborted by the advancing sequence.

**5\. Tank: Creep Action Logic** The creeping action computes a dynamic destination based on distance and writes a standard `MoveTo` intent to the `LocomotionChannel`. It never returns `Success`—it yields `Running` indefinitely, relying entirely on the parent `Selector` to interrupt it when the target is spotted.

```
[SharedAiAction]
[WritesChannel(ChannelKind.Locomotion)]
public static NodeStatus Action_CreepToAndBeyondSlot(
    ref HullDownAttackParams p, 
    Entity self, 
    EntityRepository repo)
{
    ref readonly var tf = ref repo.GetComponentRO<SimTransform>(self);
    var currentPos = new Vector2(tf.Position.X, tf.Position.Y);
    var slotPos = new Vector2(p.SlotX, p.SlotY);
    var attackDir = new Vector2(p.AttackDirX, p.AttackDirY);

    float distanceToSlot = Vector2.Distance(currentPos, slotPos);
    
    ref var loco = ref repo.GetComponentRW<LocomotionChannel>(self);
    
    // We only write to the channel if the state needs to change, preventing zombie resets.
    bool needsUpdate = loco.ActiveAction != NavigationConstants.ActionIdMoveTo || loco.Status == NodeStatus.Failure;

    if (distanceToSlot > 5.0f)
    {
        // Phase 1: Approach the rough slot
        if (needsUpdate)
        {
            loco.ActiveAction = NavigationConstants.ActionIdMoveTo;
            WriteToLocomotionParams(ref loco, new MoveToParams { 
                Destination = slotPos, 
                Speed = p.ApproachSpeed 
            });
            unchecked { loco.ActionInstanceId++; }
        }
    }
    else
    {
        // Phase 2: Overshoot and creep along the attack direction
        if (needsUpdate)
        {
            // Project a point far along the attack vector
            Vector2 infiniteTarget = currentPos + (attackDir * 1000f);
            
            loco.ActiveAction = NavigationConstants.ActionIdMoveTo;
            WriteToLocomotionParams(ref loco, new MoveToParams { 
                Destination = infiniteTarget, 
                Speed = p.CreepSpeed 
            });
            unchecked { loco.ActionInstanceId++; }
        }
    }

    return NodeStatus.Running;
}
```

This design strictly enforces the CQRS boundary. The cognitive tier manages spatial intent and delegates physical locomotion directly to the muscle tier without managing custom trajectories or circumventing the established dispatcher systems.

Because slots are on a line degment, one float for relative normalizef position on line segment can define the slot position precisely. Randimization can simply pick random slot index, while maintaining minimal defined distance between each two adjacent slots. No need for storing (x,y) precomputed coords.

Reducing the spatial slots to a 1D scalar parameterization along the line segment is the mathematically correct optimization. It eliminates the need for inline arrays in the working memory and defers absolute coordinate resolution to the exact moment of dispatch.

To implement this within the architecture, we strictly separate the static geometry configuration from the mutable tracking state.

**1\. Static Parameter Definition** The firing line definition and spacing configurations are static per assignment and fit safely within the 60-byte parameter region of the `BrainBlackboard`. Define the unmanaged parameter struct to hold the line segment:

```
[StructLayout(LayoutKind.Sequential)]
public struct PlatoonHillAttackParams
{
    public float StartX;
    public float StartY;
    public float EndX;
    public float EndY;
    public float AttackDirX;
    public float AttackDirY;
    public float TankSpacing;
}
```

**2\. Slim the Mutable State DTO** The mutable tracking state remains mapped to the `Blackboard1024` component chunk, but we strip out the `fixed float` arrays. This struct now only manages the bitmasks necessary for wave execution and randomization.

```
[StructLayout(LayoutKind.Sequential)]
public struct HillAttackMutableState
{
    public int TotalSlots;
    public ushort ActiveAttackersMask;
    public ushort UsedSlotsMask;
    public byte CurrentWave;
}
```

**3\. Just-In-Time Coordinate Computation** In the commander's `Action_CalculateFiringLine`, you compute `TotalSlots` by dividing the segment length by `TankSpacing`, initializing `UsedSlotsMask` to 0.

During `Action_DispatchWave`, you iterate the `UnitRoster` to dispatch the current wave. For each subordinate:

-   Select a random available bit index from `~UsedSlotsMask` (constrained by `TotalSlots`).-   Mark the bit as used in the mask.-   Compute the normalized position: `float t = slotIndex / (float)(TotalSlots - 1)`.-   Interpolate the exact 2D world coordinate: `SlotX = Lerp(StartX, EndX, t)` and `SlotY = Lerp(StartY, EndY, t)`.

The commander then publishes the `AssignTacticalIntentEvent` for the child entity, serializing the computed `SlotX`, `SlotY`, `AttackDirX`, and `AttackDirY` into the JSON payload.

**4\. Subordinate Integrity** The subordinate tank's `HullDownAttackParams` requires zero architectural changes. The `BehaviorIngressSystem` will parse the absolute `(X,Y)` slot coordinates and the attack direction directly into the tank's 60-byte parameter bus. The tank remains completely decoupled from the platoon's 1D segment parameterization, ensuring its creeping attack behavior remains reusable if assigned by a completely different tactical system.

I need you to use fluent btree definitions, not the json one.

Defining behaviors via the fluent `BTreeBuilder` DSL is the correct architectural choice. It completely eliminates magic strings and runtime JSON parsing, allowing the Roslyn source generators to compute deterministic memory offsets for your blackboard parameters at compile time.

Here is the pure C# implementation for the hill attack group behavior, adhering strictly to the data-oriented paradigms and the 1D segment parameterization we established.

1\. Blackboard Wrappers

To use the fluent builder's expression-bound methods, you must define the minimal wrapper structs that enforce the sequential memory layout of the 128-byte `BrainBlackboard`.

```
[StructLayout(LayoutKind.Sequential)]
public struct PlatoonHillAttackBlackboard
{
    public PlatoonHillAttackParams Params;
}

[StructLayout(LayoutKind.Sequential)]
public struct HullDownAttackBlackboard
{
    public HullDownAttackParams Params;
}
```

2\. Platoon Commander Fluent BTree

The commander's BTree uses a sequence to alternate between waves and block execution until the dispatched wave is completed. You define this using a static factory method decorated with `[BTreeDefinition]`.

Notice that the expression lambda (`bb => bb.Params`) resolves the offset for the 60-byte parameter region. Access to the expanded `Blackboard1024` state (where your active wave mask and random seeds live) is handled purely inside the node delegates via the `[SharedAiHeavyAction]` signatures we discussed earlier, keeping the tree definition clean.

```
[BTreeDefinition("PlatoonHillAttack")]
public static BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext> BuildPlatoonHillAttackTree()
{
    return new BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext>()
        .Sequence(seq => seq
            // Precompute TotalSlots from the 1D segment parameters
            .Action(bb => bb.Params, Action_CalculateFiringLine)
            
            // Wave 0
            .Action(bb => bb.Params, Action_SetWaveZero)
            .Action(bb => bb.Params, Action_DispatchWave)
            .Condition(bb => bb.Params, Condition_IsWaveCompleted)
            
            // Wave 1
            .Action(bb => bb.Params, Action_SetWaveOne)
            .Action(bb => bb.Params, Action_DispatchWave)
            .Condition(bb => bb.Params, Condition_IsWaveCompleted)
        );
}
```

3\. Subordinate Tank Fluent BTree

The tank relies on a `Selector` to interrupt the creeping locomotion action as soon as the target enters its field of view.

```
[BTreeDefinition("HullDownAttackRun")]
public static BTreeBuilder<HullDownAttackBlackboard, BTreeContext> BuildHullDownAttackRunTree()
{
    return new BTreeBuilder<HullDownAttackBlackboard, BTreeContext>()
        .Sequence(seq => seq
            .Selector(sel => sel
                // Short-circuits the selector and halts creeping the moment the target is spotted
                .Condition(bb => bb.Params, Condition_HasTarget)
                
                // Creep action returns NodeStatus.Running to persist across frames
                .Action(bb => bb.Params, Action_CreepToAndBeyondSlot)
            )
            // Sequence advances here once the target is visible
            .Action(bb => bb.Params, Action_AimAndFire)
            
            // Retreat after engagement
            .Action(bb => bb.Params, Action_ReverseToCover)
        );
}
```

4\. Zero-Cost Registration

Because you decorated these methods with `[BTreeDefinition]`, the `Fbt.SourceGen` compiler intercepts them during the build process. It will automatically emit a catalog file (`FbtTreeCatalog.g.cs`) containing `GetPlatoonHillAttack()` and `GetHullDownAttackRun()`.

When you register your behavior in the engine, you simply call the generated catalog method to retrieve the compiled `BehaviorTreeBlob` without incurring any runtime reflection or tree construction costs:

```
registry.Register(HillAttackBehaviorId, "PlatoonHillAttack", new BehaviorDefinition
{
    Name = "PlatoonHillAttack",
    BrainTier = BehaviorConstants.BrainTierBTree,
    BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
        FbtTreeCatalog.GetPlatoonHillAttack(), 
        actionRegistry),
    ParamsDtoType = typeof(PlatoonHillAttackParams),
    HeavyDtoType = typeof(HillAttackMutableState) // Projects the Blackboard1024 raw bytes for debugging
});
```

The waves must continue until target destroyed. Target defined as area entity id. All enemy emtities inside this area must be destroyed. Attacking tanks receive their concrete target entity at the beginning of the wave, from the platoon commander. The tanks from a single wave should not fire at the same target unless there are less targets than tanks in the wave.

To fulfill these requirements within the strict Data-Oriented Design (DOD) constraints of the FDP engine, we must extend the commander's cognitive working memory, utilize the `Repeater` BTree node to drive infinite wave loops until termination conditions are met, and implement a deterministic target-distribution algorithm over the `TargetMemory` component.

Here is the architectural implementation.

1\. Commander: Expanding the Heavy State and Parameters

The target area is an ECS entity carrying the geographic definition (e.g., an `EditablePolyline`). You pass this entity handle directly into the `PlatoonHillAttackParams`. The unmanaged `HillAttackMutableState` stored in `Blackboard1024` remains lightweight, managing only the wave toggles and active tracker masks.

```
[StructLayout(LayoutKind.Sequential)]
public struct PlatoonHillAttackParams
{
    public float StartX;
    public float StartY;
    public float EndX;
    public float EndY;
    public float AttackDirX;
    public float AttackDirY;
    public float TankSpacing;
    public Entity TargetAreaEntity; 
}

[StructLayout(LayoutKind.Sequential)]
public struct HillAttackMutableState
{
    public int TotalSlots;
    public ushort ActiveAttackersMask;
    public ushort UsedSlotsMask;
    public byte CurrentWave; // 0 or 1
}
```

2\. Commander: Area Target Resolution and Round-Robin Distribution

The commander must isolate valid enemies within the area boundary and distribute them to the subordinates. To prevent scanning the entire world, the commander queries its own `TargetMemory` component (which is maintained organically by the perception tier).

Implement the `Action_DispatchWaveWithTargets` delegate. You extract valid targets into a stack-allocated buffer and distribute them to the active wave using modulo arithmetic, which mathematically guarantees tanks will not share a target unless targets are fewer than the attacking tanks.

```
[SharedAiHeavyAction(..., typeof(HillAttackMutableState))]
public static NodeStatus Action_DispatchWaveWithTargets(
    ref PlatoonHillAttackParams p, 
    ref HillAttackMutableState state, 
    Entity self, 
    EntityRepository repo)
{
    // 1. Gather valid targets inside the area
    ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(self);
    var polyline = repo.GetManagedComponentRO<EditablePolyline>(p.TargetAreaEntity);
    
    Span<Entity> validTargets = stackalloc Entity[PerceptionConstants.MaxTrackedTargets];
    int targetCount = 0;

    for (int i = 0; i < mem.Count; i++)
    {
        if (mem.ThreatScores[i] > 0f)
        {
            var targetPos = new Vector2(mem.PositionsX[i], mem.PositionsY[i]);
            if (IsPointInPolygon(targetPos, polyline.Points)) // Simple point-in-polygon math
            {
                validTargets[targetCount++] = new Entity((ulong)mem.EntityIds[i]);
            }
        }
    }

    if (targetCount == 0) return NodeStatus.Failure;

    // 2. Dispatch wave with target distribution
    ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(self);
    int activeTankIndex = 0;

    for (int i = 0; i < roster.Count; i++)
    {
        if (i % 2 != state.CurrentWave) continue;

        Entity subordinate = new Entity((ulong)roster.SubordinateEntities[i]);
        if (!repo.IsAlive(subordinate)) continue;

        // Assign target deterministically
        Entity assignedTarget = validTargets[activeTankIndex % targetCount];
        activeTankIndex++;

        state.ActiveAttackersMask |= (ushort)(1 << i);
        
        // JIT compute spatial slot coordinates as established previously
        // ...
        
        // Dispatch tactical intent with the assigned Target entity included in JSON
        repo.Bus.PublishManaged(new AssignTacticalIntentEvent
        {
            Entity = subordinate,
            IntentId = "HullDownAttack",
            JsonParams = $"{{ \"SlotX\": {slotX}, \"SlotY\": {slotY}, \"TargetEntityPacked\": {(long)assignedTarget.PackedValue} }}"
        });
    }

    // Toggle wave for the next execution
    state.CurrentWave = (byte)(1 - state.CurrentWave);
    return NodeStatus.Success;
}
```

3\. Commander: BTree Topology for Infinite Waves

You do not use custom looping states. You rely on FastBTree's `Repeater` node configured for infinite loops (`-1`).

If `Condition_HasTargetsInArea` finds 0 targets, it returns `NodeStatus.Failure`. The `Repeater` aborts on child failure and returns `Failure` to the root. `BTreeTickSystem` detects this terminal state and publishes a `BehaviorFinishedEvent`, natively notifying the Mission layer that the hill attack doctrine has successfully concluded due to target elimination.

```
[BTreeDefinition("PlatoonHillAttack")]
public static BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext> BuildPlatoonHillAttackTree()
{
    return new BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext>()
        .Sequence(seq => seq
            .Action(bb => bb.Params, Action_CalculateFiringLine)
            
            // Loop waves indefinitely until the area is cleared
            .Repeater(-1, rep => rep 
                .Sequence(waveSeq => waveSeq
                    // Fails when 0 targets remain, breaking the Repeater and finishing the behavior
                    .Condition(bb => bb.Params, Condition_HasTargetsInArea) 
                    
                    .Action(bb => bb.Params, Action_DispatchWaveWithTargets)
                    .Condition(bb => bb.Params, Condition_IsWaveCompleted)
                )
            )
        );
}
```

4\. Subordinate Tank: Executing the Assigned Target

The subordinate tank's `HullDownAttackParams` now accepts `public long TargetEntityPacked`.

Its `Selector` block updates to watch for this specific entity instead of scanning generically. The tank crawls forward; when `TargetMemory` registers a threat score for the specific `TargetEntityPacked` assignment, `Condition_IsAssignedTargetVisible` yields `Success`, short-circuiting the creep and allowing the sequence to advance to the engagement phase.

```
[SharedAiAction]
public static NodeStatus Action_AimAndFireSpecific(
    ref HullDownAttackParams p, 
    Entity self, 
    EntityRepository repo)
{
    ref var channel = ref repo.GetComponentRW<WeaponChannel>(self);
    
    bool needsUpdate = channel.ActiveAction != CombatConstants.ActionIdAimAndFire || channel.Status == NodeStatus.Failure;
    
    if (needsUpdate)
    {
        // Pack exact target entity to bypass target acquisition logic in the muscle tier
        WriteToWeaponParams(ref channel, new AimAndFireParams { 
            Target = new Entity((ulong)p.TargetEntityPacked), 
            CooldownSeconds = 0f 
        });
        unchecked { channel.ActionInstanceId++; }
        channel.ActiveAction = CombatConstants.ActionIdAimAndFire;
    }
    
    return NodeStatus.Running;
}
```

This ensures zero network knowledge in the cognitive nodes, pure ECS reads across the perception layer's `TargetMemory`, and complete reliance on the existing `ChannelArbitrationSystem` and BTree semantics for state control.

The tanks might get destroyed when attacking because of enemy fire. Destroyed tank blocks the slot so it can not be reused.

The requirement to block spatial slots upon tank destruction exposes a critical architectural edge case in how the FDP engine manages hierarchical state.

Because `UnitHierarchySystem` automatically intercepts entity destruction and performs an order-preserving left-shift on the commander's `UnitRoster`, any tracking logic that maps spatial slots to a roster index will become corrupted the moment a tank dies.

To solve this while adhering to zero-allocation Data-Oriented Design constraints, we must decouple wave tracking from the `UnitRoster`. We will implement a Struct-of-Arrays (SoA) tracker directly inside the `Blackboard1024` component. This guarantees stable tracking of which entity owns which slot, allowing us to permanently burn slots when `EntityRepository.IsAlive` returns false.

1\. Refined Heavy Mutable State

Modify the unmanaged mutable state to use SoA tracking. We maintain a `BurnedSlotsMask` for slots permanently lost to wrecks, and a `WaveUsedSlotsMask` to prevent duplicate assignments during the current wave.

```
[StructLayout(LayoutKind.Sequential)]
public unsafe struct HillAttackMutableState
{
    public int TotalSlots;
    public byte CurrentWave;
    
    // Slot availability tracking
    public ushort BurnedSlotsMask;     // Slots permanently blocked by wrecks
    public ushort WaveUsedSlotsMask;   // Slots currently occupied in this wave
    
    // SoA tracking decoupled from the volatile UnitRoster
    public int ActiveAttackerCount;
    public fixed long ActiveEntityPacked[5];
    public fixed byte ActiveSlotIndex[5];
}
```

2\. Dispatch with Decoupled Tracking

In `Action_DispatchWaveWithTargets`, we clear the `WaveUsedSlotsMask` at the start of the wave. When iterating the `UnitRoster` to dispatch tanks, we select a slot strictly from the available bitmask `~(state.BurnedSlotsMask | state.WaveUsedSlotsMask)`. We then store the assigned entity and slot in our stable SoA arrays.

```
[SharedAiHeavyAction(..., typeof(HillAttackMutableState))]
public static unsafe NodeStatus Action_DispatchWaveWithTargets(
    ref PlatoonHillAttackParams p, 
    ref HillAttackMutableState state, 
    Entity self, 
    EntityRepository repo)
{
    // Gather targets...
    // ...

    state.WaveUsedSlotsMask = 0;
    state.ActiveAttackerCount = 0;
    
    ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(self);
    
    for (int i = 0; i < roster.Count; i++)
    {
        if (i % 2 != state.CurrentWave) continue;

        Entity subordinate = new Entity((ulong)roster.SubordinateEntities[i]);
        if (!repo.IsAlive(subordinate)) continue;

        // Find available slot using bitwise operations
        ushort blockedMask = (ushort)(state.BurnedSlotsMask | state.WaveUsedSlotsMask);
        int slotIndex = GetRandomAvailableSlot(blockedMask, state.TotalSlots);
        if (slotIndex < 0) continue; // No slots available

        // Record tracking data decoupled from roster index
        int activeIdx = state.ActiveAttackerCount++;
        state.ActiveEntityPacked[activeIdx] = (long)subordinate.PackedValue;
        state.ActiveSlotIndex[activeIdx] = (byte)slotIndex;
        
        state.WaveUsedSlotsMask |= (ushort)(1 << slotIndex);
        
        // JIT compute absolute coordinates and dispatch intent
        // ...
    }

    state.CurrentWave = (byte)(1 - state.CurrentWave);
    return NodeStatus.Success;
}
```

3\. Verification and Slot Burning

In `Condition_IsWaveCompleted`, we iterate the SoA tracker backwards. This allows us to perform an O(1) swap-remove when a tank finishes its run or is destroyed.

If the tank is dead, we permanently update the `BurnedSlotsMask`. Because we rely purely on `repo.IsAlive()`, we do not need to subscribe to managed destruction events, preserving the CQRS separation.

```
[SharedAiHeavyCondition(..., typeof(HillAttackMutableState))]
public static unsafe bool Condition_IsWaveCompleted(
    ref PlatoonHillAttackParams p, 
    ref HillAttackMutableState state, 
    Entity self, 
    EntityRepository repo)
{
    // Iterate backwards for safe O(1) swap-removal
    for (int i = state.ActiveAttackerCount - 1; i >= 0; i--)
    {
        Entity attacker = new Entity((ulong)state.ActiveEntityPacked[i]);
        byte slotIndex = state.ActiveSlotIndex[i];

        if (!repo.IsAlive(attacker))
        {
            // Tank destroyed: permanently burn the slot
            state.BurnedSlotsMask |= (ushort)(1 << slotIndex);
            SwapRemoveActiveAttacker(ref state, i);
        }
        else
        {
            // Tank alive: query its BehaviorState to see if it returned to Idle/finished
            ref readonly var behavior = ref repo.GetComponentRO<BehaviorState>(attacker);
            if (behavior.ActiveBehaviorHash != "HullDownAttack".GetHashCode()) // Or exact registry ID
            {
                SwapRemoveActiveAttacker(ref state, i);
            }
        }
    }

    return state.ActiveAttackerCount == 0;
}

private static unsafe void SwapRemoveActiveAttacker(ref HillAttackMutableState state, int index)
{
    int lastIdx = state.ActiveAttackerCount - 1;
    if (index != lastIdx)
    {
        state.ActiveEntityPacked[index] = state.ActiveEntityPacked[lastIdx];
        state.ActiveSlotIndex[index] = state.ActiveSlotIndex[lastIdx];
    }
    state.ActiveAttackerCount--;
}
```

This implementation strictly isolates the cognitive memory in the unmanaged 1024-byte block, prevents `UnitRoster` compaction bugs, and evaluates ECS structural liveness natively without invoking the event bus.

From the firing line the tanks need to return (reverse driving) to a baseline slot farther down under the hills crest. This baseline is also defined as behavior parameter. At the beginnin of the hill attack, the slots on this baseline should be precomputed and the tank platoon instructed to move there. Tanks should return the closest available baseline slot not reserved for other tank. Return slot is assigned with attack slot by the commander at the beginning of each wave.

To support the baseline retreat requirement while adhering to the FDP engine's strict Data-Oriented Design (DOD) constraints, we must extend the commander's 1D segment parameterization to include the baseline geometry. We will also introduce an explicit initialization phase in the commander's BTree to deploy the platoon to the baseline before the wave loop begins.

1\. Extend the Parameter and State DTOs

Define the baseline mathematically as a second line segment parallel to the firing line. This avoids storing absolute coordinate arrays and allows Just-In-Time (JIT) interpolation.

Update the commander's unmanaged parameters and the `Blackboard1024` SoA tracker. We must add a `BaselineReservedMask` to guarantee mutually exclusive retreat slot assignments.

```
[StructLayout(LayoutKind.Sequential)]
public struct PlatoonHillAttackParams
{
    // Firing line segment
    public float StartX; public float StartY;
    public float EndX; public float EndY;
    // Baseline segment
    public float BaselineStartX; public float BaselineStartY;
    public float BaselineEndX; public float BaselineEndY;
    
    public float AttackDirX; public float AttackDirY;
    public float TankSpacing;
    public Entity TargetAreaEntity; 
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct HillAttackMutableState
{
    public int TotalSlots;
    public byte CurrentWave;
    
    public ushort BurnedSlotsMask;     
    public ushort WaveUsedSlotsMask;   
    public ushort BaselineReservedMask; // Tracks occupied baseline slots
    
    public int ActiveAttackerCount;
    public fixed long ActiveEntityPacked[4];
    public fixed byte ActiveSlotIndex[4];
    public fixed byte ReturnBaselineSlotIndex[4]; // Links the tank to its designated return slot
}
```

2\. Commander: Initial Baseline Deployment

Before any attack waves commence, the commander must dispatch all subordinates to the baseline. Add an initial preparation sequence to the fluent BTree definition.

```
[BTreeDefinition("PlatoonHillAttack")]
public static BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext> BuildPlatoonHillAttackTree()
{
    return new BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext>()
        .Sequence(seq => seq
            .Action(bb => bb.Params, Action_CalculateSegments)
            
            // Initial deployment phase
            .Action(bb => bb.Params, Action_DispatchAllToBaseline)
            .Condition(bb => bb.Params, Condition_AreAllAtBaseline) // Blocks until all reach the baseline
            
            // Attack loop
            .Repeater(-1, rep => rep 
                .Sequence(waveSeq => waveSeq
                    .Condition(bb => bb.Params, Condition_HasTargetsInArea) 
                    .Action(bb => bb.Params, Action_DispatchWaveWithTargets)
                    .Condition(bb => bb.Params, Condition_IsWaveCompleted)
                )
            )
        );
}
```

In `Action_DispatchAllToBaseline`, iterate the `UnitRoster`, JIT compute a baseline slot for each tank, reserve the bit in `BaselineReservedMask`, and dispatch a basic `"MoveToLocation"` intent. `Condition_AreAllAtBaseline` evaluates `NodeStatus.Success` only when all entities report `NavState.HasArrived == 1`.

3\. Commander: Spatial Closest-Slot Resolution

During `Action_DispatchWaveWithTargets`, after selecting the firing slot `F`, the commander must identify the closest unreserved baseline slot `B` in O(N) time without heap allocations.

```
// Inside Action_DispatchWaveWithTargets (per tank)

// 1. Un-reserve the tank's CURRENT baseline slot so it can be evaluated or freed
state.BaselineReservedMask &= ~(ushort)(1 << currentBaselineSlotOfTank);

// 2. Compute Firing Slot (F)
float tF = firingSlotIndex / (float)(state.TotalSlots - 1);
Vector2 firingPos = new Vector2(
    Lerp(p.StartX, p.EndX, tF), 
    Lerp(p.StartY, p.EndY, tF));

// 3. Find closest available baseline slot
int bestBaselineIdx = -1;
float minDistanceSq = float.MaxValue;

for (int j = 0; j < state.TotalSlots; j++)
{
    if ((state.BaselineReservedMask & (1 << j)) != 0) continue; // Slot reserved by another tank

    float tB = j / (float)(state.TotalSlots - 1);
    Vector2 candidatePos = new Vector2(
        Lerp(p.BaselineStartX, p.BaselineEndX, tB), 
        Lerp(p.BaselineStartY, p.BaselineEndY, tB));
        
    float distSq = Vector2.DistanceSquared(firingPos, candidatePos);
    if (distSq < minDistanceSq)
    {
        minDistanceSq = distSq;
        bestBaselineIdx = j;
    }
}

// 4. Reserve and record the chosen baseline slot
state.BaselineReservedMask |= (ushort)(1 << bestBaselineIdx);
state.ReturnBaselineSlotIndex[activeIdx] = (byte)bestBaselineIdx;

// 5. Compute absolute coordinates for the chosen baseline slot
float tFinalB = bestBaselineIdx / (float)(state.TotalSlots - 1);
float baselineX = Lerp(p.BaselineStartX, p.BaselineEndX, tFinalB);
float baselineY = Lerp(p.BaselineStartY, p.BaselineEndY, tFinalB);

// 6. Serialize intent payload including BaselineX and BaselineY
```

When checking for wave completion in `Condition_IsWaveCompleted`, if a tank is destroyed, permanently burn its firing slot in `BurnedSlotsMask` and free its baseline slot by clearing the corresponding bit in `BaselineReservedMask` so surviving tanks can utilize it in subsequent waves.

4\. Subordinate Tank: Reversing to the Baseline

Update the subordinate tank's `HullDownAttackParams` to include the assigned `BaselineX` and `BaselineY`.

Replace `Action_ReverseToCover` with `Action_ReverseToBaseline`. This action writes to the `LocomotionChannel`, commanding a move to the baseline coordinates.

To satisfy the "reverse driving" requirement, the muscle tier executor responsible for this specific action must configure the physical `NavState`. The cognitive action passes a flag in its channel parameters, and the executor writes `NavState.ReverseAllowed = 1` so the `CarKinematicsSystem` resolves the kinematic trajectory using a negative velocity constraint.

```
[BTreeDefinition("HullDownAttackRun")]
public static BTreeBuilder<HullDownAttackBlackboard, BTreeContext> BuildHullDownAttackRunTree()
{
    return new BTreeBuilder<HullDownAttackBlackboard, BTreeContext>()
        .Sequence(seq => seq
            .Selector(sel => sel
                .Condition(bb => bb.Params, Condition_HasTarget)
                .Action(bb => bb.Params, Action_CreepToAndBeyondSlot)
            )
            .Action(bb => bb.Params, Action_AimAndFireSpecific)
            
            // Executes reverse locomotion to the assigned baseline slot
            .Action(bb => bb.Params, Action_ReverseToBaseline) 
        );
}
```

Summarize the hill attack behavior. First a specification for the commander.

The platoon commander's hill attack behavior is implemented as a `FastBTree`, which is the architectural standard for multi-phase, sequential planning. The specification enforces strict Data-Oriented Design by partitioning cognitive memory into two unmanaged structs. Static configurations—including the 1D firing line segment, parallel baseline segment, attack direction, tank spacing, and target area entity—are defined in `PlatoonHillAttackParams` and map precisely into the safe 60-byte parameter region of the 128-byte `BrainBlackboard`.

Volatile tracking data is decoupled into the `Blackboard1024` component via the `HillAttackMutableState` struct. To prevent state corruption when child entities are destroyed and the engine's `UnitRoster` automatically compacts, this heavy state uses a stable Struct-of-Arrays (SoA) layout to track active attackers and their assigned slots. It utilizes bitmasks (`BurnedSlotsMask`, `WaveUsedSlotsMask`, `BaselineReservedMask`) to manage spatial availability and prevent duplicate assignments without relying on managed heap allocations.

The commander's BTree topology is a `Sequence` that first dispatches all subordinate tanks to the baseline using Just-In-Time (JIT) coordinate interpolation, blocking execution until all entities report arrival. It then enters an infinite loop via a `Repeater` node to execute alternating attack waves. During the wave dispatch phase, the commander queries its `TargetMemory` to isolate enemies within the target area and distributes them round-robin to the wave's active tanks, ensuring targets are not shared unless enemies are outnumbered. Tactical orders are issued top-down by publishing `AssignTacticalIntentEvent`. Wave execution is monitored via a condition node that evaluates ECS structural liveness natively (`repo.IsAlive`); if a tank is destroyed, its spatial slot is permanently burned and the tracker performs an O(1) swap-removal. The loop terminates when no targets remain, breaking the repeater and triggering a `BehaviorFinishedEvent` to notify the mission layer.

The subordinate tank executes the `HullDownAttackRun` behavior. The `BehaviorIngressSystem` parses the JIT-computed absolute spatial coordinates, shared attack direction, and assigned target entity directly into the tank's 60-byte `HullDownAttackParams` struct.

The tank's execution loop uses a `FastBTree` `Sequence` containing a blocking `Selector`. The tank writes a locomotion intent to drive toward the firing slot, transitioning to a creep mode that yields `NodeStatus.Running` continuously across frames. The moment `Condition_HasTarget` registers the specific assigned threat in `TargetMemory`, the selector short-circuits, instantly halting the creeping action. The sequence advances to `Action_AimAndFireSpecific`, which pushes the exact target entity to the `WeaponChannel`, intentionally bypassing generic target acquisition in the muscle tier. Following engagement, the tank writes a reverse intent to the `LocomotionChannel` flagged with `NavState.ReverseAllowed = 1`, commanding kinematics to resolve a negative velocity trajectory back to its reserved baseline slot. Channel-writing actions must be decorated with the `[WritesChannel]` attribute, allowing the source generator to emit failure-reset wrappers that mathematically prevent zombie actions when the BTree sequence advances.

No i want you to specify the behavior requirements, not concrete implementation and architecture. What the commander gets as inputs, what he is supposed to do, how he conceptually control the progress

**Inputs**

-   **Target Area:** A defined geographic boundary containing the enemy entities to be eliminated.-   **Firing Line Segment:** The start and end coordinates defining the crest of the hill where tanks will expose themselves.-   **Baseline Segment:** A line segment safely behind cover where tanks will stage and retreat.-   **Attack Direction:** The shared vector indicating the direction the tanks must face when cresting the hill.-   **Tank Spacing:** The minimum distance required between individual tank slots.-   **Subordinates:** The list of child tank entities currently assigned to this commander.

**Responsibilities**

-   **Preparation:** Compute the total number of available spatial slots along both the firing line and the baseline based on the provided segment lengths and the tank spacing parameter.-   **Initial Deployment:** Command all subordinate tanks to move to their designated baseline slots to stage the attack before any engagement begins.-   **Target Acquisition & Distribution:** Scan the target area to isolate valid enemy targets. Distribute these targets among the attacking tanks in a round-robin fashion to ensure tanks do not fire at the same target unless the enemies are outnumbered.-   **Wave Dispatch:** Divide the available tanks into alternating attack waves (e.g., tanks 1 and 3, then 2 and 4). For each tank in the active wave, assign a specific target, a randomly selected firing slot, and the closest available baseline slot for its retreat.

**Conceptual Progress Control**

-   **Asynchronous Orchestration:** The commander does not micromanage tank locomotion. He issues a high-level tactical intent (move to the firing slot, attack the assigned target, and return) and halts his own decision loop until the wave resolves.-   **Wave Synchronization:** The commander continuously monitors the state of the dispatched tanks. A wave is considered complete only when all participating tanks have finished their attack runs and returned to the baseline, or have been destroyed in combat.-   **Spatial Resource Management:** The commander tracks the availability of all spatial slots. If a tank is destroyed during an attack, the commander must detect the loss, permanently mark the firing slot as blocked to prevent future waves from pathing into the wreck, and free up the associated baseline slot.-   **Termination Condition:** The commander loops the alternating attack waves indefinitely until he determines that zero valid targets remain inside the target area. Once the area is cleared, the behavior terminates and signals mission success to the higher-level strategy tier.

Perfect! Now similar specs for the subordinate tank

**Inputs**

-   **Assigned Target:** The exact enemy entity the tank is ordered to engage and destroy.-   **Firing Slot:** Absolute 2D world coordinates for the designated attack position on the hill crest.-   **Baseline Slot:** Absolute 2D world coordinates for the safe staging area behind cover where the tank must return.-   **Attack Direction:** A shared vector dictating the approach and creeping trajectory over the horizon.-   **Kinematic Limits:** Defined movement speeds for the initial approach and the tactical creep.

**Responsibilities**

-   **Approach:** Navigate from the current baseline position directly toward the assigned firing slot at the standard approach speed.-   **Creep and Scan:** Upon nearing the firing slot, transition to a slow tactical creep along the attack direction, deliberately overshooting the rough slot coordinates if necessary to gain line of sight.-   **Engagement:** Continuously evaluate visual contact with the specific assigned target; the instant it is acquired, halt all forward movement, aim, and fire.-   **Retreat:** Immediately following the engagement, initiate reverse driving to fall back into cover at the assigned baseline slot.

**Conceptual Progress Control**

-   **Reactive Short-Circuiting:** The creeping phase is open-ended and not bound by a strict terminal spatial waypoint. It is governed purely by perception, instantly interrupting the locomotion logic the moment the assigned threat registers in the tank's memory.-   **Target Isolation:** The tank ignores general targets of opportunity. Its sensory evaluation and combat execution are strictly filtered to the exact enemy entity assigned by the platoon commander, preventing wasted ammunition or overlapping fire during the wave.-   **Autonomous Encapsulation:** The tank manages its own kinematic state transitions (forward approach versus reverse retreat) and weapon dispatch locally. It requires no micro-management or polling from the commander during the attack run, naturally terminating its behavior and signaling its availability only once it has safely returned to the baseline.

Enumerate the actions and conditions necessary

Here is the enumeration of the actions and conditions required to implement the hill attack behavior, adhering strictly to the engine's Data-Oriented Design and CQRS boundaries.

Platoon Commander Nodes

These nodes manage the cognitive planning loop and require access to the 1024-byte heavy working memory block to track wave state and spatial slot masks without generating garbage.

-   **Action\_CalculateSegments**
    -   **Attribute:** `[SharedAiHeavyAction]`-   **Responsibility:** Reads the static firing line and baseline segment parameters from the 60-byte parameter bus and calculates the `TotalSlots` capacity based on tank spacing. Initializes the tracking bitmasks inside the `Blackboard1024` `HillAttackMutableState` payload.-   **Action\_DispatchAllToBaseline**
    -   **Attribute:** `[SharedAiHeavyAction]`-   **Responsibility:** Iterates the `UnitRoster` component to deploy all active tanks to the baseline. Uses Just-In-Time 2D interpolation along the 1D baseline segment and publishes an `AssignTacticalIntentEvent` for each subordinate.-   **Condition\_AreAllAtBaseline**
    -   **Attribute:** `[SharedAiCondition]`-   **Responsibility:** Queries the physical `NavState.HasArrived` state of all subordinates in the `UnitRoster`. Yields `NodeStatus.Success` only when all tanks have staged successfully.-   **Condition\_HasTargetsInArea**
    -   **Attribute:** `[SharedAiCondition]`-   **Responsibility:** Filters the commander's `TargetMemory` against the geometric boundaries of the designated target area entity. Yields `NodeStatus.Failure` when zero targets remain, explicitly breaking the BTree `Repeater` node to trigger the `BehaviorFinishedEvent`.-   **Action\_DispatchWaveWithTargets**
    -   **Attribute:** `[SharedAiHeavyAction]`-   **Responsibility:** Executes round-robin target assignment and randomizes unreserved firing and baseline slots. Writes active attacker handles and their assigned slots to the Struct-of-Arrays (SoA) fields inside `Blackboard1024`. Issues top-down orders via `AssignTacticalIntentEvent`, serializing absolute 2D coordinates and the specific target entity handle into the JSON payload.-   **Condition\_IsWaveCompleted**
    -   **Attribute:** `[SharedAiHeavyCondition]`-   **Responsibility:** Evaluates ECS liveness via `EntityRepository.IsAlive` and queries `BehaviorState` transitions by iterating the SoA tracking arrays backwards. If a subordinate is destroyed, it permanently updates `BurnedSlotsMask` and swap-removes the entity in O(1) time. Yields `NodeStatus.Success` when all dispatched attackers have completed their runs or died.

Subordinate Tank Nodes

These nodes dictate the physical execution of the attack run. They map directly to standard AI attributes and use the `[WritesChannel]` compiler injection to guarantee actuator preemption.

-   **Condition\_HasTarget**
    -   **Attribute:** `[SharedAiCondition]`-   **Responsibility:** Queries the local `TargetMemory` component. Yields `NodeStatus.Success` only if the specific enemy entity assigned by the commander registers a threat score greater than zero. Used in a `Selector` to short-circuit the creeping approach.-   **Action\_CreepToAndBeyondSlot**
    -   **Attributes:** `[SharedAiAction]`, `[WritesChannel(ChannelKind.Locomotion)]`-   **Responsibility:** Writes navigation intents to the `LocomotionChannel`. Evaluates distance to the rough firing slot: approaches at standard speed, then transitions to a slow creep intersecting the attack direction vector. Perpetually yields `NodeStatus.Running` across frames until preempted by the parent `Selector`.-   **Action\_AimAndFireSpecific**
    -   **Attributes:** `[SharedAiAction]`, `[WritesChannel(ChannelKind.Weapon)]`-   **Responsibility:** Packs the specifically assigned target entity into `AimAndFireParams` and writes the `ActionIdAimAndFire` command to the `WeaponChannel`. Returns `NodeStatus.Running` while the engagement lasts. The compiler-generated wrapper mathematically prevents zombie firing actions when the sequence advances.-   **Action\_ReverseToBaseline**
    -   **Attributes:** `[SharedAiAction]`, `[WritesChannel(ChannelKind.Locomotion)]`-   **Responsibility:** Writes a reverse locomotion intent to the `LocomotionChannel`, utilizing the assigned baseline coordinates and flagging `NavState.ReverseAllowed = 1` so that the muscle tier kinematic systems resolve a negative-velocity retreat trajectory.

Present the final dtos for parameters and the heavy data work memory

The following are the exact unmanaged C# data structures that implement the FDP engine's strict Data-Oriented Design requirements for the hill attack behavior.

They strictly separate the static, 60-byte generic parameter limits of the `BrainBlackboard` bus from the dynamic working memory managed within the `Blackboard1024` component.

1\. Platoon Commander: The 60-Byte Bus Parameters

The `PlatoonHillAttackParams` struct carries the static tactical geometry and target references assigned by the mission layer. Its total size is exactly 52 bytes, mathematically guaranteeing it fits safely within the 60-byte `BehaviorParameters` region without corrupting the soft-advice or interrupt registers.

```
using System.Runtime.InteropServices;
using Fdp.Core;

// Total size: 52 bytes
[StructLayout(LayoutKind.Sequential)]
public struct PlatoonHillAttackParams
{
    // Firing line segment (16 bytes)
    public float StartX;
    public float StartY;
    public float EndX;
    public float EndY;
    
    // Baseline segment (16 bytes)
    public float BaselineStartX;
    public float BaselineStartY;
    public float BaselineEndX;
    public float BaselineEndY;
    
    // Attack trajectory and spacing (12 bytes)
    public float AttackDirX;
    public float AttackDirY;
    public float TankSpacing;
    
    // Geographical boundary containing enemies (8 bytes)
    public Entity TargetAreaEntity; 
}

[StructLayout(LayoutKind.Sequential)]
public struct PlatoonHillAttackBlackboard
{
    public PlatoonHillAttackParams Params;
}
```

2\. Platoon Commander: The 1024-Byte Heavy Mutable State

The `HillAttackMutableState` is mapped to the `Blackboard1024` component chunk using the `[SharedAiHeavyAction]` projection. It implements a pure Struct-of-Arrays (SoA) layout. By avoiding inline arrays of Cartesian coordinates, and instead mapping active entities to 1D scalar slot indices, the memory footprint remains extremely light while entirely decoupling the wave execution from the engine's volatile `UnitRoster` component.

```
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct HillAttackMutableState
{
    // 1D slot parameterization tracking
    public int TotalSlots;
    public byte CurrentWave;
    
    // Spatial availability masks
    public ushort BurnedSlotsMask;      // Permanently lost to destroyed tanks
    public ushort WaveUsedSlotsMask;    // Occupied on the firing line this wave
    public ushort BaselineReservedMask; // Occupied on the baseline retreat
    
    // Decoupled SoA active tracking (supports UnitRoster max capacity of 16)
    public int ActiveAttackerCount;
    public fixed long ActiveEntityPacked[5];
    public fixed byte ActiveSlotIndex[5];
    public fixed byte ReturnBaselineSlotIndex[5];
}
```

3\. Subordinate Tank: The 60-Byte Bus Parameters

The `HullDownAttackParams` struct carries the JIT-computed absolute coordinate orders and the exact target entity dispatched by the commander. Its size is exactly 40 bytes, fitting well within the 60-byte parameter boundary. The tank behavior requires no `Blackboard1024` memory; its entire execution relies purely on polling its `TargetMemory` and evaluating this static DTO.

```
using System.Runtime.InteropServices;

// Total size: 40 bytes
[StructLayout(LayoutKind.Sequential)]
public struct HullDownAttackParams
{
    // Assigned absolute firing slot (8 bytes)
    public float SlotX;
    public float SlotY;
    
    // Assigned absolute retreat baseline slot (8 bytes)
    public float BaselineX;
    public float BaselineY;
    
    // Attack direction perpendicular to the horizon (8 bytes)
    public float AttackDirX;
    public float AttackDirY;
    
    // Kinematic constraints (8 bytes)
    public float ApproachSpeed;
    public float CreepSpeed;
    
    // Specifically assigned target entity (8 bytes)
    public long TargetEntityPacked;
}

[StructLayout(LayoutKind.Sequential)]
public struct HullDownAttackBlackboard
{
    public HullDownAttackParams Params;
}
```

How to detect entities within given area (inside the polygon)? Brain has no spatial grid. We could use time sliced entity query and iterate all entities all the time during the behavior?

Do not use time-sliced entity queries to scan the entire world from a behavior node. Doing so bypasses the engine's perception pipeline, violates the CQRS boundary between the cognitive and muscle tiers, and pollutes the behavior tree execution state with system-level iterator tracking. The `QueryTimeSliced` API is designed for heavy system-level maintenance passes, such as background layer mask assignments, not for continuous tactical cognitive evaluation.

In a pure Data-Oriented Design, the cognitive tier (Brain) must not possess omniscient spatial awareness. It relies entirely on the perception tier (Muscle) to supply sensory data. The perception systems, such as `VisionBroadphaseSystem` and `AudioPerceptionSystem`, utilize the `SpatialHashGrid` on the muscle tier to detect entities. These systems run through a debounce and threat-evaluation pipeline that ultimately writes confirmed contacts into the Brain's `TargetMemory` component.

To detect if enemy entities are inside your target polygon, your behavior action must exclusively iterate its own `TargetMemory`. Because `TargetMemory` is a fixed-size unmanaged buffer capped at `MaxTrackedTargets` (currently 4), iterating it and performing a 2D point-in-polygon math check using its cached `PositionsX` and `PositionsY` fields is a strictly bounded, zero-allocation O(1) operation. This design protects the hot path performance and enforces the architectural rule that an AI cannot react to an entity it has not organically perceived.

I need much more than 4 targets. Area can be big. Commander entity has to evaluate targets in area without any perception sensor it owns. Maybe we could create a special 'field/satellite observer' virtual entity watching a target area. Maybe the commander can dynamically create one and use it exclusively.

Your instinct to bypass the organic perception pipeline for theater-wide targeting is correct, but dynamically spawning a "satellite" entity is an architectural anti-pattern. We do not pollute the ECS repository and the `EntityLifecycleModule` with invisible dummy actors just to circumvent a memory limit or act as data proxies. Entities represent physical or logical actors, not query handles.

Furthermore, the organic `TargetMemory` component is mathematically capped at `PerceptionConstants.MaxTrackedTargets` (exactly 4 slots) to guarantee zero-allocation hot-path performance for localized threat hysteresis. It is fundamentally the wrong data structure for omniscient area scans.

To solve this while adhering strictly to the engine's CQRS boundaries and Data-Oriented Design, we must establish a dedicated reconnaissance data link between the Muscle tier (which owns the spatial grid) and the Brain tier (where the Commander evaluates its BTree).

1\. The CQRS Reconnaissance Component

Instead of forcing targets into `TargetMemory` or `Blackboard1024`, define a standalone unmanaged component to act as the feedback channel. This allows it to hold a large inline array (e.g., 64 targets) without violating the Brain's generic memory limits.

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(240)] // Example available ID
public unsafe struct AreaReconStatus
{
    public const int MaxAreaTargets = 64;
    
    public int TargetCount;
    public fixed long TargetEntityPacked[MaxAreaTargets];
}
```

2\. Muscle Tier: Spatial Hash Query

The Muscle tier maintains the `SpatialHashGrid` natively for collision and perception broadphases. You will write an `AreaReconSystem` that runs on the Muscle node during `SystemPhase.Simulation`.

This system queries commanders that have the `PlatoonHillAttackParams` assigned. It extracts the geographic polygon from the `TargetAreaEntity`, calculates a bounding circle, and queries the `SpatialGridData` singleton directly.

```
[UpdateInPhase(SystemPhase.Simulation)]
public class AreaReconSystem : IEcsModuleSystem
{
    public unsafe void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        if (!repo.HasSingleton<SpatialGridData>()) return;
        var grid = repo.GetSingleton<SpatialGridData>().Grid;
        
        var commanderQuery = repo.Query().With<PlatoonHillAttackParams>().Build();
        Span<(Entity entity, Vector2 pos)> candidates = stackalloc (Entity, Vector2)[9];

        foreach (var commander in commanderQuery)
        {
            var p = repo.GetComponentRO<PlatoonHillAttackParams>(commander);
            
            // Resolve polygon centroid and radius for the broadphase query
            Vector2 centroid = GetPolygonCentroid(repo, p.TargetAreaEntity);
            float radius = GetPolygonRadius(repo, p.TargetAreaEntity);
            
            int candidateCount = grid.QueryNeighbors(centroid, radius, candidates);
            
            var reconStatus = new AreaReconStatus();
            for (int i = 0; i < candidateCount && reconStatus.TargetCount < AreaReconStatus.MaxAreaTargets; i++)
            {
                Entity candidate = candidates[i].entity;
                if (!repo.IsAlive(candidate)) continue;
                
                // Filter by hostile force affiliation natively
                if (repo.HasComponent<EntityInfo>(candidate) && 
                    repo.GetComponentRO<EntityInfo>(candidate).ForceId == ForceId.Hostile)
                {
                    // Precise Point-in-Polygon check
                    if (IsPointInPolygon(candidates[i].pos, repo, p.TargetAreaEntity))
                    {
                        reconStatus.TargetEntityPacked[reconStatus.TargetCount++] = (long)candidate.PackedValue;
                    }
                }
            }
            
            repo.SetComponent(commander, reconStatus);
        }
    }
}
```

3\. Network Replication (CQRS Boundary)

If your architecture splits the Brain and Muscle into distributed nodes, the Muscle node holds authority over `AreaReconStatus`. You must implement a standard egress/ingress DDS translator pair (similar to `NavigationStatusEgressTranslator`) to replicate this component back to the Commander's Brain node.

4\. Brain Tier: BTree Evaluation

The Commander's BTree node `Action_DispatchWaveWithTargets` no longer reads the heavily constrained `TargetMemory`. It simply queries its own `AreaReconStatus` and distributes the IDs directly.

```
[SharedAiHeavyAction(..., typeof(HillAttackMutableState))]
public static unsafe NodeStatus Action_DispatchWaveWithTargets(
    ref PlatoonHillAttackParams p, 
    ref HillAttackMutableState state, 
    Entity self, 
    EntityRepository repo)
{
    // If the muscle tier hasn't supplied recon data yet, wait.
    if (!repo.HasComponent<AreaReconStatus>(self)) 
        return NodeStatus.Running;

    ref readonly var recon = ref repo.GetComponentRO<AreaReconStatus>(self);
    
    if (recon.TargetCount == 0) 
        return NodeStatus.Failure; // Breaks the Repeater loop

    // Proceed with wave dispatch using recon.TargetEntityPacked[...]
    // ...
}
```

This design strictly honors the separation of concerns: the Muscle tier acts as the omniscient spatial observer via the `SpatialHashGrid` without inventing phantom entities, and the Brain tier processes the pure data feed via its BTree logic without executing heavy spatial mathematics.

Oh no. No system should scan for commanders running doctrine and peek into their params! There should be a environment query system. Similar to raycasts. Wjen triggered, it can run in the background on the muscle node. and once targets acquired, it reports back to the requestor. Can use managed components (not saved to recording) holding extra large resulrs.

That is the correct architectural assessment. Coupling a generic spatial query system directly to a behavior-specific struct like `PlatoonHillAttackParams` violates the Open-Closed Principle and breaks the separation of concerns. The AI domain should not dictate how the muscle tier performs spatial queries.

Implementing an asynchronous Environment Query System (EQS) that mirrors the engine's decoupled Pathfinding and Raycast pipelines is the proper Data-Oriented Design approach. Using a managed component for the payload is also correct, provided we apply the strict data policies required by the engine to prevent garbage collection and serialization bottlenecks.

Here is the clean architecture implementation for the asynchronous EQS pipeline.

1\. The CQRS Query Components

We define a purely structural request component and a managed result component. The managed result component must be decorated with `[DataPolicy(DataPolicy.NoSave | DataPolicy.NoRecord | DataPolicy.NoSnapshot)]`. This guarantees the massive `List<Entity>` payload remains strictly transient and will not crash the `RecorderSystem` or pollute binary checkpoints.

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(245)] // Example ID
public struct AreaQueryRequest
{
    public Entity TargetAreaEntity;
    public ForceId TargetForce;
}

[ComponentId(246)]
[DataPolicy(DataPolicy.Transient)] // Expands to NoSave | NoRecord | NoSnapshot
public class AreaQueryResult
{
    public List<Entity> Targets { get; set; } = new();
}
```

2\. Muscle Tier: The EQS Solver System

The `AreaQuerySolverSystem` runs on the Muscle node during `SystemPhase.Simulation`. It looks for incoming requests, queries the `SpatialHashGrid` natively, and resolves the targets.

If your architecture is distributed, the solver does not attach the managed component directly. It publishes an unmanaged `AreaQueryCompletedEvent` to the bus, which a DDS egress translator packages and sends back to the Brain node (exactly matching how `PathResponseSolverEgressTranslator` operates).

```
[UpdateInPhase(SystemPhase.Simulation)]
public class AreaQuerySolverSystem : IEcsModuleSystem
{
    public unsafe void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        if (!repo.HasSingleton<SpatialGridData>()) return;
        var grid = repo.GetSingleton<SpatialGridData>().Grid;
        
        var query = repo.Query().With<AreaQueryRequest>().Build();
        Span<(Entity entity, Vector2 pos)> candidates = stackalloc (Entity, Vector2)[8];

        foreach (var requester in query)
        {
            var req = repo.GetComponentRO<AreaQueryRequest>(requester);
            
            // Resolve polygon and query grid...
            int candidateCount = grid.QueryNeighbors(centroid, radius, candidates);
            
            // 1. Gather results (omitted for brevity)
            // 2. Publish unmanaged resolution event for the network egress translator
            repo.Bus.Publish(new AreaQueryCompletedEvent 
            { 
                Requester = requester,
                // Pass a pooled array or chunked data for network egress
            });
        }
    }
}
```

3\. Brain Tier: Ingress and Component Assignment

On the Brain node, the ingress translator receives the DDS response, reconstructs the `Entity` handles using the `NetworkEntityMap`, and uses the `IEntityCommandBuffer` to attach the managed class.

```
// Inside your AreaQueryResponseIngressTranslator
var resultComponent = new AreaQueryResult();
// Populate resultComponent.Targets from the DDS payload...

cmd.AddManagedComponent(requesterEntity, resultComponent);
```

4\. Brain Tier: BTree Execution Flow

The commander's BTree uses an asynchronous polling pattern to trigger the query and wait for the managed result without blocking the thread.

-   **Action\_RequestAreaQuery**: Checks if the entity already has an `AreaQueryRequest`. If not, uses the command buffer to add it. Yields `NodeStatus.Running`.-   **Condition\_IsAreaQueryResolved**: Checks `repo.HasManagedComponent<AreaQueryResult>(ctx.Self)`. Blocks the sequence until the ingress translator attaches the result.-   **Action\_DispatchWaveWithTargets**: Retrieves the managed list via `repo.GetManagedComponentRO<AreaQueryResult>(ctx.Self)`. It extracts the targets, dispatches the tactical intents to the tanks, and then cleans up.

```
[SharedAiAction]
public static NodeStatus Action_DispatchWave(
    ref PlatoonHillAttackParams p, 
    Entity self, 
    EntityRepository repo)
{
    // 1. Read the heavy managed result
    var result = repo.GetManagedComponentRO<AreaQueryResult>(self);
    
    // 2. Distribute targets to the wave
    // ...
    
    // 3. Cleanup: Free the heap memory and clear the request so it can be fired again
    var cmd = repo.GetCommandBuffer();
    cmd.RemoveComponent<AreaQueryRequest>(self);
    cmd.RemoveManagedComponent<AreaQueryResult>(self);
    
    return NodeStatus.Success;
}
```

This ensures the Brain remains perfectly isolated. It delegates spatial heavy lifting to the Muscle's generic EQS, waits reactively for the asynchronous response, and safely disposes of the managed memory once the planning phase concludes.

The eqs must run in sod module, working with snapshot, to avoid blocking the main fast simulation loop.

Moving the Environment Query System (EQS) into a Snapshot-on-Demand (SoD) module is the architecturally correct decision to protect the 60Hz hot path. By running the query asynchronously against a read-only replica of the world, we completely decouple the heavy spatial mathematics from the main simulation thread.

Here is how you implement the asynchronous EQS solver using the engine's `ModuleHostKernel` SoD architecture.

1\. Define the Async Module

You must define an `IEcsModule` configured with `ExecutionPolicy.SlowBackground`. This instructs the kernel to allocate a background thread and provide a thread-safe snapshot of the ECS world via the `OnDemandProvider`.

```
public class EqsModule : IEcsModule
{
    public string Name => "EnvironmentQuerySystem";
    
    // Runs asynchronously on a background thread at 10 Hz. 
    // The kernel passes a zero-copy or pool-recycled ISimulationView snapshot.
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);
    
    public IReadOnlyList<Type>? WatchComponents => null;
    public IReadOnlyList<Type>? WatchEvents => null;

    private readonly AreaQuerySolverSystem _solverSystem = new();

    public void RegisterSystems(ISystemRegistry registry)
    {
        // Direct execution pattern is preferred here, or register to run in the module's Tick
        registry.RegisterManualSystem(_solverSystem);
    }

    public void Tick(ISimulationView view, float deltaTime)
    {
        _solverSystem.Execute(view, deltaTime);
    }
    
    public IReadOnlyList<Type>? GetRequiredComponents() => null;
}
```

2\. The SoD Solver System

The solver runs on the background thread. It must adhere to the strict SoD contract: it can only read from the `ISimulationView` snapshot and must queue all structural mutations through the thread-local `IEntityCommandBuffer`.

Because the main thread's `SpatialHashGrid` is subject to mutation, the background solver should either maintain a module-private spatial grid (like the `AutonomousPerceptionModule` does) or use raw entity queries against the snapshot's transforms if the query volume is small.

```
public class AreaQuerySolverSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        // 1. Get the thread-local command buffer from the SoD view
        var cmd = view.GetCommandBuffer();
        
        var requests = view.Query().With<AreaQueryRequest>().Build();

        foreach (var requester in requests)
        {
            var req = view.GetComponentRO<AreaQueryRequest>(requester);
            var result = new AreaQueryResult();

            // 2. Perform heavy spatial mathematics against the snapshot
            // (e.g., iterating transforms or querying a module-private grid)
            // ...
            // result.Targets.Add(foundEntity);

            // 3. Queue the mutation. We attach the managed payload and clear the request.
            cmd.AddManagedComponent(requester, result);
            cmd.RemoveComponent<AreaQueryRequest>(requester);
        }
    }
}
```

3\. Automatic Main-Thread Reconciliation

You do not need to write an ingress translator or handle locks to get the managed component back onto the live entity.

When the background task completes, the `ModuleHostKernel`'s harvest loop (`HarvestEntry`) intercepts the module's thread-local `EntityCommandBuffer` and automatically calls `Playback()` on the main thread during the next available boundary. This guarantees that the `AreaQueryResult` managed class is safely attached to the live ECS world without race conditions.

4\. BTree Execution Unchanged

The commander's behavior tree requires zero modifications from our previous design.

The `Condition_IsAreaQueryResolved` node running in `FastBTree` on the main thread simply polls `repo.HasManagedComponent<AreaQueryResult>(self)` every frame. Because the BTree inherently yields `NodeStatus.Running` when blocked, the AI gracefully idles until the `EqsModule` finishes its background task and the kernel plays back the command buffer. Once attached, the BTree extracts the targets, distributes the tactical intents, and explicitly calls `cmd.RemoveManagedComponent<AreaQueryResult>` to release the heap memory.

Eqs runs on muscle node. We need network translator to transport the result to the brain node. Egress Translator picks internal query result managed fdp event and transport it to the brain node via Ingress translator. Specual system on brain writes the result to managed query result component. Behavior action polls the managed query result compinent.

Your proposed pipeline is structurally sound, but it introduces a redundant system on the Brain node. The FDP architecture provides an `IEntityCommandBuffer` directly to the `PollIngress` method of network translators. Therefore, the Ingress Translator itself must instantiate the managed component and attach it to the entity, eliminating the need for an intermediate bridging system.

Here is the clean implementation of the distributed EQS pipeline.

1\. DDS Wire Contract

We define a DDS message that uses `[DdsManaged]` to support a dynamic list of network IDs. We never send volatile local memory pointers over the network.

```
[DdsTopic("AreaQueryResponse")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
public partial struct AreaQueryResponse
{
    public long RequesterEntityId;
    
    [DdsManaged] 
    public List<long> TargetEntityIds;
}
```

2\. Muscle Tier: Egress Translator

The EQS SoD module on the Muscle node publishes a managed `AreaQueryCompletedEvent` to the local bus containing local `Entity` handles. The Egress Translator drains this event, maps the local handles to network IDs using the `NetworkEntityMap`, and publishes to DDS.

```
public sealed class AreaQueryEgressTranslator : IDescriptorTranslator
{
    public string TopicName => "AreaQueryResponse";
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    
    private readonly DdsWriter<AreaQueryResponse> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly FdpEventBus _eventBus;

    public AreaQueryEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, FdpEventBus eventBus)
    {
        _writer = new DdsWriter<AreaQueryResponse>(participant, TopicName);
        _entityMap = entityMap;
        _eventBus = eventBus;
    }

    public void ScanAndPublish(ISimulationView view)
    {
        foreach (var evt in _eventBus.ReadManaged<AreaQueryCompletedEvent>())
        {
            if (!_entityMap.TryGetNetworkId(evt.Requester, out long requesterNetId)) continue;

            var targetNetIds = new List<long>(evt.Targets.Count);
            foreach (var target in evt.Targets)
            {
                if (_entityMap.TryGetNetworkId(target, out long targetNetId))
                {
                    targetNetIds.Add(targetNetId);
                }
            }

            _writer.Write(new AreaQueryResponse
            {
                RequesterEntityId = requesterNetId,
                TargetEntityIds = targetNetIds
            });
        }
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
}
```

3\. Brain Tier: Ingress Translator

On the Brain node, the Ingress Translator receives the DDS message, maps the network IDs back to local `Entity` handles, and writes the `AreaQueryResult` managed component directly into the ECS via the `IEntityCommandBuffer`.

```
public sealed class AreaQueryIngressTranslator : IDescriptorTranslator
{
    public string TopicName => "AreaQueryResponse";
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    
    private readonly DdsReader<AreaQueryResponse> _reader;
    private readonly NetworkEntityMap _entityMap;

    public AreaQueryIngressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = new DdsReader<AreaQueryResponse>(participant, TopicName);
        _entityMap = entityMap;
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
    {
        using var loan = _reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var data = sample.Data;

            if (!_entityMap.TryGetEntity(data.RequesterEntityId, out var requester)) continue;
            
            var localTargets = new List<Entity>(data.TargetEntityIds.Count);
            foreach (var targetNetId in data.TargetEntityIds)
            {
                // Only include targets that have already materialized on the Brain node
                if (_entityMap.TryGetEntity(targetNetId, out var target))
                {
                    localTargets.Add(target);
                }
            }

            var resultComponent = new AreaQueryResult { Targets = localTargets };
            
            // Write directly to the command buffer. No intermediate system required.
            cmd.AddManagedComponent(requester, resultComponent);
        }
    }

    public void ScanAndPublish(ISimulationView view) { }
}
```

4\. Memory Safety Constraints

Ensure your `AreaQueryResult` component is decorated with `[DataPolicy(DataPolicy.Transient)]`. This guarantees the flight recorder and snapshot serialization modules will ignore the heavy managed list, preserving the performance of the checkpointing pipeline.

The BTree node on the Brain tier simply polls `repo.HasManagedComponent<AreaQueryResult>(self)`. Once the component appears, it reads the targets, dispatches the tactical intent, and immediately calls `cmd.RemoveManagedComponent<AreaQueryResult>(self)` to release the heap memory.

Imagine networkless editor. No network translators whatsoever. no one to write result to component. Special system necessary.

In a networkless, single-process environment like the standalone Editor, the Brain and Muscle tiers share the exact same `EntityRepository`, `ModuleHostKernel`, and `FdpEventBus`. There is no physical network boundary.

However, writing a "special system" just for the editor violates clean architecture. You should not maintain two separate execution paths (one for distributed, one for offline). Instead, you unify the pipeline using the event bus as the universal CQRS bridge.

To achieve this, we introduce a **Local Resolution System** that runs on the cognitive tier. It consumes a managed event from the bus and attaches the heavy managed component, making the pipeline perfectly agnostic to whether it is running in the Editor or a distributed cluster.

1\. The Universal Completion Event

Define a managed event to carry the heavy payload. Because it is managed, it avoids the strict 16-byte inline limits of unmanaged structs and allows dynamic lists, utilizing `PublishManaged`.

```
public class AreaQueryCompletedEvent
{
    public Entity Requester { get; set; }
    public List<Entity> Targets { get; set; } = new();
}
```

2\. The Muscle Tier: Solver System

The SoD-based `AreaQuerySolverSystem` runs its heavy spatial math in the background. When finished, it does not attach the component directly. It uses the `IEntityCommandBuffer` to publish the managed event.

```
public class AreaQuerySolverSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        var requests = view.Query().With<AreaQueryRequest>().Build();

        foreach (var requester in requests)
        {
            var req = view.GetComponentRO<AreaQueryRequest>(requester);
            var targets = new List<Entity>();
            
            // Perform spatial hash query against the snapshot...
            // targets.Add(foundEntity);
            
            // Output to the bus. We do NOT attach the managed component here.
            cmd.PublishManaged(new AreaQueryCompletedEvent
            {
                Requester = requester,
                Targets = targets
            });
            
            cmd.RemoveComponent<AreaQueryRequest>(requester);
        }
    }
}
```

3\. The Brain Tier: Resolution System

This system runs on the Cognitive Tier (and in the Editor) during `SystemPhase.Input`. Its sole responsibility is to drain the completion events and attach the `AreaQueryResult` managed component so the BTree can poll it.

```
[UpdateInPhase(SystemPhase.Input)]
public class AreaQueryResolutionSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;

        foreach (var evt in repo.Bus.ReadManaged<AreaQueryCompletedEvent>())
        {
            if (!repo.IsAlive(evt.Requester)) continue;

            // Attach the heavy managed result for the BTree to consume
            var resultComponent = new AreaQueryResult { Targets = evt.Targets };
            repo.SetManagedComponent(evt.Requester, resultComponent);
        }
    }
}
```

Architectural Elegance

This design provides a unified, zero-branching architecture across all deployment targets:

-   **Networkless Editor:** The `AreaQuerySolverSystem` and `AreaQueryResolutionSystem` both run in the same process. The solver publishes `AreaQueryCompletedEvent` to the local bus, and the resolution system reads it on the next frame. The BTree receives its data seamlessly.-   **Distributed Cluster:** The Muscle node runs the `AreaQuerySolverSystem` and an Egress Translator. The Egress Translator drains the `AreaQueryCompletedEvent` from the bus and sends it over DDS. The Brain node runs an Ingress Translator. The Ingress Translator reads the DDS packet and simply calls `repo.Bus.PublishManaged(new AreaQueryCompletedEvent(...))`. The `AreaQueryResolutionSystem` reads the event from the local bus and attaches the component.

You completely eliminate the need for offline hacks. The Brain's `FastBTree` node (`Condition_IsAreaQueryResolved`) simply polls for the `AreaQueryResult` component, completely oblivious to whether the result took 1 frame from a local system or 3 frames across a DDS network.

How does this relate to how pathfinding or raycast queries work and provides results? We should be consistent.

The previous proposal to use a managed component (`AreaQueryResult`) directly attached to the commander entity violates the established spatial query architecture of the engine. Both pathfinding and raycast pipelines utilize a zero-allocation, singleton batching pattern to communicate across the CQRS and network boundaries without polluting individual entities with transient structural changes or managed memory.

To enforce strict consistency with the engine's pathfinding and raycasting, the Environment Query System (EQS) must adopt the **Batch Data Singleton** pattern.

The Engine's Spatial Query Pattern

The engine manages heavy spatial requests (Raycasts, Pathfinding) through a highly optimized, three-tier data flow:

**1\. Unmanaged Singleton Batches** Rather than attaching query components to individual entities, the engine uses global singletons like `RaycastBatchData` and `PathfindingBatchData`. These singletons hold pre-allocated, fixed-capacity unmanaged memory buffers: `NativeArray<Request>` and `NativeArray<Result>`.

**2\. Synchronous BTree Polling** The Brain tier (BTree actions) never waits for managed events. It uses static helpers like `PathfindingBatchHelper` to append a request directly into the singleton's `Requests` array, which returns a uniquely packed `RequestId`. The BTree node then yields `NodeStatus.Running` and polls the singleton's `Results` array using that ID on subsequent frames (`GetPathResult`, `GetRaycastResult`).

**3\. Network Translators & Resolution** If the architecture is distributed, egress translators (e.g., `RaycastBatchEgressTranslator`, `PathRequestBrainEgressTranslator`) drain the local singleton's requests, package them into DDS batches (e.g., `RaycastRequestBatch`), and immediately reset the singleton's `Count` to zero. The target Muscle/Solver node ingests the batch, runs the heavy system (e.g., `RaycastSolverSystem`), and egresses the results back in a bulk `ResponseBatch`.

Implementing EQS Consistently

To align the hill attack area query with this standard, we must discard the managed `AreaQueryResult` component and implement `AreaQueryBatchData`.

**1\. Define the Batch Singleton** Define an unmanaged singleton holding the requests and results. Because unmanaged structs cannot hold a `List<Entity>`, you must mirror how pathfinding handles variable-length waypoint arrays. Pathfinding registers the waypoints into a `TrajectoryPoolManager` and returns an integer `RouteHandle`. Your EQS will register found targets into a `TargetPoolManager` and return a `TargetGroupHandle`.

```
[StructLayout(LayoutKind.Sequential)]
public struct AreaQueryRequest
{
    public long RequestId;
    public Entity TargetAreaEntity;
    public int SourceNodeId; 
}

[StructLayout(LayoutKind.Sequential)]
public struct AreaQueryResult
{
    public long RequestId;
    public bool IsReady;
    public int TargetCount;
    public int TargetGroupHandle; // Points to unmanaged memory pool
    public int SourceNodeId;
}

[ComponentId(XXX)]
public struct AreaQueryBatchData
{
    public const int DefaultCapacity = 64;
    public int Count;
    public NativeArray<AreaQueryRequest> Requests;
    public NativeArray<AreaQueryResult> Results;
}
```

**2\. The BTree Action Nodes** The commander's BTree uses a two-step node setup, identical to pathfinding.

-   `Action_RequestAreaQuery`: Submits the polygon area bounds to `AreaQueryBatchData`, saves the `RequestId` in the BTree `AsyncData` or `Blackboard1024`, and returns `Running`.-   `Condition_IsAreaQueryResolved`: Polls `AreaQueryBatchData.Results` for the `RequestId`. Once `IsReady` is true, it retrieves the targets via the `TargetGroupHandle`, dispatches the wave to the `UnitRoster`, and returns `Success`.

**3\. The Network/Solver Tier**

-   **Offline/Editor:** `AreaQuerySolverSystem` runs in `SystemPhase.Simulation`, reads the requests, executes the spatial query, registers the targets in the `TargetPoolManager`, and writes to `AreaQueryBatchData.Results`.-   **Distributed:** A Brain-side Egress Translator reads `AreaQueryBatchData.Requests`, converts them to an `AreaQueryRequestBatch` DDS topic, and clears the local batch. A Muscle-side Solver executes the query and publishes an `AreaQueryResponseBatch` DDS topic, which an Ingress Translator writes back into the Brain's `AreaQueryBatchData.Results`.

This completely eliminates structural ECS mutations (no `AddComponent` or `RemoveComponent` for query state), requires zero garbage collection, and mathematically mirrors the existing `Raycast` and `Pathfinding` pipelines perfectly.

Ok. Are the RaycastBatchData and PathfindingBatchData already instantiatedd on cgf subsystem? Are they connected to their muscle node counterparts via network?

Yes, both batch data singletons are instantiated on the CGF (Brain) subsystem and are seamlessly connected to their remote solver counterparts via dedicated DDS translators.

For raycasting, the `CgfSubsystem` explicitly initializes the `PhysicsToolkitModule`, which allocates the persistent native arrays for the `RaycastBatchData` singleton and registers it in the CGF's ECS world. The network bridge is established by the `BrainPerceptionTranslatorPack`, which is registered on nodes with the `Brain` role. This pack provides the `RaycastBatchEgressTranslator`, which drains the pending requests from the CGF's local `RaycastBatchData` singleton, converts absolute Cartesian coordinates to relative ENU offsets, and publishes a `RaycastRequestBatch` to the Perception Solver via DDS. Once the remote solver processes the batch, the CGF's `RaycastBatchIngressTranslator` receives the `RaycastResponseBatch` DDS message and injects the resolved hit results back into the local `RaycastBatchData.Hits` array.

For pathfinding, `PathfindingBatchData` is designed using the exact same zero-allocation singleton pattern. The connection to the Navigation Solver is managed by the `BrainPathfindingTranslatorPack`. The `PathRequestBrainEgressTranslator` reads the Brain's local pathfinding requests, translates the coordinates, and publishes a `PathRequestBatch` to DDS. The Navigation Solver computes the routes and returns a `PathResponseBatch`, which the `PathResponseBrainIngressTranslator` intercepts to reconstruct absolute Cartesian waypoints and populate the CGF's `PathfindingBatchData.Results`.

This data-oriented architecture ensures that your behavior tree actions simply append requests to a local unmanaged singleton and poll for readiness on subsequent frames. The cognitive AI logic remains completely isolated from the network serialization mechanisms and the heavy spatial mathematics executing on the remote muscle nodes.

pls describe again how the new area EQS querying will work

Finding relevant info...

pls describe again how the new area EQS querying will work

The Environment Query System (EQS) area querying operates strictly on the Batch Data Singleton pattern, guaranteeing zero heap allocations, zero structural ECS mutations, and preserving the CQRS boundary between the cognitive and muscle tiers. It mathematically mirrors the engine's existing pathfinding and raycast architectures.

**1\. Unmanaged Batch Singleton** Rather than attaching transient managed components to individual entities, all queries route through a global unmanaged ECS singleton, `AreaQueryBatchData`. This singleton holds pre-allocated `NativeArray<AreaQueryRequest>` and `NativeArray<AreaQueryResult>` buffers. Because unmanaged structs cannot hold dynamic collections like `List<Entity>`, the discovered targets are written into an unmanaged memory pool, similar to how the `TrajectoryPoolManager` handles route waypoints. The `AreaQueryResult` struct simply receives an integer `TargetGroupHandle` that points to this data block.

**2\. Brain Tier: Asynchronous BTree Polling** The commander's `FastBTree` evaluates the query using a two-step polling pattern that persists state across frames without blocking the main thread.

-   **Action\_RequestAreaQuery**: Appends the area bounds into the `AreaQueryBatchData.Requests` array, caches the uniquely assigned `RequestId`, and returns `NodeStatus.Running`.-   **Condition\_IsAreaQueryResolved**: Polls the `AreaQueryBatchData.Results` array on subsequent frames using the cached ID. Once `IsReady` evaluates to true, it retrieves the entity handles via the `TargetGroupHandle`, distributes the targets to the wave, and returns `NodeStatus.Success`.

**3\. Muscle Tier: Background SoD Execution** The heavy spatial mathematics are completely decoupled from the 60 Hz simulation hot path. The `AreaQuerySolverSystem` runs in an asynchronous module utilizing the Snapshot-on-Demand (SoD) execution policy. It reads requests from the thread-safe snapshot, queries the `SpatialHashGrid` natively to find valid entities within the polygon, registers the results in the target pool, and writes the final `AreaQueryResult`.

**4\. Network Agnosticism** The AI cognitive logic remains perfectly isolated from network serialization mechanisms. In a distributed cluster, an egress translator on the Brain node automatically drains local requests and publishes an `AreaQueryRequestBatch` DDS topic. The remote Muscle node ingests the batch, runs the solver system, and returns an `AreaQueryResponseBatch` over DDS, which an ingress translator intercepts to populate the Brain's `AreaQueryBatchData.Results` array. The BTree remains completely oblivious to whether the result took one frame from a local solver or multiple frames across a network boundary.

and now describe again how it will be used by the hill attack behavior

Getting the context...

and now describe again how it will be used by the hill attack behavior

Sifting through pages...

and now describe again how the new area EQS query will be used by the hill attack behavior

The integration of the Environment Query System (EQS) into the hill attack behavior enforces strict Data-Oriented Design by delegating spatial queries to a background solver and using the Batch Data Singleton pattern, structurally mirroring the engine's pathfinding and raycast pipelines. The platoon commander's `FastBTree` topology must be updated to asynchronously poll for targets within the target area before dispatching each wave.

Instead of relying on the locally constrained `TargetMemory` component, the commander executes a three-step asynchronous pipeline within its infinite wave sequence:

**1\. Request Submission (****Action\_RequestAreaQuery****)** This action initiates the cycle by reading the `TargetAreaEntity` from the 60-byte `PlatoonHillAttackParams`. It appends a new `AreaQueryRequest` directly into the pre-allocated `AreaQueryBatchData.Requests` native array. The uniquely generated `RequestId` is cached in the commander's unmanaged `HillAttackMutableState` payload inside the `Blackboard1024` component. The action then returns `NodeStatus.Success` to advance the sequence.

**2\. Asynchronous Polling (****Condition\_IsAreaQueryResolved****)** This node acts as a non-blocking gate. Every tick, it polls the `AreaQueryBatchData.Results` array using the cached `RequestId`.

-   If `IsReady` is false, it yields `NodeStatus.Running`. The `BTreeTickSystem` parks the execution pointer in `BrainBTreeState` and yields the thread, allowing the commander to idle gracefully while the muscle node processes the spatial math.-   Once `IsReady` is true, the node inspects the returned `TargetCount`. If the count is zero, it yields `NodeStatus.Failure`. This failure breaks the parent `Repeater` loop, which fulfills the termination condition and signals `BehaviorFinishedEvent` to the mission director.-   If targets exist, it yields `NodeStatus.Success`, allowing the sequence to proceed.

**3\. Wave Dispatch and Cleanup (****Action\_DispatchWaveWithTargets****)** The commander uses the `TargetGroupHandle` provided in the query result to access the unmanaged target memory pool (matching the `RouteHandle` pattern used in pathfinding). It extracts the confirmed enemy `Entity` handles, randomizes the spatial slots, and performs a round-robin distribution to the active wave's subordinates, publishing top-down orders via `AssignTacticalIntentEvent`.

Before returning `NodeStatus.Success`, this action is strictly responsible for calling a free/release method on the `TargetGroupHandle`. This releases the chunk back to the unmanaged memory pool, mathematically preventing native memory leaks across an indefinite number of attack waves.

This architecture guarantees the cognitive hot path never blocks waiting for heavy spatial hash grid evaluations, avoids all managed heap allocations, and maintains the rigid CQRS boundary between tactical decision-making and physical simulation.

