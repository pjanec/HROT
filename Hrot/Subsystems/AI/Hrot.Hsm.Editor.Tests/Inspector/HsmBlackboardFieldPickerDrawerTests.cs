using System;
using System.Collections.Generic;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
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
/// B-1 HSM drawer: type-filtering + Promote (B-2) headless tests.
/// </summary>
public sealed class HsmBlackboardFieldPickerDrawerTests
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

    // ── B-1: type filtering ───────────────────────────────────────────────────

    [Fact]
    public void GetItems_ReturnsOnlyCompatibleVars_ForKnownFqn()
    {
        var asset    = MakeAsset(Var("floatField", typeof(float)), Var("intField", typeof(int)));
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new HsmFacetFqnContext { CurrentActionFqn = "Ns.FloatAction" };
        var drawer   = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.GetItems().Should().ContainSingle().Which.Should().Be("floatField");
    }

    [Fact]
    public void GetItems_ReturnsAllVars_ForUnknownFqn()
    {
        var asset    = MakeAsset(Var("a", typeof(float)), Var("b", typeof(int)));
        var exporter = new StubExporter();
        var ctx      = new HsmFacetFqnContext { CurrentActionFqn = "Unknown.Action" };
        var drawer   = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.GetItems().Should().HaveCount(2);
    }

    [Fact]
    public void GetItems_ReturnsAllVars_WhenNoExporterConfigured()
    {
        var asset  = MakeAsset(Var("Speed", typeof(float)));
        var drawer = new HsmBlackboardFieldPickerDrawer(asset);

        drawer.GetItems().Should().Contain("Speed");
    }

    [Fact]
    public void GetItems_ReturnsEmpty_AndHasNoCompatible_WhenNoMatchingVars()
    {
        var asset    = MakeAsset(Var("intField", typeof(int)));
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new HsmFacetFqnContext { CurrentActionFqn = "Ns.FloatAction" };
        var drawer   = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.GetItems().Should().BeEmpty();
        drawer.HasNoCompatibleVariables.Should().BeTrue();
    }

    [Fact]
    public void HasNoCompatibleVariables_False_WhenFqnUnknown()
    {
        var asset    = MakeAsset(Var("x", typeof(int)));
        var exporter = new StubExporter();
        var ctx      = new HsmFacetFqnContext { CurrentActionFqn = "Unknown.Action" };
        var drawer   = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        drawer.HasNoCompatibleVariables.Should().BeFalse();
    }

    // ── B-2: Promote ─────────────────────────────────────────────────────────

    [Fact]
    public void Promote_CreatesAutoVar_WithCorrectNameAndType_AndIsAutoManaged()
    {
        var asset    = MakeAsset();
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new HsmFacetFqnContext { CurrentActionFqn = "Ns.FloatAction" };
        var drawer   = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        var visualId   = Guid.NewGuid();
        var resultName = drawer.Promote(visualId.ToString());

        resultName.Should().Be($"_auto_{visualId:N}");
        var created = asset.BlackboardVariables.Should().ContainSingle().Subject;
        created.FieldType.Should().Be(typeof(float));
        created.IsAutoManaged.Should().BeTrue();
    }

    [Fact]
    public void Promote_Idempotent_WhenVarAlreadyExists()
    {
        var asset    = MakeAsset();
        var entry    = new ActionSchemaEntry("Ns.IntAction", typeof(int), ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new HsmFacetFqnContext { CurrentActionFqn = "Ns.IntAction" };
        var drawer   = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        var visualId = Guid.NewGuid();
        drawer.Promote(visualId.ToString());
        drawer.Promote(visualId.ToString());

        asset.BlackboardVariables.Should().HaveCount(1);
    }

    [Fact]
    public void Promote_ReturnsNull_WhenFqnNotResolvable()
    {
        var asset    = MakeAsset();
        var exporter = new StubExporter();
        var ctx      = new HsmFacetFqnContext { CurrentActionFqn = "Unknown.Action" };
        var drawer   = new HsmBlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn);

        var result = drawer.Promote(Guid.NewGuid().ToString());

        result.Should().BeNull();
        asset.BlackboardVariables.Should().BeEmpty();
    }

    // ── PromoteRequested flag ─────────────────────────────────────────────────

    [Fact]
    public void TriggerPromote_SetsFlag_ResetClearsIt()
    {
        var asset  = MakeAsset();
        var drawer = new HsmBlackboardFieldPickerDrawer(asset);

        drawer.PromoteRequested.Should().BeFalse();
        drawer.TriggerPromote();
        drawer.PromoteRequested.Should().BeTrue();
        drawer.ResetPromoteRequest();
        drawer.PromoteRequested.Should().BeFalse();
    }

    // ── Factory registration ──────────────────────────────────────────────────

    [Fact]
    public void HsmFactory_StringDrawer_DispatchesBlackboardFieldPicker()
    {
        var asset    = MakeAsset(Var("Speed", typeof(float)));
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new HsmFacetFqnContext { CurrentActionFqn = "Ns.FloatAction" };
        var drawers  = HsmPickerDrawerFactory.BuildDrawers(asset, exporter, ctx);

        var composite = drawers[typeof(string)] as HsmCompositeStringDrawer;
        composite.Should().NotBeNull();

        var meta     = new StructEdit.Core.EditNodeMetadata { CustomAttributes = new[] { new HsmBlackboardFieldPickerAttribute() } };
        var editNode = new StructEdit.Core.EditNode(
            id:       new StructEdit.Core.EditNodeId(0),
            name:     "F",
            jsonPath: "$.F",
            kind:     StructEdit.Core.EditNodeKind.String,
            clrType:  typeof(string),
            metadata: meta);
        var resolved = composite!.Resolve(editNode);

        resolved.Should().BeOfType<HsmBlackboardFieldPickerDrawer>();
        ((HsmBlackboardFieldPickerDrawer)resolved!).GetItems().Should().Contain("Speed");
    }
}
