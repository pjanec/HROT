using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hrot.Hsm.Editor.Model;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

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
            LastLabelCount++;

            if (t.Kind == TransitionKind.Internal)
            {
                LastInternalTransitionCount++;
                if (!canDraw) continue;

                // Draw a small self-loop arc in the upper-right quadrant of the source state.
                var stateSize = t.Source.SizeOverride ?? DefaultStateSize;
                var stateMin = ctx.Viewport.GraphToScreen(t.Source.Position);
                var stateMax = ctx.Viewport.GraphToScreen(t.Source.Position + stateSize);
                var loopCenter = new Vector2(
                    stateMin.X + (stateMax.X - stateMin.X) * 0.75f,
                    stateMin.Y + (stateMax.Y - stateMin.Y) * 0.25f);
                float loopRadius = Math.Min(10f * ctx.Zoom, (stateMax.Y - stateMin.Y) * 0.18f);
                uint loopColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.2f, 0.9f));
                drawList.AddCircle(loopCenter, loopRadius, loopColor, 16, 1.5f * ctx.Zoom);
                drawList.AddText(loopCenter + new Vector2(loopRadius + 2f, -8f * ctx.Zoom), loopColor, label);
            }
            else
            {
                if (!canDraw) continue;

                // Draw label at midpoint between source and target state positions.
                var mid = ctx.Viewport.GraphToScreen(
                    (t.Source.Position + t.Target.Position) * 0.5f);
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
