# BATCH-08 — ContainerNodes: Model Foundation and Transform Helpers

## Tasks Covered
- **TASK-NEC-01** — `IContainerNodeModel` + `RegionDescriptor` + `ContainerPadding`
  + `INodeModelExtensions` + `INodeModel.ParentContainerId`
- **TASK-NEC-02** — `GraphView` transform helpers (`NodeCanvasPosition`, `NodeLocalPosition`,
  `GetParentContainer`)

## Prerequisites
BATCH-07 is committed (b2d6082e). All Phase 2 tasks are complete.

## Context

The ContainerNodes extension allows a node to act as a hierarchical container for other nodes.
Key design decisions (from `NodeEditor_Extension_ContainerNodes.md`):
- A container IS a node (reuses `NodeId`, inherits `INodeModel`).
- Children store positions RELATIVE to the container's interior origin.
- Root-level (non-container-children) nodes keep canvas-absolute positions. Unchanged.
- `INodeModel.ParentContainerId` is a NEW default interface member (returns `null`) -- backwards compatible.
- `IContainerNodeModel : INodeModel` -- implementing classes opt in; non-container nodes only implement `INodeModel`.

---

## Step 1 — TASK-NEC-01 (part 1): Add ParentContainerId to INodeModel

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/INodeModel.cs`

Add the `ParentContainerId` default interface member as the last member of `INodeModel`.

Current file ending:
```csharp
    /// <summary>The node's pins in declaration order.</summary>
    IReadOnlyList<IPinModel> Pins { get; }
}
```

Replace with:
```csharp
    /// <summary>The node's pins in declaration order.</summary>
    IReadOnlyList<IPinModel> Pins { get; }

    /// <summary>
    /// Parent container id, or null if this node is at the root level.
    /// Nodes inside a container store Position in the container's interior coordinate space.
    /// Nodes at root level store Position in canvas-absolute coordinates.
    /// Default: null (root level). Override only when the node is a container child.
    /// </summary>
    NodeId? ParentContainerId => null;
}
```

---

## Step 2 — TASK-NEC-01 (part 2): Create IContainerNodeModel.cs

**File (NEW):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IContainerNodeModel.cs`

Create with the following content exactly:

```csharp
using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Extended node model interface for container nodes.
/// A container visually and logically encloses child nodes. Its bounds
/// auto-resize to fit its children. Child positions are expressed in the
/// container's interior coordinate space, not in canvas space.
/// </summary>
public interface IContainerNodeModel : INodeModel
{
    /// <summary>
    /// True when this node acts as a container. Implementing classes that set
    /// IsContainer = false behave as regular nodes regardless of the interface.
    /// </summary>
    bool IsContainer { get; }

    /// <summary>
    /// Ordered list of child node IDs. Order determines sibling z-order (later
    /// indices render on top) and serialization order determinism.
    /// </summary>
    IReadOnlyList<NodeId> ChildNodeIds { get; }

    /// <summary>
    /// Region descriptors for parallel-region containers.
    /// Empty list for simple (non-region) containers.
    /// </summary>
    IReadOnlyList<RegionDescriptor> Regions { get; }

    /// <summary>Returns the zero-based region index for the given child, or -1 if not applicable.</summary>
    int GetRegionIndexForChild(NodeId childId);

    /// <summary>Interior padding from the container edge to the child layout area.</summary>
    ContainerPadding Padding { get; }

    /// <summary>
    /// Minimum interior size in graph units. Container auto-resize never
    /// shrinks the interior below this value.
    /// </summary>
    Vector2 MinimumInteriorSize { get; }

    /// <summary>
    /// When true, children are hidden and the container renders at header height only.
    /// Children remain in the model; they are only hidden visually.
    /// </summary>
    bool IsCollapsed { get; }
}

/// <summary>Describes one region within a parallel-region container.</summary>
public sealed record RegionDescriptor(
    int Index,
    string Name,
    int Priority,
    System.Numerics.Vector4? CustomColor);

/// <summary>Interior padding (all values in graph units at zoom 1.0).</summary>
public sealed record ContainerPadding(
    float Top,
    float Right,
    float Bottom,
    float Left)
{
    /// <summary>Default padding: 8 px top, 12 px on each other side.</summary>
    public static ContainerPadding Default { get; } = new(8f, 12f, 12f, 12f);
}

/// <summary>Extension methods on INodeModel for container-related queries.</summary>
public static class INodeModelExtensions
{
    /// <summary>Returns true if this node is an active container (IsContainer = true).</summary>
    public static bool IsContainerNode(this INodeModel node) =>
        node is IContainerNodeModel { IsContainer: true };

    /// <summary>
    /// Returns the node cast to IContainerNodeModel if it is an active container,
    /// or null if it is a regular node or a non-active container.
    /// </summary>
    public static IContainerNodeModel? AsContainer(this INodeModel node) =>
        node is IContainerNodeModel c && c.IsContainer ? c : null;
}
```

