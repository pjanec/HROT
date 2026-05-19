using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fbt;
using Fbt.Runtime;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// Entity-aware ImGui renderer for <see cref="BrainBTreeState"/>.
/// Renders a color-coded interactive tree showing the active execution path,
/// decodes per-node runtime state (LocalRegisters, AsyncHandles), and shows
/// source-location tooltips from NodeDebugMetadata.
/// </summary>
[ImGuiRenderer(typeof(BrainBTreeState))]
public sealed class BTreeVisualizerRenderer : IEntityAwareImGuiRenderer
{
    private static readonly Vector4 ColorGreen  = new Vector4(0.2f, 0.9f, 0.2f, 1.0f);
    private static readonly Vector4 ColorYellow = new Vector4(0.9f, 0.9f, 0.2f, 1.0f);
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

    public string? GetSummary(IInspectableSession session, Entity entity, object value)
    {
        string baseSummary = GetSummary(value) ?? string.Empty;

        var registry = BehaviorRegistryAccessor;
        if (registry != null && session.HasComponent(entity, typeof(BehaviorState)))
        {
            if (session.GetComponent(entity, typeof(BehaviorState)) is BehaviorState state
                && state.ActiveBehaviorHash != 0
                && registry.TryGetName(state.ActiveBehaviorHash, out string? name))
            {
                return $"{name} | {baseSummary}";
            }
        }

        return baseSummary;
    }

    public bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
    {
        doubleClickedPath = null;

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

        // Resolve global simulation time for elapsed-time labels on Wait/Cooldown nodes.
        float globalTime = session is RepositoryAdapter ra
            ? (float)ra.Repo.GetSingletonUnmanaged<GlobalTime>().TotalTime
            : 0f;

        DrawNode(blob, btState.State, 0, globalTime);
        return true;
    }

    // ---- Tree drawing ----

