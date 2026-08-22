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

        /// <summary>⭐ Batch 97 (97d) — the BINDING generation this entry's baseline belongs to.</summary>
        public uint   AtBinding;
    }

    private readonly Dictionary<(Guid, Entity, string), Entry> _byRow = new();

    /// <summary>
    /// ⭐⭐⭐ Batch 94 (<c>94d</c>) — the managed-value byte bridge. ⛔ One per monitor, i.e. one per
    /// panel, because it owns a pooled buffer and is therefore not thread-safe *(and does not need to
    /// be: a panel samples on its own UI thread)*.
    /// </summary>
    private readonly ManagedValueBytes _managed = new();

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
    /// <param name="staged">
    /// ⭐⭐⭐ <b><c>W4</c> — the SHARED staged set, and the ONLY source of 🟡 yellow.</b>
    /// 📄 <c>DESIGN_Staged_Live_Write.md</c> §4 fork A.
    ///
    /// <para>⚠⚠ <b>Passed per call, not held as a field, and that is the point.</b> This monitor is
    /// <b>per panel</b> *(the user's ruling — a Watch row and a Details row are independent memories)*;
    /// the staged set is <b>shared by the whole editor</b> *(<c>R-120</c>)*. ⛔ Storing it here would
    /// make a shared query look like panel state, which is exactly the confusion <c>I2</c> measured.</para>
    ///
    /// <para>⭐ <c>null</c> ⇒ nothing can be pending, so nothing yellows. ⚠ That is the honest answer for
    /// a host with no staged-write source *(a rail's hand-built model, an authoring-only surface)* —
    /// ⛔ NOT a silent default: there is genuinely no staged set to consult.</para>
    /// </param>
    public RowHighlight Observe(VariableRow row, VariableRunState runState, StagedWriteView? staged = null)
    {
        if (runState == VariableRunState.Planning) return RowHighlight.None;

        // ⭐⭐⭐ W4 — PENDING IS COMPUTED FIRST, and it is NOT gated on the tick or on the byte compare.
        // 🔴 Both guards below exist to stop the RED cache recording a change it cannot later clear —
        //    ⛔ neither has anything to say about a staged edit. A row with no asset tick that the
        //    designer has just edited is still pending, and returning RowHighlight.None for it would
        //    lose the one colour the user asked for by name.
        bool pending = staged?.IsPending(row.Origin) == true;

        // ⛔ No tick source ⇒ the RED half is INERT. Recording without a tick would make the "changed"
        //    state permanent, because nothing could ever advance past LastChangedAssetTick.
        uint? tick = row.AssetTick?.Invoke();
        if (tick is null) return new RowHighlight(false, pending);

        var key = row.Origin.Key;
        if (!_byRow.TryGetValue(key, out var entry))
        {
            _byRow[key] = entry = new Entry();
            entry.AtBinding = EntityBindingFrame.Current;
        }

        // ⭐⭐⭐ Batch 97 (97d) — THE BASELINE FOLLOWS THE BINDING.
        // 🔴🔴 A chameleon row is "this variable, on whoever is selected" (R-78), so selecting another
        //    entity does not CHANGE the value — it changes which value the row is about. ⛔ Comparing
        //    the new entity's value against the old entity's would paint a red "the sim changed it"
        //    the instant the designer clicked, which is 📌 the one thing a monitor must not do.
        // ⭐ Forgetting the baseline is enough: the next block's `!entry.Seen` arm records the new
        //   value WITHOUT calling it a change, which is exactly the first-sighting rule.
        uint binding = EntityBindingFrame.Current;
        if (entry.AtBinding != binding)
        {
            entry.AtBinding      = binding;
            entry.Seen           = false;
            entry.HasEverChanged = false;
        }

        // ⭐⭐ Compare RAW BYTES, not the formatted string: a float moving in its 7th digit renders
        //     identically, and hiding that is exactly what a value monitor must not do.
        // ⭐⭐⭐ Batch 94 (94d) — BOTH ARMS feed the same comparison.
        //    🔴 This used to read only row.ReadValue(), the BYTE arm ⇒ Blueprint's values, which
        //    arrive already-decoded through the OBJECT arm, could never highlight at all.
        //    📄 R-103 (the user): "it produces bytes. we compare these bytes. No way comparing
        //    rendered text!" ⇒ a managed value is serialised by FdpAutoSerializer and the BYTES are
        //    compared — ⛔ never its ToString().
        // ⚠ A type the serializer cannot handle yields null, which means "this row never
        //   highlights" — ⛔ NOT "unchanged", so it must not be folded into the empty-array case.
        byte[]? currentOrNull = ValueBytesOf(row);
        if (currentOrNull is null) return new RowHighlight(false, pending);

        ReadOnlySpan<byte> current = currentOrNull;
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

        // ⭐⭐⭐ W4 — BOTH STAY REPRESENTABLE, and the reason is worth stating because the design's
        //    sentence reads at first like the opposite.
        // 📄 §1: "A row is never red and yellow FOR THE SAME CAUSE — a user edit is yellow, never red."
        //    ⭐ That is honoured UPSTREAM, not here: `changed` is computed from the SAMPLED bytes, and
        //    VariableTableModel.Build applies the staged override only AFTER this call ⇒ a designer's
        //    own edit can never be the thing that sets `changed`.
        // ⛔ Collapsing them here would be a DIFFERENT claim — "the sim moved this while your edit was
        //    still staged" is a real, distinct fact, and 📌 RowHighlight exists precisely so the
        //    renderer, not the monitor, decides which colour wins.
        return new RowHighlight(changed, pending);
    }

    /// <summary>
    /// ⭐⭐ The row's value as bytes, from whichever arm it carries.
    /// ⛔ Returns <c>null</c> when the value cannot be turned into bytes at all — the caller reports
    /// no highlight, which is the honest answer for a value nothing can compare.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>The byte arm wins when it has content.</b> A row that carries real bytes is a struct/
    /// unmanaged value and its bytes ARE the value — 📌 <c>Q46</c> rule 7: any size, no limit.
    /// ⚠ The object arm is consulted only when the byte arm is empty, which is exactly the shape
    /// <c>SectionVariableRowSource</c> builds for a decoded live value.
    /// </remarks>
    private byte[]? ValueBytesOf(VariableRow row)
    {
        byte[] raw;
        try { raw = row.ReadValue().ToArray(); }
        catch { raw = Array.Empty<byte>(); }

        if (raw.Length > 0) return raw;
        if (row.ReadValueObject is not { } readObject) return raw;

        object? value;
        try { value = readObject(); }
        catch { return null; }

        return _managed.TryGetBytes(value);
    }

    // ⛔⛔⛔ W4 — `MarkPending` / `ClearPending` / `IsPending` and `Entry.Pending` are GONE.
    //
    // 📄 DESIGN_Staged_Live_Write.md §4 fork A, verbatim: "⛔ the unwired MarkPending/ClearPending flag
    //    is NOT wired — it is collapsed into the query (R-13: route, don't duplicate)."
    // 📐 Measured before deleting (§2, I3): "0 production callers" — built-but-unwired since Batch 84.
    //    Its only callers were three rails, re-expressed against the shared query in the same commit.
    // ⭐⭐ Why DELETING and not wiring, stated because "keep it, it's harmless" is the tempting answer:
    //    a flag must be CLEARED by whoever applies the write, and 📌 R-126 made the drain a PULL from
    //    the tick loop for exactly the reason that "no path can forget to raise what is never raised."
    //    A flag here would have put the forgettable half straight back in.
    // ⇒ ⭐ StagedWriteView answers "is this row pending?" from the ONE staged set, so the auto-clear IS
    //    the mutation leaving the queue. There is nothing left here to keep in sync — and 📌 R-130
    //    ("pending ⟺ a mutation for this field sits un-drained") becomes true by construction rather
    //    than by every caller remembering to say so.

    /// <summary>Rows tracked. ⭐ Used by the heterogeneous-source rail to prove two rows of the same
    /// asset on two entities occupy two cache slots.</summary>
    public int TrackedRowCount => _byRow.Count;
}
