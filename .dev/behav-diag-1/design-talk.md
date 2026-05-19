The framework provides a robust set of debugging and diagnostic tools specifically tailored for inspecting and troubleshooting entity behavior, encompassing both FastBTree and FastHSM execution, as well as shared memory blackboards.

**FastHSM (Hierarchical State Machines) Diagnostics**

**HSM Trace Buffer:** The `HsmTraceBuffer` is a zero-allocation, thread-local ring buffer (defaulting to 64KB) that records detailed execution traces. It uses varying `TraceLevel` filters to control verbosity: Tier 1 (Transitions, Events, State Enter/Exit), Tier 2 (Actions, Timers), and Tier 3 (Guards, Activities).**Trace Symbolicator:** The `TraceSymbolicator` utility converts the binary records from the trace buffer into human-readable text logs (e.g., mapping state indices to their actual names).**Compile-time Validation:** The `HsmGraphValidator` catches structural errors before execution, such as conflicting history/timer slots across orthogonal regions or event payloads exceeding the 16-byte limit.

**FastBTree (Behavior Trees) Diagnostics**

**Interactive Visualizer:** The `BTreeVisualizerRenderer` is an entity-aware ImGui panel that renders a color-coded tree of the entity's `BrainBTreeState`. It highlights the active execution path (e.g., running nodes in green, ancestral paths in yellow, inactive leaves in gray) and decodes runtime states like async handles and local registers directly in the UI.**Node Debug Metadata:** During tree compilation, `NodeDebugMetadata` captures the source file, line number, and custom comments for each node. The visualizer exposes this metadata via UI tooltips when hovering over nodes.

**Shared Behavior Diagnostics (BTree & HSM)**

**Structured Behavior Logging:** The `BehaviorLog` utility provides unified, structured logging for both BTree and HSM nodes, emitting to the `AI.Behavior` NLog target. It enforces a highly queryable format: `Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[{ActionName}] | {UserMessage}` and includes rapid level-probing to bypass string allocations when disabled.**Blackboard Memory Inspectors:** To debug the shared memory used by AI, the `BrainBlackboardRenderer` and `Blackboard1024Renderer` integrate with the ImGui inspector. Instead of showing raw bytes, they use the behavior's registered Data Transfer Object (DTO) types to project and decode the blackboard memory into readable property trees.**Architecture & Event Browsers:** The `ArchitectureDiagnosticsService` can take point-in-time snapshots of the running modules, systems, and network translators. Additionally, the `EventHistoryCaptureSystem` writes bus events into a circular `DiagnosticEventHistoryService` buffer, which can be reviewed in real-time via the `EventBrowserPanel`.





I want to be able to diagnose what condition caused the state change or equivalent tracing info for btrees to identify why ai decided what it did. How to approach it, what are the possibilities? What are we missing?

To diagnose why an AI entity made a specific decision and what conditions triggered a state change, the framework provides a mix of historical tracing, real-time memory inspection, and live visualization.

Here is how to approach diagnosing AI decisions, the tools available, and where the current limitations lie:

1. Diagnosing FastHSM (Hierarchical State Machines)

For HSMs, the framework explicitly records the "why" behind decisions using a zero-allocation ring buffer.

- Diagnosing FastBTree (Behavior Trees)

For BTrees, diagnosis relies heavily on real-time execution tracking rather than automatic historical buffers.

- Shared Memory Inspection (The "Why")

Both HSMs and BTrees make decisions based on their internal memory. To see what the AI "knows" at the exact moment a decision is made, you can inspect its blackboards.

**Typed Blackboard Renderers:** The `BrainBlackboardRenderer` (for up to 128 bytes) and `Blackboard1024Renderer` (for heavy 1024-byte states) project raw unmanaged memory bytes into readable property trees. This allows you to open the ImGui inspector and see exact variables—such as `ExpectedThreatLevel`, `TargetNetworkId`, or the specific `X, Y` coordinates the entity is trying to reach.**Structured Behavior Logging:** You can use the `BehaviorLog` static utility to emit structured logs directly from your condition or action nodes. It enforces a highly searchable format (`Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[{ActionName}] | {UserMessage}`). This requires you to manually log your failure conditions (e.g., `BehaviorLog.Trace(ref ctx, "Target out of range")` inside a condition node).

What are we missing?

Based on the architecture, there is a notable gap in **BTree historical tracing**.

**No Automatic BTree Trace Buffer:** While the `HsmTraceBuffer` automatically logs every guard evaluation and transition without allocations, FastBTree lacks an equivalent automatic ring buffer for node evaluations. If a BTree condition fails for a single frame and causes the tree to branch differently, you will not see a historical record of it unless you happened to be watching the `BTreeVisualizerRenderer` live, or you manually wrote a `BehaviorLog.Debug` call into the specific condition node.**No Integrated Timeline Scrubbing for BTrees:** While the `FlightRecorder` lets you step back and forth through physics and component states, the BTree visualizer only reflects the exact frame currently loaded. To piece together a fleeting AI decision during replay, you have to watch the `BrainBlackboard` values change frame-by-frame rather than looking at an ordered log of evaluated BTree nodes.





What such ordered log of evaluated BTree nodes might look like? I can not imagine, pls explain

While the framework does not automatically generate a historical trace buffer for Behavior Trees, you can imagine what one would look like by combining the framework's structured **BehaviorLog** format with the way a Behavior Tree evaluates its nodes frame-by-frame.

If you were to manually add `BehaviorLog.Trace()` calls into your nodes, or if the engine had an automatic trace buffer, the log would show the sequential, top-down execution of the tree as it probes different branches to find an action it can run.

To illustrate, let's look at the **MockCombat_BT** behavior tree from the framework's test scenarios, which is structured like this:

**Selector** (tries children until one succeeds/runs)**Sequence** (tries children until one fails)`Condition_ThreatVisible``Condition_HasAmmo``Action_AimAndFire``Action_Flee`

Here is what an ordered log of evaluated nodes would look like across different simulation ticks. The log uses the framework's standard format: `Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[{ActionName}] | {UserMessage}`.

Scenario A: No Threat is Visible (Tick 10)

On this tick, the AI evaluates the tree from the root. Because it sees no threat, it decides to flee. The ordered log of evaluated nodes would look like this:

**[Tick 10] Entity:** **Behavior:[MockCombat] Node:[Condition_ThreatVisible] | Evaluated -> Failure** *Because the first condition failed, the parent* *Sequence* *immediately aborts and fails.*

**[Tick 10] Entity:** **Behavior:[MockCombat] Node:[Action_Flee] | Evaluated -> Running** *The parent* *Selector* *moves to its next child, the flee action, which takes over the active state.*

**What this tells you:** You can instantly see *why* the AI decided to flee. It wasn't because it ran out of ammo; it was because `Condition_ThreatVisible` returned a `Failure` status, causing the tree to branch to the fallback action.

Scenario B: Threat Appears and Ammo is Available (Tick 20)

Ten ticks later, the scenario injects a visible threat into the AI's blackboard memory. The tree evaluates from the root again.

**[Tick 20] Entity:** **Behavior:[MockCombat] Node:[Condition_ThreatVisible] | Evaluated -> Success** *The sequence proceeds to the next node.*

**[Tick 20] Entity:** **Behavior:[MockCombat] Node:[Condition_HasAmmo] | Evaluated -> Success** *The sequence proceeds to the final node.*

**[Tick 20] Entity:** **Behavior:[MockCombat] Node:[Action_AimAndFire] | Evaluated -> Running** *The action begins executing, taking control of the AI's weapon channel.*

**What this tells you:** The log proves that the AI successfully passed both prerequisite checks (`ThreatVisible` and `HasAmmo`) in order, allowing it to reach the `Action_AimAndFire` node.

Scenario C: Ammo Runs Out (Tick 30)

The AI has been firing, and its ammo drops to zero.

**[Tick 30] Entity:** **Behavior:[MockCombat] Node:[Condition_ThreatVisible] | Evaluated -> Success**

**[Tick 30] Entity:** **Behavior:[MockCombat] Node:[Condition_HasAmmo] | Evaluated -> Failure** *The sequence fails and bails out before reaching the firing action.*

**[Tick 30] Entity:** **Behavior:[MockCombat] Node:[Action_Flee] | Evaluated -> Running**

**What this tells you:** This is where an ordered log is incredibly powerful for debugging. If the AI suddenly stopped shooting and ran away, looking at the blackboard memory alone might just show `Ammo = 0`. But the ordered log proves the exact causal chain: the AI *saw* the threat, but specifically failed the `HasAmmo` check, which forced the `Selector` to fall back to `Action_Flee`.

If FastBTree had a historical trace buffer like FastHSM, you would be able to scrub backwards in time and read these exact sequences of `Success`, `Failure`, and `Running` returns to perfectly reconstruct the AI's decision-making process.





Such a trace buffer would contain each condition results evaluated that tick? What if the tree is large?

If a Behavior Tree trace buffer existed in the same way the Hierarchical State Machine (FastHSM) trace buffer does, yes—it would contain the result of every condition and action that was *actually evaluated* during that specific tick.

However, even for a very large tree, the amount of data generated per tick is kept manageable due to how the framework's execution engine and diagnostic buffers are designed:

**1. FastBTree Does Not Evaluate the Entire Tree Every Tick** Even if your Behavior Tree has hundreds of nodes, the engine only evaluates a small fraction of them per frame. The FastBTree `Interpreter` uses highly optimized **resume logic**. When a tree evaluates a sequence or selector, it checks the `RunningNodeIndex` stored in the entity's 64-byte `BehaviorTreeState`. If the engine sees that the active execution path has already passed a specific child's subtree, it knows that child already succeeded or failed on a previous tick, and completely skips evaluating it. Therefore, a trace log for a large tree would typically only contain the 3 to 5 nodes on the active path, rather than evaluating the entire structure.

**2. Circular Ring Buffer Architecture** If a BTree trace buffer were implemented identically to the existing `HsmTraceBuffer`, it would use a fixed-capacity, zero-allocation ring buffer (defaulting to 64KB). If a massive tree *did* evaluate many nodes in a single tick and exhausted the capacity, the buffer simply wraps around and safely overwrites the oldest trace records without allocating new memory on the heap.

**3. Verbosity Filtering** To prevent diagnostic noise in large structures, the framework uses bitmask filtering for its trace buffers. In the HSM equivalent, the `TraceLevel` enum allows you to selectively filter out high-frequency checks. For example, you can choose to only log major structural shifts (`TraceLevel.Transitions` or `TraceLevel.StateChanges`) and turn off individual condition probes (`TraceLevel.Guards`) unless you are actively debugging a specific decision.





Ok i am tnking about implementing such a ring buffer for btree. Lets say there will be a new managed component that holds the unmanaged buffer memory for the currently running btree. How could we intercept the condition (or other) calls in the btree to capture their results automatically and only when such debugging is enabled? To avoid slow downs when disabled. Must be poosible to rurn on/off dynamicalky at runtime per entity.

Implementing a managed component to hold the unmanaged ring buffer is exactly the right architectural approach for this framework. To intercept the condition and action calls dynamically with **zero overhead when disabled**, you should inject the tracing context directly into the execution pipeline via the stack-allocated `BTreeContext`, rather than wrapping delegates or adding virtual method calls.

Here is the step-by-step approach to achieving this based on the framework's existing patterns:

1. Augment the `BTreeContext`

The `BTreeContext` is a `ref struct` that is stack-allocated once per entity inside the `BTreeTickSystem` and passed by reference to every single node evaluation. This makes it the perfect carrier for your trace buffer.

You would add a pointer or reference to your new trace buffer directly in the context:

```
public struct BTreeContext : IAIContext
{
    public Entity Self;
    public EntityRepository World;
    
    // NEW: Pointer to the unmanaged ring buffer memory (null if disabled)
    public unsafe BTreeTraceBuffer* TraceBuffer; 
    
    // ... existing time and parameter fields [3]
}
```

2. Dynamically Toggle in `BTreeTickSystem`

To make tracing toggleable at runtime per entity, you control whether `TraceBuffer` is assigned during the BTree tick setup.

Inside `BTreeTickSystem.Execute`, when iterating over entities, you check if the entity has your new tracing component. If it does, you pass it to the context; if not, you pass `null`:

```
// Inside BTreeTickSystem.Execute:
bool isTracingEnabled = repo.HasManagedComponent<BTreeTraceBufferComponent>(entity);
BTreeTraceBuffer* tracePtr = null;

if (isTracingEnabled) {
    tracePtr = repo.GetManagedComponentRO<BTreeTraceBufferComponent>(entity).GetUnmanagedBufferPointer();
}

var context = new BTreeContext
{
    Self = entity,
    World = repo,
    TraceBuffer = tracePtr, // Injected here
    // ... other fields [2]
};

var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);
```

3. Intercept Calls in the `Interpreter`

You can now modify `Fbt.Kernel.Interpreter` to intercept the evaluations. The `Interpreter` evaluates leaf nodes in `ExecuteAction` (which is also used for `Condition` nodes).

You simply add a fast null-check immediately after the node executes:

```
private NodeStatus ExecuteAction(
    int nodeIndex, ref NodeDefinition node, ref TBlackboard bb, 
    ref BehaviorTreeState state, ref TContext ctx)
{
    var actionDelegate = _actionDelegates[node.PayloadIndex];
    
    // 1. Evaluate the condition/action [6]
    var status = actionDelegate(ref bb, ref state, ref ctx, node.PayloadIndex);

    // 2. INTERCEPT: Automatically record the trace if enabled
    unsafe {
        if (ctx.TraceBuffer != null) {
            ctx.TraceBuffer->WriteNodeEvaluated(nodeIndex, status);
        }
    }

    // ... existing running state logic [7]
    return status;
}
```

You can apply the exact same `if (ctx.TraceBuffer != null)` check inside `ExecuteSequence` and `ExecuteSelector` to trace branch abortions or progression.

Why this avoids slowdowns when disabled

**No Virtual Dispatch or Closures:** By embedding the hook inside `Interpreter.ExecuteAction` instead of wrapping the `NodeLogicDelegate` in a decorator, you avoid creating closure allocations or deeper call stacks.**Branch Prediction:** The check `if (ctx.TraceBuffer != null)` is evaluated locally on a `ref struct`. Because tracing is disabled for 99% of entities, the CPU branch predictor will almost perfectly predict the `null` path, meaning the CPU will breeze right past the trace hook with near-zero instruction overhead.**Cache Coherency:** Since `BTreeContext` is passed by reference anyway, checking a pointer inside it doesn't incur any additional cache misses.

This design directly mirrors how the `FastHSM` engine bypasses its tracing overhead using the `_traceBuffer != null && (header->Flags & InstanceFlags.DebugTrace) != 0` check during its transitions and guard evaluations.





What everything could be part of this trace buffer? Just tree node results per tick? Or something more would make sense?

While logging the basic node results (`Success`, `Failure`, or `Running`) per tick is the foundational requirement, stopping there would leave out critical context. To build a truly comprehensive BTree trace buffer, you should look directly at how the framework's existing `HsmTraceBuffer` works.

The FastHSM engine records a rich set of operational codes (`TraceOpCode`) such as `StateEnter`, `GuardEvaluated`, `ActionExecuted`, `TimerSet`, and `Error`. You can map these exact concepts to Behavior Trees to capture the complete "why" behind an AI's actions.

Here is what a robust BTree trace buffer should realistically capture per tick:

**1. Node Evaluation Results (The Baseline)** This is the core execution path. You record when a node is evaluated and what `NodeStatus` it returned (`Success`, `Failure`, or `Running`). For condition nodes, this is the exact equivalent of the HSM's `TraceOpCode.GuardEvaluated`.

**2. Composite/Subtree Scope Changes (The "Where")** Just as the HSM buffer records `TraceOpCode.StateEnter` and `StateExit` to track hierarchy changes, your BTree buffer should record when the execution pointer drops into a new composite node (like a `Sequence` or `Selector`) or enters an external `Subtree`.

**Implementation Hook:** You would capture this when the BTree engine pushes or pops a node index onto the entity's `NodeIndexStack` and increments the `StackPointer`.

**3. Timer and Async State (The "When")** Behavior Trees use `Wait` and `Cooldown` nodes, which rely on the 24-byte `AsyncHandles` block in the entity's memory to track start times.

The trace buffer should record events analogous to HSM's `TraceOpCode.TimerSet` and `TimerFired`.When a `Wait` node begins, record the target duration. When it finishes, record the completion. This prevents confusion when an AI appears "stuck" in a `Running` state for several frames.

**4. Actuator Channel Mutations (The "What")** The primary way a BTree affects the world is by writing to channels like `LocomotionChannel` or `WeaponChannel`. Similar to HSM's `TraceOpCode.ActionExecuted`, the buffer should capture when a BTree Action node successfully claims a channel or changes an intent.

For example, if the `Action_WriteMoveToChannel` node executes, the trace buffer should record that it specifically mutated the `LocomotionChannel` to request movement.

**5. Diagnostics and Critical Errors** The HSM trace buffer has a dedicated `TraceOpCode.Error` for critical failures, such as infinite RTC loops. Your BTree buffer should capture equivalent architectural failures.

For example, if an action node tries to execute but the entity is missing a required component (e.g., trying to shoot without a `WeaponState`), this should be written to the trace buffer as an error, much like how `BehaviorLog.Error` handles missing channels today.

A Hypothetical `BTreeTraceOpCode`

If you model your unmanaged BTree ring buffer after the `TraceRecord` union, your opcodes would look something like this:

`NodeEvaluated` (Carries the Node Index and `NodeStatus`)`ScopePushed` / `ScopePopped` (Carries the Stack Depth)`WaitStarted` / `WaitCompleted` (Carries the duration/elapsed time)`ChannelMutated` (Carries the `ChannelKind` like Locomotion or Weapon)`Error` (Carries an error code for missing blackboards or components)

By combining **Node Evaluations** with **Channel Mutations** and **Timers**, your trace buffer will perfectly reconstruct not just the logic path the AI took, but the exact physical commands it issued to the simulation engine.





Sounds perfect. Lets imagine we have all those opcodes implemented. Which of them can be recorded automatically and which would need cooperation with the 'user' code like conditions and actions?

Based on the framework's architecture, the responsibility for recording these trace opcodes would be split. The FastBTree engine (`Interpreter`) can handle the structural and execution flow entirely on its own, but domain-specific data and logic will require cooperation from your user-defined action and condition delegates.

Here is how the implementation breakdown would look:

1. Automatically Recorded (Handled by the Engine)

Because the FastBTree `Interpreter` natively controls the traversal and built-in node types, you can intercept these directly inside the kernel with **zero cooperation from the user code**:

- Requires Cooperation (Handled by User Code)

The FastBTree kernel is intentionally decoupled from the specific game domain (like physics, combat, or navigation). Therefore, opcodes that interact with the ECS world must be injected by the developer writing the action nodes:

**ChannelMutated**: Requires user cooperation. The engine does not inherently know what a `WeaponChannel` or `LocomotionChannel` is. Action delegates (e.g., `Action_AimAndFire` or `Action_Wander`) use the context to grab the channel directly (e.g., `ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self)`) and mutate it. To capture this, the developer would need to explicitly write to the trace buffer when they change an actuator intent (e.g., `ctx.TraceBuffer->WriteChannelMutated(...)`).*Note on partial automation:* The framework currently uses a `[WritesChannel]` source-generator attribute to automatically clear channels when a node fails. You could potentially expand this code generator to auto-inject trace calls, but the initial successful mutation still requires the user's code.**Error**: Mixed, but mostly cooperative.**Automatic:** The engine can automatically trace kernel-level errors, such as an invalid `PayloadIndex` or a missing action delegate in the registry.**Cooperative:** Domain-level errors are handled by the user. For instance, if an AI tries to evaluate `Action_AimAndFire` but is missing a `WeaponChannel` component, the user's code currently detects this and calls `BehaviorLog.Error(ref ctx, "Entity is missing LocomotionChannel...")`. The user would need to push this domain failure to the trace buffer manually.

