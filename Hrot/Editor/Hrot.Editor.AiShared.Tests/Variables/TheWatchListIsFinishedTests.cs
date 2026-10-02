using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>The watch-variable list, finished — <c>BP-499</c>…<c>BP-502</c>.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §1/§1b *(grouping)* · §3 *(the two binding kinds)* ·
/// §5 *(persist the pin set)*.
///
/// <para>⭐ Every case here is headless: the grouping engine, the pin store, the binding and the
/// persistence mapping are all pure. ⛔ Only the selector's PAINTING needs ImGui, which is why its
/// behaviour lives in <c>VariableGroupBySelector.Modes</c>/<c>IndexOf</c> and is asserted directly.</para>
/// </summary>
public sealed class TheWatchListIsFinishedTests
{
    private static readonly Guid AssetA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssetB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static VariableRow Row(Guid asset, Entity entity, string name, string assetName)
        => new(Origin:    new VariableRowOrigin(asset, entity, "s", name, assetName),
               ShortName: name,
               TypeText:  "int",
               ClrType:   typeof(int),
               ReadValue: () => BitConverter.GetBytes(1));

    private static Entity Ent(int index) => new(index, 1);

    // ══ BP-499 — grouping is wired into the Watch ═══════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>And the grouping actually produces HEADERS</b> — the wiring is only worth anything if the
    /// engine it feeds emits groups for a real mixed list.
    /// <para>⚠ Two assets × two entities, because 📌 a UNIFORM facet emits no header by design: a
    /// single-asset list would prove nothing.</para>
    /// </summary>
    [Fact]
    public void AMixedWatchListRendersGroupHeaders()
    {
        var pinned = new PinnedVariableRowSource();
        pinned.Pin(Row(AssetA, Ent(1), "Health", "Alpha"));
        pinned.Pin(Row(AssetA, Ent(2), "Health", "Alpha"));
        pinned.Pin(Row(AssetB, Ent(1), "Ammo",   "Bravo"));

        var model = new VariableTableModel(pinned, VariableTableColumns.Watch,
                                           VariableRowGrouping.WatchDefault);
        var view  = model.Build();

        Assert.NotEmpty(view.Groups);
        Assert.Equal(new[] { "Alpha", "Bravo" },
                     view.Groups.Select(g => g.Header).OrderBy(h => h, StringComparer.Ordinal));

        // ⭐ Asset A holds two entities ⇒ it nests a second level; asset B holds one ⇒ uniform, no header.
        var alpha = view.Groups.Single(g => g.Header == "Alpha");
        Assert.Equal(2, alpha.Children.Count);
        Assert.Empty(view.Groups.Single(g => g.Header == "Bravo").Children);
    }

    // ══ BP-500 — the group-by selector ══════════════════════════════════════════

    /// <summary>
    /// ⭐ <b>The selector offers §1b's four modes as FACET LISTS</b>, and each round-trips through
    /// <c>IndexOf</c>. ⛔ Not an enum of behaviours — adding a mode must need no grouping code.
    /// </summary>
    [Fact]
    public void TheSelectorOffersTheDesignedModesAndRoundTrips()
    {
        Assert.Equal(4, VariableGroupBySelector.Modes.Count);

        Assert.Equal(VariableRowGrouping.WatchDefault, VariableGroupBySelector.Modes[0].Facets);
        Assert.Contains(VariableGroupBySelector.Modes, m => m.Facets.SequenceEqual(new[] { VariableFacet.Entity }));
        Assert.Contains(VariableGroupBySelector.Modes, m => m.Facets.SequenceEqual(new[] { VariableFacet.Section }));
        Assert.Contains(VariableGroupBySelector.Modes, m => m.Facets.Count == 0);

        for (int i = 0; i < VariableGroupBySelector.Modes.Count; i++)
            Assert.Equal(i, VariableGroupBySelector.IndexOf(VariableGroupBySelector.Modes[i].Facets));
    }

