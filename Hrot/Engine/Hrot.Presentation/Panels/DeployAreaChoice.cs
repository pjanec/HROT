using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Hrot.Gameplay;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐ CE-3370 (Player Join S3-d, docs/DESIGN_Player_Join.md §11) — <b>the ONE owner of "the side and the spawn area the Player has chosen"</b>: the deploy screen's list and the Player's 2-D map
/// (<see cref="Hrot.UI.Common.Map2D.DeployAreaMapLayer"/>) both read and write THIS, so a click on the map and a click in the list are the same selection and each follows the other. One per
/// <see cref="DeployFlowSystem"/> (<see cref="Of"/>); the choice rides the next <see cref="DeployFlowSystem.Request"/> (<see cref="DeployScreen.Deploy"/>).
/// </summary>
public sealed class DeployAreaChoice
{
    private static readonly ConditionalWeakTable<DeployFlowSystem, DeployAreaChoice> PerFlow = new();

    /// <summary>The choice of <paramref name="flow"/> (made on first use).</summary>
    public static DeployAreaChoice Of(DeployFlowSystem flow) => PerFlow.GetValue(flow, _ => new DeployAreaChoice());

    /// <summary>The side whose offers and areas are shown (null until the screen or a rail picks; <see cref="EffectiveSide"/> then falls back to the first side offered).</summary>
    public ForceId? Side { get; set; }

    /// <summary>The spawn area chosen (an area offer's net id); 0 = "Any area" — the default.</summary>
    public int AreaNetId { get; private set; }

    /// <summary>Chooses a spawn area (0 = any).</summary>
    public void Select(int areaNetId) => AreaNetId = areaNetId;

    /// <summary>The side the Player's choice applies to: <see cref="Side"/> while it still has a unit or role on offer, else the first side offered; null with nothing offered. (Same rule the screen draws by.)</summary>
    public ForceId? EffectiveSide(IReadOnlyList<DeployOffer> offers)
    {
        ForceId? first = null;
        for (int i = 0; i < offers.Count; i++)
        {
            var o = offers[i];
            if (o.IsArea) continue;
            if (Side is { } s && o.Side == s) return s;
            if (first is null || o.Side < first.Value) first = o.Side;   // the lowest side — the screen lists its sides sorted
        }
        return first;
    }

    /// <summary>Drops a choice that is gone (the area vanished or belongs to the other side): "Any area".</summary>
    public void Normalise(IReadOnlyList<DeployOffer> offers, ForceId side)
    {
        if (AreaNetId == 0) return;
        for (int i = 0; i < offers.Count; i++)
            if (offers[i].IsArea && offers[i].Side == side && offers[i].UnitNetId == AreaNetId) return;
        AreaNetId = 0;
    }
}
