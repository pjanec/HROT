using System;
using Hrot.Editor.AiShared;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

// ── Stub exporter ─────────────────────────────────────────────────────────────

file sealed class StubExporterForHsmBB1D : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _map = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
    public event Action? Changed { add { } remove { } }

    public void Register(string fqn, Type dtoType)
        => _map[fqn] = new ActionSchemaEntry(fqn, dtoType, ActionHosting.Hsm, BlackboardAccess.ReadWrite);

    public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
    public void Rebuild() { }
}

/// <summary>
/// BB1D integration tests, HSM: the production dispatcher and the production drawers, wired as
/// <c>AiFacetPickerBinder</c> wires them, offer ONLY the type-compatible variables for the selected binding.
///
/// <para>⭐ <c>CE-417</c> slice 4b: the HSM counterpart of the BTree test. BB1D's gap was the <c>HsmFacetFqnContext</c>
/// side channel; it is gone, and every binding facet carries its own method — which also makes a transition's guard
/// and action filter SEPARATELY (B-2), something one shared context could never express.</para>
/// </summary>
public sealed class BB1DHsmSharedContextIntegrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder b)
    {
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        return (HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph));
    }

    /// <summary>
    /// Builds an HSM with states "Idle" → "Active" connected by a transition with an
    /// action function bound to <paramref name="actionFqn"/>, plus blackboard variables.
    /// </summary>
    private static HsmAsset MakeHsmAssetWithTransitionAction(
        string actionFqn,
        params BlackboardVariableEntry[] vars)
    {
        var b = new HsmBuilder("BB1D");
        b.Event("Fire", 1);
        b.State("Active").Final();
        b.State("Idle").Initial().On("Fire").GoTo("Active").Action(actionFqn);
        var (blob, meta) = Compile(b);
        var asset = HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "BB1D", "", false, "");
        if (vars.Length > 0)
            asset.SetBlackboardVariables(vars);
        return asset;
    }

    private static ActionBindingSources BindingSources(HsmAsset asset, IActionSchemaExporter exporter)
        => HsmPickerDrawerFactory.BuildDrawers(asset, exporter)[typeof(BehaviorActionBindingFacet)]
               .Should().BeOfType<ActionBindingDrawer>().Subject.Sources;

    // ── Core integration test: the dispatcher's binding filters the drawer's list ──

    /// <summary>CRITICAL: the BB1D HSM wiring test — the transition's action binding filters to the float variable.</summary>
    [Fact]
    public void Dispatcher_Hsm_ActionBindingCarriesTheMethod_DrawerFiltersToTType()
    {
        const string fqn = "Ns.FloatAction";
        var asset = MakeHsmAssetWithTransitionAction(fqn,
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));
        var exporter = new StubExporterForHsmBB1D();
        exporter.Register(fqn, typeof(float));

        var dispatcher = HsmSelectionBridgeHelper.BuildFacetDispatcher(asset);
        dispatcher.Should().NotBeNull();
        var transition = asset.AllTransitions.First();
        var facet = dispatcher!.GetFacet(new HsmTransitionSelection(transition.VisualId));

        facet.Should().BeOfType<TransitionFacet>();
        var action = ((TransitionFacet)facet!).Action;
        action.MethodFqn.Should().Be(fqn);

        var items = BindingSources(asset, exporter).GetVariables(action);
        items.Should().ContainSingle("only the float variable is compatible with the float DtoType");
        items[0].Should().Be("floatVar");
    }

    /// <summary>
    /// ⭐⭐ B-2 — a transition's guard and action each filter by their OWN method. 🔴 With one shared context (and one
    /// shared target field) before 4b, the guard's list was the action's.
    /// </summary>
    [Fact]
    public void Dispatcher_Hsm_GuardAndAction_EachFilterByTheirOwnMethod()
    {
        var asset = MakeHsmAssetWithTransitionAction("Ns.FloatAction",
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));
        var transition = asset.AllTransitions.First();
        transition.Guard = BehaviorActionBinding.ForMethod("Ns.IntGuard");
        var exporter = new StubExporterForHsmBB1D();
        exporter.Register("Ns.FloatAction", typeof(float));
        exporter.Register("Ns.IntGuard",    typeof(int));

        var facet   = (TransitionFacet)HsmSelectionBridgeHelper.BuildFacetDispatcher(asset)!
                          .GetFacet(new HsmTransitionSelection(transition.VisualId))!;
        var sources = BindingSources(asset, exporter);

        sources.GetVariables(facet.Action).Should().Equal(new[] { "floatVar" });
        sources.GetVariables(facet.Guard).Should().Equal(new[] { "intVar" });
    }

    /// <summary>A state's slots that bind nothing yet offer every variable.</summary>
    [Fact]
    public void Dispatcher_Hsm_AnUnboundStateSlot_ListsAllVars()
    {
        var asset = MakeHsmAssetWithTransitionAction("Ns.FloatAction",
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));
        var exporter = new StubExporterForHsmBB1D();
        exporter.Register("Ns.FloatAction", typeof(float));
        var idle = asset.AllStates.First(s => s.Name == "Idle");

        var facet = (StateFacet)HsmSelectionBridgeHelper.BuildFacetDispatcher(asset)!
                        .GetFacet(new HsmStateSelection(idle.StableId))!;

        BindingSources(asset, exporter).GetVariables(facet.Activity).Should().HaveCount(2, "no method means no filtering");
    }

    /// <summary>BuildFacetDispatcher(asset: null) returns null and does not throw.</summary>
    [Fact]
    public void BuildFacetDispatcher_NullAsset_ReturnsNull()
        => HsmSelectionBridgeHelper.BuildFacetDispatcher(null).Should().BeNull();
}
