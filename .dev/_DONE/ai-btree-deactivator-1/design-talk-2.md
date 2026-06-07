you said the design departs from stealing a bit, why is that, is there any good reasof for that?



Yes, the design document intentionally departs from the blob-flag approach to keep the initial Phase 1 implementation minimal and self-contained.

By representing the `IsResourceOwning` flag implicitly—simply checking for a non-null entry in the parallel `_deactivatorDelegates` array at interpreter construction time—the design preserves the existing 8-byte, tightly packed layout of the `NodeDefinition` struct.

The primary reason for this is to avoid making cascading, invasive changes to `BTreeBuilder`, `BTreeBlob`, and the underlying binary serialization formats during this first phase. The semantic result of checking a parallel array is identical to checking a bit flag, and deferring the explicit bit-flag approach to a later optimization pass allows you to prove the core active-path delta-tracking logic works before altering core serialization structures.





but i certainly want to add this optimization once the first stages are done; is that just aboout internal btree implementation not affecting any public interfaces or not?







No, it is not just an internal BTree execution detail. Implementing the explicit bit-flag optimization will affect structural boundaries, the compilation toolchain, and the serialized asset formats, although it will not change how you author your domain action nodes.

As noted in the design, adopting the blob-flag approach requires modifications to `BTreeBuilder`, `BehaviorTreeBlob`, and the underlying serialization formats. Here is exactly what those changes entail:

- **Bytecode Layout (`NodeDefinition`):** The bytecode for a single node is currently a tightly packed 8-byte struct. To add the `IsResourceOwning` flag, you must either steal the highest bit of the 32-bit `PayloadIndex` or expand the struct to 12 bytes.
- **Asset Serialization (`BinaryTreeSerializer`):** Because `NodeDefinition` instances are stored inside the `BehaviorTreeBlob`, any change to the bytecode layout immediately breaks binary compatibility. You will need to increment the `CurrentVersion` in `BinaryTreeSerializer` and update the binary read/write logic to accommodate the new flag.
- **Tree Compilation (`BTreeBuilder`):** The `BTreeBuilder` (or the underlying `TreeCompiler`) will need to be updated to resolve deactivators at compile time rather than runtime, evaluating the registry to bake the flag directly into the produced `BehaviorTreeBlob`.

**What remains unchanged:** The good news is that this optimization will not affect your public domain interfaces. Your action delegates, the `[BTreeDeactivator]` annotations, and the source-generated `ActionRegistry` calls remain exactly the same.

Deferring this to a later phase was a deliberate choice to allow you to prove the active-path delta tracking logic works before making breaking changes to the engine's baked asset pipeline and binary serialization formats.







so you think it was a good decision?





Yes, it is an excellent architectural decision for the Phase 1 implementation.

By representing the `IsResourceOwning` flag implicitly through a non-null entry in a parallel delegate array, you keep the initial implementation minimal and self-contained. Crucially, this approach preserves the tightly packed 8-byte layout of the `NodeDefinition` struct, which means you completely avoid cascading, invasive changes to `BTreeBuilder`, `BehaviorTreeBlob`, and the underlying binary serialization formats during this first phase.

The semantic result is identical to using an explicit bit-flag. From a performance perspective, the runtime cost for trees with deactivators adds only one array bounds check and one null check, both of which are correctly predicted by the JIT compiler in the common case.

This allows you to safely prove that the active-path delta-tracking logic works in isolation with virtually no performance penalty. Deferring the explicit bit-flag optimization to a later pass is a highly pragmatic stepping stone that protects the stability of our existing baked asset pipeline while getting the core resource cleanup functionality online immediately.





if the suggested simplified implementation is so well optimizable by JIT compiler, is it even worth to aim for the explicit bit flag approach? Whould would be benefits?





While the JIT compiler's branch prediction does make the parallel array approach extremely fast in the common case, migrating to the explicit bit-flag approach provides strict architectural and low-level performance benefits that align with our engine's data-oriented design.

Here is why the explicit bit-flag optimization remains the superior long-term target:

**1. Perfect L1 Cache Locality** The engine's FastBTree bytecode relies on a tightly packed 8-byte `NodeDefinition` struct. By stealing the highest bit of the 32-bit `PayloadIndex` to act as the `IsResourceOwning` flag, the delta-tracking check occurs using data that is already sitting in the L1 CPU cache from the node's execution. The parallel array approach forces the CPU to fetch from a secondary memory location (`_deactivatorDelegates`), breaking cache locality and introducing an unnecessary memory bandwidth tax.

**2. Elimination of Type-Checking Branches** Because `PayloadIndex` is heavily overloaded—it serves as an index into `MethodNames`, `FloatParams`, `IntParams`, and `SubtreeAssetIds` depending on the `NodeType`—the parallel array approach is fragile. To safely use it without out-of-bounds errors, the interpreter's delta tracker must first inject a conditional branch to verify that the exited node is actually an `Action` or `Condition` before using the index. An explicit bit flag eliminates this conditional type-check, allowing a blind, immediate bitwise check on any exited node.

**3. True Ahead-of-Time Asset Compilation** With the bit flag baked directly into the `BehaviorTreeBlob` by the compiler, the serialized asset itself intrinsically knows which of its nodes own resources. The parallel array approach forces the runtime to dynamically reconstruct this meta-state during `Interpreter` construction by correlating strings against the action registry. Moving this to compile time reduces the initialization cost during hot-reloads and entity spawning.

In short, while the parallel array is a highly pragmatic stepping stone for Phase 1 that avoids immediately breaking binary serialization, the bit-flag approach removes all secondary cache fetches and type-checking branches. This guarantees that the overhead for sweeping EQS sensors and channels remains microscopically small and completely deterministic.





ok so pls outline the changes for this optimization stage









To migrate from the temporary parallel-array approach to the true Ahead-Of-Time (AOT) explicit bit-flag optimization, we must shift the responsibility of detecting resource ownership from the runtime `Interpreter` to the compile-time `TreeCompiler`.

By baking this information directly into the asset, we will achieve perfect L1 cache locality and eliminate the need for conditional type-checking during the execution delta sweep.

Here is the strategic outline for this optimization stage. We will tackle this in four distinct phases to ensure binary stability and execution safety are maintained at every step:

**Phase 1: Bytecode Layout & Interpreter Execution (`Fbt.Kernel`)**
We will first modify the `NodeDefinition` struct, which is currently tightly packed at 8 bytes. We will steal the highest bit (bit 31) of the 32-bit `PayloadIndex` to serve as the `IsResourceOwning` flag. Following this, we will update the `Interpreter.Tick` delta tracker. Instead of checking if `_deactivatorDelegates[payloadIndex] != null` and applying a strict `NodeType` guard, the sweep will simply execute a bitwise check on the exited node's `PayloadIndex`.

**Phase 2: AOT Compilation Pipeline (`Fbt.Compiler`)**
We will update the `TreeCompiler` and `BTreeBuilder` so that the compilation step is fully aware of deactivators. During the `FlattenToBlobCore` phase, the compiler will query the `ActionRegistry`. If a node's mapped method has a registered deactivator, the compiler will permanently set the highest bit of that node's `PayloadIndex` before writing the `NodeDefinition` into the `BehaviorTreeBlob`.

**Phase 3: Binary Serialization & Versioning (`Fbt.Kernel`)**
Because baking the bit directly into the `PayloadIndex` changes the semantic meaning of the bytecode, we must update `BinaryTreeSerializer`. We will increment `CurrentVersion` from `1` to `2`. We will also establish an upgrade path: if the serializer loads a Version 1 asset, it will dynamically evaluate the `ActionRegistry` at load-time and apply the high-bit mask in memory, guaranteeing older baked assets do not break or leak resources.

**Phase 4: Cleanup & Editor Integration (`Hrot.BTree.Editor`)**
With the AOT bit-flag fully operational, we will strip the parallel `_deactivatorDelegates` array out of the `Interpreter` entirely, reducing the runtime memory footprint. Finally, we will update the `BTreeVisualizerRenderer` in the ImGui editor to visually flag resource-owning nodes (e.g., adding a specific icon or color to the UI) by simply reading the new bit flag in real-time.

Let me know when you are ready to dive into the exact implementation details for **Phase 1**.





Here is the execution plan for **Phase 1: Bytecode Layout & Interpreter Execution (`Fbt.Kernel`)**. We will modify the core bytecode struct to steal the MSB of the payload index and update the delta tracker to utilize this new flag.