    /// <summary>
    /// ⚠⚠ <b>A grouping no mode names answers <c>-1</c>, NOT <c>0</c>.</b>
    /// ⛔ Falling back to <c>0</c> would show "Asset, then entity" while the model was grouped otherwise —
    /// 📌 precisely the defect <c>BP-114</c> fixed in the type picker, and worth not repeating.
    /// </summary>
    [Fact]
    public void AnUnnamedGroupingIsNoSelectionRatherThanTheFirstMode()
    {
        Assert.Equal(-1, VariableGroupBySelector.IndexOf(new[] { VariableFacet.Entity, VariableFacet.Asset }));
        Assert.Equal(-1, VariableGroupBySelector.IndexOf(null!));
    }

    // ══ BP-501 — the two-kind binding, and the CHOICE ═══════════════════════════

    /// <summary>
    /// 🔴🔴 <b>A CONCRETE pin stays on its captured entity; a CHAMELEON follows the selection.</b>
    /// ⭐ This is the choice §3 asks for, made visible: two pins of the SAME variable differ only in the
    /// binding the designer chose, and they resolve to different entities.
    /// <para>⭐ Resolution is asserted through <see cref="EntityBinding.OriginEntity"/> — the sentinel
    /// convention <c>StagedWriteView.EntityFor</c> and <c>VariableChangeMonitor</c> already read, ⛔ not a
    /// second encoding of "follow the selection".</para>
    /// </summary>
    [Fact]
    public void AConcretePinKeepsItsEntityWhileAChameleonFollowsTheSelection()
    {
        var pinned = new PinnedVariableRowSource();
        var target = Ent(7);

        pinned.Pin(Row(AssetA, target, "Health", "Alpha"), EntityBinding.Concrete(4242, target));
        pinned.Pin(Row(AssetB, target, "Ammo",   "Bravo"), EntityBinding.Chameleon);

        var rows = pinned.GetRows();

        var concrete = rows.Single(r => r.Origin.VariablePath == "Health");
        var chameleon = rows.Single(r => r.Origin.VariablePath == "Ammo");

        // ⭐ The concrete row is pinned to the entity that was selected — it does not move.
        Assert.Equal(target, concrete.Origin.Entity);
        Assert.Equal(EntityBindingKind.Concrete, pinned.BindingOf(concrete.Origin)!.Value.Kind);
        Assert.Equal(4242, pinned.BindingOf(concrete.Origin)!.Value.StagingNetworkId);

        // ⭐⭐ The chameleon carries the SENTINEL, which is what makes it follow the selection downstream.
        Assert.Equal(default(Entity), chameleon.Origin.Entity);
        Assert.Equal(EntityBindingKind.Chameleon, pinned.BindingOf(chameleon.Origin)!.Value.Kind);
    }

    /// <summary>
    /// ⭐ <b>Pinning a chameleon REWRITES the row's entity to the sentinel.</b>
    /// ⛔ Otherwise the stored row and its binding disagree: the row would resolve to the captured entity
    /// while the binding claimed it follows the selection — a silent lie in the direction of "looks fine".
    /// </summary>
    [Fact]
    public void AChameleonPinCannotKeepAConcreteEntityOnItsRow()
    {
        var pinned = new PinnedVariableRowSource();
        pinned.Pin(Row(AssetA, Ent(9), "Health", "Alpha"), EntityBinding.Chameleon);

        Assert.Equal(default(Entity), pinned.GetRows().Single().Origin.Entity);
    }

    /// <summary>
    /// ⭐ <b>The pre-<c>BP-501</c> single-argument <c>Pin</c> still means what it always meant</b> — the
    /// kind is INFERRED from the row. ⚠ Asserted because every existing caller relies on it.
    /// </summary>
    [Fact]
    public void AnUnspecifiedBindingIsInferredFromTheRow()
    {
        var pinned = new PinnedVariableRowSource();
        pinned.Pin(Row(AssetA, Ent(3), "Health", "Alpha"));
        pinned.Pin(Row(AssetB, default, "Ammo",  "Bravo"));

        var rows = pinned.GetRows();
        Assert.Equal(EntityBindingKind.Concrete,
                     pinned.BindingOf(rows.Single(r => r.Origin.VariablePath == "Health").Origin)!.Value.Kind);
        Assert.Equal(EntityBindingKind.Chameleon,
                     pinned.BindingOf(rows.Single(r => r.Origin.VariablePath == "Ammo").Origin)!.Value.Kind);
    }

