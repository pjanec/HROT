i would like to prepare a guide for AI behavior developer on how to use the  Btree and HSM  for AI behavior development, giving them basic description first of what building blocks are available, like that

1. behavior can be either btree or hsm or even hardcoded csharp script
2. how/what for the brainblackboard can be used
3. how the behavior parameters relate to the blackboard and how to access them
4. how to define and use reusable conditions and action, how to use them for both the btree and hsm
etc...

Here is the outline you can use for the AI Behavior Development Guide, structured to emphasize our clean architecture, zero-allocation design, and strict execution boundaries.

**Title: Cognitive Tier Architecture: A Guide to AI Behavior Development in FDP**

**1. The Cognitive Tier & Behavior Paradigms**
*   **Architectural Overview**: Explain the CQRS (Command Query Responsibility Segregation) boundary between the Cognitive tier (making decisions) and the Muscle tier (executing physical actions).
*   **Available Paradigms**: A tactical brain (Behavior) is treated as an interchangeable black box by the mission director and can be authored in three ways:
    *   **FastBTree (Tier 2)**: Polling-based behavior trees using sequential memory execution.
    *   **FastHSM (Tier 1)**: Event-driven hierarchical state machines utilizing unmanaged C# function pointers for zero-allocation performance.
    *   **Hardcoded Scripts (Tier 0)**: Simple, domain-specific C# systems (like `TrafficBrainSystem` for civilians) that directly process entities without a formal graph.

**2. The BrainBlackboard: The Universal Cognitive Bus**
*   **Memory Layout**: Describe the `BrainBlackboard` as a strict 128-byte inline memory buffer attached to tactical entities.
*   **Purpose**: It acts as the shared memory space for behavior execution and is shared across both BTree and HSM engines.
*   **Zero-Allocation Data Storage**: Explain that reading and writing from this blackboard avoids heap allocations, keeping the AI simulation strictly on the high-performance hot path.

**3. Behavior Parameters & Memory Projection**
*   **Initialization**: When a mission assigns a behavior, the `BehaviorIngressSystem` invokes a generated `ParseParamsDelegate` to deserialize the behavior's JSON parameters directly into the `BrainBlackboard.Memory` buffer. 
*   **Memory Projection**: Explain how to access these parameters safely. Instead of dynamic reflection at runtime, our architecture uses `Unsafe.As` and `Unsafe.AddByteOffset` to project the raw blackboard bytes back into strongly-typed parameter DTOs.

**4. Unified AI Building Blocks: Shared Conditions and Actions**
*   **The DRY Principle**: Explain how to write core AI domain logic once and expose it to both FastBTree and FastHSM using the `[SharedAiCondition]` and `[SharedAiAction]` attributes.
*   **Method Signatures**: 
    *   Shared logic must be pure, stateless `static` methods.
    *   Methods should request their specific DTO by `ref`, along with the ECS `Entity` and `EntityRepository` context.
*   **Semantic Offset Resolution**: Explain that developers do not hardcode byte offsets. By supplying the parent DTO type and field name (e.g., `[SharedAiCondition(typeof(CombatParams), nameof(CombatParams.Weapon))]`), the Roslyn source generators statically analyze the struct layout and compute the exact byte offset at compile time.
*   **Execution Adapters**:
    *   **BTree**: The compiler emits a zero-allocation managed closure returning a `NodeStatus`.
    *   **HSM**: The compiler emits a highly-optimized, unmanaged `unsafe void` thunk. Shared actions must return a `NodeStatus` for BTree, which the HSM adapter intentionally discards since state machines are event-driven.
*   **Strict Mutability Constraint**: Warn developers that shared actions must *never* make structural ECS changes (e.g., adding or removing components), as this bypasses the HSM deferred command buffer and will corrupt ECS chunk arrays during iteration.

**5. Actuator Preemption and Channel Safety**
*   **The Zombie Action Problem**: Explain the danger of abrupt state transitions leaving orphaned commands running on physical actuators (CQRS channels).
*   **Compiler-Enforced Safety**: Introduce the `[WritesChannel]` attribute (e.g., `[WritesChannel(ChannelKind.Locomotion)]`).
*   **How it Works**: 
    *   For BTrees, the source generator emits a safety wrapper that automatically preempts the channel if the branch aborts with `NodeStatus.Failure`.
    *   For HSMs, the generator emits an `ExitCleanup_{MethodName}` thunk. 
    *   Explain that the `HsmGraphValidator` throws a build-time error if a state machine designer forgets to map this generated cleanup thunk to their state's `OnExit` hook.

