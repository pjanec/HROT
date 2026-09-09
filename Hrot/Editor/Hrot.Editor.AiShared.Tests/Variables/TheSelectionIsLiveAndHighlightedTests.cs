using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 84 item 4 — the two routing defects from the user's visual check.</b>
///
/// <para>📌 <b><c>DESIGN_Variable_Details_And_Editing.md</c> §1, verbatim:</b> <i>"Clicking any row in
/// <i>Local Variables</i> routes Details to the locals-of-this-graph table <b>with that row
/// highlighted</b>"</i> ⇒ <i>"the routing key is <c>(asset, section)</c> <b>+ a highlight</b>."</i></para>
///
/// <para>🔴 <b><c>4a</c> — the TYPE could not express it.</b> <c>VariableOutlineSelection</c> carried a
/// heading and a source, ⛔ and no clicked-row identity, so no panel could highlight anything however
/// it drew.</para>
///
/// <para>🔴 <b><c>4b</c> — and the handoff's premise was HALF right.</b> 📐 Measured: the graph-scoped
/// arm's <b>ROWS already follow the canvas</b> — <c>BlueprintLocalVariableSchemaSource</c> resolves the
/// graph through a <c>Func&lt;Graph?&gt;</c> at call time. ⛔ <b>The HEADING did not:</b>
/// <c>$"Local Variables — {graph.Name}"</c> was computed once, at click time. ⇒ ⚠⚠ <b>the symptom is
/// worse than "Details does not follow the graph"</b> — the rows updated and the label did not, so the
/// panel <b>contradicted itself</b>.</para>
/// </summary>
public sealed class TheSelectionIsLiveAndHighlightedTests
{
    // ══ 4a — selection is a state, SEPARATE from the change highlight ════════

    /// <summary>
    /// 🔴 <b>RED before Batch 84</b> — the record had nowhere to put this.
    /// </summary>
    [Fact]
    public void TheSelectionCarriesTheClickedRow()
    {
        var selection = new VariableOutlineSelection(
            "Local Variables", Source("Health", "Ammo"), SelectedVariablePath: "Ammo");

        var section = Section();
        section.Show(selection);

        var view = section.Model.Build();
        Assert.False(view.IsSelected(view.AllRows[0]));
        Assert.True(view.IsSelected(view.AllRows[1]));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Selection does NOT touch the change highlight.</b>
    ///
    /// <para>⛔⛔ 📌 §1b makes a collapsed header inherit <b>red if any child changed this tick, yellow
    /// if any is pending</b> — statements about the SIMULATION. ⚠ Overloading that for selection would
    /// make the monitor <b>lie</b>: a header would read "something changed" because the designer
    /// clicked a row.</para>
    /// </summary>
    [Fact]
    public void SelectingARow_DoesNotChangeItsChangeHighlight()
    {
        var section = Section();

        section.Show(new VariableOutlineSelection("Vars", Source("Health")));
        var before = section.Model.Build();
        var unselected = before.HighlightOf(before.AllRows[0]);

        section.Show(new VariableOutlineSelection("Vars", Source("Health"), SelectedVariablePath: "Health"));
        var after = section.Model.Build();

        Assert.True(after.IsSelected(after.AllRows[0]));
        Assert.Equal(unselected, after.HighlightOf(after.AllRows[0]));
    }

    /// <summary>⭐ No selection ⇒ nothing is selected. ⛔ Not "the first row", which is a guess.</summary>
    [Fact]
    public void WithNoSelectedPath_NoRowIsSelected()
    {
        var section = Section();
        section.Show(new VariableOutlineSelection("Vars", Source("Health", "Ammo")));

        var view = section.Model.Build();
        Assert.All(view.AllRows, r => Assert.False(view.IsSelected(r)));
    }

    /// <summary>⭐ Letting go of the list lets go of the selection too — ⛔ no ghost highlight.</summary>
    [Fact]
    public void Clearing_DropsTheSelection()
    {
        var section = Section();
        section.Show(new VariableOutlineSelection("Vars", Source("Health"), SelectedVariablePath: "Health"));

        section.Clear();

        Assert.Null(section.SelectedVariablePath);
    }

    // ══ 4b — the heading follows the canvas ══════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before Batch 84.</b> The graph-scoped heading was a click-time snapshot, so
    /// switching graph left the label naming the OLD graph while the rows showed the NEW one.
    /// </summary>
    [Fact]
    public void TheGraphScopedHeading_FollowsTheCanvas()
    {
        var graph   = "Tick";
        var section = Section();

        section.Show(new VariableOutlineSelection(
            $"Local Variables — {graph}", Source("Health"),
            HeadingAtReadTime: () => $"Local Variables — {graph}"));

        Assert.Equal("Local Variables — Tick", section.Heading);

        graph = "OnDamage";        // …the designer switches graph

        Assert.Equal("Local Variables — OnDamage", section.Heading);
    }

    /// <summary>
    /// ⭐ <b>An asset-scoped heading needs no delegate and keeps its click-time text.</b> ⛔ Item 4b is
    /// about the GRAPH-scoped arm only: a section's name does not depend on the canvas, so making it
    /// live would be machinery with nothing to follow.
    /// </summary>
    [Fact]
    public void AnAssetScopedHeading_StaysAsGiven()
    {
        var section = Section();
        section.Show(new VariableOutlineSelection("Variables", Source("Health")));

        Assert.Equal("Variables", section.Heading);
    }

    /// <summary>⭐ And <c>CurrentHeading</c> on the record itself agrees with what the panel shows.</summary>
    [Fact]
    public void CurrentHeading_PrefersTheLiveOne()
    {
        Assert.Equal("live", new VariableOutlineSelection(
            "stale", Source("Health"), HeadingAtReadTime: () => "live").CurrentHeading);

        Assert.Equal("stale", new VariableOutlineSelection("stale", Source("Health")).CurrentHeading);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static VariableDetailsSection Section()
        => new(new VariableValueFormatter((_, __) => null));

    private static IVariableRowSource Source(params string[] names)
    {
        var rows = new List<VariableRow>();
        foreach (var n in names)
            rows.Add(new VariableRow(
                Origin:    new VariableRowOrigin(Guid.Empty, default, "locals", n, "Asset"),
                ShortName: n,
                TypeText:  "int",
                ClrType:   typeof(int),
                ReadValue: () => Array.Empty<byte>()));
        return new FixedVariableRowSource(rows);
    }
}
