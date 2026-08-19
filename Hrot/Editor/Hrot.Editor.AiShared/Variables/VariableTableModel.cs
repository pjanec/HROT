using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐ One repaint's worth of table, fully resolved and free of ImGui. ⭐⭐ <b>Everything §9 asks for is
/// asserted against THIS</b> — grouping, header suppression, qualification, highlight, collapsed
/// inheritance — which is what makes a control whose drawing cannot be tested still have a tested
/// meaning.
/// </summary>
public sealed class VariableTableView
{
    private readonly Dictionary<(Guid, Fdp.Core.Entity, string), RowHighlight> _highlights;
    private readonly Dictionary<(Guid, Fdp.Core.Entity, string), string>       _names;

    internal VariableTableView(
        IReadOnlyList<VariableRow> allRows,
        IReadOnlyList<VariableRowGroup> groups,
        IReadOnlyList<VariableRow> ungroupedRows,
        VariableTableColumns columns,
        Dictionary<(Guid, Fdp.Core.Entity, string), RowHighlight> highlights,
        Dictionary<(Guid, Fdp.Core.Entity, string), string> names,
        VariableValueMode mode,
        string? selectedVariablePath = null)
    {
        AllRows              = allRows;
        Groups               = groups;
        UngroupedRows        = ungroupedRows;
        Columns              = columns;
        _highlights          = highlights;
        _names               = names;
        ValueMode            = mode;
        SelectedVariablePath = selectedVariablePath;
    }

    /// <summary>
    /// ⭐⭐ <b>Which row the OUTLINE selected</b>, or <c>null</c>. 📌
    /// <c>DESIGN_Variable_Details_And_Editing.md</c> §1: <i>"Clicking any row in Local Variables routes
    /// Details to the locals-of-this-graph table <b>with that row highlighted</b>"</i> ⇒ <i>"the
    /// routing key is <c>(asset, section)</c> <b>+ a highlight</b>."</i>
    /// </summary>
    public string? SelectedVariablePath { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>Whether <paramref name="row"/> is the SELECTED one — a state SEPARATE from
    /// <see cref="HighlightOf(VariableRow)"/>.</b>
    ///
    /// <para>⛔⛔ <b>Do NOT express selection through the change highlight.</b> 📌 §1b makes a collapsed
    /// header inherit <b>red if any child changed this tick, yellow if any is pending</b> — those are
    /// statements about the SIMULATION. ⚠ Mixing a selection colour into that aggregate would make the
    /// monitor <b>lie</b>: a header would read "something changed" because the designer clicked.</para>
    ///
    /// <para>⭐ Two orthogonal states, so a selected row that also changed this tick can show both,
    /// and neither can be mistaken for the other.</para>
    /// </summary>
    public bool IsSelected(VariableRow row)
        => SelectedVariablePath != null && row.Origin.VariablePath == SelectedVariablePath;

    public IReadOnlyList<VariableRow>      AllRows       { get; }
    public IReadOnlyList<VariableRowGroup> Groups        { get; }
    /// <summary>Rows outside every group — the whole list when every grouped facet was uniform.</summary>
    public IReadOnlyList<VariableRow>      UngroupedRows { get; }
    public VariableTableColumns            Columns       { get; }

    /// <summary>
    /// ⭐⭐ Which arm the ONE Value column is showing this frame *(row 58, <c>Q32</c> ruling 3)*.
    /// ⛔ Resolved ONCE in <see cref="VariableTableModel.Build"/> through
    /// <see cref="VariableValue.ModeFor"/>, so every cell and tooltip in a frame agrees.
    /// </summary>
    public VariableValueMode ValueMode { get; }

    public RowHighlight HighlightOf(VariableRow row)
        => _highlights.TryGetValue(row.Origin.Key, out var h) ? h : RowHighlight.None;

    /// <summary>⭐ The qualified <c>Name</c> cell text — short when a header or uniformity already
    /// carries the asset, qualified only when nothing does (§1a).</summary>
    public string DisplayNameOf(VariableRow row)
        => _names.TryGetValue(row.Origin.Key, out var n) ? n : row.ShortName;

    /// <summary>⭐⭐⭐ A collapsed header's inherited state (§1b) — red if any descendant changed,
    /// yellow if any is pending.</summary>
    public RowHighlight HighlightOf(VariableRowGroup group)
        => VariableRowGrouping.AggregateHighlight(group, HighlightOf);
}

/// <summary>
/// ⭐⭐⭐ <b>The Details/Watch table, as a model.</b> It holds the source, the one column toggle, the
/// facet list and the highlight cache, and turns them into a <see cref="VariableTableView"/>.
///
/// <para>
/// ⭐ <b>The cache lives HERE, not in the view</b>, because §4a requires it to survive repaints and
/// cover the whole list — <i>"so scrolling does not reset it"</i>. A view is one frame; the monitor is
/// the panel's memory.
/// </para>
/// </summary>
public sealed class VariableTableModel
{
    private readonly VariableChangeMonitor _monitor = new();

