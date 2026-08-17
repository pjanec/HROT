using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>C-table</c> — <c>DESIGN_Variable_Details_And_Editing.md</c> §9's rails.</b>
///
/// <para>
/// ⚠⚠ <b>What these tests deliberately do NOT cover, because nothing headless can:</b> the table
/// DRAWING, the double-click gestures, and the planning-only byte-budget indicator. §9 lists them as
/// <i>"visual check required"</i>, and the visual check is suspended. ⇒ ⭐ <b>what is asserted here is
/// the table's MEANING</b> — which rows exist, what they are called, how they nest, and which of them
/// is highlighted.
/// </para>
/// </summary>
public sealed class VariableTableRailsTests
{
    // ── fixtures ────────────────────────────────────────────────────────────────

    private static readonly Guid AssetA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AssetB = new("bbbbbbbb-0000-0000-0000-000000000002");

    private static Entity Ent(int index) => new Entity(index, 1);

    private sealed class Cell
    {
        public byte[] Bytes = Array.Empty<byte>();
        public uint?  Tick  = 0;
    }

    private static (VariableRow Row, Cell Cell) MakeRow(
        Guid assetId, string assetName, Entity entity, string name,
        Type? clrType = null, string section = "Variables",
        VariableRowKind kind = VariableRowKind.Normal, bool stale = false,
        bool hasEverBeenWritten = true)
    {
        var cell = new Cell();
        var row = new VariableRow(
            Origin:    new VariableRowOrigin(assetId, entity, section, name, assetName),
            ShortName: name,
            TypeText:  (clrType ?? typeof(int)).Name,
            ClrType:   clrType ?? typeof(int),
            ReadValue: () => cell.Bytes,
            AssetTick: () => cell.Tick,
            RowKind:   kind,
            IsStale:   stale,
            HasEverBeenWritten: hasEverBeenWritten);
        return (row, cell);
    }

    private static byte[] I32(int v) { var b = new byte[4]; MemoryMarshal.Write(b, in v); return b; }
    private static byte[] F32(float v) { var b = new byte[4]; MemoryMarshal.Write(b, in v); return b; }

    private static VariableTableModel Model(
        IEnumerable<VariableRow> rows, VariableTableColumns? columns = null,
        IReadOnlyList<VariableFacet>? groupBy = null,
        VariableRunState runState = VariableRunState.Running)
        => new(new FixedVariableRowSource(rows.ToList()),
               columns ?? VariableTableColumns.Details, groupBy) { RunState = runState };

    // ── §9 · the column set ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The visible set is a subset of <c>{Name, Type, Value}</c> with <c>Name</c> and
    /// <c>Value</c> mandatory.</b>
    ///
    /// <para>
    /// ⛔ <b>The rail is enforced by the TYPE, not by this test</b>, and that is the point worth
    /// recording: <c>VariableTableColumns</c> is a struct with ONE bool, so there is no expression a
    /// caller can write that names a fourth column. §1's reasoning is explicit — <i>"seven columns is
    /// what we are escaping; a configurable system is how it grows back"</i>. ⇒ this test guards the
    /// two DEFAULTS and the mandatory pair; the absence of a framework is guarded by there not being
    /// one.
    /// </para>
    /// </summary>
    [Fact]
    public void ColumnSet_IsAlwaysNameAndValue_WithTypeTheOnlyToggle()
    {
        Assert.True(VariableTableColumns.Details.IsValid);
        Assert.True(VariableTableColumns.Watch.IsValid);

        // Details is authoring -- you pick types there.
        Assert.Contains(VariableColumn.Type, VariableTableColumns.Details.Visible);
        // Watch is monitoring -- the user's own words: "not even the data type is important".
        Assert.DoesNotContain(VariableColumn.Type, VariableTableColumns.Watch.Visible);

        foreach (var set in new[] { VariableTableColumns.Details, VariableTableColumns.Watch })
        {
            Assert.Contains(VariableColumn.Name,  set.Visible);
            Assert.Contains(VariableColumn.Value, set.Visible);
            Assert.All(set.Visible, c => Assert.True(
                c is VariableColumn.Name or VariableColumn.Value or VariableColumn.Type));
        }
    }

