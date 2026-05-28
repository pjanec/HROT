using System;
using System.IO;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Hsm.Editor.Comparison;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Comparison;

/// <summary>
/// Verifies that sanitizing an HSM asset against itself produces byte-identical output
/// (design §10.3 self-comparison / no-edit round-trip property).
/// </summary>
public sealed class HsmSelfComparisonTests
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
    public void Sanitize_SameFileTwice_ProducesByteIdenticalOutput(string fixture)
    {
        string path = FixturePath(fixture);
        var sanitizer = MakeSanitizer();

        string resultA = Sanitize(sanitizer, path).SanitizedText;
        string resultB = Sanitize(sanitizer, path).SanitizedText;

        Assert.Equal(resultA, resultB);
    }

    [Theory]
    [InlineData("simple_machine.cs")]
    [InlineData("parallel_machine.cs")]
    public void Sanitize_TwoIndependentCatalogInstances_ProducesByteIdenticalOutput(string fixture)
    {
        // Two independent catalog instances with identical content must produce
        // byte-identical output (catalog iteration order must not affect results).
        string path = FixturePath(fixture);

        var sanitizerA = new HsmComparisonSanitizer(new FakeCatalog());
        var sanitizerB = new HsmComparisonSanitizer(new FakeCatalog());

        string resultA = sanitizerA.Sanitize(new AssetExportRequest(path, null, AssetKind.Hsm)).SanitizedText;
        string resultB = sanitizerB.Sanitize(new AssetExportRequest(path, null, AssetKind.Hsm)).SanitizedText;

        Assert.Equal(resultA, resultB);
    }
}