    private static unsafe void DrawNode(BehaviorTreeBlob blob, BehaviorTreeState state, int nodeIndex, float globalTime)
    {
        if (nodeIndex < 0 || nodeIndex >= blob.Nodes.Length) return;

        var node = blob.Nodes[nodeIndex];
        bool isRunning    = state.RunningNodeIndex > 0 && state.RunningNodeIndex == nodeIndex;
        bool isIdle       = state.RunningNodeIndex == 0;
        bool isAncestral  = !isRunning && IsAncestralPath(blob, ref state, nodeIndex);
        bool isActivePath = isRunning || isAncestral;

        // Color coding
        int popColors = 0;
        if (isRunning)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ColorGreen);
            popColors++;
        }
        else if (isAncestral)
        {
            // Composite on the active execution path
            ImGui.PushStyleColor(ImGuiCol.Text, ColorYellow);
            popColors++;
        }
        else if (!isIdle && node.ChildCount == 0)
        {
            // Inactive leaf while tree is running -- dim it
            ImGui.PushStyleColor(ImGuiCol.Text, ColorGray);
            popColors++;
        }

        // Label with decoded runtime state
        string label = GetNodeLabel(blob, ref state, nodeIndex, isActivePath, globalTime);

        bool hasChildren = node.ChildCount > 0;
        ImGuiTreeNodeFlags flags = hasChildren
            ? ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.OpenOnArrow
            : ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        bool open = ImGui.TreeNodeEx($"##n{nodeIndex}", flags, $"{nodeIndex} {label}");

        if (popColors > 0) ImGui.PopStyleColor(popColors);

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
                DrawNode(blob, state, childIndex, globalTime);
                childIndex += blob.Nodes[childIndex].SubtreeOffset;
            }
            ImGui.TreePop();
        }
    }

    private static unsafe string GetNodeLabel(
        BehaviorTreeBlob blob,
        ref BehaviorTreeState state,
        int nodeIndex,
        bool isActivePath,
        float globalTime)
    {
        var node = blob.Nodes[nodeIndex];

        switch (node.Type)
        {
            case NodeType.Repeater:
            {
                int target = blob.IntParams.Length > node.PayloadIndex
                    ? blob.IntParams[node.PayloadIndex] : 0;
                if (isActivePath)
                {
                    // LocalRegisters[0] holds the current loop iteration counter
                    int current = state.LocalRegisters[0];
                    return $"Repeater ({current}/{target}x)";
                }
                return $"Repeater ({target}x)";
            }

            case NodeType.Parallel:
                if (isActivePath)
                {
                    // LocalRegisters[0] = completion bitmask (lower 16 bits)
                    // LocalRegisters[1] = success bitmask (lower 16 bits)
                    int completed = System.Numerics.BitOperations.PopCount(
                        (uint)(state.LocalRegisters[0] & 0xFFFF));
                    int succeeded = System.Numerics.BitOperations.PopCount(
                        (uint)(state.LocalRegisters[1] & 0xFFFF));
                    return $"Parallel ({succeeded} ok / {completed} done / {node.ChildCount} total)";
                }
                return "Parallel";

            case NodeType.Wait:
            {
                float duration = blob.FloatParams.Length > node.PayloadIndex
                    ? blob.FloatParams[node.PayloadIndex] : 0f;
                if (isActivePath && state.RunningNodeIndex == nodeIndex && state.AsyncData != 0)
                {
                    // AsyncData stores start-time via AsyncToken.FloatA
                    float startTime = new AsyncToken(state.AsyncData).FloatA;
                    float elapsed   = globalTime - startTime;
                    return $"Wait ({elapsed:F1}s / {duration:F1}s)";
                }
                return $"Wait ({duration:F1}s)";
            }

            case NodeType.Cooldown:
            {
                float duration = blob.FloatParams.Length > node.PayloadIndex
                    ? blob.FloatParams[node.PayloadIndex] : 0f;
                if (isActivePath && state.RunningNodeIndex == nodeIndex && state.AsyncData != 0)
                {
                    float startTime = new AsyncToken(state.AsyncData).FloatA;
                    float elapsed   = globalTime - startTime;
                    float remaining = duration - elapsed;
                    return $"Cooldown ({remaining:F1}s left)";
                }
                return $"Cooldown ({duration:F1}s)";
            }

            case NodeType.Action:
            {
                if (blob.DebugMetadata != null && (uint)nodeIndex < (uint)blob.DebugMetadata.Length)
                {
                    var meta = blob.DebugMetadata[nodeIndex];
                    if (meta != null && !string.IsNullOrEmpty(meta.Label))
                        return $"[A] {meta.Label}";
                }

                return blob.MethodNames.Length > node.PayloadIndex
                    ? $"[A] {ShortenNodeName(blob.MethodNames[node.PayloadIndex])}"
                    : "[A]";
            }

            case NodeType.Condition:
            {
                if (blob.DebugMetadata != null && (uint)nodeIndex < (uint)blob.DebugMetadata.Length)
                {
                    var meta = blob.DebugMetadata[nodeIndex];
                    if (meta != null && !string.IsNullOrEmpty(meta.Label))
                        return $"[C] {meta.Label}";
                }

                return blob.MethodNames.Length > node.PayloadIndex
                    ? $"[C] {ShortenNodeName(blob.MethodNames[node.PayloadIndex])}"
                    : "[C]";
            }

            default:
                return node.Type.ToString();
        }
    }

    /// <summary>
    /// Returns true when <paramref name="nodeIndex"/> is a composite ancestor of the
    /// currently running node (i.e., the running node is inside its subtree).
    /// Uses the DFS preorder blob layout: node N with SubtreeOffset S covers [N, N+S).
    /// </summary>
    internal static bool IsAncestralPath(BehaviorTreeBlob blob, ref BehaviorTreeState state, int nodeIndex)
    {
        int runningIdx = state.RunningNodeIndex;
        if (runningIdx == 0) return false;
        if (nodeIndex == runningIdx) return false; // the running node itself is green, not ancestral
        if (nodeIndex < 0 || nodeIndex >= blob.Nodes.Length) return false;
        var node = blob.Nodes[nodeIndex];
        return nodeIndex < runningIdx && runningIdx < nodeIndex + node.SubtreeOffset;
    }

    /// <summary>
    /// Testable helper: returns the color to use for a node index given current state.
    /// Returns 0 = default, 1 = green (running), 2 = gray (inactive leaf).
    /// Ancestral (yellow) detection requires a full <see cref="BehaviorTreeState"/>; use
    /// <see cref="IsAncestralPath"/> separately.
    /// </summary>
    internal static int GetNodeColorCode(int nodeIndex, int runningNodeIndex, bool hasChildren)
    {
        if (runningNodeIndex > 0 && runningNodeIndex == nodeIndex) return 1; // green
        if (runningNodeIndex != 0 && !hasChildren) return 2;                 // gray
        return 0;                                                             // default
    }

    private static string ShortenNodeName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return fullName;

        int atIdx = fullName.IndexOf('@');
        string baseName = atIdx >= 0 ? fullName.Substring(0, atIdx) : fullName;

        int dotIdx = baseName.LastIndexOf('.');
        return dotIdx >= 0 ? baseName.Substring(dotIdx + 1) : baseName;
    }
}