Summary

To implement this cleanly, you only need to expose two methods on your `BTreeContext.TraceBuffer` pointer for the user: **WriteChannelMutated** and **WriteError**. The framework's internal `Interpreter` will automatically take care of the structural pathing (`NodeEvaluated`, `Scope`, `Wait`), meaning 90% of the tracing is completely free and invisible to the developers writing the AI logic.





What could be the data user could pass to these 2 methods, what would be their signature to be diagnosticakly very useful?

To make these two methods diagnostically very useful while adhering to the framework's strict zero-allocation, unmanaged memory constraints, the data passed to them needs to be primitive, compact, and directly correlated to the ECS state.

Here is what the signatures and data payloads should look like based on the framework's existing patterns:

1. `WriteChannelMutated`

Every actuator channel in the framework (`LocomotionChannel`, `WeaponChannel`, `InteractionChannel`) shares a common layout containing an `ActiveAction` ID, an `ActionInstanceId`, and a `NodeStatus`.

To diagnostically track what an AI is attempting to do, this method must capture exactly which channel was touched and what the new intent is.

**Proposed Signature:**

```
public unsafe void WriteChannelMutated(
    int nodeIndex, 
    ChannelKind channel, 
    ushort activeAction, 
    NodeStatus status)
```

**What the data represents:**

**nodeIndex**: Identifies which specific BTree node issued the command.**channel**: The `ChannelKind` enum (`Locomotion`, `Weapon`, or `Interaction`).**activeAction**: The `ushort` identifier of the command being sent to the muscle tier (e.g., `NavigationConstants.ActionIdMoveTo`).**status**: The `NodeStatus` (Running, Success, Failure) being written to the channel.

**Why it is diagnostically useful:** If an AI suddenly stops shooting, the trace buffer will show exactly which action node was evaluated, that it mutated the `ChannelKind.Weapon` channel, and whether it set the `activeAction` to a new ID (like reloading) or changed the status to `Failure`. You can cross-reference the `activeAction` integer directly against your game's constants.

2. `WriteError`

In the existing `FastHSM` engine, the `TraceError` unmanaged struct simply records a 16-bit `ErrorCode` alongside the timestamp and instance ID. For Behavior Trees, you have two options depending on how readable you want the trace buffer to be without external mapping.

**Option A: The Integer-Based Signature (Fastest)**

```
public unsafe void WriteError(int nodeIndex, ushort errorCode)
```

**Data**: You define a domain-specific `enum : ushort` (e.g., `MissingComponent = 1`, `InvalidBlackboard = 2`).**Utility**: This perfectly matches the 12-byte `TraceError` struct size used in FastHSM. It is incredibly fast but requires your `TraceSymbolicator` to map the `errorCode` back to a readable string.

**Option B: The Zero-Allocation String Signature (Most Readable)** Because this framework heavily utilizes `FixedString32` for zero-allocation text (storing up to 31 UTF-8 characters inline), you can pass a fixed string directly into the buffer.

```
public unsafe void WriteError(int nodeIndex, FixedString32 message)
```

**Data**: A stack-allocated `FixedString32` carrying a short, human-readable error.**Utility**: It allows developers to write `ctx.TraceBuffer->WriteError(nodeIndex, new FixedString32("Missing WeaponChannel"));`. When reviewing the trace, you instantly see the text without needing a separate lookup table.

How it looks in user code

If a developer writes an action node to aim and fire, their integration with your trace buffer would look like this:

```
[BTreeAction]
public static NodeStatus Action_AimAndFire(
    ref CombatParams p, ref BehaviorTreeState state, ref BTreeContext ctx)
{
    // 1. Domain Error interception
    if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self))
    {
        if (ctx.TraceBuffer != null)
            ctx.TraceBuffer->WriteError(state.RunningNodeIndex, new FixedString32("No WpnChannel"));
            
        return NodeStatus.Failure;
    }

    ref var weapon = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);
    weapon.ActiveAction = CombatConstants.ActionIdFire;
    weapon.Status = NodeStatus.Running;

    // 2. Channel Mutation interception
    if (ctx.TraceBuffer != null)
    {
        ctx.TraceBuffer->WriteChannelMutated(
            state.RunningNodeIndex, 
            ChannelKind.Weapon, 
            weapon.ActiveAction, 
            weapon.Status);
    }

    return NodeStatus.Running;
}
```

By supplying these two methods, the unmanaged ring buffer captures the exact physical intents (`WriteChannelMutated`) and the exact domain failures (`WriteError`), perfectly complementing the engine's automatic tracking of the tree's structural flow.





Can we suppirt both options, error opcode with fixed string as well with it errorcode and domain specific TraceSymbolicator?

Yes, you can absolutely support both options side-by-side.

Because the framework’s ring buffer architecture (modeled after the `HsmTraceBuffer`) writes memory using a generic `WriteRecord<T>(ref T record, int size)` method with a byte-level cursor `_writePos += size`, **the trace buffer naturally supports variable-length records**. You are not restricted to making every trace record the exact same size.

You can implement this by introducing two distinct opcodes and two distinct unmanaged structs. This gives developers the choice to use the ultra-fast 12-byte integer error when performance is paramount, or the 40-byte string error when they want instant readability without mapping.

Here is how you would structure it:

1. Define Two Error Opcodes

You would extend your `TraceOpCode` equivalent to include two separate error markers:

```
public enum BTreeTraceOpCode : byte
{
    // ... other opcodes
    Error = 0x0E,         // Integer-based error code
    ErrorString = 0x0F    // FixedString32 based error
}
```

2. Define the Unmanaged Structs

You define one small struct for the integer code (exactly like FastHSM's `TraceError`) and a larger struct that embeds the 32-byte `FixedString32`. They both share the standard 8-byte `TraceRecordHeader`.

```
// 12-byte fast error (needs Symbolicator)
[StructLayout(LayoutKind.Explicit, Size = 12)]
public struct TraceError
{
    [FieldOffset(0)] public TraceRecordHeader Header;
    [FieldOffset(8)] public ushort ErrorCode;
    [FieldOffset(10)] public ushort Reserved;
}

// 40-byte readable error (zero-allocation inline text)
[StructLayout(LayoutKind.Explicit, Size = 40)]
public struct TraceErrorString
{
    [FieldOffset(0)] public TraceRecordHeader Header;
    [FieldOffset(8)] public FixedString32 Message;
}
```

3. Expose Overloads on the Trace Buffer

In your `BTreeTraceBuffer`, you simply provide two overloads for the user. Both will safely write to the same byte array, advancing the `_writePos` cursor by either 12 or 40 bytes:

```
public unsafe void WriteError(int nodeIndex, ushort errorCode)
{
    var record = new TraceError
    {
        Header = new TraceRecordHeader { OpCode = BTreeTraceOpCode.Error, /* ... */ },
        ErrorCode = errorCode
    };
    WriteRecord(ref record, sizeof(TraceError));
}

public unsafe void WriteError(int nodeIndex, FixedString32 message)
{
    var record = new TraceErrorString
    {
        Header = new TraceRecordHeader { OpCode = BTreeTraceOpCode.ErrorString, /* ... */ },
        Message = message
    };
    WriteRecord(ref record, sizeof(TraceErrorString));
}
```

4. Reading it back via the Symbolicator

When your diagnostic tool (like the `TraceSymbolicator`) reads the ring buffer, it will read the first 8 bytes as a `TraceRecordHeader` to check the `OpCode`.

Depending on the opcode, it will cast the pointer to the correct struct and advance the read cursor by the correct amount:

If `OpCode == Error`, it reads the `ushort` and looks up the human-readable string in your domain-specific `MachineMetadata` dictionaries.If `OpCode == ErrorString`, it reads the `FixedString32`, calls `.ToString()` to retrieve the inline text directly, and advances the reader by 40 bytes.

By supporting both, you allow domain code (like your Combat or Navigation toolkits) to use `ushort` mappings for high-frequency failures, while allowing custom gameplay scripts to throw quick, one-off `FixedString32` text errors without needing to register them in a central lookup table.





Good plan! Now how to manage the buffer memory. The new component holding unmanaged buffer. Is this good idea? We might need one  for hsm and one for btree as one entity can run both btree and hsm at the same time . It should be recordable so that i can see it in replay.

While introducing an unmanaged trace buffer is conceptually sound, **storing it inside an ECS component is actually an anti-pattern for this specific engine architecture**, especially if your goal is to view the traces during a Flight Recorder replay.

Here is why a component-based buffer won't work for replays, and the exact FDP-native approach you should use instead to support dual BTree/HSM execution and replay compatibility.

Why an ECS Component Buffer is Problematic Here

**Replay Mute Logic:** During playback, the engine disables the `TogglableSimulationGroup` to prevent simulation logic from overwriting historical data. This means `BTreeTickSystem` and `HsmTickSystem` **do not execute during replay**. You cannot generate traces dynamically while scrubbing the timeline; the traces *must* be embedded in the `.fdp` recording file.**Serialization Limits:** The Flight Recorder (`RecorderSystem`) records raw bytes of unmanaged components.If you store the buffer directly in the struct, you will violate the `MaxComponentSize` limit of 1024 bytes. FDP components are meant to be small and cache-friendly.If you store an unmanaged pointer (`void*`) in the component, the Flight Recorder will just record the 8-byte memory address. When you load the replay, that pointer will point to garbage memory, and the trace data will be lost.

The FDP-Native Solution: The Event Bus

To achieve your exact goals—seeing traces in replay, supporting both HSM and BTree, and dynamically toggling them—you should route your traces through the **FdpEventBus** **as Unmanaged Events**.

The `FdpEventBus` is natively hooked into the `RecorderSystem`. Any unmanaged event published during the simulation is automatically LZ4-compressed and written into the `.fdp` file. During replay, the `PlaybackSystem` injects these events back into the read buffer, perfectly synchronized with that specific frame.

Here is how you architect this:

1. Define a Unified Trace Event

Define a small, unmanaged struct decorated with an `[EventId]`. Because the Event Bus multiplexes all events naturally, it seamlessly supports an entity running both HSM and BTree simultaneously.

```
[StructLayout(LayoutKind.Explicit, Size = 16)]
[EventId(5000)] // Assign a unique EventId
public struct AiTraceEvent
{
    [FieldOffset(0)] public TraceOpCode OpCode;
    [FieldOffset(2)] public ushort NodeOrStateId;
    [FieldOffset(4)] public Entity Entity;       // The entity this trace belongs to
    [FieldOffset(12)] public byte BrainTier;     // e.g., 1 for HSM, 2 for BTree
    [FieldOffset(13)] public byte ResultStatus;  // e.g., Success, Failure, Running
}
```

2. Control Tracing via a Tag Component

To ensure you don't flood the Event Bus (and your recording file) with thousands of traces every frame, use a zero-size Tag Component to opt-in specific entities dynamically.

```
[ComponentId(GlobalComponentIds.AiTraceEnabledTag)]
[DataPolicy(DataPolicy.NoSave)] // Don't save to scenarios, it's just for live debugging
public struct AiTraceEnabledTag { }
```

3. Publish during Execution

In your `BTreeTickSystem` (or HSM equivalent), simply check for the tag. If present, pass a flag down to your `BTreeContext` so the `Interpreter` knows to publish events.

```
// Inside BTreeTickSystem.Execute:
bool isTracing = repo.HasComponent<AiTraceEnabledTag>(entity);

var context = new BTreeContext
{
    Self = entity,
    World = repo,
    _isTracingEnabled = isTracing // Injected into context
};
```

Then, intercept the evaluation in your `Interpreter`:

```
if (ctx._isTracingEnabled)
{
    ctx.World.Bus.Publish(new AiTraceEvent 
    {
        Entity = ctx.Self,
        BrainTier = BehaviorConstants.BrainTierBTree,
        OpCode = TraceOpCode.NodeEvaluated,
        NodeOrStateId = (ushort)nodeIndex,
        ResultStatus = (byte)status
    });
}
```

How this behaves in the Engine

**Live Simulation:** The operator clicks an entity and adds the `AiTraceEnabledTag`. The entity starts dumping `AiTraceEvent` messages to the bus. Your Diagnostic UI calls `view.ReadEvents<AiTraceEvent>()` to draw the live history.**Recording:** Because it's on the `FdpEventBus`, the `RecorderTickSystem` grabs the pending events and bakes them perfectly into the timeline.**Replay Scrubbing:** The user scrubs the timeline backward. The `PlaybackController` reads the events from disk and injects them. You open your Diagnostic UI, and the traces magically populate for that exact historical frame, even though the BTree itself is not actually executing!





Events are fine but for easy viewing having a copy of the trace ring buffer every frame is much more convenient. What about a global singleton component managing unmanaged trace buffers for each entity that ever called the trace api. It will record the buffers every frame to the fdp file. And restore in replay.

While having a continuous ring buffer is incredibly convenient for UI viewing and scrubbing, attempting to store and manage this via a **global singleton component recorded to the** **.fdp** **file** conflicts heavily with the engine's memory constraints and Flight Recorder design.

Here is why the singleton approach will cause issues, and how you can achieve your exact UI convenience using the engine's native diagnostic pipeline.

Why a Global Singleton Buffer Fails for Replay

**1. Catastrophic** **.fdp** **File Bloat** The Flight Recorder's `RecordDeltaFrame` writes the entire memory layout of dirty unmanaged components every frame. A standard `HsmTraceBuffer` is 64KB. If you store an unmanaged trace buffer per entity in a singleton, the engine would serialize megabytes of raw buffer memory to disk *every single frame* at 60Hz. Your `.fdp` files would explode in size, whereas recording discrete 16-byte events only costs a few bytes per frame.

**2. Pointer Corruption on Replay** If your singleton manages unmanaged memory allocations (like `NativeMemoryAllocator.Reserve`), it uses pointers (`void*`). The Flight Recorder performs raw memory copies (`CopyChunkToBuffer`) of component data. When you load a replay, the engine will restore those exact pointer addresses from disk, which will point to garbage memory in the new process session, causing immediate access violations.

**3. Strict Component Size Limits** The framework has strict constraints on component sizes to ensure they fit cleanly inside the 64KB `NativeChunkTable`. `EntityCommandBuffer` explicitly enforces a `MaxComponentSize` of 1024 bytes. Pushing large ring buffers into ECS component memory violates this data-oriented design.

The FDP-Native Solution: Event-Sourced Ring Buffers

To get the exact easy-viewing experience of a ring buffer without the serialization overhead, you must decouple **how the data is saved** from **how the data is viewed**.

The engine already does this via the **DiagnosticEventHistoryService**. You should apply this exact pattern to your Behavior Trees:

**1. Record Tiny Events to the Bus (The Storage)** During live simulation, your BTree `Interpreter` continues to publish tiny 16-byte `AiTraceEvent` structs to the `FdpEventBus`. The Flight Recorder effortlessly compresses and bakes these into the timeline.

**2. Reconstruct the Ring Buffer in the UI (The Viewer)** You do not need to save the ring buffer to the `.fdp` file; you only need it to exist in the memory of the machine viewing the replay. You can create a standalone service (e.g., `BTreeDiagnosticHistoryService`) that holds an array of `HsmTraceBuffer` / `BTreeTraceBuffer` instances in managed memory.

You register an `EventHistoryCaptureSystem` in the `SystemPhase.PostSimulation` phase.Every frame, this system reads the `AiTraceEvent` messages from the bus and pushes them into your service's local ring buffers.Your ImGui diagnostic panel simply reads from these local, in-memory ring buffers to draw its timeline UI.

**3. How it Works During Replay** When you scrub the replay timeline, the `PlaybackController` injects the historical `AiTraceEvent` bytes directly back into the `FdpEventBus`'s read buffer for that specific frame. Because your `EventHistoryCaptureSystem` is listening to the bus, it naturally catches these restored events and pushes them into your UI's ring buffer.

By pushing events to the bus and assembling the ring buffer purely on the diagnostic consumer side, your `.fdp` recordings stay perfectly lean, you avoid all pointer corruption, and your UI gets the exact scrubbable ring-buffer convenience you are looking for.





If i seek in replay, to reconstruct the history i would need to go many frames back and gather those events. Lets do both at the same time, events (can stay enabled in production builds)  and optional trace buffer snapshot every frame. This is a debugging feature. If enabled, file size is no issue. Managed Singleton component will use custom serializer to save snapshots of trace buffers  to fdp. Check how to make this possible.

Yes, this dual approach is completely possible and very well-supported by the framework. You can keep the lightweight `AiTraceEvent` publishing on the `FdpEventBus` for production, and introduce an optional Managed Singleton to capture the full unmanaged ring buffers for debugging.

Based on how the `RecorderSystem` and `PlaybackSystem` are implemented, here is the exact architecture to make your managed singleton snapshot feature work:

1. The Managed Singleton Component

Because the `RecorderSystem` hardcodes the use of `FdpAutoSerializer` for managed singletons , you do not actually need to write a custom serializer for the recorder itself. Instead, you can bypass the serializer's reflection overhead by manually packing your unmanaged buffers into a single flat `byte[]` field inside your managed singleton. `FdpAutoSerializer` has a highly optimized fast-path for `byte[]` arrays.

```
[DataPolicy(DataPolicy.SnapshotViaClone)] 
public class TraceBufferSnapshot
{
    // A single, flat array to hold all concatenated unmanaged buffers for the frame.
    // FdpAutoSerializer will bulk-copy this to disk almost instantly.
    public byte[] PackedData = Array.Empty<byte>();
    
    // Number of valid bytes in the PackedData array this frame.
    public int Length; 
}
```

*Note: The* *[DataPolicy(DataPolicy.SnapshotViaClone)]* *attribute (or* *DataPolicy.Default**) ensures the engine knows this class is safe to record and save into the* *.fdp* *file* *.*

2. The Snapshot Capture System

You will need a system that runs at the very end of the frame, right before the `RecorderTickSystem` writes to disk. This system gathers the unmanaged buffers and packs them into your singleton.

```
[UpdateInPhase(SystemPhase.PostSimulation)]
[UpdateBefore(typeof(RecorderTickSystem))] // Ensure it runs before the recorder captures the frame
public class TraceSnapshotCaptureSystem : IEcsModuleSystem
{
    public unsafe void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        if (!repo.HasSingletonManaged<TraceBufferSnapshot>()) return;

        var snapshot = repo.GetSingletonManaged<TraceBufferSnapshot>()!;
        
        // 1. Calculate required size for all entities with tracing enabled
        // 2. Ensure snapshot.PackedData is large enough (reuse the array to avoid GC allocations)
        // 3. Memcpy the unmanaged trace buffers into snapshot.PackedData
        // 4. Update snapshot.Length
    }
}
```

3. How the Flight Recorder handles it natively

Once your system packs the data, the framework's existing Flight Recorder takes over automatically:

- Reading during Replay Scrubbing

Because the `PlaybackController` completely overwrites the Managed Singleton reference when scrubbing to a historical frame , your ImGui diagnostic panel only needs to do this:

```
if (repo.HasSingletonManaged<TraceBufferSnapshot>())
{
    var snapshot = repo.GetSingletonManaged<TraceBufferSnapshot>()!;
    // Unpack snapshot.PackedData into your UI and render the historical ring buffers
}
```

**Why this design is ideal:** By centralizing the unmanaged-to-managed copy in `TraceSnapshotCaptureSystem`, you decouple the heavy 64KB per-entity memory extraction from the core BTree execution . If the debug feature is disabled (i.e., the singleton is never added to the repository), the capture system will simply `return`, keeping your production builds completely zero-allocation  while the 16-byte `AiTraceEvent` structs continue streaming via the `FdpEventBus` .





ok but the singleton also needs to manage the unmanaged trace buffers for entities, used by the tracing api to write the opcodes into, right? Some field being a dictionary of Entity->UnmanagedTraceBuffer. This field will not be saved to fdp recording file, just keeping "working memory" for saving tracing opcodes every tick. And the tracing API would need to take the pointer to the trace buffer from here.

While your instinct is exactly right that the unmanaged "working memory" must be kept separate from the data saved to the `.fdp` file, using a global `Dictionary<Entity, UnmanagedTraceBuffer>` inside a singleton is an anti-pattern in this engine.

A global dictionary introduces lock contention if behaviors are evaluated in parallel across multiple threads, and it defeats the O(1) cache-friendly memory access that the ECS provides.

Instead, the FDP-native approach is to store the "working memory" pointer directly on the entity using a **Transient Managed Component**. The framework specifically provides `[DataPolicy(DataPolicy.Transient)]` to completely hide temporary buffers, raw pointers, and debug metrics from the Flight Recorder and serialization pipelines.

Here is how you separate the working memory from the serialization singleton:

1. The Per-Entity Working Memory (Transient)

You create a managed component that owns the raw unmanaged memory for a single entity. Because it is marked `Transient`, the `RecorderSystem` will never attempt to save it to disk, avoiding the pointer-corruption issue entirely.

```
[DataPolicy(DataPolicy.Transient)]
public sealed class BTreeTraceWorkingMemory : IDisposable
{
    public unsafe BTreeTraceBuffer* Buffer;

    public unsafe BTreeTraceWorkingMemory()
    {
        // Allocate unmanaged ring buffer memory via NativeMemoryAllocator
    }

    public unsafe void Dispose()
    {
        // Free unmanaged memory
    }
}
```

2. High-Performance Tracing (During Simulation)

During your `BTreeTickSystem`, you retrieve this component in O(1) time without any dictionary lookups or locks. You pass the pointer down into your `BTreeContext`.

```
// Inside BTreeTickSystem:
if (repo.HasManagedComponent<BTreeTraceWorkingMemory>(entity))
{
    var workingMemory = repo.GetManagedComponentRO<BTreeTraceWorkingMemory>(entity);
    context.TraceBuffer = workingMemory.Buffer; // Pass pointer to Tracing API
}
```

3. The Snapshot Capture (Post-Simulation)

At the end of the frame, your `TraceSnapshotCaptureSystem` queries all entities that have this working memory. It extracts their unmanaged bytes and concatenates them into the global singleton's `byte[]` array, which *will* be recorded to the `.fdp` file.

```
[UpdateInPhase(SystemPhase.PostSimulation)]
public class TraceSnapshotCaptureSystem : IEcsModuleSystem
{
    public unsafe void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        if (!repo.HasSingletonManaged<TraceBufferSnapshot>()) return;

        var snapshot = repo.GetSingletonManaged<TraceBufferSnapshot>()!;
        var query = repo.Query().WithManaged<BTreeTraceWorkingMemory>().Build();

        // 1. Calculate total required byte size from all queried entities
        // 2. Resize snapshot.PackedData if necessary
        
        int offset = 0;
        foreach (var entity in query)
        {
            var workingMemory = repo.GetManagedComponentRO<BTreeTraceWorkingMemory>(entity);
            
            // 3. Fast memcpy from the unmanaged ring buffer into snapshot.PackedData
            // ...
            offset += bufferSize;
        }
        
        snapshot.Length = offset;
    }
}
```

**Why this is the perfect architecture:**

**Zero Contention:** The tracing API interacts purely with data locally attached to the executing entity. There are no global dictionaries to lock.**Safe Serialization:** The `FdpAutoSerializer` perfectly handles serializing the single `byte[]` in your singleton, while ignoring the raw `void*` pointers hiding in the `Transient` components.**Clean Teardown:** By making `BTreeTraceWorkingMemory` implement `IDisposable`, the unmanaged memory will be properly cleaned up when the entity is destroyed or the simulation shuts down.





Cool! I am thinking about possible simplification by using much smaller BTreeTraceWorkingMemory1024 as unmanaged component kust 1KB long. Maybe it could be sufficient for tracing one single entity with moderate btree size and still would be large enough to keep few historical frames?

Using a 1KB unmanaged component is a viable architectural shortcut that leverages the engine's existing memory constraints and serialization pipeline, but it comes with severe trade-offs regarding delta-compression efficiency.

Here is the technical breakdown of how this approach interacts with the framework and why it is a double-edged sword.

1. Capacity and Constraints

The `EntityCommandBuffer` enforces a strict `MaxComponentSize` of exactly 1024 bytes. You can define a `BTreeTraceWorkingMemory1024` component that perfectly fits this limit, mirroring the framework's existing `Blackboard1024` pattern.

If you use the standard 16-byte `TraceRecord` layout, your capacity math looks like this:

**Header:** 4 bytes for ring buffer cursors (`WritePos`, `Count`).**Payload:** 1020 bytes available for trace records.**Capacity:** ~63 discrete records.

If a moderately sized Behavior Tree evaluates 5 nodes per frame, the buffer will hold approximately 12 frames of execution history (0.2 seconds of time at 60Hz). This is sufficient for immediate causality debugging (e.g., identifying why an action failed on the exact frame a transition occurred) but inadequate for long-term historical scrubbing.

2. Architectural Simplification

This approach entirely eliminates the need for managed singletons, transient allocations, and custom `PostSimulation` snapshot systems.

Because the trace buffer is now a standard unmanaged component, the `RecorderSystem` automatically handles it. During the `RecordDeltaFrame` and `RecordKeyframe` passes, the engine will extract the 64KB chunk containing your trace buffers using `CopyChunkToBuffer` and serialize it directly to the `.fdp` file. During replay, `PlaybackSystem.ApplyChunkData` will restore it identically.

You would define the component like this:

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(YourAssignedId)]
[DataPolicy(DataPolicy.NoSave)] // Prevents the trace buffer from being saved into declarative TKB scenario JSON files [8]
public unsafe struct BTreeTraceWorkingMemory1024
{
    public ushort WritePos;
    public ushort RecordCount;
    public fixed byte Buffer[9];
}
```

3. The Catch: Flight Recorder Delta Bloat

While structurally elegant, embedding high-frequency telemetry into ECS components violates the engine's delta-compression design.

The Flight Recorder relies on chunk-level versioning (`_chunkVersions`) to skip unchanged memory blocks during delta frame recording. Because the BTree will write to this trace buffer on every single tick, the chunk version will be continuously invalidated.

Consequently, the recorder will be forced to serialize the full 1KB payload per entity, every single frame, completely bypassing delta compression. If you enable this on 50 entities, you are injecting 50KB of raw uncompressed data into the delta stream every 16ms, which will bloat the `.fdp` file exponentially compared to the event-bus approach.

Conclusion

If you restrict this feature to a strictly opt-in debugging tool applied to only one or two entities at a time, the 1024-byte unmanaged component is a highly efficient, zero-allocation simplification. However, it must not be left enabled in standard simulation passes due to the catastrophic impact on the `RecorderSystem` delta sizes.





Yes, enabling for just a few entities during development & debugging of the behavior is exactly what this will be used for. As noted before, file size is not an issue in such a case. Let's stick to this simple solution. Later if this proves not satifying the needs (too short buffer) we could introduce the managed component with much larger buffer. Of course, this short buffer would not use the FixedStrings trace opcodes or just if really necessary.

What about similar 1KB unmanaged component trace buffer for HSM? Could that suffice for few frames as well?

A 1KB unmanaged component for FastHSM is architecturally consistent and yields the exact same capacity constraint: 63 trace records per entity (1020 bytes of payload divided by the 16-byte `TraceRecord` union).

Whether 63 records suffice for a few frames depends strictly on the `TraceLevel` bitmask applied to the entity:

**TraceLevel.Tier1 (Transitions, Events, StateChanges):** At this level, the HSM only emits records during discrete state shifts or event consumptions. Since idle frames generate zero trace records, a 63-record capacity will easily hold several seconds (or even minutes) of execution history.**TraceLevel.Tier3 (Guards, Activities):** At this maximum verbosity, the HSM logs every guard evaluation and activity execution. Because activities execute every frame while an entity is in a state, the engine will emit multiple records per tick. In this scenario, 63 records will be exhausted in roughly 10 to 30 frames (0.16 to 0.5 seconds at 60Hz), which remains sufficient for immediate causality debugging during replay.

To implement this cleanly, you must refactor the existing FastHSM trace architecture. Currently, `HsmKernelCore` relies on a single static `_traceBuffer` injected via `SetTraceBuffer`. This violates per-entity isolation and prevents concurrent tracing of multiple entities.

You will need to:

Define an `HsmTraceWorkingMemory1024` unmanaged component. Reserve 4 bytes for the header (e.g., a 16-bit `WritePos` cursor and a 16-bit `TraceLevel` filter mask), leaving exactly 1020 bytes for the `fixed byte` buffer array.Remove the static `_traceBuffer` from `HsmKernelCore`.Inject the per-entity buffer pointer into the evaluation pipeline. You can pass it through the `HsmKernelBridge` context struct, or pass it directly into `HsmKernel.Update` and route it down through `HsmKernelCore.UpdateBatchCore`.

This ensures both BTree and HSM tracing share the identical unmanaged ECS memory pattern, delta-recording pipeline, and zero-allocation constraints.





good! the idea becomes much more clear! Let's imagine we implemented  unmanaged BTreeTraceWorkingMemory1024 and HsmTraceWorkingMemory1024 components.

Now let's pls think how to make sure we can see the trace records in the entity inspector and how to serialize them to fdp json dump. We need some custom renderers and serializers, probably very similar to what is used for blackboards.

To expose the `BTreeTraceWorkingMemory1024` and `HsmTraceWorkingMemory1024` unmanaged components to the diagnostic UI and the JSON serialization pipeline, you must implement the framework's existing `IEntityAwareImGuiRenderer` and `IEntityScenarioTranslator` patterns. This is the exact architecture used for `BrainBlackboard` and `Blackboard1024`.

Here is the architectural implementation for both the UI rendering and JSON extraction.

1. Entity Inspector Rendering (`IEntityAwareImGuiRenderer`)

To render the trace buffer in the ImGui Entity Inspector, create a custom renderer decorated with `[ImGuiRenderer]`. Unlike blackboards which cast the entire memory block to a single DTO struct, the trace renderer must iterate the ring buffer in logical chronological order (oldest to newest) and decode the unmanaged records into a readable format.

```
using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using ImGuiNET;

