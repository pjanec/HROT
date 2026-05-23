using Hrot.Hsm.Editor.Model;
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

    public void Render(ICanvasRenderContext ctx)
    {
        // TODO: draw Event[Guard]/Action label at each transition midpoint
        // Runs in AfterWires pass; uses ctx.VisibleLinks + ctx.Graph
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