    /// <summary>
    /// ⭐⭐⭐ Batch 94 (<c>94c</c>) — this panel's sampler. ⛔ <b>Per MODEL, i.e. per panel</b>, which is
    /// the user's ruling: <i>"watch panel rows are not identical instances to details panel rows…
    /// completely independent on each other."</i> ⭐ Same ownership as <see cref="Monitor"/>, for the
    /// same reason: a view is one frame, the panel is the memory.
    /// </summary>
    private readonly VariableRowSampler _sampler = new();

    public VariableTableModel(IVariableRowSource source, VariableTableColumns columns,
                              IReadOnlyList<VariableFacet>? groupBy = null)
    {
        Source  = source ?? throw new ArgumentNullException(nameof(source));
        Columns = columns;
        GroupBy = groupBy ?? VariableRowGrouping.DetailsDefault;
    }

    public IVariableRowSource            Source   { get; set; }
    public VariableTableColumns          Columns  { get; set; }
    /// <summary>⭐ An ORDERED FACET LIST, not a mode (§1b). Persisted per panel in the editor layout.</summary>
    public IReadOnlyList<VariableFacet>  GroupBy  { get; set; }
    public VariableRunState              RunState { get; set; } = VariableRunState.Planning;

    /// <summary>
    /// ⭐⭐ The outline's selected row, or <c>null</c>. 📌 §1: <i>"the routing key is
    /// <c>(asset, section)</c> <b>+ a highlight</b>."</i> ⛔ Kept OFF the change monitor — see
    /// <see cref="VariableTableView.IsSelected"/> for why that separation is load-bearing.
    /// </summary>
    public string? SelectedVariablePath { get; set; }

    public VariableChangeMonitor Monitor => _monitor;

    /// <summary>⭐ This panel's sampler. ⛔ Exposed for rails only — nothing outside drives it.</summary>
    internal VariableRowSampler Sampler => _sampler;

    public VariableTableView Build()
    {
        // ⭐⭐⭐ Batch 94 (94c) — ONE sample per row per behaviour frame, then everything below draws
        //    from that sample: the cell, the tooltip and the change comparison all see the SAME bytes.
        //    📄 Q46 §2 rule 3: "rendered every UI frame from the cache, without calling the accessor."
        // ⛔ Before this, the arms were invoked once per repaint (60×/s) whether or not the world had
        //    moved — and the monitor invoked them AGAIN, so a cell and its highlight could disagree.
        var rows = _sampler.Sample(Source.GetRows(), RunState);

        var highlights = new Dictionary<(Guid, Fdp.Core.Entity, string), RowHighlight>();
        var names      = new Dictionary<(Guid, Fdp.Core.Entity, string), string>();
        foreach (var row in rows)
        {
            highlights[row.Origin.Key] = _monitor.Observe(row, RunState);
            names[row.Origin.Key]      = VariableRowGrouping.DisplayName(row, rows, GroupBy);
        }

        var groups    = VariableRowGrouping.Group(rows, GroupBy);
        var ungrouped = groups.Count == 0 ? rows : VariableRowGrouping.Ungrouped(rows, GroupBy);

        // ⭐ ONE resolution per frame — ⛔ not per cell, which is how a table ends up half in one
        //   mode and half in the other while the sim starts mid-draw.
        return new VariableTableView(
            rows, groups, ungrouped, Columns, highlights, names, VariableValue.ModeFor(RunState),
            SelectedVariablePath);
    }
}
