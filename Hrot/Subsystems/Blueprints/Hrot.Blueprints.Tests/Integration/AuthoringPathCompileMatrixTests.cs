using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// <b>BP-120 — the authoring-path compile matrix.</b> Composes blueprints through the <b>editor's own
/// APIs</b> and compiles them through the <b>real source generator</b> plus <b>real Roslyn</b>.
///
/// <para>
/// ⭐ <b>Why this class is the deliverable, not the individual fixes.</b> Four consecutive batches ended
/// with a human finding, by clicking the UI, a defect that a headless test should have caught. The user
/// put it directly: <i>"i still dont understand why i need to test the stuff that can be tested
/// headlessly — like if a blueprint calling a function can be compiled — the AI agent should be able to
/// compose such a blueprint set and compile it automatically."</i> They were right every time.
/// </para>
///
/// <para>
/// Each rule below is load-bearing, and each exists because skipping it let a specific bug ship:
/// <list type="number">
///   <item><b>Compose through the editor's APIs.</b> <see cref="AuthoringPath"/> uses
///     <c>BlueprintNewAssetService</c>, <c>BlueprintCommandSink</c>, <c>GraphSignatureEditModel</c> and
///     the peer-picker session. <b>BP-116 was invisible to every test that writes
///     <c>CallablePeers</c> itself</b> — and every fixture in this repo does.</item>
///   <item><b>Compile through the real generator</b>, not <c>BlueprintTestFixture.CompileAndLoad</c>.</item>
///   <item><b>Assert on diagnostics and on a real Roslyn emit</b>, never on
///     <c>CompileResult.Succeeded</c> — which never invokes Roslyn, and is how BP-104 and BP-110 both
///     hid.</item>
///   <item><b>Sweep combinations</b> rather than testing one shape.</item>
/// </list>
/// </para>
///
/// <para>
/// ⚠ <b>The check that decides whether this suite is worth anything:</b> with BP-116 and BP-117
/// reverted, the peer-call and Library-fall-through cells <b>must</b> go red. Verified in Batch 25 —
/// see the tracker note on BP-120. If they ever stay green, this suite has stopped composing through
/// the authoring path and is worthless until fixed.
/// </para>
/// </summary>
public sealed class AuthoringPathCompileMatrixTests
{
    private const string ReturnKind = "Return";

    // ── dispatch × outputs × {explicit Return | chain ends} ───────────────────────────────────────

    [Theory]
    // Instance, void — the baseline shape.
    [InlineData(BlueprintDispatchKind.Instance, 0, true)]
    [InlineData(BlueprintDispatchKind.Instance, 0, false)]
    // Library with no outputs — the status-returning branch.
    [InlineData(BlueprintDispatchKind.Library, 0, true)]
    [InlineData(BlueprintDispatchKind.Library, 0, false)]
    // ⭐ Library WITH outputs and NO Return node — BP-117's shape. `false` here is the cell that
    // emitted a bare `return;` and produced CS0126 against generated code the author never wrote.
    [InlineData(BlueprintDispatchKind.Library, 1, false)]
    [InlineData(BlueprintDispatchKind.Library, 2, false)]
    [InlineData(BlueprintDispatchKind.Library, 3, false)]
    public void DispatchAndOutputs_AuthoredThroughTheEditor_CompileClean(
        BlueprintDispatchKind dispatch, int outputCount, bool withReturnNode)
    {
        var asset = AuthoringPath.NewAsset($"Matrix_{dispatch}_{outputCount}_{withReturnNode}", dispatch);
        var graph = asset.Graphs[0];

        for (int i = 0; i < outputCount; i++)
            AuthoringPath.AddOutput(graph, $"Out{i}", "System.Int32");

        // ⚠ BP-126 seeds every new Function graph with a Return node, exec-wired from Entry. That is
        // correct authoring behaviour, but it means `withReturnNode: false` would otherwise be a LIE —
        // the graph would still contain a Return and these cells would quietly stop covering the
        // fall-off-the-end shape BP-117 fixed, while continuing to pass. Strip the seeded node so the
        // cell tests what its name says.
        if (!withReturnNode)
        {
            graph.Nodes.RemoveAll(n => n is ReturnNode);
            graph.Links.Clear();
        }

        var result = AuthoringPath.Generate(asset);

        Assert.True(result.Clean,
            $"An asset authored entirely through the editor's own APIs "
            + $"(dispatch={dispatch}, outputs={outputCount}, explicitReturn={withReturnNode}) "
            + $"did not compile:{Environment.NewLine}{result.Report()}");
    }