**6. Decoupled Cognitive Interrupts**
*   **Hardware-Level Registers**: Detail how physical capabilities (like taking damage) alert the AI. Bytes 126 and 127 in the `BrainBlackboard` are reserved as edge-triggered interrupt registers.
*   **Universal Ingestion**: Explain that the `CognitiveInterruptSystem` pulses byte 126 to `1` when an entity loses mobility. 
    *   FastBTree `Observer` nodes simply poll this byte natively.
    *   FastHSM's tick system translates this byte into an unmanaged `EventId_MobilityLost` injection into the state machine's queue.
*   **Lifecycle**: The `CognitiveCleanupSystem` automatically zeros these registers at the end of the frame to prevent permanent soft-locks.

**7. Mission Routing and Terminal States**
*   **CQRS Handoff**: Explain that the mission layer (`MissionDirectorSystem`) strings behaviors together without needing to know *how* they execute.
*   **Signaling Completion**:
    *   **FastBTree**: Achieved naturally when the root node evaluates to `NodeStatus.Success` or `NodeStatus.Failure`.
    *   **FastHSM**: Achieved by marking a topological node with the `.Final()` builder extension, which stamps `StateFlags.IsFinal`.
*   **The Contract**: Both paradigms will automatically publish a `BehaviorFinishedEvent` exactly once per behavior instance when these terminal states are reached, seamlessly advancing the entity to its next mission phase.


Take a look on the engine sources and understand it first before theying to give instruction on how to use it properly. There are tons of unit tests than can give you some idea. 

You can consult also the .dev\btree-hsm-unif\DESIGN.md describing the unification of usage and blackboard access for btree and hsm.

In the guide pls give plenty of examples how to do stuff.

The reader of the guide knows how to use btrees and hsm in general. The goal is to explain how these work within our engine and how to use them properly.

write the guide as markdown document to .dev\btree-hsm-unif\GUIDE.md

below there are few technical details you can use:

# 1. The Cognitive Tier & Behavior Paradigms

At the core of our engine's AI architecture lies a strict separation of concerns between decision-making and physical execution. We enforce a rigid CQRS (Command Query Responsibility Segregation) boundary between the Cognitive tier (the Brain) and the Muscle tier. The Cognitive tier is solely responsible for evaluating logic and writing "intents" to actuator channels, such as the `LocomotionChannel` or `WeaponChannel`. It never touches physics or transforms directly. The Muscle tier then reads these intents, performs the physical actions, and writes back a status, such as a `NavigationStatus`. 

From the perspective of the higher-level mission routing (`MissionDirectorSystem`), the tactical brain of an entity is treated as a perfectly interchangeable black box known as a "Behavior". The mission layer assigns a behavior and simply waits for a completion signal, completely oblivious to the underlying execution technology. 

To satisfy different performance and complexity budgets, we expose three distinct paradigms for authoring these behaviors:

**FastBTree (Tier 2)**
This is our polling-based behavior tree engine, ideal for complex, sequential tactical logic like infantry combat or ambushes. It utilizes sequential memory execution via the `BrainBTreeState` and the universal `BrainBlackboard`. The interpreter ticks nodes each frame, making it highly flexible for designers who need to compose deep selector and sequence trees. 

**FastHSM (Tier 1)**
This is our event-driven hierarchical state machine engine, optimized for highly reactive behaviors like a military APC running a convoy escort. It achieves blistering, zero-allocation performance by entirely bypassing reflection and managed overhead, relying instead on unmanaged C# function pointers and rigidly packed memory structs like `BrainHsm64` and `BrainHsm128`. Because it is event-driven rather than polling-based, transitions are evaluated by pushing events directly into the machine's unmanaged queues.

