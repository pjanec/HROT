can you see the fastbtree sources?

Yes, I can see the FastBTree sources in the provided text. They are located within the `FDP/ExtDeps/FastBTree/` directory.

Here is a summary of the FastBTree components and files visible in the sources:

-   **Core Kernel Types:** Structures and classes that define the behavior tree data and state, such as `BehaviorTreeBlob`, `BehaviorTreeState`, `NodeDefinition`, `NodeStatus`, and `NodeType`.-   **Runtime Execution:** Files handling the execution of the trees, including `ActionRegistry`, the core `Interpreter`, `ITreeRunner`, and context interfaces like `IAIContext` which support pathfinding and raycast queries.-   **Serialization and Compilation:** Tools for building and saving the trees, such as `TreeCompiler` (which compiles JSON into optimized runtime blobs), `JsonTreeData`, `BinaryTreeSerializer`, `BuilderNode`, and `TreeValidator`.-   **Utilities:** A `TreeVisualizer` for generating text-based representations of the behavior tree structure.

Additionally, the solution files show that the FastBTree dependency includes several related projects such as `Fbt.Kernel`, `Fbt.Benchmarks`, `Fbt.Demo.Visual`, `Fbt.Examples.Console`, and `Fbt.Tests`.

Let me know if you would like to explore a specific part of the FastBTree implementation!

how the fastbtree tree nodes & conditions are now defined?

Systém nedokázal odpovědět.

how the fastbtree definition looks like (what is processed by the compiler)

The FastBTree compiler processes behavior tree definitions in a **JSON format**.

When you pass a JSON string into the compiler (via `TreeCompiler.CompileFromJson`), it parses the string into raw `JsonTreeData` and transforms it into an optimized, memory-efficient bytecode format called a `BehaviorTreeBlob`.

Here is a breakdown of how the JSON definition is structured:

1\. Root Level (`JsonTreeData`)

At the top level of the JSON document, the definition requires:

-   **TreeName**: A string representing the name of the behavior tree.-   **Version**: An integer (defaults to 1) for compatibility tracking.-   **Root**: A nested JSON object defining the entry point of the behavior tree logic.

2\. Node Level (`JsonNode`)

Each node in the tree (including the root) can specify several properties, depending on its role:

-   **Type**: A string indicating the behavior of the node (e.g., `"Selector"`, `"Sequence"`, `"Parallel"`, `"Action"`, `"Condition"`, `"Wait"`, `"Repeater"`, `"Cooldown"`, etc.).-   **Children**: An array of nested child nodes, typically used by composite nodes like Selectors and Sequences.-   **Action** (or **Method**): The name of the registered C# action/condition delegate to execute. The compiler binds this string to a pre-registered method at runtime.-   **Node-Specific Parameters**:
    -   **WaitTime**: Used by `"Wait"` nodes (can also be specified as `"duration"` in a `Params` object).-   **RepeatCount**: Used by `"Repeater"` nodes (can also be specified as `"count"`).-   **CooldownTime**: Used by `"Cooldown"` nodes.-   **Policy**: Used by `"Parallel"` nodes to define how it handles child completion (e.g., RequireAll vs RequireOne).

Example JSON Definition

Here is a complete example of a FastBTree definition from the sources (an "Ambush" behavior tree):

```
{
  "TreeName": "Ambush_BT",
  "Version": 1,
  "Root": {
    "Type": "Selector",
    "Children": [
      {
        "Type": "Sequence",
        "Children": [
          {
            "Type": "Condition",
            "Action": "Condition_HasTarget"
          },
          {
            "Type": "Action",
            "Action": "Action_AimAndFire"
          }
        ]
      },
      {
        "Type": "Action",
        "Action": "Action_HoldPosition"
      }
    ]
  }
}
```

_Source:_

how can the conditions and actions use the parameters?

In FastBTree, conditions and actions access parameters through the arguments provided to their execution delegate, **NodeLogicDelegate**.

Every custom action or condition you write must match this signature:

```
public delegate NodeStatus NodeLogicDelegate<TBlackboard, TContext>(
    ref TBlackboard blackboard,
    ref BehaviorTreeState state,
    ref TContext context,
    int paramIndex)
```

Through these parameters, actions and conditions can utilize data in three main ways:

**1\. The Blackboard (For Dynamic/Parsed Parameters)** The `ref TBlackboard blackboard` argument provides access to entity-specific memory. In the provided FDP implementation, this uses a `BrainBlackboard` struct containing a fixed byte array (`Memory`). When a behavior tree is assigned, a parser (like `ParseFireAtTargetParams`) deserializes JSON parameters and writes them into this blackboard memory. The action or condition then uses `unsafe` C# code to read those parameters back out. For example, `Action_FireAtTarget` retrieves its parameters like this:

```
FireAtTargetParams p;
fixed (byte* src = blackboard.Memory)
    p = Unsafe.Read<FireAtTargetParams>(src);
```

**2\. The Context and Payload Index (For Static Tree Parameters)** The `ref TContext context` argument (which implements `IAIContext`) provides access to the static data compiled into the `BehaviorTreeBlob`. The `int paramIndex` argument corresponds to the `PayloadIndex` of the compiled node. While standard actions use this index to look up their method name, other nodes use it to access static parameters via the context:

-   `context.GetFloatParam(paramIndex)` retrieves pre-compiled float parameters (e.g., used by `Wait` or `Cooldown` nodes).-   `context.GetIntParam(paramIndex)` retrieves pre-compiled integer parameters (e.g., used by `Repeater` or `Parallel` nodes).

**3\. The Behavior Tree State (For Runtime/Local Node State)** The `ref BehaviorTreeState state` argument provides a 64-byte cache-line of execution state for the entity. Actions and conditions can use this to store and retrieve their own runtime parameters:

-   **LocalRegisters**: An array of 4 integers used for general-purpose local state, such as keeping track of iteration counts in a loop.-   **AsyncHandles** **/** **AsyncData**: Storage for packed 64-bit values, commonly used to store timestamps for time-based parameters like wait durations or cooldown timers.

how can we define static parameters in the btree json?

In the FastBTree JSON definition, static parameters can be defined directly on the node object using strongly-typed properties, or as fallback values inside a generic dictionary mapped by a `[JsonExtensionData]` attribute.

Here are the specific static parameters supported by the compiler and how they are defined for different node types:

-   **Wait Node**: Defines how long to wait. You can use the **"WaitTime"** property (float) directly, or supply a **"duration"** key inside a `"Params"` object.-   **Repeater Node**: Defines the number of loop iterations. You can use the **"RepeatCount"** property (integer), or supply a **"count"** key inside a `"Params"` object.-   **Cooldown Node**: Defines the timeout period. You can use the **"CooldownTime"** property (float), or supply a **"duration"** key inside a `"Params"` object.-   **Parallel Node**: Defines the execution policy (e.g., RequireAll vs RequireOne). You can use the **"Policy"** property (integer), or supply a **"policy"** key inside a `"Params"` object.

For example, a Wait node can be defined using the direct property:

```
{
  "Type": "Wait",
  "WaitTime": 5.0
}
```

Or it can be defined using the flexible `Params` fallback:

```
{
  "Type": "Wait",
  "Params": {
    "duration": 5.0
  }
}
```

**How the compiler processes them:** When `TreeCompiler.CompileFromJson` parses your JSON, it evaluates these fields and deduplicates the static values into flat, memory-efficient arrays called **FloatParams** and **IntParams** inside the generated `BehaviorTreeBlob`.

The compiler then assigns a **PayloadIndex** to the compiled `NodeDefinition`. At runtime, the behavior tree interpreter uses this index to look up the static parameter from the context, such as calling `context.GetFloatParam(paramIndex)` to retrieve a wait duration.

Let's say i want to define a highly reusable btree node with conditions and actions which are supposed to take parameters from the blackboard. How can we specify where in the blackboard the condition and/or action the parameters are located? I do not want to hardcode the blackboard memory offsets inside the actions or conditions. I would like to specify the offset from outside (maybe by specifying the offset value as static parameter?) Even better i would like for that node to specify the name of the field in the btree parameter dto structure (which is used to interpret the byte array of the blackboard), expecting some runtime compiler to find the real offset. The btree parameter dto structure could be registered with the doctrine id when a doctrine gets registered. This registration could be used by the custom renderer for the brain blackboard component to render the dto fields instead of plain bytes

To achieve highly reusable Behavior Tree nodes that dynamically read parameters from specific blackboard offsets, you can leverage the existing FastBTree architecture while extending the Doctrine registration and UI rendering systems to support your ideal workflow.

Here is how you can implement both of your proposed solutions based on the FDP architecture:

1\. Specifying Offsets via Static Parameters (Fastest to Implement)

Currently, FastBTree's compiler supports defining static parameters on a node through a flexible `"Params"` dictionary in the JSON definition. These parameters are compiled into memory-efficient flat arrays like `IntParams`.

You can pass the blackboard byte offset as an integer parameter in your JSON:

```
{
  "Type": "Condition",
  "Action": "Condition_CheckDistance",
  "Params": {
    "offset": 16
  }
}
```

When `TreeCompiler.CompileFromJson` processes this, it saves the value `16` into the compiled `BehaviorTreeBlob`'s `IntParams` array and assigns a `PayloadIndex` to the node.

Inside your reusable C# condition delegate, you retrieve this offset from the context and read the blackboard memory using `unsafe` code:

```
public static unsafe NodeStatus Condition_CheckDistance(
    ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int paramIndex)
{
    // Retrieve the static offset defined in the JSON
    int offset = ctx.GetIntParam(paramIndex);

    // Read the value dynamically from the blackboard memory
    fixed (byte* src = bb.Memory)
    {
        float distanceThreshold = *(float*)(src + offset);
        // ... perform logic ...
    }
}
```

2\. Specifying Field Names (Dynamic Offset Resolution)

To use field names directly in the JSON and have the compiler calculate the real offset, you can introduce a pre-processing step before handing the JSON to the standard `TreeCompiler`.

Currently, FDP doctrines map a `ParseParamsDelegate` to handle how JSON is injected into the 128-byte `BrainBlackboard` memory. You can extend the `DoctrineDefinition` class to explicitly hold the DTO Type:

```
public sealed class DoctrineDefinition
{
    public required string Name { get; init; }
    public byte BrainTier { get; init; }
    public Type? ParamsDtoType { get; init; } // NEW: Explicitly link the DTO
    // ...
}
```

When building your behavior trees, you could write a JSON pre-processor that:

-   Reads `"FieldName": "AmmoCount"` from the node definition.-   Uses `System.Runtime.InteropServices.Marshal.OffsetOf(doctrineDef.ParamsDtoType, "AmmoCount")` to find the exact byte offset in the struct.-   Injects this calculated offset into the `"Params": { "offset": X }` dictionary.-   Passes the modified JSON to `TreeCompiler.CompileFromJson`.

This gives you the authoring convenience of string-based field names without paying any reflection or string-lookup costs during the 60Hz runtime tick.

3\. Custom Rendering for the Brain Blackboard

Your idea to use the registered DTO type to render the blackboard memory gracefully in the UI aligns perfectly with FDP's presentation layer.

The engine uses `IImGuiRenderer` plugins (registered via `[ImGuiRendererAttribute]`) to override how specific types are drawn in the Entity Inspector. You can create a custom renderer for `BrainBlackboard`:

```
[ImGuiRenderer]
public class BrainBlackboardRenderer : IImGuiRenderer
{
    public string? GetSummary(object value) => "Blackboard Memory";

    public unsafe bool RenderValue(object value)
    {
        if (value is not BrainBlackboard bb) return false;

        // 1. You would need access to the entity's DoctrineState to get the ActiveDoctrineHash. 
        // 2. Query the DoctrineRegistry for the DoctrineDefinition to get ParamsDtoType.
        // 3. If a valid DTO type is found, marshal the raw bytes into a boxed object:
        
        object dtoBoxed;
        fixed (byte* ptr = bb.Memory)
        {
            dtoBoxed = System.Runtime.InteropServices.Marshal.PtrToStructure(
                (IntPtr)ptr, ParamsDtoType);
        }

        // 4. Pass the typed DTO to the standard hierarchical ImGui tree renderer
        ImGuiPropertyTree.Render(dtoBoxed); [10, 11]

        return true; // Tells the UI we handled the rendering
    }
}
```

_Note: Because_ _IImGuiRenderer.RenderValue_ _only receives the value itself__, you may need to extend the signature to pass the current_ _Entity_ _or_ _IInspectableSession_ _context so you can look up the entity's_ _DoctrineState_ _to find the correct DTO._

By linking the DTO struct to the `DoctrineRegistry`, both your compiler and your ImGui debug tools share a single source of truth for memory layout, completely eliminating the need to read raw hex bytes while debugging.

how do the local refisters and async handles work?

In the FastBTree architecture, **Local Registers** and **Async Handles** are fundamental parts of the `BehaviorTreeState`, which is a highly optimized, 64-byte (single cache-line) unmanaged struct that tracks the runtime state of a behavior tree for a specific entity.

Because nodes in FastBTree are stateless and memory is completely separated from the logic, these fields provide the scratchpad memory necessary for nodes to execute multi-frame tasks (like looping, waiting, or parallel execution).

Here is exactly how they work:

1\. Local Registers (`LocalRegisters`)

The local registers consist of an array of four 32-bit integers (`public fixed int LocalRegisters`), providing general-purpose storage for nodes. Since they persist across frames, nodes use them to maintain counters or flags while they return a `Running` status.

-   **Repeater Node (Looping):** The `Repeater` node uses `LocalRegisters` to track the `currentIteration` count. It increments this integer every time its child succeeds, and resets it to `0` when the target loop count is reached or the child fails.-   **Parallel Node (Execution State):** The `Parallel` node uses `LocalRegisters` as a bitfield. Because it manages up to 16 children concurrently, it uses bits 0-15 to track which children succeeded, and bits 16-31 to track which children have finished evaluating. This prevents the node from re-executing children that have already completed.

**Important Limitation:** Because the registers are globally shared within the entity's tree state, nesting nodes that use the same register (e.g., placing a `Repeater` inside another `Repeater`, or a `Parallel` inside another `Parallel`) will cause them to overwrite each other's data. The compiler actively scans for this and will emit warnings if you attempt to nest these types of nodes.

2\. Async Handles (`AsyncHandles` / `AsyncData`)

Async handles consist of an array of three 64-bit unsigned integers (`public fixed ulong AsyncHandles`). They are designed to track time or external asynchronous operations (like pathfinding or raycasts).

The first slot (`AsyncHandles`) is exposed via a convenience property called **AsyncData**.

FastBTree uses a helper struct called `AsyncToken` to pack two 32-bit values into these 64-bit slots:

-   **For External Systems (Raycasts/Pathfinding):** It packs an external `RequestID` alongside the current `TreeVersion`. If the tree is aborted or reset, the `TreeVersion` increments. When the async result eventually arrives, the token checks if the versions match, effectively providing built-in "zombie-request" detection.-   **For Timers (Wait / Cooldown):** It can pack a 32-bit `float` representing the simulation time alongside a state flag.
    -   **Wait Node:** On its first tick, it packs the current `ctx.Time` into `state.AsyncData`. On subsequent frames, it unpacks this start time and compares it against the current time to see if the wait duration has elapsed.-   **Cooldown Node:** When its child succeeds, it packs the current `ctx.Time` into `state.AsyncData`. The next time the node is evaluated, it unpacks this timestamp and automatically fails execution if the elapsed time is less than the cooldown duration.

ad the "defining static parameters on a node through a flexible Params dictionary in the JSON definition", for example the {"offset": 16} like in

{ "Type": "Condition", "Action": "Condition\_CheckDistance", "Params": { "offset": 16 } }

can i use the name "offset" somewhere (for some debug visulatization perhaps? or for looking up the parameter index by name?) or is just a helper for the human definig the parameters and what matters is just the order of the key-value items in the Params dictionary?

To answer your question directly: **No, you cannot use the name "offset" at runtime.** The string keys in the `Params` dictionary are completely discarded by the compiler. They are not serialized into the compiled `BehaviorTreeBlob`, so they cannot be used for debug visualizations or name-based lookups at runtime.

Furthermore, I need to make an important correction to my previous explanation regarding how FastBTree handles parameters for custom actions.

Based on the actual compiler implementation in the sources, **the** **TreeCompiler** **completely ignores the** **Params** **dictionary for** **Action** **and** **Condition** **nodes.**

Here is how it actually works under the hood:

1\. `Params` is strictly a compile-time helper for built-in nodes

The `Params` dictionary is only evaluated at compile-time by `BuilderNode`. It is hardcoded to look for very specific fallback keys used by built-in flow control and decorator nodes:

-   `"duration"` for `Wait` and `Cooldown` nodes.-   `"count"` for `Repeater` nodes.-   `"policy"` for `Parallel` nodes.

Once it finds these specific keys, it extracts the raw value, adds it to the `FloatParams` or `IntParams` arrays, and discards the string key entirely.

2\. Actions and Conditions use `PayloadIndex` differently

For `Action` and `Condition` nodes, the compiler does not attempt to read the `Params` dictionary at all. Instead, it uses the node's `PayloadIndex` exclusively to map to the `MethodName` (e.g., `"Condition_CheckDistance"`) inside the `MethodNames` string array.

When the interpreter ticks your custom node, the `paramIndex` passed to your delegate is actually the index of the method name string, _not_ an index into the static `IntParams` array.

What this means for your Reusable Node idea

Because the native `TreeCompiler` will ignore `{"offset": 16}` on a `Condition` node, you cannot use the native static parameter arrays to pass your blackboard offsets.

To achieve your goal of defining the target field/offset in the JSON, you have two options:

-   **Use the Pre-processor / Blackboard Approach:** As discussed in the previous response, you write a pre-processor that reads your custom JSON properties (like `"FieldName": "AmmoCount"`), calculates the byte offset using reflection, and injects it into the JSON payload that gets sent to the Doctrine's `ParseParamsDelegate`. That delegate then writes the calculated offset directly into `BrainBlackboard.Memory` when the entity spawns. Your custom condition then reads the offset from the blackboard.-   **Extend the Compiler:** You would need to modify `BuilderNode.cs` and `TreeCompiler.cs` to explicitly support parsing custom `Params` for `Action`/`Condition` nodes, and extend `NodeDefinition` (which is currently tightly packed at 8 bytes) to hold a secondary payload index for static parameter lookups.

ad Use the Pre-processor / Blackboard Approach: ParseParamsDelegate is used to parse parameters provided from outside (from the scenario for example) to the doctrine. Parameters get parsed into a DTO which is then written to the blackoard byte array, is that correct? What i need is to tell the reusable node (used inside different Btrees using different parameter dtos) what exact field of the concrete doctrine's param Dto should carries the parameters for this reusable node. This is not what comes from the external parameter source. This is not a doctrine instance parameter. What i need is is a parameter of concrete doctrine type (concrete btree json) that gets saved with the doctrine json, referencing an existing field in that doctrine's parameter dto.

Yes, your understanding is exactly correct. The `ParseParamsDelegate` is specifically for parsing dynamic, per-instance parameters (like those assigned by a scenario script or `MissionPhase`) into a concrete DTO struct, which is then written into the 128-byte `BrainBlackboard.Memory` array.

Since you want the BTree definition to specify the target DTO field for a reusable node, this represents a **static parameter of the tree itself**. Because the `BehaviorTreeBlob` is shared across all entities using that doctrine, the byte offset for that field is constant for that specific tree.

Since we know the native `TreeCompiler` strips custom parameters from `Action` and `Condition` nodes, the most elegant way to build your pre-processor without modifying the FastBTree source code is to use **Delegate Currying via the Action Registry**.

Here is how you can implement this pre-processor approach:

1\. Authoring the Custom JSON

You author your BTree JSON using your custom property (e.g., `TargetField`) to reference the DTO field by name.

```
{
  "Type": "Condition",
  "Action": "Condition_CheckDistance",
  "TargetField": "EngagementRange"
}
```

2\. The Pre-processor & Dynamic Registration

When you register a new Doctrine, your pre-processor will intercept the JSON before passing it to `TreeCompiler.CompileFromJson`. It will reflect on the Doctrine's DTO, calculate the byte offset, and **dynamically generate a new method name** to register in the `ActionRegistry`.

Here is how the pre-processor logic would look:

```
public Interpreter<BrainBlackboard, BTreeContext> CompileWithDynamicOffsets(
    string jsonText, 
    Type dtoType, 
    ActionRegistry<BrainBlackboard, BTreeContext> registry)
{
    // 1. Parse JSON as a mutable DOM (e.g. using System.Text.Json.Nodes)
    var dom = JsonNode.Parse(jsonText);
    
    // 2. Scan for nodes with a "TargetField"
    foreach (var node in FindNodesWithTargetField(dom))
    {
        string baseAction = node["Action"].GetValue<string>();
        string fieldName = node["TargetField"].GetValue<string>();
        
        // 3. Calculate the exact byte offset using the provided DTO type
        int byteOffset = (int)Marshal.OffsetOf(dtoType, fieldName);
        
        // 4. Generate a unique action name for this specific offset
        string generatedActionName = $"{baseAction}_Offset{byteOffset}";
        
        // 5. Rewrite the BTree JSON to use the new generated action name
        node["Action"] = generatedActionName;
        
        // 6. Register a closure (curried delegate) in the registry
        // that passes the calculated static offset into your reusable method
        if (!registry.TryGetAction(generatedActionName, out _))
        {
            registry.Register(generatedActionName, 
                (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int paramIndex) => 
                {
                    // Call your highly reusable node, injecting the static offset
                    return Reusable_CheckDistance(ref bb, ref state, ref ctx, byteOffset);
                });
        }
    }

    // 7. Pass the modified JSON to the standard FastBTree compiler
    var blob = TreeCompiler.CompileFromJson(dom.ToJsonString());
    return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
}
```

3\. The Reusable Node Implementation

Now, your highly reusable condition node simply accepts the `byteOffset` as an argument from the closure, completely decoupling it from any specific DTO layout:

```
public static unsafe NodeStatus Reusable_CheckDistance(
    ref BrainBlackboard bb, 
    ref BehaviorTreeState state, 
    ref BTreeContext ctx, 
    int byteOffset)
{
    fixed (byte* memory = bb.Memory)
    {
        // Read the threshold parameter using the offset resolved at compile-time
        float threshold = *(float*)(memory + byteOffset);
        
        // ... perform distance check against threshold ...
    }
}
```

Why this architecture is ideal:

-   **Zero Runtime Reflection:** `Marshal.OffsetOf` is only called once when the doctrine is registered.-   **Zero Memory Waste:** Because the offset is baked directly into the delegate closure registered in the `ActionRegistry`, you don't waste any of your precious 128-byte `BrainBlackboard.Memory` or custom static array space storing the offset.-   **Clean Authoring:** Your AI designers can write JSON using human-readable DTO property names, and the system guarantees the exact memory offset is perfectly synced with the C# struct layout.

interception json is clumsy. Also defining the doctrines only in json is not practical. Much better would be if we define some kind of (fluent?) btree building api allowing to define the nodes using csharp object with constructors, building a DOM. This DOM tree would be equivalent to the json but would allow defining the btrees in csharp code in type safe way. The btree compiler might accept this DOM, or the DOM can be convertible to the JSON. Such DOM could carry the equivalent of the "TargetField", and maby more debug-friendly metadata information usable for diagnostics (maybe somehow usable by the entity inspector renderers?)

You are absolutely right, and your intuition aligns perfectly with how the engine already handles state machines!

While FastBTree relies on JSON, the **FastHSM** (Hierarchical State Machine) library in the engine already uses exactly the kind of fluent C# builder API you are describing. In `Fhsm.Compiler`, the `HsmBuilder` allows developers to define state nodes, transitions, and actions using C# object constructors, building an intermediate DOM (`StateMachineGraph`) which is then compiled into a `HsmDefinitionBlob`.

Implementing the exact same pattern for FastBTree is highly idiomatic to the FDP architecture. In fact, **FastBTree already has a DOM**: the `BuilderNode` class.

Currently, `TreeCompiler.CompileFromJson` parses a JSON string into `JsonTreeData`, immediately converts it into a tree of `BuilderNode` objects, and then flattens that DOM into the binary `BehaviorTreeBlob`. You can simply bypass the JSON step entirely by creating a `BTreeBuilder` that constructs the `BuilderNode` tree directly.

Here is how you can build this fluent API, solve the `TargetField` problem with strict type safety, and hook it into the UI:

1\. The Fluent C# Builder API

You can write a `BTreeBuilder` that exposes a fluent interface using C# lambdas. Because you are in C#, you can use **Expression Trees** (`Expression<Func<TDto, TValue>>`) to eliminate the "TargetField" magic strings completely.

```
public class BTreeBuilder
{
    private readonly BuilderNode _root = new BuilderNode { Type = NodeType.Root };
    private readonly string _treeName;
    private readonly ActionRegistry<BrainBlackboard, BTreeContext> _registry;

    public BTreeBuilder(string treeName, ActionRegistry<BrainBlackboard, BTreeContext> registry)
    {
        _treeName = treeName;
        _registry = registry;
    }

    // Example of a strongly-typed parameter node!
    public BTreeBuilder Condition<TDto>(Expression<Func<TDto, object>> fieldSelector, string baseActionName)
    {
        // 1. Calculate the exact byte offset using the Expression safely at compile-time
        var propertyInfo = GetPropertyInfoFromExpression(fieldSelector);
        int byteOffset = (int)Marshal.OffsetOf<TDto>(propertyInfo.Name);

        // 2. Dynamically register the curried delegate (like we discussed previously)
        string generatedActionName = $"{baseActionName}_Offset{byteOffset}";
        if (!_registry.TryGetAction(generatedActionName, out _))
        {
            _registry.Register(generatedActionName, (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int pIdx) => 
                Reusable_Condition(ref bb, ref state, ref ctx, byteOffset));
        }

        // 3. Add to our DOM
        _root.Children.Add(new BuilderNode { Type = NodeType.Condition, MethodName = generatedActionName });
        return this;
    }

    public BehaviorTreeBlob Compile()
    {
        // Pass the constructed BuilderNode directly to the compiler's flattening logic,
        // skipping JSON parsing entirely!
        return TreeCompiler.FlattenToBlob(_root, _treeName); 
    }
}
```

2\. Defining Trees in Type-Safe C#

Now, instead of writing fragile JSON files, your AI designers or programmers can define trees with compile-time guarantees:

```
var builder = new BTreeBuilder("MockCombat_BT", myActionRegistry)
    .Selector(s => s
        .Sequence(seq => seq
            // Type-safe! If ThreatVisible is renamed in MockBlackboardState, this fails to compile!
            .Condition<MockBlackboardState>(dto => dto.ThreatVisible, "Condition_CheckFlag")
            .Condition<MockBlackboardState>(dto => dto.AmmoCount, "Condition_HasAmmo")
            .Action("Action_AimAndFire")
        )
        .Action("Action_Flee")
    );

BehaviorTreeBlob blob = builder.Compile();
```

3\. Debug Visualization via ImGui

Because you are no longer limited by the JSON parser, your custom `BuilderNode` (or an extended `DebugBuilderNode`) can capture rich metadata—such as the original C# file path, line numbers, or human-readable comments.

To render this in the Entity Inspector, you can hook into FDP's existing **IImGuiRenderer** plugin system. The engine already provides a `TreeVisualizer` utility that generates text-based trees. By writing a custom ImGui renderer for `BrainBTreeState`, you can draw the tree visually:

```
[ImGuiRenderer(typeof(BrainBTreeState))]
public class BTreeStateRenderer : IImGuiRenderer
{
    public string? GetSummary(object value) => "Behavior Tree State";

    public bool RenderValue(object value)
    {
        var btState = (BrainBTreeState)value;
        
        // You can look up the ActiveDoctrineHash from the entity's DoctrineState,
        // grab the BehaviorTreeBlob from the DoctrineRegistry, and then use ImGui
        // to draw the nodes. 
        
        // Because btState.State.RunningNodeIndex tracks the current execution path,
        // you can highlight the currently executing node in green!
        ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Currently Running Node Index: {btState.State.RunningNodeIndex}");
        
        return true; 
    }
}
```

By leveraging `IImGuiRenderer`, the live behavior tree state will automatically integrate into the **Entity Watch Panel** and the **Entity Inspector**. Furthermore, your `BTreeBuilder` can output the pure JSON representation using the `TreeVisualizer` or standard serializers if you ever need to dump the tree for external tools.

ad Condition(dto => dto.ThreatVisible, "Condition\_CheckFlag")

the "Condition\_CheckFlag" is a string. But we are defining the tree in c#. Can't we simply pass the consition function there?

Yes, you are absolutely right. If we are defining the tree entirely via a fluent C# builder, we can hide the string linkage entirely from the user and just pass the function directly.

The only reason the string exists in the underlying engine is for **serialization**. The compiled `BehaviorTreeBlob` is designed to be a flat, immutable asset that can be serialized to disk (via `BinaryTreeSerializer`) or sent over the network. Because C# delegates cannot be serialized, the blob stores a `MethodNames` string array. At runtime, the `Interpreter` uses these strings to look up the actual function pointers inside the `ActionRegistry`.

However, your `BTreeBuilder` sits _above_ this layer. It can automatically generate the required linkage string behind the scenes, register the delegate, and hide the boilerplate completely.

Here is how you can implement that in your builder:

1\. Define a Reusable Delegate Signature

First, define a delegate signature for your reusable conditions/actions that explicitly expects the `byteOffset` instead of standard FastBTree parameters:

```
public delegate NodeStatus ReusableLogicDelegate(
    ref BrainBlackboard bb, 
    ref BehaviorTreeState state, 
    ref BTreeContext ctx, 
    int byteOffset);
```

2\. Update the Builder to accept the Delegate

Change your fluent builder method so that it accepts this delegate instead of a string:

```
public BTreeBuilder Condition<TDto>(
    Expression<Func<TDto, object>> fieldSelector, 
    ReusableLogicDelegate logicFunc)
{
    // 1. Calculate the exact byte offset at compile-time
    var propertyInfo = GetPropertyInfoFromExpression(fieldSelector);
    int byteOffset = (int)Marshal.OffsetOf<TDto>(propertyInfo.Name);

    // 2. Generate an automatic, hidden string key for the FastBTree registry linkage.
    // We use the method's name and offset to ensure it is unique but stable.
    string autoMethodName = $"Auto_{logicFunc.Method.Name}_Offset{byteOffset}";

    // 3. Automatically register the curried delegate in the ActionRegistry
    if (!_registry.TryGetAction(autoMethodName, out _))
    {
        _registry.Register(autoMethodName, (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int pIdx) => 
            logicFunc(ref bb, ref state, ref ctx, byteOffset));
    }

    // 4. Add the node to our DOM, using the auto-generated string
    _root.Children.Add(new BuilderNode { 
        Type = NodeType.Condition, 
        MethodName = autoMethodName 
    });
    
    return this;
}
```

3\. The Resulting Clean API

Now, your AI designers can build trees using pure, type-safe C# function references. The magical string registration and memory offset calculations are entirely invisible:

```
var builder = new BTreeBuilder("MockCombat_BT", myActionRegistry)
    .Selector(s => s
        .Sequence(seq => seq
            // Perfectly clean: just pass the DTO field and the function reference!
            .Condition<MockBlackboardState>(dto => dto.ThreatVisible, Reusable_CheckFlag)
            .Condition<MockBlackboardState>(dto => dto.AmmoCount, Reusable_HasAmmo)
            .Action(Action_AimAndFire)
        )
        .Action(Action_Flee)
    );
```

This gives you the absolute best of both worlds:

-   **Developer Experience:** Strict type safety, refactoring support (renaming fields in the DTO or renaming the action methods will automatically update the BTree), and zero magic strings.-   **Engine Performance:** When you call `.Compile()`, it outputs a standard, highly-optimized `BehaviorTreeBlob` that the FDP `Interpreter` ticks with zero allocations and zero reflection.

how the Reusable\_CheckFlag implementation might lok like, to give complete picture?

To complete the picture of the fluent C# builder API, here is exactly how the `Reusable_CheckFlag` (and its companion `Reusable_HasAmmo`) would be implemented.

These methods match the `ReusableLogicDelegate` signature we established. They use the `byteOffset` calculated by your expression tree to dive directly into the 128-byte unmanaged `BrainBlackboard` array, read the value using `unsafe` C# pointers, and return the standard FastBTree `NodeStatus`.

1\. The Boolean Flag Checker

This method handles the `dto.ThreatVisible` condition:

```
public static unsafe NodeStatus Reusable_CheckFlag(
    ref BrainBlackboard bb, 
    ref BehaviorTreeState state, 
    ref BTreeContext ctx, 
    int byteOffset)
{
    // Pin the 128-byte unmanaged blackboard memory buffer
    fixed (byte* memory = bb.Memory)
    {
        // Advance the pointer by the offset calculated from our DTO expression
        // and safely read the 1-byte boolean value
        bool flagValue = *(bool*)(memory + byteOffset);
        
        // Return standard FastBTree execution results
        return flagValue ? NodeStatus.Success : NodeStatus.Failure;
    }
}
```

_Note on memory layout: FDP enforces a strict 1-byte layout for unmanaged boolean fields to prevent interop memory corruption__. This guarantees that reading_ _\*(bool\*)_ _perfectly aligns with the size of the original struct field._

2\. The Integer Checker

To show how this scales to other data types, here is the implementation for the `dto.AmmoCount` condition from the same example:

```
public static unsafe NodeStatus Reusable_HasAmmo(
    ref BrainBlackboard bb, 
    ref BehaviorTreeState state, 
    ref BTreeContext ctx, 
    int byteOffset)
{
    fixed (byte* memory = bb.Memory)
    {
        // Cast the offset memory address to an integer pointer 
        // to read the 4-byte AmmoCount value
        int ammoCount = *(int*)(memory + byteOffset);
        
        return ammoCount > 0 ? NodeStatus.Success : NodeStatus.Failure;
    }
}
```

How the Complete Loop Works

With this setup, the entire pipeline is seamless and zero-allocation:

-   **Authoring (Compile Time):** You write `.Condition<MockBlackboardState>(dto => dto.ThreatVisible, Reusable_CheckFlag)`.-   **Builder (Setup Time):** The builder uses `Marshal.OffsetOf` to find that `ThreatVisible` is exactly at offset `0` in your DTO. It registers a curried delegate mapping `"Auto_Reusable_CheckFlag_Offset0"` to `Reusable_CheckFlag(..., offset: 0)`.-   **Runtime (Tick Time):** FastBTree's `Interpreter.Tick` looks up the action delegate. It passes the `BTreeContext` and `BehaviorTreeState`. Your `Reusable_CheckFlag` then reads offset `0` from the `BrainBlackboard` in raw memory, requiring absolutely no boxing, string lookups, or reflection.

Let's say the action needs to read not just one byte, but whole sub-structure - that the action uses its own dto and the offset indicates where this tdo starts in the blackoard byte array. Can we somehow simplify the access to this blackoard-mapped dto inside the btree condition method? to hide the pointer arithmetic magics and offer some ref var style access to that dto? Best if the compiler deduces the dto automatically from the lambda in ".Condition(dto => dto.ThreatVisible"

Yes, you can absolutely achieve this. By combining C# Generic Type Inference with `System.Runtime.CompilerServices.Unsafe`, you can eliminate the `unsafe` keyword and pointer arithmetic entirely from your action delegates.

Instead of passing the raw blackboard and an integer offset to your reusable node, your builder can automatically resolve the sub-DTO type from the lambda and inject a `ref TValue` directly into the method signature.

Here is how you implement this highly advanced, zero-allocation pattern:

1\. Define the Generic Reusable Delegate

First, update your reusable delegate signature so that it completely hides the `BrainBlackboard` and instead requests the specific mapped `ref TValue` (which could be a boolean flag, an integer, or an entire DTO struct):

```
public delegate NodeStatus ReusableLogicDelegate<TValue>(
    ref TValue data, 
    ref BehaviorTreeState state, 
    ref BTreeContext ctx) where TValue : unmanaged;
```

2\. Upgrade the Fluent Builder using `Unsafe`

Next, update the `Condition` (or `Action`) method on your `BTreeBuilder`. By defining two generic parameters—`TBlackboardDto` and `TValue`—the C# compiler will automatically deduce the type of the DTO you are returning in the lambda.

Inside the auto-generated closure, we use the `Unsafe` class (the exact same high-performance pattern used throughout FDP's `UnsafeShim`) to shift the memory reference and cast it without ever fixing or pinning pointers:

```
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public BTreeBuilder Condition<TBlackboardDto, TValue>(
    Expression<Func<TBlackboardDto, TValue>> fieldSelector, 
    ReusableLogicDelegate<TValue> logicFunc) 
    where TBlackboardDto : unmanaged
    where TValue : unmanaged
{
    // 1. Resolve offset safely at compile-time
    var propertyInfo = GetPropertyInfoFromExpression(fieldSelector);
    int byteOffset = (int)Marshal.OffsetOf<TBlackboardDto>(propertyInfo.Name);

    // 2. Generate stable, unique name for the FastBTree registry
    string autoMethodName = $"Auto_{logicFunc.Method.Name}_Offset{byteOffset}";

    if (!_registry.TryGetAction(autoMethodName, out _))
    {
        _registry.Register(autoMethodName, (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int pIdx) => 
        {
            // ✨ ZERO-POINTER MAGIC: Shift the base reference and cast to our sub-DTO type.
            // This compiles down to a single JIT memory-offset instruction (zero cost).
            ref byte bbBase = ref Unsafe.As<BrainBlackboard, byte>(ref bb);
            ref byte targetRef = ref Unsafe.AddByteOffset(ref bbBase, (nint)byteOffset);
            ref TValue subDtoRef = ref Unsafe.As<byte, TValue>(ref targetRef);

            // 3. Call the user's logic function with pure 'ref var' style access!
            return logicFunc(ref subDtoRef, ref state, ref ctx);
        });
    }

    _root.Children.Add(new BuilderNode { 
        Type = NodeType.Condition, 
        MethodName = autoMethodName 
    });
    
    return this;
}
```

3\. The Resulting Action Implementation

Your reusable action methods now deal _exclusively_ with their own typed DTOs. There is no `unsafe` block, no `fixed` pinning, and no pointer arithmetic. It feels just like interacting with a standard C# `ref` variable:

```
// Example Sub-Structure
public struct CombatConfigDto 
{
    public bool ThreatVisible;
    public int AmmoCount;
    public float EngagementRange;
}

// The Reusable Logic Node
public static NodeStatus Reusable_ComplexCheck(
    ref CombatConfigDto config, 
    ref BehaviorTreeState state, 
    ref BTreeContext ctx)
{
    // Direct, ref-var style access to the mapped blackboard struct!
    if (config.ThreatVisible && config.AmmoCount > 0)
    {
        // We can also mutate it directly, and it modifies BrainBlackboard.Memory!
        config.AmmoCount--; 
        return NodeStatus.Success;
    }

    return NodeStatus.Failure;
}
```

4\. The Clean Usage

Because of C# type inference, if you declare the lambda parameter type explicitly, the compiler deduces both `TBlackboardDto` and `TValue` automatically.

```
var builder = new BTreeBuilder("MockCombat_BT", myActionRegistry)
    .Selector(s => s
        // The compiler sees that 'dto' is MockBlackboardState, 
        // and dto.CombatConfig is of type 'CombatConfigDto'.
        // It seamlessly injects everything into Reusable_ComplexCheck!
        .Condition((MockBlackboardState dto) => dto.CombatConfig, Reusable_ComplexCheck)
    );
```

**Why this is the ultimate solution:** By combining `System.Linq.Expressions` (for offset calculation) and `System.Runtime.CompilerServices.Unsafe` (for memory projection), you have completely bridged the gap between the flexible, type-safe C# domain and FastBTree's raw 128-byte unmanaged execution memory. The JIT compiler optimizes the `Unsafe` calls away entirely, granting you the exact same performance as raw C/C++ pointer manipulation with none of the safety risks.

you mention previously we could render the btree structure with color coded active node in the entity inspector using a custom renderer. What would it take? Like extending IImguiRenderer etc. what debug info we could show for the tree, for each of its node etc; as we utilize the c# fluid btree builder - we migh specify extra metadata usable for the improving debug view to give the best info possible - please suggest

To build a rich, color-coded visualizer for your behavior trees in the Entity Inspector, you need to connect the runtime execution state (`BrainBTreeState`) with the static compiled tree structure (`BehaviorTreeBlob`).

Because you are using a fluent C# builder, you have a massive advantage over JSON: you can automatically capture compile-time C# metadata and inject it into the compiled blob for the UI to consume.

Here is a complete blueprint for how to implement this.

1\. Augmenting the Blob & Fluent Builder for Metadata

Currently, the `BehaviorTreeBlob` is highly optimized for execution. You can add a parallel managed array to hold UI-only debug information without affecting the 8-byte `NodeDefinition` runtime performance.

First, define the metadata and add it to the blob:

```
public class NodeDebugMetadata
{
    public string Label;
    public string SourceFile;
    public int LineNumber;
    public string CustomComment;
}

// Inside BehaviorTreeBlob:
public NodeDebugMetadata[] DebugMetadata = Array.Empty<NodeDebugMetadata>();
```

Next, update your fluent `BTreeBuilder` to capture caller info automatically using native C# attributes:

```
using System.Runtime.CompilerServices;

public class BTreeBuilder
{
    public BTreeBuilder Condition<TDto>(
        Expression<Func<TDto, object>> fieldSelector, 
        ReusableLogicDelegate logicFunc,
        string customComment = "",
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        string fieldName = GetPropertyInfoFromExpression(fieldSelector).Name;
        string autoLabel = $"Check: {fieldName}";

        // Store this metadata in your intermediate BuilderNode
        var metadata = new NodeDebugMetadata {
            Label = autoLabel,
            SourceFile = Path.GetFileName(sourceFile),
            LineNumber = lineNumber,
            CustomComment = customComment
        };
        
        // ... build node ...
        return this;
    }
}
```

When your builder calls `.Compile()`, it populates the `DebugMetadata` array so that `DebugMetadata[i]` perfectly aligns with `blob.Nodes[i]`.

2\. Implementing the Custom ImGui Renderer

To render this in the Entity Inspector, you implement the `IImGuiRenderer` interface and register it for `BrainBTreeState`.

_Note: Because_ _IImGuiRenderer.RenderValue_ _only receives the value itself__, your UI framework will need a way to look up the entity's_ _DoctrineState_ _to find the correct_ _BehaviorTreeBlob_ _from the_ _DoctrineRegistry__._

```
[ImGuiRenderer(typeof(BrainBTreeState))]
public class BTreeVisualizerRenderer : IImGuiRenderer
{
    public string? GetSummary(object value) => "Live BTree State";

    public bool RenderValue(object value)
    {
        var btreeState = (BrainBTreeState)value;
        ref var state = ref btreeState.State;
        
        // 1. Resolve the static BehaviorTreeBlob for this entity (via DoctrineRegistry)
        BehaviorTreeBlob blob = ResolveBlobForCurrentEntity(); 
        if (blob == null) return false;

        // 2. Render general tree execution info
        ImGui.TextDisabled($"Tree Version: {state.TreeVersion}");
        
        // 3. Render the recursive tree view starting at the root (index 0)
        DrawNode(blob, ref state, nodeIndex: 0);

        return true;
    }
}
```

3\. Rendering Nodes with Color-Coding & Debug Info

FastBTree's `BehaviorTreeState` provides a 64-byte snapshot of execution. You can use `state.RunningNodeIndex` and `state.NodeIndexStack` to accurately highlight the active execution path.

Here is how you implement the `DrawNode` recursive function:

```
private void DrawNode(BehaviorTreeBlob blob, ref BehaviorTreeState state, int nodeIndex)
{
    if (nodeIndex >= blob.Nodes.Length) return;

    ref var node = ref blob.Nodes[nodeIndex];
    var meta = blob.DebugMetadata[nodeIndex];

    // --- 1. COLOR CODING THE ACTIVE PATH ---
    bool isCurrentlyRunning = (state.RunningNodeIndex == nodeIndex);
    bool isAncestralPath = IsNodeInExecutionStack(ref state, nodeIndex);

    int popColors = 0;
    if (isCurrentlyRunning)
    {
        // Highlight the currently executing leaf node in Green
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1.0f, 0.2f, 1.0f)); 
        popColors++;
    }
    else if (isAncestralPath)
    {
        // Highlight parent composite nodes (Selectors/Sequences) in Yellow
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.2f, 1.0f)); 
        popColors++;
    }

    // --- 2. NODE HEADER ---
    string nodeTitle = $"{node.Type} : {meta.Label}";
    var flags = node.ChildCount == 0 ? ImGuiTreeNodeFlags.Leaf : ImGuiTreeNodeFlags.DefaultOpen;
    
    bool isOpen = ImGui.TreeNodeEx($"{nodeTitle}###node_{nodeIndex}", flags);
    
    if (popColors > 0) ImGui.PopStyleColor(popColors);

    // --- 3. LIVE DEBUG DATA (Tooltips & Inline Info) ---
    if (ImGui.IsItemHovered())
    {
        ImGui.SetTooltip($"File: {meta.SourceFile} (Line {meta.LineNumber})\n{meta.CustomComment}");
    }

    if (isOpen)
    {
        // Draw specific runtime state based on Node Type
        DrawNodeRuntimeDetails(blob, ref state, nodeIndex, ref node);

        // Recursively draw children
        int childIndex = nodeIndex + 1;
        for (int i = 0; i < node.ChildCount; i++)
        {
            DrawNode(blob, ref state, childIndex);
            childIndex += blob.Nodes[childIndex].SubtreeOffset; // Jump to next sibling
        }
        ImGui.TreePop();
    }
}
```

4\. Exposing Internal Node State (`LocalRegisters` & `AsyncData`)

FastBTree uses `LocalRegisters` and `AsyncData` (packed 64-bit timestamps) to hold multi-frame state. Your renderer can decode these to provide incredible live visibility into _why_ a node is doing what it is doing:

```
private unsafe void DrawNodeRuntimeDetails(BehaviorTreeBlob blob, ref BehaviorTreeState state, int nodeIndex, ref NodeDefinition node)
{
    // Only show live details if this node is currently running
    if (state.RunningNodeIndex != nodeIndex) return;

    if (node.Type == NodeType.Wait)
    {
        // Decode the wait timer using AsyncToken
        var token = new AsyncToken(state.AsyncData);
        float startTime = token.FloatA;
        float duration = blob.FloatParams[node.PayloadIndex];
        
        // (Assuming you pass in the current Time via context)
        // ImGui.TextDisabled($"Waiting: {elapsed:F1}s / {duration:F1}s");
    }
    else if (node.Type == NodeType.Repeater)
    {
        // Repeater stores its current iteration count in LocalRegisters
        int currentLoop = state.LocalRegisters;
        int targetLoops = blob.IntParams[node.PayloadIndex];
        ImGui.TextDisabled($"Iteration: {currentLoop} / {targetLoops}");
    }
    else if (node.Type == NodeType.Parallel)
    {
        // Parallel node stores bitflags for success/completion in LocalRegisters[11]
        int flags = state.LocalRegisters[11];
        ImGui.TextDisabled($"Parallel Bitflags: 0x{flags:X8}");
    }
}

private bool IsNodeInExecutionStack(ref BehaviorTreeState state, int nodeIndex)
{
    // Traverse the current call stack to see if nodeIndex is an ancestor
    for (int i = 0; i <= state.StackPointer; i++)
    {
        if (state.NodeIndexStack[i] == nodeIndex) return true;
    }
    return false;
}
```

**Summary of what this gives you:** By combining the static DOM you built in C# with the 64-byte `BehaviorTreeState`, your ImGui renderer will instantly display a live, color-coded execution tree. Designers and engineers will be able to see the exact C# file and line number that generated a specific behavior, watch timers count down in real-time on `Wait` nodes, and see exactly which branch of a `Selector` or `Sequence` is actively evaluating.

ad "Because IImGuiRenderer.RenderValue only receives the value itself, your UI framework will need a way to look up the entity's DoctrineState to find the correct BehaviorTreeBlob from the DoctrineRegistry"

this is what i meant with extending the IImGuiRenderer interface - how to extend it to get access to all what we might need? just optionally, to still keeping the simplicity of the interfaces for cases where we do not need suc extended access

To extend the rendering framework while preserving the simplicity of the existing `IImGuiRenderer` for standard types (like `Vector3` or `Guid`), you can introduce a new interface that inherits from it.

Because the `ImGuiRendererRegistry` caches these renderer instances as application-wide singletons, you cannot safely inject the context into class properties. Instead, you must pass the context directly into an overloaded method signature.

Here is the cleanest way to implement this in the FDP architecture:

1\. Define the Extended Interface

Create a new interface that specifically passes down the `IInspectableSession` and the `Entity`. This gives the renderer read access to the entire entity and its sibling components.

```
using Fdp.Core;
using Fdp.Presentation.Abstractions;

namespace Fdp.Presentation.Renderers
{
    /// <summary>
    /// Extended ImGui renderer that requires entity context (e.g., to look up sibling components).
    /// </summary>
    public interface IEntityAwareImGuiRenderer : IImGuiRenderer
    {
        /// <summary>
        /// Renders a custom value cell using the full entity context.
        /// </summary>
        bool RenderValue(IInspectableSession session, Entity entity, object value);
    }
}
```

2\. Update the Component Reflector

Currently, the `ComponentReflector.DrawComponents` method iterates through the components and delegates rendering to the standard interface. You simply update this specific block to check for the extended interface first:

```
// Inside ComponentReflector.cs -> DrawComponents()
var renderer = ImGuiRendererRegistry.GetRenderer(type);
bool handled = false;

// ✨ NEW: Check if the renderer supports extended context
if (renderer is IEntityAwareImGuiRenderer entityRenderer)
{
    handled = entityRenderer.RenderValue(session, e, data);
}
else if (renderer != null)
{
    handled = renderer.RenderValue(data);
}

// Fallback to the standard hierarchical tree if neither handled it
if (!handled)
    ImGuiPropertyTree.Render(data, contextType: type, out doubleClickedPath);
```

3\. Implement Your BTree Visualizer

Now, your behavior tree visualizer can implement `IEntityAwareImGuiRenderer`. The standard `RenderValue` remains to satisfy the base interface contract, but the framework will automatically route to your contextual method when rendering the Entity Inspector.

```
[ImGuiRenderer(typeof(BrainBTreeState))]
public class BTreeVisualizerRenderer : IEntityAwareImGuiRenderer
{
    // Resolves from standard interface
    public string? GetSummary(object value) => "Live BTree State";

    // Standard signature fallback (ignored by ComponentReflector if context is available)
    public bool RenderValue(object value) => false;

    // ✨ NEW: Context-aware rendering
    public bool RenderValue(IInspectableSession session, Entity entity, object value)
    {
        var btreeState = (BrainBTreeState)value;
        ref var state = ref btreeState.State;
        
        // 1. Use the session to safely retrieve the sibling DoctrineState
        if (!session.HasComponent(entity, typeof(DoctrineState))) 
            return false;

        var doctrine = (DoctrineState)session.GetComponent(entity, typeof(DoctrineState));
        
        // 2. You now have the ActiveDoctrineHash to look up your BehaviorTreeBlob!
        BehaviorTreeBlob blob = ResolveBlobFromRegistry(doctrine.ActiveDoctrineHash);
        if (blob == null) return false;

        // 3. Draw the tree using ImGui
        DrawNode(blob, ref state, nodeIndex: 0);

        return true; // Tells the UI we handled the rendering
    }
}
```

**Why this approach is ideal:**

-   **Backward Compatibility:** All existing simple renderers (like `Vector3Renderer` or `EntityRenderer`) continue to implement just `IImGuiRenderer` and work exactly as they did before without needing empty context parameters.-   **Safe State:** Because the `IInspectableSession` interface abstracts over both the live `EntityRepository` and read-only snapshots (like `ISimulationView` adapters), your debugger will safely work whether the game is paused, running, or dry-running a scenario replay.

good! let's pls summarize all the cool stuff we designed so far!

Here is a summary of the elegant, high-performance architecture we designed to elevate the FastBTree system:

**1\. Type-Safe Fluent C# Builder (Replacing JSON)** We completely bypassed the fragile string-based JSON parsing (`TreeCompiler.CompileFromJson`) by creating a `BTreeBuilder` in C#. This allows AI designers to author behavior trees using fluent, strongly-typed lambdas (e.g., `.Condition((MockBlackboardState dto) => dto.CombatConfig, Reusable_ComplexCheck)`). If a DTO property is renamed or removed, the behavior tree throws a compile-time error, ensuring perfect synchronization between data and logic.

**2\. Zero-Reflection Memory Offsets** Instead of relying on magic strings or paying reflection costs at runtime, our builder uses C# Expression Trees (`Expression<Func<TDto, TValue>>`) to calculate the exact byte offsets of fields within your unmanaged DTOs at setup time.

**3\. Zero-Pointer** **ref var** **Projection** We eliminated the need for `unsafe` code and pointer arithmetic inside your reusable action delegates. By utilizing `System.Runtime.CompilerServices.Unsafe`, the builder automatically shifts the base reference of the 128-byte `BrainBlackboard` and casts it to your specific sub-DTO type. Your highly reusable condition and action nodes now receive a perfectly safe, statically-typed `ref TValue` parameter, providing zero-allocation, JIT-optimized memory access.

**4\. Invisible Delegate Currying** To bridge the gap between our dynamic offsets and the underlying engine, we designed a pre-processor step that automatically generates unique method names (like `"Auto_ReusableCheck_Offset16"`) and registers curried closures into the FastBTree `ActionRegistry`. This hides all the linkage boilerplate from the user while emitting a perfectly standard, memory-efficient `BehaviorTreeBlob` for the runtime interpreter to tick.

**5\. Live, Color-Coded UI Visualizer** To make debugging seamless, we extended the engine's `IImGuiRenderer` into a context-aware interface (`IEntityAwareImGuiRenderer`). This allows the Entity Inspector to safely look up the static tree definition and combine it with the 64-byte `BehaviorTreeState`. The result is a rich visualizer that:

-   Highlights the currently executing path and active nodes in real-time using `RunningNodeIndex` and `NodeIndexStack`.-   Decodes and displays internal multi-frame node states, such as loop iterations tracked in `LocalRegisters` and active wait timers packed inside `AsyncData`.-   Displays human-readable tooltips detailing the exact C# source file, line number, and custom developer comments captured automatically during the fluent build process.

The json were originally meant for being authored by a graphical tool. With the capabilities of the c# framework it now looks to me like much better option to rather generate csharp code in our fluent format and let it compile using c# compiler into an assembly that can be scanned and the btrees found and auto-registered to the engine. Does that make sense?

Yes, that makes absolute sense! In fact, your intuition perfectly aligns with the architectural direction the engine is already taking for its other systems.

If you look at the sources, the engine is currently suffering from a "worst of both worlds" scenario with FastBTree: because there is no external graphical tool, the BTrees are literally being hardcoded as raw multi-line JSON strings directly inside the C# files! For example, in `CgfNodes.cs`, doctrines like `MoveToLocation`, `FollowRoute`, and `FireAtTarget` are defined as raw JSON strings that are parsed at runtime.

Transitioning this to a fluent C# builder and an assembly-scanning auto-registration system is the ideal path forward. Here is how it directly mirrors existing patterns in your codebase:

1\. The Precedent: FastHSM already does exactly this

The engine's Hierarchical State Machine (`FastHSM`) already implements the exact architecture you are proposing for FastBTree.

-   **Fluent Builder:** Instead of JSON, `FastHSM` uses an `HsmBuilder` in C# to define states, activities, and transitions. For instance, `ApcHsmSetup.cs` builds the `ConvoyEscort_HSM` entirely in type-safe C#, validating and emitting a binary `HsmDefinitionBlob` directly.-   **Roslyn Source Generators:** The engine includes an `Fhsm.SourceGen` project that scans your C# assembly during compilation for methods tagged with `[HsmAction]` or `[HsmGuard]`. It automatically generates the boilerplate `HsmActionRegistrar.g.cs` to bind these function pointers into the kernel's dispatcher, completely eliminating manual registration errors.

2\. Auto-Registration is already an FDP Paradigm

Your idea to scan the assembly and auto-register the compiled trees fits perfectly with how the engine's `DoctrineRegistry` handles DTOs. Currently, `DoctrineSchemaDiscovery.AutoRegister` uses reflection to scan the assembly for types decorated with `[DoctrineContractAttribute]` and automatically wires them into the UI and behavior remappers.

The Ideal FastBTree Workflow

By replacing the JSON approach with your fluent C# builder API, you could create a `Fbt.SourceGen` project that completes the pipeline:

-   **Authoring:** Developers define BTrees in pure C# using the fluent `BTreeBuilder` (as we designed earlier), utilizing lambda expressions to safely map parameters to the `BrainBlackboard`.-   **Compilation:** You decorate these builder methods with an attribute like `[BTreeDefinition("Ambush_BT")]`.-   **Source Generation:** During the build step, your Roslyn source generator scans for these attributes, automatically generates the string-to-delegate mapping required by `ActionRegistry`, and emits the `BehaviorTreeBlob`.-   **Startup:** `CgfDoctrineSetup.RegisterAll` simply calls an auto-generated `FbtRegistrar.RegisterAll(registry)`, instantly registering all BTrees without parsing a single string of JSON at runtime.

**Conclusion:** Moving away from JSON to a fluent, C#-compiled, and auto-registered approach eliminates the parsing overhead during `TreeCompiler.CompileFromJson`, gives you compile-time safety against typos in node names, ensures memory offsets never break, and matches the high-performance design philosophy already established by `FastHSM` and `Fhsm.SourceGen`.

amazing idea! pls detail what the Fbt.SourceGen should be all about

Based on the engine's existing architecture and our previous design iterations, building **Fbt.SourceGen** is the perfect way to modernize FastBTree. We can directly mirror the Roslyn incremental source generator pattern already used by FastHSM (`Fhsm.SourceGen`).

Here is a detailed blueprint of what the `Fbt.SourceGen` project should do, how it hooks into the pipeline, and the code it will generate.

1\. The Core Architecture (The `IIncrementalGenerator`)

Just like `HsmActionGenerator`, `Fbt.SourceGen` will implement `Microsoft.CodeAnalysis.IIncrementalGenerator`. During compilation, it will hook into the Roslyn compiler pipeline to scan your C# code for specific behavior tree attributes, analyze the syntax tree, and emit generated C# files (`.g.cs`) directly into the compilation.

It will introduce three new marker attributes:

-   `[BTreeCondition]` and `[BTreeAction]`: To tag your reusable logic delegates (similar to `[HsmGuard]` and `[HsmAction]`).-   `[BTreeDefinition("TreeName")]`: To tag the static methods that define your fluent `BTreeBuilder` trees.

2\. What the Developer Writes (The Input)

With the source generator in place, your AI developers simply write clean, type-safe C# code without worrying about registration, string keys, or memory offsets:

```
public static class UrbanCombatDoctrines
{
    // 1. Define the highly reusable, type-safe condition
    [BTreeCondition]
    public static NodeStatus CheckAmmo(ref int ammo, ref BehaviorTreeState state, ref BTreeContext ctx)
    {
        return ammo > 0 ? NodeStatus.Success : NodeStatus.Failure;
    }

    // 2. Define the tree using the fluent builder
    [BTreeDefinition("Ambush_BT")]
    public static BTreeBuilder BuildAmbushTree()
    {
        return new BTreeBuilder()
            .Sequence(seq => seq
                // Type-safe lambda mapping to the blackboard DTO!
                .Condition((MockBlackboardState dto) => dto.AmmoCount, CheckAmmo)
                .Action(CgfNodes.Action_AimAndFire)
            );
    }
}
```

3\. What `Fbt.SourceGen` Does at Compile Time

When you build the project, the source generator performs several critical tasks behind the scenes:

**A. Compile-Time Offset Resolution** Instead of using `Marshal.OffsetOf` at runtime (like we did in our previous iterations), the Roslyn semantic model analyzes the lambda `(MockBlackboardState dto) => dto.AmmoCount`. It looks up the struct layout of `MockBlackboardState` at compile time and calculates that `AmmoCount` is exactly at byte offset `4`.

**B. Static Blob Generation (Skipping the DOM)** Because the tree structure is defined statically, the source generator can optionally execute the builder logic _during compilation_. It flattens the hierarchy and pre-calculates the `BehaviorTreeBlob`'s `NodeDefinition[]`, `IntParams`, and `FloatParams` arrays, converting them directly into static `ReadOnlySpan<byte>` arrays. This completely eliminates the need for `TreeCompiler.CompileFromJson` and node flattening at startup.

**C. Generating the Linkage (The "Magic" Strings)** FastBTree's `BehaviorTreeBlob` uses string arrays (`MethodNames`) to link compiled nodes to delegates. The source generator automatically generates stable names (e.g., `"CheckAmmo_Offset4"`) and injects them into the generated blob.

4\. What `Fbt.SourceGen` Emits (The Output)

The generator will emit a file named `FbtActionRegistrar.g.cs` (mirroring `HsmActionRegistrar.g.cs`). This generated code automatically handles the high-performance memory projection (using `Unsafe.AddByteOffset`) and registers everything seamlessly.

```
// <auto-generated/>
using System;
using System.Runtime.CompilerServices;
using Fbt.Runtime;

namespace Fdp.Examples.UrbanCombat.Generated
{
    public static class FbtActionRegistrar
    {
        public static void RegisterAll(ActionRegistry<BrainBlackboard, BTreeContext> registry)
        {
            // 1. Auto-generated curried delegate for CheckAmmo at offset 4
            registry.Register("CheckAmmo_Offset4", 
                (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int pIdx) => 
                {
                    // Zero-allocation, zero-pointer ref projection generated by Roslyn!
                    ref byte bbBase = ref Unsafe.As<BrainBlackboard, byte>(ref bb);
                    ref byte targetRef = ref Unsafe.AddByteOffset(ref bbBase, (nint)4);
                    ref int subDtoRef = ref Unsafe.As<byte, int>(ref targetRef);
                    
                    return UrbanCombatDoctrines.CheckAmmo(ref subDtoRef, ref state, ref ctx);
                });

            // ... registers other actions ...
        }
    }
    
    public static class FbtTreeCatalog
    {
        // 2. The pre-compiled Blob emitted as static data, zero parsing cost!
        public static BehaviorTreeBlob GetAmbush_BT() 
        {
            return new BehaviorTreeBlob 
            {
                TreeName = "Ambush_BT",
                Version = 1,
                MethodNames = new[] { "CheckAmmo_Offset4", "Action_AimAndFire" },
                // ... pre-calculated flat NodeDefinition array ...
            };
        }
    }
}
```

5\. The Ultimate Application Startup

Because of `Fbt.SourceGen`, your engine startup code in `CgfDoctrineSetup.RegisterAll` becomes incredibly clean. You no longer need to parse raw JSON strings. You just invoke the generated registrar:

```
public static void RegisterAll(DoctrineRegistry registry)
{
    var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
    
    // Call the source-generated registrar to bind all memory-projected delegates
    FbtActionRegistrar.RegisterAll(actionReg);

    // Register doctrines directly from the source-generated catalog
    registry.Register(CgfDoctrineIds.Ambush_BT, "Ambush", new DoctrineDefinition
    {
        Name = "Ambush",
        BrainTier = BehaviorConstants.BrainTierBTree,
        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
            FbtTreeCatalog.GetAmbush_BT(), actionReg)
    });
}
```

Summary of Benefits

By implementing `Fbt.SourceGen`, you align FastBTree with the elite, high-performance standard set by FastHSM:

-   **Zero Runtime Parsing:** JSON string parsing (`TreeCompiler.CompileFromJson`) is eliminated from the startup hot path.-   **Zero Reflection:** Byte offsets for blackboard parameters are resolved entirely during the Roslyn compilation step.-   **Compile-Time Safety:** If a developer renames a DTO property or an action method, the build fails immediately—no more silent runtime errors from misspelled JSON keys.-   **Developer Experience:** AI designers get full IDE autocomplete, refactoring support, and inline documentation when building their behavior trees.

cool! i would like use the Fbt.SourceGen while defining multiple (project specific) btrees (and their custom conditions and actions in separate assemblies containing a registration method that i can scan for dynamically and use fo registering at engine startup. Would that be possible?

Yes, this is absolutely possible and represents the gold standard for a modular, extensible engine architecture!

Because Roslyn Source Generators run at the compilation level for _each individual project_, `Fbt.SourceGen` will automatically generate a separate registration class for every assembly that uses it. You can then use reflection at engine startup to discover and invoke these generated registrars dynamically.

This approach is highly idiomatic to the FDP/HROT engine. The engine already uses this exact pattern for discovering UI panels (`ImGuiRendererRegistry.ScanAllAssemblies()`), schema DTOs (`DoctrineSchemaDiscovery.AutoRegister`), and even entire subsystem plugins (`ScanForSubsystems()` in `ClusterRunner/Program.cs`).

Here is how you can implement this cross-assembly auto-registration pipeline:

1\. Introduce a Marker Attribute

First, define a simple marker attribute in your core library (e.g., `Fbt.Kernel`) that the source generator will apply to the code it emits. This tells the engine scanner exactly what to look for.

```
[AttributeUsage(AttributeTargets.Class)]
public class FbtRegistrarAttribute : Attribute { }
```

2\. What `Fbt.SourceGen` Emits Per Project

When `Fbt.SourceGen` runs on your individual projects (like `Project.Core.AI` or `Project.Combat.AI`), it generates a localized registrar and tags it with the marker attribute. Notice how it passes both the `ActionRegistry` and the `DoctrineRegistry`:

```
// <auto-generated/>
using System;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;

namespace Project.Combat.AI.Generated
{
    [FbtRegistrar] // <-- The marker attribute
    public static class FbtActionRegistrar
    {
        public static void RegisterAll(
            ActionRegistry<BrainBlackboard, BTreeContext> actionReg, 
            DoctrineRegistry doctrineReg)
        {
            // 1. Registers memory-projected actions local to THIS assembly
            actionReg.Register("Auto_CheckAmmo_Offset4", ...);

            // 2. Registers the compiled blob local to THIS assembly
            var ambushBlob = new BehaviorTreeBlob { ... };
            doctrineReg.Register(2001, "Ambush_BT", new DoctrineDefinition
            {
                Name = "Ambush",
                BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(ambushBlob, actionReg)
            });
        }
    }
}
```

3\. The Engine Auto-Discovery Scanner

At engine startup (e.g., inside `CgfDoctrineSetup.RegisterAll` or a central bootstrapper), you write a small reflection utility to scan the `AppDomain` for any class tagged with `[FbtRegistrar]`, and invoke its `RegisterAll` method:

```
public static class FbtAutoDiscovery
{
    public static void ScanAndRegister(
        ActionRegistry<BrainBlackboard, BTreeContext> actionReg, 
        DoctrineRegistry doctrineReg)
    {
        // Search all currently loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try 
            {
                // Find generated classes marked with [FbtRegistrar]
                var registrars = assembly.GetTypes()
                    .Where(t => t.GetCustomAttribute<FbtRegistrarAttribute>() != null);

                foreach (var registrarType in registrars)
                {
                    // Find and invoke the static RegisterAll method
                    var method = registrarType.GetMethod("RegisterAll", 
                        BindingFlags.Public | BindingFlags.Static);
                        
                    method?.Invoke(null, new object[] { actionReg, doctrineReg });
                }
            }
            catch (ReflectionTypeLoadException) 
            {
                // Safely skip uncooperative system/dynamic assemblies [4]
            }
        }
    }
}
```

4\. A Crucial Engine Detail: Eager Loading

Because the C# compiler occasionally drops references to plugin assemblies if no type is statically referenced in the code, the `AppDomain.CurrentDomain.GetAssemblies()` call might initially miss your separate project assemblies.

The HROT `ClusterRunner` already solves this elegantly. In `Program.cs`, there is a method called **LoadReferencedAssemblies()** that scans the deployment directory for `.dll` files matching your project prefix (e.g., `Hrot.*` or `Fdp.*`) and forces them to load via `Assembly.Load()`.

As long as your separate AI assemblies are copied to the output directory (which happens automatically if you project-reference them), the engine will eagerly load the DLLs, your `FbtAutoDiscovery` will find the `[FbtRegistrar]` classes, and all BTrees from all projects will be cleanly injected into the kernel with zero manual registration!

Later I would definitely like to edit the btrees in a graphical authoring tool that outputs the csharp source code and automatically compiles it into a dynamically loadable assembly. How to approach this? Does this requires (or can benefit from) some changes in the solution we have designed so far?

This approach is an excellent idea. Generating C# code from a visual tool and compiling it into a dynamically loadable assembly is actually the most robust and performant way to build modern game AI. It keeps your engine completely free of heavy visual-graph parsers or interpreter overhead while still giving your designers a node-based UI.

The solution we have designed so far (Fluent C# Builder + Roslyn Source Generator + Auto-Discovery) is already 90% of the way there. If a graphical tool generates our fluent C# syntax, the Roslyn generator will still automatically calculate the memory offsets and emit the binary blobs just as if a programmer wrote it.

However, to fully support a seamless visual authoring workflow with dynamic loading (especially if you want to hot-reload trees while the game is running), we should make a few enhancements to our design.

Here is how you approach this and the specific changes we should make to our solution:

1\. Link Compiled Nodes to Visual Tool Nodes (Node Identity)

When the graphical tool connects to the running game for live debugging, the engine needs to tell the tool exactly which node is currently executing. Right now, our `NodeDebugMetadata` relies on C# line numbers and file names.

**The Change:** We need to update our `BTreeBuilder` so the visual tool can inject its own stable unique identifiers (like a UUID) for each node when it generates the C# code.

```
// Generated by the visual tool:
builder.Condition((MockBlackboardState dto) => dto.AmmoCount, CheckAmmo, visualId: "node_5a3f8");
```

We then store this `visualId` in the `BehaviorTreeBlob.DebugMetadata`. During live debugging, your custom UI (or the network telemetry sending data back to the visual tool) can read `state.RunningNodeIndex`, look up `blob.DebugMetadata[index].VisualId`, and tell the external tool exactly which box to highlight in green.

2\. Implement a FastBTree Hot-Reload Manager

If the visual tool compiles a new `.dll` while the engine is running, we need to hot-reload the behavior tree without breaking entities currently executing it.

If you look at the engine's `FastHSM` library, it already has a `HotReloadManager` that perfectly handles this problem. It compares the old blob's `StructureHash` against the new blob to determine if it can perform a "Soft Reload" (only parameters changed) or a "Hard Reset" (the tree shape changed).

**The Change:** The FastBTree `BehaviorTreeBlob` already contains a `StructureHash` and `ParamHash`, and the `Interpreter.Tick` method actually contains a placeholder comment reading `// === HOT RELOAD CHECK (Stub for now) ===`. We need to implement a BTree `HotReloadManager`. When the engine detects a new DLL, it scans it for `[FbtRegistrar]`, updates the `DoctrineRegistry`, and uses the hashes to seamlessly transition the `BehaviorTreeState` of live entities to the new logic.

3\. Use `AssemblyLoadContext` for True Dynamic Unloading

Currently, the HROT `ClusterRunner` uses `Assembly.Load` to eagerly load referenced plugin assemblies at startup. While this works for auto-discovery, standard .NET AppDomains cannot _unload_ an assembly once loaded.

**The Change:** To support continuous iteration where the visual tool repeatedly compiles and injects a new `.dll`, the engine's auto-discovery scanner must be updated to load these specific AI project assemblies into a collectible **AssemblyLoadContext** **(ALC)**. When the tool pushes a new DLL, the engine:

-   Unloads the old ALC.-   Loads the new DLL into a new ALC.-   Invokes the generated `FbtActionRegistrar.RegisterAll()` to overwrite the pointers in the `ActionRegistry` and `DoctrineRegistry`.

4\. Create a Schema Exporter for the Visual Tool

The graphical tool needs to know what actions, conditions, and blackboard DTOs exist in the engine so it can populate its node palette.

**The Change:** Because we are using strongly typed C# delegates tagged with `[BTreeCondition]` and `[BTreeAction]`, we can write a tiny utility in the engine that uses reflection to find all these methods, reads their generic `TBlackboardDto` parameters, and exports a `BTreeSchema.json` file.

The visual tool reads this schema on startup. If an engine programmer adds a new condition:

```
[BTreeCondition]
public static NodeStatus CheckSuppression(ref SuppressionDto data, ref BehaviorTreeState state, ref BTreeContext ctx)
```

The schema exporter automatically sees it, tells the visual tool about it, and the visual tool adds a "Check Suppression" node to its UI, knowing exactly which DTO fields to expose to the designer.

Summary of the Pipeline

With these minor additions, your pipeline becomes incredibly powerful:

-   **Visual Tool:** The designer drags a "Check Ammo" node into a sequence and sets the threshold to 5.-   **Code Generation:** The tool silently writes a `.cs` file using our fluent `BTreeBuilder`, embedding `"node_123"` as the visual ID.-   **Compilation:** The tool triggers `dotnet build`. Our Roslyn `Fbt.SourceGen` resolves the exact memory offset for "Ammo" at compile time and embeds it into the bytecode array without parsing any JSON.-   **Hot Reload:** The tool signals the engine. The engine loads the new `.dll` via ALC, the `HotReloadManager` patches the active `DoctrineRegistry`, and the live enemies immediately start using the new logic. Live telemetry flows back to the tool highlighting `"node_123"` in real-time.

The graphical authoring tool will be c# based so it can easily parse the btree code itself, no need to use an intermediate file format. But for sure we will need to define data structures describing the btrees "decompiled" from the csharp sources so the authoring tool can directly use it!

The btree hot reload manager should mimic the HSM approach. Not sure if the HSM uses ALC, but whatever it uses, we could reuse!

You are spot on! Parsing the C# source directly using Roslyn is a fantastic approach. It completely bypasses the need for an intermediate file format, and we can directly mirror the compiler graph architecture that FastHSM already uses.

Furthermore, looking at the FastHSM source code, its `HotReloadManager` **does not actually use AssemblyLoadContext (ALC)** internally. Instead, it relies on swapping the immutable `HsmDefinitionBlob` and patching the unmanaged state of live entities in memory based on hash comparisons. We can adapt this exact, highly efficient pattern for FastBTree.

Here is the design for both the authoring tool's graph data structures and the BTree Hot Reload Manager:

1\. The "Decompiled" Graph Data Structures (`Fbt.Compiler.Graph`)

To allow your C# visual authoring tool to render and edit the trees, we should mimic FastHSM's `Fhsm.Compiler.Graph` namespace (which uses `StateMachineGraph` and `StateNode`).

When the authoring tool uses Roslyn to parse your fluent C# builder code, it will construct this mutable DOM:

```
namespace Fbt.Compiler.Graph
{
    /// <summary>
    /// Root container for the behavior tree graph before compilation.
    /// Mirrors FastHSM's StateMachineGraph.
    /// </summary>
    public class BehaviorTreeGraph
    {
        public string TreeName { get; set; }
        public Guid TreeId { get; set; } // Stable ID for the tool
        
        public BehaviorTreeNode RootNode { get; set; }
    }

    /// <summary>
    /// Base representation of a node for the visual authoring tool.
    /// </summary>
    public abstract class BehaviorTreeNode
    {
        public Guid VisualId { get; set; } // Matches the visualId injected into C#
        public NodeType Type { get; set; }
        public BehaviorTreeNode Parent { get; set; }
        
        // UI Metadata (can be parsed from attributes or comments)
        public float UiPosX { get; set; }
        public float UiPosY { get; set; }
        public string CustomComment { get; set; }
    }

    // --- Specific Node Types ---

    public class CompositeNode : BehaviorTreeNode
    {
        // Selectors, Sequences, Parallels
        public List<BehaviorTreeNode> Children { get; } = new();
        public int ParallelPolicy { get; set; } // Only used if Type == Parallel
    }

    public class DecoratorNode : BehaviorTreeNode
    {
        // Wait, Cooldown, Repeater
        public BehaviorTreeNode Child { get; set; }
        
        // The static parameters the designer edits in the UI
        public float Duration { get; set; } 
        public int RepeatCount { get; set; }
    }

    public class LogicNode : BehaviorTreeNode
    {
        // Actions and Conditions
        public string DelegateName { get; set; } // e.g., "CheckAmmo"
        
        // Extracted from the C# Lambda: (MockBlackboardState dto) => dto.AmmoCount
        public string TargetDtoType { get; set; } 
        public string TargetFieldName { get; set; }
    }
}
```

Your authoring tool uses this DOM to draw the boxes and wires. When the user saves, the tool simply walks this graph and writes out the fluent C# code strings to a `.cs` file, allowing Roslyn to compile it normally.

2\. The BTree Hot Reload Manager

FastHSM's `HotReloadManager` works by comparing the `StructureHash` and `ParameterHash` of the old and new blobs. FastBTree's `BehaviorTreeBlob` already possesses these exact two hash fields.

We can create a `BTreeHotReloadManager` that uses the exact same `ReloadResult` enum as FastHSM. The engine will pass the newly compiled `BehaviorTreeBlob` (fished out of the newly compiled assembly) and a span of the live `BrainBTreeState` components:

```
using System;
using Fbt.Runtime;

namespace Fbt.HotReload
{
    public enum ReloadResult
    {
        NewTree,      // First time loading this tree ID
        NoChange,     // Hashes match, no reload needed
        SoftReload,   // Parameters changed, state preserved
        HardReset     // Structure changed, state cleared
    }

    public class BTreeHotReloadManager
    {
        private readonly Dictionary<string, BehaviorTreeBlob> _loadedBlobs = new();

        public ReloadResult TryReload(
            string treeName, 
            BehaviorTreeBlob newBlob, 
            Span<BrainBTreeState> liveInstances)
        {
            if (!_loadedBlobs.TryGetValue(treeName, out var oldBlob))
            {
                _loadedBlobs[treeName] = newBlob;
                return ReloadResult.NewTree;
            }

            bool structureChanged = newBlob.StructureHash != oldBlob.StructureHash;
            bool parameterChanged = newBlob.ParamHash != oldBlob.ParamHash;

            if (!structureChanged && !parameterChanged)
                return ReloadResult.NoChange;

            // Update the registry to point to the new logic
            _loadedBlobs[treeName] = newBlob;

            if (structureChanged)
            {
                // Hard Reset: The shape of the tree changed (e.g., added a new sequence).
                // We must wipe the execution state of all live entities.
                for (int i = 0; i < liveInstances.Length; i++)
                {
                    // FastBTree has a built-in Reset() that safely clears the 
                    // 64-byte unmanaged state, zeroes the stacks, and bumps the TreeVersion
                    liveInstances[i].State.Reset(); 
                }
                return ReloadResult.HardReset;
            }

            if (parameterChanged)
            {
                // Soft Reload: Only static parameters (Wait times, offsets) changed.
                // Because FastBTree retrieves parameters dynamically via payload indices 
                // during Tick(), we do nothing to the instances! They will automatically 
                // read the new FloatParams/IntParams on the next frame.
                return ReloadResult.SoftReload;
            }

            return ReloadResult.NoChange;
        }
    }
}
```

How ALC fits into this

While the `HotReloadManager` itself doesn't need to know about `AssemblyLoadContext`, your host application (like the HROT Editor) can absolutely use it!

When your visual tool compiles a new DLL:

-   The Editor loads the DLL into a collectible `AssemblyLoadContext`.-   It uses reflection to find the generated `FbtTreeCatalog.GetAmbush_BT()` method and extracts the `BehaviorTreeBlob`.-   It passes that new Blob to `BTreeHotReloadManager.TryReload`.-   The manager patches the memory of the live entities (doing a `SoftReload` or `HardReset`).-   Because the instances just execute against the new `BehaviorTreeBlob` arrays, the old `AssemblyLoadContext` can be cleanly unloaded by the Editor, freeing the memory.

While authoring, I will certanly need to change not just the BTree structure, but also the condition and action method code. I assume these could be compiled to the custom project assembly together with fluent btree structure definition. And i will need the hot reload mechanism to reload also these, which migh require the ALC. Let' hope the ALC allows working in the same memory space so that the BTrees can interact with the engine internals (like access entity compoennts etc.)

Yes, your assumption is completely correct! **AssemblyLoadContext** **(ALC) operates entirely within the same process and shared memory space.**

Because of how .NET handles ALCs and how the FDP engine's ECS is architected, hot-reloading your custom C# conditions and actions alongside the tree structure is not only possible, but it will be incredibly fast and seamless.

Here is how the ALC memory space interacts with the engine internals to make this work:

1\. Shared Type Identity (The Default ALC)

An ALC provides isolation for _assembly loading_, but it does not create a hard memory boundary like an AppDomain used to.

To allow your custom BTree assembly to interact with the engine internals, the core engine assemblies (`Fdp.Core`, `Fbt.Kernel`) are loaded into the **Default AssemblyLoadContext**. When your visual authoring tool compiles your custom AI project into a new DLL and loads it into a custom ALC, the runtime resolves all engine references (like `EntityRepository` and `BrainBlackboard`) back to the Default ALC.

This means your hot-reloaded actions and conditions understand the exact same types as the engine kernel.

2\. Zero-Cost Memory Access via ECS

The FDP engine is built on unmanaged, contiguous memory arrays managed by the `NativeMemoryAllocator`. The state components your trees rely on, such as `BehaviorTreeState` and `BrainBlackboard`, are strictly unmanaged structs.

When a newly hot-reloaded C# action executes from your custom ALC, it receives `ref BrainBlackboard` and `ref BehaviorTreeState` directly. **Because it is the same memory space, your new code is performing direct pointer arithmetic on the live ECS memory.** There is zero marshalling, zero serialization, and zero cross-domain penalty.

3\. Hot-Reloading the Actions and Conditions

Here is exactly how the hot reload flow will work for your custom C# code:

-   **Recompile:** You change the code inside `Action_AimAndFire`. The visual tool compiles a new `MyProject.AI.v2.dll`.-   **Load:** The engine loads this DLL into a _new_ ALC.-   **Register:** The engine calls the source-generated `FbtActionRegistrar.RegisterAll` inside the new DLL. This method overwrites the string keys (like `"Action_AimAndFire"`) in the engine's global `ActionRegistry`.-   **Tick:** The `Interpreter` is oblivious to the ALC. When it ticks a node with `PayloadIndex` 5, it looks up the delegate in the registry and invokes it. That delegate now points to the newly JIT-compiled memory address of your updated method in the new ALC.-   **Unload:** The old ALC is told to unload. Once all live trees finish their current tick and drop their references to the old delegates, the garbage collector safely removes the old assembly from memory.

4\. Synergy with the BTree Hot Reload Manager

While the ALC handles swapping the C# function pointers, your `BTreeHotReloadManager` (mirroring the FastHSM `HotReloadManager`) handles the state.

If you changed a condition's threshold parameter in the builder, the manager detects a `ParameterHash` change and performs a `SoftReload`, seamlessly pointing the active entities to the new `BehaviorTreeBlob`'s static parameters. If you completely restructured the tree by adding new sequence nodes, it detects a `StructureHash` change and triggers a `HardReset`, which safely zeroes the 64-byte `BehaviorTreeState` execution stack so the entities restart the new logic cleanly.

**In summary:** Because your actions use pure `ref` parameters pointing into native memory chunks, ALC hot-reloading will feel like absolute magic. You can rewrite the internal C# math of an action, hit save, and watch the live entities instantly change their behavior on the very next 60Hz tick.