namespace Hrot.Presentation.Renderers
{
    [ImGuiRenderer(typeof(BTreeTraceWorkingMemory1024))]
    public sealed class BTreeTraceWorkingMemoryRenderer : IEntityAwareImGuiRenderer
    {
        // Injected at startup to resolve node indices to human-readable names
        public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

        public string? GetSummary(object value) => "BTree Execution Trace";
        public bool RenderValue(object value) => false;

        public unsafe bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
        {
            doubleClickedPath = null;
            if (value is not BTreeTraceWorkingMemory1024 traceData) return false;

            ImGui.TextDisabled($"Records: {traceData.RecordCount} | Cursor: {traceData.WritePos}");
            ImGui.Separator();

            if (traceData.RecordCount == 0)
            {
                ImGui.TextDisabled("No trace history.");
                return true;
            }

            // Draw as a structured ImGui table
            if (ImGui.BeginTable("BTreeTraceTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Tick", ImGuiTableColumnFlags.WidthFixed, 50f);
                ImGui.TableSetupColumn("OpCode", ImGuiTableColumnFlags.WidthFixed, 100f);
                ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                fixed (byte* bufferPtr = traceData.Buffer)
                {
                    // Ring buffer logical iteration
                    int capacityBytes = 1020; // 1024 - 4 byte header
                    int recordSize = sizeof(TraceRecord);
                    int startOffset = traceData.RecordCount * recordSize >= capacityBytes 
                        ? traceData.WritePos 
                        : 0;

                    for (int i = 0; i < traceData.RecordCount; i++)
                    {
                        int offset = (startOffset + (i * recordSize)) % capacityBytes;
                        TraceRecord* record = (TraceRecord*)(bufferPtr + offset);

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted(record->Timestamp.ToString());

                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextUnformatted(record->OpCode.ToString());

                        ImGui.TableSetColumnIndex(2);
                        // Decode specific details based on opcode
                        ImGui.TextUnformatted($"Node: {record->StateIndex} Result: {record->GuardResult}");
                    }
                }
                ImGui.EndTable();
            }

            return true;
        }
    }
}
```

*Note: For the HSM equivalent (**HsmTraceWorkingMemory1024Renderer**), you will implement the same structure but use* *MachineMetadata* *(retrieved via the* *BehaviorRegistry**) to convert* *record->StateIndex* *and* *record->EventId* *into human-readable strings**.*

2. FDP JSON Dump Serialization (`IEntityScenarioTranslator`)

To include the trace buffer in clipboard copies and diagnostic dumps, you must write a custom scenario translator.

Because `BTreeTraceWorkingMemory1024` represents transient execution state, it must be marked with `[DataPolicy(DataPolicy.NoSave)]` on its struct definition. Mirroring `BrainBlackboardTranslator`, your translator will implement `Extract` to build a `JsonObject` for the dump, but leave `Inject` completely empty so the engine never attempts to load historical traces from a scenario file.

```
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    public sealed class BTreeTraceWorkingMemoryTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(BTreeTraceWorkingMemory1024);

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(BTreeTraceWorkingMemory1024));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<BTreeTraceWorkingMemory1024>(entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var traceData = ref repo.GetComponentRO<BTreeTraceWorkingMemory1024>(entity);
            
            var recordsArray = new JsonArray();
            int capacityBytes = 1020;
            int recordSize = sizeof(TraceRecord);

            fixed (byte* bufferPtr = traceData.Buffer)
            {
                int startOffset = traceData.RecordCount * recordSize >= capacityBytes 
                    ? traceData.WritePos 
                    : 0;

                for (int i = 0; i < traceData.RecordCount; i++)
                {
                    int offset = (startOffset + (i * recordSize)) % capacityBytes;
                    TraceRecord* record = (TraceRecord*)(bufferPtr + offset);

                    // Box the unmanaged struct to a managed type for FdpAutoSerializer JSON mapping
                    recordsArray.Add(new JsonObject
                    {
                        ["Tick"] = record->Timestamp,
                        ["OpCode"] = record->OpCode.ToString(),
                        ["NodeIndex"] = record->StateIndex,
                        ["Result"] = record->GuardResult
                    });
                }
            }

            var root = new JsonObject
            {
                ["RecordCount"] = traceData.RecordCount,
                ["History"] = recordsArray
            };

            return new Dictionary<string, object> { [Key] = root };
        }

        // NO-OP: Trace buffers are transient debugging state and must never be injected from a scenario file.
        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver resolver) { }

        public IEnumerable<string> GetOutputDomKeys() { yield return Key; }
    }
}
```

3. Registration

To finalize the integration:

**UI**: The `[ImGuiRenderer]` attribute automatically registers your renderers with the `ImGuiRendererRegistry` during application startup.**Serialization**: You must manually register the new translators in `HrotScenarioSerializerFactory.Build()`, immediately alongside `BrainBlackboardTranslator` and `Blackboard1024Translator`.





the btree record will also need some string translator to show the btree name, node name, as well as the converter for int error codes, right?

Yes, translating those raw integer indices and hashes into human-readable strings requires a mapping mechanism. However, because of how FastBTree is architected, you do not need to build a separate `MachineMetadata` object like FastHSM does. The `BehaviorTreeBlob` itself acts as the symbolicator.

Here is how the architecture cleanly handles the translation of those three elements without allocating strings in the trace buffer:

1. Resolving the BTree Name

The trace buffer itself does not need to record which BTree is running. The entity already carries a `BehaviorState` component that tracks this. In your `BTreeTraceWorkingMemoryRenderer`, you read the `BehaviorState.ActiveBehaviorHash` from the entity. You then pass that hash into the injected `BehaviorRegistry` to retrieve the `BehaviorDefinition`, which gives you the human-readable `Name`.

2. Resolving the Node Name

Once you have the `BehaviorDefinition` from the registry, you have direct access to the compiled tree via `def.BTreeInterpreter.Blob`. The `BehaviorTreeBlob` contains all the structural metadata needed to decode the `nodeIndex` stored in your trace records:

- Resolving the Error Codes

If you opt for the fast 12-byte integer error opcode (`TraceOpCode.Error` with a `ushort errorCode`), you will indeed need a domain-specific string translator. Since the FastBTree kernel is decoupled from the game domain, the framework does not know what your error codes mean. You typically handle this in the renderer by casting the `errorCode` to your domain-specific enum:

```
string errorName = Enum.GetName(typeof(CombatErrorCodes), record->ErrorCode) ?? "Unknown";
ImGui.TextUnformatted($"Error: {errorName}");
```

*(If you use the* *FixedString32* *inline opcode we discussed earlier, this translation step is skipped entirely, as the string is read directly from the unmanaged struct).*

Putting it together in the UI

Your ImGui renderer already has the necessary dependencies to do this live. The `BTreeTraceWorkingMemoryRenderer` uses the `BehaviorRegistryAccessor` to grab the definition, unpacks the blob, and loops through the unmanaged buffer. For each record, it translates the `NodeIndex` using the blob's metadata arrays, completely separating the heavy string representations from the ultra-fast execution and recording pipeline.





good! what are we still missing to have full set of changes necessary to enable the diagnostics? I am thinking about

1. how to enable it per entity, from the entity inspector; maybe UI action in the context menu, whose handler instantiates the BTreeTraceWorkingMemory1024 and HSM equivalnt for the entity, which enables storing the traces to those buffers.
2. system wide configuration setting that auto-enables those trace buffer components for all AI-enabled entities
3. Optional dumping the opcodes in readable form to the AI behavior message log (per entity, maybe again a UI action from context menu)
4. something else?

To implement this diagnostic architecture cleanly, several integration points across the presentation, simulation, and kernel layers must be finalized. Here is the exact breakdown of the missing components to fulfill your requirements.

1. Per-Entity Activation via UI Context Menu

The UI layer is decoupled from the simulation and must not mutate ECS components directly. To enable tracing per-entity from the inspector, you must route the UI action through the event bus.

- System-Wide Auto-Enable Configuration

If you require a system-wide toggle, inject the flag into the existing debug configuration pipeline rather than creating a new singleton.

- Optional Logging to `BehaviorLog`

Routing trace opcodes to the `MessageLogWindow` via NLog requires a mechanism that does not compromise the hot path when logging is disabled.

- What Else is Missing? (Critical Architectural Gaps)

To make the system compile and function correctly, you must address the following engine-level requirements:

**A. FastHSM Kernel Refactoring** The `FastHSM` engine currently relies on a static, globally injected `HsmTraceBuffer` managed via `HsmKernelCore.SetTraceBuffer(buffer)`. This violates per-entity concurrency and prevents ECS component-based tracing. You must strip this static pointer out of the kernel. Instead, retrieve the `HsmTraceWorkingMemory1024*` pointer inside `HsmTickSystem` and pass it down into the core via the `HsmKernelBridge` struct or append it to the `HsmCommandWriter` payload.

**B. ECS Component Registration** The new 1024-byte unmanaged components will cause command buffer crashes if not explicitly registered. You must add `world.RegisterComponent<BTreeTraceWorkingMemory1024>()` and `world.RegisterComponent<HsmTraceWorkingMemory1024>()` to the `CognitiveComponentRegistry.RegisterAll` method.

**C. Ring Buffer Overwrite Handling (The Header)** Since the 1024-byte component represents a fixed ring buffer without automatic capacity expansion, your unmanaged write logic must handle wrap-around safely. The component needs a header storing `ushort WritePos` and `ushort Count`. When writing a 16-byte record, calculate `offset = WritePos % 1020` (leaving 4 bytes for the header). If the buffer wraps, you increment `WritePos` but cap `Count` at the maximum number of records (~63). Your UI renderer must account for this wrap-around when iterating from oldest to newest.





the UI action events do not need to be managed as they carry just primitive blittable data fields, right?

having a separate event for every type of UI action also seems liek wasting the resources (extra buffer in the event bus), maybe we can make some union-like command for UI actions. And introduce injectable UI action handlers invoked by unified UI action handling system (maybe it already exists?)

The context menu is now built using json definitions, can you check?

Instead of special initialization system for auto-enabling the trace buffer components, couldn't we add a specialized tkb translator that checks the system setting and makes sure the ECS components are created?

Also the trace buffer unmanaged 1KB components should have their fixed byte buffer capacity set to perfectly fill the 1024 byte space.

You are correct on all points. The framework already provides the exact unified patterns you are describing, and leveraging them keeps the architecture clean and performant.

Here is the refined implementation strategy based on the existing framework capabilities:

1. Unified UI Action Handling

UI action events do not need to be managed, nor do they require separate event types per action. The framework already implements a unified, unmanaged command bus for this exact purpose:

- Context Menu JSON Definitions

The framework has indeed transitioned to a JSON-driven context menu architecture via the `ContextMenuState.MenuJson` component. The `JsonEntityContextMenuHandler` parses this state to populate the menu.

However, because AI tracing is a local debugging feature and not a synchronized domain action, you should bypass the network-replicated JSON and use the `LambdaEntityContextMenuHandler` instead. The editor already uses this pattern for local tools (like "Center on entity" or "Delete"). You register the lambda, check if the entity has a `BehaviorState`, and add a "Toggle AI Trace" item. When clicked, the callback simply publishes the `GlobalActionRequestedEvent` with your new Action ID to the interaction bus.

3. TKB Translator for Auto-Enabling

Using an `ITkbEntityTranslator` is the architecturally superior approach to auto-enabling tracing. It completely eliminates the need for a scanning system that iterates over entities every frame.

You will implement an `AiDiagnosticsTkbTranslator` that plugs into the existing TKB injection pipeline. During entity genesis, the translator checks the global debug configuration. If auto-tracing is enabled, it checks the `BehaviorProfileDto.BrainTier` of the blueprint. If it is a BTree or HSM, the translator immediately calls `repo.AddComponent` to stamp the entity with the corresponding `BTreeTraceWorkingMemory1024` or `HsmTraceWorkingMemory1024` component. This guarantees the buffer is present before the first cognitive tick ever executes.

4. 1024-Byte Memory Layout

To maximize the capacity while strictly adhering to the `EntityCommandBuffer`'s `MaxComponentSize` limit of 1024 bytes, the unmanaged component must be explicitly packed.

Assuming a 4-byte header to track the ring buffer state, the struct definition must look exactly like this:

```
[StructLayout(LayoutKind.Sequential, Size = 1024)]
[ComponentId(YourAssignedId)]
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct BTreeTraceWorkingMemory1024
{
    // 4-byte header
    public ushort WritePos;
    public ushort RecordCount;
    
    // 1020-byte payload buffer
    public fixed byte Buffer[12];
}
```

Since each `TraceRecord` is 16 bytes, the 1020-byte buffer perfectly holds 63 discrete records (`1020 / 16 = 63.75`), leaving 12 bytes of unused trailing padding within the 1024-byte footprint. When writing to the buffer, your tracing API will wrap the cursor using `offset = WritePos % 1008` (63 records * 16 bytes) to prevent memory access violations.





good, the specification seem clear, now let's start going into details. pls enumerate all the 'tasks' necessary to change the system from v213 state to the desired state with full behavior tracing capabilities. Then we will go one by one and will detail each of them. Now just enumerate the 'tasks'

**Unmanaged Memory Definitions:** Define the `BTreeTraceWorkingMemory1024` and `HsmTraceWorkingMemory1024` structs to conform to the strict 1024-byte `MaxComponentSize` limit. Define the BTree opcodes, trace record unions, and ring buffer header layouts. Register both explicitly in the component registry.**FastBTree Kernel Instrumentation:** Augment `BTreeContext` with the unmanaged trace pointer. Hook `Fbt.Runtime.Interpreter` to automatically emit structural opcodes (`NodeEvaluated`, `Scope`, `Wait`). Expose domain-cooperative APIs (`WriteChannelMutated`, `WriteError`) on the trace buffer.**FastHSM Kernel Refactoring:** Eradicate the global static `_traceBuffer` from `HsmKernelCore`. Route the per-entity trace buffer pointer through `HsmKernelBridge` to strictly isolate execution state per entity during transitions and guard evaluations.**Tick Systems Integration:** Update `BTreeTickSystem` and `HsmTickSystem<T>` to retrieve the unmanaged trace components in O(1) time and inject their pointers into the execution contexts. Enforce ring-buffer wrap-around arithmetic here or within the buffer write methods.**Diagnostic Rendering (ImGui):** Implement `BTreeTraceWorkingMemoryRenderer` and `HsmTraceWorkingMemoryRenderer` conforming to `IEntityAwareImGuiRenderer`. Use `BehaviorRegistry` to unpack `BehaviorTreeBlob` strings and `MachineMetadata` to decode raw indices and hashes on the fly.**JSON Dump Serialization:** Implement `IEntityScenarioTranslator` for both trace components to support copying to the clipboard and diagnostic dumps. Implement `Extract` to project the binary rings into JSON arrays, and leave `Inject` strictly empty to prevent loading transient traces from disk. Register them in `HrotScenarioSerializerFactory`.**UI Control & Context Menu:** Define a new constant in `GlobalActionIds` and map it via `GlobalActionRegistry`. Inject a "Toggle AI Tracing" item using `LambdaEntityContextMenuHandler` that publishes the unmanaged `GlobalActionRequestedEvent` to the interaction bus.**System-Wide Auto-Enable (TKB):** Add an `AutoEnableAiTracing` flag to the global debug settings. Implement an `ITkbEntityTranslator` that evaluates this setting during entity genesis and injects the 1KB trace components into matching AI blueprints before the first tick.**BehaviorLog Integration:** Introduce a transient tag component (e.g., `AiTraceLogRoutingTag`). Add logic at the end of the tick systems to read the newest opcodes from the ring buffer and decode them to `BehaviorLog.Trace` calls, gated strictly behind a zero-allocation `BehaviorLog.IsTraceEnabled` check.





regarding point 9 BehaviorLog integration. I can not afford having separate AiTraceLogRoutingTag component. Our component type space is very limited (256 component types only). I would rather introduce (or reuse some existing) transient component that maintains various boolean flags like this one. The entity inspector will allow (via existing StructEdit & imgui) to interactively change those flags.

You are absolutely right to protect the 256 component type space. Consuming a type ID for a single-purpose tag is an architectural anti-pattern in this framework, especially when multiple diagnostic toggles will inevitably be needed.

To solve this cleanly while fully leveraging the existing `StructEdit` and ImGui integration, you should introduce a single, consolidated `AiDebugState` transient component.

By utilizing a `[Flags]` enum inside this component, `StructEdit` will automatically render the fields as interactive, individual checkboxes in the Entity Inspector without requiring you to write a custom `[ImGuiRenderer]`.

1. The Consolidated Component

Define an unmanaged struct carrying a bitmask enum. This single component will replace both the `AiTraceEnabledTag` (for recording the buffer) and the `AiTraceLogRoutingTag` (for emitting to NLog).

```
using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    [Flags]
    public enum AiDebugFeatures : byte
    {
        None              = 0,
        EnableTraceBuffer = 1 << 0, // Activates the 1KB unmanaged trace buffer
        EmitToMessageLog  = 1 << 1, // Routes trace opcodes to BehaviorLog/NLog
        // ... 6 remaining bits for future AI debug toggles
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(AssignAnAvailableIdHere)] // e.g., an unused ID in the 20-49 Behavior block
    [DataPolicy(DataPolicy.Transient)]     // Excludes from .fdp recordings and scenario JSON
    public struct AiDebugState
    {
        public AiDebugFeatures Features;
    }
}
```

2. Zero-Effort UI Integration

Because `StructEdit` explicitly supports `[Flags]` enums natively, adding this component requires zero UI code.

When you select an entity in the inspector and expand `AiDebugState`, `ComponentEditDrawer.DrawPrimitiveInput` dynamically detects the `FlagsAttribute`, loops through the enum values, and emits an `ImGui.Checkbox` for each bit. When you toggle a checkbox, it mutates the bitmask and writes it back to the ECS.

3. Execution in the Tick System

Your `BTreeTickSystem` and `HsmTickSystem` will now check this single component in O(1) time. The CPU branch predictor handles this efficiently, and because it is marked `DataPolicy.Transient`, modifying it interactively does not dirty your delta chunks for the Flight Recorder.

```
// Inside BTreeTickSystem.Execute:
bool enableBuffer = false;
bool emitToLog = false;