---

## Step 3 — TASK-NEC-01 (part 3): Update FakeNodeModel for demo use

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeNodeModel.cs`

Add `ParentContainerId` as a settable property so demo container scenarios can assign parent IDs.

Current property block:
```csharp
    public NodeId        Id           { get; }
    public NodeKindKey   Kind         { get; }
    public string        Title        { get; set; }
    public string?       Subtitle     { get; set; }
    public NodeCategory  Category     { get; set; } = NodeCategory.Function;
    public Vector2       Position     { get; set; }
    public Vector2?      SizeOverride { get; set; }
    public NodeState     State        { get; set; } = NodeState.Normal;
    public string?       StatusTooltip { get; set; }
    public bool          IsCollapsed  { get; set; }
    public bool          ShowAdvancedPins { get; set; }
    public IReadOnlyList<IPinModel> Pins => _pins;
```

Replace with:
```csharp
    public NodeId        Id               { get; }
    public NodeKindKey   Kind             { get; }
    public string        Title            { get; set; }
    public string?       Subtitle         { get; set; }
    public NodeCategory  Category         { get; set; } = NodeCategory.Function;
    public Vector2       Position         { get; set; }
    public Vector2?      SizeOverride     { get; set; }
    public NodeState     State            { get; set; } = NodeState.Normal;
    public string?       StatusTooltip    { get; set; }
    public bool          IsCollapsed      { get; set; }
    public bool          ShowAdvancedPins { get; set; }
    public NodeId?       ParentContainerId { get; set; }
    public IReadOnlyList<IPinModel> Pins => _pins;
```

---

## Step 4 — TASK-NEC-02: Add transform helpers to GraphView

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/GraphView.cs`

Read the full file first (especially the constructor and existing public members) to understand where to add.

Add the following three public methods to the `GraphView` class. Place them after the existing constructor (or after the last existing method -- wherever it reads cleanly). The methods must come INSIDE the `GraphView` class body.

```csharp
    // ── Container transform helpers (TASK-NEC-02) ─────────────────────────────

    /// <summary>
    /// Returns the position of the node's top-left corner in canvas-absolute coordinates.
    /// For root-level nodes (ParentContainerId == null) this equals INodeModel.Position.
    /// For children of containers, walks the ancestor chain accumulating offsets.
    /// Returns Vector2.Zero if the node is not found.
    /// </summary>
    public Vector2 NodeCanvasPosition(NodeId id)
    {
        var node = Model.FindNode(id);
        if (node == null) return Vector2.Zero;

        if (node.ParentContainerId == null)
            return node.Position;

        var parent = Model.FindNode(node.ParentContainerId.Value);
        if (parent?.AsContainer() is not { } container)
            return node.Position; // parent is not an active container; treat as root

        var parentCanvas = NodeCanvasPosition(parent.Id);
        var interiorOrigin = parentCanvas + new System.Numerics.Vector2(
            container.Padding.Left,
            Host.Theme.NodeHeaderHeight + container.Padding.Top);
        return interiorOrigin + node.Position;
    }

    /// <summary>
    /// Returns the node's local position (INodeModel.Position).
    /// For root nodes this is canvas-absolute; for container children it is parent-local.
    /// Returns Vector2.Zero if the node is not found.
    /// </summary>
    public System.Numerics.Vector2 NodeLocalPosition(NodeId id) =>
        Model.FindNode(id)?.Position ?? System.Numerics.Vector2.Zero;

    /// <summary>
    /// Returns the parent container ID for the node, or null if the node is at root level.
    /// Returns null if the node is not found.
    /// </summary>
    public NodeId? GetParentContainer(NodeId id) =>
        Model.FindNode(id)?.ParentContainerId;
```

