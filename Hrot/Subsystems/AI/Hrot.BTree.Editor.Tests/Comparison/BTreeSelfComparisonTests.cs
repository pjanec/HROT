using System;
using System.IO;
using Hrot.BTree.Editor.Comparison;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Comparison;

/// <summary>
/// Verifies that sanitizing the same BTree file twice (possibly with two independent catalog
/// instances) produces byte-identical output — a prerequisite for the comparison diffing
/// to produce an empty diff when comparing a file against itself (design §2.1).
/// </summary>
public sealed class BTreeSelfComparisonTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Comparison", "Fixtures", fileName);

    // Retreat subtree asset used by complex_combat.cs fixture.
    private static readonly FakeAsset RetreatAsset = new FakeAsset
    {
        AssetId = new Guid("eeeeeeee-0000-0002-0000-000000000001"),
        Name    = "Retreat_BT",
        Kind    = AssetKind.BTree,
    };

    private static BTreeComparisonSanitizer MakeSanitizer() =>
        new BTreeComparisonSanitizer(new FakeCatalog(RetreatAsset));

    private static SanitizationResult Sanitize(BTreeComparisonSanitizer s, string path) =>
        s.Sanitize(new AssetExportRequest(path, null, AssetKind.BTree));

    // ---- Tests ----

    [Theory]
    [InlineData("simple_guard.cs")]
    [InlineData("complex_combat.cs")]
    public void SanitizeFile_SameFileTwiceWithSameSanitizer_ProducesByteIdenticalOutput(
        string fixture)
    {
        string path    = FixturePath(fixture);
        var sanitizer  = MakeSanitizer();
        string runA    = Sanitize(sanitizer, path).SanitizedText;
        string runB    = Sanitize(sanitizer, path).SanitizedText;

        Assert.Equal(runA, runB);
    }

    [Theory]
    [InlineData("simple_guard.cs")]
    [InlineData("complex_combat.cs")]
    public void SanitizeFile_TwoIndependentCatalogInstances_ProducesByteIdenticalOutput(
        string fixture)
    {
        // Two sanitizer instances backed by separate (but equal-content) catalog objects.
        var sanitizerA = new BTreeComparisonSanitizer(new FakeCatalog(RetreatAsset));
        var sanitizerB = new BTreeComparisonSanitizer(new FakeCatalog(RetreatAsset));

        string path = FixturePath(fixture);
        string runA = Sanitize(sanitizerA, path).SanitizedText;
        string runB = Sanitize(sanitizerB, path).SanitizedText;

        Assert.Equal(runA, runB);
    }
}