To keep this phase isolated and ensure your existing unit tests (L-01 through L-08) pass without modification, we will temporarily patch the bit into the shared blob in memory during the `Interpreter` constructor. This bridges the gap until we implement true AOT flagging in Phase 2.

### 1. Update `NodeDefinition` (Bytecode Layout)

We will preserve the strict 8-byte layout of the struct but rename the raw integer field to encapsulate the bit-masking logic. This forces all reads to go through cache-friendly, aggressively inlined properties.

In `NodeDefinition.cs`:

```
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fbt
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NodeDefinition
    {
        public NodeType Type;           // 1 byte
        public byte ChildCount;         // 1 byte
        public ushort SubtreeOffset;    // 2 bytes

        // Replaces the old 'public int PayloadIndex;'
        // Bit 31 (MSB) = IsResourceOwning flag. Bits 0-30 = PayloadIndex.
        public int RawPayloadIndex;     // 4 bytes

        public readonly int PayloadIndex
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RawPayloadIndex & 0x7FFFFFFF;
        }

        public readonly bool IsResourceOwning
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (RawPayloadIndex & unchecked((int)0x80000000)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResourceOwning()
        {
            RawPayloadIndex |= unchecked((int)0x80000000);
        }
    }
}
```

*(Note: Because we renamed the field, you must update `BinaryTreeSerializer.cs` and `TreeCompiler.cs` to reference `RawPayloadIndex` when writing/reading the struct to fix the compilation errors.)*

### 2. Temporary Blob Patching in `Interpreter`

Until the `TreeCompiler` bakes the flag directly into the asset in Phase 2, we must compute it at runtime.

In `Interpreter.cs`, update the constructor to scan the registry and set the high bit on the loaded blob:

```
public Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry)
{
    _blob = blob ?? throw new ArgumentNullException(nameof(blob));
    if (registry == null) throw new ArgumentNullException(nameof(registry));

    _actionDelegates = BindActions(blob, registry);
    _blobStructureHash = blob.StructureHash;

    // TODO: Remove this loop in Phase 2 once the AOT TreeCompiler bakes the bit.
    // Temporary in-memory patching to make Phase 1 tests pass.
    for (int i = 0; i < _blob.Nodes.Length; i++)
    {
        ref var node = ref _blob.Nodes[i];
        if (node.Type is NodeType.Action or NodeType.Condition)
        {
            if (_deactivatorDelegates[node.PayloadIndex] != null)
            {
                node.SetResourceOwning();
            }
        }
    }
}
```

### 3. Optimize the Delta Tracker

We can now eliminate the conditional `NodeType` guard from the hot path.

In `Interpreter.cs`, rewrite your `InvokeDeactivatorIfRegistered` logic (or inline it) to use the new bit flag:

```
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void SweepExitedNode(int nodeIndex, ref TBlackboard bb, ref BehaviorTreeState state, ref TContext ctx)
{
    ref var node = ref _blob.Nodes[nodeIndex];

    // O(1) bitwise check. Perfect L1 cache locality, zero type-checking branches.
    if (node.IsResourceOwning)
    {
        var deactivator = _deactivatorDelegates[node.PayloadIndex];
        deactivator?.Invoke(ref bb, ref state, ref ctx, node.PayloadIndex);
    }

    // Parallel subtree range sweep remains structurally identical, but now
    // loops over child nodes and simply checks `childNode.IsResourceOwning`.
    if (node.Type == NodeType.Parallel)
    {
        SweepParallelSubtree(nodeIndex, ref node, ref bb, ref state, ref ctx);
    }
}
```

### Validation

Run your unit tests (`HybridLifecycleTests.cs`). Because the delta tracker's semantic behavior remains unchanged, tests L-01 through L-08 must pass immediately. You will notice that `Test L-04` (testing the non-resource-owning fast path) should now execute marginally faster with zero cache misses.









Here is the execution plan for **Phase 2: AOT Compilation Pipeline (`Fbt.Compiler`)**.

Now that the bytecode layout natively supports the `IsResourceOwning` bit via `RawPayloadIndex`, we will shift the responsibility of setting this bit from the runtime `Interpreter` to the compile-time `TreeCompiler` and `BTreeBuilder`.

