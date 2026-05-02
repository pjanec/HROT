# BATCH-08: Phase 4 — IEntityAwareImGuiRenderer, ComponentReflector, BehaviorDefinition, BrainBlackboardRenderer, BTreeVisualizerRenderer

**Batch Number:** BATCH-08
**Tasks:** FBT-030, FBT-031, FBT-032, FBT-033, FBT-034, FBT-035, FBT-036, FBT-037
**Phase:** Phase 4 (FDP Engine — Extended ImGui Rendering)
**Estimated Effort:** 10-12 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 through BATCH-07

---

## 📋 Onboarding — Read FIRST

1. `.dev/fluent-btree/TASK-DETAIL.md` — FBT-030 through FBT-037
2. `FDP/Engine/Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs` — existing interface
3. `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` — lines 210-225 (dispatch)
4. `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs` — `BehaviorDefinition` class
5. `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs` — `BrainBTreeState`
6. `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` — `BrainBlackboard`
7. `Hrot/Engine/Hrot.Presentation/Renderers/MissionPlanQueueRenderer.cs` — existing renderer pattern
8. `FDP/Engine/Fdp.Presentation.Tests/ImGui/ImGuiTestFixture.cs` — test helper
9. `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` — to add `Blob` property
10. `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/NodeDefinition.cs` — tree structure

---

## 🔧 Build Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# FastBTree (after adding Blob property)
dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln -v quiet 2>&1 | Select-String "error|warning|succeeded|FAILED"
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2

# Fdp.Presentation tests
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2

# Hrot.Presentation tests
dotnet test Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2

# Fdp.Toolkits tests (verify BehaviorDefinition change doesn't break them)
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2
```

### Baselines (do not regress)
- `Fbt.Tests`: 149 passing
- `Fdp.Presentation.Tests`: 249 passing
- `Hrot.Presentation.Tests`: 31 passing
- `Fdp.Toolkits.Tests`: verify count before and ensure no regression

---

## ✅ Tasks (in implementation order)

### Step 1: Add `Blob` property to `Interpreter<T>` (FBT-034 prerequisite)

**File:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs`

After the `_blobStructureHash` field, add:
```csharp
/// <summary>Exposes the compiled blob for diagnostic/visualizer tools.</summary>
public BehaviorTreeBlob Blob => _blob;
```

This is a minimal, safe change. All existing tests must still pass.

---

### Step 2: Add `IEntityAwareImGuiRenderer` (FBT-030)

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs`

Add AFTER the existing `IImGuiRenderer` interface (same file):
```csharp
/// <summary>
/// Extended ImGui renderer that receives entity and session context.
/// Implement this in addition to <see cref="IImGuiRenderer"/> when the renderer
/// needs to read sibling ECS components (e.g., BehaviorState alongside BrainBlackboard).
/// </summary>
public interface IEntityAwareImGuiRenderer : IImGuiRenderer
{
    /// <summary>
    /// Renders a custom detail view using entity and session context.
    /// Return <c>true</c> if rendering was handled; <c>false</c> to fall through
    /// to the default hierarchical tree rendering.
    /// </summary>
    bool RenderValue(IInspectableSession session, Entity entity, object value);
}
```

**Required usings already in scope** (`Fdp.Presentation.Abstractions` for `IInspectableSession`, `Fdp.Core` for `Entity`). Check the existing `using` statements at the top of the file.

---

### Step 3: Update `ComponentReflector` Dispatch (FBT-031)

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs`

Find the dispatch section (approx. line 215):
```csharp
                var renderer = ImGuiRendererRegistry.GetRenderer(type);
                bool handled = renderer != null && renderer.RenderValue(data);

                if (!handled)
                    ImGuiPropertyTree.Render(data, contextType: type, out doubleClickedPath);
```

Replace with:
```csharp
                var renderer = ImGuiRendererRegistry.GetRenderer(type);
                bool handled = false;
                if (renderer is IEntityAwareImGuiRenderer entityRenderer)
                    handled = entityRenderer.RenderValue(session, e, data);
                else if (renderer != null)
                    handled = renderer.RenderValue(data);

                if (!handled)
                    ImGuiPropertyTree.Render(data, contextType: type, out doubleClickedPath);
```