if (repo.HasComponent<AiDebugState>(entity))
{
    var debugState = repo.GetComponentRO<AiDebugState>(entity);
    enableBuffer = (debugState.Features & AiDebugFeatures.EnableTraceBuffer) != 0;
    emitToLog = (debugState.Features & AiDebugFeatures.EmitToMessageLog) != 0;
}

// 1. Conditionally inject the trace buffer pointer
if (enableBuffer && repo.HasComponent<BTreeTraceWorkingMemory1024>(entity))
{
    // Pass pointer to Context
}

// ... tick the BTree ...

// 2. Conditionally decode and route to NLog
if (emitToLog && BehaviorLog.IsTraceEnabled)
{
    // Read the newest records from the buffer and call BehaviorLog.Trace()
}
```

4. Initialization

Instead of a context menu action creating specific trace buffers, your UI Context Menu handler simply calls `repo.AddComponent(entity, new AiDebugState { Features = AiDebugFeatures.EnableTraceBuffer })`.

A lightweight setup system running in `SystemPhase.BeforeSync` can watch for entities that have `AiDebugState` with `EnableTraceBuffer` set, but are missing `BTreeTraceWorkingMemory1024`, and instantiate the 1KB buffer for them dynamically.





the AiDebugState should be more generic, not focused to just AI, it is a generic DebugState and we will extend it in the future my many other fields.

The lightwight syste, scanning these is a good idea;

UI Context Menu handler cannot call AddComponent as it would replace/rewrite the component if already existing; we should introduce a managed fdp event that is able to change the debug settings component in some generic way, for example carrying short json stringized object with boolean fields { 'Behavior': {'EnableTraceBuffer': true}, 'OtherFeatureGroup': {'EnableFeature1': true, 'EnableFeature2': false}, 'NonEnumIntegerDebugSetting':15, 'StringDebugSetting':'Hello'}; each feature group cound be represented as standalone enum field, and EnableFeature1 and EnableFeature2 are the enum item names. Some automatic expression tree based generator can translate the json into adjusting the component fields appropriately.

The transition to a generic `DebugState` and a data-driven patching mechanism resolves the initialization race conditions and prevents destructive `AddComponent` overwrites.

To achieve this, we will introduce a transient `DebugState` component, a managed command event, and an expression-tree-based compiler that generates zero-reflection setter delegates at startup. This perfectly mirrors the framework's existing `JsonAttributeCompiler` and `FdpAutoSerializer` architectures.

Here is the architectural implementation.

1. The Generic Component and Event

Define the generic state and the managed event. By using `[Flags]` enums, we establish the feature groups.

```
[Flags]
public enum BehaviorDebugFlags : uint
{
    None = 0,
    EnableTraceBuffer = 1 << 0,
    EmitToLog = 1 << 1
}

[StructLayout(LayoutKind.Sequential)]
[ComponentId(AssignAnAvailableIdHere)]
[DataPolicy(DataPolicy.Transient)]
public struct DebugState
{
    public BehaviorDebugFlags Behavior;
    public int NonEnumIntegerDebugSetting;
    public FixedString32 StringDebugSetting;
}

/// <summary>
/// Managed command published by the UI to patch the debug state of an entity.
/// </summary>
public sealed class PatchDebugStateCommand
{
    public Entity Target;
    public string PatchJson = string.Empty;
}
```

2. The Expression-Tree Patcher Compiler

To translate nested JSON like `{"Behavior": {"EnableTraceBuffer": true}}` into structural mutations without runtime reflection, we build a dictionary of compiled delegates keyed by the JSON property name.

For `[Flags]` enums, the generator iterates the enum values and emits bitwise OR/AND assignment expressions based on the incoming boolean value. For primitive fields, it emits direct assignments.

```
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

public delegate void DebugStateSetter(ref DebugState state, JsonElement element);

public static class DebugStatePatchCompiler
{
    private static readonly Dictionary<string, DebugStateSetter> _setters = new(StringComparer.OrdinalIgnoreCase);

    public static void Build()
    {
        var stateParam = Expression.Parameter(typeof(DebugState).MakeByRefType(), "state");
        var elementParam = Expression.Parameter(typeof(JsonElement), "element");

        foreach (var field in typeof(DebugState).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var fieldAccess = Expression.Field(stateParam, field);

            if (field.FieldType.IsEnum && field.FieldType.GetCustomAttribute<FlagsAttribute>() != null)
            {
                // For [Flags], we expect a nested JSON object: { "EnableTraceBuffer": true, "EmitToLog": false }
                _setters[field.Name] = CompileFlagsPatcher(field.FieldType, fieldAccess, stateParam, elementParam);
            }
            else
            {
                // For primitives/strings, we expect a direct value: { "NonEnumIntegerDebugSetting": 15 }
                _setters[field.Name] = CompilePrimitivePatcher(field.FieldType, fieldAccess, stateParam, elementParam);
            }
        }
    }

    public static void ApplyPatch(ref DebugState state, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (_setters.TryGetValue(prop.Name, out var setter))
            {
                setter(ref state, prop.Value);
            }
        }
    }

    private static DebugStateSetter CompileFlagsPatcher(Type enumType, Expression fieldAccess, ParameterExpression stateParam, ParameterExpression elementParam)
    {
        var getBoolMethod = typeof(JsonElement).GetMethod(nameof(JsonElement.GetBoolean))!;
        var getPropertyMethod = typeof(JsonElement).GetMethod(nameof(JsonElement.TryGetProperty))!;
        
        var statements = new List<Expression>();
        var propElementVar = Expression.Variable(typeof(JsonElement), "propElement");

        foreach (var enumName in Enum.GetNames(enumType))
        {
            if (enumName == "None") continue;

            object enumValue = Enum.Parse(enumType, enumName);
            var typedEnumValue = Expression.Constant(enumValue, enumType);

            // If element.TryGetProperty("EnumName", out propElement)
            var tryGetProp = Expression.Call(elementParam, getPropertyMethod, Expression.Constant(enumName), propElementVar);
            
            // if (propElement.GetBoolean()) field |= EnumValue; else field &= ~EnumValue;
            var isTrue = Expression.Call(propElementVar, getBoolMethod);
            
            var bitwiseOr = Expression.Assign(fieldAccess, Expression.Or(fieldAccess, typedEnumValue));
            var bitwiseAndNot = Expression.Assign(fieldAccess, Expression.And(fieldAccess, Expression.Not(typedEnumValue)));

            var condition = Expression.IfThen(
                tryGetProp,
                Expression.IfThenElse(isTrue, bitwiseOr, bitwiseAndNot)
            );

            statements.Add(condition);
        }

        var block = Expression.Block(new[] { propElementVar }, statements);
        return Expression.Lambda<DebugStateSetter>(block, stateParam, elementParam).Compile();
    }

    private static DebugStateSetter CompilePrimitivePatcher(Type fieldType, Expression fieldAccess, ParameterExpression stateParam, ParameterExpression elementParam)
    {
        Expression readValue;
        if (fieldType == typeof(int))
            readValue = Expression.Call(elementParam, typeof(JsonElement).GetMethod(nameof(JsonElement.GetInt32))!);
        else if (fieldType == typeof(string) || fieldType == typeof(Fdp.Core.FixedString32))
        {
            var getString = Expression.Call(elementParam, typeof(JsonElement).GetMethod(nameof(JsonElement.GetString))!);
            readValue = fieldType == typeof(string) ? getString : Expression.New(typeof(Fdp.Core.FixedString32).GetConstructor(new[] { typeof(string) })!, getString);
        }
        else
            throw new NotSupportedException($"Unsupported DebugState field type: {fieldType}");

        var assign = Expression.Assign(fieldAccess, readValue);
        return Expression.Lambda<DebugStateSetter>(assign, stateParam, elementParam).Compile();
    }
}
```

3. The Patching System

Create an ingress system running in `SystemPhase.Input` to drain the commands. This system enforces the existence of `DebugState` using `repo.HasComponent` and `repo.AddComponent`, ensuring an existing component is never indiscriminately overwritten.

```
[UpdateInPhase(SystemPhase.Input)]
public sealed class DebugStatePatchSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        var commands = repo.Bus.ReadManaged<PatchDebugStateCommand>();

        foreach (var cmd in commands)
        {
            if (!repo.IsAlive(cmd.Target)) continue;

            // 1. Ensure the component exists
            if (!repo.HasComponent<DebugState>(cmd.Target))
            {
                repo.AddComponent(cmd.Target, new DebugState());
            }

            // 2. Read, modify via expression trees, and write back
            ref var state = ref repo.GetComponentRW<DebugState>(cmd.Target);
            DebugStatePatchCompiler.ApplyPatch(ref state, cmd.PatchJson);
        }
    }
}
```

4. UI Context Menu Handler

Your context menu lambda handler now simply pushes the stringified JSON payload to the bus, remaining entirely ignorant of the underlying component structure.

```
builder.AddItem("Enable AI Trace", () => 
{
    _world.Bus.PublishManaged(new PatchDebugStateCommand 
    {
        Target = entity,
        PatchJson = "{\"Behavior\": {\"EnableTraceBuffer\": true}}"
    });
});
```

By explicitly mapping structural JSON to compiled operations, you avoid the runtime overhead of `PropertyInfo.SetValue` while keeping the component strictly unmanaged.





good. now pls let's detail point 1. **Unmanaged Memory Definitions:**. Exact definitions please

Here are the exact unmanaged memory definitions required for Task 1.

As agreed, by moving to a strict 1024-byte unmanaged component, we must abandon the 40-byte variable-length `FixedString32` opcode. Every trace record must be exactly 16 bytes to ensure safe, zero-allocation ring buffer arithmetic and fast serialization.

1. BTree Opcodes

Define the opcodes specific to the BTree execution flow. These map to the structural and domain events discussed previously.

```
namespace Fdp.Toolkit.Behavior.Diagnostics
{
    public enum BTreeTraceOpCode : byte
    {
        None = 0,
        NodeEvaluated = 1,
        ScopePushed = 2,
        ScopePopped = 3,
        WaitStarted = 4,
        WaitCompleted = 5,
        ChannelMutated = 6,
        Error = 0x0E
    }
}
```

2. The 16-Byte BTree Trace Record Union

This struct models the exact 16-byte union pattern used by the existing FastHSM `TraceRecord`. Using `[FieldOffset]` creates a C-style union where the payload fields share the final 8 bytes.

```
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct BTreeTraceRecord
    {
        // ── Header (8 bytes) ────────────────────────────────────────────────
        [FieldOffset(0)] public BTreeTraceOpCode OpCode;
        [FieldOffset(1)] public byte Reserved;
        [FieldOffset(2)] public ushort Timestamp;   // Simulation Tick
        [FieldOffset(4)] public uint InstanceId;    // BehaviorState.InstanceId

        // ── Payload Union (8 bytes) ─────────────────────────────────────────
        
        // NodeEvaluated
        [FieldOffset(8)] public ushort NodeIndex;
        [FieldOffset(10)] public NodeStatus Status; // byte

        // ScopePushed / ScopePopped
        [FieldOffset(8)] public ushort StackDepth;

        // WaitStarted / WaitCompleted
        [FieldOffset(8)] public float Duration;

        // ChannelMutated
        [FieldOffset(8)] public ChannelKind Channel;       // byte
        [FieldOffset(10)] public ushort ActiveAction;      // ushort (aligned to offset 10)
        [FieldOffset(12)] public NodeStatus ChannelStatus; // byte

        // Error
        [FieldOffset(8)] public ushort ErrorCode;
    }
}
```

3. The 1024-Byte Unmanaged Components

These components define the working memory for the ring buffers. They are sized to exactly 1024 bytes to satisfy the `EntityCommandBuffer` maximum component size constraint.

The layout reserves 4 bytes for the header, leaving exactly 1020 bytes for the `fixed byte` array. Because 1020 divided by 16 is 63.75, exactly 63 records fit into this buffer. The tracing API will calculate offsets using `_writePos % 1008`.

```
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    // Note: Assign specific IDs in BehaviorApplicationComponentIds for these
    [StructLayout(LayoutKind.Sequential, Size = 1024)]
    [ComponentId(BehaviorApplicationComponentIds.BTreeTraceWorkingMemory)]
    [DataPolicy(DataPolicy.NoSave)] // Recorded to FDP for replay, but excluded from TKB JSON [5, 6].
    public unsafe struct BTreeTraceWorkingMemory1024
    {
        public const int CapacityRecords = 63;
        public const int PayloadBytes = 1020;

        // 4-byte header
        public ushort WritePos;
        public ushort RecordCount;
        
        // 1020-byte payload buffer
        public fixed byte Buffer[PayloadBytes];
    }

    [StructLayout(LayoutKind.Sequential, Size = 1024)]
    [ComponentId(BehaviorApplicationComponentIds.HsmTraceWorkingMemory)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct HsmTraceWorkingMemory1024
    {
        public const int CapacityRecords = 63;
        public const int PayloadBytes = 1020;

        // 4-byte header
        public ushort WritePos;
        public ushort RecordCount;
        
        // 1020-byte payload buffer
        public fixed byte Buffer[PayloadBytes];
    }
}
```

*(You will need to define* *BTreeTraceWorkingMemory* *and* *HsmTraceWorkingMemory* *constants inside* *Fdp.Toolkit.Behavior.Components.BehaviorApplicationComponentIds* *picking unused IDs in the 160-199 range**.)*

4. ECS Component Registration

To ensure the `EntityCommandBuffer` does not crash during playback or when the UI toggle applies the component, both 1KB unmanaged structs must be registered with the engine at startup.

Add the registrations to `CognitiveComponentRegistry.RegisterAll` in `Fdp.Toolkits`:

```
// Inside FDP/Toolkits/Fdp.Toolkits/SimHost/CognitiveComponentRegistry.cs
public static void RegisterAll(EntityRepository world)
{
    // ... existing registrations ...
    
    world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.BTreeTraceWorkingMemory1024>();
    world.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.HsmTraceWorkingMemory1024>();
}
```





good! now please detail 2. **FastBTree Kernel Instrumentation:**

To instrument the FastBTree kernel with zero-allocation tracing, you must embed the unmanaged pointer into the execution context, inject automatic hooks directly into the `Interpreter` pipeline, and expose the domain-cooperative APIs on the unmanaged component struct.

Because the `Fbt.Kernel` must remain decoupled from the specific `Fdp.Toolkits` domain, the safest architectural approach is to define the unmanaged ring-buffer writing methods directly on the `BTreeTraceWorkingMemory1024` struct, and pass its pointer via the context.

Here is the exact implementation detail for Task 2.

1. Augment `BTreeContext`

The `BTreeContext` struct is instantiated once per tick per entity. Add the unmanaged pointer so it naturally cascades down through all node logic delegates.

```
public struct BTreeContext : IAIContext
{
    public Entity Self;
    public EntityRepository World;
    