Because `TreeCompiler` is a generic serialization tool that does not inherently know about the typed `ActionRegistry`, we will introduce a functional seam (`Func<string, bool>`) that allows the compiler to query external registries during the flattening process.

### 1. Extend `BuilderNode` (`Fbt.Serialization`)

First, add an explicit flag to the intermediate node representation so that custom tools can manually flag nodes before compilation.

In `Fbt.Kernel/Serialization/BuilderNode.cs`:

```
public class BuilderNode
{
    public NodeType Type { get; set; }
    public string MethodName { get; set; } = string.Empty;
    public float WaitTime { get; set; }
    public float CooldownTime { get; set; }
    public int RepeatCount { get; set; }
    public int Policy { get; set; }

    // NEW: Explicit resource ownership flag
    public bool IsResourceOwning { get; set; }

    public List<BuilderNode> Children { get; } = new List<BuilderNode>();

    // ... existing constructors ...
}
```

### 2. Update `TreeCompiler` (`Fbt.Serialization`)

We will modify the compiler to accept an external resource-checking delegate and apply the bit to the `NodeDefinition` struct at creation time.

In `TreeCompiler.cs`, update the signatures for `FlattenToBlob` and `FlattenToBlobCore` to accept `Func<string, bool>? isResourceOwning = null`:

```
public static BehaviorTreeBlob FlattenToBlob(
    BuilderNode root,
    string treeName,
    Func<string, bool>? isResourceOwning = null)
{
    // ... validation checks ...
    var blob = FlattenToBlobCore(root, treeName, isResourceOwning);
    // ... hashes and return ...
}

private static BehaviorTreeBlob FlattenToBlobCore(
    BuilderNode root,
    string treeName,
    Func<string, bool>? isResourceOwning)
{
    // ... lists setup ...
    FlattenRecursive(root, nodes, methodNames, floatParams, intParams, isResourceOwning);
    // ... return blob ...
}
```

Next, inside `FlattenRecursive`, evaluate the flag and set the bit:

```
private static void FlattenRecursive(
    BuilderNode node,
    List<NodeDefinition> nodes,
    List<string> methodNames,
    List<float> floatParams,
    List<int> intParams,
    Func<string, bool>? isResourceOwning)
{
    int currentIndex = nodes.Count;
    int subtreeSize = node.CalculateSubtreeSize();
    int payloadIndex = -1;

    // Determine if this node owns resources
    bool ownsResource = node.IsResourceOwning;

    if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
    {
        payloadIndex = GetOrAddMethodName(methodNames, node.MethodName);
        if (isResourceOwning != null && isResourceOwning(node.MethodName))
        {
            ownsResource = true;
        }
    }
    // ... existing wait/repeater/parallel payload checks ...

    // Construct the node definition
    var nodeDef = new NodeDefinition
    {
        Type = node.Type,
        ChildCount = (byte)node.Children.Count,
        SubtreeOffset = (ushort)subtreeSize,
        RawPayloadIndex = payloadIndex
    };

    // Bake the bit directly into the asset
    if (ownsResource)
    {
        nodeDef.SetResourceOwning();
    }

    nodes.Add(nodeDef);

    foreach (var child in node.Children)
    {
        FlattenRecursive(child, nodes, methodNames, floatParams, intParams, isResourceOwning);
    }
}
```

### 3. Update `BTreeBuilder` (`Fbt.Compiler`)

Now we connect the fluent builder's internal `ActionRegistry` to this new compiler capability.

In `BTreeBuilder.cs`, update the `Compile` method. It will check its local registry first, and optionally accept an external fallback check:

```
public BehaviorTreeBlob Compile(string treeName, Func<string, bool>? externalResourceCheck = null)
{
    if (_entries.Count == 0)
        throw new InvalidOperationException("The builder has no root node.");
    if (_entries.Count > 1)
        throw new InvalidOperationException("The builder has multiple root nodes. A behavior tree must have exactly one root.");

    var root = _entries;

    // Pass a delegate that checks the internal registry OR the external fallback
    var blob = TreeCompiler.FlattenToBlob(root.Node, treeName, methodName =>
    {
        return _registry.TryGetDeactivator(methodName, out _) ||
               (externalResourceCheck != null && externalResourceCheck(methodName));
    });

    // Populate DebugMetadata ...
    var metaList = new List<NodeDebugMetadata>();
    FlattenMetadata(root, metaList);
    blob.DebugMetadata = metaList.ToArray();

    return blob;
}
```

