using System.Text.RegularExpressions;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// CE-3340 (3) / R-3066 - <b>every host seeds the NAS through ONE place</b>: <c>SubsystemOrchestrator.Initialize</c> calls
/// <c>ContentSeed.SeedIntoNas</c>; no host (the cluster runner, the editor inside it, the in-process test harnesses) calls it - and no host
/// starts a subsystem around the orchestrator. Pattern: <c>EveryEcsHostComposesOneClusterHandlerSetRails</c> (a source scan of the composition roots; the behaviour itself
/// is <c>TheContentSeedRunsAtTheOneHostStartTests</c>). Before: four call sites (<c>Program.cs</c>, <c>EditorSubsystem.Initialize</c>, two harness static constructors).
/// </summary>
public sealed class EveryHostSeedsThroughTheOneHostStartRails
{
    private static readonly Regex NotProduction = new(
        @"[\\/](obj|bin|ExtDeps|TestShared)[\\/]|Tests?[\\/]|Tests?\.cs$|Harness\.cs$|Benchmark", RegexOptions.Compiled);

    private const string TheHostStart = "FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs";
    private const string TheSeed      = "FDP/Toolkits/Fdp.Toolkits/Orchestration/ContentSeed.cs";

    /// <summary>
    /// The ONE host start that is not the orchestrator: the Stride desktop app hosts <c>EditorSubsystem</c> itself (its own composition root; its config differs from the
    /// orchestrator's), so it seeds on its own - named here so a THIRD one cannot appear unnoticed. (Windows-only; not runnable in the cloud - see DESIGN_Content_Layout 5b D-14.)
    /// </summary>
    private const string TheStrideHostStart = "Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs";

    private static string Root()
        => ContentLayout.TryFindSourceTree() ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);

    private static IEnumerable<(string Rel, string Code)> ProductionCode()
    {
        var root = Root();
        foreach (var top in new[] { "FDP", "Hrot", "Stride" })
        {
            var dir = Path.Combine(root, top);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (NotProduction.IsMatch(rel)) continue;
                var code = string.Join('\n', File.ReadAllLines(file).Where(l => { var t = l.TrimStart(); return !t.StartsWith("//") && !t.StartsWith("*"); }));
                yield return (rel, code);
            }
        }
    }

    private static string Source(string rel) => File.ReadAllText(Path.Combine(Root(), rel));

    /// <summary>
    /// The seed has ONE caller in production code - the host start - plus the named Stride host. Red proof: a <c>ContentSeed.SeedIntoNas(...)</c> added to <c>Program.cs</c> (or back into
    /// <c>EditorSubsystem.Initialize</c>) lists it here.
    /// </summary>
    [Fact]
    public void TheSeedHasOneCaller_TheOneHostStart()
    {
        var callers = ProductionCode()
            .Where(f => Regex.IsMatch(f.Code, @"\b(SeedIntoNas|SeedTreeFrom)\s*\("))
            .Select(f => f.Rel).Where(r => r != TheSeed).OrderBy(r => r).ToArray();

        Assert.Equal(new[] { TheHostStart, TheStrideHostStart }.OrderBy(r => r), callers);
        Assert.Matches(@"SeedIntoNas\(", Source(TheHostStart));
    }

    /// <summary>
    /// No production host starts a subsystem around the orchestrator (<c>subsystem.Initialize(config)</c>) unless it seeds itself: the matches are the orchestrator,
    /// <c>CiSubsystem</c> (a decorator the orchestrator itself initialises) and the Stride host, which is also a seeding caller above.
    /// </summary>
    [Fact]
    public void NoProductionHostInitializesASubsystemAroundTheOrchestrator()
    {
        var hits = ProductionCode()
            .Where(f => Regex.IsMatch(f.Code, @"\.Initialize\(\s*(new SubsystemConfig|cfg|config)\b"))
            .Select(f => f.Rel).OrderBy(r => r).ToArray();

        Assert.Equal(new[] { TheHostStart, "Hrot/Runner/Hrot.ClusterRunner/Scenarios/CiSubsystem.cs", TheStrideHostStart }.OrderBy(r => r), hits);
    }

    /// <summary>
    /// Each host family reaches that start: the cluster runner's composition root, and both in-process test harnesses (Brain/Muscle <c>HrotRunnerHarness</c>, CGF
    /// <c>CgfHarness</c>) construct a <c>SubsystemOrchestrator</c> and neither seeds or initialises a subsystem itself.
    /// </summary>
    [Theory]
    [InlineData("Hrot/Runner/Hrot.ClusterRunner/Program.cs")]
    [InlineData("Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs")]
    [InlineData("Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs")]
    public void EveryHostFamily_StartsThroughTheOrchestrator_AndSeedsNothingItself(string compositionRoot)
    {
        var code = string.Join('\n', Source(compositionRoot).Split('\n').Where(l => { var t = l.TrimStart(); return !t.StartsWith("//") && !t.StartsWith("*"); }));

        Assert.Matches(@"new SubsystemOrchestrator\(", code);
        Assert.DoesNotMatch(@"\b(SeedIntoNas|SeedTreeFrom|SeedFrom)\s*\(", code);
        Assert.DoesNotMatch(@"\.Initialize\(\s*new SubsystemConfig", code);
    }
}