Note: `using System.Numerics;` may already be in the file via the using block at the top. If it is, you can use `Vector2` directly. If not, add the fully-qualified form `System.Numerics.Vector2`. Check the existing using directives first.

Also: `AsContainer()` is an extension method defined in `INodeModelExtensions` (the new file). Since it is in the `NodeEditor.Core.Interfaces` namespace and `GraphView.cs` likely already imports `NodeEditor.Core.Interfaces`, the method should be available without additional using directives. Verify and add `using NodeEditor.Core.Interfaces;` if needed.

---

## Step 5 — Tests for NEC-01

**File (NEW):** `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/Interfaces/ContainerNodeModelTests.cs`

Create the directory `NodeEditor.Core.Tests/Interfaces/` if it does not already exist (just create the file; dotnet will handle the directory).

```csharp
using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.Interfaces;

/// <summary>Tests for IContainerNodeModel, ContainerPadding, RegionDescriptor, and INodeModelExtensions.</summary>
public sealed class ContainerNodeModelTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubNode : INodeModel
    {
        public NodeId Id { get; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("stub");
        public string Title => "Stub";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position => Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();
    }

    private sealed class StubContainer : IContainerNodeModel
    {
        public NodeId Id { get; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("container");
        public string Title => "Container";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position => Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();

        public bool IsContainer => true;
        public IReadOnlyList<NodeId> ChildNodeIds => System.Array.Empty<NodeId>();
        public IReadOnlyList<RegionDescriptor> Regions => System.Array.Empty<RegionDescriptor>();
        public int GetRegionIndexForChild(NodeId childId) => -1;
        public ContainerPadding Padding => ContainerPadding.Default;
        public Vector2 MinimumInteriorSize => new(200f, 100f);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RegularNode_IsContainerNode_ReturnsFalse()
    {
        INodeModel node = new StubNode();
        node.IsContainerNode().Should().BeFalse();
    }

    [Fact]
    public void RegularNode_AsContainer_ReturnsNull()
    {
        INodeModel node = new StubNode();
        node.AsContainer().Should().BeNull();
    }

    [Fact]
    public void ContainerNode_IsContainerNode_ReturnsTrue()
    {
        INodeModel node = new StubContainer();
        node.IsContainerNode().Should().BeTrue();
    }

    [Fact]
    public void ContainerNode_AsContainer_ReturnsSelf()
    {
        var container = new StubContainer();
        INodeModel node = container;
        node.AsContainer().Should().BeSameAs(container);
    }

    [Fact]
    public void RegularNode_ParentContainerId_DefaultIsNull()
    {
        INodeModel node = new StubNode();
        node.ParentContainerId.Should().BeNull();
    }

    [Fact]
    public void ContainerPadding_Default_HasExpectedValues()
    {
        var pad = ContainerPadding.Default;
        pad.Top.Should().Be(8f);
        pad.Left.Should().Be(12f);
        pad.Right.Should().Be(12f);
        pad.Bottom.Should().Be(12f);
    }

    [Fact]
    public void RegionDescriptor_StoresFields()
    {
        var rd = new RegionDescriptor(1, "Combat", 2, null);
        rd.Index.Should().Be(1);
        rd.Name.Should().Be("Combat");
        rd.Priority.Should().Be(2);
        rd.CustomColor.Should().BeNull();
    }
}
```

---

## Step 6 — Tests for NEC-02

**File (NEW):** `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/View/ContainerTransformTests.cs`