This is the ONLY change in this file. No other modifications.

---

### Step 4: Add `ParamsDtoType` to `BehaviorDefinition` (FBT-032)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs`

In `BehaviorDefinition`, add AFTER the `ParseParams` property:
```csharp
/// <summary>
/// Optional type of the params DTO struct stored at the start of
/// <see cref="BrainBlackboard.Memory"/> for this behavior.
/// When non-null, enables typed rendering in <c>BrainBlackboardRenderer</c>.
/// The type must be unmanaged (enforced by convention, not the compiler).
/// </summary>
public Type? ParamsDtoType { get; init; }
```

No other changes to this file. All existing behavior registrations continue to work with `ParamsDtoType = null` (the default).

---

### Step 5: `BrainBlackboardRenderer` (FBT-033)

**New file:** `Hrot/Engine/Hrot.Presentation/Renderers/BrainBlackboardRenderer.cs`

```csharp
using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// Entity-aware ImGui renderer for <see cref="BrainBlackboard"/>.
/// When the active behavior has a <see cref="BehaviorDefinition.ParamsDtoType"/>,
/// interprets <see cref="BrainBlackboard.Memory"/> as that typed struct and renders
/// it via <see cref="ImGuiPropertyTree.Render"/>. Falls back to raw hex display otherwise.
/// </summary>
[ImGuiRenderer(typeof(BrainBlackboard))]
public sealed class BrainBlackboardRenderer : IEntityAwareImGuiRenderer
{
    /// <summary>
    /// Set once at startup (e.g., in CgfSubsystem initialization).
    /// Required for behavior lookup.
    /// </summary>
    public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

    // ---- IImGuiRenderer ----

    public string? GetSummary(object value) => "Blackboard Memory";

    /// <summary>
    /// Non-entity-aware fallback. Cannot look up behavior without an entity.
    /// Always falls through to default rendering.
    /// </summary>
    public bool RenderValue(object value) => false;

    // ---- IEntityAwareImGuiRenderer ----

    public bool RenderValue(IInspectableSession session, Entity entity, object value)
    {
        if (value is not BrainBlackboard bb) return false;

        var registry = BehaviorRegistryAccessor;
        if (registry == null) return false;

        if (!session.HasComponent(entity, typeof(BehaviorState))) return false;

        var behaviorStateObj = session.GetComponent(entity, typeof(BehaviorState));
        if (behaviorStateObj is not BehaviorState ds) return false;

        if (!registry.TryGetDefinition(ds.ActiveBehaviorHash, out var def)) return false;

        if (def.ParamsDtoType != null)
        {
            RenderTypedDto(bb, def.ParamsDtoType);
        }
        else
        {
            RenderRawBytes(bb);
        }

        return true;
    }

    // ---- Helpers ----

    private static unsafe void RenderTypedDto(BrainBlackboard bb, Type dtoType)
    {
        int size = Marshal.SizeOf(dtoType);
        object boxed;
        fixed (byte* ptr = bb.Memory)
        {
            boxed = Marshal.PtrToStructure((IntPtr)ptr, dtoType)!;
        }
        ImGuiPropertyTree.Render(boxed, contextType: dtoType);
    }

    private static unsafe void RenderRawBytes(BrainBlackboard bb)
    {
        const int BytesPerRow = 16;
        fixed (byte* ptr = bb.Memory)
        {
            int total = BehaviorConstants.BrainBlackboardByteSize;
            for (int row = 0; row < total; row += BytesPerRow)
            {
                int count = Math.Min(BytesPerRow, total - row);
                var span = new ReadOnlySpan<byte>(ptr + row, count);
                ImGui.Text($"[{row:X3}] {FormatHex(span)}");
            }
        }
    }

    private static string FormatHex(ReadOnlySpan<byte> bytes)
    {
        // Simple hex formatting without allocation beyond the string
        var chars = new char[bytes.Length * 3 - 1];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) chars[i * 3 - 1] = ' ';
            bytes[i].TryFormat(chars.AsSpan(i > 0 ? i * 3 : 0, 2), out _, "X2");
        }
        return new string(chars);
    }
}
```

