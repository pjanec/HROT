using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3293</c> S3 (R-3036 rule 4) — no production code reads a world's events off its bus: a system, and anything it calls,
/// reads them through the repository (<c>ReadEvents</c> / <c>ReadManagedEvents</c> / <c>HasManagedEvents</c> /
/// <c>ReadRawEventsByTypeId</c> / <c>HasEventsByTypeId</c> / <c>HasEvents</c>), which from S4 answers the calling system's window —
/// every event since it last ran. A read that goes around them sees only the current frame and silently loses what was published
/// while time stood still. 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c> §1 rule 4, §10 F4 (96 files, Roslyn).
///
/// <para>Asserted over SOURCE: the read is a member access on a world's <c>Bus</c> property, and the blueprint emitter writes the
/// same text into generated code, so one scan covers both. The bus's owner (<c>Fdp.Core</c>) and the framework that feeds the
/// window (<c>Fdp.ModuleHost</c>) are the only places allowed to read it. Reads of the orchestration, interaction and replay
/// buses are separate bus instances held in fields — they are not windowed and are not matched here.</para>
/// </summary>
public sealed class NoWorldBusReadOutsideTheFrameworkTests
{
    private static readonly Regex WorldBusRead = new(
        @"\bBus\s*\.\s*(Read|ReadManaged|HasEvent|HasManagedEvent|ReadRawByTypeId)\s*[<(]", RegexOptions.Compiled);

    private static readonly Regex NotProduction = new(
        @"[\\/](obj|bin|ExtDeps|TestShared|Examples)[\\/]|Tests?[\\/]|Tests?\.cs$|Benchmark", RegexOptions.Compiled);

    private static readonly string[] Framework =
    {
        "FDP/Engine/Fdp.Core/",          // the bus and the repository's reads over it
        "FDP/Engine/Fdp.ModuleHost/",    // the kernel that captures every frame's events into the window
    };

    [Fact]
    public void NoProductionCode_ReadsAWorldsEventsOffItsBus()
    {
        var root = RepoRoot();
        var hits = new List<string>();
        int scanned = 0;
        foreach (var top in new[] { "FDP", "Hrot", "Stride" })
        {
            var dir = Path.Combine(root.FullName, top);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root.FullName, file).Replace('\\', '/');
                if (NotProduction.IsMatch(rel) || Framework.Any(rel.StartsWith)) continue;
                scanned++;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimStart();
                    if (line.StartsWith("//") || line.StartsWith("*")) continue;
                    if (WorldBusRead.IsMatch(lines[i])) hits.Add($"{rel}:{i + 1}: {line}");
                }
            }
        }

        Assert.True(scanned > 1000, $"only {scanned} production files scanned — the rail is not finding the tree.");
        Assert.True(hits.Count == 0,
            $"{hits.Count} world-bus read(s) outside the framework — read through the repository instead (R-3036 rule 4):\n"
            + string.Join("\n", hits));
    }

    // Walks up from the test binary directory until HROT.sln is found — the idiom TheMapIsNotSelectionGatedRails uses.
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HROT.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate workspace root (HROT.sln not found).");
        return dir!;
    }
}
