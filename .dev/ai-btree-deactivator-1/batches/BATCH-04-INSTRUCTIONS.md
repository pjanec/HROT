# BATCH-04 Instructions — Phase 5: AOT Bit-Flag Optimization (EQL-009, EQL-010, EQL-011)

**Design reference:** `.dev/ai-btree-deactivator-1/DESIGN.md` §5.1, §5.2, §5.3
**Task details:** `.dev/ai-btree-deactivator-1/TASK-DETAIL.md` §TASK-EQL-009, §TASK-EQL-010, §TASK-EQL-011
**Prerequisites:** BATCH-01 through BATCH-03 complete and committed.

---

## Context

Phase 1 (BATCH-01) introduced a `_deactivatorDelegates` parallel array in `Interpreter<TBlackboard, TContext>`.
Each tick delta sweep, before looking up a deactivator, the code guards on `node.Type is Action or Condition`
because composite/decorator nodes cannot safely index into the array via their `PayloadIndex`.

Phase 5 replaces this with a single bit embedded in `NodeDefinition.RawPayloadIndex` (bit 31), eliminating
the type guard and the secondary array cache miss. This batch covers three sequential steps:
- **EQL-009**: Add the bit-flag API to `NodeDefinition`, fix all write sites, add a temporary
  Interpreter constructor patching loop as a bridge.
- **EQL-010**: AOT-bake the bit at compile time via `TreeCompiler.FlattenToBlob` and
  `BTreeBuilder.Compile`; remove the EQL-009 TODO loop.
- **EQL-011**: Bump binary format to V2; add a V1 legacy fallback in `Interpreter` constructor
  so old disk files still work.

Since all three tasks are in one batch, implement them sequentially. The **committed code** must
represent the final state after EQL-011 (no intermediate TODO loops). The EQL-009 patching loop
is added in EQL-009 and then removed in EQL-010 — since they are in the same batch, the committed
state must NOT contain the `// TODO: Remove in Phase 5.2` loop. Instead, the EQL-011 V1 fallback
takes its place.

**All work in this batch is inside `FDP/ExtDeps/FastBTree/`.** No changes to Hrot or FDP engine.

---

## Test baseline (before any changes)

Run before starting:
```
dotnet test FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj
```
Expected: 200 passing, 11 pre-existing failures (AutoDiscovery×4, GeneratorOutput×2,
DefinitionGenerator×4, BuilderValidationTests.DtoTooLarge×1).

---

## EQL-009 — NodeDefinition bit-flag layout and Interpreter bridge

### Files to modify

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/NodeDefinition.cs`

Replace the `public int PayloadIndex` field with `RawPayloadIndex`, and add the computed
properties and method. Add `using System.Runtime.CompilerServices;`.

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fbt
{
    /// <summary>
    /// Single node in the behavior tree bytecode.
    /// Size: 8 bytes (tightly packed).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NodeDefinition
    {
        /// <summary>Type of this node.</summary>
        public NodeType Type;           // 1 byte

        /// <summary>Number of immediate children.</summary>
        public byte ChildCount;         // 1 byte

        /// <summary>
        /// Distance to next sibling (in node indices).
        /// Used for skipping entire subtrees.
        /// NextSiblingIndex = CurrentIndex + SubtreeOffset
        /// </summary>
        public ushort SubtreeOffset;    // 2 bytes

        /// <summary>
        /// Raw payload storage. Bit 31 = IsResourceOwning flag. Bits 0-30 = payload index.
        /// - For Action/Condition: bits 0-30 index into MethodNames[]
        /// - For Wait: bits 0-30 index into FloatParams[] (duration)
        /// - For Decorator params: bits 0-30 index into IntParams[]
        /// - For Subtree: bits 0-30 index into SubtreeAssetIds[]
        /// </summary>
        public int RawPayloadIndex;     // 4 bytes

        // Total: 8 bytes

        /// <summary>
        /// Payload lookup index (bits 0-30 of RawPayloadIndex).
        /// Identical to the old PayloadIndex field for values that do not set bit 31.
        /// </summary>
        public readonly int PayloadIndex
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RawPayloadIndex & 0x7FFFFFFF;
        }

        /// <summary>
        /// True when bit 31 of RawPayloadIndex is set.
        /// Indicates this Action/Condition node owns standing ECS resources and has a
        /// registered deactivator delegate that must be called on branch exit.
        /// </summary>
        public readonly bool IsResourceOwning
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (RawPayloadIndex & unchecked((int)0x80000000)) != 0;
        }

        /// <summary>Sets bit 31 of RawPayloadIndex without disturbing bits 0-30.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResourceOwning() => RawPayloadIndex |= unchecked((int)0x80000000);
    }
}
```

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs` — FlattenRecursive

Change the struct initializer write from `PayloadIndex = payloadIndex` to `RawPayloadIndex = payloadIndex`.

Find:
```csharp
            nodes.Add(new NodeDefinition
            {
                Type = node.Type,
                ChildCount = (byte)node.Children.Count,
                SubtreeOffset = (ushort)subtreeSize, // This is critical!
                PayloadIndex = payloadIndex
            });