    // NEW: Trace buffer pointer. Null if tracing is disabled via AiDebugState.
    public unsafe BTreeTraceWorkingMemory1024* TraceBuffer;

    internal float _deltaTime;
    internal float _time;
    internal int   _frameCount;
    internal float[]? _floatParams;
    internal int[]?   _intParams;
    
    public float DeltaTime => _deltaTime;
    public float Time => _time;
    public int FrameCount => _frameCount;
    
    // ... remaining IAIContext methods
}
```

2. Implement the Unmanaged Write API

On your `BTreeTraceWorkingMemory1024` component, implement the ring-buffer math and expose the write APIs. This guarantees O(1) pointer arithmetic and encapsulates the wrap-around logic.

```
public unsafe struct BTreeTraceWorkingMemory1024
{
    public const int CapacityRecords = 63;
    public const int PayloadBytes = 1020;

    public ushort WritePos;
    public ushort RecordCount;
    public fixed byte Buffer[PayloadBytes];

    private BTreeTraceRecord* NextRecord()
    {
        int offset = WritePos % PayloadBytes;
        WritePos += 16;
        if (RecordCount < CapacityRecords) RecordCount++;
        return (BTreeTraceRecord*)(Unsafe.AsPointer(ref Buffer) + offset);
    }

    // --- Automatic Engine APIs ---
    
    public void WriteNodeEvaluated(int nodeIndex, NodeStatus status, ushort tick)
    {
        var record = NextRecord();
        record->OpCode = BTreeTraceOpCode.NodeEvaluated;
        record->Timestamp = tick;
        record->NodeIndex = (ushort)nodeIndex;
        record->Status = status;
    }

    public void WriteWaitStarted(int nodeIndex, float duration, ushort tick)
    {
        var record = NextRecord();
        record->OpCode = BTreeTraceOpCode.WaitStarted;
        record->Timestamp = tick;
        record->NodeIndex = (ushort)nodeIndex;
        record->Duration = duration;
    }

    // ... Implement WriteWaitCompleted, WriteScopePushed identically ...

    // --- Domain-Cooperative APIs ---
    
    public void WriteChannelMutated(int nodeIndex, ChannelKind channel, ushort actionId, NodeStatus status, ushort tick)
    {
        var record = NextRecord();
        record->OpCode = BTreeTraceOpCode.ChannelMutated;
        record->Timestamp = tick;
        record->NodeIndex = (ushort)nodeIndex;
        record->Channel = channel;
        record->ActiveAction = actionId;
        record->ChannelStatus = status;
    }

    public void WriteError(int nodeIndex, ushort errorCode, ushort tick)
    {
        var record = NextRecord();
        record->OpCode = BTreeTraceOpCode.Error;
        record->Timestamp = tick;
        record->NodeIndex = (ushort)nodeIndex;
        record->ErrorCode = errorCode;
    }
}
```

3. Hook `Fbt.Runtime.Interpreter` (Automatic Opcodes)

Inject tracing directly into the kernel's execution evaluations. Wrap the pointer checks in `unsafe` blocks. If the CPU branch predictor sees the pointer is null, it bypasses the block with virtually zero overhead.

**Node Evaluations (****ExecuteAction****)** Intercept evaluations inside `ExecuteAction`:

```
private NodeStatus ExecuteAction(
    int nodeIndex, ref NodeDefinition node, ref TBlackboard bb, 
    ref BehaviorTreeState state, ref TContext ctx)
{
    if (node.PayloadIndex < 0 || node.PayloadIndex >= _actionDelegates.Length)
        return NodeStatus.Failure;

    var actionDelegate = _actionDelegates[node.PayloadIndex];
    var status = actionDelegate(ref bb, ref state, ref ctx, node.PayloadIndex);

    // INJECT: Trace Evaluation
    unsafe {
        if (ctx.TraceBuffer != null)
            ctx.TraceBuffer->WriteNodeEvaluated(nodeIndex, status, (ushort)ctx.FrameCount);
    }

    if (status == NodeStatus.Running) {
        state.RunningNodeIndex = (ushort)nodeIndex;
    } else if (state.RunningNodeIndex == nodeIndex) {
        state.RunningNodeIndex = 0;
    }

    return status;
}
```

**Timers (****ExecuteWait****)** Capture the start and end of asynchronous durations in `ExecuteWait`:

```
private NodeStatus ExecuteWait(
    int nodeIndex, ref NodeDefinition node, ref TBlackboard bb, 
    ref BehaviorTreeState state, ref TContext ctx)
{
    float duration = _blob.FloatParams[node.PayloadIndex];

    if (state.RunningNodeIndex == nodeIndex)
    {
        var token = new AsyncToken(state.AsyncData);
        float elapsed = ctx.Time - token.FloatA;
        if (elapsed >= duration)
        {
            // INJECT: Trace Completed
            unsafe {
                if (ctx.TraceBuffer != null)
                    ctx.TraceBuffer->WriteWaitCompleted(nodeIndex, duration, (ushort)ctx.FrameCount);
            }
            state.RunningNodeIndex = 0;
            return NodeStatus.Success;
        }
        return NodeStatus.Running;
    }
    else
    {
        // INJECT: Trace Started
        unsafe {
            if (ctx.TraceBuffer != null)
                ctx.TraceBuffer->WriteWaitStarted(nodeIndex, duration, (ushort)ctx.FrameCount);
        }
        var token = AsyncToken.FromFloat(ctx.Time, 0);
        state.AsyncData = token.PackedValue;
        state.RunningNodeIndex = (ushort)nodeIndex;
        return NodeStatus.Running;
    }
}
```

**Scopes (****BehaviorTreeState****)** Instead of guessing branch depth inside `ExecuteSequence`, trace scope changes securely when the engine actually enters or exits a subtree. Modify `PushNode` and `PopNode` in `BehaviorTreeState` to accept the pointer:

```
public unsafe void PushNode(ushort nodeIndex, BTreeTraceWorkingMemory1024* traceBuffer = null, ushort tick = 0)
{
    if (StackPointer < 7) 
    {
        StackPointer++;
        NodeIndexStack[StackPointer] = nodeIndex;
        if (traceBuffer != null) traceBuffer->WriteScopePushed(StackPointer, tick);
    }
}

public unsafe void PopNode(BTreeTraceWorkingMemory1024* traceBuffer = null, ushort tick = 0)
{
    if (StackPointer > 0)
    {
        NodeIndexStack[StackPointer] = 0; 
        StackPointer--;
        if (traceBuffer != null) traceBuffer->WriteScopePopped(StackPointer, tick);
    }
}
```





the Fbt.Runtime.Interpretter should not know about exisstence of BTreeTraceWorkingMemory1024, the trace buffer api should be agnostic of concrete implementation of the buffer, is that fullfilled?

The previous proposal violated the dependency inversion principle by coupling the core `Fbt.Kernel` logic to `BTreeTraceWorkingMemory1024`, a domain-specific component from `Fdp.Toolkits`. The `Interpreter` only knows about the context through the generic `TContext` type constrained by `IAIContext`. Attempting to access a specific unmanaged buffer pointer directly inside `ExecuteAction` would fail to compile without breaking this boundary.

To achieve strict decoupling while preserving zero-allocation, zero-overhead tracing, you must apply the Interface Segregation Principle to the context and leverage JIT devirtualization on constrained generics.

Here is the architectural implementation.

1. Define the Tracing Contract in `Fbt.Kernel`

Introduce a dedicated interface inside the `Fbt` namespace. This keeps the core engine completely ignorant of how or where traces are stored.

```
namespace Fbt
{
    public interface ITreeTracer
    {
        void TraceNodeEvaluated(int nodeIndex, NodeStatus status);
        void TraceWaitStarted(int nodeIndex, float duration);
        void TraceWaitCompleted(int nodeIndex, float duration);
        void TraceScopePushed(ushort stackDepth);
        void TraceScopePopped(ushort stackDepth);
    }
}
```

2. Constrain the Interpreter

Update `Interpreter<TBlackboard, TContext>` to require this new capability. Because `TContext` is a `struct`, interface calls on it are constrained and will be aggressively devirtualized and inlined by the JIT compiler.

```
public class Interpreter<TBlackboard, TContext> : ITreeRunner<TBlackboard, TContext>
    where TBlackboard : struct
    where TContext : struct, IAIContext, ITreeTracer
```

3. Instrument the Engine Agnostically

Inside `ExecuteAction`, `ExecuteWait`, and `BehaviorTreeState`, invoke the interface methods on the context. The kernel knows nothing about memory layouts or null pointers.

```
private NodeStatus ExecuteAction(
    int nodeIndex, ref NodeDefinition node, ref TBlackboard bb, 
    ref BehaviorTreeState state, ref TContext ctx)
{
    if (node.PayloadIndex < 0 || node.PayloadIndex >= _actionDelegates.Length)
        return NodeStatus.Failure;

    var actionDelegate = _actionDelegates[node.PayloadIndex];
    var status = actionDelegate(ref bb, ref state, ref ctx, node.PayloadIndex);

    // Agnostic trace emission. 
    ctx.TraceNodeEvaluated(nodeIndex, status);

    // ... running state logic
    return status;
}
```

4. Implement the Specific Buffer Logic in the Application Layer

The concrete `BTreeContext` resides in `Fdp.Toolkit.Behavior` and implements the new interface. It encapsulates the `unsafe` pointer to `BTreeTraceWorkingMemory1024` and executes the fast null-checks internally.

```
public struct BTreeContext : IAIContext, ITreeTracer
{
    public Entity Self;
    public EntityRepository World;
    
    public unsafe BTreeTraceWorkingMemory1024* TraceBuffer;

    // ... existing fields ...

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceNodeEvaluated(int nodeIndex, NodeStatus status)
    {
        unsafe {
            if (TraceBuffer != null)
                TraceBuffer->WriteNodeEvaluated(nodeIndex, status, (ushort)FrameCount);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceWaitStarted(int nodeIndex, float duration)
    {
        unsafe {
            if (TraceBuffer != null)
                TraceBuffer->WriteWaitStarted(nodeIndex, duration, (ushort)FrameCount);
        }
    }
    
    // ... implement remaining ITreeTracer methods identically
}
```

This enforces a strict separation of concerns. `Fbt.Kernel` remains a pure, unmanaged-agnostic execution engine, while `Fdp.Toolkit.Behavior.Diagnostics` retains complete control over the memory layout, ring buffer arithmetic, and pointer lifecycles. Because the interface methods are called on a generic struct passed by `ref`, the JIT compiler flattens the `TraceNodeEvaluated` call into the exact same inline `if (TraceBuffer != null)` assembly instruction proposed previously, resulting in zero boxing and zero virtual dispatch overhead.





good. now please let's detail point 3. **FastHSM Kernel Refactoring:**

To fully support per-entity, zero-allocation tracing in FastHSM and enable safe concurrent execution across multiple entities, the kernel must be decoupled from the managed, globally static trace buffer.

Here is the exact architectural refactoring required for the `Fhsm.Kernel` layer.

1. Eradicate the Global Managed State

Currently, `HsmKernelCore` relies on a static, globally injected managed class (`private static HsmTraceBuffer? _traceBuffer`). This inherently violates multi-threaded scaling and per-entity isolation.

Delete the `_traceBuffer` field, the `SetTraceBuffer` method, and the entire managed `HsmTraceBuffer` class. The kernel will no longer own the allocation or lifecycle of the ring buffer.

2. Define the Unmanaged Trace Context

Because `Fhsm.Kernel` cannot depend on the domain-specific 1024-byte ECS components, you must define an unmanaged struct contract that allows the kernel to perform O(1) pointer arithmetic on *any* provided memory block.

Define `HsmTraceContext` directly in `Fhsm.Kernel.Data`. This struct will encapsulate the ring-buffer math previously locked inside the managed class.

```
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    public unsafe struct HsmTraceContext
    {
        public byte* Buffer;
        public ushort* WritePos;
        public ushort* RecordCount;
        public ushort CapacityBytes;
        public ushort MaxRecords;
        
        public TraceLevel FilterLevel;
        public ushort CurrentTick;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteStateChange(uint instanceId, ushort stateIndex, bool isEntry)
        {
            if ((FilterLevel & TraceLevel.StateChanges) == 0) return;

            var record = new TraceStateChange
            {
                Header = new TraceRecordHeader
                {
                    OpCode = isEntry ? TraceOpCode.StateEnter : TraceOpCode.StateExit,
                    Timestamp = CurrentTick,
                    InstanceId = instanceId
                },
                StateIndex = stateIndex
            };
            WriteRecord(&record, sizeof(TraceStateChange));
        }

        // Port WriteEventHandled, WriteTransition, WriteGuardEvaluated identically...

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteRecord(void* recordPtr, int size)
        {
            int offset = *WritePos % CapacityBytes;
            *WritePos += (ushort)size;
            if (*RecordCount < MaxRecords) (*RecordCount)++;

            byte* dst = Buffer + offset;
            Unsafe.CopyBlock(dst, recordPtr, (uint)size);
        }
    }
}
```

3. Refactor the Execution Pipeline (`HsmKernel` and `HsmKernelCore`)

The unmanaged trace context must be passed directly into the execution pipeline, cascading down to all structural evaluation methods.

Modify the public facade in `HsmKernel`:

```
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static unsafe void Update<TInstance, TContext>(
    HsmDefinitionBlob definition,
    ref TInstance instance,
    in TContext context,
    float deltaTime,
    ref CommandPage commandPage,
    HsmTraceContext* traceCtx = null) // INJECTED
    where TInstance : unmanaged
    where TContext : unmanaged
{
    fixed (TInstance* instPtr = &instance)
    fixed (TContext* ctxPtr = &context)
    fixed (CommandPage* cmdPtr = &commandPage)
    {
        HsmKernelCore.UpdateBatchCore(
            definition, instPtr, 1, sizeof(TInstance), 
            ctxPtr, deltaTime, cmdPtr, traceCtx);
    }
}
```

Modify the core unmanaged loop in `HsmKernelCore` to accept the pointer and pass it downward. Replace all internal references to `_traceBuffer.WriteX` with `traceCtx->WriteX`.

```
internal static unsafe void UpdateBatchCore(
    HsmDefinitionBlob definition,
    void* instancePtr,
    int instanceCount,
    int instanceSize,
    void* contextPtr,
    float deltaTime,
    void* commandPagePtr,
    HsmTraceContext* traceCtx) // INJECTED
{
    // ...
    for (int i = 0; i < instanceCount; i++)
    {
        // ...
        ProcessInstancePhase(
            definition, instPtr, instanceSize, contextPtr, 
            deltaTime, header, ref cmdWriter, traceCtx);
    }
}

private static unsafe void ExecuteTransition(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    TransitionDef transition,
    ushort* activeLeafIds,
    int regionCount,
    void* contextPtr,
    ref HsmCommandWriter cmdWriter,
    HsmTraceContext* traceCtx)
{
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    
    // Engine automatically logs structural changes via the provided pointer
    if (traceCtx != null && (header->Flags & InstanceFlags.DebugTrace) != 0)
    {
        traceCtx->WriteTransition(header->MachineId, transition.SourceStateIndex, transition.TargetStateIndex, transition.EventId);
    }
    
    // ... continues exiting and entering states ...
}
```

4. Expose to Domain Code (`HsmKernelBridge`)

While `HsmKernelCore` automatically traces state transitions and evaluation paths, domain-specific cooperative tracing (e.g. logging missing component errors from an Action) requires the user logic to have access to the trace context.

Update the `HsmKernelBridge` struct used in the ECS integration layer so your actions and guards can optionally access the buffer.

```
public unsafe struct HsmKernelBridge
{
    public Entity Self;
    public IntPtr WorldHandle; 
    
