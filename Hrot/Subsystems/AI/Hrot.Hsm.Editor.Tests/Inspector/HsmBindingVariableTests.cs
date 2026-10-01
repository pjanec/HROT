using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Inspector;

// ── Stubs ─────────────────────────────────────────────────────────────────────

file sealed class StubExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _map;

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
    public event Action? Changed { add { } remove { } }

    public StubExporter(params ActionSchemaEntry[] entries)
    {
        _map = new Dictionary<string, ActionSchemaEntry>(StringComparer.Ordinal);
        foreach (var e in entries) _map[e.Fqn] = e;
    }

    public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
    public void Rebuild() { }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>
/// B-1 / B-2 on the HSM host: a binding's variable list is filtered by its OWN method, and "Promote" creates THAT
/// binding's variable. ⭐ <c>CE-417</c> slice 4b: the subject is the ONE shared binding drawer the HSM factory registers
/// (it was <c>HsmBlackboardFieldPickerDrawer</c> fed by an <c>HsmFacetFqnContext</c> side channel).
/// </summary>
public sealed class HsmBindingVariableTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static HsmAsset MakeAsset(params BlackboardVariableEntry[] vars)
    {
        var b = new HsmBuilder("T");
        b.State("Idle").Initial().Final();
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flat);
        var meta = HsmEmitter.BuildMachineMetadata(graph);
        var asset = HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "T", "", false, "");
        if (vars.Length > 0) asset.SetBlackboardVariables(vars);
        return asset;
    }

    private static BlackboardVariableEntry Var(string name, Type t) =>
        new BlackboardVariableEntry(name, t, null);

    /// <summary>The sources behind the drawer the PRODUCTION factory registers for the binding facet.</summary>
    private static ActionBindingSources Sources(HsmAsset asset, IActionSchemaExporter? exporter = null)
        => HsmPickerDrawerFactory.BuildDrawers(asset, exporter)[typeof(BehaviorActionBindingFacet)]
               .Should().BeOfType<ActionBindingDrawer>().Subject.Sources;

    private static BehaviorActionBindingFacet Binding(string? fqn, Guid? site = null, string? slot = null) =>
        new() { MethodFqn = fqn, SiteId = (site ?? Guid.NewGuid()).ToString(), SiteSlot = slot };

    private static ActionSchemaEntry Entry(string fqn, Type t) =>
        new(fqn, t, ActionHosting.Hsm, BlackboardAccess.ReadWrite);

    // ── B-1: type filtering ───────────────────────────────────────────────────

    [Fact]
    public void Variables_AreOnlyTheCompatibleOnes_ForAKnownMethod()
    {
        var asset = MakeAsset(Var("floatField", typeof(float)), Var("intField", typeof(int)));

        Sources(asset, new StubExporter(Entry("Ns.FloatAction", typeof(float))))
            .GetVariables(Binding("Ns.FloatAction")).Should().ContainSingle().Which.Should().Be("floatField");
    }

    [Fact]
    public void Variables_AreAll_ForAnUnknownMethod()
    {
        var asset = MakeAsset(Var("a", typeof(float)), Var("b", typeof(int)));

        Sources(asset, new StubExporter()).GetVariables(Binding("Unknown.Action")).Should().HaveCount(2);
    }

    [Fact]
    public void Variables_AreAll_WhenNoExporterIsConfigured()
    {
        var asset = MakeAsset(Var("Speed", typeof(float)));

        Sources(asset).GetVariables(Binding("Ns.FloatAction")).Should().Contain("Speed");
    }

    [Fact]
    public void NoMatchingVariable_IsTheNoCompatibleState()
    {
        var asset   = MakeAsset(Var("intField", typeof(int)));
        var sources = Sources(asset, new StubExporter(Entry("Ns.FloatAction", typeof(float))));

        sources.GetVariables(Binding("Ns.FloatAction")).Should().BeEmpty();
        sources.HasNoCompatibleVariables(Binding("Ns.FloatAction")).Should().BeTrue();
    }

    [Fact]
    public void AnUnknownMethod_IsNotTheNoCompatibleState()
    {
        var asset = MakeAsset(Var("x", typeof(int)));

        Sources(asset, new StubExporter()).HasNoCompatibleVariables(Binding("Unknown.Action")).Should().BeFalse();
    }

    // ── B-2: Promote ─────────────────────────────────────────────────────────

    [Fact]
    public void Promote_CreatesAutoVar_WithCorrectNameAndType_AndIsAutoManaged()
    {
        var asset    = MakeAsset();
        var visualId = Guid.NewGuid();

        var resultName = Sources(asset, new StubExporter(Entry("Ns.FloatAction", typeof(float))))
            .Promote(Binding("Ns.FloatAction", visualId));

        resultName.Should().Be($"_auto_{visualId:N}");
        var created = asset.BlackboardVariables.Should().ContainSingle().Subject;
        created.FieldType.Should().Be(typeof(float));
        created.IsAutoManaged.Should().BeTrue();
    }

    /// <summary>
    /// ⭐⭐ <c>CE-417</c> B-2 — a node's SECOND binding gets its own variable. 🔴 Before 4b a transition's guard and action
    /// shared one field, so promoting for the guard would have bound the action's variable (another type) to it.
    /// </summary>
    [Fact]
    public void Promote_ForASecondaryBinding_NamesItsOwnVariable_BesideThePrimary()
    {
        var asset    = MakeAsset();
        var sources  = Sources(asset, new StubExporter(
            Entry("Ns.FloatAction", typeof(float)), Entry("Ns.IntGuard", typeof(int))));
        var visualId = Guid.NewGuid();

        var action = sources.Promote(Binding("Ns.FloatAction", visualId));
        var guard  = sources.Promote(Binding("Ns.IntGuard",    visualId, slot: "guard"));

        action.Should().Be($"_auto_{visualId:N}", "the primary binding keeps the pre-4b name");
        guard.Should().Be($"_auto_{visualId:N}_guard");
        asset.BlackboardVariables.Single(v => v.Name == guard).FieldType.Should().Be(typeof(int));
        asset.BlackboardVariables.Single(v => v.Name == action).FieldType.Should().Be(typeof(float));
    }

    [Fact]
    public void Promote_Idempotent_WhenVarAlreadyExists()
    {
        var asset    = MakeAsset();
        var sources  = Sources(asset, new StubExporter(Entry("Ns.IntAction", typeof(int))));
        var visualId = Guid.NewGuid();

        sources.Promote(Binding("Ns.IntAction", visualId));
        sources.Promote(Binding("Ns.IntAction", visualId));

        asset.BlackboardVariables.Should().HaveCount(1);
    }

    [Fact]
    public void Promote_ReturnsNull_WhenFqnNotResolvable()
    {
        var asset = MakeAsset();

        Sources(asset, new StubExporter()).Promote(Binding("Unknown.Action")).Should().BeNull();
        asset.BlackboardVariables.Should().BeEmpty();
    }
}
