using System.IO;
using System.Linq;
using Fdp.TestShared;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>R-3039</c> S4 — every field a system, module or network translator carries between frames is classified in
/// <c>docs/DESIGN_System_State_Inventory.md</c>: K (not state) · A (derived, rebuilt from the world) · B (protocol, re-derived at a restore) ·
/// C (simulation memory — must move into a component) · X (state in an engine outside the ECS). A checkpoint, a recording and the dry run copy
/// the world and nothing else, so an unclassified field is a restore bug nobody has looked at. The C and X rows are the S1 / S2 work list:
/// ⛔ a NEW one fails here — write the state into a component instead. ⚠ The Stride assemblies are not in this binary's folder and are
/// not walked yet (CE-3307).
/// </summary>
public sealed class EverySystemStateFieldIsClassifiedTests
{
    private readonly ITestOutputHelper _out;
    public EverySystemStateFieldIsClassifiedTests(ITestOutputHelper output) => _out = output;

    /// <summary>The C / X rows the inventory holds today — the moves still to do (S1, S2). It may only shrink.</summary>
    public const int PendingMoves = 23;

    [Fact]
    public void EveryFieldThatCanCarryStateBetweenFrames_IsClassified()
    {
        var doc = Path.Combine(SystemStateProbe.RepoRoot(), "docs", "DESIGN_System_State_Inventory.md");
        var r = SystemStateProbe.Check(SystemStateProbe.ReadInventory(doc));
        Assert.True(r.Owners > 300, $"only {r.Owners} systems, modules and translators walked — the probe is not finding the tree.");

        _out.WriteLine($"{r.Owners} owners, {r.Hits.Count} fields, {r.Pending.Count} pending moves (C / X)");
        Assert.True(r.Unclassified.Count == 0,
            $"{r.Unclassified.Count} fields can carry state between frames and are not in {doc}. Classify each (K / A / B), or keep the state "
            + "in a component instead (R-3039):\n  " + string.Join("\n  ", r.Unclassified.Select(h => $"{h.Key}  [{h.Kind}{(h.TypeKey.Length > 0 ? " " + h.TypeKey : "")}]")));
        Assert.True(r.UnusedRows.Count == 0, "inventory rows naming no field or type any more — delete them: " + string.Join(", ", r.UnusedRows));
        if (PendingMoves >= 0)
            Assert.True(r.Pending.Count <= PendingMoves,
                $"{r.Pending.Count} C / X fields, more than the {PendingMoves} already listed — new simulation memory belongs in a component:\n  "
                + string.Join("\n  ", r.Pending.Select(p => $"{p.Hit.Key} — {p.Row.Reason}")));
    }
}
