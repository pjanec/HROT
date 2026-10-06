using Fdp.Toolkit.Utility;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐ BP-27 / CE-3083 (<c>docs/DESIGN_Decision_Layer.md</c> §3.3d) — the Score Decision's Details editor: a picker over the
/// <see cref="UtilityDecisionCatalog"/>'s authored decisions, storing the decision's ASSET ID (what the compiler hashes).
/// </summary>
public sealed class ScoreDecisionNodeDrawerTests
{
    private sealed class RecordingEditService : IEditService
    {
        public int Edits;
        public Action? LastUndo;
        public void MarkDirty(BlueprintAsset asset) { }
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        { apply(); Edits++; LastUndo = undo; }
        public void NotifyStructureChanged(BlueprintAsset asset) { }
    }

    private sealed class NullPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileComponentPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root) => (_, _) => false;
        public IReadOnlyList<Type> ExtractMandatoryComponents(Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root) => [];
    }

    private static readonly (string Name, string AssetId)[] Decisions =
        { ("Posture", "id-posture"), ("Approach", "id-approach"), ("PostureFast", "id-fast") };
    private static BlueprintAsset Asset() => new() { AssetId = Guid.NewGuid(), Name = "Host", Dispatch = BlueprintDispatchKind.Behavior };

    private static ScoreDecisionNodeSession Session(ScoreDecisionNode node, IEditService edits)
        => (ScoreDecisionNodeSession)new ScoreDecisionNodeDrawer(edits, () => Decisions).CreateSession(node, Asset());

    [Fact]
    public void BP27_TheDrawer_HandlesAScoreDecision_Only()
    {
        var drawer = new ScoreDecisionNodeDrawer(new RecordingEditService());
        Assert.True(drawer.Handles(new ScoreDecisionNode()));
        Assert.False(drawer.Handles(new BranchNode()));
    }

    [Fact]
    public void BP27_ThePicker_ListsTheDecisions_FilteredByName()
    {
        var s = Session(new ScoreDecisionNode(), new RecordingEditService());
        Assert.Equal(3, s.GetFilteredDecisionsForTest("").Count);
        Assert.Equal(new[] { "id-posture", "id-fast" }, s.GetFilteredDecisionsForTest("posture").Select(d => d.AssetId));
    }

    /// <summary>⭐ Picking stores the ASSET ID, is ONE undoable edit, and the label shows the decision's name.</summary>
    [Fact]
    public void BP27_Picking_StoresTheAssetId_AsOneUndoableEdit()
    {
        var node = new ScoreDecisionNode { AssetId = "id-posture" };
        var edits = new RecordingEditService();
        var s = Session(node, edits);
        Assert.Equal("Posture", s.CurrentLabelForTest());
        s.SetDecisionForTest("id-approach");
        Assert.Equal("id-approach", node.AssetId);
        Assert.Equal("Approach", s.CurrentLabelForTest());
        Assert.True(s.IsDirty);
        Assert.Equal(1, edits.Edits);
        edits.LastUndo!();
        Assert.Equal("id-posture", node.AssetId);
        s.SetDecisionForTest("id-posture");             // the same id is not an edit
        Assert.Equal(1, edits.Edits);
    }

    /// <summary>⚠ An id the catalog does not know is KEPT and flagged, never dropped.</summary>
    [Fact]
    public void BP27_AnUnlistedId_IsKeptAndFlagged()
    {
        var node = new ScoreDecisionNode { AssetId = "id-gone" };
        var s = Session(node, new RecordingEditService());
        Assert.True(s.IsCurrentUnlistedForTest());
        Assert.Equal("id-gone", s.CurrentLabelForTest());
        Assert.Equal("id-gone", node.AssetId);
    }

    /// <summary>
    /// ⭐⭐ The production list IS the catalog: every authored decision, by name, with the id the compiler hashes into the
    /// id it registers under (<see cref="UtilityDecisionCatalog.ComputeId"/>) — so a picked decision resolves by construction.
    /// </summary>
    [Fact]
    public void BP27_TheCatalogList_CarriesEveryAuthoredDecision_ByTheIdItRegistersUnder()
    {
        var list = ScoreDecisionNodeDrawer.CatalogDecisions();
        Assert.Contains(list, d => d.AssetId == "3c6f9e42-5d10-6f3a-ac23-posture0000001");
        foreach (var (_, assetId) in list)
        {
            Assert.True(UtilityDecisionCatalog.Shared.TryGet(UtilityDecisionCatalog.ComputeId(assetId), out var def, out _));
            Assert.Equal(assetId, def!.AssetId);
        }
    }

    /// <summary>
    /// ⭐ The forwarding rail: the registry builds this drawer for a Score Decision, over the catalog (no host argument —
    /// the catalog is process-wide, so there is nothing a host could hold and fail to pass).
    /// </summary>
    [Fact]
    public void BP27_TheRegistry_BuildsTheDecisionPicker_OverTheCatalog()
    {
        var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            BuiltInChannelCommandCatalog.Instance, BuiltInEngineEventCatalog.Instance, new RecordingEditService(),
            new NullPredicateCompiler(), new EqsTemplateRegistry());
        var drawer = registry.GetDrawerFor(new ScoreDecisionNode());
        Assert.IsType<ScoreDecisionNodeDrawer>(drawer);
        var s = (ScoreDecisionNodeSession)drawer!.CreateSession(new ScoreDecisionNode(), Asset());
        Assert.Equal(ScoreDecisionNodeDrawer.CatalogDecisions().Count, s.GetFilteredDecisionsForTest("").Count);
    }

    /// <summary>
    /// ⭐⭐ Staleness rail (like <c>ShippedSensorDeclsTests</c>): every SHIPPED Score Decision names a decision the catalog
    /// registers today — a renamed / removed decision fails here, not as a silent no-op at runtime.
    /// </summary>
    [Fact]
    public void BP27_EveryShippedScoreDecision_ResolvesInTheCatalog()
    {
        UtilityDecisionCatalog.EnsureRegistered();
        int seen = 0;
        foreach (var file in Golden.GoldenCorpus.EnumerateFiles())
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            foreach (var g in root["Graphs"]!.AsArray())
                foreach (var n in g!["Nodes"]!.AsArray())
                {
                    if ((string?)n!["kind"] != "ScoreDecision") continue;
                    seen++;
                    var assetId = (string?)n["AssetId"] ?? "";
                    Assert.True(UtilityDecisionCatalog.Shared.TryGet(UtilityDecisionCatalog.ComputeId(assetId), out _, out _),
                        $"{Path.GetFileName(file)}: ScoreDecision '{assetId}' is not in the decision catalog.");
                }
        }
        Assert.True(seen > 0, "no shipped ScoreDecision found — the rail is vacuous");
    }
}
