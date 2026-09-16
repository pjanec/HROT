using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 (<c>94c</c>) — samples each row's accessor ONCE PER BEHAVIOUR FRAME and lets the
/// panel draw from the cache in between.</b>
///
/// <para>📄 <b>Design basis — <c>Q46</c> §2, the user's own specification:</b>
/// <b>rule 2</b> <i>"the accessor is called once per brain frame … all rows are evaluated at the same
/// time, on that one pulse"</i> · <b>rule 3</b> <i>"the value is CACHED on the row and rendered every UI
/// frame from the cache, without calling the accessor"</i> · <b>rule 4a</b> <i>"pin while
/// running-but-PAUSED ⇒ call the accessor immediately"</i> · <b>rule 4b</b> <i>"pin while PLANNING ⇒ do
/// not call it."</i></para>
///
/// <para>⭐⭐⭐ <b>ONE INSTANCE PER PANEL</b> — 📌 the user's ruling, verbatim: <i>"watch panel rows are
/// not identical instances to details panel rows… each completely independent on each other knowing
/// nothing about each other."</i> ⇒ ⛔ <b>never a process-wide cache keyed by
/// <c>(AssetId, Entity, VariablePath)</c></b>, which would couple a Watch row to the Details row of the
/// same variable. ⭐ <b>One implementation of this class, N instances</b> — that is the ruling-9 shape,
/// not a violation of it. <see cref="VariableTableModel"/> owns one, exactly as it already owns its
/// <see cref="VariableChangeMonitor"/>.</para>
///
/// <para>⭐⭐ <b>How the cache reaches the renderer without touching the formatter.</b>
/// <c>VariableRow</c> is an immutable <c>sealed record</c> that Details rebuilds every frame, so
/// <i>"cached on the row"</i> cannot mean a mutable field — it would be discarded on the next rebuild.
/// ⇒ ⭐ the sampler returns each row <b>rewritten</b> so its arms read the cache: a <b>camera</b> row
/// goes in, a <b>per-pulse photograph</b> row comes out. ⛔ The formatter, the monitor and the control
/// are unchanged and all see the SAME sample — which is what makes the cell and the change highlight
/// agree by construction.</para>
///
/// <para>⚠ <b>The pulse is READ, not pushed</b>, and the consequence is deliberate: sampling happens at
/// draw time, so if the UI is slower than the sim the sampler sees the <b>latest</b> value and misses
/// intermediate ones. ⭐ That is correct for a watch panel — ⛔ do not add buffering to chase it.</para>
///
/// <para>⚠ <b>The cache is not pruned</b>, matching <see cref="VariableChangeMonitor"/>'s own rule
/// *(§4a: "covering the whole list, so scrolling does not reset it")*. It is bounded by the distinct
/// row identities a panel has shown.</para>
/// </summary>
public sealed class VariableRowSampler
{
    private sealed class Cell
    {
        public uint?   AtPulse;
        /// <summary>⭐ Batch 97 (97d) — the BINDING generation this sample was taken at.</summary>
        public uint    AtBinding;
        public bool    Taken;
        public byte[]  Bytes = Array.Empty<byte>();
        public object? Object;
        public bool    Written;
    }

    private readonly Dictionary<(Guid, Entity, string), Cell> _byRow = new();

    /// <summary>⭐ How many row identities this panel has sampled. ⛔ Diagnostics only.</summary>
    internal int TrackedRowCount => _byRow.Count;

    /// <summary>
    /// Returns the rows rewritten to read this pulse's cached value.
    /// </summary>
    /// <param name="rows">This frame's rows, straight from the source.</param>
    /// <param name="runState">
    /// ⭐ <b>Planning is not sampled at all</b> *(rule 4b)* — the accessor is never called, and the
    /// rows pass through untouched so the Value column's INITIAL arm still renders the authored
    /// default. ⛔ Not "sampled and discarded".
    /// </param>
    public IReadOnlyList<VariableRow> Sample(
        IReadOnlyList<VariableRow> rows, VariableRunState runState)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        if (runState == VariableRunState.Planning) return rows;

        var result = new List<VariableRow>(rows.Count);
        foreach (var row in rows) result.Add(SampleOne(row));
        return result;
    }

    private VariableRow SampleOne(VariableRow row)
    {
        var key = row.Origin.Key;
        if (!_byRow.TryGetValue(key, out var sample))
            _byRow[key] = sample = new Cell();

        uint? pulse   = row.AssetTick?.Invoke();
        uint  binding = EntityBindingFrame.Current;

        // ⭐⭐ The sample condition, and ALL THREE parts are load-bearing:
        //   • never taken  ⇒ take one NOW. 📌 rule 4a: a row pinned while PAUSED must show its value
        //     immediately, and while paused the pulse does not move — without this clause it would
        //     wait for a resume that may never come.
        //   • the pulse MOVED ⇒ the sim produced new values. 📌 rule 2.
        //   • ⭐⭐⭐ Batch 97 (97d) — the BINDING moved ⇒ this row is now ABOUT A DIFFERENT ENTITY.
        //     📌 R-76's second clock. ⛔ It fires REGARDLESS OF RUN STATE, which is the whole point:
        //     while the debugger holds time the value pulse never moves, so without this a selection
        //     change would show the previous entity's value until the run continued.
        // ⛔ A row with no pulse at all samples exactly once and then holds, which is the honest
        //   answer: nothing can tell it the world changed.
        if (!sample.Taken || pulse != sample.AtPulse || binding != sample.AtBinding)
        {
            byte[] bytes;
            try { bytes = row.ReadValue().ToArray(); }
            catch { bytes = Array.Empty<byte>(); }   // ⭐ a sampler never takes the window down

            object? obj = null;
            if (row.ReadValueObject is { } readObject)
            {
                try { obj = readObject(); }
                catch { obj = null; }
            }

            // ⭐⭐ Batch 94 (94e) — (pending) is sampled on the SAME pulse as the value. ⛔ Reading
            //    it per repaint would break rule 3 as surely as reading the value would, and a cell
            //    whose text and whose "(pending)" disagreed would be worse than either.
            bool written;
            try { written = row.WrittenNow; }
            catch { written = row.HasEverBeenWritten; }

            sample.Bytes   = bytes;
            sample.Object  = obj;
            sample.Written = written;
            sample.AtPulse   = pulse;
            sample.AtBinding = binding;
            sample.Taken     = true;
        }

        // ⭐ The rewritten row. ⛔ `Origin` is untouched, so grouping, the highlight cache and the
        //   selection all key on the same identity they did before.
        byte[]  cachedBytes  = sample.Bytes;
        object? cachedObject = sample.Object;

        bool cachedWritten = sample.Written;

        return row with
        {
            ReadValue       = () => cachedBytes,
            ReadValueObject = row.ReadValueObject is null ? null : () => cachedObject,
            ReadWritten     = () => cachedWritten,
        };
    }
}