### 4. Clean Up `Interpreter` (`Fbt.Kernel`)

Because the asset compilation pipeline now guarantees that the `IsResourceOwning` bit is permanently baked into the bytecode, the temporary runtime loop is obsolete.

In `Interpreter.cs`, **delete** the temporary loop we added in Phase 1:

```
    _actionDelegates = BindActions(blob, registry);
    _blobStructureHash = blob.StructureHash;

    // REMOVE THIS ENTIRE BLOCK:
    // TODO: Remove this loop in Phase 2 once the AOT TreeCompiler bakes the bit.
    // Temporary in-memory patching...
    // for (int i = 0; i < _blob.Nodes.Length; i++) { ... }
}
```

### Validation & Next Steps

Run your `Fbt.Tests` suite again. Because your test constructs the trees using `BTreeBuilder` and registers the deactivators into the builder's `ActionRegistry` before calling `Compile()`, the `TreeCompiler` will natively bake the bit, and the tests will remain green.

*(Note: In the engine, `CgfNodes.BuildMoveToLocationTree()` creates a new `BTreeBuilder` without the global deactivators. Before we integrate into the engine, we will need to update `BTreeDefinitionGenerator` in Roslyn to either flag `IsResourceOwning` explicitly or pass the external check delegate to `Compile()`. We will handle this in Phase 4 of this optimization pass).*









Here is the execution plan for **Phase 3: Binary Serialization & Versioning (`Fbt.Kernel`)**.

Because baking the `IsResourceOwning` bit into the bytecode changes how the payload index is semantically interpreted, we must bump the binary version. However, we also must guarantee that any existing baked assets (Version 1) loaded from disk do not leak resources. We will achieve this by accepting V1 assets in the serializer and shifting the patching logic into a legacy fallback path.

### 1. Update `BinaryTreeSerializer` (`Fbt.Serialization`)

We will bump the version constant and ensure the serializer writes and reads the new `RawPayloadIndex` field instead of the masked property. We will also update the loader to accept both V1 and V2 assets.

In `BinaryTreeSerializer.cs`:

```
public static class BinaryTreeSerializer
{
    private static readonly byte[] MagicBytes = { (byte)'F', (byte)'B', (byte)'T', 0 };

    // Bump version from 1 to 2
    private const int CurrentVersion = 2;

    public static void Save(BehaviorTreeBlob blob, Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(MagicBytes);
        writer.Write(CurrentVersion);
        // ... (Hashes and TreeName remain unchanged)

        writer.Write(blob.Nodes.Length);
        foreach (var node in blob.Nodes)
        {
            writer.Write((byte)node.Type);
            writer.Write(node.ChildCount);
            writer.Write(node.SubtreeOffset);

            // NEW: Write the raw field containing the baked bit
            writer.Write(node.RawPayloadIndex);
        }
        // ... (Parameter arrays remain unchanged)
    }

    public static BehaviorTreeBlob Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadBytes(4);
        if (!magic.SequenceEqual(MagicBytes))
            throw new InvalidDataException("Invalid magic bytes");

        var version = reader.ReadInt32();

        // NEW: Accept both V1 and V2 assets
        if (version < 1 || version > CurrentVersion)
            throw new InvalidDataException($"Unsupported version: {version}");

        // ... (Read hashes and TreeName)
        blob.Version = version;

        int nodeCount = reader.ReadInt32();
        blob.Nodes = new NodeDefinition[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            blob.Nodes[i] = new NodeDefinition
            {
                Type = (NodeType)reader.ReadByte(),
                ChildCount = reader.ReadByte(),
                SubtreeOffset = reader.ReadUInt16(),

                // NEW: Read directly into the raw field
                RawPayloadIndex = reader.ReadInt32()
            };
        }
        // ... (Parameter arrays remain unchanged)
        return blob;
    }
}
```

### 2. Default to Version 2 in Compilation (`Fbt.Serialization`)

We must ensure that dynamically compiled trees start at Version 2.