**Note:** `BehaviorConstants.BrainBlackboardByteSize` is `128`. `BrainBlackboard.Memory` is `fixed byte Memory[128]`. The unsafe block uses the fixed pointer.

---

### Step 6: `BTreeVisualizerRenderer` (FBT-034)

**New file:** `Hrot/Engine/Hrot.Presentation/Renderers/BTreeVisualizerRenderer.cs`

**Tree structure recap:**
- `blob.Nodes` is a flat depth-first array
- Each `NodeDefinition` has `SubtreeOffset` = total nodes in subtree (including self)
- To iterate children of node at index `i`:
  - First child is at `i + 1`
  - Next sibling of child at `j` is at `j + blob.Nodes[j].SubtreeOffset`
  - Iterate `ChildCount` times using this pattern

**Color coding:**
- `RunningNodeIndex == nodeIndex` → green (active)  
- `RunningNodeIndex == 0` → no highlight (tree is idle)
- Inactive → default text color

```csharp
using System;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fbt;
using Fbt.Runtime;
using ImGuiNET;
using System.Numerics;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// Entity-aware ImGui renderer for <see cref="BrainBTreeState"/>.
/// Renders a color-coded interactive tree showing the active execution path.
/// </summary>
[ImGuiRenderer(typeof(BrainBTreeState))]
public sealed class BTreeVisualizerRenderer : IEntityAwareImGuiRenderer
{
    private static readonly Vector4 ColorGreen  = new Vector4(0.2f, 0.9f, 0.2f, 1.0f);
    private static readonly Vector4 ColorGray   = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);

    /// <summary>Set at startup; required for blob lookup.</summary>
    public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

    // ---- IImGuiRenderer ----

    public string? GetSummary(object value)
    {
        var s = (BrainBTreeState)value;
        return $"RunningNode: {s.State.RunningNodeIndex}, v{s.State.TreeVersion}";
    }

    public bool RenderValue(object value) => false;

    // ---- IEntityAwareImGuiRenderer ----

    public bool RenderValue(IInspectableSession session, Entity entity, object value)
    {
        if (value is not BrainBTreeState btState) return false;

        var registry = BehaviorRegistryAccessor;
        if (registry == null) return false;

        if (!session.HasComponent(entity, typeof(BehaviorState))) return false;
        var dsObj = session.GetComponent(entity, typeof(BehaviorState));
        if (dsObj is not BehaviorState ds) return false;

        if (!registry.TryGetDefinition(ds.ActiveBehaviorHash, out var def)) return false;

        var interpreter = def.BTreeInterpreter;
        if (interpreter == null) return false;

        var blob = interpreter.Blob;
        if (blob == null || blob.Nodes.Length == 0) return false;

        DrawNode(blob, btState.State, 0);
        return true;
    }

    // ---- Tree drawing ----

    private static void DrawNode(BehaviorTreeBlob blob, BehaviorTreeState state, int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= blob.Nodes.Length) return;

        var node = blob.Nodes[nodeIndex];
        bool isRunning = state.RunningNodeIndex > 0 && state.RunningNodeIndex == nodeIndex;
        bool isIdle    = state.RunningNodeIndex == 0;

        string label = GetNodeLabel(blob, nodeIndex);

        // Color coding
        bool pushed = false;
        if (isRunning)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ColorGreen);
            pushed = true;
        }
        else if (!isIdle && node.ChildCount == 0)
        {
            // Inactive leaf while tree is running — dim it
            ImGui.PushStyleColor(ImGuiCol.Text, ColorGray);
            pushed = true;
        }

        bool hasChildren = node.ChildCount > 0;
        ImGuiTreeNodeFlags flags = hasChildren
            ? ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.OpenOnArrow
            : ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnLeaf;

        bool open = ImGui.TreeNodeEx($"##n{nodeIndex}", flags, label);

        if (pushed) ImGui.PopStyleColor();

        // Tooltip with debug metadata
        if (ImGui.IsItemHovered() && blob.DebugMetadata != null && nodeIndex < blob.DebugMetadata.Length)
        {
            var meta = blob.DebugMetadata[nodeIndex];
            if (meta != null)
            {
                ImGui.SetTooltip(
                    $"{meta.SourceFile}:{meta.LineNumber}" +
                    (string.IsNullOrEmpty(meta.CustomComment) ? "" : $"\n{meta.CustomComment}") +
                    (string.IsNullOrEmpty(meta.VisualId) ? "" : $"\nVisualId: {meta.VisualId}"));
            }
        }

        if (open && hasChildren)
        {
            int childIndex = nodeIndex + 1;
            for (int i = 0; i < node.ChildCount; i++)
            {
                if (childIndex >= blob.Nodes.Length) break;
                DrawNode(blob, state, childIndex);
                childIndex += blob.Nodes[childIndex].SubtreeOffset;
            }
            ImGui.TreePop();
        }
    }

    private static string GetNodeLabel(BehaviorTreeBlob blob, int nodeIndex)
    {
        var node = blob.Nodes[nodeIndex];
        return node.Type switch
        {
            NodeType.Sequence  => "Sequence",
            NodeType.Selector  => "Selector",
            NodeType.Parallel  => "Parallel",
            NodeType.Inverter  => "Inverter",
            NodeType.Wait      => blob.FloatParams.Length > node.PayloadIndex
                                    ? $"Wait({blob.FloatParams[node.PayloadIndex]:F1}s)"
                                    : "Wait",
            NodeType.Repeater  => blob.IntParams.Length > node.PayloadIndex
                                    ? $"Repeater({blob.IntParams[node.PayloadIndex]}x)"
                                    : "Repeater",
            NodeType.Cooldown  => blob.FloatParams.Length > node.PayloadIndex
                                    ? $"Cooldown({blob.FloatParams[node.PayloadIndex]:F1}s)"
                                    : "Cooldown",
            NodeType.Action    => blob.MethodNames.Length > node.PayloadIndex
                                    ? $"[A] {blob.MethodNames[node.PayloadIndex]}"
                                    : "[A]",
            NodeType.Condition => blob.MethodNames.Length > node.PayloadIndex
                                    ? $"[C] {blob.MethodNames[node.PayloadIndex]}"
                                    : "[C]",
            _                  => node.Type.ToString(),
        };
        // Note: debug metadata labels (if non-empty) could override the type label here;
        // kept simple for now (type-based labels are always accurate).
    }

    /// <summary>
    /// Testable helper: returns the color to use for a node index given current state.
    /// Returns 0 = default, 1 = green (running), 2 = gray (inactive leaf).
    /// </summary>
    internal static int GetNodeColorCode(int nodeIndex, int runningNodeIndex, bool hasChildren)
    {
        if (runningNodeIndex > 0 && runningNodeIndex == nodeIndex) return 1; // green
        if (runningNodeIndex != 0 && !hasChildren) return 2;                 // gray
        return 0;                                                             // default
    }
}
```

