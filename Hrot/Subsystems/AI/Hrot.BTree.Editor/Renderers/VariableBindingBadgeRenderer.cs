using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using ImGuiNET;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Selection;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Renderers;

// Custom canvas renderer for the BTree editor.
// Draws a green (bound) or red (unbound) badge over Action and Condition leaf nodes
// that carry a blackboard expression-target field binding.
// Runs at the AfterNodes pass so badges appear on top of node bodies.
public sealed class VariableBindingBadgeRenderer : ICustomCanvasRenderer
{
    // Badge color when a variable binding is present.
    private static readonly Vector4 BoundColor   = new(0.2f, 0.5f, 0.2f, 0.75f);
    // Badge color when no variable binding is set.
    private static readonly Vector4 UnboundColor = new(0.6f, 0.15f, 0.15f, 0.75f);
    // Badge text color (white).
    private static readonly Vector4 TextColor    = new(1f, 1f, 1f, 1f);

    private readonly EditorSelectionStore _store;

    public string Id   => "btree.variable_binding_badges";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    // Number of badges drawn in the most recent Render() call.
    // Used by unit tests that cannot inspect ImGui draw list calls directly.
    internal int LastRenderBadgeCount;

    public VariableBindingBadgeRenderer(EditorSelectionStore store)
    {
        _store = store;
    }

    public void Render(ICanvasRenderContext ctx)
    {
        if (ctx.IsLowZoom) return;
        LastRenderBadgeCount = 0;

        var asset = _store.ActiveAsset as BehaviorTreeAsset;

        foreach (var nodeId in ctx.VisibleNodes)
        {
            var graphNode = ctx.Graph.FindNode(nodeId);
            if (graphNode is null) continue;

            // Only badge Action and Condition leaf nodes.
            if (graphNode.Kind.Id != BTreeKinds.Action && graphNode.Kind.Id != BTreeKinds.Condition)
                continue;

            var editorNode = asset?.FindNode(nodeId.Value);

            string? exprTarget =
                editorNode?.Action?.ExpressionTargetField ??
                editorNode?.Condition?.ExpressionTargetField;

            bool isBound = !string.IsNullOrEmpty(exprTarget);

            LastRenderBadgeCount++;

            var screenPos = ctx.Viewport.GraphToScreen(graphNode.Position + new Vector2(0f, -14f));
            DrawBadge(ctx.DrawList, screenPos, ctx.Zoom, isBound, exprTarget);
        }
    }

    private static void DrawBadge(
        ImDrawListPtr dl, Vector2 screenPos, float zoom, bool isBound, string? label)
    {
        float fontSize = 10f * zoom;
        if (fontSize < 7f) return;   // too small to be legible

        // Skip drawing when not running inside a live ImGui frame (e.g. unit tests).
        if (Unsafe.As<ImDrawListPtr, nint>(ref dl) == 0) return;

        string text    = isBound ? label! : "(unbound)";
        var    bgColor = isBound ? BoundColor : UnboundColor;

        var textSize = ImGui.CalcTextSize(text);
        float padX   = 4f * zoom;
        float padY   = 2f * zoom;
        var bgMin    = screenPos - new Vector2(padX, padY);
        var bgMax    = screenPos + textSize + new Vector2(padX, padY);

        dl.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(bgColor), 3f * zoom);
        dl.AddText(screenPos, ImGui.GetColorU32(TextColor), text);
    }
}
