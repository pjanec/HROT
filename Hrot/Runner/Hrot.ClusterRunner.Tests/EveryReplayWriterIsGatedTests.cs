using Fdp.Testing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fdp.TestShared;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3329</c> P2 (docs/DESIGN_Replay_Time_Axis_And_Review.md §12l) — every system that writes world state stands down in a
/// replay by reading the world's <c>ReplayState</c>. The design's REPLAY-WRITERS table is the list; this rail fails when a listed file no
/// longer declares the system or no longer calls <c>ReplayState.IsActive</c>. ⚠ A TEXT check: it proves the gate is in the file, not that it
/// is the first statement of <c>Execute</c> (the behavioural rails — <c>LiveFromReplayTests</c>, <c>APreviewLeavesNoTraceTests</c>,
/// <c>EffectTests</c> — cover the mechanism). 🔴 Red-proof: remove one gate ⇒ red.
/// </summary>
public sealed class EveryReplayWriterIsGatedTests
{
    private readonly ITestOutputHelper _out;
    public EveryReplayWriterIsGatedTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryListedWriter_ReadsTheReplayFlag()
    {
        var root = TestRepo.Root;
        var doc = File.ReadAllText(Path.Combine(root, "docs", "DESIGN_Replay_Time_Axis_And_Review.md"));
        int start = doc.IndexOf("<!--REPLAY-WRITERS-->", System.StringComparison.Ordinal);
        int end = doc.IndexOf("<!--/REPLAY-WRITERS-->", System.StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the REPLAY-WRITERS table moved out of §12l");

        var row = new Regex(@"^\|\s*`(?<sys>[A-Za-z0-9_]+)`\s*\|\s*`(?<file>[^`]+)`\s*\|", RegexOptions.Multiline);
        var rows = row.Matches(doc.Substring(start, end - start)).Select(m => (Sys: m.Groups["sys"].Value, File: m.Groups["file"].Value)).ToList();
        _out.WriteLine($"{rows.Count} replay writers listed");
        Assert.True(rows.Count > 40, $"only {rows.Count} rows read — the table moved or lost its rows");

        var missing = rows.Where(r =>
        {
            var path = Path.Combine(root, r.File);
            if (!File.Exists(path)) return true;
            var text = File.ReadAllText(path);
            return !Regex.IsMatch(text, $@"class\s+{r.Sys}\b") || !Regex.IsMatch(text, @"ReplayState\.Is(Active|Restoring)\(");
        }).Select(r => $"{r.Sys} ({r.File})").ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} listed world writer(s) no longer read the replay flag (or moved) — gate them, or fix the row:\n  "
            + string.Join("\n  ", missing));
    }
}