```csharp
using FluentAssertions;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.View;

/// <summary>Tests for GraphView.NodeCanvasPosition and GetParentContainer.</summary>
public sealed class ContainerTransformTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubNode : INodeModel
    {
        public NodeId Id { get; set; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("stub");
        public string Title => "Stub";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position { get; set; } = Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public NodeId? ParentContainerId { get; set; }
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();
    }

    private sealed class StubContainer : IContainerNodeModel
    {
        public NodeId Id { get; set; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("container");
        public string Title => "Container";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position { get; set; } = Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public NodeId? ParentContainerId { get; set; }
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();

        public bool IsContainer => true;
        public IReadOnlyList<NodeId> ChildNodeIds { get; set; } = System.Array.Empty<NodeId>();
        public IReadOnlyList<RegionDescriptor> Regions => System.Array.Empty<RegionDescriptor>();
        public int GetRegionIndexForChild(NodeId childId) => -1;
        public ContainerPadding Padding { get; set; } = ContainerPadding.Default;
        public Vector2 MinimumInteriorSize => new(200f, 100f);
    }

    private sealed class GraphModelWithNodes : IGraphModel
    {
        private readonly Dictionary<NodeId, INodeModel> _nodes = new();

        public GraphId Id => GraphId.Empty;
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "Test", false, false);
        public IReadOnlyCollection<INodeModel> Nodes => _nodes.Values;
        public IReadOnlyCollection<ILinkModel> Links => System.Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => System.Array.Empty<ICommentModel>();
        public INodeModel? FindNode(NodeId id) => _nodes.TryGetValue(id, out var v) ? v : null;
        public IPinModel? FindPin(PinId id) => null;
        public ILinkModel? FindLink(LinkId id) => null;
        public event System.Action<GraphChangeNotification>? Changed { add { } remove { } }

        public void Add(INodeModel node) => _nodes[node.Id] = node;
    }

    private sealed class StubTheme : IEditorTheme
    {
        public Vector4 BackgroundColor        => Vector4.Zero;
        public Vector4 GridMinorColor         => Vector4.Zero;
        public Vector4 GridMajorColor         => Vector4.Zero;
        public Vector4 SelectionAccent        => Vector4.One;
        public Vector4 PrimarySelectionAccent => Vector4.One;
        public Vector4 ErrorColor             => Vector4.One;
        public Vector4 WarningColor           => Vector4.One;
        public Vector4 TextDefault            => Vector4.One;
        public Vector4 TextMuted              => new(0.6f, 0.6f, 0.6f, 1f);
        public float NodeCornerRadius    => 4f;
        public float NodeBorderThickness => 1.5f;
        public float NodeHeaderHeight    => 28f;
        public float PinGlyphSize        => 10f;
        public float WireThicknessExec   => 3f;
        public float WireThicknessData   => 2f;
        public Vector4 GetCategoryHeaderColor(NodeCategory _) => Vector4.Zero;
        public nint GetFontForSize(float _) => System.IntPtr.Zero;
    }

    private sealed class StubSink : IGraphCommandSink
    {
        public GraphCommandResult Apply(GraphCommand command) => new(true, null);
    }

    private sealed class StubValidator : ILinkValidator
    {
        public LinkValidationResult Validate(PinId from, PinId to)
            => new(LinkValidity.Valid, null, false, null);
    }

    private sealed class StubTypeSystem : ITypeSystem
    {
        public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
        { info = default!; return false; }
        public Vector4 GetPinColor(TypeKey key) => default;
        public PinShape GetPinShape(TypeKey key, ContainerKind container) => default;
        public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;
        public bool AreCompatible(TypeKey from, TypeKey to) => false;
        public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
    }

    private sealed class StubCatalog : INodeCatalog
    {
        public IReadOnlyList<NodeCatalogEntry> All => System.Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCategoryDescriptor> Categories => System.Array.Empty<NodeCategoryDescriptor>();
        public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q) => System.Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) => System.Array.Empty<NodeCatalogEntry>();
    }

    private sealed class StubHost : IEditorHostServices
    {
        private readonly IEditorTheme _theme;
        public StubHost(IEditorTheme theme) { _theme = theme; }
        public INodeCatalog NodeCatalog => new StubCatalog();
        public ITypeSystem TypeSystem => new StubTypeSystem();
        public ILinkValidator LinkValidator => new StubValidator();
        public IGraphCommandSink CommandSink => new StubSink();
        public IPickerRegistry Pickers => throw new System.NotImplementedException();
        public IClipboard Clipboard => throw new System.NotImplementedException();
        public IIconProvider Icons => throw new System.NotImplementedException();
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession? Debug => null;
        public IInputSource Input => throw new System.NotImplementedException();
        public IEditorTheme Theme => _theme;
    }

    private static GraphView MakeView(GraphModelWithNodes model, IEditorTheme? theme = null)
    {
        var t = theme ?? new StubTheme();
        var sink = new StubSink();
        return new GraphView(model, sink, new StubValidator(), new StubTypeSystem(), new StubCatalog(), new StubHost(t));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RootNode_NodeCanvasPosition_EqualsNodePosition()
    {
        var model = new GraphModelWithNodes();
        var node = new StubNode { Position = new Vector2(100f, 200f) };
        model.Add(node);

        var view = MakeView(model);
        view.NodeCanvasPosition(node.Id).Should().Be(new Vector2(100f, 200f));
    }

    [Fact]
    public void RootNode_GetParentContainer_ReturnsNull()
    {
        var model = new GraphModelWithNodes();
        var node = new StubNode();
        model.Add(node);

        var view = MakeView(model);
        view.GetParentContainer(node.Id).Should().BeNull();
    }

    [Fact]
    public void ChildNode_NodeCanvasPosition_IncludesParentOffset()
    {
        // Container at canvas (500, 400), padding default (Top=8, Left=12), header=28.
        // Child at local (20, 30).
        // Expected canvas: (500 + 12 + 20, 400 + 28 + 8 + 30) = (532, 466).
        var model = new GraphModelWithNodes();
        var container = new StubContainer
        {
            Position = new Vector2(500f, 400f),
            Padding  = ContainerPadding.Default,
        };
        var child = new StubNode
        {
            Position          = new Vector2(20f, 30f),
            ParentContainerId = container.Id,
        };
        model.Add(container);
        model.Add(child);

        var view = MakeView(model);
        var expected = new Vector2(500f + 12f + 20f, 400f + 28f + 8f + 30f);
        view.NodeCanvasPosition(child.Id).Should().Be(expected);
    }

    [Fact]
    public void UnknownNode_NodeCanvasPosition_ReturnsZero()
    {
        var model = new GraphModelWithNodes();
        var view  = MakeView(model);
        view.NodeCanvasPosition(IdGenerator.NewNodeId()).Should().Be(Vector2.Zero);
    }
}
```

