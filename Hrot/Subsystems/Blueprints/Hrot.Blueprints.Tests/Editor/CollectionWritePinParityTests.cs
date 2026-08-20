using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// FC-1 (Q#20) -- editor coverage for <see cref="CollectionWriteNode"/>:
/// <list type="bullet">
///   <item>pin PARITY per op (mirrors <see cref="ComponentCollectionConsumerPinParityTests"/>):
///   <see cref="NodePinSchema.GetCanonicalPins"/> must match <see cref="Stage0_Rehydrate"/>'s
///   <c>EnrichCollectionWritePins</c> exactly, including the empty-ElementTypeFqn fallback;</item>
///   <item>write-accessor DISCOVERY (<see cref="ComponentFieldReflector.TryReflectWriteAccessors"/>)
///   against the two FC-0 reference ops classes + the <c>[BlueprintWritable]</c> gate check;</item>
///   <item><see cref="BlueprintNodeModel"/> title / bake-incomplete / stale-ref behavior.</item>
/// </list>
/// </summary>
public sealed class CollectionWritePinParityTests
{
    // ComponentFieldReflector resolves purely by scanning LOADED assemblies (this class names
    // its targets via string FQNs only, so nothing here would otherwise trigger the load) --
    // force-load Hrot.AI.Behaviors deterministically instead of depending on a sibling test
    // class having touched one of its types first (a latent ordering flake).
    // ⭐ Batch 52: superseded by TestAssemblyModuleInit; kept as a local guard because the central
    // one fails silently. A new test class needs nothing of its own.
    static CollectionWritePinParityTests()
    {
        _ = typeof(Hrot.AI.Behaviors.BpFixedListDemo).Assembly;
    }