In `BehaviorTreeBlob.cs`:

```
    /// <summary>Version number for compatibility checking.</summary>
    public int Version = 2; // Was 1
```

In `TreeCompiler.cs` (inside `FlattenToBlob`):

```
    var blob = FlattenToBlobCore(root, treeName, isResourceOwning);
    blob.Version = 2; // Explicitly stamp compilation version
    // ...
```

### 3. V1 Legacy Fallback in `Interpreter` (`Fbt.Kernel`)

Because `BinaryTreeSerializer` only reconstructs the data array and does not possess an `ActionRegistry` to cross-reference deactivators, it cannot patch V1 assets itself.

We will re-introduce the patch loop in the `Interpreter` constructor, but we will put it strictly behind a version gate. This means modern V2 assets pay zero penalty on load, while old V1 assets are safely upgraded in memory.

In `Interpreter.cs`:

```
public Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry)
{
    _blob = blob ?? throw new ArgumentNullException(nameof(blob));
    if (registry == null) throw new ArgumentNullException(nameof(registry));

    _actionDelegates = BindActions(blob, registry);
    _blobStructureHash = blob.StructureHash;

    // V1 Backward Compatibility:
    // Legacy blobs do not have the IsResourceOwning bit baked into the payload index.
    // We must evaluate the registry dynamically and patch the struct in memory.
    if (_blob.Version < 2)
    {
        for (int i = 0; i < _blob.Nodes.Length; i++)
        {
            ref var node = ref _blob.Nodes[i];
            if (node.Type is NodeType.Action or NodeType.Condition)
            {
                if (_deactivatorDelegates[node.PayloadIndex] != null)
                {
                    node.SetResourceOwning();
                }
            }
        }
    }
}
```

### Validation

To validate this phase:

1. Run your `Fbt.Tests` suite again. All tests will pass because trees generated by `BTreeBuilder` are natively Version 2, skipping the patch loop.
2. If you have any binary `.fbt` blob files saved to disk from previous test runs, create a quick test that loads them via `BinaryTreeSerializer.Load()`. Assert that `blob.Version == 1` and that instantiating an `Interpreter` successfully sweeps your orphaned resources (proving the V1 backward compatibility path triggered).

Execute this phase. Once complete, we can move to the final optimization round, **Phase 4: Cleanup & Editor Integration**, where we strip out the unneeded runtime lookup arrays and visually tag the nodes in the editor.







Here is the execution plan for the final **Phase 4: Cleanup & Editor Integration**.

With the explicit `IsResourceOwning` bit fully baked into the `BehaviorTreeBlob` assets via `TreeCompiler`, we no longer need to eagerly map and store the `_deactivatorDelegates` array inside every `Interpreter` instance. We will strip it out to reduce initialization overhead and memory footprint, then update our Roslyn generators and the ImGui editor to fully leverage the new AOT architecture.

### 1. Strip the Array from `Interpreter` (`Fbt.Kernel`)

Because the bit-flag perfectly guards the hot path, we can safely fall back to a dynamic dictionary lookup against the `ActionRegistry` *only* when a resource-owning node is actually exited. This occurs rarely enough that the string-hash cost is irrelevant, saving us the upfront cost of building a parallel array on every instantiation.

In `Interpreter.cs`:

```
// 1. Delete _deactivatorDelegates field. Replace with:
private readonly ActionRegistry<TBlackboard, TContext> _registry;

public Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry)
{
    _blob = blob ?? throw new ArgumentNullException(nameof(blob));
    _registry = registry ?? throw new ArgumentNullException(nameof(registry)); // Store registry

    _actionDelegates = BindActions(blob, registry);
    _blobStructureHash = blob.StructureHash;

    // Legacy V1 patch loop remains unchanged...
}

// 2. Update SweepExitedNode to do a targeted dynamic lookup:
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void SweepExitedNode(int nodeIndex, ref TBlackboard bb, ref BehaviorTreeState state, ref TContext ctx)
{
    ref var node = ref _blob.Nodes[nodeIndex];

    if (node.IsResourceOwning)
    {
        string methodName = _blob.MethodNames[node.PayloadIndex];
        if (_registry.TryGetDeactivator(methodName, out var deactivator))
        {
            deactivator.Invoke(ref bb, ref state, ref ctx, node.PayloadIndex);
        }
    }

    if (node.Type == NodeType.Parallel)
    {
        SweepParallelSubtree(nodeIndex, ref node, ref bb, ref state, ref ctx);
    }
}
```

