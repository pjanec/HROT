using Fdp.TestShared;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3293</c> S1 (R-3036) — every production system and module beside the runner declares <c>[TickMode]</c>; there is no
/// default. 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c> §1 rule 1 and Appendix A/B (202 systems, 40 modules,
/// counted by Roslyn). The Stride assemblies are walked by the same probe in <c>HrotStrideApp.Game.Tests</c>.
/// </summary>
public sealed class EverySystemDeclaresItsTickModeTests
{
    [Fact]
    public void EveryProductionSystemAndModule_DeclaresItsTickMode()
    {
        var r = TickModeProbe.Walk();
        Assert.True(r.Systems > 150, $"only {r.Systems} systems walked in {string.Join(", ", r.Assemblies)} — the probe is not finding the tree.");
        Assert.True(r.Modules > 25, $"only {r.Modules} modules walked — the probe is not finding the tree.");
        Assert.True(r.Missing.Count == 0, $"{r.Missing.Count} declare no TickMode: " + string.Join(" | ", r.Missing));
    }
}
