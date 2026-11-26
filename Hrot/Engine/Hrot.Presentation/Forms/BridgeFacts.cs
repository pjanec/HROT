using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.UI.Common.Map3D;

namespace Hrot.Presentation.Forms;

/// <summary>
/// ⭐ CE-6501 (docs/designs/engineering/DESIGN_Bridge_Laying.md §13; mockup UX/bridge-laying-ui-visual.svg ③) — the Unit form's Bridge rows for a bridge engineer: what it does with
/// its span (<see cref="BridgeLayerLook.Of"/>, the renderers' one rule) and, while its replicated <see cref="BridgeCarried.Refusal"/> is set, the refusal in
/// <see cref="BridgeLaying.Say"/>'s words. Read-only. ⭐ 0 B on a steady frame: the texts are cached and rebuilt only when the state or the refusal changes (UI thread only).
/// </summary>
public static class BridgeFacts
{
    private static readonly FixedString64 NoSpanText = new("No span on board (laid)");
    private static readonly FixedString64 LayingText = new("Laying its span");
    private static readonly FixedString64 RecoveringText = new("Recovering a span");
    private static readonly FixedString64[] CarriedText = { new("Carried — no span"), new("Carried — 1 span"), new("Carried — 2 spans"), new("Carried — 3 spans") };

    private static BridgeRefusal _refusal;
    private static float _gap = float.NaN;
    private static long _type;
    private static FixedString64 _refusalText;

    /// <summary>Is <paramref name="e"/> a bridge engineer (it carries <see cref="BridgeCarried"/>)?</summary>
    public static bool IsBridgeLayer(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<BridgeCarried>() && w.HasComponent<BridgeCarried>(e);

    /// <summary>Is a refusal standing on <paramref name="e"/>?</summary>
    public static bool IsRefused(EntityRepository w, Entity e)
        => IsBridgeLayer(w, e) && w.GetComponentRO<BridgeCarried>(e).Refusal != BridgeRefusal.NotRefused;

    /// <summary>The Bridge row: carried (with the count) · laying · recovering · no span.</summary>
    public static FixedString64 StateText(EntityRepository w, Entity e) => BridgeLayerLook.Of(w, e) switch
    {
        BridgeLayerState.Laying => LayingText,
        BridgeLayerState.Recovering => RecoveringText,
        BridgeLayerState.Carried => CarriedText[System.Math.Min(w.GetComponentRO<BridgeCarried>(e).Count, (byte)(CarriedText.Length - 1))],
        _ => NoSpanText,
    };

    /// <summary>The Refused row: the engineer's last refusal in words (its span from its type, looked up only when the refusal changes).</summary>
    public static FixedString64 RefusalText(EntityRepository w, Entity e)
    {
        ref readonly var c = ref w.GetComponentRO<BridgeCarried>(e);
        if (c.Refusal == _refusal && c.GapM.Equals(_gap) && c.BridgeType == _type) return _refusalText;
        _refusal = c.Refusal; _gap = c.GapM; _type = c.BridgeType;
        float span = BridgeLaying.Figures(w, e, out var layer, out _) ? layer.SpanMaxM : 0f;
        _refusalText = new FixedString64(BridgeLaying.Say(c.Refusal, c.GapM, span));
        return _refusalText;
    }
}