```
Change to:
```csharp
            nodes.Add(new NodeDefinition
            {
                Type = node.Type,
                ChildCount = (byte)node.Children.Count,
                SubtreeOffset = (ushort)subtreeSize, // This is critical!
                RawPayloadIndex = payloadIndex
            });
```

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/BinaryTreeSerializer.cs` — Load

In the `Load(Stream stream)` method, change the node struct initializer from `PayloadIndex` to
`RawPayloadIndex`:

```csharp
            blob.Nodes[i] = new NodeDefinition
            {
                Type = (NodeType)reader.ReadByte(),
                ChildCount = reader.ReadByte(),
                SubtreeOffset = reader.ReadUInt16(),
                RawPayloadIndex = reader.ReadInt32()
            };
```

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` — rename + update SweepExitedNode

Rename `InvokeDeactivatorIfRegistered` to `SweepExitedNode`. Replace the body to use
`IsResourceOwning` instead of the NodeType guard. Keep the bounds check. Also handles the
Parallel case (previously in `SweepExitedNodes`).

The current `SweepExitedNodes` has an if/else dispatching between Parallel and Action/Condition.
After this change, `SweepExitedNode` handles BOTH cases:

```csharp
        // Invokes the deactivator for nodeIndex if it is resource-owning, and handles
        // Parallel subtree sweeping. Replaces InvokeDeactivatorIfRegistered.
        private void SweepExitedNode(
            ushort nodeIndex,
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context)
        {
            if ((uint)nodeIndex >= (uint)_blob.Nodes.Length) return;
            ref var node = ref _blob.Nodes[nodeIndex];
            if (node.IsResourceOwning)
            {
                int pi = node.PayloadIndex;
                if ((uint)pi < (uint)_deactivatorDelegates.Length)
                {
                    var deactivator = _deactivatorDelegates[pi];
                    deactivator?.Invoke(ref blackboard, ref state, ref context, pi);
                }
            }
            if (node.Type == NodeType.Parallel)
                SweepParallelChildren(nodeIndex, ref blackboard, ref state, ref context);
        }
```

Update `SweepExitedNodes` to call `SweepExitedNode` for every old path entry (both Parallel and
non-Parallel — `SweepExitedNode` handles both):

```csharp
        private void SweepExitedNodes(
            Span<ushort> oldPath,
            Span<ushort> newPath,
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context)
        {
            for (int i = 0; i < 9; i++)
            {
                ushort old = oldPath[i];
                if (old == 0) continue;
                if (newPath.Contains(old)) continue;
                SweepExitedNode(old, ref blackboard, ref state, ref context);
            }
        }
```

Update `SweepParallelChildren` to call `SweepExitedNode` instead of `InvokeDeactivatorIfRegistered`:

Find in `SweepParallelChildren`:
```csharp
                        InvokeDeactivatorIfRegistered((ushort)j, ref blackboard, ref state, ref context);
```
Change to:
```csharp
                        SweepExitedNode((ushort)j, ref blackboard, ref state, ref context);
