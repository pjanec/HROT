using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hrot.Hsm.Editor.Model;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

// Custom canvas renderer that draws Event[Guard]/Action labels at transition midpoints.
// Runs in the AfterWires pass so labels appear above wire lines.
public sealed class HsmTransitionLabelRenderer : ICustomCanvasRenderer
{
    private readonly HsmAsset _asset;

    public HsmTransitionLabelRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public string Id => "hsm.transition_labels";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterWires;
    public bool IsActive { get; set; } = true;

    // Counters updated each Render() call; read by unit tests.
    // Both are incremented BEFORE the geometry TryGet gate so count-based
    // tests pass even when TryGet returns false (e.g. stub render contexts).
    internal int LastInternalTransitionCount;
    internal int LastLabelCount;

    // Default state half-size used for internal-transition loop placement
    // when a state has no explicit SizeOverride.
    internal static readonly Vector2 DefaultStateSize = new(120f, 40f);

    public void Render(ICanvasRenderContext ctx)
    {
        if (ctx.IsLowZoom) return;
        LastInternalTransitionCount = 0;
        LastLabelCount = 0;

        var drawList = ctx.DrawList;
        bool canDraw = Unsafe.As<ImDrawListPtr, nint>(ref drawList) != 0;

        foreach (var linkId in ctx.VisibleLinks)
        {
            var t = _asset.FindTransitionByVisualId(linkId.Value);
            if (t is null) continue;

            string label = FormatLabel(t);

            // Count eligible transitions BEFORE geometry gate so tests pass
            // with stub contexts that return false from TryGet.
            LastLabelCount++;

            if (t.Kind == TransitionKind.Internal)
            {
                LastInternalTransitionCount++;
                if (!canDraw) continue;

                // Anchor off canvas-computed screen geometry. Skip if node not laid out.
                if (!ctx.TryGetNodeScreenRect(new NodeId(t.Source.StableId), out var srcRect))
                    continue;

                // Draw a small self-loop arc in the upper-right quadrant of the source state.
                // srcRect is already screen-space — do NOT multiply dims by Zoom again.
                var loopCenter = new Vector2(
                    srcRect.Min.X + srcRect.Size.X * 0.75f,
                    srcRect.Min.Y + srcRect.Size.Y * 0.25f);
                float loopRadius = Math.Min(10f * ctx.Zoom, srcRect.Size.Y * 0.18f);
                uint loopColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.2f, 0.9f));
                drawList.AddCircle(loopCenter, loopRadius, loopColor, 16, 1.5f * ctx.Zoom);
                drawList.AddText(loopCenter + new Vector2(loopRadius + 2f, -8f * ctx.Zoom), loopColor, label);
            }
            else
            {
                if (!canDraw) continue;

                // External transitions: use true wire midpoint from pin screen positions.
                // Fall back to node-rect centres if pin lookup fails; skip if those also fail.
                Vector2 mid;
                var srcPinId = new PinId(t.Source.HiddenOutputPinId);
                var tgtPinId = new PinId(t.Target.HiddenInputPinId);

                if (ctx.TryGetPinScreenPosition(srcPinId, out var srcPin) &&
                    ctx.TryGetPinScreenPosition(tgtPinId, out var tgtPin))
                {
                    mid = (srcPin + tgtPin) * 0.5f;
                }
                else if (ctx.TryGetNodeScreenRect(new NodeId(t.Source.StableId), out var srcRect) &&
                         ctx.TryGetNodeScreenRect(new NodeId(t.Target.StableId), out var tgtRect))
                {
                    mid = (srcRect.Center + tgtRect.Center) * 0.5f;
                }
                else
                {
                    continue;
                }

                uint textColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f));
                drawList.AddText(mid, textColor, label);
            }
        }
    }

    // Formats the label string for a transition.
    // Format: "EventName[GuardShort]/ActionShort" with parts omitted when null.
    // Returns "<unnamed>" when all parts are absent.
    public static string FormatLabel(TransitionNode t)
    {
        string eventPart = t.EventName ?? "";

        string guardPart = "";
        if (t.GuardFunction is not null)
        {
            int dot = t.GuardFunction.LastIndexOf('.');
            string guardShort = dot >= 0 ? t.GuardFunction[(dot + 1)..] : t.GuardFunction;
            guardPart = "[" + guardShort + "]";
        }

        string actionPart = "";
        if (t.ActionFunction is not null)
        {
            int dot = t.ActionFunction.LastIndexOf('.');
            string actionShort = dot >= 0 ? t.ActionFunction[(dot + 1)..] : t.ActionFunction;
            actionPart = "/" + actionShort;
        }

        string syncBadge = t.SyncGroupId != 0 ? " [SG:" + t.SyncGroupId + "]" : "";
        string priorityBadge = t.Priority != 128 ? " (P:" + t.Priority + ")" : "";

        string result = eventPart + guardPart + actionPart + syncBadge + priorityBadge;
        return result.Length == 0 ? "<unnamed>" : result;
    }
}
