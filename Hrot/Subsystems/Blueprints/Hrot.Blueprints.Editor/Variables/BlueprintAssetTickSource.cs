using System;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Blueprints.Editor.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>C-tick</c>'s editor half — the bridge that finally makes the variable table LIVE.</b>
///
/// <para>
/// Batch 68 cut the seam and left it open on purpose: <c>VariableRow.AssetTick</c> is a per-row
/// nullable delegate, and a row without one is <b>inert rather than wrong</b>, because the only clock
/// that existed was the WORLD tick and building the highlight on it would clear the red while paused.
/// ⇒ ⭐ <b>this supplies the real one, and nothing else changes.</b>
/// </para>
///
/// <para>
/// ⚠ <b>Why the bridge lives HERE and not in <c>Hrot.Editor.AiShared</c>:</b> AiShared cannot see
/// <c>Fdp.Toolkits</c>, and it should not — the row model is host-agnostic by design (Watch mixes
/// BTree, HSM and blueprint rows). ⛔ Teaching it about <c>BlueprintAssetTick</c> would make the
/// generic control know one host's clock. ⇒ the host supplies its own.
/// </para>
///
/// <para>
/// ⭐ <b><see cref="Attach"/>/<see cref="Detach"/> are refcounted</b> because the counter is opt-in:
/// the tick loop must not pay for it when no panel is open, and two open panels must not have the
/// first one closed switch it off under the second.
/// </para>
///
/// <para>
/// ⛔⛔ <b>SUPERSEDED by <c>Fdp.Core.BehaviorFrame</c>, Batch 94 (<c>94b</c>) — and KEPT DORMANT
/// deliberately.</b> 📄 <c>Q46</c> §2 rule 2b is the user's own specification: <i>"the brain (cgf) does
/// not tick ANY behavior when dt=0 so the tick source is not dependent on behavior type"</i> ⇒ ⭐ ONE
/// global pulse for all three hosts, with <b>no <c>Enabled</c> flag, no refcount and no
/// per-<c>(asset, entity)</c> table</b>. Every production row now reads that instead.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Why this was not deleted, stated so it is a decision rather than an oversight.</b>
/// <c>Q46</c> §4b calls for routing it away *(<c>R-13</c>: duplicate CODE ⇒ route)*, and the handoff
/// allows keeping it if routing costs more than the slice itself. 📐 <b>It does, for one concrete
/// reason:</b> its rails are <c>BlueprintAssetTickTests</c> in <b><c>Fdp.Toolkits.Tests</c></b> —
/// 📌 <c>DEBT-AIB-030</c>, the suite with <b>seven tests whose identity ROTATES between runs</b>, so
/// neither a red nor a green from it is evidence. ⇒ ⛔ <b>a rail migration there could not be
/// gated</b>, and removing <c>BlueprintAssetTick.Bump</c> also touches four sites inside
/// <c>BlueprintTickSystem</c>'s hot path. ⭐ The new pulse's own rails were therefore written in a
/// suite that IS gated *(<c>TheBehaviorFramePulseTests</c>, <c>Hrot.Blueprints.Tests</c>)*.
/// ⇒ 📌 filed for a batch that can gate it. ⛔ <b>Do not wire this to anything new.</b>
/// </para>
/// </summary>
public static class BlueprintAssetTickSource
{
    private static int _attachCount;

    /// <summary>⭐ The delegate for one row's <c>(asset, entity)</c>.</summary>
    /// <remarks>
    /// ⚠ Returns <c>null</c> until that instance has actually ticked — ⛔ <b>NOT zero</b>. The row
    /// contract treats <c>null</c> as "no source yet ⇒ inert", which is exactly right for an instance
    /// the simulation has never run: it cannot have changed.
    /// </remarks>
    public static ReadAssetTick For(Guid assetId, Entity entity)
    {
        int blueprintId = BlueprintIdHash.Compute(assetId);
        return () => BlueprintAssetTick.Get(blueprintId, entity);
    }

    /// <summary>Turns the counter on for as long as at least one panel needs it.</summary>
    public static void Attach()
    {
        if (System.Threading.Interlocked.Increment(ref _attachCount) == 1)
            BlueprintAssetTick.Enabled = true;
    }

    /// <summary>Turns it off when the last panel goes away.</summary>
    public static void Detach()
    {
        if (System.Threading.Interlocked.Decrement(ref _attachCount) <= 0)
        {
            _attachCount = 0;
            BlueprintAssetTick.Enabled = false;
        }
    }

    /// <summary>Panels currently holding the counter on. ⭐ Asserted by the refcount rail.</summary>
    public static int AttachCount => _attachCount;
}