    // ── §9 · THE heterogeneous-source rail ──────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The rail that matters most (§9): the control must not quietly assume ONE asset.</b>
    ///
    /// <para>
    /// Two different assets AND the same asset on two entities, in one list. ⇒ distinct identities ·
    /// independent highlight state · a stale row that renders but refuses its dialog.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>The half that would break a naive control is the SAME asset on TWO entities</b> — an
    /// implementation keyed by <c>(asset, variable)</c> passes every other test in this file and
    /// collapses these two rows onto one cache slot, so one entity's change lights the other's row.
    /// ⇒ §1a: <b>entity is PART of identity</b>.
    /// </para>
    /// </summary>
    [Fact]
    public void HeterogeneousSource_KeepsIdentitiesAndHighlightsIndependent()
    {
        var (a1, cellA1) = MakeRow(AssetA, "Alpha", Ent(1), "Health");
        var (a2, cellA2) = MakeRow(AssetA, "Alpha", Ent(2), "Health");   // same asset, other entity
        var (b1, cellB1) = MakeRow(AssetB, "Bravo", Ent(1), "Health");   // other asset, same entity
        var (stale, _)   = MakeRow(AssetB, "Bravo", Ent(9), "Ghost", stale: true);

        foreach (var c in new[] { cellA1, cellA2, cellB1 }) c.Bytes = I32(1);

        var model = Model(new[] { a1, a2, b1, stale });
        model.Build();                                   // seed

        // ⭐ Only ONE of the three moves.
        cellA1.Bytes = I32(2);
        cellA1.Tick  = 1;
        var view = model.Build();

        Assert.True (view.HighlightOf(a1).Changed);
        Assert.False(view.HighlightOf(a2).Changed);      // 🔴 same asset, different entity
        Assert.False(view.HighlightOf(b1).Changed);

        // Distinct identities: four rows, four cache slots.
        Assert.Equal(4, new[] { a1, a2, b1, stale }.Select(r => r.Origin.Key).Distinct().Count());

        // ⭐ The stale row still renders...
        Assert.Contains(stale, view.AllRows);
        // ...and refuses its dialog.
        Assert.False(stale.CanEverBeWritten);
    }

    /// <summary>⭐ Row kinds that never get a writable dialog, in either mode (§5) — proven by asking.</summary>
    [Theory]
    [InlineData(VariableRowKind.ReadOnlyPassthrough)]
    [InlineData(VariableRowKind.NodeOwned)]
    public void ReadOnlyAndNodeOwnedRows_NeverGetAWritableDialog(VariableRowKind kind)
    {
        var (row, _) = MakeRow(AssetA, "Alpha", Ent(1), "Locked", kind: kind);
        Assert.False(row.CanEverBeWritten);
    }

    // ── §9 · grouping ───────────────────────────────────────────────────────────

    /// <summary>⭐ <c>GroupBy = [Asset, Entity]</c> over a mixed list ⇒ correct nesting and membership.</summary>
    [Fact]
    public void GroupByAssetThenEntity_NestsCorrectly()
    {
        var (a1, _) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        var (a2, _) = MakeRow(AssetA, "Alpha", Ent(2), "H");
        var (b1, _) = MakeRow(AssetB, "Bravo", Ent(1), "H");

        var view = Model(new[] { a1, a2, b1 }, groupBy: VariableRowGrouping.WatchDefault).Build();

        Assert.Equal(2, view.Groups.Count);
        Assert.All(view.Groups, g => Assert.Equal(VariableFacet.Asset, g.Facet));

        var alpha = view.Groups.Single(g => g.Header == "Alpha");
        Assert.Equal(2, alpha.Children.Count);                       // two entities
        Assert.All(alpha.Children, c => Assert.Equal(VariableFacet.Entity, c.Facet));

        var bravo = view.Groups.Single(g => g.Header == "Bravo");
        Assert.Empty(bravo.Children);                                // one entity ⇒ uniform ⇒ no header
        Assert.Single(bravo.Rows);
    }