**Important:** The `BTreeVisualizerRenderer` uses `interpreter.Blob` — this requires the `Blob` property added in Step 1.

---

### Step 7: Tests (FBT-035, FBT-036, FBT-037)

#### FBT-035: Tests in `Fdp.Presentation.Tests`

**New file:** `FDP/Engine/Fdp.Presentation.Tests/ImGui/EntityAwareRendererTests.cs`

```csharp
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Xunit;

namespace Fdp.Presentation.Tests;

[Collection("ImGui Sequential")]
public class EntityAwareRendererTests
{
    // SC1: Interface hierarchy
    [Fact]
    public void IEntityAwareImGuiRenderer_IsAssignableFrom_IImGuiRenderer()
    {
        Assert.True(typeof(IImGuiRenderer).IsAssignableFrom(typeof(IEntityAwareImGuiRenderer)));
    }

    // Verify ComponentReflector dispatches to entity-aware path for IEntityAwareImGuiRenderer
    [Fact]
    public void ComponentReflector_DispatchesTo_EntityAwareRenderer_WhenRegistered()
    {
        // Arrange: create a mock IEntityAwareImGuiRenderer
        var mock = new MockEntityAwareRenderer();
        ImGuiRendererRegistry.Register(typeof(SampleComponent), mock);

        // Act: check via interface that the registry returns the right type
        var renderer = ImGuiRendererRegistry.GetRenderer(typeof(SampleComponent));
        
        // Assert: the renderer implements IEntityAwareImGuiRenderer
        Assert.NotNull(renderer);
        Assert.IsAssignableFrom<IEntityAwareImGuiRenderer>(renderer);
    }

    // Helper types
    [ComponentId(299)]
    private struct SampleComponent { public int X; }

    private sealed class MockEntityAwareRenderer : IEntityAwareImGuiRenderer
    {
        public bool WasCalled { get; private set; }
        public string? GetSummary(object value) => null;
        public bool RenderValue(object value) => false;
        public bool RenderValue(IInspectableSession session, Entity entity, object value)
        {
            WasCalled = true;
            return true;
        }
    }
}
```

