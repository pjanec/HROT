using Fdp.TestShared;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3293</c> S1 (R-3036) — every production system and module beside the Stride game declares <c>[TickMode]</c>; there is
/// no default. The rest of the tree is walked by the same probe in <c>Hrot.ClusterRunner.Tests</c>.
/// 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c> §1 rule 1.
/// </summary>
public sealed class EverySystemDeclaresItsTickModeTests
{
    [Fact]
    public void EveryProductionSystemAndModule_DeclaresItsTickMode()
    {
        var r = TickModeProbe.Walk();
        Assert.True(r.Systems > 50, $"only {r.Systems} systems walked in {string.Join(", ", r.Assemblies)} — the probe is not finding the tree.");
        Assert.True(r.Missing.Count == 0, $"{r.Missing.Count} declare no TickMode: " + string.Join(" | ", r.Missing));
    }
}
