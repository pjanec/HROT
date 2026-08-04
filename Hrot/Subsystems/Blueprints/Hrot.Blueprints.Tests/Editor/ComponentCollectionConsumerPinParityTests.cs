using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// CA-07b — pin-parity coverage for the three component-collection consumer nodes
/// (<see cref="CollectionForEachNode"/>/<see cref="CollectionItemGetNode"/>/
/// <see cref="CollectionItemCountNode"/>), mirroring <c>GetComponentPinParityTests</c> exactly:
/// proves <see cref="NodePinSchema.GetCanonicalPins"/>'s projection is byte-identical (by Name,
/// Direction, IsExec, TypeId, IsArray, in order) to the compiler's real
/// <see cref="Stage0_Rehydrate"/> enrichment for each kind, including the element-typed
/// "Collection"/"CurrentItem"/"Element" pins and the empty-<c>ElementTypeFqn</c> fallback.
/// </summary>
public sealed class ComponentCollectionConsumerPinParityTests
{
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

    // ── CollectionForEachNode ──────────────────────────────────────────────────

    [Fact]
    public void ComponentForEach_Baked_EditorProjection_MatchesStage0Enrichment_Exactly()
    {
        CollectionForEachNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "System.Int32",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("In",           "In",  true,  (string?)"",             false),
            ("Collection",   "In",  false, (string?)"System.Int32", true),
            ("Body",         "Out", true,  (string?)"",             false),
            ("Completed",    "Out", true,  (string?)"",             false),
            ("CurrentItem",  "Out", false, (string?)"System.Int32", false),
            ("CurrentIndex", "Out", false, (string?)"System.Int32", false),
            ("Count",        "Out", false, (string?)"System.Int32", false),
        }, fromEditor);
    }

    [Fact]
    public void ComponentForEach_EmptyElementTypeFqn_FallsBackToSystemObject_BothSidesAgree()
    {
        CollectionForEachNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Contains(("Collection", "In", false, (string?)"System.Object", true), fromEditor);
        Assert.Contains(("CurrentItem", "Out", false, (string?)"System.Object", false), fromEditor);
    }

    // ── CollectionItemGetNode ──────────────────────────────────────────────────

    [Fact]
    public void ComponentItemGet_Baked_EditorProjection_MatchesStage0Enrichment_Exactly()
    {
        CollectionItemGetNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "System.Int32",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("Collection", "In",  false, (string?)"System.Int32", true),
            ("Index",      "In",  false, (string?)"System.Int32", false),
            ("Element",    "Out", false, (string?)"System.Int32", false),
        }, fromEditor);
    }

    [Fact]
    public void ComponentItemGet_EmptyElementTypeFqn_FallsBackToSystemObject_BothSidesAgree()
    {
        CollectionItemGetNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("Collection", "In",  false, (string?)"System.Object", true),
            ("Index",      "In",  false, (string?)"System.Int32",  false),
            ("Element",    "Out", false, (string?)"System.Object", false),
        }, fromEditor);
    }

    // ── CollectionItemCountNode ────────────────────────────────────────────────

    [Fact]
    public void ComponentItemCount_Baked_EditorProjection_MatchesStage0Enrichment_Exactly()
    {
        CollectionItemCountNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        // No ElementTypeFqn on this node -- "Collection" is ALWAYS System.Object, regardless of bake.
        Assert.Equal(new[]
        {
            ("Collection", "In",  false, (string?)"System.Object", true),
            ("Count",      "Out", false, (string?)"System.Int32",  false),
        }, fromEditor);
    }

    // ── CollectionContainsNode ─────────────────────────────────────────────────

    [Fact]
    public void ComponentContains_Baked_EditorProjection_MatchesStage0Enrichment_Exactly()
    {
        CollectionContainsNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "System.Int32",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("Collection", "In",  false, (string?)"System.Int32", true),
            ("Item",       "In",  false, (string?)"System.Int32", false),
            ("Result",     "Out", false, (string?)"System.Boolean", false),
        }, fromEditor);
    }

    [Fact]
    public void ComponentContains_EmptyElementTypeFqn_FallsBackToSystemObject_BothSidesAgree()
    {
        CollectionContainsNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("Collection", "In",  false, (string?)"System.Object", true),
            ("Item",       "In",  false, (string?)"System.Object", false),
            ("Result",     "Out", false, (string?)"System.Boolean", false),
        }, fromEditor);
    }

    // ── CollectionFindNode ─────────────────────────────────────────────────────

    [Fact]
    public void ComponentFind_Baked_EditorProjection_MatchesStage0Enrichment_Exactly()
    {
        CollectionFindNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "System.Int32",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("Collection", "In",  false, (string?)"System.Int32", true),
            ("Item",       "In",  false, (string?)"System.Int32", false),
            ("Index",      "Out", false, (string?)"System.Int32", false),
            ("Found",      "Out", false, (string?)"System.Boolean", false),
        }, fromEditor);
    }

    [Fact]
    public void ComponentFind_EmptyElementTypeFqn_FallsBackToSystemObject_BothSidesAgree()
    {
        CollectionFindNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
            CountAccessorFqn = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count",
            ItemAccessorFqn  = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item",
            ElementTypeFqn   = "",
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("Collection", "In",  false, (string?)"System.Object", true),
            ("Item",       "In",  false, (string?)"System.Object", false),
            ("Index",      "Out", false, (string?)"System.Int32", false),
            ("Found",      "Out", false, (string?)"System.Boolean", false),
        }, fromEditor);
    }
}
