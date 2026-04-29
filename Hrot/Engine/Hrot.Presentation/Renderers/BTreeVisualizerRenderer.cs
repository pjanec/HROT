using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fbt;
using Fbt.Runtime;
using ImGuiNET;

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
    public static DoctrineRegistry? DoctrineRegistryAccessor { get; set; }

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

        var registry = DoctrineRegistryAccessor;
        if (registry == null) return false;

        if (!session.HasComponent(entity, typeof(DoctrineState))) return false;
        var dsObj = session.GetComponent(entity, typeof(DoctrineState));
        if (dsObj is not DoctrineState ds) return false;

        if (!registry.TryGetDefinition(ds.ActiveDoctrineHash, out var def)) return false;

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
            // Inactive leaf while tree is running -- dim it
            ImGui.PushStyleColor(ImGuiCol.Text, ColorGray);
            pushed = true;
        }

        bool hasChildren = node.ChildCount > 0;
        ImGuiTreeNodeFlags flags = hasChildren
            ? ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.OpenOnArrow
            : ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

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
