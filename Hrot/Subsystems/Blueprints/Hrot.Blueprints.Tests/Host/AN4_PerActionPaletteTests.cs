using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.ActionCatalog;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Headless tests for AN4 — Per-action palette generation (D-B: one action = one node).
///
/// Verifies:
/// 1. Given N channel-command actions, the palette registry yields exactly N
///    ChannelCommand entries each with the "ChannelCommand:{Channel}:{Action}" kind format.
/// 2. Each entry has the correct ChannelType and ActionId baked in via CreateInstance.
/// 3. Placement via the command-sink add path bakes ChannelType+ActionId on the node.
/// 4. NodePinSchema.GetCanonicalPins projects the action's param pins when catalog is present.
/// </summary>
public sealed class AN4_PerActionPaletteTests
{
    // ── Shared fake data ─────────────────────────────────────────────────────

    private const string LocoFqn   = "Fdp.Toolkit.Behavior.Components.LocomotionChannel";
    private const string WeaponFqn = "Fdp.Toolkit.Behavior.Components.WeaponChannel";

    private static readonly ChannelCommandCatalogEntry CcMoveTo =
        new("MoveTo", LocoFqn, 1, "Fdp.Toolkit.Navigation.MoveToParams");

    private static readonly ChannelCommandCatalogEntry CcFollowRoute =
        new("FollowRoute", LocoFqn, 3, "Fdp.Toolkit.Navigation.FollowRouteParams");

    private static readonly ChannelCommandCatalogEntry CcAimAndFire =
        new("AimAndFire", WeaponFqn, 1, "Fdp.Toolkit.Combat.Executors.AimAndFireParams");

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (BlueprintAsset asset, Graph graph) MakeAssetWithGraph()
    {
        var asset = BlueprintAssetBuilder.Instance("AN4TestAsset")
            .WithGraph("Main", GraphKind.Event, _ => { })
            .Build();
        return (asset, asset.Graphs[0]);
    }

    private static (BlueprintCommandSink sink,
                    BlueprintGraphModel  model)
        MakeSinkWithCatalog(IChannelCommandCatalog catalog,
                            BlueprintAsset? asset = null,
                            Graph? graph = null)
    {
        if (asset == null)
            (asset, graph) = MakeAssetWithGraph();
        else if (graph == null)
            throw new ArgumentNullException(nameof(graph));

        var typeSystem   = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var kindRegistry = BlueprintEditorBootstrap.CreatePaletteRegistry(catalog);
        var model        = new BlueprintGraphModel(asset, graph!, kindRegistry,
                               channelCommands: catalog);
        var nodeCatalog  = new BlueprintNodeCatalog(kindRegistry);
        var validator    = new BlueprintLinkValidator(model, typeSystem);
        var history      = new CommandHistory();
        var editService  = new EditService
        {
            Context = new EditServiceContext(history, _ => { })
        };

        var sink = new BlueprintCommandSink(
            asset, graph!, model, nodeCatalog, validator, history, editService,
            markDirty:       _ => { },
            channelCommands: catalog);

        return (sink, model);
    }

    // ── 1. Palette entry count ───────────────────────────────────────────────

