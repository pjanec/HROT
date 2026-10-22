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
    public const int PendingMoves = 20;

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

    /// <summary>
    /// ⭐⭐ <c>R-3039</c> S3 (docs/DESIGN_System_State_Inventory.md §8) — a class-B field is protocol RE-DERIVED at a world replacement, so
    /// its owner must react to one: <c>WorldEpoch.Moved</c> (directly or through <c>OnWorldBoundary</c> / <c>ForgetLastWorld</c>) or the
    /// ELM's <c>OnWorldReplaced</c>. ⚠ <c>AfterRestore.Run</c> raises the signal once; nobody calls the owners. Session state that a restore
    /// leaves by design (the cluster, the transport) is class S and needs no hook. ⚠ A TEXT check on the row's evidence file: it proves the
    /// hook exists, not that it clears the right field (the ELM has a behavioural rail, <c>APreviewLeavesNoTraceTests.R3039_*</c>).
    /// 🔴 Red-proof: a B row on an owner with no hook ⇒ red (15 owners failed it before S3).
    /// </summary>
    [Fact]
    public void EveryProtocolField_IsReDerivedAtAWorldReplacement()
    {
        var root = SystemStateProbe.RepoRoot();
        var row = new System.Text.RegularExpressions.Regex(
            @"^\|\s*`(?<key>[^`]+)`\s*\|\s*\*\*B\*\*\s*\|[^|]*\|\s*(?<ev>[^|:\s]+)");
        var hook = new System.Text.RegularExpressions.Regex(@"WorldEpoch\.|OnWorldBoundary|ForgetLastWorld|OnWorldReplaced");
        var missing = new System.Collections.Generic.List<string>();
        int rows = 0;
        foreach (var line in File.ReadLines(Path.Combine(root, "docs", "DESIGN_System_State_Inventory.md")))
        {
            var m = row.Match(line);
            if (!m.Success || m.Groups["key"].Value.Contains('*')) continue;
            rows++;
            var file = Path.Combine(root, m.Groups["ev"].Value);
            if (!File.Exists(file)) { missing.Add($"{m.Groups["key"].Value} — evidence file {m.Groups["ev"].Value} not found"); continue; }
            if (!hook.IsMatch(File.ReadAllText(file))) missing.Add($"{m.Groups["key"].Value} ({m.Groups["ev"].Value})");
        }
        _out.WriteLine($"{rows} B rows checked");
        Assert.True(rows > 50, $"only {rows} B rows read — the table moved");
        Assert.True(missing.Count == 0,
            $"{missing.Count} class-B fields whose owner never reacts to a world replacement (WorldEpoch.Moved) — clear or re-derive them "
            + "there, or classify them S if a restore must leave them:\n  " + string.Join("\n  ", missing));
    }
}
