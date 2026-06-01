using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.Panels;

/// <summary>
/// A custom details provider for the demo that displays properties of a SingleNode,
/// including its container hierarchy and region introspection.
/// </summary>
public sealed class DemoNodeDetailsProvider : IDetailsViewProvider
{
    private readonly FakeGraphModel _graph;
    public int Priority => 100;

    public DemoNodeDetailsProvider(FakeGraphModel graph) => _graph = graph;

    public bool CanHandle(DetailsTarget target) => target is DetailsTarget.SingleNode;

    public IDetailsView Build(DetailsTarget target, IDetailsContext ctx)
    {
        var singleNode = (DetailsTarget.SingleNode)target;
        return new DemoNodeDetailsView(singleNode.Id, _graph);
    }
}

internal sealed class DemoNodeDetailsView : IDetailsView
{
    private readonly NodeId _nodeId;
    private readonly FakeGraphModel _graph;

    public bool IsDirty => false;
    public void Commit() { }
    public void Revert() { }

    public DemoNodeDetailsView(NodeId nodeId, FakeGraphModel graph)
    {
        _nodeId = nodeId;
        _graph = graph;
    }

    public void Draw(IDetailsRenderContext ctx)
    {
        var node = _graph.FindNode(_nodeId);
        if (node == null)
        {
            ImGui.TextColored(ctx.Theme.TextMuted, "Node not found");
            return;
        }

        ImGui.Text("ID:");
        ImGui.SameLine(80f);
        ImGui.TextDisabled(_nodeId.ToString());

        ImGui.Text("Kind:");
        ImGui.SameLine(80f);
        ImGui.TextDisabled(node.Kind.Id);

        ImGui.Text("Title:");
        ImGui.SameLine(80f);
        ImGui.TextDisabled(node.Title);

        ImGui.Text("Position:");
        ImGui.SameLine(80f);
        ImGui.TextDisabled($"{node.Position.X:F1}, {node.Position.Y:F1}");

        if (node.SizeOverride.HasValue)
        {
            ImGui.Text("Size:");
            ImGui.SameLine(80f);
            ImGui.TextDisabled($"{node.SizeOverride.Value.X:F1} x {node.SizeOverride.Value.Y:F1}");
        }

        if (node.ParentContainerId.HasValue)
        {
            ImGui.Separator();
            ImGui.TextColored(ctx.Theme.TextMuted, "Hierarchy Information");

            ImGui.Text("Parent:");
            ImGui.SameLine(80f);
            ImGui.TextDisabled(node.ParentContainerId.Value.ToString());

            var parent = _graph.FindNode(node.ParentContainerId.Value);
            if (parent?.AsContainer() is { } container)
            {
                int rIdx = container.GetRegionIndexForChild(_nodeId);
                ImGui.Text("Region:");
                ImGui.SameLine(80f);

                if (rIdx >= 0)
                {
                    var regionName = rIdx < container.Regions.Count ? container.Regions[rIdx].Name : "Unknown";
                    ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), $"[{rIdx}] {regionName}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.4f, 1f), "(Base Interior / No Region)");
                }
            }
        }
    }
}

