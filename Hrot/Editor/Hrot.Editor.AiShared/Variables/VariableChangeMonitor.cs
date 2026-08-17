using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>§5 — the run state, which governs everything.</summary>
public enum VariableRunState
{
    Planning,
    Running,
    Paused,
    Replay,
}

/// <summary>
/// ⭐⭐ <b>Two DISTINCT states, never one flag (§4a).</b> 🔴 red = <i>the sim changed it</i>;
/// 🟡 yellow = <i>your optimistic edit has not landed yet</i>. ⛔ Collapsing them would make
/// <i>"the sim changed this"</i> and <i>"my edit is still pending"</i> indistinguishable, which is the
/// one thing a monitor must not do — so this carries both booleans and lets the renderer choose.
/// </summary>
public readonly record struct RowHighlight(bool Changed, bool Pending)
{
    public static RowHighlight None => default;
    public bool Any => Changed || Pending;
}

/// <summary>
/// ⭐⭐⭐ <b>The change-highlight cache and its predicate (§4a).</b>
///
/// <para>
/// ⭐ <b>VS-debugger behaviour:</b> a value that changed is drawn red for one step, then returns to
/// normal. ⛔ Non-planning modes only.
/// </para>
///
/// <para>
/// ⭐⭐⭐ <b>The unit is the ASSET's own tick, not the world tick</b> — <i>"a non-frozen CGF behavior
/// tick, i.e. the asset tick/update call."</i> ⇒ paused on a breakpoint the highlight <b>PERSISTS</b>
/// (behaviours do not tick while frozen, so nothing has happened) and clears when you actually Step.
/// A world-tick counter would wrongly clear it — which is why <see cref="VariableRow.AssetTick"/> is
/// per-row and nullable, and why this type never reads a clock of its own.
/// </para>
///
/// <para>
/// 🔴🔴 <b>MEASURED, Batch 68 — no per-asset tick exists in this codebase.</b>
/// <c>BlueprintDebugSession:1543</c> reads <c>_view.Tick</c> ⇒ <c>ISimulationView.Tick</c>
/// (<i>"current simulation tick (frame number)"</i>) ⇒ <c>EntityRepository.SimulationTick</c>, the
/// <i>"semantic frame clock, incremented only by <c>Tick()</c>"</i> — <b>per WORLD, not per asset</b>;
/// and <c>BlueprintTickSystem</c> stamps no per-instance counter anywhere. ⇒ ⛔ <b>a row whose
/// <c>AssetTick</c> is <c>null</c> is reported <see cref="RowHighlight.None"/> and is not even
/// recorded</b> — inert, never wrong. ⭐ The predicate below is complete and tested; it is waiting on a
/// tick source, not on logic.
/// </para>
///
/// <para>
/// ⭐ <b>Ownership (§4a):</b> ⛔ <b>not the breakpoint snapshots</b> — <c>_preTickSnapshot</c>/
/// <c>_postTickSnapshot</c> exist only while the debugger is engaged, and the monitor must work when it
/// is not. ⇒ the shared row renderer owns this cache, keyed by <c>(AssetId, Entity, VariablePath)</c>
/// and covering the whole list, so scrolling does not reset it.
/// </para>
/// </summary>
public sealed class VariableChangeMonitor
{
    private sealed class Entry
    {
        public byte[] LastValue = Array.Empty<byte>();
        public uint   LastChangedAssetTick;
        public bool   Seen;
        public bool   HasEverChanged;
        public bool   Pending;
    }

    private readonly Dictionary<(Guid, Entity, string), Entry> _byRow = new();

    /// <summary>
    /// ⭐ <b>Observe one row and return its highlight.</b> Call once per row per repaint; the cache is
    /// keyed by identity, so the answer does not depend on scroll position or row order.
    ///
    /// <para>
    /// ⛔ <b>Planning never highlights</b> (§5) — and it does not merely suppress the colour, it does
    /// not record either: entering Running must not light up every row because the first observation
    /// happened under a different mode.
    /// </para>
    /// </summary>
    public RowHighlight Observe(VariableRow row, VariableRunState runState)
    {
        if (runState == VariableRunState.Planning) return RowHighlight.None;

        // ⛔ No tick source ⇒ INERT. Recording without a tick would make the "changed" state permanent,
        //    because nothing could ever advance past LastChangedAssetTick.
        uint? tick = row.AssetTick?.Invoke();
        if (tick is null) return RowHighlight.None;

        var key = row.Origin.Key;
        if (!_byRow.TryGetValue(key, out var entry))
            _byRow[key] = entry = new Entry();

        // ⭐⭐ Compare RAW BYTES, not the formatted string: a float moving in its 7th digit renders
        //     identically, and hiding that is exactly what a value monitor must not do.
        var current = row.ReadValue();
        if (!entry.Seen)
        {
            // ⭐ First sighting: remember the value, but do NOT call it a change -- there is nothing
            //   for it to have changed FROM, and opening a panel must not light every row.
            entry.Seen      = true;
            entry.LastValue = current.ToArray();
        }
        else if (!current.SequenceEqual(entry.LastValue))
        {
            entry.LastValue            = current.ToArray();
            entry.LastChangedAssetTick = tick.Value;
            entry.HasEverChanged       = true;
        }

        // ⭐ The predicate, and it is the whole rule: highlighted while this row's asset tick still
        //   equals the tick on which it last changed. Frozen (no asset tick) ⇒ still equal ⇒ still red.
        bool changed = entry.HasEverChanged && tick.Value == entry.LastChangedAssetTick;
        return new RowHighlight(changed, entry.Pending);
    }

    /// <summary>§6 — optimistic display: the edit is painted immediately and staged; this marks the row
    /// yellow until the staged write lands at the N+1 boundary.</summary>
    public void MarkPending(VariableRowOrigin origin)
    {
        var key = origin.Key;
        if (!_byRow.TryGetValue(key, out var entry)) _byRow[key] = entry = new Entry();
        entry.Pending = true;
    }

    /// <summary>Clears the pending flag when the staged write lands.</summary>
    public void ClearPending(VariableRowOrigin origin)
    {
        if (_byRow.TryGetValue(origin.Key, out var entry)) entry.Pending = false;
    }

    /// <summary>Test/diagnostic read of the current state without observing.</summary>
    public bool IsPending(VariableRowOrigin origin)
        => _byRow.TryGetValue(origin.Key, out var e) && e.Pending;

    /// <summary>Rows tracked. ⭐ Used by the heterogeneous-source rail to prove two rows of the same
    /// asset on two entities occupy two cache slots.</summary>
    public int TrackedRowCount => _byRow.Count;
}