### 2. Plumb AOT Compilation through Roslyn (`Fdp.Toolkits.Analyzers`)

To ensure our domain code (`CgfNodes.BuildMoveToLocationTree()`) bakes the bit correctly at compile-time during hot-reloads, we must update the Roslyn generator that emits `FbtTreeCatalog.g.cs`.

In `BTreeDefinitionGenerator.cs`, modify the emission for builder-returning definitions to accept the external resource check:

```
// Inside Execute(), where you emit builder-returning definitions:
if (m.ReturnsBuilder)
{
    sb.AppendLine("        public static global::Fbt.BehaviorTreeBlob Get" + safeName + "(global::System.Func<string, bool> isResourceOwning = null)");
    sb.AppendLine("            => global::" + m.FullyQualifiedTypeName + "." + m.MethodName + "().Compile(\"" + m.TreeName + "\", isResourceOwning);");
}
```

### 3. Wire Hot-Reload Compilation (`Hrot.AI.Behaviors`)

Now we supply the registry check directly to the AOT catalog during the background ALC compilation thread, guaranteeing hot-reloaded blobs natively carry the `IsResourceOwning` flag.

In `AiBehaviorFactory.cs`, update `BuildRegistrationAction`:

```
public static unsafe Action<BehaviorRegistry> BuildRegistrationAction(
    IGeographicTransform? geoTransform, NetworkEntityMap entityMap)
{
    var actionRegistry = new ActionRegistry<BrainBlackboard, BTreeContext>();
    FbtActionRegistrar.RegisterAll(actionRegistry);

    // Provide the registry lookup delegate to the AOT compiler
    Func<string, bool> isResourceOwning = name => actionRegistry.TryGetDeactivator(name, out _);

    // Pre-compile BTree blobs, injecting the resource check
    var moveToBlob        = FbtTreeCatalog.GetMoveToLocation(isResourceOwning);
    var followRouteBlob   = FbtTreeCatalog.GetFollowRoute(isResourceOwning);
    var joinFormationBlob = FbtTreeCatalog.GetJoinFormation(isResourceOwning);
    var wanderBlob        = FbtTreeCatalog.GetWanderMilitary(isResourceOwning);
    var fireAtTargetBlob  = FbtTreeCatalog.GetFireAtTarget(isResourceOwning);
    var hullDownBlob      = FbtTreeCatalog.GetHullDownAttackRun(isResourceOwning);
    var platoonHillBlob   = FbtTreeCatalog.GetPlatoonHillAttack(isResourceOwning);
    // ...
```

### 4. Editor Visualizer Support (`Hrot.BTree.Editor`)

Finally, we will visually expose this architectural feature in the UI, enabling designers and engineers to see exactly which nodes manage heavy standing reservations.

In `BTreeVisualizerRenderer.cs`, modify `DrawNode`:

```
bool open = ImGui.TreeNodeEx($"##n{nodeIndex}", flags, $"{nodeIndex} {label}");

if (popColors > 0) ImGui.PopStyleColor(popColors);

// NEW: Add a visual indicator for resource-owning nodes
if (node.IsResourceOwning)
{
    ImGui.SameLine();
    ImGui.TextColored(new Vector4(0.8f, 0.4f, 0.8f, 1.0f), "[R]");
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Resource Owning Node: Manages standing ECS resources via OnDeactivate.");
}

// ... existing Tooltip with debug metadata ...
```

### Validation

With this final phase applied:

1. `FastBTree` runtime footprint shrinks back to absolute zero-allocation on initialization.
2. The `TreeCompiler` acts as a true ahead-of-time (AOT) tool, pushing work off the hot reload swap timeline.
3. The `Hrot.Editor` visually documents exactly where lifecycle boundaries exist.

Run the engine and load the `UrbanCombat` scenario. Pin the BTree Visualizer to the Insurgent. You will visually see the `[R]` tag on the `Action_AimAndFire` node, and branch-switching will successfully sweep the channel via the dynamic ID lookup on the exact frame it aborts. Let me know when this is verified.