**Hardcoded Scripts (Tier 0)**
For massive numbers of simple entities where even the overhead of a compiled graph is unwarranted, we support Tier 0 domains driven by pure C# systems. A prime example is our `TrafficBrainSystem`, which operates on civilian pedestrians and cars. This system simply queries for entities marked as civilian (`SimTier` value of 1) and writes basic commands directly into their `LocomotionChannel`—such as fleeing from a threat or wandering—completely sidestepping the formal BTree or HSM interpreters.

By decoupling the mission layer from these execution mechanics, you as an AI developer are free to choose the exact paradigm that fits the performance and cognitive budget of your unit without breaking the rest of the simulation pipeline.


# 2. The BrainBlackboard: The Universal Cognitive Bus

The `BrainBlackboard` is an elegant solution to one of the hardest problems in AI architecture: sharing state and sensory data between entirely different execution paradigms without polluting the garbage collector. 

Architecturally, the `BrainBlackboard` is implemented as an ECS component containing a strictly packed, 128-byte inline memory buffer. By leveraging unmanaged, fixed-size memory, we guarantee zero-allocation reads and writes, keeping our cognitive simulation strictly on the high-performance hot path while it is shared universally across both FastBTree and FastHSM behaviors.

To maintain strict memory safety and prevent data collisions, we divide this 128-byte contiguous span into distinct, conventionally enforced regions:

**1. Behavior Parameters (Low Offsets)**
When the mission tier assigns a new behavior, the `BehaviorIngressSystem` invokes a `ParseParamsDelegate` to deserialize the JSON configuration directly into the start of the blackboard's memory. At compile-time, our Roslyn source generators analyze the required parameter DTOs and emit highly optimized pointer math (`Unsafe.As` and `Unsafe.AddByteOffset`) to safely project these raw bytes back into strongly typed structs. This cleanly isolates behavior configurations at the lower offsets (e.g., offsets 0–15).

**2. Contextual "Soft Advice" (High Offsets)**
We reserve the higher end of the memory buffer for continuous, contextual feedback provided by external systems. For example, the `RouteContextSystem` parses dynamic route parameters and writes a parsed `dangerLevel` directly into the `ExpectedThreatLevel` byte offset. By placing these at the high end of the buffer, they remain safely isolated from the core behavior parameter DTOs.

**3. Hardware-Level Interrupt Registers (Bytes 126 & 127)**
The last two bytes of the blackboard are strictly reserved as single-frame cognitive interrupt registers. When the `CognitiveInterruptSystem` detects a physical state change—such as an entity transitioning to a state where it can no longer move—it writes a `1` directly to byte 126 (`InterruptRegister_MobilityLost`). 

This is the ultimate expression of the Open/Closed Principle. Because the signal is universally written to the blackboard:
*   **FastBTree** Observer nodes can natively poll this memory address to cleanly abort running branches.
*   **FastHSM** tick systems check this exact same register and translate it into a `MobilityLost` event injected directly into the unmanaged event queue.

To ensure these edge-triggered interrupts do not permanently soft-lock the AI on subsequent frames, a `CognitiveCleanupSystem` runs at the absolute end of the simulation pipeline to unconditionally zero out these reserved registers.

From a clean architecture perspective, the `BrainBlackboard` serves as the perfect decoupling mechanism. It allows the muscle tier, mission tier, and external systems to communicate rich, contextual data to the AI without ever needing to know if the entity is being driven by a behavior tree or a state machine.

# 3. Behavior Parameters & Memory Projection

When bridging high-level mission configuration with a high-performance execution tier, the architectural challenge is moving data from JSON into the simulation hot-path without incurring garbage collection (GC) overhead or relying on slow runtime reflection. We solve this through a combination of atomic deserialization and compile-time memory projection.

**Atomic Deserialization at Ingress**
When the mission layer assigns a new behavior, it publishes an `AssignBehaviorEvent` that carries the serialized JSON configuration. The CQRS boundary dictates that this is consumed by the `BehaviorIngressSystem`. 

Instead of deserializing into managed heap objects, this system invokes a `ParseParamsDelegate` specified in the `BehaviorDefinition`. This delegate is responsible for parsing the JSON and writing the values directly into the unmanaged, fixed-size byte array of the `BrainBlackboard`. 

