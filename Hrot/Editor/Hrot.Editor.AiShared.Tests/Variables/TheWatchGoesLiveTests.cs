using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 — THE HEADLINE CLAIM, end to end: pin a variable, advance the pulse, the Watch
/// value MOVES.</b>
///
/// <para>📌 The handoff asked for exactly one unambiguous test of this: <i>"pin a variable, advance the
/// counter, assert the Watch value moved."</i> ⭐ Every other rail in this batch proves one link;
/// this one drives the whole chain — <b>camera arm (<c>94a</c>) → pulse (<c>94b</c>) → sampler
/// (<c>94c</c>) → comparison (<c>94d</c>) → <c>(pending)</c> (<c>94e</c>)</b>.</para>
///
/// <para>⛔ It goes through the REAL <c>VariableTableModel</c>, ⛔ not the sampler directly — a rail that
/// drives the sampler cannot see whether the panel actually samples.</para>
/// </summary>
public sealed class TheWatchGoesLiveTests
{
    private static readonly Guid AssetId = new("ffffffff-0000-0000-0000-00000000000f");

    private static string Cell(VariableTableView view, VariableRow row)
        => new VariableValueFormatter(RawValueDecoder.Instance)
               .Cell(view.AllRows.Single(r => r.Origin.Key.Equals(row.Origin.Key)), view.ValueMode);

    /// <summary>
    /// ⭐⭐⭐ <b>THE end-to-end rail.</b> The designer pins a variable from a live Details source while
    /// paused; the sim then steps twice, and the Watch panel follows.
    /// </summary>
    [Fact]
    public void APinnedVariableFollowsTheRunAcrossBehaviourFrames()
    {
        // A live provider, exactly the shape a host supplies.
        var live    = new Dictionary<string, object> { ["Health"] = 10 };
        var details = new SectionVariableRowSource(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new IntSchema("Health"),
            liveObjects: () => live);

        // ⭐ The designer pins the row the Details table drew.
        var pinnedStore = new PinnedVariableRowSource();
        var pinnedRow   = details.GetRows()[0];
        pinnedStore.Pin(pinnedRow);

        // ⭐ The Watch panel — its OWN model, and therefore its own sampler and monitor.
        var watch = new VariableTableModel(pinnedStore, VariableTableColumns.Details)
        { RunState = VariableRunState.Paused };

        Assert.Contains("10", Cell(watch.Build(), pinnedRow));

        // ── the sim steps ────────────────────────────────────────────────────
        live["Health"] = 99;
        BehaviorFrame.Advance();

        var afterFirstStep = watch.Build();
        Assert.Contains("99", Cell(afterFirstStep, pinnedRow));

        // ⭐⭐ …and the change HIGHLIGHTS — 🔴 impossible before this batch on any host, because every
        //    production row passed AssetTick: null and the monitor returned None on its first line.
        Assert.True(afterFirstStep.HighlightOf(pinnedRow).Changed);

        // ── it steps again, with no change this time ─────────────────────────
        BehaviorFrame.Advance();

        var afterSecondStep = watch.Build();
        Assert.Contains("99", Cell(afterSecondStep, pinnedRow));
        Assert.False(afterSecondStep.HighlightOf(pinnedRow).Changed,
            "the highlight clears on the next frame — VS-debugger behaviour");
    }

    /// <summary>
    /// ⭐⭐ <b>And the value holds STILL between pulses</b>, which is the other half of rule 2: a watch
    /// under a breakpoint must not flicker because the UI repainted.
    /// </summary>
    [Fact]
    public void APinnedVariableHoldsStillBetweenPulses()
    {
        var live    = new Dictionary<string, object> { ["Health"] = 10 };
        var details = new SectionVariableRowSource(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new IntSchema("Health"),
            liveObjects: () => live);

        var store = new PinnedVariableRowSource();
        var row   = details.GetRows()[0];
        store.Pin(row);

        var watch = new VariableTableModel(store, VariableTableColumns.Details)
        { RunState = VariableRunState.Paused };

        watch.Build();
        live["Health"] = 99;                       // the world changed, the sim did not step

        Assert.Contains("10", Cell(watch.Build(), row));
        Assert.Contains("10", Cell(watch.Build(), row));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>(pending)</c> UNPENDS end to end</b> — a variable the run starts writing AFTER the
    /// pin. 🔴 Batch 93 measured this as frozen for ever.
    /// </summary>
    [Fact]
    public void AVariableFirstWrittenAfterThePinStopsSayingPending()
    {
        var live    = new Dictionary<string, object>();       // nothing written yet
        var details = new SectionVariableRowSource(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new IntSchema("Health"),
            liveObjects: () => live);

        var store = new PinnedVariableRowSource();
        var row   = details.GetRows()[0];
        store.Pin(row);

        var watch = new VariableTableModel(store, VariableTableColumns.Details)
        { RunState = VariableRunState.Paused };

        Assert.Contains("pending", Cell(watch.Build(), row), StringComparison.OrdinalIgnoreCase);

        live["Health"] = 42;                                   // the run writes it
        BehaviorFrame.Advance();

        Assert.DoesNotContain("pending", Cell(watch.Build(), row), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("42", Cell(watch.Build(), row));
    }

    // ── fixture ─────────────────────────────────────────────────────────────

    private sealed class IntSchema : IVariablesSchemaSource
    {
        public IntSchema(params string[] names)
            => Variables = names.Select(n => new VariableViewModel(
                   Name: n, TypeName: "int", ByteSize: 4, FieldType: typeof(int),
                   Comment: null, AliasedBy: Array.Empty<(string, Guid, Guid)>(), IsUnused: false))
               .ToList();

        public IReadOnlyList<VariableViewModel> Variables { get; }
        public bool IsReadOnly => false;
        public bool SupportsRoleScopeEditing => false;
        public string? GetRefactorKey(string variableName) => null;
        public void AddVariable(Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void RenameVariable(string oldName, string newName) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<Hrot.Editor.AiShared.Windows.UnboundRequirementViewModel> UnboundRequirements
            => Array.Empty<Hrot.Editor.AiShared.Windows.UnboundRequirementViewModel>();
        public void AddAlias(string name, Hrot.Editor.AiShared.Blackboard.BlackboardAliasBinding binding) { }
        public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
        public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
    }
}
