using System;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>C-watch</c> — the Watch panel shares the row renderer.</b>
///
/// <para>
/// 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §1a/§1b/§7. ⭐⭐ <b>If <c>C-table</c>'s
/// heterogeneous rail was right, this is mostly wiring</b> — and it was, so these tests are about
/// <b>Watch's own three claims</b>: mixed sources render correctly, stale rows survive, and the
/// 64-byte carrier limit is not inherited.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Not verifiable and the visual check is suspended:</b> the greying of a stale row, the pin/unpin
/// gestures, and the <c>Type</c>-column toggle actually being hidden on screen.
/// </para>
/// </summary>
public sealed class WatchPinnedSourceTests
{
    private static readonly Guid AssetA = new("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid AssetB = new("bbbbbbbb-0000-0000-0000-00000000000b");

    private static Entity Ent(int i) => new Entity(i, 1);

    private static VariableRow Row(
        Guid assetId, string assetName, Entity entity, string name,
        Func<byte[]>? read = null, Type? clr = null)
        => new(
            Origin:    new VariableRowOrigin(assetId, entity, "Variables", name, assetName),
            ShortName: name,
            TypeText:  (clr ?? typeof(int)).Name,
            ClrType:   clr ?? typeof(int),
            ReadValue: () => (read ?? (() => Array.Empty<byte>()))());

    private static byte[] I32(int v) { var b = new byte[4]; MemoryMarshal.Write(b, in v); return b; }

    /// <summary>
    /// ⭐⭐⭐ <b>A pinned set spanning TWO assets and TWO entities groups correctly and keeps its
    /// highlight state independent</b> — Watch's defining case, through the shared model.
    /// </summary>
    [Fact]
    public void APinnedSetAcrossTwoAssetsAndTwoEntities_GroupsAndHighlightsIndependently()
    {
        int a1 = 0, a2 = 0, b1 = 0;
        uint tick = 0;

        VariableRow Make(Guid asset, string name, Entity e, Func<int> read)
            => Row(asset, asset == AssetA ? "Alpha" : "Bravo", e, "Health", () => I32(read()))
               with { AssetTick = () => tick };

        var rowA1 = Make(AssetA, "Alpha", Ent(1), () => a1);
        var rowA2 = Make(AssetA, "Alpha", Ent(2), () => a2);
        var rowB1 = Make(AssetB, "Bravo", Ent(1), () => b1);

        var source = new PinnedVariableRowSource();
        source.Pin(rowA1);
        source.Pin(rowA2);
        source.Pin(rowB1);

        var model = new VariableTableModel(source, VariableTableColumns.Watch,
                                           VariableRowGrouping.WatchDefault)
        { RunState = VariableRunState.Running };

        model.Build();                                  // seed

        a1 = 42; tick = 1;                              // only ONE of the three moves
        var view = model.Build();

        Assert.True (view.HighlightOf(rowA1).Changed);
        Assert.False(view.HighlightOf(rowA2).Changed);  // 🔴 same asset, other entity
        Assert.False(view.HighlightOf(rowB1).Changed);

        // Grouping: two assets ⇒ two headers; Alpha has two entities, Bravo one (⇒ uniform, no header).
        Assert.Equal(2, view.Groups.Count);
        Assert.Equal(2, view.Groups.Single(g => g.Header == "Alpha").Children.Count);
        Assert.Empty(view.Groups.Single(g => g.Header == "Bravo").Children);
    }

    /// <summary>⭐ Watch hides the <c>Type</c> column by default — <i>"not even the data type is
    /// important for monitoring"</i> — and Details shows it.</summary>
    [Fact]
    public void WatchDefaults_HideTypeAndGroupByAssetThenEntity()
    {
        Assert.DoesNotContain(VariableColumn.Type, VariableTableColumns.Watch.Visible);
        Assert.Equal(new[] { VariableFacet.Asset, VariableFacet.Entity },
                     VariableRowGrouping.WatchDefault);
        Assert.Empty(VariableRowGrouping.DetailsDefault);
    }

    /// <summary>
    /// ⭐ <b>A stale row RENDERS and refuses its dialog</b> — it is kept, not dropped. ⛔ Dropping it
    /// would make the Watch list silently shrink when an asset closes.
    /// </summary>
    [Fact]
    public void AStaleRow_IsKept_RendersAndRefusesItsDialog()
    {
        var row    = Row(AssetA, "Alpha", Ent(1), "Ghost");
        var source = new PinnedVariableRowSource();
        source.Pin(row);

        Assert.True(source.MarkStale(row.Origin));

        var stale = Assert.Single(source.GetRows());
        Assert.True(stale.IsStale);
        Assert.False(stale.CanEverBeWritten);
        Assert.Equal(VariableEditAvailability.Denied,
            VariableEditPolicy.Resolve(VariableEditAction.EditValue, VariableRunState.Running, stale));
    }

    /// <summary>
    /// 🔴🔴 <b>A 136-byte struct pins and renders — the <c>Watch._valueBuffer</c> limit is NOT
    /// inherited.</b>
    ///
    /// <para>
    /// That buffer is <c>new byte[64]</c> and <c>WriteValue</c> <b>throws</b> above it, so
    /// <c>HillAttackSharedState</c> (136) could not pass through the old carrier at all. ⚠ Asserted at
    /// exactly 136 because that is the largest of the three the design names.
    /// </para>
    /// </summary>
    [Fact]
    public void A136ByteValue_PinsAndRenders()
    {
        var big = new byte[136];
        big[0] = 1;

        var row    = Row(AssetA, "Alpha", Ent(1), "Big", () => big, typeof(DateTime));
        var source = new PinnedVariableRowSource();
        source.Pin(row);

        var model = new VariableTableModel(source, VariableTableColumns.Watch);
        var view  = model.Build();

        Assert.Single(view.AllRows);
        // ⛔ Undecodable is fine and says so in WORDS; what matters is that 136 bytes did not throw.
        var formatter = new VariableValueFormatter((bytes, _) => bytes);
        Assert.Equal(VariableValueFormatter.Unreadable, formatter.Cell(row));
    }

    /// <summary>⚠ Re-pinning one identity replaces rather than duplicates — a Watch list that grew a
    /// second copy of a row would double-count it in every group.</summary>
    [Fact]
    public void RePinningTheSameIdentity_ReplacesRatherThanDuplicates()
    {
        var source = new PinnedVariableRowSource();
        source.Pin(Row(AssetA, "Alpha", Ent(1), "Health"));
        source.Pin(Row(AssetA, "Alpha", Ent(1), "Health"));

        Assert.Single(source.GetRows());
    }

    /// <summary>⭐ And unpinning removes exactly one.</summary>
    [Fact]
    public void Unpin_RemovesThatRowOnly()
    {
        var keep = Row(AssetA, "Alpha", Ent(1), "Keep");
        var drop = Row(AssetA, "Alpha", Ent(1), "Drop");

        var source = new PinnedVariableRowSource();
        source.Pin(keep);
        source.Pin(drop);

        Assert.True(source.Unpin(drop.Origin));
        Assert.Equal("Keep", Assert.Single(source.GetRows()).ShortName);
        Assert.False(source.Unpin(drop.Origin));
    }
}