    // ══ BP-502 — the pin set survives save → reload ═════════════════════════════

    /// <summary>
    /// 🔴 <b>A pinned row survives a save and a reload, entity-keyed.</b>
    /// ⭐ Both kinds: the concrete pin comes back with its <c>NetworkId</c>, the chameleon comes back as a
    /// chameleon. ⛔ Neither carries an <c>Entity</c> through the file — a handle is recycled and would
    /// point at whatever now occupies the slot.
    /// </summary>
    [Fact]
    public void APinnedRowSurvivesSaveAndReload()
    {
        var pinned = new PinnedVariableRowSource();
        pinned.Pin(Row(AssetA, Ent(7), "Health", "Alpha"), EntityBinding.Concrete(4242, Ent(7)));
        pinned.Pin(Row(AssetB, default, "Ammo",  "Bravo"), EntityBinding.Chameleon);

        var entries = PinnedVariablePersistence.Capture(pinned, out int skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(2, entries.Count);

        var path = Path.Combine(Path.GetTempPath(), "bp502-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            DebugSessionPersistence.Save(
                Array.Empty<Hrot.Blueprints.Core.Debug.Breakpoint>(),
                Array.Empty<Hrot.Blueprints.Core.Debug.Watch>(),
                Array.Empty<Breakpoint>(),
                path,
                entries);

            var file = DebugSessionPersistence.TryLoad(path);
            Assert.NotNull(file);

            var restored = PinnedVariablePersistence.Restore(file!, out int dropped);
            Assert.Equal(0, dropped);
            Assert.Equal(2, restored.Count);

            var concrete = restored.Single(p => p.VariablePath == "Health");
            Assert.Equal(AssetA, concrete.AssetId);
            Assert.Equal("Alpha", concrete.AssetName);
            Assert.Equal(EntityBindingKind.Concrete, concrete.Binding.Kind);
            Assert.Equal(4242, concrete.Binding.StagingNetworkId);
            // ⛔ The in-session handle is NOT in the file — the caller resolves NetworkId.
            Assert.Equal(default(Entity), concrete.Binding.Captured);

            var chameleon = restored.Single(p => p.VariablePath == "Ammo");
            Assert.Equal(EntityBindingKind.Chameleon, chameleon.Binding.Kind);
            Assert.Equal(0, chameleon.Binding.StagingNetworkId);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// ⚠ <b>A concrete pin with no <c>NetworkIdentity</c> is SKIPPED and COUNTED, not written as id 0.</b>
    /// ⛔ Writing it would restore a pin pointing at nothing, which reads as data loss on the next load
    /// rather than as the within-session pin it always was.
    /// </summary>
    [Fact]
    public void AnUnpersistablePinIsReportedRatherThanWrittenAsEntityZero()
    {
        var pinned = new PinnedVariableRowSource();
        pinned.Pin(Row(AssetA, Ent(3), "Health", "Alpha"));   // inferred concrete, NetworkId 0

        var entries = PinnedVariablePersistence.Capture(pinned, out int skipped);

        Assert.Empty(entries);
        Assert.Equal(1, skipped);
    }

    /// <summary>
    /// ⚠⚠ <b>An unknown binding kind is SKIPPED, not coerced.</b> ⛔ <c>Enum.TryParse</c> failing leaves
    /// the zero value — which happens to be <c>Concrete</c> — so a silent parse failure would turn a future
    /// kind into a concrete pin on entity 0 and show the wrong entity's value.
    /// </summary>
    [Fact]
    public void AnUnknownBindingKindIsSkippedNotCoercedToConcrete()
    {
        var file = new DebugSessionFile();
        file.PinnedVariables.Add(new PinnedVariableEntry
        {
            AssetId = AssetA, Section = "s", VariablePath = "Health",
            BindingKind = "SomethingFromTheFuture", NetworkId = 7,
        });

        var restored = PinnedVariablePersistence.Restore(file, out int dropped);

        Assert.Empty(restored);
        Assert.Equal(1, dropped);
    }
}