    /// <summary>
    /// Given a catalog with N channel-command entries, ChannelCommandEntries() yields
    /// exactly N descriptors (one per action).
    /// </summary>
    [Fact]
    public void ChannelCommandEntries_NActions_YieldsNDescriptors()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo, CcFollowRoute, CcAimAndFire);

        var entries = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).ToList();

        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void ChannelCommandEntries_NullCatalog_YieldsEmpty()
    {
        var entries = BlueprintNodePaletteEntries.ChannelCommandEntries(null).ToList();

        Assert.Empty(entries);
    }

    [Fact]
    public void ChannelCommandEntries_EmptyCatalog_YieldsEmpty()
    {
        var catalog = new FakeChannelCommandCatalog(); // no entries

        var entries = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).ToList();

        Assert.Empty(entries);
    }

    // ── 2. Kind id format ────────────────────────────────────────────────────

    /// <summary>
    /// Each per-action descriptor must have Kind = "ChannelCommand:{ShortChannelName}:{ActionId}".
    /// </summary>
    [Fact]
    public void ChannelCommandEntries_KindFormat_IsChannelCommandColonChannelColonAction()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo);

        var entry = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).Single();

        Assert.Equal("ChannelCommand:LocomotionChannel:MoveTo", entry.Kind);
    }

    [Fact]
    public void ChannelCommandEntries_MultipleActions_AllKindsAreUnique()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo, CcFollowRoute, CcAimAndFire);

        var kinds = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog)
            .Select(e => e.Kind)
            .ToList();

        Assert.Equal(kinds.Distinct().Count(), kinds.Count);
    }

    [Fact]
    public void ChannelCommandEntries_AllKindsStartWithChannelCommandPrefix()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo, CcFollowRoute, CcAimAndFire);

        var entries = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).ToList();

        Assert.All(entries, e => Assert.StartsWith("ChannelCommand:", e.Kind));
    }

    // ── 3. Baking via CreateInstance ─────────────────────────────────────────

    /// <summary>
    /// CreateInstance must bake ChannelType (short channel class name) on the new node.
    /// </summary>
    [Fact]
    public void ChannelCommandEntries_CreateInstance_BakesChannelType()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo);

        var descriptor = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).Single();
        var node       = descriptor.CreateInstance() as ChannelCommandNode;

        Assert.NotNull(node);
        Assert.Equal("LocomotionChannel", node!.ChannelType);
    }

    /// <summary>
    /// CreateInstance must bake ActionId (action name string) on the new node.
    /// </summary>
    [Fact]
    public void ChannelCommandEntries_CreateInstance_BakesActionId()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo);

        var descriptor = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).Single();
        var node       = descriptor.CreateInstance() as ChannelCommandNode;

        Assert.NotNull(node);
        Assert.Equal("MoveTo", node!.ActionId);
    }

    [Fact]
    public void ChannelCommandEntries_MultipleActions_EachEntryBakesCorrectChannelTypeAndActionId()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo, CcFollowRoute, CcAimAndFire);

        var entries = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).ToList();

        var moveToEntry = entries.Single(e => e.Kind.EndsWith(":MoveTo"));
        var followEntry = entries.Single(e => e.Kind.EndsWith(":FollowRoute"));
        var aimEntry    = entries.Single(e => e.Kind.EndsWith(":AimAndFire"));

        var moveToNode  = (ChannelCommandNode)moveToEntry.CreateInstance();
        var followNode  = (ChannelCommandNode)followEntry.CreateInstance();
        var aimNode     = (ChannelCommandNode)aimEntry.CreateInstance();

        Assert.Equal("LocomotionChannel", moveToNode.ChannelType);
        Assert.Equal("MoveTo",            moveToNode.ActionId);

        Assert.Equal("LocomotionChannel", followNode.ChannelType);
        Assert.Equal("FollowRoute",       followNode.ActionId);

        Assert.Equal("WeaponChannel",     aimNode.ChannelType);
        Assert.Equal("AimAndFire",        aimNode.ActionId);
    }

    [Fact]
    public void ChannelCommandEntries_CreateInstance_EachCallProducesNewId()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo);
        var descriptor = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).Single();

        var n1 = (ChannelCommandNode)descriptor.CreateInstance();
        var n2 = (ChannelCommandNode)descriptor.CreateInstance();

        Assert.NotEqual(n1.Id, n2.Id);
    }

    // ── 4. Registry registration ─────────────────────────────────────────────

    /// <summary>
    /// CreatePaletteRegistry with a catalog containing N actions registers exactly N
    /// ChannelCommand palette entries (all with Kind starting with "ChannelCommand:").
    /// </summary>
    [Fact]
    public void CreatePaletteRegistry_WithCatalog_RegistersAllChannelCommandEntries()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo, CcFollowRoute, CcAimAndFire);

        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry(catalog);

        // Count registry entries with ChannelCommand: prefix using TryGet on the expected kinds.
        var expectedKinds = new[]
        {
            "ChannelCommand:LocomotionChannel:MoveTo",
            "ChannelCommand:LocomotionChannel:FollowRoute",
            "ChannelCommand:WeaponChannel:AimAndFire",
        };

        foreach (var kind in expectedKinds)
        {
            Assert.True(registry.TryGet(kind) != null,
                $"Expected kind '{kind}' to be registered in the palette registry.");
        }
    }

    [Fact]
    public void CreatePaletteRegistry_WithNullCatalog_NoChannelCommandKindsRegistered()
    {
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry(null);

        // The old generic "ChannelCommand" entry must not exist either (AN4).
        Assert.True(registry.TryGet("ChannelCommand") == null,
            "Generic 'ChannelCommand' kind must not be registered (AN4: D-B, no chameleon).");
    }

    // ── 5. Placement via command sink bakes ChannelType+ActionId ─────────────

    /// <summary>
    /// Placing a per-action node via sink.Apply(AddNode) must result in a ChannelCommandNode
    /// with ChannelType and ActionId baked from the factory (not from InitialProperties).
    /// </summary>
    [Fact]
    public void SinkAddNode_PerActionKind_BakesChannelTypeOnNode()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo);
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, _) = MakeSinkWithCatalog(catalog, asset, graph);

        var pinIds = Enumerable.Range(0, 8)
            .Select(_ => new PinId(Guid.NewGuid()))
            .ToList();

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("ChannelCommand:LocomotionChannel:MoveTo"),
            Vector2.Zero,
            new Dictionary<string, object?> { ["PinIds"] = (IReadOnlyList<PinId>)pinIds }));

        Assert.True(result.Success);
        var node = graph.Nodes.Last() as ChannelCommandNode;
        Assert.NotNull(node);
        Assert.Equal("LocomotionChannel", node!.ChannelType);
    }

    [Fact]
    public void SinkAddNode_PerActionKind_BakesActionIdOnNode()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo);
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, _) = MakeSinkWithCatalog(catalog, asset, graph);

        var pinIds = Enumerable.Range(0, 8)
            .Select(_ => new PinId(Guid.NewGuid()))
            .ToList();

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("ChannelCommand:LocomotionChannel:MoveTo"),
            Vector2.Zero,
            new Dictionary<string, object?> { ["PinIds"] = (IReadOnlyList<PinId>)pinIds }));

        Assert.True(result.Success);
        var node = graph.Nodes.Last() as ChannelCommandNode;
        Assert.NotNull(node);
        Assert.Equal("MoveTo", node!.ActionId);
    }

    // ── 6. NodePinSchema projects param pins for baked action ─────────────────

    /// <summary>
    /// After placement the node must have more than 2 pins (exec-only would be exactly 2).
    /// The BuiltIn catalog provides MoveTo which has MoveToParams — NodePinSchema should
    /// project at least one data-IN pin beyond the two exec pins.
    /// Uses BuiltInChannelCommandCatalog to get the real params type FQN.
    /// </summary>
    [Fact]
    public void SinkAddNode_BuiltInMoveTo_ProjectsParamPinsViaNodePinSchema()
    {
        // Use the real built-in catalog so MoveToParams type FQN is correct
        // and NodePinSchema can find it (in the net8 test host).
        var catalog = BuiltInChannelCommandCatalog.Instance;
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, _) = MakeSinkWithCatalog(catalog, asset, graph);

        var pinIds = Enumerable.Range(0, 8)
            .Select(_ => new PinId(Guid.NewGuid()))
            .ToList();

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("ChannelCommand:LocomotionChannel:MoveTo"),
            Vector2.Zero,
            new Dictionary<string, object?> { ["PinIds"] = (IReadOnlyList<PinId>)pinIds }));

        Assert.True(result.Success);
        var node = graph.Nodes.Last() as ChannelCommandNode;
        Assert.NotNull(node);

        // Must have more than exec-only (execIn + execOut = 2).
        Assert.True(node!.Pins.Count > 2,
            $"Expected >2 pins (exec + param data-IN) for MoveTo but got {node.Pins.Count}.");
    }

    /// <summary>
    /// A manually-constructed ChannelCommandNode with ChannelType+ActionId already set
    /// must expose param pins via the model (NodePinSchema projection path).
    /// </summary>
    [Fact]
    public void ModelRebuild_ChannelCommandNode_ProjectsParamPins()
    {
        var catalog = BuiltInChannelCommandCatalog.Instance;
        var (asset, graph) = MakeAssetWithGraph();
        var (_, model)     = MakeSinkWithCatalog(catalog, asset, graph);

        var ccNode = new ChannelCommandNode
        {
            Id          = Guid.NewGuid(),
            ChannelType = "LocomotionChannel",
            ActionId    = "MoveTo",
        };
        graph.Nodes.Add(ccNode);
        model.RebuildAndNotify();

        var modelNode = model.FindNode(new NodeId(ccNode.Id));
        Assert.NotNull(modelNode);
        Assert.True(modelNode!.Pins.Count > 2,
            $"Model should project param data-IN pins for MoveTo. Got {modelNode.Pins.Count}.");
    }

    // ── 7. Display name + category format ─────────────────────────────────────

    [Fact]
    public void ChannelCommandEntries_DisplayName_IsFriendlyChannelSlashAction()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo);

        var entry = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).Single();

        // "LocomotionChannel" → strip "Channel" suffix → "Locomotion / MoveTo"
        Assert.Equal("Locomotion / MoveTo", entry.DisplayName);
    }

    [Fact]
    public void ChannelCommandEntries_Category_IsChannelFriendlyName()
    {
        var catalog = new FakeChannelCommandCatalog(CcMoveTo);

        var entry = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).Single();

        Assert.Equal("Channel/Locomotion", entry.Category);
    }

    [Fact]
    public void ChannelCommandEntries_WeaponChannel_DisplayNameStripsChannelSuffix()
    {
        var catalog = new FakeChannelCommandCatalog(CcAimAndFire);

        var entry = BlueprintNodePaletteEntries.ChannelCommandEntries(catalog).Single();

        Assert.Equal("Weapon / AimAndFire", entry.DisplayName);
        Assert.Equal("Channel/Weapon", entry.Category);
    }

    // ── 8. BuiltIn catalog: 5 entries → 5 palette kinds ─────────────────────

    [Fact]
    public void CreatePaletteRegistry_WithBuiltInCatalog_RegistersFiveChannelCommandKinds()
    {
        var catalog = BuiltInChannelCommandCatalog.Instance;
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry(catalog);

        var expectedKinds = new[]
        {
            "ChannelCommand:LocomotionChannel:MoveTo",
            "ChannelCommand:LocomotionChannel:FollowRoute",
            "ChannelCommand:WeaponChannel:AimAndFire",
            "ChannelCommand:InteractionChannel:OpenDoor",
            "ChannelCommand:InteractionChannel:EjectPassengers",
        };

        foreach (var kind in expectedKinds)
        {
            Assert.True(registry.TryGet(kind) != null,
                $"Expected '{kind}' in palette registry.");
        }

        // Generic "ChannelCommand" must NOT be present (AN4 D-B).
        Assert.True(registry.TryGet("ChannelCommand") == null,
            "Generic 'ChannelCommand' kind must not exist after AN4.");
    }

    // ── AN7: Non-channel action palette entries ───────────────────────────────

    /// <summary>Stub IBehaviorActionCatalog backed by a fixed list of entries.</summary>
    private sealed class StubBehaviorActionCatalog : IBehaviorActionCatalog
    {
        private readonly IReadOnlyList<BehaviorActionEntry> _entries;
        public StubBehaviorActionCatalog(params BehaviorActionEntry[] entries) => _entries = entries;
        public IReadOnlyList<BehaviorActionEntry> GetActions() => _entries;
        public IReadOnlyList<BehaviorActionEntry> GetActions(BehaviorActionHosts host)
        {
            var result = new System.Collections.Generic.List<BehaviorActionEntry>();
            foreach (var e in _entries)
                if ((e.ValidHosts & host) != 0)
                    result.Add(e);
            return result;
        }
        public event Action? Changed { add { } remove { } }
    }

    private static BehaviorActionEntry MakeNonChannelEntry(string fqn, string category = "FakeActions") =>
        new BehaviorActionEntry(
            Id:             fqn,
            DisplayName:    fqn.Split('.').Last(),
            Category:       category,
            ChannelTypeFqn: null,
            ActionId:       0,
            ParamsTypeFqn:  "System.Object",
            ValidHosts:     BehaviorActionHosts.Blueprint | BehaviorActionHosts.BTree | BehaviorActionHosts.Hsm,
            Source:         BehaviorActionSource.Hardcoded);

    /// <summary>
    /// AN7: NonChannelActionEntries yields one descriptor per Blueprint-valid non-channel entry.
    /// </summary>
    [Fact]
    public void NonChannelActionEntries_NActions_YieldsNDescriptors_AN7()
    {
        var catalog = new StubBehaviorActionCatalog(
            MakeNonChannelEntry("Foo.Ns.Actions.DoThing"),
            MakeNonChannelEntry("Foo.Ns.Actions.DoOther"));

        var entries = BlueprintNodePaletteEntries.NonChannelActionEntries(catalog).ToList();

        Assert.Equal(2, entries.Count);
    }

    /// <summary>
    /// AN7: NonChannelActionEntries kind = "Action:{FQN}", unique per entry.
    /// </summary>
    [Fact]
    public void NonChannelActionEntries_KindFormat_IsActionColonFqn_AN7()
    {
        var fqn     = "Foo.Ns.Actions.DoThing";
        var catalog = new StubBehaviorActionCatalog(MakeNonChannelEntry(fqn));

        var entry = BlueprintNodePaletteEntries.NonChannelActionEntries(catalog).Single();

        Assert.Equal($"Action:{fqn}", entry.Kind);
    }

    /// <summary>
    /// AN7: CreateInstance bakes ActionFqn, leaves ChannelType/ActionId empty (D-B).
    /// </summary>
    [Fact]
    public void NonChannelActionEntries_CreateInstance_BakesActionFqn_AN7()
    {
        var fqn     = "Foo.Ns.Actions.DoThing";
        var catalog = new StubBehaviorActionCatalog(MakeNonChannelEntry(fqn));

        var descriptor = BlueprintNodePaletteEntries.NonChannelActionEntries(catalog).Single();
        var node       = descriptor.CreateInstance() as ChannelCommandNode;

        Assert.NotNull(node);
        Assert.Equal(fqn, node!.ActionFqn);         // baked FQN
        Assert.Equal("",  node.ChannelType);         // non-channel: no channel type
        Assert.Equal("",  node.ActionId);            // non-channel: no action id
    }

    /// <summary>
    /// AN7: Channel-command entries in the unified catalog are NOT emitted by
    /// NonChannelActionEntries (they belong to ChannelCommandEntries).
    /// </summary>
    [Fact]
    public void NonChannelActionEntries_SkipsChannelCommandEntries_AN7()
    {
        var channelEntry = new BehaviorActionEntry(
            Id:             "Fdp.Toolkit.Behavior.Components.LocomotionChannel::1",
            DisplayName:    "MoveTo",
            Category:       "Locomotion",
            ChannelTypeFqn: "Fdp.Toolkit.Behavior.Components.LocomotionChannel",
            ActionId:       1,
            ParamsTypeFqn:  "Fdp.Toolkit.Navigation.MoveToParams",
            ValidHosts:     BehaviorActionHosts.Blueprint,
            Source:         BehaviorActionSource.ChannelCommand); // CHANNEL COMMAND → skip

        var catalog = new StubBehaviorActionCatalog(channelEntry);

        var entries = BlueprintNodePaletteEntries.NonChannelActionEntries(catalog).ToList();

        Assert.Empty(entries); // channel commands filtered out
    }

    /// <summary>
    /// AN7: CreatePaletteRegistry with a behavior-action catalog registers non-channel
    /// action kinds in addition to channel-command kinds.
    /// </summary>
    [Fact]
    public void CreatePaletteRegistry_WithBehaviorActionCatalog_RegistersNonChannelKinds_AN7()
    {
        var fqn = "Foo.Ns.Actions.DoThing";
        var behaviorCatalog = new StubBehaviorActionCatalog(MakeNonChannelEntry(fqn));

        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry(
            channelCatalog:        null,
            behaviorActionCatalog: behaviorCatalog);

        // The non-channel action kind must be present.
        var descriptor = registry.TryGet($"Action:{fqn}");
        Assert.NotNull(descriptor);

        // And the created node must have ActionFqn baked.
        var node = descriptor!.CreateInstance() as ChannelCommandNode;
        Assert.NotNull(node);
        Assert.Equal(fqn, node!.ActionFqn);
    }

    /// <summary>
    /// AN7: NonChannelActionEntries with null catalog yields empty sequence (no throw).
    /// </summary>
    [Fact]
    public void NonChannelActionEntries_NullCatalog_YieldsEmpty_AN7()
    {
        var entries = BlueprintNodePaletteEntries.NonChannelActionEntries(null).ToList();

        Assert.Empty(entries);
    }
}
