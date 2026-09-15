using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Integration;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-126 — a newly created Function graph used to contain only an <see cref="EventEntryNode"/>.
/// In Unreal, a new function gives the author entry + return, already wired; here the author had
/// to find <c>Return</c> in the palette, place it, and wire it — miss the wire and the compiler
/// reports BP3010 (orphan) + BP1657, which is exactly the shape a user hit (their asset JSON showed
/// exactly one <c>EventEntry</c>).
///
/// <para>
/// <see cref="BlueprintDocumentFactory.CreateFunctionGraph"/> is the single choke point for every
/// "new Function graph" caller: the My Blueprint <c>+ Function</c> quick-add, the "Create Function"
/// modal (<c>FunctionCreateModal</c> via <c>BlueprintMyBlueprintWindow</c>), and
/// <c>BlueprintNewAssetService.MakeEmptyBlueprint</c> (BP-103's asset-seed graph). Fixing it there
/// covers all three without touching any of their call sites, and — because BP-103 calls it exactly
/// once per new asset — cannot double-seed.
/// </para>
/// </summary>
public sealed class BP126_NewFunctionGraphSeedingTests
{
    private static BlueprintAsset MakeAsset()
        => BlueprintAssetBuilder.Instance("BP126Host")
            .WithGraph("Main", GraphKind.Function, _ => { })
            .Build();

    // ── A newly created Function graph has both nodes, exec-linked ────────────

    [Fact]
    public void NewFunctionGraph_ContainsBothAnEntryAndAReturnNode()
    {
        var asset = MakeAsset();

        var graph = BlueprintDocumentFactory.CreateFunctionGraph(asset, "DoThing");

        Assert.NotNull(graph);
        Assert.Single(graph!.Nodes.OfType<EventEntryNode>());
        Assert.Single(graph.Nodes.OfType<ReturnNode>());
    }

    [Fact]
    public void NewFunctionGraph_EntryAndReturnAreExecLinked()
    {
        var asset = MakeAsset();

        var graph = BlueprintDocumentFactory.CreateFunctionGraph(asset, "DoThing");

        Assert.NotNull(graph);
        var entry  = graph!.Nodes.OfType<EventEntryNode>().Single();
        var ret    = graph.Nodes.OfType<ReturnNode>().Single();

        // EventEntry's exec-out ("Out") wired to Return's exec-in ("In") — the node pin schema's
        // canonical names (BuiltInNodeRegistry: EventEntryNode => ExecOut(), ReturnNode => ExecIn()).
        // Pins are not materialised at authoring time (projection-only asset), so the link addresses
        // them by the same deterministic scheme Stage0_Rehydrate/BlueprintGraphModel.Rebuild use.
        var expectedFromPin = DeterministicIds.PinId(entry.Id, "Out", "Out");
        var expectedToPin   = DeterministicIds.PinId(ret.Id,   "In",  "In");

        Assert.Contains(graph.Links, l =>
            l.FromNodeId == entry.Id && l.FromPinId == expectedFromPin &&
            l.ToNodeId   == ret.Id   && l.ToPinId   == expectedToPin);
    }

    [Fact]
    public void NewFunctionGraph_EntryAndReturnArePositionedApart()
    {
        var asset = MakeAsset();

        var graph = BlueprintDocumentFactory.CreateFunctionGraph(asset, "DoThing");

        Assert.NotNull(graph);
        var entry = graph!.Nodes.OfType<EventEntryNode>().Single();
        var ret   = graph.Nodes.OfType<ReturnNode>().Single();

        Assert.NotEqual(entry.EditorMetadata.X, ret.EditorMetadata.X);
    }

    // ── Function graphs only — Event (and Construction, when reachable) are not seeded ───────────