```

#### Existing test files — fix `PayloadIndex = X` struct initializers

`PayloadIndex` is now a read-only computed property and cannot be used as a struct initializer
target. Change ALL occurrences of `PayloadIndex = <value>` in struct initializers to
`RawPayloadIndex = <value>` in the following files:

- `tests/Fbt.Tests/Unit/TreeVisualizerTests.cs`
- `tests/Fbt.Tests/Unit/TreeValidatorTests.cs`
- `tests/Fbt.Tests/Unit/BinarySerializerTests.cs`
- `tests/Fbt.Tests/Unit/InterpreterTests.cs`
- `examples/Fbt.Examples.FluentBTree/Program.cs` (if any write to `PayloadIndex`)

**Read accesses** (`node.PayloadIndex`, `blob.Nodes[i].PayloadIndex`) do NOT need to change — the
computed property still works for reads.

**IMPORTANT:** Any struct initializer `new NodeDefinition { ..., PayloadIndex = X }` is a WRITE
to the field, which breaks after the rename. Fix every occurrence.

### New test file — `tests/Fbt.Tests/Unit/NodeDefinitionBitFlagTests.cs`

Write the following tests. Note: since EQL-009, EQL-010, and EQL-011 are all in this batch, tests
T7-T9 test the FINAL AOT state (bit set at compile time, not by Interpreter constructor):

```csharp
using System;
using System.Runtime.InteropServices;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>
    /// Tests for EQL-009: NodeDefinition bit-flag layout.
    /// T1-T6: struct API; T7-T9: AOT integration via BTreeBuilder (EQL-010 final state).
    /// </summary>
    public class NodeDefinitionBitFlagTests
    {
        // T1: RawPayloadIndex with value 5, no bit 31 set -> PayloadIndex == 5
        [Fact]
        public void T1_PayloadIndex_MasksBit31()
        {
            var d = new NodeDefinition { RawPayloadIndex = 5 };
            Assert.Equal(5, d.PayloadIndex);
        }

        // T2: RawPayloadIndex = 5 -> IsResourceOwning == false
        [Fact]
        public void T2_IsResourceOwning_FalseWhenBit31Clear()
        {
            var d = new NodeDefinition { RawPayloadIndex = 5 };
            Assert.False(d.IsResourceOwning);
        }

        // T3: After SetResourceOwning(), PayloadIndex still == 5 (bits 0-30 unchanged)
        [Fact]
        public void T3_SetResourceOwning_PreservesBits0To30()
        {
            var d = new NodeDefinition { RawPayloadIndex = 5 };
            d.SetResourceOwning();
            Assert.Equal(5, d.PayloadIndex);
        }

        // T4: After SetResourceOwning(), IsResourceOwning == true
        [Fact]
        public void T4_SetResourceOwning_SetsBit31()
        {
            var d = new NodeDefinition { RawPayloadIndex = 5 };
            d.SetResourceOwning();
            Assert.True(d.IsResourceOwning);
        }

        // T5: RawPayloadIndex with bit 31 set -> PayloadIndex masks it out
        [Fact]
        public void T5_PayloadIndex_MasksExistingBit31()
        {
            var d = new NodeDefinition { RawPayloadIndex = unchecked((int)0x80000005) };
            Assert.Equal(5, d.PayloadIndex);
        }

        // T6: sizeof(NodeDefinition) == 8
        [Fact]
        public void T6_NodeDefinition_IsSizeOf8Bytes()
        {
            Assert.Equal(8, Marshal.SizeOf<NodeDefinition>());
        }

        // T7: BTreeBuilder compiles a tree with a registered deactivator ->
        //     the action node has IsResourceOwning == true on the blob BEFORE Interpreter construction.
        [Fact]
        public void T7_AotBaking_ResourceOwningActionHasBitSet()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));

            var blob = builder.Compile("T7");
            var registry = builder.GetRegistry();

            // Register a deactivator for the action before re-compiling isn't needed;
            // BTreeBuilder passes the registry's TryGetDeactivator during Compile.
            // Re-register after compile to demonstrate the registry-driven approach:
            string actionKey = blob.MethodNames[blob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(actionKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            // Recompile so the deactivator is present when FlattenToBlob calls isResourceOwning
            var blob2 = builder.Compile("T7");
            Assert.True(blob2.Nodes[1].IsResourceOwning);
        }

        // T8: Action with no registered deactivator -> IsResourceOwning == false on blob.
        [Fact]
        public void T8_AotBaking_NoDeactivator_BitNotSet()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));

            // Do NOT register any deactivator
            var blob = builder.Compile("T8");
            Assert.False(blob.Nodes[1].IsResourceOwning);
        }

        // T9: Composite node (Sequence, index 0) -> IsResourceOwning == false.
        //     SetResourceOwning is only called for Action/Condition nodes.
        [Fact]
        public void T9_CompositeNode_IsResourceOwningAlwaysFalse()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));

            var registry = builder.GetRegistry();
            // Register deactivator so compile would set bits for action nodes
            string actionKey;
            var tmpBlob = builder.Compile("T9tmp");
            actionKey = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            registry.RegisterDeactivator(actionKey,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            var blob = builder.Compile("T9");
            // Node 0 is the Sequence composite — must NOT have IsResourceOwning set
            Assert.Equal(NodeType.Sequence, blob.Nodes[0].Type);
            Assert.False(blob.Nodes[0].IsResourceOwning);
            // Node 1 is the Action — MUST have IsResourceOwning set
            Assert.Equal(NodeType.Action, blob.Nodes[1].Type);
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }
    }
}
```

---

## EQL-010 — AOT compilation pipeline

### Files to modify

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/BuilderNode.cs`