---

#### FBT-036/037: Tests in `Hrot.Presentation.Tests`

**New file:** `Hrot/Engine/Hrot.Presentation.Tests/Behavior/BrainBlackboardRendererTests.cs`

First, create the ImGui test fixture in this project:
**New file:** `Hrot/Engine/Hrot.Presentation.Tests/ImGuiTestFixture.cs`

Copy from `FDP/Engine/Fdp.Presentation.Tests/ImGui/ImGuiTestFixture.cs` (same content, namespace changed to `Hrot.Presentation.Tests`).

The test project needs a reference to `ImGuiNET`. Check `Hrot.Presentation.csproj` to see if it already brings in ImGui transitively. If not, add a package reference for `ImGui.NET` to `Hrot.Presentation.Tests.csproj`.

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Presentation.Renderers;
using Xunit;

namespace Hrot.Presentation.Tests;

[Collection("ImGui Sequential")]
public class BrainBlackboardRendererTests
{
    private static readonly BrainBlackboardRenderer _renderer = new BrainBlackboardRenderer();

    // SC3: Renderer returns false when entity has no BehaviorState
    [Fact]
    public void RenderValue_ReturnsFalse_WhenNoBehaviorState()
    {
        var session = new MockSession(hasBehaviorState: false);
        var entity = new Entity(1, 1);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, entity, bb);

        Assert.False(result);
    }

    // Renderer returns false when BehaviorRegistry is null
    [Fact]
    public void RenderValue_ReturnsFalse_WhenRegistryNull()
    {
        BrainBlackboardRenderer.BehaviorRegistryAccessor = null;
        var session = new MockSession(hasBehaviorState: true, behaviorHash: 42);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, new Entity(1, 1), bb);

        Assert.False(result);
    }

    // GetSummary returns non-null
    [Fact]
    public void GetSummary_ReturnsNonNull()
    {
        var bb = new BrainBlackboard();
        var result = _renderer.GetSummary(bb);
        Assert.NotNull(result);
    }

    // Non-entity-aware path always returns false
    [Fact]
    public void RenderValue_Object_ReturnsFalse()
    {
        bool result = _renderer.RenderValue(new BrainBlackboard());
        Assert.False(result);
    }

    // With registry but unknown behavior hash → false
    [Fact]
    public void RenderValue_ReturnsFalse_WhenBehaviorNotRegistered()
    {
        var registry = new BehaviorRegistry();
        BrainBlackboardRenderer.BehaviorRegistryAccessor = registry;
        var session = new MockSession(hasBehaviorState: true, behaviorHash: 999);
        var bb = new BrainBlackboard();

        bool result = _renderer.RenderValue(session, new Entity(1, 1), bb);

        Assert.False(result);
    }

    // Helpers
    private sealed class MockSession : IInspectableSession
    {
        private readonly bool _hasBehaviorState;
        private readonly int _behaviorHash;
        public MockSession(bool hasBehaviorState, int behaviorHash = 0)
        {
            _hasBehaviorState = hasBehaviorState;
            _behaviorHash     = behaviorHash;
        }
        public bool IsReadOnly => true;
        public int EntityCount => 1;
        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
        public bool HasComponent(Entity e, Type t) => t == typeof(BehaviorState) && _hasBehaviorState;
        public object? GetComponent(Entity e, Type t)
            => t == typeof(BehaviorState) && _hasBehaviorState
                ? (object)new BehaviorState { ActiveBehaviorHash = _behaviorHash }
                : null;
        public void SetComponent(Entity e, Type t, object v) { }
        public bool HasAuthority(Entity e, Type t) => false;
    }
}
```

**New file:** `Hrot/Engine/Hrot.Presentation.Tests/Behavior/BTreeVisualizerRendererTests.cs`

```csharp
using Hrot.Presentation.Renderers;
using Xunit;

