using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hrot.Hsm.Editor.Model;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

// Custom canvas renderer that draws Event[Guard]/Action labels at transition midpoints
// (the arrowhead itself is drawn by the canvas since CE-1000).
// Runs in the AfterWires pass so labels and arrowheads appear above wire lines.
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

                // ⭐ CE-1000: the label sits ON the drawn arrow — the canvas's own LinkPath, the same instance the
                //    wire renderer drew and the hit-tester tests. The arrowhead is drawn by the canvas now.
                //    Fallback (stub contexts / not laid out): the midpoint between the two state centres.
                Vector2 mid;
                if (ctx.TryGetLinkScreenPath(linkId, out var path))
                {
                    mid = LabelAnchor(path, ImGui.CalcTextSize(label));
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
        if (t.Guard?.MethodFqn is { } guardFqn)   // CE-417
        {
            int dot = guardFqn.LastIndexOf('.');
            string guardShort = dot >= 0 ? guardFqn[(dot + 1)..] : guardFqn;
            guardPart = "[" + guardShort + "]";
        }

        string actionPart = "";
        if (t.Action?.MethodFqn is { } actionFqn)   // CE-417
        {
            int dot = actionFqn.LastIndexOf('.');
            string actionShort = dot >= 0 ? actionFqn[(dot + 1)..] : actionFqn;
            actionPart = "/" + actionShort;
        }

        string syncBadge = t.SyncGroupId != 0 ? " [SG:" + t.SyncGroupId + "]" : "";
        string priorityBadge = t.Priority != 128 ? " (P:" + t.Priority + ")" : "";

        string result = eventPart + guardPart + actionPart + syncBadge + priorityBadge;
        return result.Length == 0 ? "<unnamed>" : result;
    }

    /// <summary>
    /// CE-1000 — where a label of <paramref name="textSize"/> goes on <paramref name="path"/>: at the path's middle,
    /// pushed to the OUTER side of the bend so the text does not sit on the line (pairs A→B / B→A bend apart, so
    /// their labels separate too). Returns the text's top-left corner.
    /// </summary>
    internal static Vector2 LabelAnchor(LinkPath path, Vector2 textSize)
    {
        var mid = path.Point(0.5f);
        var chord = path.End - path.Start;
        var bulge = mid - (path.Start + path.End) * 0.5f;           // which side the arc bends to
        var n = bulge.LengthSquared() > 1e-6f
            ? Vector2.Normalize(bulge)
            : (chord.LengthSquared() > 1e-6f ? Vector2.Normalize(new Vector2(chord.Y, -chord.X)) : new Vector2(0f, -1f));
        // Centre the box on a point 4 px beyond the curve along n, then step out by half the box's extent along n.
        float halfExtent = MathF.Abs(n.X) * textSize.X * 0.5f + MathF.Abs(n.Y) * textSize.Y * 0.5f;
        var centre = mid + n * (4f + halfExtent);
        return centre - textSize * 0.5f;
    }
}