Add `public bool IsResourceOwning { get; set; }` property to `BuilderNode`:

```csharp
        public int Policy { get; set; }
        public bool IsResourceOwning { get; set; }
        public List<BuilderNode> Children { get; } = new List<BuilderNode>();
```

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs`

**1. Update `FlattenToBlob` signature** — add optional `isResourceOwning` delegate:

```csharp
        public static BehaviorTreeBlob FlattenToBlob(
            BuilderNode root,
            string treeName,
            Func<string, bool>? isResourceOwning = null)
```

**2. Update `FlattenToBlobCore` signature** and call:

```csharp
        private static BehaviorTreeBlob FlattenToBlobCore(
            BuilderNode root,
            string treeName,
            Func<string, bool>? isResourceOwning)
        {
            var nodes = new List<NodeDefinition>();
            var methodNames = new List<string>();
            var floatParams = new List<float>();
            var intParams = new List<int>();

            FlattenRecursive(root, nodes, methodNames, floatParams, intParams, isResourceOwning);
            ...
        }
```

Update the call inside `FlattenToBlob`:
```csharp
            var blob = FlattenToBlobCore(root, treeName, isResourceOwning);
```

**3. Update `FlattenRecursive` signature** and add `IsResourceOwning` bit logic:

```csharp
        private static void FlattenRecursive(
            BuilderNode node,
            List<NodeDefinition> nodes,
            List<string> methodNames,
            List<float> floatParams,
            List<int> intParams,
            Func<string, bool>? isResourceOwning)
```

After computing `payloadIndex` and BEFORE adding the node to the list, compute whether to set
the resource-owning bit. Only set it for `Action` and `Condition` nodes:

```csharp
            var nodeDef = new NodeDefinition
            {
                Type = node.Type,
                ChildCount = (byte)node.Children.Count,
                SubtreeOffset = (ushort)subtreeSize,
                RawPayloadIndex = payloadIndex
            };
            if ((node.Type == NodeType.Action || node.Type == NodeType.Condition)
                && (node.IsResourceOwning || (isResourceOwning?.Invoke(node.MethodName) ?? false)))
            {
                nodeDef.SetResourceOwning();
            }
            nodes.Add(nodeDef);
```

Update the recursive call to pass `isResourceOwning`:
```csharp
            foreach (var child in node.Children)
            {
                FlattenRecursive(child, nodes, methodNames, floatParams, intParams, isResourceOwning);
            }
```

#### `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` — `Compile` method

Update `Compile` to pass an `isResourceOwning` delegate to `FlattenToBlob`. The delegate
queries the builder's own `_registry`:

```csharp
        public BehaviorTreeBlob Compile(string treeName)
        {
            if (_entries.Count == 0)
                throw new InvalidOperationException("The builder has no root node.");
            if (_entries.Count > 1)
                throw new InvalidOperationException(
                    "The builder has multiple root nodes. A behavior tree must have exactly one root.");

            var root = _entries[0];
            var blob = TreeCompiler.FlattenToBlob(
                root.Node,
                treeName,
                methodName => _registry.TryGetDeactivator(methodName, out _));

            // Populate DebugMetadata in depth-first order (mirrors FlattenToBlob ordering)
            var metaList = new List<NodeDebugMetadata>();
            FlattenMetadata(root, metaList);
            blob.DebugMetadata = metaList.ToArray();

            return blob;
        }