    // ── the peer call — BP-116's shape ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The cell BP-116 lived in.</b> Two assets: a Library peer, and a caller whose peer is chosen
    /// through the <b>Details-panel picker session</b> — the only route a designer actually has, since
    /// the peer palette is projected from <c>CallablePeers</c> and is therefore empty until something
    /// declares one.
    ///
    /// <para>
    /// ⚠ Nothing here touches <c>caller.CallablePeers</c>. That is the entire point: before BP-116 this
    /// produced <c>BP1300: … is not in CallablePeers list</c>, and no existing test could see it because
    /// every fixture wrote that list by hand.
    /// </para>
    /// </summary>
    [Fact]
    public void PeerCall_PeerChosenThroughThePicker_CompilesAcrossTheAssetBoundary()
    {
        // ⚠ BP-126 seeds the Return node now, so this must NOT add a second one — two Returns in one
        // graph is a real (if self-inflicted) authoring error and would mask what this test is for.
        // The peer function declares NO outputs deliberately: a declared-but-unwired output is BP1655
        // (an Error), which would fail this test for a reason that has nothing to do with peer calls.
        // Crossing the asset boundary is what BP-116 is about, and a zero-output function crosses it.
        var peer = AuthoringPath.NewAsset("MatrixPeerLib", BlueprintDispatchKind.Library);

        var caller      = AuthoringPath.NewAsset("MatrixPeerCaller", BlueprintDispatchKind.Instance);
        var callerGraph = caller.Graphs[0];
        var callerSink  = AuthoringPath.Sink(caller, callerGraph);

        // Create the node the way the canvas does, then pick the peer the way the Details panel does.
        var node = AuthoringPath.AddNode(callerSink, callerGraph, "CallPeerBlueprint");
        var cpb  = Assert.IsType<CallPeerBlueprintNode>(node);

        var session = (CallPeerBlueprintNodeSession)new CallPeerBlueprintNodeDrawer(
                new DirectEditService(), new SinglePeerProvider(peer))
            .CreateSession(cpb, caller);
        session.SetPeerForTest(peer.AssetId);
        // Picking a peer deliberately clears FunctionRef (a function the new peer does not export must
        // not survive), so the designer's second gesture — choosing the function — is part of the path.
        session.SetFunctionForTest(peer.Graphs[0].Name);

        var result = AuthoringPath.Generate(peer, caller);

        Assert.True(result.Clean,
            "A peer call authored through the picker did not compile. Before BP-116 this failed with "
            + $"BP1300 because the editor never declared the peer:{Environment.NewLine}{result.Report()}");
    }

    /// <summary>
    /// ⭐ <b>BP-121 — the warning must actually reach the build.</b> A Library graph declaring outputs
    /// with no <c>Return</c> node compiles (BP1657 is a Warning, per the user's ruling) <b>and must
    /// emit that warning</b>.
    ///
    /// <para>
    /// ⚠ <b>Why this belongs in the matrix and not in a unit test.</b> The generator drained its
    /// diagnostic sink only on the failure path, so on a successful compile every warning was computed
    /// and then discarded. A unit test over the sink would have passed the whole time — the bug was in
    /// the generator's plumbing, which only the real generator exercises. This asserts the end the
    /// designer actually sees.
    /// </para>
    ///
    /// <para>
    /// ⭐ It also closes the loop on BP-117: BP1657 was downgraded to a Warning precisely so it would
    /// warn rather than block, and until BP-121 it did neither.
    /// </para>
    /// </summary>
    [Fact]
    public void LibraryFallingOffTheEnd_CompilesAndEmitsTheBP1657Warning()
    {
        var asset = AuthoringPath.NewAsset("MatrixWarnsBP1657", BlueprintDispatchKind.Library);
        var graph = asset.Graphs[0];

        // BP-126: NewAsset's seed graph now ships with a Return node exec-wired from Entry (every
        // newly created Function graph does, to close the "missing Return" authoring gap). This
        // test specifically wants the shape BP-117 fixed — an exec chain that falls off the end
        // with NO Return node anywhere in the graph — so strip the auto-seeded one back out before
        // declaring the output. Without this the Return node IS reached (with an unconnected value
        // pin), which is a different, already-covered code path (BP4001), not BP1657.
        graph.Nodes.RemoveAll(n => n is ReturnNode);
        graph.Links.Clear();

        AuthoringPath.AddOutput(graph, "Out0", "System.Int32");

        var result = AuthoringPath.Generate(asset);

        Assert.True(result.Clean,
            $"Expected a warning, not a failure:{Environment.NewLine}{result.Report()}");
        Assert.Contains(result.GeneratorDiagnostics,
            d => d.Id == "BP1657" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// The harness's own guard. If <see cref="AuthoringPath.Generate"/> ever stops actually producing
    /// code, every <c>Clean</c> assertion above would pass vacuously — no source, no errors. This pins
    /// that the generator really ran.
    /// </summary>
    [Fact]
    public void Generate_ActuallyProducesGeneratedSource_SoCleanIsNotVacuous()
    {
        var asset = AuthoringPath.NewAsset("MatrixNonVacuous", BlueprintDispatchKind.Instance);

        var result = AuthoringPath.Generate(asset);

        Assert.NotEmpty(result.GeneratedSources);
        Assert.Contains(result.GeneratedSources, src => src.Contains("_Bp"));
    }

    // ── test doubles ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Applies edits immediately; the undo stack is not what this suite is testing.</summary>
    private sealed class DirectEditService : IEditService
    {
        public void MarkDirty(BlueprintAsset asset) { }
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
            => apply();
        public void NotifyStructureChanged(BlueprintAsset asset) { }
    }

    /// <summary>Stands in for on-disk peer discovery, exposing exactly one peer.</summary>
    private sealed class SinglePeerProvider : IBlueprintPeerProvider
    {
        private readonly BlueprintPeerInfo _peer;

        public SinglePeerProvider(BlueprintAsset peer)
        {
            var exported = peer.Graphs
                .Where(g => g.Kind == GraphKind.Function)
                .Select(g => g.Name)
                .ToList();
            _peer = new BlueprintPeerInfo(peer.AssetId, peer.Name, exported);
        }

        public IReadOnlyList<BlueprintPeerInfo> GetPeers() => new[] { _peer };
    }
}
