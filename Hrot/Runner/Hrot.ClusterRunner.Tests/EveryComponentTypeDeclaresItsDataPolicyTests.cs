using System.Linq;
using Fdp.TestShared;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3299</c> D7 (R-3046) — every type the ECS can register (every <c>[ComponentId]</c> type: components, singletons and the
/// service interfaces a singleton is keyed by) DECLARES its <c>[DataPolicy]</c>. ⛔ Without one a type is recorded into every keyframe,
/// saved into scenarios and snapshotted by default — right for world state, wrong for content and services (the TKB and the navmesh
/// provider were written into every keyframe until D7). The author decides; the rail makes them. ⚠ The Stride assemblies are not in this
/// binary's folder and are not walked (CE-3307). 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md D7.
/// </summary>
public sealed class EveryComponentTypeDeclaresItsDataPolicyTests
{
    private readonly ITestOutputHelper _out;
    public EveryComponentTypeDeclaresItsDataPolicyTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryRegistrableType_DeclaresItsDataPolicy()
    {
        var r = DataPolicyProbe.Walk();
        _out.WriteLine($"{r.Types} [ComponentId] types in {r.Assemblies.Count} assemblies");
        Assert.True(r.Types > 250, $"only {r.Types} [ComponentId] types walked — the probe is not finding the tree.");
        Assert.True(r.Undeclared.Count == 0,
            $"{r.Undeclared.Count} type(s) the ECS can register declare no [DataPolicy] — say whether each is world state (recorded: Default, "
            + "or NoPreview for a mutable class) or not (a service, a cache, UI: NoScenario | NoReplay, or Transient):\n  "
            + string.Join("\n  ", r.Undeclared));
    }
}