```

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs`

The EQL-009 TODO loop is NOT added to the committed code (since EQL-010 immediately removes it).
The final Interpreter constructor does NOT have a patching loop for resource-owning nodes.
(The V1 fallback from EQL-011 is added separately below.)

Verify the constructor does NOT contain any loop over `_blob.Nodes` patching `SetResourceOwning`.

### New test file — `tests/Fbt.Tests/Unit/AotCompilationPipelineTests.cs`

Write tests for EQL-010 success conditions T1-T7:

```csharp
using System;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Serialization;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>Tests for EQL-010: AOT compilation pipeline.</summary>
    public class AotCompilationPipelineTests
    {
        // T1: BTreeBuilder.Compile with deactivator registered for action A ->
        //     blob has IsResourceOwning set on the action node BEFORE Interpreter construction.
        [Fact]
        public void T1_BTreeBuilder_SetsResourceOwningBit_WhenDeactivatorRegistered()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));
            // Get a blob to find the method key, then re-register deactivator
            var tmpBlob = builder.Compile("T1tmp");
            string key = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            builder.GetRegistry().RegisterDeactivator(key,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            var blob = builder.Compile("T1");
            // Action is at node index 1 (Sequence=0, Action=1)
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }

        // T2: Action B has no deactivator -> blob.Nodes[actionBIndex].IsResourceOwning == false.
        [Fact]
        public void T2_BTreeBuilder_NoDeactivator_BitNotSet()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));
            // No RegisterDeactivator call
            var blob = builder.Compile("T2");
            Assert.False(blob.Nodes[1].IsResourceOwning);
        }

        // T3: BuilderNode.IsResourceOwning = true with no registry match ->
        //     compiled blob still has IsResourceOwning bit set.
        [Fact]
        public void T3_BuilderNodeFlag_HonoredEvenWithoutRegistryMatch()
        {
            // Build a blob directly via FlattenToBlob with a BuilderNode that has IsResourceOwning=true
            // but pass no isResourceOwning delegate (null) — the BuilderNode flag alone should set the bit.
            var action = new BuilderNode
            {
                Type = NodeType.Action,
                MethodName = "SomeAction",
                IsResourceOwning = true
            };
            var seq = new BuilderNode { Type = NodeType.Sequence };
            seq.Children.Add(action);

            var blob = TreeCompiler.FlattenToBlob(seq, "T3", null);
            // Node 0 = Sequence, Node 1 = Action
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }

        // T4: Sequence node in the same tree -> IsResourceOwning == false.
        [Fact]
        public void T4_CompositeNode_NeverHasResourceOwningBit()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));
            var tmpBlob = builder.Compile("T4tmp");
            string key = tmpBlob.MethodNames[tmpBlob.Nodes[1].PayloadIndex];
            builder.GetRegistry().RegisterDeactivator(key,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            var blob = builder.Compile("T4");
            Assert.Equal(NodeType.Sequence, blob.Nodes[0].Type);
            Assert.False(blob.Nodes[0].IsResourceOwning);
        }

        // T5: FlattenToBlob called without delegate -> no IsResourceOwning bits set.
        [Fact]
        public void T5_FlattenToBlob_NullDelegate_NoBitsSet()
        {
            var action = new BuilderNode { Type = NodeType.Action, MethodName = "SomeAction" };
            var seq = new BuilderNode { Type = NodeType.Sequence };
            seq.Children.Add(action);

            var blob = TreeCompiler.FlattenToBlob(seq, "T5");
            Assert.False(blob.Nodes[1].IsResourceOwning);
        }

        // T6 (regression - no patch loop): Hybrid lifecycle tests L1-L8 pass via BTreeBuilder.
        //     Verified by running HybridLifecycleTests externally; this test checks the
        //     Interpreter constructor does NOT contain a node-patching loop (compile-time only).
        [Fact]
        public void T6_Interpreter_HasNo_PatchingLoop_InConstructor()
        {
            // If the patch loop existed, constructing an Interpreter from a blob where bits
            // are already set (by AOT) and the registry has no deactivators would clear them.
            // Verify that a V2 blob's resource-owning bit is NOT cleared by construction.
            var action = new BuilderNode
            {
                Type = NodeType.Action,
                MethodName = "SomeAction",
                IsResourceOwning = true // explicitly set
            };
            var seq = new BuilderNode { Type = NodeType.Sequence };
            seq.Children.Add(action);

            var blob = TreeCompiler.FlattenToBlob(seq, "T6", null);
            // ManuallySet bit via FlattenToBlob with explicit BuilderNode flag
            Assert.True(blob.Nodes[1].IsResourceOwning);

            // Construct Interpreter with an EMPTY registry (no deactivators)
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("SomeAction",
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success);

            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            // Bit must still be set (constructor did not clear it via a spurious patch loop)
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }

        // T7: All three projects build without errors (verified by running dotnet build).
        //     No automated assertion; build success is the test.
    }
}
```

