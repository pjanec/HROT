using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Golden;

namespace Hrot.Blueprints.Tests.Authoring;

/// <summary>
/// ⭐ <c>CE-464</c> — the authored <c>PlatoonHillAttackBp.bp.json</c> is exactly what <see cref="PlatoonHillAttackBpAuthoring"/>
/// builds, and it compiles. Regenerate with <c>HILL_ATTACK_BP_REGENERATE=1</c> (the Blueprints-golden convention).
/// </summary>
public class PlatoonHillAttackBpAuthoringTests
{
    internal static string AssetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "Blueprints",
            PlatoonHillAttackBpAuthoring.Name + ".bp.json");
    }

    private static string Diags(CompileResult r)
        => string.Join("\n", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    [Fact]
    public void TheAuthoredAsset_Compiles()
    {
        var result = new BlueprintCompiler().Compile(PlatoonHillAttackBpAuthoring.Build(), GoldenCorpus.Options());
        Assert.True(result.Succeeded, Diags(result));
        // ⛔ BP4004 is a WARNING that drops a node from the generated code (e.g. a Return inside a loop body) — none allowed.
        Assert.True(!result.Diagnostics.Any(), "no diagnostics expected:\n" + Diags(result));
    }

    [Fact]
    public void TheAssetOnDisk_IsWhatTheAuthoringBuilds()
    {
        var json = BlueprintJsonServices.Serialize(PlatoonHillAttackBpAuthoring.Build());
        var path = AssetPath();
        if (Environment.GetEnvironmentVariable("HILL_ATTACK_BP_REGENERATE") == "1")
            File.WriteAllText(path, json);
        Assert.True(File.Exists(path), $"{path} missing — run with HILL_ATTACK_BP_REGENERATE=1");
        Assert.Equal(json, File.ReadAllText(path));
    }
}
