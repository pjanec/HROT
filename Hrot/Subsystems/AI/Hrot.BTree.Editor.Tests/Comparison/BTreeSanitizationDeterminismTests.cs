using System;
using System.IO;
using System.Linq;
using Hrot.BTree.Editor.Comparison;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Comparison;

/// <summary>
/// Verifies that BTreeComparisonSanitizer produces byte-identical output on repeated invocations
/// and when the layout .Node() order is permuted (design §3.3: determinism requirement).
/// </summary>
public sealed class BTreeSanitizationDeterminismTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Comparison", "Fixtures", fileName);

    private static BTreeComparisonSanitizer MakeSanitizer() =>
        new BTreeComparisonSanitizer(new FakeCatalog(
            new FakeAsset
            {
                AssetId = new Guid("eeeeeeee-0000-0002-0000-000000000001"),
                Name    = "Retreat_BT",
                Kind    = AssetKind.BTree,
            }));

    private static SanitizationResult Sanitize(BTreeComparisonSanitizer sanitizer, string path) =>
        sanitizer.Sanitize(new AssetExportRequest(path, null, AssetKind.BTree));

    // ---- Tests ----

    [Theory]
    [InlineData("simple_guard.cs")]
    [InlineData("complex_combat.cs")]
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
    public void Sanitize_LayoutNodeOrderPermuted_ProducesSameOutput()
    {
        // The sanitizer indexes comments by GUID, so reordering layout .Node() entries
        // must not change the sanitized output.
        string original = File.ReadAllText(FixturePath("simple_guard.cs"));

        // Build a reordered variant: swap the first two .Node() entries in the layout.
        string reordered = ReorderLayoutNodes(original);

        string tempPath = Path.Combine(Path.GetTempPath(), $"reorder_test_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempPath, reordered);
        try
        {
            var sanitizer = MakeSanitizer();

            string expected = Sanitize(sanitizer, FixturePath("simple_guard.cs")).SanitizedText;
            string actual   = Sanitize(sanitizer, tempPath).SanitizedText;

            Assert.Equal(expected, actual);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // Swap the first two .Node(...) blocks (each block ends at the line whose trimmed form
    // starts with ')' at balanced depth, i.e., the closing paren of the call).
    private static string ReorderLayoutNodes(string fileContent)
    {
        string normalized = fileContent.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n').ToList();

        // Locate the layout attribute line.
        int layoutIdx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("[BTreeLayout(", StringComparison.Ordinal))
            {
                layoutIdx = i;
                break;
            }
        }
        if (layoutIdx < 0) return fileContent;

        // Collect [start, end] inclusive ranges of the first two .Node(...) calls.
        var nodeRanges = new List<(int Start, int End)>();
        int j = layoutIdx + 1;
        while (j < lines.Count && nodeRanges.Count < 2)
        {
            string trimmed = lines[j].TrimStart();
            if (trimmed.StartsWith(".Node(", StringComparison.Ordinal))
            {
                int start = j;
                int depth = 0;
                while (j < lines.Count)
                {
                    foreach (char c in lines[j]) { if (c == '(') depth++; else if (c == ')') depth--; }
                    if (depth <= 0) break;
                    j++;
                }
                nodeRanges.Add((start, j));
            }
            j++;
        }

        if (nodeRanges.Count < 2) return fileContent;

        // Swap the two node blocks in-place by extracting them and reinserting in reverse order.
        var (s1, e1) = nodeRanges[0];
        var (s2, e2) = nodeRanges[1];

        var block1 = lines.GetRange(s1, e1 - s1 + 1);
        var block2 = lines.GetRange(s2, e2 - s2 + 1);

        // Replace second block with first block's content.
        lines.RemoveRange(s2, e2 - s2 + 1);
        lines.InsertRange(s2, block1);

        // Replace first block with second block's content.
        lines.RemoveRange(s1, e1 - s1 + 1);
        lines.InsertRange(s1, block2);

        return string.Join("\n", lines);
    }
}