---

## EQL-011 — Binary serialization versioning and V1 legacy fallback

### Files to modify

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeBlob.cs`

Change the default `Version` value:

```csharp
        /// <summary>Version number for compatibility checking.</summary>
        public int Version = 2;
```

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/BinaryTreeSerializer.cs`

**1. Bump `CurrentVersion`:**
```csharp
        private const int CurrentVersion = 2;
```

**2. Update `Save` to write `RawPayloadIndex`:**
In the Save method, the node loop currently writes `writer.Write(node.PayloadIndex)`. Change to:
```csharp
                writer.Write(node.RawPayloadIndex);
```
This preserves bit 31 (IsResourceOwning) in V2 files.

**3. Update `Load` to accept V1 and V2:**
Change the version validation from `if (version != CurrentVersion)` to accept both:
```csharp
            if (version < 1 || version > 2)
                throw new InvalidDataException($"Unsupported version: {version}");
```
The node read loop already reads into `RawPayloadIndex` after EQL-009 fix. No further change
needed there — V1 blobs simply won't have bit 31 set (the stored int had no bit 31), and V2
blobs may have it set.

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs` — FlattenToBlob

After building the blob in `FlattenToBlob`, stamp `blob.Version = 2`:

```csharp
        public static BehaviorTreeBlob FlattenToBlob(
            BuilderNode root,
            string treeName,
            Func<string, bool>? isResourceOwning = null)
        {
            ...
            // 1. Flatten the node tree
            var blob = FlattenToBlobCore(root, treeName, isResourceOwning);

            // 2. Stamp version
            blob.Version = 2;

            // 3. Calculate hashes
            blob.StructureHash = CalculateStructureHash(blob.Nodes);
            blob.ParamHash = CalculateParamHash(blob.FloatParams, blob.IntParams);
            ...
        }
```

Note: `CompileFromJson` overrides `blob.Version = treeData.Version` AFTER calling `FlattenToBlob`.
Since `JsonTreeData.Version` defaults to `1`, blobs from `CompileFromJson` remain V1. This is
intentional — the V1 fallback in the Interpreter handles them.

#### `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` — Add V1 fallback

In the `Interpreter` constructor, after `_deactivatorDelegates = BindDeactivators(blob, registry);`,
add the V1 fallback patching loop:

```csharp
            // V1 blob legacy fallback: blobs produced by CompileFromJson (Version == 1)
            // do not have the IsResourceOwning bit baked in at compile time.
            // Patch in-memory to set the bit for any Action/Condition node whose method name
            // has a registered deactivator. V2 blobs (from BTreeBuilder.Compile) skip this.
            if (_blob.Version < 2)
            {
                for (int i = 0; i < _blob.Nodes.Length; i++)
                {
                    ref var node = ref _blob.Nodes[i];
                    if (node.Type is not (NodeType.Action or NodeType.Condition)) continue;
                    int pi = node.PayloadIndex;
                    if ((uint)pi >= (uint)_blob.MethodNames.Length) continue;
                    if (_deactivatorDelegates.Length > pi && _deactivatorDelegates[pi] != null)
                        node.SetResourceOwning();
                }
            }
