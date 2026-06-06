using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Tests.Inspector;

/// <summary>
/// SE2 headless tests: verifies that <see cref="BTreePickerDrawerFactory"/> and
/// <see cref="HsmPickerDrawerFactory"/> produce per-asset custom-drawer maps that:
/// <list type="bullet">
///   <item>Contain a <c>typeof(string)</c> entry that dispatches attribute-specific sub-drawers.</item>
///   <item>Contain a <c>typeof(ushort)</c> entry for HSM sync-group fields.</item>
///   <item>Reflect the new asset after an asset-switch (sources are fresh per call).</item>
/// </list>
/// All tests are headless — no ImGui context required.
/// </summary>
public sealed class SE2_PickerDrawerRebuildTests
{
    // ── BTree helpers ─────────────────────────────────────────────────────────

    private static BehaviorTreeBlob EmptyBlob(string name = "T") => new()
    {
        TreeName        = name,
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset MakeBTreeAsset(string name = "T")
    {
        var blob = EmptyBlob(name);
        return BehaviorTreeAssetProjector.Project(
            blob, null, null,
            Guid.NewGuid(), blob.TreeName, "/t.cs", false, "", "");
    }

    private static void RegisterBehavior(BehaviorRegistry registry, string methodName, int id)
    {
        registry.Register(id, methodName, new BehaviorDefinition { Name = methodName, BrainTier = 0 });
    }

    // ── HSM helpers ───────────────────────────────────────────────────────────

    private static HsmAsset MakeHsmAsset(string name = "Test")
    {
        var b = new HsmBuilder(name);
        b.Event("Fire",  1);
        b.State("Active").Final();
        b.State("Idle").Initial()
            .OnEntry("Ns.OnEntry")
            .OnExit("Ns.OnExit")
            .On("Fire").GoTo("Active");
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flat);
        var meta = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), name, "", false, "");
    }

    // ── EditNode helpers ──────────────────────────────────────────────────────

    private static EditNode NodeWithAttr(params Attribute[] attrs) =>
        new EditNode(
            id:       new EditNodeId(0),
            name:     "Field",
            jsonPath: "$.Field",
            kind:     EditNodeKind.String,
            clrType:  typeof(string),
            metadata: new EditNodeMetadata { CustomAttributes = attrs });

    // ═════════════════════════════════════════════════════════════════════════
    // BTree factory tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BTreeFactory_BuildDrawers_ContainsStringDrawer()
    {
        var asset    = MakeBTreeAsset();
        var registry = new BehaviorRegistry();

        var drawers = BTreePickerDrawerFactory.BuildDrawers(asset, registry);

        drawers.Should().ContainKey(typeof(string),
            "BTree custom-drawers map must contain a string drawer keyed by typeof(string)");
    }

    [Fact]
    public void BTreeFactory_StringDrawer_DispatchesBehaviorHashPicker()
    {
        var registry = new BehaviorRegistry();
        RegisterBehavior(registry, "Ns.Patrol", 1);
        var asset  = MakeBTreeAsset();
        var drawers = BTreePickerDrawerFactory.BuildDrawers(asset, registry);

        var composite = drawers[typeof(string)] as CompositeStringDrawer;
        composite.Should().NotBeNull("the string drawer must be a CompositeStringDrawer");

        var node     = NodeWithAttr(new BehaviorHashPickerAttribute());
        var resolved = composite!.Resolve(node);

        resolved.Should().NotBeNull(
            "CompositeStringDrawer must resolve a sub-drawer for [BehaviorHashPicker]");
        resolved.Should().BeOfType<BehaviorHashPickerDrawer>(
            "the resolved drawer must be a BehaviorHashPickerDrawer");

        // And it must enumerate the registered behavior.
        ((BehaviorHashPickerDrawer)resolved!).GetItems().Should().Contain("Ns.Patrol");
    }

    [Fact]
    public void BTreeFactory_StringDrawer_DispatchesBlackboardFieldPicker()
    {
        var registry = new BehaviorRegistry();
        var asset    = MakeBTreeAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("Speed", typeof(float), null),
        });
        var drawers = BTreePickerDrawerFactory.BuildDrawers(asset, registry);

        var composite = drawers[typeof(string)] as CompositeStringDrawer;
        composite.Should().NotBeNull();

        var node     = NodeWithAttr(new BlackboardFieldPickerAttribute());
        var resolved = composite!.Resolve(node);

        resolved.Should().BeOfType<BlackboardFieldPickerDrawer>(
            "the resolved drawer must be a BlackboardFieldPickerDrawer");
        ((BlackboardFieldPickerDrawer)resolved!).GetItems().Should().Contain("Speed");
    }

    [Fact]
    public void BTreeFactory_AssetSwitch_DrawersReflectNewAsset()
    {
        var registry = new BehaviorRegistry();

        // First asset: has blackboard field "Health".
        var asset1 = MakeBTreeAsset("Asset1");
        asset1.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("Health", typeof(float), null),
        });
        var drawers1 = BTreePickerDrawerFactory.BuildDrawers(asset1, registry);

        // Second asset: has blackboard field "Stamina".
        var asset2 = MakeBTreeAsset("Asset2");
        asset2.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("Stamina", typeof(float), null),
        });
        var drawers2 = BTreePickerDrawerFactory.BuildDrawers(asset2, registry);

        // drawers1 must reflect asset1.
        var bbNode = NodeWithAttr(new BlackboardFieldPickerAttribute());
        var resolved1 = ((CompositeStringDrawer)drawers1[typeof(string)]).Resolve(bbNode)
            as BlackboardFieldPickerDrawer;
        resolved1!.GetItems().Should().Contain("Health").And.NotContain("Stamina",
            "drawers1 was built from asset1 which has only 'Health'");

        // drawers2 must reflect asset2 (independent from drawers1).
        var resolved2 = ((CompositeStringDrawer)drawers2[typeof(string)]).Resolve(bbNode)
            as BlackboardFieldPickerDrawer;
        resolved2!.GetItems().Should().Contain("Stamina").And.NotContain("Health",
            "drawers2 was built from asset2 which has only 'Stamina'");
    }

    [Fact]
    public void BTreeFactory_NullAsset_Throws()
    {
        var registry = new BehaviorRegistry();
        var act = () => BTreePickerDrawerFactory.BuildDrawers(null!, registry);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("asset");
    }

    [Fact]
    public void BTreeFactory_NullRegistry_Throws()
    {
        var asset = MakeBTreeAsset();
        var act = () => BTreePickerDrawerFactory.BuildDrawers(asset, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("registry");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HSM factory tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HsmFactory_BuildDrawers_ContainsStringDrawer()
    {
        var asset   = MakeHsmAsset();
        var drawers = HsmPickerDrawerFactory.BuildDrawers(asset);

        drawers.Should().ContainKey(typeof(string),
            "HSM custom-drawers map must contain a string drawer keyed by typeof(string)");
    }

    [Fact]
    public void HsmFactory_BuildDrawers_ContainsUshortDrawer()
    {
        var asset   = MakeHsmAsset();
        var drawers = HsmPickerDrawerFactory.BuildDrawers(asset);

        drawers.Should().ContainKey(typeof(ushort),
            "HSM custom-drawers map must contain a ushort drawer for sync-group fields");
        drawers[typeof(ushort)].Should().BeOfType<HsmSyncGroupPickerDrawer>();
    }

    [Fact]
    public void HsmFactory_StringDrawer_DispatchesActionPicker()
    {
        var asset   = MakeHsmAsset();
        var drawers = HsmPickerDrawerFactory.BuildDrawers(asset);

        var composite = drawers[typeof(string)] as HsmCompositeStringDrawer;
        composite.Should().NotBeNull("the HSM string drawer must be an HsmCompositeStringDrawer");

        var node     = NodeWithAttr(new HsmActionPickerAttribute());
        var resolved = composite!.Resolve(node);

        resolved.Should().NotBeNull(
            "HsmCompositeStringDrawer must resolve a sub-drawer for [HsmActionPicker]");
        resolved.Should().BeOfType<HsmActionPickerDrawer>();
        // OnEntry/OnExit actions from the "Idle" state must appear.
        ((HsmActionPickerDrawer)resolved!).GetItems()
            .Should().Contain("Ns.OnEntry").And.Contain("Ns.OnExit");
    }

    [Fact]
    public void HsmFactory_StringDrawer_DispatchesGuardPicker()
    {
        var asset   = MakeHsmAsset();
        var drawers = HsmPickerDrawerFactory.BuildDrawers(asset);

        var composite = drawers[typeof(string)] as HsmCompositeStringDrawer;
        var node      = NodeWithAttr(new HsmGuardPickerAttribute());
        var resolved  = composite!.Resolve(node);

        resolved.Should().NotBeNull("must resolve [HsmGuardPicker]");
        resolved.Should().BeOfType<HsmGuardPickerDrawer>();
    }

    [Fact]
    public void HsmFactory_StringDrawer_DispatchesStateSelector()
    {
        var asset   = MakeHsmAsset();
        var drawers = HsmPickerDrawerFactory.BuildDrawers(asset);

        var composite = drawers[typeof(string)] as HsmCompositeStringDrawer;
        var node      = NodeWithAttr(new HsmStateSelectorAttribute());
        var resolved  = composite!.Resolve(node);

        resolved.Should().NotBeNull("must resolve [HsmStateSelector]");
        resolved.Should().BeOfType<HsmStateSelectorDrawer>();
        ((HsmStateSelectorDrawer)resolved!).GetItems()
            .Should().Contain("Idle").And.Contain("Active");
    }

    [Fact]
    public void HsmFactory_StringDrawer_DispatchesEventPicker()
    {
        var asset   = MakeHsmAsset();
        var drawers = HsmPickerDrawerFactory.BuildDrawers(asset);

        var composite = drawers[typeof(string)] as HsmCompositeStringDrawer;
        var node      = NodeWithAttr(new HsmEventPickerAttribute());
        var resolved  = composite!.Resolve(node);

        resolved.Should().NotBeNull("must resolve [HsmEventPicker]");
        resolved.Should().BeOfType<HsmEventPickerDrawer>();
        ((HsmEventPickerDrawer)resolved!).GetItems().Should().Contain("Fire");
    }

    [Fact]
    public void HsmFactory_AssetSwitch_DrawersReflectNewAsset()
    {
        // First asset: has "Fire" event + Ns.OnEntry action.
        var asset1   = MakeHsmAsset("Machine1");
        var drawers1 = HsmPickerDrawerFactory.BuildDrawers(asset1);

        // Second asset: different states and events.
        var b2 = new HsmBuilder("Machine2");
        b2.Event("Shot", 10);
        b2.State("Ready").Initial().OnEntry("Ops.Initialize");
        var graph2 = b2.Build();
        HsmNormalizer.Normalize(graph2);
        var flat2 = HsmFlattener.Flatten(graph2);
        var blob2 = HsmEmitter.Emit(flat2);
        var meta2 = HsmEmitter.BuildMachineMetadata(graph2);
        var asset2 = HsmAssetProjector.Project(blob2, meta2, null, Guid.NewGuid(), "Machine2", "", false, "");
        var drawers2 = HsmPickerDrawerFactory.BuildDrawers(asset2);

        var evNode = NodeWithAttr(new HsmEventPickerAttribute());

        // drawers1: event picker returns "Fire".
        var evPickerD1 = ((HsmCompositeStringDrawer)drawers1[typeof(string)]).Resolve(evNode)
            as HsmEventPickerDrawer;
        evPickerD1!.GetItems().Should().Contain("Fire").And.NotContain("Shot",
            "drawers1 was built from asset1 which has only 'Fire'");

        // drawers2: event picker returns "Shot".
        var evPickerD2 = ((HsmCompositeStringDrawer)drawers2[typeof(string)]).Resolve(evNode)
            as HsmEventPickerDrawer;
        evPickerD2!.GetItems().Should().Contain("Shot").And.NotContain("Fire",
            "drawers2 was built from asset2 which has only 'Shot'");
    }

    [Fact]
    public void HsmFactory_NullAsset_Throws()
    {
        var act = () => HsmPickerDrawerFactory.BuildDrawers(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("asset");
    }
}