    /// <summary>
    /// An <b>Event</b> graph's body is built by <see cref="BlueprintDocumentFactory.CreateCustomEvent"/>,
    /// a completely different path from <see cref="BlueprintDocumentFactory.CreateFunctionGraph"/> —
    /// it never runs the Return-seeding code, so an Event graph is born with just its entry, exactly
    /// as before BP-126. (Unreal event graphs have no Return node either.)
    /// </summary>
    [Fact]
    public void NewEventGraph_IsNotSeededWithAReturnNode()
    {
        var asset = MakeAsset();

        var decl = BlueprintDocumentFactory.CreateCustomEvent(asset, "OnScored");
        Assert.NotNull(decl);
        var body = BlueprintDocumentFactory.FindCustomEventBodyGraph(asset, decl!);

        Assert.NotNull(body);
        Assert.Equal(GraphKind.Event, body!.Kind);
        Assert.Empty(body.Nodes.OfType<ReturnNode>());
    }

    /// <summary>
    /// There is no "create a Construction graph" path anywhere in the editor today (no analogue of
    /// <c>CreateFunctionGraph</c>/<c>CreateCustomEvent</c> that mints <see cref="GraphKind.Construction"/>)
    /// — grep for <c>GraphKind.Construction</c> turns up only the compiler's scheduler and the graph
    /// model's switch, never a creation site. This test pins that absence rather than fabricating a
    /// path that does not exist: if one is ever added, it must NOT reuse
    /// <see cref="BlueprintDocumentFactory.CreateFunctionGraph"/> unless it also degates to a
    /// non-Function <c>GraphKind</c>, since a Construction graph is not a function and must not get a
    /// Return node either.
    /// </summary>
    [Fact]
    public void CreateFunctionGraph_AlwaysProducesFunctionKind_NeverConstruction()
    {
        var asset = MakeAsset();

        var graph = BlueprintDocumentFactory.CreateFunctionGraph(asset, "DoThing");

        Assert.NotNull(graph);
        Assert.Equal(GraphKind.Function, graph!.Kind);
        Assert.NotEqual(GraphKind.Construction, graph.Kind);
    }

    // ── Anti-double-seed guard: a new ASSET ends up with exactly one Return ──────────────────────

    /// <summary>
    /// <see cref="BlueprintNewAssetService.MakeEmptyBlueprint"/> (BP-103) calls
    /// <see cref="BlueprintDocumentFactory.CreateFunctionGraph"/> exactly once, to seed the new
    /// asset's first (and only) graph. Since the Return-seeding now lives INSIDE
    /// <c>CreateFunctionGraph</c> rather than being layered on top by the asset-creation path, a
    /// freshly created asset gets exactly one Return node — not two.
    /// </summary>
    [Theory]
    [InlineData("Empty")]
    [InlineData("Function Library")]
    public void NewAsset_SeedGraph_HasExactlyOneReturnNode_NotTwo(string templateName)
    {
        var svc      = new BlueprintNewAssetService();
        var template = svc.AvailableRecipes().First(r => r.Name == templateName);

        var result = svc.CreateNew(template, "MyAsset", "");
        var bp     = Assert.IsType<BlueprintEditableAssetAdapter>(result).Asset;

        var graph = Assert.Single(bp.Graphs);
        Assert.Single(graph.Nodes.OfType<EventEntryNode>());
        Assert.Single(graph.Nodes.OfType<ReturnNode>());
    }

    // ── Payoff: the seeded graph compiles clean — no BP3010, no BP1657 ────────────────────────────

    /// <summary>
    /// The actual user-visible payoff: a brand-new asset's seed graph, composed exactly through
    /// <see cref="BlueprintNewAssetService"/> (the same "New Blueprint" flow a designer drives), runs
    /// through the REAL source generator and Roslyn with neither BP3010 (orphan node) nor BP1657
    /// (Library output path with no Return) — the two diagnostics the bug report's un-wired,
    /// Return-less function produced.
    /// </summary>
    [Theory]
    [InlineData(BlueprintDispatchKind.Instance)]
    [InlineData(BlueprintDispatchKind.Library)]
    public void NewAsset_SeedGraph_CompilesClean_NoOrphanNoMissingReturnWarning(
        BlueprintDispatchKind dispatch)
    {
        var asset = AuthoringPath.NewAsset("BP126CompileCheck", dispatch);

        var result = AuthoringPath.Generate(asset);

        Assert.True(result.Clean,
            $"A freshly created {dispatch} asset's seed graph did not compile clean:"
            + $"{Environment.NewLine}{result.Report()}");
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "BP3010");
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "BP1657");
    }
}