Crucially, this operation enforces strict transactional safety. To prevent an entity from entering a corrupted or partially transitioned state due to malformed JSON, the `BehaviorIngressSystem` allocates a temporary `stackalloc` shadow copy of the blackboard memory. The parsing is attempted against this shadow buffer first. If the parsing succeeds, the shadow memory is copied to the live ECS component, making the behavior assignment and parameter injection perfectly atomic. If it fails, the error is safely caught and the entity continues executing its previous behavior uninterrupted.

**Zero-Overhead Memory Projection**
Once the parameters are safely packed into the blackboard's byte array, your behavior logic needs a clean, type-safe way to read them. Doing dynamic offset calculations or boxing at runtime is strictly prohibited in this engine. 

Instead, you author pure, unmanaged DTO structs—such as `FireAtTargetParams`—to define your memory layout. When writing a reusable behavior, you annotate your method with `[SharedAiCondition]` or `[SharedAiAction]`, passing the parent DTO type and the specific field name.

The Roslyn source generators (`Fbt.SourceGen` and `Fhsm.SourceGen`) take over at compile time. They interrogate the C# Semantic Model to analyze your DTO's structural layout and calculate the exact byte offset of the requested field. The generators then emit engine-specific adapter thunks that use `System.Runtime.CompilerServices.Unsafe.AddByteOffset` and `Unsafe.As` to cast the raw blackboard bytes back into a reference to your strongly-typed DTO.

This pattern is the gold standard for data-oriented clean architecture: it completely isolates the AI developer from raw pointer arithmetic, guarantees zero allocations, and ensures that data projection executes as instantaneous, inlined pointer math at runtime.


# 4. Unified AI Building Blocks: Shared Conditions and Actions

