using System.Text.RegularExpressions;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐ <c>CE-3354</c> ③ (the silent-default rule) — <b>a host that HAS a 3-D map passes it to its load chain</b>, so the master's map-in-load setting can make that node's load wait for it. Every host that attaches map layers
/// (<c>MapInteractionPack.AttachMapLayers</c>) keeps the <c>Map3D</c> build it gets back and hands a lazy <c>MapBuildStatus</c> to its node bootstrapper, whose <c>BuildOrchestration</c> passes it on as <c>mapBuildStatus:</c>.
/// A source scan of the composition roots (pattern: <c>EveryHostSeedsThroughTheOneHostStartRails</c>) — a headless construction of each real host is not cheap; the chain's own behaviour is <c>LoadPhaseChainTests.CE3354_*</c>.
/// 🔴 Red-proof: the <c>MapBuildStatus =</c> line removed from a host (or its bootstrapper's <c>mapBuildStatus:</c> argument) ⇒ that host is listed.
/// </summary>
public sealed class EveryHostWithAMapPassesItToItsLoadChainRails
{
    private static string Source(string rel)
        => File.ReadAllText(Path.Combine(ContentLayout.TryFindSourceTree() ?? throw new DirectoryNotFoundException(AppContext.BaseDirectory), rel));

    /// <summary>host file that wires the lazy status · the bootstrapper file whose BuildOrchestration passes it on</summary>
    public static IEnumerable<object[]> Hosts() => new[]
    {
        new object[] { "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",       @"MapBuildStatus\s*=\s*\(\)\s*=>\s*_cgfMap3D", "Hrot/Subsystems/Hrot.CGF/CgfNodeBootstrapper.cs" },
        new object[] { "Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs",     @"MapBuildStatus\s*=\s*\(\)\s*=>\s*_vis\?\.Map3D", "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs" },
        new object[] { "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs", @"MapBuildStatus\s*=\s*\(\)\s*=>\s*_map3DBuild", "Hrot/Subsystems/Hrot.Editor/EditorNodeBootstrapper.cs" },
        new object[] { "Hrot/Subsystems/Hrot.IG/IgApplication.cs",       @"MapBuildStatus\s*=\s*\(\)\s*=>\s*MapBuildStatus", "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs" },
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public void AHostThatAttachesAMap_WiresItsStatus_AndItsBootstrapperPassesItOn(string hostFile, string wiring, string bootstrapperFile)
    {
        Assert.True(Regex.IsMatch(Source(hostFile), wiring), $"{hostFile} does not hand its 3-D map build to its node bootstrapper (expected /{wiring}/)");
        Assert.True(Regex.IsMatch(Source(bootstrapperFile), @"mapBuildStatus:\s*MapBuildStatus"), $"{bootstrapperFile}'s BuildOrchestration does not pass mapBuildStatus: MapBuildStatus");
    }
}