namespace Hrot.Presentation.Tests;

public class BTreeVisualizerRendererTests
{
    // SC2 (logic): Active node at RunningNodeIndex gets green color code
    [Fact]
    public void GetNodeColorCode_ReturnsGreen_ForRunningNode()
    {
        int colorCode = BTreeVisualizerRenderer.GetNodeColorCode(
            nodeIndex: 2, runningNodeIndex: 2, hasChildren: false);
        Assert.Equal(1, colorCode); // 1 = green
    }

    // Non-running node gets default color
    [Fact]
    public void GetNodeColorCode_ReturnsDefault_WhenTreeIdle()
    {
        int colorCode = BTreeVisualizerRenderer.GetNodeColorCode(
            nodeIndex: 2, runningNodeIndex: 0, hasChildren: false);
        Assert.Equal(0, colorCode); // 0 = default
    }

    // Inactive leaf while tree is running gets gray
    [Fact]
    public void GetNodeColorCode_ReturnsGray_ForInactiveLeafWhenTreeRunning()
    {
        int colorCode = BTreeVisualizerRenderer.GetNodeColorCode(
            nodeIndex: 3, runningNodeIndex: 2, hasChildren: false);
        Assert.Equal(2, colorCode); // 2 = gray
    }

    // GetSummary returns structured string
    [Fact]
    public void GetSummary_ReturnsNonNull()
    {
        var renderer = new BTreeVisualizerRenderer();
        var state = new Fdp.Toolkit.Behavior.Components.BrainBTreeState();
        Assert.NotNull(renderer.GetSummary(state));
    }