    /// <summary>
    /// ⭐⭐ <b>A UNIFORM facet emits NO header</b> — watching one asset produces no asset group, by
    /// itself. ⛔ No setting, no special mode, no pointless single group. ⭐ This is what lets
    /// <c>[Asset, Entity]</c> be a sensible Watch DEFAULT instead of something to switch off.
    /// </summary>
    [Fact]
    public void AUniformFacet_EmitsNoHeader()
    {
        var (a1, _) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        var (a2, _) = MakeRow(AssetA, "Alpha", Ent(1), "S");         // one asset, one entity

        var view = Model(new[] { a1, a2 }, groupBy: VariableRowGrouping.WatchDefault).Build();

        Assert.Empty(view.Groups);
        Assert.Equal(2, view.UngroupedRows.Count);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A COLLAPSED header inherits its children's state.</b> ⛔ Without this, folding only
    /// hides — it does not help a monitor. With it you fold everything and can still see WHERE the
    /// activity is.
    /// </summary>
    [Fact]
    public void ACollapsedGroup_ReportsRedIfAnyChild_ChangedAndYellowIfAnyIsPending()
    {
        var (a1, cellA1) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        var (b1, cellB1) = MakeRow(AssetB, "Bravo", Ent(1), "H");
        cellA1.Bytes = I32(1); cellB1.Bytes = I32(1);

        var model = Model(new[] { a1, b1 }, groupBy: new[] { VariableFacet.Asset });
        model.Build();

        cellA1.Bytes = I32(7); cellA1.Tick = 1;
        model.Monitor.MarkPending(b1.Origin);
        var view = model.Build();

        var alpha = view.Groups.Single(g => g.Header == "Alpha");
        var bravo = view.Groups.Single(g => g.Header == "Bravo");

        Assert.True (view.HighlightOf(alpha).Changed);
        Assert.False(view.HighlightOf(alpha).Pending);
        Assert.True (view.HighlightOf(bravo).Pending);
        Assert.False(view.HighlightOf(bravo).Changed);
    }

    // ── §1a · qualification ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The CONTROL qualifies, and only what grouping has not already hoisted.</b> ⛔ An earlier
    /// draft put qualification in the source's display name, so every row would repeat
    /// <c>Asset.Var</c> even when a header already said it.
    /// </summary>
    [Fact]
    public void QualificationHappensOnlyWhenNothingElseCarriesTheAsset()
    {
        var (a1, _) = MakeRow(AssetA, "Alpha", Ent(1), "Health");
        var (b1, _) = MakeRow(AssetB, "Bravo", Ent(1), "Health");

        // heterogeneous + ungrouped ⇒ nothing else carries it
        var ungrouped = Model(new[] { a1, b1 }).Build();
        Assert.Equal("Alpha.Health", ungrouped.DisplayNameOf(a1));

        // grouped by asset ⇒ the header carries it
        var grouped = Model(new[] { a1, b1 }, groupBy: new[] { VariableFacet.Asset }).Build();
        Assert.Equal("Health", grouped.DisplayNameOf(a1));

        // one asset ⇒ nothing to disambiguate
        var (a2, _) = MakeRow(AssetA, "Alpha", Ent(1), "Speed");
        var single  = Model(new[] { a1, a2 }).Build();
        Assert.Equal("Health", single.DisplayNameOf(a1));
    }

    // ── §9 · change highlight ───────────────────────────────────────────────────

    /// <summary>⭐ Changed ⇒ true for one asset tick, false on the next.</summary>
    [Fact]
    public void Changed_IsTrueForOneAssetTick_ThenFalse()
    {
        var (row, cell) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        cell.Bytes = I32(1);
        var model = Model(new[] { row });
        model.Build();

        cell.Bytes = I32(2); cell.Tick = 1;
        Assert.True(model.Build().HighlightOf(row).Changed);

        cell.Tick = 2;                                   // the asset ticked; nothing changed
        Assert.False(model.Build().HighlightOf(row).Changed);
    }

    /// <summary>⭐ Unchanged ⇒ never highlighted, no matter how many asset ticks pass.</summary>
    [Fact]
    public void Unchanged_IsNeverHighlighted()
    {
        var (row, cell) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        cell.Bytes = I32(5);
        var model = Model(new[] { row });

        for (uint t = 0; t < 5; t++)
        {
            cell.Tick = t;
            Assert.False(model.Build().HighlightOf(row).Changed);
        }
    }

    /// <summary>⛔ Planning ⇒ never highlighted (§5).</summary>
    [Fact]
    public void Planning_NeverHighlights()
    {
        var (row, cell) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        cell.Bytes = I32(1);
        var model = Model(new[] { row }, runState: VariableRunState.Planning);
        model.Build();

        cell.Bytes = I32(2); cell.Tick = 1;
        Assert.False(model.Build().HighlightOf(row).Changed);
    }

    /// <summary>
    /// ⭐⭐ <b>Format-equal but byte-different must be TRUE — the float-7th-digit case.</b>
    /// ⛔ This is precisely why §4a diffs RAW BYTES: both values render as <c>1.0</c>, and a formatted
    /// comparison would call the change invisible.
    /// </summary>
    [Fact]
    public void FormatEqualButByteDifferent_IsStillAChange()
    {
        const float a = 1.0000000f;
        const float b = 1.0000001f;
        Assert.Equal(a.ToString("0.###"), b.ToString("0.###"));      // identical to the eye
        Assert.NotEqual(BitConverter.SingleToInt32Bits(a), BitConverter.SingleToInt32Bits(b));

        var (row, cell) = MakeRow(AssetA, "Alpha", Ent(1), "F", typeof(float));
        cell.Bytes = F32(a);
        var model = Model(new[] { row });
        model.Build();

        cell.Bytes = F32(b); cell.Tick = 1;
        Assert.True(model.Build().HighlightOf(row).Changed);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Frozen: N world frames with NO asset tick ⇒ STILL highlighted.</b> This is the ruling's
    /// whole point — paused on a breakpoint behaviours do not tick, so nothing has happened, and the
    /// red must survive until you actually Step. ⛔ A world-tick counter would clear it here.
    /// </summary>
    [Fact]
    public void Frozen_ManyWorldFramesWithNoAssetTick_StaysHighlighted()
    {
        var (row, cell) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        cell.Bytes = I32(1);
        var model = Model(new[] { row });
        model.Build();

        cell.Bytes = I32(2); cell.Tick = 7;
        Assert.True(model.Build().HighlightOf(row).Changed);

        // 100 repaints, 100 world frames, ZERO asset ticks.
        for (int i = 0; i < 100; i++)
            Assert.True(model.Build().HighlightOf(row).Changed);

        cell.Tick = 8;                                   // the asset finally ticks
        Assert.False(model.Build().HighlightOf(row).Changed);
    }

    /// <summary>
    /// ⭐ <b>Pending and changed are DISTINGUISHABLE states, not one flag.</b> ⛔ Collapsing them makes
    /// <i>"the sim changed this"</i> and <i>"my edit has not landed"</i> the same colour, which is the
    /// one thing a monitor must not do.
    /// </summary>
    [Fact]
    public void PendingAndChanged_AreTwoIndependentStates()
    {
        var (row, cell) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        cell.Bytes = I32(1);
        var model = Model(new[] { row });
        model.Build();

        model.Monitor.MarkPending(row.Origin);
        var pendingOnly = model.Build().HighlightOf(row);
        Assert.True (pendingOnly.Pending);
        Assert.False(pendingOnly.Changed);

        cell.Bytes = I32(2); cell.Tick = 1;
        var both = model.Build().HighlightOf(row);
        Assert.True(both.Pending);
        Assert.True(both.Changed);                       // ⭐ both at once is representable

        model.Monitor.ClearPending(row.Origin);
        Assert.False(model.Build().HighlightOf(row).Pending);
    }

    /// <summary>
    /// 🔴🔴 <b>The measured STOP, pinned: a row with NO asset-tick source is INERT, never wrong.</b>
    ///
    /// <para>
    /// Batch 68 measured that no per-asset tick exists — <c>_view.Tick</c> is the WORLD tick and
    /// <c>BlueprintTickSystem</c> stamps no per-instance counter. ⇒ rather than wiring the world tick
    /// in (which would clear the red while paused, the exact case the ruling exists for), a row with
    /// no tick source reports no highlight at all. ⭐ Asserted so the choice reads as a decision.
    /// </para>
    /// </summary>
    [Fact]
    public void ARowWithNoAssetTickSource_IsInertRatherThanWrong()
    {
        var cell = new Cell();
        var row = new VariableRow(
            Origin:    new VariableRowOrigin(AssetA, Ent(1), "Variables", "H", "Alpha"),
            ShortName: "H", TypeText: "Int32", ClrType: typeof(int),
            ReadValue: () => cell.Bytes,
            AssetTick: null);                            // ⛔ no source
        cell.Bytes = I32(1);

        var model = Model(new[] { row });
        model.Build();

        cell.Bytes = I32(2);
        Assert.False(model.Build().HighlightOf(row).Changed);
        Assert.Equal(0, model.Monitor.TrackedRowCount);  // ⭐ not even recorded
    }

    /// <summary>⭐ Opening a panel must not light every row: the first sighting is not a change.</summary>
    [Fact]
    public void TheFirstSighting_IsNotAChange()
    {
        var (row, cell) = MakeRow(AssetA, "Alpha", Ent(1), "H");
        cell.Bytes = I32(42);
        Assert.False(Model(new[] { row }).Build().HighlightOf(row).Changed);
    }
}
