using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Hsm.Editor.Comparison;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Comparison;

/// <summary>
/// Verifies that HsmComparisonSanitizer produces byte-identical output on repeated
/// invocations and when the layout .State()/.Transition()/.Region() order is permuted
/// (design §10.3 determinism requirement).
/// </summary>
public sealed class HsmSanitizationDeterminismTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Comparison", "Fixtures", fileName);

    private static HsmComparisonSanitizer MakeSanitizer() =>
        new HsmComparisonSanitizer(new FakeCatalog());

    private static SanitizationResult Sanitize(HsmComparisonSanitizer s, string path) =>
        s.Sanitize(new AssetExportRequest(path, null, AssetKind.Hsm));

    // ---- Tests ----

    [Theory]
    [InlineData("simple_machine.cs")]
    [InlineData("parallel_machine.cs")]
    public void Sanitize_SameFixture_TenConsecutiveRuns_ProducesByteIdenticalOutput(string fixture)
    {
        string path      = FixturePath(fixture);
        var sanitizer    = MakeSanitizer();
        string reference = Sanitize(sanitizer, path).SanitizedText;

        for (int i = 0; i < 9; i++)
        {
            string run = Sanitize(sanitizer, path).SanitizedText;
            Assert.Equal(reference, run);
        }
    }

    [Fact]
    public void Sanitize_LayoutStateOrderPermuted_ProducesSameOutput()
    {
        // The sanitizer indexes comments by GUID, so reordering .State() entries in the
        // layout method must not change the sanitized output.
        string original  = File.ReadAllText(FixturePath("simple_machine.cs"));
        string reordered = SwapFirstTwoLayoutStates(original);

        string tempPath = Path.Combine(Path.GetTempPath(), $"hsm_reorder_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempPath, reordered);
        try
        {
            var sanitizer = MakeSanitizer();
            string outputOriginal  = Sanitize(sanitizer, FixturePath("simple_machine.cs")).SanitizedText;
            string outputReordered = Sanitize(sanitizer, tempPath).SanitizedText;
            Assert.Equal(outputOriginal, outputReordered);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Sanitize_MalformedFixture_NeverThrows_HasWarning()
    {
        string path   = FixturePath("malformed_no_layout.cs");
        var sanitizer = MakeSanitizer();
        var result    = Sanitize(sanitizer, path);

        // Must return a result (not throw).
        Assert.NotNull(result);
        // Must have at least one warning (no [HsmLayout] attribute).
        Assert.NotEmpty(result.Warnings);
    }

    // ---- Permutation helper ----

    /// <summary>
    /// Swaps the first two <c>.State("...", ...)</c> entries in the Layout method of
    /// the given HSM source text. The swap is purely textual within the layout body.
    /// </summary>
    private static string SwapFirstTwoLayoutStates(string source)
    {
        // Locate the [HsmLayout( line.
        string[] lines    = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int layoutLine    = Array.FindIndex(lines, l => l.TrimStart().StartsWith("[HsmLayout("));
        if (layoutLine < 0) return source;

        // Find the indices of the first two .State( calls in the layout body.
        var stateLines = new List<int>();
        for (int i = layoutLine + 1; i < lines.Length && stateLines.Count < 2; i++)
        {
            if (lines[i].TrimStart().StartsWith(".State("))
                stateLines.Add(i);
        }

        if (stateLines.Count < 2) return source;

        // Collect each .State( call (potentially multi-line) so we can swap the blocks.
        int end1 = CollectCallEnd(lines, stateLines[0]);
        int end2 = CollectCallEnd(lines, stateLines[1]);

        // Build the swapped source.
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < stateLines[0]; i++)
            result.Append(lines[i]).Append('\n');

        for (int i = stateLines[1]; i <= end2; i++)
            result.Append(lines[i]).Append('\n');
        for (int i = end1 + 1; i < stateLines[1]; i++)
            result.Append(lines[i]).Append('\n');
        for (int i = stateLines[0]; i <= end1; i++)
            result.Append(lines[i]).Append('\n');
        for (int i = end2 + 1; i < lines.Length; i++)
        {
            result.Append(lines[i]);
            if (i < lines.Length - 1) result.Append('\n');
        }

        return result.ToString();
    }

    private static int CollectCallEnd(string[] lines, int startLine)
    {
        int depth = 0;
        for (int i = startLine; i < lines.Length; i++)
        {
            foreach (char c in lines[i])
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
            }
            if (depth <= 0 && i >= startLine)
                return i;
        }
        return startLine;
    }
}