```

The complete constructor after all Phase 5 changes:

```csharp
        public Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry)
        {
            _blob = blob ?? throw new ArgumentNullException(nameof(blob));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            _actionDelegates = BindActions(blob, registry);
            _deactivatorDelegates = BindDeactivators(blob, registry);
            _blobStructureHash = blob.StructureHash;

            // V1 blob legacy fallback: blobs produced by CompileFromJson (Version == 1)
            // do not have the IsResourceOwning bit baked in at compile time.
            // Patch in-memory to set the bit for any Action/Condition node whose method name
            // has a registered deactivator. V2 blobs (from BTreeBuilder.Compile) skip this.
            if (_blob.Version < 2)
            {
                for (int i = 0; i < _blob.Nodes.Length; i++)
                {
                    ref var node = ref _blob.Nodes[i];
                    if (node.Type is not (NodeType.Action or NodeType.Condition)) continue;
                    int pi = node.PayloadIndex;
                    if ((uint)pi >= (uint)_blob.MethodNames.Length) continue;
                    if (_deactivatorDelegates.Length > pi && _deactivatorDelegates[pi] != null)
                        node.SetResourceOwning();
                }
            }
        }
```

### New test file — `tests/Fbt.Tests/Unit/BinarySerializationVersioningTests.cs`

```csharp
using System;
using System.IO;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Serialization;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>Tests for EQL-011: Binary serialization versioning and V1 legacy fallback.</summary>
    public class BinarySerializationVersioningTests
    {
        // T1: BehaviorTreeBlob produced by FlattenToBlob has blob.Version == 2.
        [Fact]
        public void T1_FlattenToBlob_StampsVersion2()
        {
            var action = new BuilderNode { Type = NodeType.Action, MethodName = "A" };
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(action);

            var blob = TreeCompiler.FlattenToBlob(root, "T1");
            Assert.Equal(2, blob.Version);
        }

        // T2 (V2 round-trip): Compile a tree with resource-owning action via BTreeBuilder.
        //     Save and Load. Assert: (a) loaded blob.Version == 2; (b) IsResourceOwning bit preserved.
        [Fact]
        public void T2_V2RoundTrip_IsResourceOwningBitPreserved()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));
            var tmp = builder.Compile("T2tmp");
            string key = tmp.MethodNames[tmp.Nodes[1].PayloadIndex];
            builder.GetRegistry().RegisterDeactivator(key,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            var blob = builder.Compile("T2");
            Assert.True(blob.Nodes[1].IsResourceOwning);
            Assert.Equal(2, blob.Version);

            string path = System.IO.Path.GetTempFileName();
            try
            {
                BinaryTreeSerializer.Save(blob, path);
                var loaded = BinaryTreeSerializer.Load(path);

                Assert.Equal(2, loaded.Version);
                Assert.True(loaded.Nodes[1].IsResourceOwning);
            }
            finally { System.IO.File.Delete(path); }
        }

        // T3 (V1 round-trip): Manually set blob.Version = 1 (simulating an old disk file).
        //     Load via stream manually (or use a builder then force Version=1).
        //     Assert: before Interpreter construction, IsResourceOwning == false.
        //     After Interpreter construction with a registered deactivator, IsResourceOwning == true.
        [Fact]
        public void T3_V1LegacyFallback_PatchesResourceOwningBit()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Running));
            // Don't register deactivator during compile -> bit NOT set by AOT
            var blob = builder.Compile("T3");
            // Simulate a V1 blob (bit not set, version=1)
            blob.Version = 1;
            Assert.False(blob.Nodes[1].IsResourceOwning);

            // Register deactivator in the registry
            string key = blob.MethodNames[blob.Nodes[1].PayloadIndex];
            var registry = builder.GetRegistry();
            registry.RegisterDeactivator(key,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            // Construct Interpreter -> V1 fallback fires -> bit gets set
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }

        // T4 (V2 skips patch): V2 blob with IsResourceOwning bit NOT set by AOT (null delegate),
        //     but a deactivator IS registered in registry. After Interpreter construction, bit
        //     must remain FALSE because V1 fallback does not run for V2 blobs.
        [Fact]
        public void T4_V2Blob_SkipsV1Patching()
        {
            // Build a V2 blob WITHOUT AOT bits (FlattenToBlob with null delegate)
            var action = new BuilderNode { Type = NodeType.Action, MethodName = "ActionA" };
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(action);
            var blob = TreeCompiler.FlattenToBlob(root, "T4", null); // no isResourceOwning delegate
            Assert.Equal(2, blob.Version);
            Assert.False(blob.Nodes[1].IsResourceOwning); // bit NOT set by AOT

            // Register a deactivator in the registry
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("ActionA",
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success);
            registry.RegisterDeactivator("ActionA",
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            // Construct Interpreter with V2 blob -> V1 fallback must NOT run
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            // Bit remains false: V1 loop skipped
            Assert.False(blob.Nodes[1].IsResourceOwning);
        }

        // T5 (regression): All L-01 through L-08 tests in HybridLifecycleTests pass.
        //     Verified by running dotnet test filtering HybridLifecycleTests; no assertion here.

        // T6: Invalid version in binary stream -> InvalidDataException.
        [Fact]
        public void T6_InvalidVersion_ThrowsInvalidDataException()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                using (var fs = System.IO.File.OpenWrite(path))
                using (var w = new System.IO.BinaryWriter(fs))
                {
                    w.Write((byte)'F'); w.Write((byte)'B'); w.Write((byte)'T'); w.Write((byte)0); // magic
                    w.Write(99);         // invalid version
                    w.Write(0);          // StructureHash
                    w.Write(0);          // ParamHash
                    w.Write("");         // TreeName
                    w.Write(0);          // node count
                    w.Write(0);          // method count
                    w.Write(0);          // float count
                    w.Write(0);          // int count
                }
                Assert.Throws<InvalidDataException>(() => BinaryTreeSerializer.Load(path));
            }
            finally { System.IO.File.Delete(path); }
        }

        // T7: Projects build without errors (verified by dotnet build; no assertion here).
    }
}
```

---

## Build and test verification

After implementing ALL changes (EQL-009 + EQL-010 + EQL-011):

**Build:**
```
dotnet build FDP\ExtDeps\FastBTree\src\Fbt.Kernel\Fbt.Kernel.csproj
dotnet build FDP\ExtDeps\FastBTree\src\Fbt.Compiler\Fbt.Compiler.csproj
dotnet build FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj
```
All must succeed with 0 errors.

**Test:**
```
dotnet test FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj
```
Expected:
- All new tests in `NodeDefinitionBitFlagTests`, `AotCompilationPipelineTests`,
  `BinarySerializationVersioningTests` pass.
- All existing `HybridLifecycleTests` L1-L8 pass (they use `BTreeBuilder` → V2 blobs with AOT bits).
- Pre-existing failures (AutoDiscovery×4, GeneratorOutput×2, DefinitionGenerator×4,
  BuilderValidationTests.DtoTooLarge×1) may still fail — these are NOT regressions.
- No new failures beyond pre-existing baseline.

---

## What NOT to do

- Do NOT modify any files outside `FDP/ExtDeps/FastBTree/`.
- Do NOT add the `// TODO: Remove in Phase 5.2` loop to the Interpreter — skip it entirely
  since EQL-010 is implemented in the same batch.
- Do NOT change `BTreeDefinitionGenerator`, `AiBehaviorFactory`, or `BTreeVisualizerRenderer`
  — those are BATCH-05 (EQL-012) scope.
- Do NOT remove `_deactivatorDelegates` from `Interpreter` — that is EQL-012 scope.
- Do NOT update `CompileFromJson` to accept an `isResourceOwning` delegate — leave it as-is;
  the V1 fallback in Interpreter handles JSON-compiled blobs at runtime.

---

## Report format

After completion, write a report to `.dev/ai-btree-deactivator-1/reports/BATCH-04-REPORT.md`
covering:

1. Files modified and what changed in each.
2. New test files created (file name, test count, test names).
3. Test run results (dotnet test output, total/pass/fail counts).
4. Any deviations from these instructions with justification.
5. Whether the solution builds without errors.
