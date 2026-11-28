using System.IO;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐ CE-11037 (docs/DESIGN_Terrain_Import_UTM.md §13 F-1) — every node process starts in <c>Hrot.ClusterRunner/Program.Main</c> (the editor, player, orchestrator
/// and replay browser are subsystems of it; the one other executable that resolves terrains by name does not exist), so the import-on-first-use hook is
/// registered THERE, once. Main cannot be called from a test, so the rail reads its source: the registration is outside every early return and precedes any host.
/// </summary>
public sealed class TerrainImportIsRegisteredAtStartupTests
{
    [Fact]
    public void ProgramMain_RegistersTheSourceImport_BeforeAnyHostStarts()
    {
        var tree = ContentLayout.TryFindSourceTree();
        Assert.NotNull(tree);
        string program = File.ReadAllText(Path.Combine(tree!, "Hrot", "Runner", "Hrot.ClusterRunner", "Program.cs"));
        int registration = program.IndexOf("Fdp.Toolkit.Terrain.TerrainSources.Import = new Hrot.Terrain.Import.SourceImport();", System.StringComparison.Ordinal);
        Assert.True(registration > 0, "Program.Main must register Hrot.Terrain.Import.SourceImport as TerrainSources.Import");
        Assert.True(registration < program.IndexOf("Parser.Default.ParseArguments", System.StringComparison.Ordinal), "the registration must come before the host is configured");
        Assert.Equal(registration, program.LastIndexOf("TerrainSources.Import =", System.StringComparison.Ordinal) - "Fdp.Toolkit.Terrain.".Length);
    }
}