    // Non-entity-aware RenderValue always returns false
    [Fact]
    public void RenderValue_Object_ReturnsFalse()
    {
        var renderer = new BTreeVisualizerRenderer();
        Assert.False(renderer.RenderValue(new Fdp.Toolkit.Behavior.Components.BrainBTreeState()));
    }
}
```

---

## ⚠️ Quality Standards

- FBT-032: Adding `ParamsDtoType = null` as default must not break any existing `new BehaviorDefinition { ... }` initializers. It is `init`-only so it's optional.
- FBT-031: The dispatch change must NOT break any `IImGuiRenderer` implementations that do NOT implement `IEntityAwareImGuiRenderer` — they still use the `else if` path.
- `BrainBlackboardRenderer`: The `fixed (byte* ptr = bb.Memory)` block requires `AllowUnsafeBlocks = true` in `Hrot.Presentation.csproj` — verify this is set.
- `BTreeVisualizerRenderer`: `interpreter.Blob` requires the `Blob` property to be added in Step 1.
- `GetSummary` MUST NOT call ImGui (it's called outside of render frames).
- `ImGuiTestFixture` in `Hrot.Presentation.Tests`: Copy it from `Fdp.Presentation.Tests/ImGui/ImGuiTestFixture.cs`. The namespace in the copy should be `Hrot.Presentation.Tests` (or any valid namespace for that project). Add `ImGui.NET` package reference if not already transitive.

---

## 🎯 Success Criteria

- [ ] `IEntityAwareImGuiRenderer` interface exists and inherits `IImGuiRenderer`
- [ ] `ComponentReflector` uses entity-aware path when renderer implements the new interface
- [ ] `BehaviorDefinition.ParamsDtoType` property added with `null` default
- [ ] `Interpreter<T>.Blob` property added
- [ ] `BrainBlackboardRenderer` compiles + registered via `[ImGuiRenderer]` attribute
- [ ] `BTreeVisualizerRenderer` compiles + `GetNodeColorCode` internal helper exists
- [ ] All 149 `Fbt.Tests` pass
- [ ] All 249 `Fdp.Presentation.Tests` pass
- [ ] `Hrot.Presentation.Tests` at 31 + at least 10 new tests passing
- [ ] `Fdp.Toolkits.Tests` unchanged (no regression)
- [ ] Zero build errors or warnings (TreatWarningsAsErrors)

---

## ⚠️ Common Pitfalls

- `AllowUnsafeBlocks` must be `true` in `Hrot.Presentation.csproj` to use `fixed (byte*)` — check before adding `BrainBlackboardRenderer`
- `ImGui.TreeNodeEx` with `ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnLeaf` does NOT push a tree scope, so do NOT call `TreePop()` for leaf nodes
- `ImGui.TreeNodeEx` with `ImGuiTreeNodeFlags.DefaultOpen` DOES push a scope (regardless of return value) — wait, actually TreeNodeEx only pushes when it returns true. Use the return value correctly: only call `TreePop()` when `open == true` AND `hasChildren == true`.
- `BrainBlackboard.Memory` is a `fixed byte[]` — accessing it requires an `unsafe` block or pinning
- The `HotReload/ImGuiTestFixture.cs` file to copy: located at `FDP/Engine/Fdp.Presentation.Tests/ImGui/ImGuiTestFixture.cs`
- For `Hrot.Presentation.Tests`, check if `ImGuiNET` is already available via transitive reference from `Hrot.Presentation` — if so, no additional package reference is needed
- `BTreeVisualizerRenderer.GetNodeColorCode` is `internal` but it's in `Hrot.Presentation`. The test is in `Hrot.Presentation.Tests`. Check that `InternalsVisibleTo` in `Hrot.Presentation.csproj` includes `Hrot.Presentation.Tests`.

---

## 📊 Report Requirements

`.dev/fluent-btree/reports/BATCH-08-REPORT.md`:

```markdown
# BATCH-08 Report

## Summary

## Tasks Completed
- [ ] FBT-030: IEntityAwareImGuiRenderer interface
- [ ] FBT-031: ComponentReflector dispatch update
- [ ] FBT-032: BehaviorDefinition.ParamsDtoType
- [ ] FBT-033: BrainBlackboardRenderer
- [ ] FBT-034: BTreeVisualizerRenderer
- [ ] FBT-035: Tests (Fdp.Presentation.Tests)
- [ ] FBT-036/037: Tests (Hrot.Presentation.Tests)

## Test Results
- Fbt.Tests: XX / 149
- Fdp.Presentation.Tests: XX / 249+
- Hrot.Presentation.Tests: XX / 31+

## Developer Insights
Q1: Issues with unsafe BrainBlackboard.Memory access?
Q2: How did you handle TreeNodeEx push/pop for leaf vs. composite nodes?
Q3: Any rendering issues with the IEntityAwareImGuiRenderer dispatch?

## Suggested Commit Message
```

---

## 📚 Reference Files

| File | Purpose |
|------|---------|
| `FDP/Engine/Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs` | Add `IEntityAwareImGuiRenderer` |
| `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` | Update dispatch |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs` | Add `ParamsDtoType` |
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` | Add `Blob` property |
| `Hrot/Engine/Hrot.Presentation/Renderers/MissionPlanQueueRenderer.cs` | Pattern reference |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ImGuiTestFixture.cs` | Copy to Hrot.Presentation.Tests |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/ComponentReflectorTests.cs` | Test pattern |
