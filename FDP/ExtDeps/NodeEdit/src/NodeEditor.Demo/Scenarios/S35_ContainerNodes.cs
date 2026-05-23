using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>
/// S35: Container nodes in several configurations:
///   - Flat container with a few children.
///   - Nested containers (2 levels).
///   - Parallel-region container (3 regions).
///   - Empty container (minimum size).
///   - Collapsed container.
/// </summary>
public sealed class S35_ContainerNodes : Scenario
{
    public override string Name        => "35 -- Container Nodes";
    public override string Description => "Flat, nested, parallel-region, empty, and collapsed containers. Drag nodes into/out of containers; zoom out below 0.5 for low-zoom view.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // ── 1. Flat container with three children ──────────────────────────────

        var flat = graph.AddContainer(IdGenerator.NewNodeId(), "FlatContainer", new Vector2(50f, 50f));
        flat.Category = NodeCategory.Function;

        var c1 = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(20f, 40f));
        var n1 = graph.FindNode(c1)!;
        ((FakeNodeModel)n1).ParentContainerId = flat.Id;
        flat.AddChild(c1);

        var c2 = AddNode(graph, catalog, "Util.Print", new Vector2(160f, 40f));
        var n2 = graph.FindNode(c2)!;
        ((FakeNodeModel)n2).ParentContainerId = flat.Id;
        flat.AddChild(c2);

        var c3 = AddNode(graph, catalog, "Flow.Delay", new Vector2(300f, 40f));
        var n3 = graph.FindNode(c3)!;
        ((FakeNodeModel)n3).ParentContainerId = flat.Id;
        flat.AddChild(c3);

        // Wire children inside the container.
        LinkNodes(graph, c1, 0, c2, 0);
        LinkNodes(graph, c2, 0, c3, 0);

        // ── 2. Nested containers (outer contains inner) ────────────────────────

        var outer = graph.AddContainer(IdGenerator.NewNodeId(), "OuterState", new Vector2(650f, 50f));
        outer.Category = NodeCategory.Event;

        var inner = graph.AddContainer(IdGenerator.NewNodeId(), "InnerState", new Vector2(30f, 40f));
        inner.Category = NodeCategory.Pure;
        inner.ParentContainerId = outer.Id;
        outer.AddChild(inner.Id);

        var innerChild = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(20f, 30f));
        ((FakeNodeModel)graph.FindNode(innerChild)!).ParentContainerId = inner.Id;
        inner.AddChild(innerChild);

        var outerChild = AddNode(graph, catalog, "Util.Print", new Vector2(250f, 40f));
        ((FakeNodeModel)graph.FindNode(outerChild)!).ParentContainerId = outer.Id;
        outer.AddChild(outerChild);

        // ── 3. Parallel-region container (3 regions) ──────────────────────────

        var parallel = graph.AddContainer(IdGenerator.NewNodeId(), "ParallelState", new Vector2(50f, 320f));
        parallel.Category = NodeCategory.FlowControl;
        parallel.AddRegion("RegionA", priority: 1);
        parallel.AddRegion("RegionB", priority: 2);
        parallel.AddRegion("RegionC", priority: 3);

        for (int r = 0; r < 3; r++)
        {
            var childId = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(20f, 30f + r * 80f));
            ((FakeNodeModel)graph.FindNode(childId)!).ParentContainerId = parallel.Id;
            parallel.AddChild(childId, regionIndex: r);

            var childId2 = AddNode(graph, catalog, "Flow.Delay", new Vector2(160f, 30f + r * 80f));
            ((FakeNodeModel)graph.FindNode(childId2)!).ParentContainerId = parallel.Id;
            parallel.AddChild(childId2, regionIndex: r);
        }

        // ── 4. Empty container (minimum size) ─────────────────────────────────

        var empty = graph.AddContainer(IdGenerator.NewNodeId(), "EmptyContainer", new Vector2(650f, 320f));
        empty.Category = NodeCategory.VariableGet;
        empty.MinimumInteriorSize = new Vector2(200f, 80f);
        // No children added.

        // ── 5. Collapsed container ─────────────────────────────────────────────

        var collapsed = graph.AddContainer(IdGenerator.NewNodeId(), "CollapsedState", new Vector2(350f, 350f));
        collapsed.Category = NodeCategory.Function;
        collapsed.IsCollapsed = true;

        var collChild = AddNode(graph, catalog, "Util.Print", new Vector2(20f, 30f));
        ((FakeNodeModel)graph.FindNode(collChild)!).ParentContainerId = collapsed.Id;
        collapsed.AddChild(collChild);

        // ── 6. Root-level nodes wired to container children ────────────────────

        var root1 = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(50f, 600f));
        var root2 = AddNode(graph, catalog, "Util.Print",      new Vector2(350f, 600f));
        LinkNodes(graph, root1, 0, root2, 0);
    }
}