    // Allows user-authored HsmAction/HsmGuard methods to write to the trace buffer
    public HsmTraceContext* TraceContext; 
}
```

5. Final ECS Tick System Integration

Inside your `HsmTickSystem<T>`, you construct the agnostic `HsmTraceContext` from the strictly-sized `HsmTraceWorkingMemory1024` right before stepping the kernel.

```
var traceCtx = default(HsmTraceContext);
HsmTraceContext* traceCtxPtr = null;

if (repo.HasComponent<HsmTraceWorkingMemory1024>(entity))
{
    ref var traceMemory = ref repo.GetComponentRW<HsmTraceWorkingMemory1024>(entity);
    
    traceCtx.Buffer = (byte*)Unsafe.AsPointer(ref traceMemory.Buffer);
    traceCtx.WritePos = (ushort*)Unsafe.AsPointer(ref traceMemory.WritePos);
    traceCtx.RecordCount = (ushort*)Unsafe.AsPointer(ref traceMemory.RecordCount);
    traceCtx.CapacityBytes = HsmTraceWorkingMemory1024.PayloadBytes;
    traceCtx.MaxRecords = HsmTraceWorkingMemory1024.CapacityRecords;
    traceCtx.CurrentTick = (ushort)repo.GlobalVersion;
    traceCtx.FilterLevel = TraceLevel.Tier3; // Can be pulled from AiDebugState
    
    traceCtxPtr = &traceCtx;
}

var bridge = new HsmKernelBridge
{
    Self = entity,
    WorldHandle = repo.UnmanagedHandle,
    TraceContext = traceCtxPtr
};

HsmKernel.Update(def.HsmDefinition, ref component, bridge, deltaTime, ref cmdPage, traceCtxPtr);
```





good, now pls detail the  point "4. **Tick Systems Integration:** Update `BTreeTickSystem` and `HsmTickSystem<T>` to retrieve the unmanaged trace components in O(1) time and inject their pointers into the execution contexts. Enforce ring-buffer wrap-around arithmetic here or within the buffer write methods." (what has left, as the MsmTick system has been already described)

To integrate the diagnostic architecture into `BTreeTickSystem` while maintaining the engine's zero-allocation hot path, the system must dynamically resolve the unmanaged memory pointer and inject it into the `BTreeContext`.

Because you established the generic `DebugState` transient component in our previous discussion, the tick system will use O(1) bitmask checks against the `EntityHeader` to conditionally resolve the trace buffer pointer before executing the tree.

Here is the exact implementation detail for modifying `BTreeTickSystem`.

1. Pointer Resolution and Context Injection

Locate the core execution loop inside `BTreeTickSystem.Execute` and inject the pointer resolution immediately before `BTreeContext` construction. By using `System.Runtime.CompilerServices.Unsafe.AsPointer` on the `ref` returned by `GetComponentRW<T>`, you acquire a stable pointer directly into the 64KB `NativeChunkTable` memory block without any heap pinning.

```
foreach (var entity in q)
{
    var behavior = repo.GetComponent<BehaviorState>(entity);
    if (behavior.BrainTier != BehaviorConstants.BrainTierBTree)
        continue;

    if (!_registry.TryGetDefinition(behavior.ActiveBehaviorHash, out var def) || def.BTreeInterpreter == null)
        continue;

    ref var btState    = ref repo.GetComponentRW<BrainBTreeState>(entity);
    ref var blackboard = ref repo.GetComponentRW<BrainBlackboard>(entity);

    // 1. Resolve Trace Pointer (O(1) Memory Access)
    BTreeTraceWorkingMemory1024* tracePtr = null;

    if (repo.HasComponent<DebugState>(entity))
    {
        ref readonly var debugState = ref repo.GetComponentRO<DebugState>(entity);
        
        if ((debugState.Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0 &&
            repo.HasComponent<BTreeTraceWorkingMemory1024>(entity))
        {
            // GetComponentRW updates the chunk version for FDP delta-recording
            ref var traceMem = ref repo.GetComponentRW<BTreeTraceWorkingMemory1024>(entity);
            tracePtr = (BTreeTraceWorkingMemory1024*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref traceMem);
        }
    }

    // 2. Inject into Context
    var context = new BTreeContext
    {
        Self        = entity,
        World       = repo,
        _deltaTime  = deltaTime,
        _frameCount = (int)repo.GlobalVersion, // Passed for trace timestamps
        _floatParams = def.BTreeInterpreter.Blob.FloatParams,
        _intParams   = def.BTreeInterpreter.Blob.IntParams,
        
        TraceBuffer = tracePtr // INJECTED POINTER
    };

    var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);

    // ... existing BehaviorFinishedEvent deduplication ...
}
```

2. Enforcing Ring-Buffer Wrap-Around

Regarding ring-buffer arithmetic, enforcing the wrap-around directly within the `BTreeTickSystem` is an anti-pattern as it leaks memory-layout concerns into the orchestrating system.

Instead, the arithmetic is strictly enforced inside the buffer write methods of `BTreeTraceWorkingMemory1024` (Task 1). Because `BTreeContext` acts as the `ITreeTracer` interface proxy (Task 2), the tick system remains completely ignorant of how the bytes are manipulated.

For completeness on how that arithmetic is safely enforced behind the pointer:

```
// Inside BTreeTraceWorkingMemory1024 struct:
private BTreeTraceRecord* NextRecord()
{
    // WritePos wraps safely within the 1020-byte payload constraint
    int offset = WritePos % PayloadBytes; 
    WritePos += 16;
    
    // Cap RecordCount at exactly 63 to prevent UI iteration overflows
    if (RecordCount < CapacityRecords) 
        RecordCount++;
        
    return (BTreeTraceRecord*)(Unsafe.AsPointer(ref Buffer) + offset);
}
```

By retrieving the unmanaged component strictly via `GetComponentRW<T>`, the `BTreeTickSystem` inherently triggers the engine's chunk-level version bump. If `DebugState` removes the `EnableTraceBuffer` flag, the `tracePtr` resolves to null, `GetComponentRW` is bypassed, the chunk version ceases to increment, and the Flight Recorder delta-compression resumes ignoring the trace array, fulfilling the zero-overhead production constraints.





ok now pls detail the "5. **Diagnostic Rendering (ImGui):** Implement `BTreeTraceWorkingMemoryRenderer` and `HsmTraceWorkingMemoryRenderer` conforming to `IEntityAwareImGuiRenderer`. Use `BehaviorRegistry` to unpack `BehaviorTreeBlob` strings and `MachineMetadata` to decode raw indices and hashes on the fly."

To expose the raw unmanaged trace buffers in the ImGui Entity Inspector while maintaining zero allocations during execution, we implement the `IEntityAwareImGuiRenderer` interface. This allows the renderers to intercept the rendering of the `BTreeTraceWorkingMemory1024` and `HsmTraceWorkingMemory1024` components.

By utilizing the `IInspectableSession` context, the renderers can read the entity's `BehaviorState` component to retrieve the `ActiveBehaviorHash`, which is then used to look up the static compilation metadata from the `BehaviorRegistry`. This cleanly separates the heavy string representations from the unmanaged execution pipeline.

1. Behavior Tree Trace Renderer

The BTree renderer unwinds the 1024-byte ring buffer in logical chronological order and uses the `BehaviorTreeBlob.DebugMetadata` to translate raw node indices into authoring-tool labels.

```
using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using ImGuiNET;

namespace Hrot.Presentation.Renderers
{
    [ImGuiRenderer(typeof(BTreeTraceWorkingMemory1024))]
    public sealed class BTreeTraceWorkingMemoryRenderer : IEntityAwareImGuiRenderer
    {
        // Injected at composition root
        public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

        public string? GetSummary(object value) => "BTree Execution Trace";
        public bool RenderValue(object value) => false; // Fallback

        public string? GetSummary(IInspectableSession session, Entity entity, object value) 
            => GetSummary(value);

        public unsafe bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
        {
            doubleClickedPath = null;
            if (value is not BTreeTraceWorkingMemory1024 traceData) return false;

            ImGui.TextDisabled($"Records: {traceData.RecordCount} | Cursor: {traceData.WritePos}");
            ImGui.Separator();

            if (traceData.RecordCount == 0) return true;

            // Resolve the BehaviorTreeBlob via the ActiveBehaviorHash
            Fbt.BehaviorTreeBlob? blob = null;
            if (BehaviorRegistryAccessor != null && session.HasComponent(entity, typeof(BehaviorState)))
            {
                var state = (BehaviorState)session.GetComponent(entity, typeof(BehaviorState))!;
                if (BehaviorRegistryAccessor.TryGetDefinition(state.ActiveBehaviorHash, out var def))
                {
                    blob = def.BTreeInterpreter?.Blob;
                }
            }

            if (ImGui.BeginTable("BTreeTraceTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Tick", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableSetupColumn("OpCode", ImGuiTableColumnFlags.WidthFixed, 100f);
                ImGui.TableSetupColumn("Node", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 60f);
                ImGui.TableHeadersRow();

                int capacityBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
                int recordSize = sizeof(BTreeTraceRecord);
                
                fixed (byte* bufferPtr = traceData.Buffer)
                {
                    int startOffset = traceData.RecordCount == BTreeTraceWorkingMemory1024.CapacityRecords
                        ? traceData.WritePos % capacityBytes 
                        : 0;

                    for (int i = 0; i < traceData.RecordCount; i++)
                    {
                        int offset = (startOffset + (i * recordSize)) % capacityBytes;
                        BTreeTraceRecord* record = (BTreeTraceRecord*)(bufferPtr + offset);

                        ImGui.TableNextRow();
                        
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted(record->Timestamp.ToString());

                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextUnformatted(record->OpCode.ToString());

                        ImGui.TableSetColumnIndex(2);
                        string nodeName = blob?.DebugMetadata != null && record->NodeIndex < blob.DebugMetadata.Length
                            ? blob.DebugMetadata[record->NodeIndex].Label
                            : $"Node {record->NodeIndex}";
                        ImGui.TextUnformatted(nodeName);

                        ImGui.TableSetColumnIndex(3);
                        ImGui.TextUnformatted(record->Status.ToString());
                    }
                }
                ImGui.EndTable();
            }

            return true;
        }
    }
}
```

2. FastHSM Trace Renderer

The HSM renderer follows the identical memory-iteration paradigm but unpacks the `TraceRecord` union. It uses a `MachineMetadata` dictionary to translate the structural transition markers and domain event IDs into readable text.

```
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fhsm.Kernel.Data;
using ImGuiNET;

namespace Hrot.Presentation.Renderers
{
    [ImGuiRenderer(typeof(HsmTraceWorkingMemory1024))]
    public sealed class HsmTraceWorkingMemoryRenderer : IEntityAwareImGuiRenderer
    {
        public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }
        
        // Metadata injected at composition root mapping StructureHashes to MachineMetadata
        public static Dictionary<uint, MachineMetadata>? HsmMetadataAccessor { get; set; }

        public string? GetSummary(object value) => "HSM Execution Trace";
        public bool RenderValue(object value) => false; 

        public string? GetSummary(IInspectableSession session, Entity entity, object value) 
            => GetSummary(value);

        public unsafe bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
        {
            doubleClickedPath = null;
            if (value is not HsmTraceWorkingMemory1024 traceData) return false;

            ImGui.TextDisabled($"Records: {traceData.RecordCount} | Cursor: {traceData.WritePos}");
            ImGui.Separator();

            if (traceData.RecordCount == 0) return true;

            MachineMetadata? meta = null;
            if (BehaviorRegistryAccessor != null && session.HasComponent(entity, typeof(BehaviorState)))
            {
                var state = (BehaviorState)session.GetComponent(entity, typeof(BehaviorState))!;
                if (BehaviorRegistryAccessor.TryGetDefinition(state.ActiveBehaviorHash, out var def) && def.HsmDefinition != null)
                {
                    HsmMetadataAccessor?.TryGetValue(def.HsmDefinition.Header.StructureHash, out meta);
                }
            }

            if (ImGui.BeginTable("HsmTraceTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Tick", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableSetupColumn("OpCode", ImGuiTableColumnFlags.WidthFixed, 110f);
                ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 60f);
                ImGui.TableHeadersRow();

                int capacityBytes = HsmTraceWorkingMemory1024.PayloadBytes;
                int recordSize = sizeof(TraceRecord);
                
                fixed (byte* bufferPtr = traceData.Buffer)
                {
                    int startOffset = traceData.RecordCount == HsmTraceWorkingMemory1024.CapacityRecords
                        ? traceData.WritePos % capacityBytes 
                        : 0;

                    for (int i = 0; i < traceData.RecordCount; i++)
                    {
                        int offset = (startOffset + (i * recordSize)) % capacityBytes;
                        TraceRecord* record = (TraceRecord*)(bufferPtr + offset);

                        ImGui.TableNextRow();
                        
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted(record->Timestamp.ToString());

                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextUnformatted(record->OpCode.ToString());

                        ImGui.TableSetColumnIndex(2);
                        RenderHsmDetails(record, meta);
                    }
                }
                ImGui.EndTable();
            }

            return true;
        }

        private unsafe void RenderHsmDetails(TraceRecord* record, MachineMetadata? meta)
        {
            switch (record->OpCode)
            {
                case TraceOpCode.StateEnter:
                case TraceOpCode.StateExit:
                    string stateName = meta?.GetStateName(record->StateIndex) ?? $"State_{record->StateIndex}";
                    ImGui.TextUnformatted(stateName);
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted("-");
                    break;

                case TraceOpCode.Transition:
                    string fromState = meta?.GetStateName(record->StateIndex) ?? $"State_{record->StateIndex}";
                    string toState = meta?.GetStateName(record->TargetStateIndex) ?? $"State_{record->TargetStateIndex}";
                    string eventName = meta?.GetEventName(record->TriggerEventId) ?? $"Event_{record->TriggerEventId}";
                    ImGui.TextUnformatted($"{fromState} -> {toState} [{eventName}]");
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted("OK");
                    break;

                case TraceOpCode.ActionExecuted:
                    string actionName = meta?.GetActionName(record->ActionId) ?? $"Action_{record->ActionId}";
                    ImGui.TextUnformatted(actionName);
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted("-");
                    break;

                case TraceOpCode.GuardEvaluated:
                    string guardName = meta?.GetActionName(record->GuardId) ?? $"Guard_{record->GuardId}";
                    ImGui.TextUnformatted(guardName);
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(record->GuardResult != 0 ? "PASS" : "FAIL");
                    break;
                    
                default:
                    ImGui.TextUnformatted($"Raw ID: {record->StateIndex}");
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted("-");
                    break;
            }
        }
    }
}
```

Architectural Note

Both renderers bypass reflection entirely during the hot evaluation path. When a frame is rendered, the UI simply reads the unmanaged bytes and performs O(1) dictionary lookups against the pre-compiled static metadata. To register these, you ensure the `ImGuiRendererRegistry` discovers them during startup and the `BehaviorRegistryAccessor` properties are wired alongside the existing `Blackboard1024Renderer.BehaviorRegistryAccessor = behaviorRegistry;` injection inside your application root.





good now pls detail "6. **JSON Dump Serialization:** Implement `IEntityScenarioTranslator` for both trace components to support copying to the clipboard and diagnostic dumps. Implement `Extract` to project the binary rings into JSON arrays, and leave `Inject` strictly empty to prevent loading transient traces from disk. Register them in `HrotScenarioSerializerFactory`."

To expose the unmanaged ring buffers to the diagnostic clipboard and JSON dump pipelines, you must implement the `IEntityScenarioTranslator` contract. This encapsulates the N:M mapping from the raw memory layout into a structured DOM representation.

Because these trace buffers represent transient debugging state, the `Inject` method must remain strictly empty to prevent the engine from attempting to restore historical traces from a scenario file.

1. BTree Trace Translator

This translator unwinds the `BTreeTraceWorkingMemory1024` ring buffer in chronological order and projects the unmanaged 16-byte unions into a `JsonArray`.

```
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    public sealed class BTreeTraceWorkingMemoryTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(BTreeTraceWorkingMemory1024);

        // Safe to extract directly from the staging repository without GUID patching
        public bool IsExtractionSafe => true;

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(BTreeTraceWorkingMemory1024));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<BTreeTraceWorkingMemory1024>(entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var traceData = ref repo.GetComponentRO<BTreeTraceWorkingMemory1024>(entity);
            
            var recordsArray = new JsonArray();
            int capacityBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
            int recordSize = sizeof(BTreeTraceRecord);

            fixed (byte* bufferPtr = traceData.Buffer)
            {
                int startOffset = traceData.RecordCount == BTreeTraceWorkingMemory1024.CapacityRecords
                    ? traceData.WritePos % capacityBytes 
                    : 0;

                for (int i = 0; i < traceData.RecordCount; i++)
                {
                    int offset = (startOffset + (i * recordSize)) % capacityBytes;
                    BTreeTraceRecord* record = (BTreeTraceRecord*)(bufferPtr + offset);

                    var recordObj = new JsonObject
                    {
                        ["Timestamp"] = record->Timestamp,
                        ["OpCode"] = record->OpCode.ToString(),
                        ["InstanceId"] = record->InstanceId
                    };

                    // Decode union based on OpCode
                    switch (record->OpCode)
                    {
                        case BTreeTraceOpCode.NodeEvaluated:
                            recordObj["NodeIndex"] = record->NodeIndex;
                            recordObj["Status"] = record->Status.ToString();
                            break;
                        case BTreeTraceOpCode.WaitStarted:
                        case BTreeTraceOpCode.WaitCompleted:
                            recordObj["NodeIndex"] = record->NodeIndex;
                            recordObj["Duration"] = record->Duration;
                            break;
                        // Map additional union states here...
                    }

                    recordsArray.Add(recordObj);
                }
            }

            var root = new JsonObject
            {
                ["RecordCount"] = traceData.RecordCount,
                ["History"] = recordsArray
            };

            return new Dictionary<string, object> { [Key] = root };
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver resolver) 
        { 
            // Intentionally empty. Transient execution state is never loaded from scenario files.
        }

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return Key;
        }
    }
}
```

2. FastHSM Trace Translator

The HSM implementation follows the exact same ring buffer arithmetic but unpacks the `TraceRecord` struct layout.

```
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    public sealed class HsmTraceWorkingMemoryTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(HsmTraceWorkingMemory1024);

        public bool IsExtractionSafe => true;

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(HsmTraceWorkingMemory1024));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<HsmTraceWorkingMemory1024>(entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var traceData = ref repo.GetComponentRO<HsmTraceWorkingMemory1024>(entity);
            var recordsArray = new JsonArray();
            int capacityBytes = HsmTraceWorkingMemory1024.PayloadBytes;
            int recordSize = sizeof(TraceRecord);

            fixed (byte* bufferPtr = traceData.Buffer)
            {
                int startOffset = traceData.RecordCount == HsmTraceWorkingMemory1024.CapacityRecords
                    ? traceData.WritePos % capacityBytes 
                    : 0;

                for (int i = 0; i < traceData.RecordCount; i++)
                {
                    int offset = (startOffset + (i * recordSize)) % capacityBytes;
                    TraceRecord* record = (TraceRecord*)(bufferPtr + offset);

                    recordsArray.Add(new JsonObject
                    {
                        ["Timestamp"] = record->Timestamp,
                        ["OpCode"] = record->OpCode.ToString(),
                        ["StateIndex"] = record->StateIndex,
                        ["TargetStateIndex"] = record->TargetStateIndex,
                        ["TriggerEventId"] = record->TriggerEventId,
                        ["ActionId"] = record->ActionId,
                        ["GuardId"] = record->GuardId,
                        ["GuardResult"] = record->GuardResult
                    });
                }
            }

            var root = new JsonObject
            {
                ["RecordCount"] = traceData.RecordCount,
                ["History"] = recordsArray
            };

            return new Dictionary<string, object> { [Key] = root };
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver resolver) { }

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return Key;
        }
    }
}
```

3. Factory Registration

Once defined, you must explicitly register both translators within the core scenario pipeline. Locate `HrotScenarioSerializerFactory.cs` and append them to the fluent builder chain just after the existing cognitive translators.

```
// File: Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs

namespace Hrot.SimHost.Serializers
{
    public static class HrotScenarioSerializerFactory
    {
        public static ScenarioSerializer Build(BehaviorRegistry behaviorRegistry)
        {
            return new ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario)
                // ... existing registrations ...
                .RegisterTranslator(new EditablePolylineTranslator())
                .RegisterTranslator(new BrainBlackboardTranslator(behaviorRegistry))
                .RegisterTranslator(new Blackboard1024Translator(behaviorRegistry))
                
                // INJECT: Diagnostic Trace Translators
                .RegisterTranslator(new BTreeTraceWorkingMemoryTranslator())
                .RegisterTranslator(new HsmTraceWorkingMemoryTranslator())
                .Build();
        }
    }
}
```

By ensuring `GetConsumedComponentsMask` clears the bits for your 1KB components, the `FdpAutoSerializer` will inherently skip over them, guaranteeing no duplicate payload conflicts in the resulting JSON tree.





nodeIndex is fine, adding its string name would be even better - these trings are to be read by human so pls add strings where possible for both btree and hsm  (keep numeric, add stirngs)

This requirement perfectly aligns with the separation of concerns: the unmanaged 16-byte `TraceRecord` structs strictly retain only numeric indices to guarantee zero-allocation execution, while the presentation and serialization boundaries (ImGui renderers and JSON translators) perform the mapping to human-readable strings.

To support both the numeric indices and the translated strings, you must inject the metadata registries into the diagnostic translators and update the ImGui renderers to display composite values.

Here are the updated implementations.

1. Updated ImGui Renderers

The renderers now combine the raw integer and the string label into a single formatted output, e.g., `1 (Cruising)` or `5 (Sequence)`.

**BTree Renderer:**

```
using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using ImGuiNET;

namespace Hrot.Presentation.Renderers
{
    [ImGuiRenderer(typeof(BTreeTraceWorkingMemory1024))]
    public sealed class BTreeTraceWorkingMemoryRenderer : IEntityAwareImGuiRenderer
    {
        public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

        public string? GetSummary(object value) => "BTree Execution Trace";
        public bool RenderValue(object value) => false; 

        public string? GetSummary(IInspectableSession session, Entity entity, object value) 
            => GetSummary(value);

        public unsafe bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
        {
            doubleClickedPath = null;
            if (value is not BTreeTraceWorkingMemory1024 traceData) return false;

            ImGui.TextDisabled($"Records: {traceData.RecordCount} | Cursor: {traceData.WritePos}");
            ImGui.Separator();

            if (traceData.RecordCount == 0) return true;

            Fbt.BehaviorTreeBlob? blob = null;
            if (BehaviorRegistryAccessor != null && session.HasComponent(entity, typeof(BehaviorState)))
            {
                var state = (BehaviorState)session.GetComponent(entity, typeof(BehaviorState))!;
                if (BehaviorRegistryAccessor.TryGetDefinition(state.ActiveBehaviorHash, out var def))
                {
                    blob = def.BTreeInterpreter?.Blob;
                }
            }

            if (ImGui.BeginTable("BTreeTraceTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Tick", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableSetupColumn("OpCode", ImGuiTableColumnFlags.WidthFixed, 100f);
                ImGui.TableSetupColumn("Node", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 60f);
                ImGui.TableHeadersRow();

                int capacityBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
                int recordSize = sizeof(BTreeTraceRecord);
                
                fixed (byte* bufferPtr = traceData.Buffer)
                {
                    int startOffset = traceData.RecordCount == BTreeTraceWorkingMemory1024.CapacityRecords
                        ? traceData.WritePos % capacityBytes 
                        : 0;

                    for (int i = 0; i < traceData.RecordCount; i++)
                    {
                        int offset = (startOffset + (i * recordSize)) % capacityBytes;
                        BTreeTraceRecord* record = (BTreeTraceRecord*)(bufferPtr + offset);

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted(record->Timestamp.ToString());

                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextUnformatted(record->OpCode.ToString());

                        ImGui.TableSetColumnIndex(2);
                        
                        string nodeName = blob?.DebugMetadata != null && record->NodeIndex < blob.DebugMetadata.Length
                            ? blob.DebugMetadata[record->NodeIndex].Label
                            : "Unknown";
                        ImGui.TextUnformatted($"{record->NodeIndex} ({nodeName})");

                        ImGui.TableSetColumnIndex(3);
                        ImGui.TextUnformatted(record->Status.ToString());
                    }
                }
                ImGui.EndTable();
            }
            return true;
        }
    }
}
```

**FastHSM Renderer:**

```
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fhsm.Kernel.Data;
using ImGuiNET;

namespace Hrot.Presentation.Renderers
{
    [ImGuiRenderer(typeof(HsmTraceWorkingMemory1024))]
    public sealed class HsmTraceWorkingMemoryRenderer : IEntityAwareImGuiRenderer
    {
        public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }
        public static Dictionary<uint, MachineMetadata>? HsmMetadataAccessor { get; set; }

        public string? GetSummary(object value) => "HSM Execution Trace";
        public bool RenderValue(object value) => false; 

        public string? GetSummary(IInspectableSession session, Entity entity, object value) 
            => GetSummary(value);

        public unsafe bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
        {
            doubleClickedPath = null;
            if (value is not HsmTraceWorkingMemory1024 traceData) return false;

            ImGui.TextDisabled($"Records: {traceData.RecordCount} | Cursor: {traceData.WritePos}");
            ImGui.Separator();

            if (traceData.RecordCount == 0) return true;

            MachineMetadata? meta = null;
            if (BehaviorRegistryAccessor != null && session.HasComponent(entity, typeof(BehaviorState)))
            {
                var state = (BehaviorState)session.GetComponent(entity, typeof(BehaviorState))!;
                if (BehaviorRegistryAccessor.TryGetDefinition(state.ActiveBehaviorHash, out var def) && def.HsmDefinition != null)
                {
                    HsmMetadataAccessor?.TryGetValue(def.HsmDefinition.Header.StructureHash, out meta);
                }
            }

            if (ImGui.BeginTable("HsmTraceTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Tick", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableSetupColumn("OpCode", ImGuiTableColumnFlags.WidthFixed, 110f);
                ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 60f);
                ImGui.TableHeadersRow();

                int capacityBytes = HsmTraceWorkingMemory1024.PayloadBytes;
                int recordSize = sizeof(TraceRecord);
                
                fixed (byte* bufferPtr = traceData.Buffer)
                {
                    int startOffset = traceData.RecordCount == HsmTraceWorkingMemory1024.CapacityRecords
                        ? traceData.WritePos % capacityBytes 
                        : 0;

                    for (int i = 0; i < traceData.RecordCount; i++)
                    {
                        int offset = (startOffset + (i * recordSize)) % capacityBytes;
                        TraceRecord* record = (TraceRecord*)(bufferPtr + offset);

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted(record->Timestamp.ToString());

                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextUnformatted(record->OpCode.ToString());

                        ImGui.TableSetColumnIndex(2);
                        RenderHsmDetails(record, meta);
                    }
                }
                ImGui.EndTable();
            }
            return true;
        }

        private unsafe void RenderHsmDetails(TraceRecord* record, MachineMetadata? meta)
        {
            switch (record->OpCode)
            {
                case TraceOpCode.StateEnter:
                case TraceOpCode.StateExit:
                    string stateName = meta?.GetStateName(record->StateIndex) ?? "Unknown";
                    ImGui.TextUnformatted($"{record->StateIndex} ({stateName})");
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted("-");
                    break;

                case TraceOpCode.Transition:
                    string fromState = meta?.GetStateName(record->StateIndex) ?? "Unknown";
                    string toState = meta?.GetStateName(record->TargetStateIndex) ?? "Unknown";
                    string eventName = meta?.GetEventName(record->TriggerEventId) ?? "Unknown";
                    ImGui.TextUnformatted($"{record->StateIndex} ({fromState}) -> {record->TargetStateIndex} ({toState}) [Event: {record->TriggerEventId} ({eventName})]");
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted("OK");
                    break;

                case TraceOpCode.ActionExecuted:
                    string actionName = meta?.GetActionName(record->ActionId) ?? "Unknown";
                    ImGui.TextUnformatted($"{record->ActionId} ({actionName})");
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted("-");
                    break;

                case TraceOpCode.GuardEvaluated:
                    string guardName = meta?.GetActionName(record->GuardId) ?? "Unknown";
                    ImGui.TextUnformatted($"{record->GuardId} ({guardName})");
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted(record->GuardResult != 0 ? "PASS" : "FAIL");
                    break;
                    
                default:
                    ImGui.TextUnformatted($"Raw ID: {record->StateIndex}");
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextUnformatted("-");
                    break;
            }
        }
    }
}
```

2. Updated JSON Translators

To safely retrieve strings during extraction, you must inject the `BehaviorRegistry` and the `MachineMetadata` dictionary via the constructor. This is structurally identical to how `BrainBlackboardTranslator` receives its dependencies.

**BTree Translator:**

```
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    public sealed class BTreeTraceWorkingMemoryTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(BTreeTraceWorkingMemory1024);
        private readonly BehaviorRegistry _registry;

        public BTreeTraceWorkingMemoryTranslator(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public bool IsExtractionSafe => true;

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(BTreeTraceWorkingMemory1024));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<BTreeTraceWorkingMemory1024>(entity);

        public unsafe Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var traceData = ref repo.GetComponentRO<BTreeTraceWorkingMemory1024>(entity);
            
            Fbt.BehaviorTreeBlob? blob = null;
            if (repo.HasComponent<BehaviorState>(entity))
            {
                var state = repo.GetComponentRO<BehaviorState>(entity);
                if (_registry.TryGetDefinition(state.ActiveBehaviorHash, out var def))
                {
                    blob = def.BTreeInterpreter?.Blob;
                }
            }

            var recordsArray = new JsonArray();
            int capacityBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
            int recordSize = sizeof(BTreeTraceRecord);

            fixed (byte* bufferPtr = traceData.Buffer)
            {
                int startOffset = traceData.RecordCount == BTreeTraceWorkingMemory1024.CapacityRecords
                    ? traceData.WritePos % capacityBytes : 0;

                for (int i = 0; i < traceData.RecordCount; i++)
                {
                    int offset = (startOffset + (i * recordSize)) % capacityBytes;
                    BTreeTraceRecord* record = (BTreeTraceRecord*)(bufferPtr + offset);

                    var recordObj = new JsonObject
                    {
                        ["Timestamp"] = record->Timestamp,
                        ["OpCode"] = record->OpCode.ToString(),
                        ["InstanceId"] = record->InstanceId
                    };

                    string nodeName = blob?.DebugMetadata != null && record->NodeIndex < blob.DebugMetadata.Length
                        ? blob.DebugMetadata[record->NodeIndex].Label
                        : "Unknown";

                    switch (record->OpCode)
                    {
                        case BTreeTraceOpCode.NodeEvaluated:
                            recordObj["NodeIndex"] = record->NodeIndex;
                            recordObj["NodeName"] = nodeName;
                            recordObj["Status"] = record->Status.ToString();
                            break;
                        case BTreeTraceOpCode.WaitStarted:
                        case BTreeTraceOpCode.WaitCompleted:
                            recordObj["NodeIndex"] = record->NodeIndex;
                            recordObj["NodeName"] = nodeName;
                            recordObj["Duration"] = record->Duration;
                            break;
                    }
                    recordsArray.Add(recordObj);
                }
            }

            return new Dictionary<string, object> { [Key] = new JsonObject { ["RecordCount"] = traceData.RecordCount, ["History"] = recordsArray } };
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver resolver) { }
        public IEnumerable<string> GetOutputDomKeys() { yield return Key; }
    }
}
```

**FastHSM Translator:**

```
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    public sealed class HsmTraceWorkingMemoryTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(HsmTraceWorkingMemory1024);
        private readonly BehaviorRegistry _registry;
        private readonly Dictionary<uint, MachineMetadata> _hsmMetadata;

        public HsmTraceWorkingMemoryTranslator(BehaviorRegistry registry, Dictionary<uint, MachineMetadata> hsmMetadata)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _hsmMetadata = hsmMetadata ?? throw new ArgumentNullException(nameof(hsmMetadata));
        }

        public bool IsExtractionSafe => true;

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(HsmTraceWorkingMemory1024));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<HsmTraceWorkingMemory1024>(entity);

        public unsafe Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var traceData = ref repo.GetComponentRO<HsmTraceWorkingMemory1024>(entity);
            
            MachineMetadata? meta = null;
            if (repo.HasComponent<BehaviorState>(entity))
            {
                var state = repo.GetComponentRO<BehaviorState>(entity);
                if (_registry.TryGetDefinition(state.ActiveBehaviorHash, out var def) && def.HsmDefinition != null)
                {
                    _hsmMetadata.TryGetValue(def.HsmDefinition.Header.StructureHash, out meta);
                }
            }

            var recordsArray = new JsonArray();
            int capacityBytes = HsmTraceWorkingMemory1024.PayloadBytes;
            int recordSize = sizeof(TraceRecord);

            fixed (byte* bufferPtr = traceData.Buffer)
            {
                int startOffset = traceData.RecordCount == HsmTraceWorkingMemory1024.CapacityRecords
                    ? traceData.WritePos % capacityBytes : 0;

                for (int i = 0; i < traceData.RecordCount; i++)
                {
                    int offset = (startOffset + (i * recordSize)) % capacityBytes;
                    TraceRecord* record = (TraceRecord*)(bufferPtr + offset);

                    var recordObj = new JsonObject
                    {
                        ["Timestamp"] = record->Timestamp,
                        ["OpCode"] = record->OpCode.ToString()
                    };

                    switch (record->OpCode)
                    {
                        case TraceOpCode.StateEnter:
                        case TraceOpCode.StateExit:
                            recordObj["StateIndex"] = record->StateIndex;
                            recordObj["StateName"] = meta?.GetStateName(record->StateIndex) ?? "Unknown";
                            break;
                        case TraceOpCode.Transition:
                            recordObj["SourceStateIndex"] = record->StateIndex;
                            recordObj["SourceStateName"] = meta?.GetStateName(record->StateIndex) ?? "Unknown";
                            recordObj["TargetStateIndex"] = record->TargetStateIndex;
                            recordObj["TargetStateName"] = meta?.GetStateName(record->TargetStateIndex) ?? "Unknown";
                            recordObj["TriggerEventId"] = record->TriggerEventId;
                            recordObj["TriggerEventName"] = meta?.GetEventName(record->TriggerEventId) ?? "Unknown";
                            break;
                        case TraceOpCode.ActionExecuted:
                            recordObj["ActionId"] = record->ActionId;
                            recordObj["ActionName"] = meta?.GetActionName(record->ActionId) ?? "Unknown";
                            break;
                        case TraceOpCode.GuardEvaluated:
                            recordObj["GuardId"] = record->GuardId;
                            recordObj["GuardName"] = meta?.GetActionName(record->GuardId) ?? "Unknown";
                            recordObj["GuardResult"] = record->GuardResult != 0;
                            break;
                    }

                    recordsArray.Add(recordObj);
                }
            }

            return new Dictionary<string, object> { [Key] = new JsonObject { ["RecordCount"] = traceData.RecordCount, ["History"] = recordsArray } };
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver resolver) { }
        public IEnumerable<string> GetOutputDomKeys() { yield return Key; }
    }
}
```

To complete this, you must update `HrotScenarioSerializerFactory.Build(BehaviorRegistry registry)` to construct the translators using these new parameterized constructors.





good now please detail "7. **UI Control & Context Menu:** Define a new constant in `GlobalActionIds` and map it via `GlobalActionRegistry`. Inject a "Toggle AI Tracing" item using `LambdaEntityContextMenuHandler` that publishes the unmanaged `GlobalActionRequestedEvent` to the interaction bus."

1. Define the Action ID

To integrate seamlessly with the existing unified action architecture, define a new numeric constant inside the `GlobalActionIds` struct. This guarantees that the presentation layer and the simulation host share the exact same identifier without reflection or string parsing.

```
// File: Hrot/Engine/Hrot.Common/Constants/GlobalActionIds.cs

namespace Hrot.Common.Constants
{
    public static class GlobalActionIds
    {
        // ... existing IDs
        
        // --- Layer control ---
        public const int OpenLayerControl = 250;
        
        // --- Diagnostics ---
        public const int ToggleAiTrace = 251;
    }
}
```

2. Inject the Context Menu Item

Use the `LambdaEntityContextMenuHandler` to conditionally inject the UI action directly into the FDP Entity Inspector. This logic runs locally in the presentation shell (e.g., inside `IgApplication.DrawUI` or `EditorSubsystem.RegisterWindows`).

Because the interaction must not mutate the ECS directly from the ImGui render loop, the lambda strictly publishes the unmanaged `GlobalActionRequestedEvent` to the isolated `_interactionBus`.

```
// Inside your UI bootstrapper (e.g., IgApplication.cs or EditorSubsystem.cs):

_fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
{
    if (!_world.IsAlive(entity)) return;
    
    // Only show the menu item for entities that possess a cognitive brain
    if (!_world.HasComponent<BehaviorState>(entity)) return;

    builder.AddItem("Toggle AI Tracing", () =>
    {
        _interactionBus.Publish(new GlobalActionRequestedEvent
        {
            ActionId = GlobalActionIds.ToggleAiTrace,
            Target   = entity
        });
    });
}));
```

3. Map the Action in `GlobalActionRegistry`

The `GlobalActionDispatchSystem` consumes `GlobalActionRequestedEvent`s from the interaction bus during the `Input` phase and routes them to delegates registered in the `GlobalActionRegistry`.

You must map the new action ID to a delegate that reads the current `DebugState`, negates the trace buffer flag, and dispatches the generic `PatchDebugStateCommand` to the world bus.

```
// Inside your composition root where actionRegistry is configured:

