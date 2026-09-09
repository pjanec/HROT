using System;
using System.Collections.Concurrent;
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// ⭐⭐⭐ <b><c>C-tick</c> — the per-<c>(asset, entity)</c> tick counter the value monitor needs.</b>
///
/// <para>
/// 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §4a, the ruling verbatim: <i>"a non-frozen CGF
/// behavior tick, i.e. the asset tick/update call."</i> ⛔ <b>NOT the rendered frame. NOT the world
/// tick.</b> The counter advances only when THAT asset's tick actually runs, and only when it is not
/// frozen.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Frozen comes FREE, and that is why the stamp lives inside the tick path.</b>
/// <c>BlueprintTickSystem.Execute</c> opens with <c>if (deltaTime &lt;= 0f) return;</c>, so every call
/// site below is unreachable while the engine is paused. ⇒ paused on a breakpoint the counter does not
/// move, the highlight persists, and it clears when you actually Step — exactly the VS behaviour the
/// ruling asks for. ⛔ A counter stamped anywhere else would have to re-derive "am I frozen".
/// </para>
///
/// <h3>⭐ Where it lives, and the three placements rejected</h3>
/// <list type="table">
///   <item>
///     <term>⛔ <c>BlueprintSlotEntry.InstanceVersion</c></term>
///     <description>It is the <b>latent-cursor staleness token</b> — bumped on hard reload and compared
///     against <c>BlueprintLatentCursor.InstanceVersion</c>. ⛔⛔ <b>A second meaning on one field is
///     the trap this programme keeps finding.</b></description>
///   </item>
///   <item>
///     <term>⛔ a NEW field on <c>BlueprintSlotEntry</c></term>
///     <description>The entry is <b>exactly 16 bytes with a documented budget</b> — <c>StructureHash</c>
///     is already <i>"truncated from ulong to fit the 16-byte slot-entry budget"</i>. Growing it shrinks
///     usable payload in <b>every</b> tier and moves the tier-fit arithmetic, ⚠ for a counter no
///     simulation code reads. It would also enter the recorded snapshot
///     (<c>[DataPolicy(NoSave)]</c> means snapshotted and recorded).</description>
///   </item>
///   <item>
///     <term>⛔ <c>BlueprintBlackboardHeader.Reserved</c></term>
///     <description>Wrong granularity: the header is per <b>entity-tier</b>, and one entity hosts many
///     slots. The ruling wants per <c>(asset, entity)</c> — <i>"the same asset on two entities ticks
///     independently"</i>.</description>
///   </item>
/// </list>
///
/// <para>
/// ⇒ ⭐ <b>a side table, owned here.</b> The counter is <b>editor telemetry, not simulation state</b>:
/// nothing in the sim reads it, so it should cost the sim nothing and must not appear in a recorded
/// frame. ⭐⭐ <b>That also makes it structurally impossible for this item to move
/// <c>StructureHash</c> or <c>persistence-shape</c></b> — it adds no byte to any persisted or
/// snapshotted layout.
/// </para>
///
/// <para>
/// ⚠ <b>OPT-IN, default OFF.</b> The tick loop runs every instance every frame; a dictionary write per
/// instance per frame is not something to add to a shipping build for a panel nobody has open. ⛔ When
/// <see cref="Enabled"/> is false <see cref="Bump"/> is a single static bool read and returns. ⭐ The
/// editor turns it on when a variable table is showing.
/// </para>
/// </summary>
public static class BlueprintAssetTick
{
    private static readonly ConcurrentDictionary<(int BlueprintId, Entity Entity), uint> Counters = new();

    // ⭐ Cached so AddOrUpdate allocates no closure per call. The hot path must stay allocation-free
    //   even when enabled -- the allocation-trait tests would see it otherwise.
    private static readonly Func<(int, Entity), uint, uint> Increment = static (_, v) => unchecked(v + 1);

    /// <summary>
    /// ⭐ Whether the counter is maintained. ⛔ <b>Default <c>false</c></b> — see the type remarks.
    /// ⚠ Turning it off does not clear what was already counted; a row's highlight simply stops
    /// advancing, which is the same inert state a row with no source has.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Records one non-frozen tick of <paramref name="blueprintId"/> on <paramref name="entity"/>.
    /// ⛔ Call only from inside the tick path, below the <c>deltaTime &lt;= 0f</c> guard.
    /// </summary>
    public static void Bump(int blueprintId, Entity entity)
    {
        if (!Enabled) return;
        Counters.AddOrUpdate((blueprintId, entity), 1u, Increment);
    }

    /// <summary>
    /// The current tick for one <c>(asset, entity)</c>, or <c>null</c> when nothing has ticked it yet.
    ///
    /// <para>
    /// ⭐ <b><c>null</c> is meaningful and is NOT "tick zero"</b> — it flows straight into
    /// <c>VariableRow.AssetTick</c>, whose contract is that a row with no tick source is <b>inert
    /// rather than wrong</b>. ⇒ an asset that has never ticked reports no highlight instead of
    /// pretending it just changed.
    /// </para>
    /// </summary>
    public static uint? Get(int blueprintId, Entity entity)
        => Counters.TryGetValue((blueprintId, entity), out var v) ? v : null;

    /// <summary>Test/host hook: forget everything. ⚠ Not called by the tick path.</summary>
    public static void Reset()
    {
        Counters.Clear();
        Enabled = false;
    }

    /// <summary>Instances currently counted. ⭐ Used by the rails to prove two entities are tracked
    /// separately rather than sharing one slot.</summary>
    public static int TrackedInstanceCount => Counters.Count;
}