    private const string WritableFqn    = "Hrot.AI.Behaviors.BpFixedListDemo";
    private const string WritableOps    = "Hrot.AI.Behaviors.Brains.BpFixedListDemoOps";
    private const string NonWritableFqn = "Hrot.AI.Behaviors.BpCollectionDemo";
    private const string FixedBufOps    = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps";

    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static List<(string Name, string Direction, bool IsExec, string? TypeId, bool IsArray)> RunStage0(Node node)
    {
        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = new List<Node> { node }, Links = new List<Link>(),
            Inputs = new(), Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "ParityTest",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
        Stage0_Rehydrate.Run(asset, DefaultOptions());
        return node.Pins
            .Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId, p.TypeRef?.IsArray ?? false))
            .ToList();
    }

    private static List<(string Name, string Direction, bool IsExec, string? TypeId, bool IsArray)> RunEditor(Node node)
        => NodePinSchema.GetCanonicalPins(node)
            .Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId, p.TypeRef?.IsArray ?? false))
            .ToList();

    private static CollectionWriteNode Build(CollectionWriteOp op, string elemFqn = "System.Int32") => new()
    {
        Id               = Guid.NewGuid(),
        ComponentTypeFqn = WritableFqn,
        Op               = op,
        WriteAccessorFqn = $"{WritableOps}.{op}",
        ElementTypeFqn   = elemFqn,
    };

    // ── pin parity ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CollectionWriteOp.Add)]
    [InlineData(CollectionWriteOp.SetAt)]
    [InlineData(CollectionWriteOp.InsertAt)]
    [InlineData(CollectionWriteOp.RemoveAt)]
    [InlineData(CollectionWriteOp.Clear)]
    [InlineData(CollectionWriteOp.Resize)]
    public void EveryOp_EditorProjection_MatchesStage0Enrichment_Exactly(CollectionWriteOp op)
    {
        var fromStage0 = RunStage0(Build(op));
        var fromEditor = RunEditor(Build(op));
        Assert.Equal(fromStage0, fromEditor);
    }

    [Fact]
    public void SetAt_PinShape_IsExecCollectionIndexValueOk()
    {
        Assert.Equal(new[]
        {
            ("In",         "In",  true,  (string?)"",             false),
            ("Out",        "Out", true,  (string?)"",             false),
            ("Collection", "In",  false, (string?)"System.Int32", true),
            ("Index",      "In",  false, (string?)"System.Int32", false),
            ("Value",      "In",  false, (string?)"System.Int32", false),
            ("Ok",         "Out", false, (string?)"System.Boolean", false),
        }, RunEditor(Build(CollectionWriteOp.SetAt)));
    }

    [Fact]
    public void EmptyElementTypeFqn_FallsBackToSystemObject_BothSidesAgree()
    {
        var fromStage0 = RunStage0(Build(CollectionWriteOp.Add, elemFqn: ""));
        var fromEditor = RunEditor(Build(CollectionWriteOp.Add, elemFqn: ""));
        Assert.Equal(fromStage0, fromEditor);
        Assert.Contains(fromEditor, p => p.Name == "Value" && p.TypeId == "System.Object");
    }

    // ── write-accessor discovery + the gates ─────────────────────────────────

    [Fact]
    public void TryReflectWriteAccessors_InlineArrayReferenceOps_FindsAllSixOps()
    {
        var ops = ComponentFieldReflector.TryReflectWriteAccessors(WritableFqn, "Items");
        Assert.Equal(6, ops.Count);
        foreach (var op in Enum.GetValues<CollectionWriteOp>())
            Assert.Equal($"{WritableOps}.{op}", ops[op]);
    }

    [Fact]
    public void TryReflectWriteAccessors_FixedBufferReferenceOps_FindsAllSixOps()
    {
        var ops = ComponentFieldReflector.TryReflectWriteAccessors(NonWritableFqn, "Values");
        Assert.Equal(6, ops.Count);
        Assert.Equal($"{FixedBufOps}.Add", ops[CollectionWriteOp.Add]);
    }

    [Fact]
    public void TryReflectWriteAccessors_UnknownCollectionName_Empty()
    {
        Assert.Empty(ComponentFieldReflector.TryReflectWriteAccessors(WritableFqn, "NoSuchCollection"));
    }

    [Fact]
    public void IsWritableComponent_GateOne_TrueOnlyForBlueprintWritable()
    {
        // The FC-0 gate-1-vs-gate-2 pair: BpCollectionDemo ships write ACCESSORS but no
        // [BlueprintWritable]; BpFixedListDemo ships both.
        Assert.True(ComponentFieldReflector.IsWritableComponent(WritableFqn));
        Assert.False(ComponentFieldReflector.IsWritableComponent(NonWritableFqn));
        Assert.False(ComponentFieldReflector.IsWritableComponent("No.Such.Type"));
    }

    // ── BlueprintNodeModel: title / bake-incomplete / stale-ref ───────────────

    private static BlueprintNodeModel Model(Node node, bool collectionPinWired = false)
        => new(node, Array.Empty<IPinModel>(), null, collectionPinWired);

    [Fact]
    public void Title_UnbakedShowsVerb_BakedShowsBracketedComponent()
    {
        Assert.Equal("Set At (Collection)", Model(new CollectionWriteNode { Op = CollectionWriteOp.SetAt }).Title);
        Assert.Equal("Set At [BpFixedListDemo]", Model(Build(CollectionWriteOp.SetAt)).Title);
        Assert.Equal("Clear (Collection)", Model(new CollectionWriteNode { Op = CollectionWriteOp.Clear }).Title);
    }

    [Fact]
    public void State_WiredButUnbaked_IsError_UnwiredIsNormal()
    {
        var unbaked = new CollectionWriteNode { Op = CollectionWriteOp.Add };
        Assert.Equal(NodeState.Normal, Model(unbaked, collectionPinWired: false).State);
        Assert.Equal(NodeState.Error,  Model(unbaked, collectionPinWired: true).State);
        Assert.Equal(NodeState.Normal, Model(Build(CollectionWriteOp.Add), collectionPinWired: true).State);
    }

    [Fact]
    public void State_UnresolvedBakedComponent_IsError()
    {
        var stale = Build(CollectionWriteOp.Add);
        stale.ComponentTypeFqn = "Hrot.AI.Behaviors.RenamedAwayComponent";
        Assert.Equal(NodeState.Error, Model(stale).State);
    }
}
