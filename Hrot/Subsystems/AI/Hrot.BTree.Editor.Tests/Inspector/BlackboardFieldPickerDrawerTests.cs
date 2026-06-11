using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

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
/// B-1 BTree drawer: type-filtering + Promote (B-2) headless tests.
/// </summary>
public sealed class BlackboardFieldPickerDrawerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeAsset MakeAsset(params BlackboardVariableEntry[] vars)
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        var asset = BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "T", "/t.cs", false, "", "");
        if (vars.Length > 0)
            asset.SetBlackboardVariables(vars);
        return asset;
    }

    private static BlackboardVariableEntry Var(string name, Type t) =>
        new BlackboardVariableEntry(name, t, null);

    // ── B-1: type filtering ───────────────────────────────────────────────────

    [Fact]
    public void GetItems_ReturnsOnlyCompatibleVars_ForKnownFqn()
    {
        var asset = MakeAsset(
            Var("floatVar", typeof(float)),
            Var("intVar",   typeof(int)));
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = "Ns.FloatAction" };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        var items = drawer.GetItems();

        items.Should().ContainSingle().Which.Should().Be("floatVar",
            "only variables matching the action's DtoType (float) should be returned");
    }

    [Fact]
    public void GetItems_ReturnsAllVars_ForUnknownFqn()
    {
        var asset    = MakeAsset(Var("a", typeof(float)), Var("b", typeof(int)));
        var exporter = new StubExporter(); // empty
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = "Unknown.Action" };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.GetItems().Should().HaveCount(2,
            "unknown FQN falls back to showing all variables");
    }

    [Fact]
    public void GetItems_ReturnsAllVars_WhenFqnIsNull()
    {
        var asset    = MakeAsset(Var("x", typeof(bool)), Var("y", typeof(float)));
        var exporter = new StubExporter();
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = null };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.GetItems().Should().HaveCount(2,
            "null FQN falls back to showing all variables");
    }

    [Fact]
    public void GetItems_ReturnsAllVars_WhenNoExporterConfigured()
    {
        var asset  = MakeAsset(Var("Speed", typeof(float)));
        var drawer = new BlackboardFieldPickerDrawer(asset);

        drawer.GetItems().Should().Contain("Speed",
            "without an exporter the drawer shows all variables");
    }

    [Fact]
    public void GetItems_ReturnsEmpty_AndHasNoCompatible_WhenNoMatchingVars()
    {
        var asset    = MakeAsset(Var("intVar", typeof(int)));
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = "Ns.FloatAction" };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.GetItems().Should().BeEmpty(
            "the int variable does not match float DtoType");
        drawer.HasNoCompatibleVariables.Should().BeTrue(
            "FQN is known but no variable matches DtoType");
    }

    [Fact]
    public void HasNoCompatibleVariables_False_WhenFqnUnknown()
    {
        var asset    = MakeAsset(Var("intVar", typeof(int)));
        var exporter = new StubExporter(); // empty
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = "Unknown.Action" };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.HasNoCompatibleVariables.Should().BeFalse(
            "unknown FQN should not trigger the 'promote' affordance");
    }

    // ── FQN context threading ─────────────────────────────────────────────────

    [Fact]
    public void FqnContext_UpdatedByMapper_IsPickedUpByDrawer()
    {
        var asset    = MakeAsset(Var("floatVar", typeof(float)), Var("intVar", typeof(int)));
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext();
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        // Before FQN is set: all vars.
        drawer.GetItems().Should().HaveCount(2);

        // Simulate mapper setting the FQN.
        ctx.CurrentActionFqn = "Ns.FloatAction";

        // Now filtered.
        drawer.GetItems().Should().ContainSingle().Which.Should().Be("floatVar");
    }

    // ── B-2: Promote ─────────────────────────────────────────────────────────

    [Fact]
    public void Promote_CreatesAutoVar_WithCorrectNameAndType_AndIsAutoManaged()
    {
        var asset    = MakeAsset(); // no vars
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = "Ns.FloatAction" };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        var visualId = Guid.NewGuid();
        var resultName = drawer.Promote(visualId.ToString());

        resultName.Should().Be($"_auto_{visualId:N}");
        var created = asset.BlackboardVariables.Should().ContainSingle().Subject;
        created.Name.Should().Be($"_auto_{visualId:N}");
        created.FieldType.Should().Be(typeof(float));
        created.IsAutoManaged.Should().BeTrue();
    }

    [Fact]
    public void Promote_Idempotent_WhenVarAlreadyExists()
    {
        var asset    = MakeAsset();
        var entry    = new ActionSchemaEntry("Ns.IntAction", typeof(int), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = "Ns.IntAction" };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        var visualId = Guid.NewGuid();
        var name1 = drawer.Promote(visualId.ToString());
        var name2 = drawer.Promote(visualId.ToString()); // same id

        name1.Should().Be(name2, "same visualId produces the same name");
        asset.BlackboardVariables.Should().HaveCount(1, "second promote is idempotent");
    }

    [Fact]
    public void Promote_TwoDifferentVisualIds_CreatesTwoVars()
    {
        var asset    = MakeAsset();
        var entry    = new ActionSchemaEntry("Ns.BoolAction", typeof(bool), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = "Ns.BoolAction" };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.Promote(Guid.NewGuid().ToString());
        drawer.Promote(Guid.NewGuid().ToString());

        asset.BlackboardVariables.Should().HaveCount(2);
    }

    [Fact]
    public void Promote_ReturnsNull_WhenFqnNotResolvable()
    {
        var asset    = MakeAsset();
        var exporter = new StubExporter(); // empty
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = "Unknown.Action" };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        var result = drawer.Promote(Guid.NewGuid().ToString());

        result.Should().BeNull("unknown FQN means no DtoType to create a var for");
        asset.BlackboardVariables.Should().BeEmpty();
    }

    [Fact]
    public void Promote_ReturnsNull_WhenFqnIsNull()
    {
        var asset    = MakeAsset();
        var entry    = new ActionSchemaEntry("Ns.Action", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = null };
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        var result = drawer.Promote(Guid.NewGuid().ToString());

        result.Should().BeNull("null FQN means promote cannot determine the type");
    }

    // ── PromoteRequested flag ─────────────────────────────────────────────────

    [Fact]
    public void PromoteRequested_InitiallyFalse()
    {
        var asset  = MakeAsset();
        var drawer = new BlackboardFieldPickerDrawer(asset);

        drawer.PromoteRequested.Should().BeFalse();
    }

    [Fact]
    public void TriggerPromote_SetsFlag_ResetClearsIt()
    {
        var asset  = MakeAsset();
        var drawer = new BlackboardFieldPickerDrawer(asset);

        drawer.TriggerPromote();
        drawer.PromoteRequested.Should().BeTrue();

        drawer.ResetPromoteRequest();
        drawer.PromoteRequested.Should().BeFalse();
    }
}
