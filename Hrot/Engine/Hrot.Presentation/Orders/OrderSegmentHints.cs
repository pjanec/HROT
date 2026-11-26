using System;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Gizmos;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐ CE-6500 (docs/designs/engineering/DESIGN_Bridge_Laying.md §13 U-2) — the LIVE LABEL an order gives its two-point pick (<see cref="Fdp.Toolkit.Vis2D.Gizmos.SegmentPickerGizmo"/>):
/// the pick is generic, its words are the order's. ⭐ <c>LayBridge</c>: the engineer's own plan for (near, cursor) — <see cref="BridgeLaying.PlanFor"/>, the call the brain and the
/// mover make, so the preview cannot disagree with the order — as "gap 18.4 m — fits (span 24 m)" or the refusal in <see cref="BridgeLaying.Say"/>'s words.
/// An order with no entry gets the plain line (its length).
/// </summary>
public static class OrderSegmentHints
{
    /// <summary>The label for order <paramref name="orderName"/> given to <paramref name="unit"/> in <paramref name="world"/>, or null (the plain line).</summary>
    public static Func<Vector2, Vector2, SegmentHint>? For(string orderName, EntityRepository world, Entity unit)
        => orderName == "LayBridge" ? (a, b) => LayBridge(world, unit, a, b) : null;

    /// <summary>LayBridge's label for a bank at <paramref name="near"/> and the cursor at <paramref name="far"/>.</summary>
    public static SegmentHint LayBridge(EntityRepository world, Entity engineer, Vector2 near, Vector2 far)
    {
        if (!world.IsAlive(engineer) || !BridgeLaying.PlanFor(world, engineer, near, far, out var plan, out float spanMax))
            return new SegmentHint("this unit carries no bridge", false);
        return plan.Ok
            ? new SegmentHint(string.Create(CultureInfo.InvariantCulture, $"gap {plan.Gap:0.0} m — fits (span {spanMax:0} m)"), true)
            : new SegmentHint(BridgeLaying.Say(plan.Refusal, plan.Gap, spanMax), false);
    }
}
