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
