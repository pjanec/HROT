using System.Text;
using System.Text.RegularExpressions;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Emit;
using Xunit.Abstractions;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// CF-1: Ground-truth diagnostic — produces an authoritative report on node-identity
/// mismatches between authored node IDs, DebugMap entries, and emitted DebugProbe.NodeEnter
/// calls. This is a reporting test; it asserts nothing about correctness and always passes.
/// </summary>
public sealed class CF1_NodeIdentityDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    // Known authored node IDs from Count4.bp.json with corresponding JSON discriminator kinds.
    private static readonly IReadOnlyList<(Guid Id, string Kind)> KnownAuthoredNodes = new[]
    {
        (Guid.Parse("20000006-0000-0000-0000-000000000001"), "EventEntry"),
        (Guid.Parse("20000006-0000-0000-0000-000000000002"), "SetVariable"),
        (Guid.Parse("20000006-0000-0000-0000-000000000003"), "FunctionCall"),
        (Guid.Parse("20000006-0000-0000-0000-000000000004"), "GetVariable"),
        (Guid.Parse("da9a9c0b-25f8-4a81-9a52-75c715456f18"), "Sequence"),
        (Guid.Parse("0b561966-b00b-4c84-a1a0-87042220ba9f"), "Delay"),
        (Guid.Parse("7b6da53f-4e11-4bc9-9d0c-bad0e22c7f5c"), "Return"),
    };

    // Node kinds known to lose their authored ID during lowering (from ground truth).
    private static readonly HashSet<string> KnownLostNodeKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sequence", "Delay"
    };

    // Ground-truth synthesized IDs from bp-diag.log (2026-06-08).
    private static readonly IReadOnlyDictionary<Guid, string> GroundTruthSynthesizedIds = new Dictionary<Guid, string>
    {
        { Guid.Parse("da9a9c0b-25f8-4a81-9a52-75c715456f18"), "0ec3b253-3c5a-1024-..." }, // Sequence
        { Guid.Parse("0b561966-b00b-4c84-a1a0-87042220ba9f"), "976ef338-34f2-1469-973f-ee53538aab17" }, // Delay
    };

    public CF1_NodeIdentityDiagnosticsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Loads Count4.bp.json, compiles it in Debug mode, and produces a Markdown report
    /// detailing the node-identity mapping from authored IDs → DebugMap entries → emitted
    /// DebugProbe.NodeEnter probes. This test always passes; its output is the report file.
    /// </summary>
    [Fact]
    public void CF1_Diagnostic_ProducesNodeIdentityReport()
    {
        // ── 1. Load the asset ─────────────────────────────────────────────
        var repoRoot = ResolveRepoRoot();
        var assetPath = Path.Combine(repoRoot,
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Blueprints", "Count4.bp.json");
        var json = File.ReadAllText(assetPath);
        var asset = BlueprintJsonServices.Deserialize(json)
                    ?? throw new InvalidOperationException(
                        $"BlueprintJsonServices.Deserialize returned null for '{assetPath}'");

        _output.WriteLine($"[CF1] Loaded asset: {asset.Name} (AssetId: {asset.AssetId:D})");

        // ── 2. Compile in Debug mode ──────────────────────────────────────
        var options = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var compiler = new BlueprintCompiler();
        var result   = compiler.Compile(asset, options);

        _output.WriteLine($"[CF1] Compilation {(result.Succeeded ? "succeeded" : "FAILED")}");

        var debugMap = result.DebugMap;
        var source   = result.GeneratedSource ?? string.Empty;

        // ── 3. Gather data ────────────────────────────────────────────────
        var graph = asset.Graphs[0];
        var authoredNodes = graph.Nodes;

        // Authored node → concrete C# type name
        string NodeKindOf(Node node) => node.GetType().Name;

        // DebugMap lookups
        var debugMapEntries = debugMap?.Entries ?? Array.Empty<DebugMapEntry>();
        var debugMapNodeIds = new HashSet<Guid>(debugMapEntries.Select(e => e.NodeId));
        var debugMapByNodeId = debugMapEntries
            .GroupBy(e => e.NodeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Regex: find every DebugProbe.NodeEnter(self, "<id>") in generated source
        var probeRegex = new Regex(
            @"DebugProbe\.NodeEnter\s*\(\s*self\s*,\s*""([^""]+)""\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var probeIds = probeRegex.Matches(source)
            .Select(m => m.Groups[1].Value)
            .ToList();

        var probeIdSet = new HashSet<string>(probeIds, StringComparer.OrdinalIgnoreCase);

        // ── 4. Build Markdown report ──────────────────────────────────────
        var mb = new MarkdownBuilder();

        mb.H1("CF1 Node Identity Diagnostic Report");
        mb.Line();
        mb.Bullet($"**Asset**: {asset.Name}");
        mb.Bullet($"**AssetId**: `{asset.AssetId:D}`");
        mb.Bullet($"**GraphId**: `{graph.Id:D}`");
        mb.Bullet($"**Compile succeeded**: {result.Succeeded}");
        mb.Bullet($"**Generated**: {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        mb.Bullet($"**Source file**: `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Count4.bp.json`");
        mb.Line();

        // ── Table A: DebugMap entries ─────────────────────────────────────
        mb.H2("Table A — DebugMap entries");
        mb.Line();
        mb.Table(
            "NodeId", "NodeKind", "DisplayName", "StartLine",
            debugMapEntries.Select(e => new[]
            {
                $"`{e.NodeId:D}`",
                Escape(e.NodeKind),
                Escape(e.DisplayName),
                e.StartLine.ToString()
            }));

        if (debugMapEntries.Count == 0)
        {
            mb.Bullet("*No DebugMap entries found.*");
        }
        mb.Line();

        // ── Table B: Authored nodes vs DebugMap ──────────────────────────
        mb.H2("Table B — Authored nodes vs DebugMap");
        mb.Line();
        mb.Table(
            "Id", "Kind", "DebugMap entry keyed by this exact authored Id?",
            authoredNodes.Select(n => new[]
            {
                $"`{n.Id:D}`",
                Escape(NodeKindOf(n)),
                debugMapNodeIds.Contains(n.Id) ? "YES" : "NO"
            }));
        mb.Line();

        // ── Table C: Emitted probes ───────────────────────────────────────
        mb.H2("Table C — Emitted DebugProbe.NodeEnter calls");
        mb.Line();
        if (probeIds.Count > 0)
        {
            mb.Table(
                "#", "Probe Id", "Matches authored node?",
                probeIds.Select((id, i) =>
                {
                    bool matches = IsAuthoredId(id, authoredNodes);
                    return new[] { (i + 1).ToString(), $"`{id}`", matches ? "YES" : "NO" };
                }));
        }
        else
        {
            mb.Bullet("*No DebugProbe.NodeEnter calls found in generated source.*");
        }
        mb.Line();

        // ── Section D: Losses ─────────────────────────────────────────────
        mb.H2("Section D — Losses: authored exec nodes with no DebugMap entry and no matching probe");
        mb.Line();

        foreach (var node in authoredNodes)
        {
            var kind = NodeKindOf(node);
            bool inDebugMap = debugMapNodeIds.Contains(node.Id);
            string nodeIdStr = node.Id.ToString("D");
            bool hasProbe = probeIdSet.Contains(nodeIdStr);

            if (!inDebugMap && !hasProbe)
            {
                mb.H3($"{kind} (`{nodeIdStr}`)");
                mb.Line();
                mb.Bullet("**In DebugMap by exact authored id?** NO");
                mb.Bullet("**Has matching NodeEnter probe by exact authored id?** NO");
                mb.Line();

                if (GroundTruthSynthesizedIds.TryGetValue(node.Id, out var synthId))
                {
                    mb.Bullet($"**Ground-truth synthesized replacement id**: `{synthId}`");
                }

                // Check if a synthesized ID appears in the DebugMap entries
                var synthEntries = debugMapEntries
                    .Where(e => !debugMapNodeIds.Contains(e.NodeId) || !IsAuthoredId(e.NodeId.ToString("D"), authoredNodes))
                    .ToList();

                // Check if a synthesized ID has a probe
                var orphanProbes = probeIds
                    .Where(id => !IsAuthoredId(id, authoredNodes))
                    .ToList();

                mb.Bullet($"**Orphan DebugMap entries** (NodeId not matching any authored node): {synthEntries.Count}");
                foreach (var se in synthEntries)
                {
                    mb.Bullet($"  - `{se.NodeId:D}` (Kind: {Escape(se.NodeKind)}, DisplayName: {Escape(se.DisplayName)}, StartLine: {se.StartLine})");
                }

                mb.Bullet($"**Orphan NodeEnter probes** (id not matching any authored node): {orphanProbes.Count}");
                foreach (var op in orphanProbes)
                {
                    mb.Bullet($"  - `{op}`");
                }

                mb.Line();

                // Note about IR access
                mb.Bullet("**IR/Synthesized tag analysis** — NOT AVAILABLE from `CompileResult` alone.");
                mb.Bullet("  The lowered IR (`IrBlock` statements with `IrDebugAnnotation.Synthesized`) is internal");
                mb.Bullet("  to the compiler pipeline and not exposed on `CompileResult`. To retrieve it, one would need");
                mb.Bullet("  to run the pipeline stages separately (as `BPF015_DebugProbeEmitTests` does) and inspect");
                mb.Bullet("  the IR directly. The `Synthesized` field on `IrDebugAnnotation` records the lowering tag");
                mb.Bullet("  (e.g. `\"stage6-wait-lower-inst\"`) responsible for the identity replacement.");
                mb.Line();
                mb.Bullet("**Known from ground truth (bp-diag.log + prior analysis):**");
                mb.Bullet($"  - Sequence `da9a9c0b` → `{GroundTruthSynthesizedIds.GetValueOrDefault(node.Id, "?")}` (Stage3_Normalize.SynthesizedGuid or Stage6 lowering)");
                mb.Bullet($"  - Delay `0b561966` → `{GroundTruthSynthesizedIds.GetValueOrDefault(node.Id, "?")}` (Stage6 WaitLowering_Instance.Synth)");
                mb.Line();
            }
        }

        // ── Summary ───────────────────────────────────────────────────────
        mb.H2("Summary");
        mb.Line();
        var authoredIdSet = new HashSet<string>(
            authoredNodes.Select(n => n.Id.ToString("D")),
            StringComparer.OrdinalIgnoreCase);

        int authoredInDebugMap = authoredNodes.Count(n => debugMapNodeIds.Contains(n.Id));
        int authoredWithProbe = authoredNodes.Count(n =>
            probeIdSet.Contains(n.Id.ToString("D")));
        int authoredMissingFromDebugMap = authoredNodes.Count(n => !debugMapNodeIds.Contains(n.Id));
        int authoredMissingProbe = authoredNodes.Count(n =>
            !probeIdSet.Contains(n.Id.ToString("D")));

        mb.Bullet($"**Authored nodes**: {authoredNodes.Count}");
        mb.Bullet($"**Authored nodes with DebugMap entry (exact Id match)**: {authoredInDebugMap}/{authoredNodes.Count}");
        mb.Bullet($"**Authored nodes with NodeEnter probe (exact Id match)**: {authoredWithProbe}/{authoredNodes.Count}");
        mb.Bullet($"**Authored nodes MISSING from DebugMap**: {authoredMissingFromDebugMap}/{authoredNodes.Count}");
        mb.Bullet($"**Authored nodes MISSING probe**: {authoredMissingProbe}/{authoredNodes.Count}");
        mb.Line();
        mb.Bullet($"**Total DebugMap entries**: {debugMapEntries.Count}");
        mb.Bullet($"**Total emitted NodeEnter probes**: {probeIds.Count}");
        mb.Bullet($"**Orphan probe IDs** (not matching any authored node): {probeIds.Count(id => !IsAuthoredId(id, authoredNodes))}");
        mb.Line();
        mb.Bullet("**Key finding:** The Sequence and Delay nodes lose their authored IDs during lowering,");
        mb.Bullet("causing their DebugMap entries and NodeEnter probes to be keyed to synthesized IDs");
        mb.Bullet("instead. Additionally, probe mis-attribution (DebugProbeInsertion using block.Statements[0])");
        mb.Bullet("means the probe for an exec node's block may be keyed to a data-input node's ID instead.");
        mb.Line();

        // ── 5. Write report to disk ───────────────────────────────────────
        var reportDir = Path.Combine(repoRoot, ".dev", "blueprint-dbg-1", "reports");
        Directory.CreateDirectory(reportDir);
        var reportPath = Path.Combine(reportDir, "CF1-NODE-IDENTITY-REPORT.md");
        var reportContent = mb.ToString();
        File.WriteAllText(reportPath, reportContent);

        _output.WriteLine(reportContent);
        _output.WriteLine(string.Empty);
        _output.WriteLine($"Report written to: {reportPath}");

        // Always pass — this is a reporting test.
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking for the solution file.
    /// </summary>
    private static string ResolveRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "IOS-IG-SimHost.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "Could not find repo root (looked for IOS-IG-SimHost.sln upward from " +
            AppContext.BaseDirectory + ")");
    }

    /// <summary>Checks whether a probe ID string matches any authored node's ID.</summary>
    private static bool IsAuthoredId(string probeId, IReadOnlyList<Node> authoredNodes)
    {
        return authoredNodes.Any(n =>
            string.Equals(n.Id.ToString("D"), probeId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Escapes pipe characters for Markdown table cells.</summary>
    private static string Escape(string s) => s.Replace("|", "\\|");
}

/// <summary>
/// Minimal Markdown builder used by CF1 to produce the report without external dependencies.
/// </summary>
internal sealed class MarkdownBuilder
{
    private readonly StringBuilder _sb = new();

    public void H1(string text) { _sb.AppendLine($"# {text}"); _sb.AppendLine(); }
    public void H2(string text) { _sb.AppendLine($"## {text}"); _sb.AppendLine(); }
    public void H3(string text) { _sb.AppendLine($"### {text}"); _sb.AppendLine(); }
    public void Bullet(string text) { _sb.AppendLine($"- {text}"); }
    public void Line() { _sb.AppendLine(); }

    public void Table(string col1, string col2, string col3, string col4, IEnumerable<string[]> rows)
    {
        _sb.AppendLine($"| {col1} | {col2} | {col3} | {col4} |");
        _sb.AppendLine($"|---|---|---|---|");
        foreach (var row in rows)
        {
            _sb.AppendLine($"| {row[0]} | {row[1]} | {row[2]} | {row[3]} |");
        }
    }

    public void Table(string col1, string col2, string col3, IEnumerable<string[]> rows)
    {
        _sb.AppendLine($"| {col1} | {col2} | {col3} |");
        _sb.AppendLine($"|---|---|---|");
        foreach (var row in rows)
        {
            _sb.AppendLine($"| {row[0]} | {row[1]} | {row[2]} |");
        }
    }

    public override string ToString() => _sb.ToString();
}
