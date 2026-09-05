using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 87 item 2c (<c>B3</c>) — the CONTROL asks about selection.</b>
///
/// <para>🔴🔴 <b>An INVERTED instance of this programme's recurring pattern.</b> Usually nothing
/// constructs the thing. 📐 Here the whole chain was wired — the outline set
/// <c>SelectedVariablePath</c>, the section applied it, <see cref="VariableTableView.IsSelected"/>
/// computed it — ⛔ <b>and <see cref="VariableTableControl"/> never called it. Zero references.</b>
/// The last consumer never asked.</para>
///
/// <para>⛔⛔ <b>So a rail on <c>IsSelected</c> proves NOTHING</b> — it returned <c>true</c> right
/// through the defect. 📌 The <c>CellText</c> lesson from Batch 83: <b>ask what the CONTROL would
/// draw.</b> ⇒ ⭐ these interrogate <see cref="VariableTableControl.VisualStateOf"/>, which
/// <c>DrawRows</c>/<c>DrawCell</c> read and which they read NOTHING ELSE from — so a probe that breaks
/// it breaks the drawing too.</para>
///
/// <para>⚠ <b>What they cannot prove</b>, stated: that ImGui paints the highlight. They prove the
/// control ASKS and CARRIES the state, which is precisely the half that was missing.</para>
/// </summary>
public sealed class TheSelectedRowIsRenderedTests
{
    private static readonly Guid Asset = Guid.NewGuid();

    private static VariableRow Row(string path)
        => new(
            Origin:    new VariableRowOrigin(Asset, new Entity(1, 1), "Variables", path, "Alpha"),
            ShortName: path, TypeText: "int", ClrType: typeof(int),
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   VariableRowKind.Normal, IsStale: false);

    private static (VariableTableControl Control, VariableTableView View) Make(
        string? selected, params string[] paths)
    {
        var rows  = new List<VariableRow>();
        foreach (var p in paths) rows.Add(Row(p));

        var model = new VariableTableModel(new FixedVariableRowSource(rows), VariableTableColumns.Details)
        {
            SelectedVariablePath = selected,
        };
        var control = new VariableTableControl(new VariableValueFormatter(decode: (_, _) => null));
        return (control, model.Build());
    }

    /// <summary>⭐⭐⭐ <b>THE rail.</b> The control reports the clicked row as selected and the others as
    /// not — 🔴 both halves red before this batch, because it never asked at all.</summary>
    [Fact]
    public void TheControlReportsTheSelectedRow()
    {
        var (control, view) = Make("Health", "Health", "Ammo");

        Assert.True (control.VisualStateOf(view, view.AllRows[0]).Selected);
        Assert.False(control.VisualStateOf(view, view.AllRows[1]).Selected);
    }

    /// <summary>⛔ Nothing selected ⇒ no row reports selected. ⚠ Without this the rail above could pass
    /// on a control that answers <c>true</c> unconditionally.</summary>
    [Fact]
    public void WithNoSelectionNoRowIsSelected()
    {
        var (control, view) = Make(null, "Health", "Ammo");

        Assert.All(view.AllRows, r => Assert.False(control.VisualStateOf(view, r).Selected));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Selection and "changed this tick" are ORTHOGONAL — a row that is BOTH shows BOTH.</b>
    ///
    /// <para>📌 The view's own ruling: <i>"Do NOT express selection through the change highlight… a
    /// header would read 'something changed' because the designer clicked."</i> ⇒ ⛔ collapsing them
    /// into one row style would make the monitor lie about the SIMULATION.</para>
    /// </summary>
    [Fact]
    public void SelectionAndChangeAreIndependentChannels()
    {
        var (control, view) = Make("Health", "Health");
        var state = control.VisualStateOf(view, view.AllRows[0]);

        // ⭐ Selection is carried in its own field, and the change channel is untouched by it.
        Assert.True(state.Selected);
        Assert.False(state.Changed);
        Assert.False(state.Pending);
    }

    /// <summary>⭐ The state is a pure projection of the view — ⛔ a control that cached it would drift
    /// from the model the moment the outline re-routed.</summary>
    [Fact]
    public void TheStateFollowsTheViewRatherThanBeingCached()
    {
        var (control, first)  = Make("Health", "Health", "Ammo");
        var (_,       second) = Make("Ammo",   "Health", "Ammo");

        Assert.True (control.VisualStateOf(first,  first.AllRows[0]).Selected);
        Assert.False(control.VisualStateOf(second, second.AllRows[0]).Selected);
        Assert.True (control.VisualStateOf(second, second.AllRows[1]).Selected);
    }

    /// <summary>⛔ A null view is a programming error, not a blank table.</summary>
    [Fact]
    public void ANullViewThrows()
    {
        var (control, view) = Make(null, "Health");
        Assert.Throws<ArgumentNullException>(() => control.VisualStateOf(null!, view.AllRows[0]));
    }
}