One of the most significant architectural victories in our cognitive tier is the strict enforcement of the DRY (Don't Repeat Yourself) principle across our two fundamentally different execution engines. Historically, if you wanted an AI agent to evaluate a combat condition, you had to write the logic twice: once as a managed `NodeLogicDelegate` for the FastBTree interpreter, and again as an unmanaged function pointer for the FastHSM kernel. 

By introducing the `[SharedAiCondition]` and `[SharedAiAction]` attributes into our core domain libraries, we have completely decoupled the AI business logic from the execution topology.

Here is how you author, project, and safely execute these unified building blocks:

**1. The Unified Domain Signature**
When you write a reusable AI behavior, you define it strictly as a pure, stateless `static` method. It should never concern itself with the global 128-byte `BrainBlackboard` directly. Instead, it asks for the exact DTO subset it needs via a `ref` parameter, alongside the ECS `Entity` and `EntityRepository` context. 

For actions, you always return a BTree-compatible `NodeStatus` (`Success`, `Failure`, or `Running`).

```csharp
[SharedAiAction(typeof(CombatParams), nameof(CombatParams.Weapon))]
public static NodeStatus Action_AimAndFire(ref WeaponParams p, Entity self, EntityRepository repo)
{
    // Domain logic manipulating the DTO and writing to ECS channels...
    return NodeStatus.Running;
}
```

**2. Semantic Offset Resolution**
Notice that we do not hardcode byte offsets (like `offset: 16`) in the attribute. Doing so would tightly couple the condition to a specific behavior's memory layout and destroy its reusability. 

Instead, you provide the parent DTO type and the target field name, such as `[SharedAiCondition(typeof(CombatParams), nameof(CombatParams.Weapon))]`. At compile time, our Roslyn source generators analyze the C# Semantic Model to determine the exact struct layout and calculate the byte offset mathematically. The generator then bakes this calculated offset into a highly specific adapter.

**3. The Compiler-Generated Adapters**
Because FastBTree and FastHSM have entirely different performance and execution constraints, they utilize separate Roslyn generators that read the exact same shared domain method:

*   **FastBTree (`Fbt.SourceGen`)**: Emits a zero-allocation managed closure registered under a compound key (e.g., `"Action_AimAndFire@16"`). This closure projects the raw blackboard bytes to your DTO using `Unsafe.AddByteOffset` and correctly returns the evaluated `NodeStatus` back to the tree interpreter.
*   **FastHSM (`Fhsm.SourceGen`)**: Emits a blisteringly fast, unmanaged `unsafe void` thunk. Because hierarchical state machines are event-driven rather than polling-based, the HSM adapter intentionally discards your method's `NodeStatus` return value. The thunk safely unwraps the `EntityRepository` from the unmanaged `HsmKernelBridge`, projects the DTO, and invokes your shared code. 

**4. Strict ECS Mutation Constraints**
There is one critical architectural constraint you must obey when writing shared actions. 

By unifying the write path, the generated adapter thunks bypass FastHSM's standard deferred `HsmCommandWriter` and write directly to the `EntityRepository`. While mutating fields on existing components is perfectly safe and highly performant, you must **never** make structural ECS changes—such as adding or removing components—from inside a shared AI action or condition. Performing structural changes directly against the repository during the cognitive system's active chunk iteration will immediately corrupt the ECS chunk arrays.


# 5. Actuator Preemption and Channel Safety

When building AI behaviors, a common architectural failure is the "zombie action." This occurs when a cognitive branch (like a BTree selector or an HSM state) abruptly aborts, but the active actuator channel (such as the `LocomotionChannel` or `WeaponChannel`) is not cleanly reset. The cognitive layer moves on to make new decisions, but the physical entity continues executing the orphaned command because the stale channel state persists. 

To solve this, we rely on compiler-enforced safety rather than human memory. We introduced the `[WritesChannel]` attribute (e.g., `[WritesChannel(ChannelKind.Locomotion)]`), which AI developers must apply to any shared action that mutates an actuator channel. 

**FastBTree Preemption**
For FastBTree, the behavior tree interpreter naturally cascades abort signals down the tree as a `NodeStatus.Failure`. Our Roslyn source generator (`Fbt.SourceGen`) detects the `[WritesChannel]` attribute and emits a zero-allocation managed safety wrapper around your action. If the tree forces an abort (returning `Failure`), this injected wrapper automatically zeroes out the channel's `ActiveAction` and increments the `ActionInstanceId`. This integer increment is the crucial handshake that signals the muscle tier's dispatcher to fire its `OnExit` cleanup routine, safely severing the physical command.

**FastHSM Preemption**
FastHSM requires a different approach because it is event-driven and state transitions happen abruptly. Here, `Fhsm.SourceGen` reads the exact same `[WritesChannel]` attribute and automatically generates a highly optimized, unmanaged `ExitCleanup_{MethodName}` thunk (for example, `ExitCleanup_MoveTo`). The generator also populates a strictly mapped `RequiredExitCleanups` dictionary that associates your action with its generated cleanup routine.

**Build-Time Enforcement**
We cannot let AI designers simply forget to wire up these cleanup routines. To guarantee absolute channel safety, the `HsmGraphValidator` enforces a strict rule during the compilation pipeline. If a state machine author registers an `OnEntry` or `Activity` action that writes to a channel, but fails to assign the corresponding generated `ExitCleanup_` thunk to that state's `OnExitAction`, the validator throws a hard build error. The error message explicitly names the offending state and the missing cleanup key, making the omission impossible to miss. 

By shifting this responsibility entirely from human discipline to the Roslyn compiler, we guarantee that no matter how complex the AI topology becomes, actuator channels are always deterministically severed and reset.


# 6. Decoupled Cognitive Interrupts

In a clean architecture, the systems evaluating physical damage should have absolutely no knowledge of how an AI agent thinks. Previously, our capability-loss pipeline was tightly coupled to the unmanaged memory layout of our state machines, requiring explicit ECS queries for `BrainHsm64` and `BrainHsm128` while completely ignoring BTree-driven entities. We eradicated this coupling by transforming the `BrainBlackboard` into a universal cognitive bus.

**Hardware-Level Registers**
We reserve the final two bytes of the 128-byte `BrainBlackboard.Memory` buffer as dedicated, hardware-style interrupt registers. Specifically, byte 126 acts as the `InterruptRegister_MobilityLost` flag, while byte 127 remains reserved for future expansion. This creates a strict data contract: the physical simulation writes to these registers, and the cognitive engines read from them, with neither side needing to know the other's implementation details.

**Edge-Triggered Detection**
To populate these registers, the `CognitiveInterruptSystem` continually compares an entity's current `ActorCapabilityState` against its `PreviousCapabilities`. By performing this edge-triggered detection, we guarantee that the interrupt signal is only raised on the exact simulation frame where a capability (like `CanMove`) transitions from true to false. When this edge is detected, the system simply writes a `1` to the corresponding blackboard byte.

**Polymorphic Ingestion**
Because the interrupt is standardized on the blackboard, both execution paradigms can consume it using their native mechanics:
*   **FastBTree**: Behavior trees are polling-based by nature. AI designers simply configure `Observer` decorator nodes to continuously read byte 126. If the byte flips to `1`, the `Observer` immediately aborts the currently running branch, enabling the tree to cleanly react to the capability loss.
*   **FastHSM**: State machines are event-driven. To bridge this, the `HsmTickSystem<T>` peeks at byte 126 just before ticking the kernel. If the register is set to `1`, the system translates the signal into an unmanaged `EventId_MobilityLost` event and injects it directly into the state machine's queue.

**The Single-Frame Pulse Guarantee**
A critical architectural risk with memory-mapped interrupts is the "soft-lock": if a BTree entity loses mobility, its `Observer` node could permanently read a `1` and lock the AI in an abort loop forever. To enforce a strict single-frame pulse, we rely on the `CognitiveCleanupSystem`. This system is scheduled to run at the absolute end of the `CognitiveRuntimeModule`, unconditionally zeroing out registers 126 and 127 for all entities possessing a `BrainBlackboard`. This elegantly guarantees that interrupts are safely cleared after all cognitive engines have had exactly one frame to process them, regardless of the active brain tier.


# 7. Mission Routing and Terminal States

**7. Mission Routing and Terminal States**

The ultimate test of our clean architecture is how the Cognitive tier reports back to the Mission tier. For the `MissionDirectorSystem` to effectively string together complex sequences of behaviors (e.g., Patrol until a waypoint is reached, then transition to Convoy Escort), it must treat the tactical brain of an entity as a perfect black box. It should never know, or care, whether the underlying execution logic is a Behavior Tree or a Hierarchical State Machine.

To achieve this absolute decoupling, we use a strict CQRS event handoff: the **Terminal State Contract**. 

**Signaling Completion**
How a behavior reaches a logical conclusion depends entirely on the paradigm used, but both converge on the exact same exit strategy.

*   **FastBTree (Polling)**: Behavior trees possess a natural termination state. When the sequential evaluation of the tree completes, the root node will evaluate to either `NodeStatus.Success` or `NodeStatus.Failure`. Our `BTreeTickSystem` natively detects this terminal status at the end of the tick.
*   **FastHSM (Event-Driven)**: State machines, by contrast, are designed to run continuously. To unify the paradigms, we introduced the concept of Terminal States to the HSM compiler. When you define your state machine topology using the `HsmBuilder`, you simply append the `.Final()` extension to a state. This instructs the compiler to stamp `StateFlags.IsFinal` onto the state definition. At runtime, when the unmanaged kernel enters this state, it automatically flags the instance's chunk memory with `InstanceFlags.Terminated`.

**The Contract: BehaviorFinishedEvent**
Regardless of which engine sets the terminal state, both `BTreeTickSystem` and `HsmTickSystem<T>` are programmed to respond identically: they immediately publish a `BehaviorFinishedEvent` to the global ECS event bus. 

This event flows strictly bottom-up from the Cognitive tier to the Mission tier.

To guarantee architectural stability and prevent event-spamming, we enforce exactly-once delivery per behavior assignment. Both tick systems read the `BehaviorState.InstanceId` (which acts as a monotonic preemption token) and cache it upon publishing the event. If the entity remains in the terminal state on the next simulation frame, the cached token prevents a duplicate event from firing. For FastHSM, we provide defense-in-depth by instantly clearing the `Terminated` flag and reverting the machine phase to `Idle` immediately after the event is published, avoiding the "terminal latch" bug if behaviors are swapped rapidly.

**Architectural Benefit**
By standardizing mission completion through the `BehaviorFinishedEvent`, we achieve perfect polymorphism. Mission designers can configure a `MissionPlanQueue` with a phase trigger set to `MissionTrigger.BehaviorFinished`. When this trigger evaluates, the `MissionDirectorSystem` simply advances the mission to the next phase, increments the `BehaviorState.InstanceId`, and assigns the new behavior. 

You, as an AI developer, are free to mix BTree and HSM behaviors interchangeably within the same mission plan without writing a single line of integration boilerplate.

