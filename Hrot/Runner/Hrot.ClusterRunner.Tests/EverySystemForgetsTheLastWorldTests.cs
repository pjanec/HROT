using System.Collections.Generic;
using Fdp.TestShared;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3294</c> — a system that keeps entities in its own fields forgets them at the world boundary (<c>WorldEpoch.Moved</c>), or the
/// field is named here with why it needs nothing. 📐 Measured before: 41 systems held per-entity state and ONE reset — after an editor reload
/// (<c>SoftClear</c>: the next run's units under the SAME handles) an aircraft never ejected, a crowd destroyed an unrelated new entity, an
/// order with last run's ids was skipped, a passenger was never killed with his vehicle. 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c>
/// §7f. ⚠ The Stride assemblies are NOT walked: <c>HrotStrideApp.Game.Tests</c> links the probe and calls it nowhere (CE-3307).
/// </summary>
public sealed class EverySystemForgetsTheLastWorldTests
{
    /// <summary>
    /// ⛔ RETIRED `2026-11-19` (R-4297, CE-1980) — a field that needs no reset says so WHERE IT IS WRITTEN: <c>[Fdp.Core.FrameScratch("why")]</c> (rebuilt before use
    /// every run). The 44 entries this dictionary held are attributes on their fields now. Kept empty so a lane's emergency exemption still has a home — prefer the
    /// attribute.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>();

    [Fact]
    public void EverySystemHoldingEntities_ForgetsThemAtTheWorldBoundary()
    {
        var r = WorldBoundaryProbe.Walk(Exempt);
        Assert.True(r.Systems > 150, $"only {r.Systems} systems walked — the probe is not finding the tree.");
        Assert.True(r.Missing.Count == 0, $"{r.Missing.Count} of {r.Holding} systems holding entities neither call WorldEpoch.Moved nor exempt the field:\n  "
                                          + string.Join("\n  ", r.Missing));
        Assert.True(r.UnusedExemptions.Count == 0, "exemptions naming no field any more: " + string.Join(", ", r.UnusedExemptions));
    }
}