actionRegistry.Register(GlobalActionIds.ToggleAiTrace, (view, target) =>
{
    if (target == Entity.Null) return;
    var repo = (EntityRepository)view;

    // Safety guard: Ensure the entity still has a brain
    if (!repo.HasComponent<BehaviorState>(target)) return;

    // 1. Resolve current state to determine the toggle direction
    bool currentState = false;
    if (repo.HasComponent<DebugState>(target))
    {
        ref readonly var debugState = ref repo.GetComponentRO<DebugState>(target);
        currentState = (debugState.Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0;
    }

    bool newState = !currentState;

    // 2. Publish the managed patch command for the ingress patcher system
    repo.Bus.PublishManaged(new PatchDebugStateCommand
    {
        Target = target,
        PatchJson = $"{{\"Behavior\": {{\"EnableTraceBuffer\": {newState.ToString().ToLowerInvariant()} }}}}"
    });
});
```

**Architectural Flow:**

The operator right-clicks an AI entity in the ImGui inspector.`LambdaEntityContextMenuHandler` evaluates `HasComponent<BehaviorState>` and renders the item.Clicking the item publishes the blittable `GlobalActionRequestedEvent` to the isolated `_interactionBus`.`GlobalActionDispatchSystem` drains the interaction bus and invokes the registered `GlobalActionRegistry` delegate.The delegate computes the JSON toggle state and publishes the managed `PatchDebugStateCommand` to the world bus.The `DebugStatePatchSystem` (implemented previously) compiles the JSON via expression trees and mutates the ECS chunk memory, satisfying the zero-reflection hot-path requirements.





using magic strings is fragile. let's pls use compiler-provided type and field names instead hardcoded strings in the jsons "{{"Behavior": {{"EnableTraceBuffer"

Hardcoding schema keys in string literals is a well-known source of divergence and runtime failures. Relying on the compiler via `nameof()` ensures that any future refactoring of the `DebugState` struct or `BehaviorDebugFlags` enum will automatically propagate to the JSON payload generation, breaking the build instead of failing silently at runtime.

By utilizing C# 11 raw string literals (`$$"""`) alongside `nameof()`, you avoid string escaping noise while guaranteeing structural type safety.

Here is the updated `GlobalActionRegistry` handler implementation:

```
// Inside your composition root where actionRegistry is configured:

actionRegistry.Register(GlobalActionIds.ToggleAiTrace, (view, target) =>
{
    if (target == Entity.Null) return;
    var repo = (EntityRepository)view;

    // Safety guard: Ensure the entity still has a brain
    if (!repo.HasComponent<BehaviorState>(target)) return;

    // 1. Resolve current state to determine the toggle direction
    bool currentState = false;
    if (repo.HasComponent<DebugState>(target))
    {
        ref readonly var debugState = ref repo.GetComponentRO<DebugState>(target);
        currentState = (debugState.Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0;
    }

    bool newState = !currentState;
    string stateStr = newState.ToString().ToLowerInvariant();

    // 2. Construct the JSON payload using compiler-verified symbols
    string patchJson = $$"""
    {
        "{{nameof(DebugState.Behavior)}}": {
            "{{nameof(BehaviorDebugFlags.EnableTraceBuffer)}}": {{stateStr}}
        }
    }
    """;

    // 3. Publish the managed patch command for the ingress patcher system
    repo.Bus.PublishManaged(new PatchDebugStateCommand
    {
        Target = target,
        PatchJson = patchJson
    });
});
```

This enforces strict coupling between the serialization boundaries. If the field `Behavior` or the enum value `EnableTraceBuffer` is renamed, this code will fail to compile, perfectly adhering to the fail-fast principle.





good now please detail "8. **System-Wide Auto-Enable (TKB):** Add an `AutoEnableAiTracing` flag to the global debug settings. Implement an `ITkbEntityTranslator` that evaluates this setting during entity genesis and injects the 1KB trace components into matching AI blueprints before the first tick."

To implement system-wide auto-enabling via the Transient Knowledge Base (TKB) pipeline, we must introduce the global configuration flag and implement an observer translator.

Be aware of a critical architectural boundary here: `GlobalDebugSettings` currently resides in `Hrot.IG.Gizmos`. The TKB entity genesis pipeline (`GhostPromotionSystem`, `BlueprintApplicationSystem`) executes on the SimHost and Brain nodes, which must not depend on the Presentation/IG layer. You must move `GlobalDebugSettings` into `Hrot.Common` or define a dedicated `AiDiagnosticsConfig` unmanaged singleton in `Fdp.Toolkit.Behavior.Diagnostics`.

Here is the exact implementation for the TKB auto-enable pipeline.

1. Global Debug Settings Struct

Add the flag to your configuration singleton. You must explicitly decorate the boolean with `[MarshalAs(UnmanagedType.I1)]` to prevent the .NET interop marshaller from defaulting to a 4-byte Win32 `BOOL` layout, which corrupts the ECS chunk alignment.

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(HrotComponentIds.GlobalDebugSettings)] // Assuming moved to Hrot.Common
[DataPolicy(DataPolicy.Transient)]
public struct GlobalDebugSettings
{
    // ... existing fields ...

    [MarshalAs(UnmanagedType.I1)] 
    public bool AutoEnableAiTracing;
}
```

2. The TKB Observer Translator

Implement the `ITkbEntityTranslator`. This translator acts as a passive observer. It reads `BehaviorProfileDto` to determine the brain tier but returns an empty array from `GetConsumedDescriptors()` so it does not steal the descriptor from the primary `BehaviorTkbTranslator`.

It stamps both the unmanaged 1KB trace buffer and the `DebugState` managed/unmanaged control flag, guaranteeing the UI inspector accurately reflects the auto-enabled state.

```
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Common; // Location of GlobalDebugSettings

namespace Fdp.Toolkit.Behavior.Translators
{
    public sealed class AiDiagnosticsTkbTranslator : ITkbEntityTranslator
    {
        // Observer pattern: we inspect BehaviorProfileDto but do not consume it.
        public IEnumerable<Type> GetConsumedDescriptors() => Array.Empty<Type>();

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            // 1. Evaluate global configuration
            if (!repo.HasSingletonUnmanaged<GlobalDebugSettings>()) return;
            if (!repo.GetSingletonUnmanaged<GlobalDebugSettings>().AutoEnableAiTracing) return;

            // 2. Read the behavior blueprint
            var profile = template.GetDescriptor<BehaviorProfileDto>();
            if (profile == null) return;

            // 3. Inject appropriate trace memory and UI control state
            if (profile.BrainTier == BehaviorConstants.BrainTierBTree)
            {
                if (repo.IsComponentTypeRegistered<BTreeTraceWorkingMemory1024>() && 
                    !repo.HasComponent<BTreeTraceWorkingMemory1024>(entity))
                {
                    repo.AddComponent(entity, new BTreeTraceWorkingMemory1024());
                    ApplyDebugState(repo, entity);
                }
            }
            else if (profile.BrainTier == BehaviorConstants.BrainTierHsm)
            {
                if (repo.IsComponentTypeRegistered<HsmTraceWorkingMemory1024>() && 
                    !repo.HasComponent<HsmTraceWorkingMemory1024>(entity))
                {
                    repo.AddComponent(entity, new HsmTraceWorkingMemory1024());
                    ApplyDebugState(repo, entity);
                }
            }
        }

        private static void ApplyDebugState(EntityRepository repo, Entity entity)
        {
            if (!repo.IsComponentTypeRegistered<DebugState>()) return;

            if (!repo.HasComponent<DebugState>(entity))
            {
                repo.AddComponent(entity, new DebugState { Behavior = BehaviorDebugFlags.EnableTraceBuffer });
            }
            else
            {
                ref var state = ref repo.GetComponentRW<DebugState>(entity);
                state.Behavior |= BehaviorDebugFlags.EnableTraceBuffer;
            }
        }
    }
}
```

3. Pipeline Registration

Register `AiDiagnosticsTkbTranslator` in the composition root alongside the existing translators. It must be provided to both the `EntityLifecycleModule` and `NetworkSpawningSystem` during the genesis pipeline configuration.

```
// Inside your node bootstrapper (e.g., NodeBootstrapper.cs or SimHostSubsystem):
var translators = new List<ITkbEntityTranslator>
{
    new SpatialCoreTkbTranslator(),
    new VehicleKinematicsTkbTranslator(),
    new BehaviorTkbTranslator(),
    new CombatTkbTranslator(),
    new PerceptionTkbTranslator(),
    
    // INJECT: Diagnostic observer
    new AiDiagnosticsTkbTranslator()
}.AsReadOnly();

// ... passed to EntityLifecycleModule and NetworkSpawningSystem
```

This guarantees the unmanaged ring buffers are physically present in the ECS chunk before the `BTreeTickSystem` evaluates frame 1, completely preventing `AddComponent` structural mutations during active simulation.





oh sure, we need to move GlobalDebugSettings into Hrot.Common.

did we detail everything needed?

Moving `GlobalDebugSettings` into `Hrot.Common` is an architectural necessity. The Transient Knowledge Base (TKB) genesis pipeline executes on the Brain and SimHost nodes. Allowing those foundational simulation nodes to take a dependency on `Hrot.IG.Gizmos` violates the established layer boundaries and would introduce a circular dependency.

We have detailed the memory layout, kernel instrumentation, execution pipeline, UI rendering, TKB injection, and UI control commands. However, we have not yet detailed the final requirement: **Point 9. BehaviorLog Integration**.

Routing trace records to the standard `MessageLogWindow` via NLog requires extracting only the *newly written* opcodes during the current tick and formatting them into strings, without polluting the hot path when logging is disabled.

Here is the exact implementation for integrating the unmanaged trace buffers with `BehaviorLog`.

9. BehaviorLog Integration

To emit logs strictly for the current frame, the tick system must capture the state of the ring buffer's cursor before execution and process the delta after execution.

Capturing the Delta in the Tick System

Inside `BTreeTickSystem.Execute` (and similarly in `HsmTickSystem`), you read the `WritePos` before stepping the interpreter. After the tick, if the `EmitToLog` flag is active and NLog is configured to consume trace logs, you decode the delta.

```
foreach (var entity in q)
{
    // ... existing setup and pointer resolution ...

    ushort startWritePos = 0;
    bool emitToLog = false;

    if (repo.HasComponent<DebugState>(entity))
    {
        ref readonly var debugState = ref repo.GetComponentRO<DebugState>(entity);
        emitToLog = (debugState.Behavior & BehaviorDebugFlags.EmitToLog) != 0;
    }

    if (tracePtr != null)
    {
        startWritePos = tracePtr->WritePos;
    }

    // --- STEP KERNEL ---
    var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);

    // --- PROCESS LOG EMISSION ---
    // Zero-allocation gate: bypass entirely if NLog trace target is disabled or entity flag is off
    if (emitToLog && tracePtr != null && BehaviorLog.IsTraceEnabled)
    {
        ushort endWritePos = tracePtr->WritePos;
        
        // Unsigned subtraction automatically handles ushort overflow wrap-around
        int bytesWritten = (ushort)(endWritePos - startWritePos);
        int recordsWritten = bytesWritten / sizeof(BTreeTraceRecord);

        if (recordsWritten > 0)
        {
            EmitToBehaviorLog(entity, repo, tracePtr, startWritePos, recordsWritten, def.BTreeInterpreter.Blob);
        }
    }
}
```

The Extraction and Formatting Method

Extracting the records requires computing the actual memory offset for each newly written record, mapping the integer opcodes to the string metadata, and dispatching to `BehaviorLog`.

```
private unsafe void EmitToBehaviorLog(
    Entity entity, 
    EntityRepository repo, 
    BTreeTraceWorkingMemory1024* traceData, 
    ushort startWritePos, 
    int count, 
    Fbt.BehaviorTreeBlob blob)
{
    int capacityBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
    int recordSize = sizeof(BTreeTraceRecord);

    fixed (byte* bufferPtr = traceData->Buffer)
    {
        for (int i = 0; i < count; i++)
        {
            // Calculate absolute memory offset for the record
            int cursor = (startWritePos + (i * recordSize));
            int offset = cursor % capacityBytes;
            
            BTreeTraceRecord* record = (BTreeTraceRecord*)(bufferPtr + offset);

            string nodeName = record->NodeIndex < blob.DebugMetadata.Length
                ? blob.DebugMetadata[record->NodeIndex].Label
                : "UnknownNode";

            string message = record->OpCode switch
            {
                BTreeTraceOpCode.NodeEvaluated => $"Node [{record->NodeIndex}] {nodeName} evaluated: {record->Status}",
                BTreeTraceOpCode.WaitStarted   => $"Node [{record->NodeIndex}] {nodeName} wait started: {record->Duration}s",
                BTreeTraceOpCode.ScopePushed   => $"Scope pushed. Depth: {record->StackDepth}",
                BTreeTraceOpCode.Error         => $"Node [{record->NodeIndex}] {nodeName} ERROR: {record->ErrorCode}",
                _                              => $"OpCode {record->OpCode} at Node {record->NodeIndex}"
            };

            // Emits to the NLog target configured for "AI.Behavior*"
            BehaviorLog.Trace(entity, repo, message, "BTreeTrace");
        }
    }
}
```

Final Architectural Verification

With this addition, the design completely fulfills the requirements:

**Zero-Allocation Hot Path:** Tracing BTree/HSM events to the 1KB chunk memory requires zero heap allocations and leverages JIT devirtualization.**No** **FlightRecorder** **Bloat:** The unmanaged component is strictly opt-in via UI dispatch and transient command injection. Normal entities do not dirty the `_chunkVersions` array.**UI Isolation:** The presentation layer unpacks unmanaged pointers to readable strings statically using dictionary lookups against the `BehaviorRegistry`.**Robust Initialization:** The TKB observer prevents race conditions by instantiating the unmanaged chunk footprints before `GlobalVersion` tick 1.**Selective Log Routing:** Emitting domain-readable strings to NLog is protected by dual conditional flags, isolating string interpolation performance penalties to active debugging sessions.

The architectural blueprint for the diagnostic tracing subsystem is complete.





Perfect, pls define the success conditions for this whole refactor (so the developer know when the job is done)

The following success conditions define the exact completion criteria for this refactoring effort. These conditions guarantee that the new diagnostic capabilities respect the engine's strict data-oriented performance constraints and layer separation rules.

**1. Complete Architectural Decoupling (Kernel Purity)** The core `Fbt.Kernel` and `Fhsm.Kernel` assemblies must contain zero references to ECS component types or the `Fdp.Toolkits` domain. FastBTree must emit traces exclusively through the constrained generic `ITreeTracer` interface, and FastHSM must route all execution events through the unmanaged `HsmTraceContext` pointer.

**2. Absolute Zero-Allocation Hot Path** Tracing execution steps within `BTreeTickSystem` and `HsmTickSystem` must result in zero heap allocations. The 1024-byte ring buffer operations must execute via O(1) unmanaged pointer arithmetic. The managed `HsmTraceBuffer` class must be entirely eradicated to prove this constraint is met.

**3. Flight Recorder and Chunk Delta Integrity** The `BTreeTraceWorkingMemory1024` and `HsmTraceWorkingMemory1024` components must be decorated with `[DataPolicy(DataPolicy.NoSave)]` to prevent transient execution history from bloating persistent scenario files. Furthermore, when tracing is disabled via the `DebugState` flag, the tick systems must bypass `GetComponentRW<T>`, ensuring the `LastChangeTick` chunk version remains un-dirtied and the Flight Recorder delta-compression is undisturbed.

**4. Strict Component Memory Layout** The unmanaged diagnostic components must perfectly respect the `EntityCommandBuffer` size limits. They must successfully pass `ComponentTypeRegistry` registration without triggering memory alignment or maximum component size exceptions during layout validation.

**5. Concurrent FastHSM Execution** The removal of the static global buffer in `HsmKernelCore` must allow multiple HSM entities to process transitions, guards, and activities concurrently across parallel worker threads without synchronization locks or race conditions.

**6. String Interpolation Segregation** String allocations for node names, state names, events, and errors must never occur in the simulation phase. All translation of raw `ushort` indices must be confined strictly to the ImGui renderers and `IEntityScenarioTranslator` implementations by querying the `BehaviorRegistry`.

**7. Unmanaged UI Event Propagation** Toggling the trace state from the ImGui inspector context menu must safely traverse the architectural boundary. The UI must emit the purely blittable `GlobalActionRequestedEvent`, which the input phase consumes to dispatch the stringified JSON patch command, ensuring the ECS chunk is never mutated directly from the rendering loop.

**8. Safe Genesis Bootstrapping** When `GlobalDebugSettings.AutoEnableAiTracing` is flagged, the `AiDiagnosticsTkbTranslator` must guarantee the 1KB buffer components are injected into the entity via the `ITkbEntityTranslator.Inject` pipeline during ghost promotion. This ensures the memory is pre-allocated and safely packed into the ECS chunk before the first cognitive tick ever evaluates.









`BehaviorLog` is a structured-logging utility designed to capture the semantic domain logic—the "why" behind AI decisions—which complements the raw structural execution traces.

You use it directly inside your BTree and HSM action/condition delegates to record context-rich domain events without breaking layer boundaries.

### API Surface and Context Binding

The API exposes overloads specifically tailored to the execution context:

- **BTree Nodes:** Overloads accept `ref BTreeContext ctx`. They extract the entity index and behavior hash automatically from the unmanaged ECS context.
- **HSM / Shared Actions:** Overloads accept `Entity self` and `EntityRepository repo` for nodes that bypass the BTree context.
- **Cold-Path Parsers:** Overloads like `ParseWarn` or `ParseError` require no entity context, used during scenario deserialization.

All methods leverage the `[CallerMemberName]` compiler attribute to automatically inject the exact C# method name as the node name, eliminating hardcoded strings. The output is strictly formatted for log aggregation (e.g., Elastic/Kibana) as: `Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[{ActionName}] | {UserMessage}`.

### Performance and Hot-Path Constraints

Because `BehaviorLog` relies on string interpolation and managed NLog targets, it allocates memory on the heap. To protect the simulation's zero-allocation hot path, you must guard any expensive string construction behind the exposed boolean level probes (`IsTraceEnabled`, `IsDebugEnabled`, `IsWarnEnabled`, `IsErrorEnabled`).

For critical failures where the allocation penalty is acceptable (because the node is about to fail anyway), you invoke it directly:

```
if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
{
    BehaviorLog.Error(ref ctx, "Entity is missing LocomotionChannel; blueprint may be misconfigured.");
    return NodeStatus.Failure;
}
```

For high-frequency debugging, you must strictly wrap the call to prevent string allocation when the log level is disabled:

```
if (BehaviorLog.IsDebugEnabled)
    BehaviorLog.Debug(ref ctx, "Calculated slots=" + totalSlots + " spacing=" + spacing + "m.");
```







OK so this is all already implemented. And to use it, it is needed just to put the BehaviorLog.Error and similar calls to the behavior code, right?





Yes, the infrastructure is fully implemented, but you cannot simply drop `BehaviorLog` calls blindly into the execution path. To maintain the engine's zero-allocation hot path and respect the domain boundaries, you must adhere to two strict architectural rules when instrumenting your behavior code.

First, you must explicitly pass the execution context. `BehaviorLog` does not rely on ambient or thread-local state. For FastBTree nodes, you must pass `ref BTreeContext ctx`. For FastHSM actions or shared AI delegates, you must pass the `Entity` and `EntityRepository`. This allows the logger to automatically extract the entity ID and active behavior hash for structured aggregation.

Second, you must rigorously guard any heap allocations caused by string interpolation. While the internal `BehaviorLog` methods check the logging level before emitting, any string interpolation (e.g., `$"Calculated slots={totalSlots}"`) allocates memory on the managed heap *before* the method is even invoked.

You must apply the following patterns:

- **Constant Strings (Fatal/Cold Paths):** For critical failures using static strings, you can call the method directly: `BehaviorLog.Error(ref ctx, "Entity is missing LocomotionChannel; blueprint may be misconfigured.");`.
- **Interpolated Strings (Hot Paths):** For high-frequency diagnostics requiring string concatenation or interpolation, you must wrap the call using the exposed boolean probes to bypass the allocation entirely when the log level is disabled: `if (BehaviorLog.IsDebugEnabled) BehaviorLog.Debug(ref ctx, "Calculated slots=" + totalSlots + " spacing=" + spacing + "m.");`.

Failing to wrap interpolated logs will continuously generate garbage during the simulation phase, destroying the cache locality and violating the performance budget.







