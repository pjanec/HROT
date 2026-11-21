using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Golden;

namespace Hrot.Blueprints.Tests.Authoring;

/// <summary>
/// ⭐ <c>R-2006</c> — the authored <c>SubordinateForEachDemo.bp.json</c> is exactly what <see cref="SubordinateForEachDemoAuthoring"/>
/// builds, it compiles with no diagnostic, and it carries every node kind it exists to prove. Regenerate with
/// <c>SUBORDINATE_FOREACH_REGENERATE=1</c>. Its runtime rail is <c>Hrot.SimHost.Tests.SubordinateForEachDemoTests</c>.
/// </summary>
public class SubordinateForEachDemoAuthoringTests
{
    internal static string AssetPath()
    {
        return Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder),
            "Samples", SubordinateForEachDemoAuthoring.Name + ".bp.json");
    }

    private static string Diags(CompileResult r)
        => string.Join("\n", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    [Fact]
    public void TheAuthoredAsset_Compiles()
    {
        var result = new BlueprintCompiler().Compile(SubordinateForEachDemoAuthoring.Build(), GoldenCorpus.Options());
        Assert.True(result.Succeeded, Diags(result));
        Assert.True(!result.Diagnostics.Any(), "no diagnostics expected:\n" + Diags(result));
    }

    /// <summary>The node usages <c>PlatoonHillAttackBp</c> was the only corpus asset to carry (design §13.5), incl. the fixed-list writes — this asset keeps them
    /// in the corpus, so retiring that asset loses no real-build coverage.</summary>
    [Fact]
    public void TheAsset_CarriesTheNodeKindsItReplaces()
    {
        var kinds = SubordinateForEachDemoAuthoring.Build().Graphs.SelectMany(g => g.Nodes).Select(n => n.GetType()).ToHashSet();
        Assert.Contains(typeof(FlowForEachNode), kinds);
        Assert.Contains(typeof(SendIntentNode), kinds);
        Assert.Contains(typeof(CastNode), kinds);
        Assert.Contains(typeof(NotNode), kinds);
        Assert.Contains(typeof(GetTimeNode), kinds);
        Assert.Contains(typeof(ComponentForEachNode), kinds);
        Assert.Contains(typeof(ComponentItemGetNode), kinds);
        Assert.Contains(typeof(ListWriteNode), kinds);
        Assert.Contains(typeof(MakeStructNode), kinds);
        Assert.Contains(typeof(BreakStructNode), kinds);
        var ops = SubordinateForEachDemoAuthoring.Build().Graphs.SelectMany(g => g.Nodes).OfType<ListWriteNode>().Select(n => n.Op).ToHashSet();
        Assert.Superset(new HashSet<CollectionWriteOp> { CollectionWriteOp.Clear, CollectionWriteOp.Resize, CollectionWriteOp.SetAt, CollectionWriteOp.Add }, ops);
    }

    [Fact]
    public void TheAssetOnDisk_IsWhatTheAuthoringBuilds()
    {
        var json = BlueprintJsonServices.Serialize(SubordinateForEachDemoAuthoring.Build());
        var path = AssetPath();
        if (Environment.GetEnvironmentVariable("SUBORDINATE_FOREACH_REGENERATE") == "1")
            File.WriteAllText(path, json);
        Assert.True(File.Exists(path), $"{path} missing — run with SUBORDINATE_FOREACH_REGENERATE=1");
        Assert.Equal(json, File.ReadAllText(path));
    }
}
