using Fdp.Core;

namespace Hrot.UI.Common.Map3D;

/// <summary>What a bridge engineer shows a renderer: its span on board, the span going out, the span coming back, or none.</summary>
public enum BridgeLayerState : byte { NoSpan = 0, Carried = 1, Laying = 2, Recovering = 3 }

/// <summary>
/// ⭐ <c>CE-4693</c> (bridge laying BL-S3, ③; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12, BL-21) — the renderers' question "what is this bridge engineer doing with its span?",
/// answered in ONE place from its replicated <see cref="Fdp.Toolkit.Terrain.BridgeCarried"/> — the 3-D map, an IG gateway and the rails ask the same rule (the
/// <see cref="FlailActivity"/> / <c>RadarActivity</c> pattern). A LAID span is the bridge obstacle itself (the terrain draws its deck, BL-13); the engineer then
/// shows <see cref="BridgeLayerState.NoSpan"/>. ⛔ No allocation: a static read.
/// </summary>
public static class BridgeLayerLook
{
    /// <summary>The engineer's span state; <see cref="BridgeLayerState.NoSpan"/> for a hull that is not a bridge engineer.</summary>
    public static BridgeLayerState Of(EntityRepository world, Entity e)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.BridgeCarried>() || !world.HasComponent<Fdp.Toolkit.Terrain.BridgeCarried>(e)) return BridgeLayerState.NoSpan;
        ref readonly var c = ref world.GetComponentRO<Fdp.Toolkit.Terrain.BridgeCarried>(e);
        return c.Order switch
        {
            Fdp.Toolkit.Terrain.BridgeOrder.Lay => BridgeLayerState.Laying,
            Fdp.Toolkit.Terrain.BridgeOrder.Recover => BridgeLayerState.Recovering,
            _ => c.Count > 0 ? BridgeLayerState.Carried : BridgeLayerState.NoSpan,
        };
    }
}
