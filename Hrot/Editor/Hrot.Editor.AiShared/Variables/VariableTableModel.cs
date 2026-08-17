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
        VariableValueMode mode)
    {
        AllRows       = allRows;
        Groups        = groups;
        UngroupedRows = ungroupedRows;
        Columns       = columns;
        _highlights   = highlights;
        _names        = names;
        ValueMode     = mode;
    }

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

    public VariableChangeMonitor Monitor => _monitor;

    public VariableTableView Build()
    {
        var rows = Source.GetRows();

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
            rows, groups, ungrouped, Columns, highlights, names, VariableValue.ModeFor(RunState));
    }
}
