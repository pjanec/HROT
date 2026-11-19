using Fdp.Testing;
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
    public const int PendingMoves = 7;

    [Fact]
    public void EveryFieldThatCanCarryStateBetweenFrames_IsClassified()
    {
        var doc = Path.Combine(TestRepo.Root, "docs", "DESIGN_System_State_Inventory.md");
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
    /// ⭐ R-4297 — a TOOL, not a gate: with <c>HROT_STATE_DUMP=&lt;file&gt;</c> set, writes every field the probe walks as a tab-separated line — key, kind, held type,
    /// the declaring type's full name, the field's name, and the class / source / reason that classifies it (attribute, ledger row, or none). What a lane runs to see
    /// which of its fields still need an attribute. A no-op otherwise.
    /// </summary>
    [Fact]
    public void Dump()
    {
        var path = System.Environment.GetEnvironmentVariable("HROT_STATE_DUMP");
        if (string.IsNullOrEmpty(path)) return;   // a tool: nothing to do without the variable
        var r = SystemStateProbe.Check(SystemStateProbe.ReadInventory(Path.Combine(TestRepo.Root, "docs", "DESIGN_System_State_Inventory.md")));
        var rows = r.Classified.ToDictionary(p => p.Hit.Key, p => p.Row);
        using var w = new StreamWriter(path!);
        foreach (var h in r.Hits)
        {
            rows.TryGetValue(h.Key, out var row);
            string field = h.Key[(h.Key.LastIndexOf('.') + 1)..];
            w.WriteLine(string.Join('\t', h.Key, h.Kind, h.TypeKey, h.Declaring?.FullName ?? "", field, row?.Class ?? "", row == null ? "none" : row.Attribute ?? "row:" + row.Key,
                                    (row?.Reason ?? "").Replace('\t', ' ')));
        }
        _out.WriteLine($"{r.Hits.Count} fields dumped to {path}");
    }

    /// <summary>
    /// ⭐⭐ <c>R-3039</c> S3 / ⭐ <c>R-4297</c> ④ (docs/DESIGN_System_State_Inventory.md §8, §9) — a class-B field is protocol RE-DERIVED at a world replacement, so its
    /// owner must react to one, in its COMPILED code: <c>WorldEpoch.Moved</c> / <c>Ended</c>, the egress shared shape <c>PublishedInstances&lt;T&gt;.OnFrame</c>
    /// (it forgets on <c>WorldEpoch.Moved</c>, CE-4007), or <c>OnWorldBoundary</c> / <c>ForgetLastWorld</c> / the ELM's <c>OnWorldReplaced</c>. Session state (S) needs
    /// no hook. ⛔ Was a TEXT search of each row's evidence file: 36 of its 45 reds were rows naming fields CE-4007 had already moved into the shared shape.
    /// 🔴 Red-proof: an ingress translator's <c>WorldEpoch.Moved</c> clear of <c>_pending</c> removed ⇒ red.
    /// </summary>
    [Fact]
    public void EveryProtocolField_IsReDerivedAtAWorldReplacement()
    {
        var r = SystemStateProbe.Check(SystemStateProbe.ReadInventory(Path.Combine(TestRepo.Root, "docs", "DESIGN_System_State_Inventory.md")));
        var missing = new System.Collections.Generic.List<string>();
        int b = 0;
        foreach (var (hit, row) in r.Classified)
        {
            if (row.Class != "B" || hit.Declaring == null) continue;
            b++;
            if (!WorldBoundaryProbe.ReactsToWorldReplacement(hit.Declaring)) missing.Add(hit.Key);
        }
        _out.WriteLine($"{b} B fields checked");
        Assert.True(b > 50, $"only {b} B fields found — the probe is not finding the tree");
        Assert.True(missing.Count == 0,
            $"{missing.Count} class-B fields whose owner never reacts to a world replacement (WorldEpoch.Moved) — clear or re-derive them "
            + "there, or classify them S if a restore must leave them:\n  " + string.Join("\n  ", missing));
    }
}