---

## Build and Test

After all changes:

```
cd FDP\ExtDeps\NodeEdit
dotnet build NodeEditor.sln -c Debug
dotnet test NodeEditor.sln -c Debug --no-build
```

Expected:
- 0 errors, 0 warnings
- All existing 95 tests pass
- 10 new tests added (7 from ContainerNodeModelTests + 4 from ContainerTransformTests = 11 new, total 106)

## Report

Write a report at `.dev/blueprints-2/reports/BATCH-08-REPORT.md` listing:
- Files created/modified
- Test counts before/after
- Any issues and how they were resolved

## Important Rules

1. No Unicode characters in comments or string literals (ASCII only).
2. `ParentContainerId` on `INodeModel` is a DEFAULT INTERFACE MEMBER returning `null`.
   Do NOT add it as abstract -- it must have a default body `=> null`.
3. `INodeModelExtensions` uses `is` pattern matching (not `as` cast) -- follow the spec exactly.
4. `AsContainer()` must return null for a node where `IsContainer == false`, even if it
   implements `IContainerNodeModel`.
5. `NodeCanvasPosition` is recursive -- it walks the parent chain. Each level adds the
   parent's canvas position + padding offset + child's local position.
6. The transform uses `Host.Theme.NodeHeaderHeight` for the vertical interior offset
   (not a hard-coded constant).
7. Minimize diffs: only change what is needed for the tasks described.
8. Build MUST succeed with 0 errors before finishing.
