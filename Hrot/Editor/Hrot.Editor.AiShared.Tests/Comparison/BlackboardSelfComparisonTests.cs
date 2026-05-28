using System;
using System.IO;
using Hrot.Editor.AiShared.Comparison;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Comparison;

/// <summary>
/// Verifies that sanitizing a Blackboard asset against itself produces byte-identical
/// output (design §10.3 self-comparison / no-edit round-trip property).
/// </summary>
public sealed class BlackboardSelfComparisonTests
{
    private static BlackboardComparisonSanitizer MakeSanitizer() =>
        new BlackboardComparisonSanitizer();

    private static string N(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    private const string InlineContent = """
        // OwningAssetId: b1000000-0000-0000-0000-000000000001
        // OwningAssetName: SelfTest_BT

        namespace Test;

        public partial struct SelfTest_BT_Blackboard
        {
            /// <summary>Ammo count field.</summary>
            public int AmmoCount;

            public bool IsReloading;
        }
        """;

    private const string HeavyContent = """
        // OwningAssetId: b1000000-0000-0000-0000-000000000001
        // OwningAssetName: SelfTest_BT

        namespace Test;

        public partial struct SelfTest_BT_Blackboard
        {
            /// <summary>Overflow field.</summary>
            public float BigBuffer;
        }
        """;

    private static (string inlinePath, string? heavyPath) WritePair(
        string inlineContent, string? heavyContent = null)
    {
        string dir      = Path.GetTempPath();
        string baseName = $"bb_self_{Guid.NewGuid():N}";
        string inlinePath = Path.Combine(dir, $"{baseName}.Blackboard.cs");
        File.WriteAllText(inlinePath, N(inlineContent));

        string? heavyPath = null;
        if (heavyContent != null)
        {
            heavyPath = Path.Combine(dir, $"{baseName}.HeavyBlackboard.cs");
            File.WriteAllText(heavyPath, N(heavyContent));
        }

        return (inlinePath, heavyPath);
    }

    private static void DeletePair(string inlinePath, string? heavyPath)
    {
        if (File.Exists(inlinePath)) File.Delete(inlinePath);
        if (heavyPath != null && File.Exists(heavyPath)) File.Delete(heavyPath);
    }

    // ---- Tests ----

    [Fact]
    public void Sanitize_InlineOnly_SameFileTwice_ByteIdentical()
    {
        var (inlinePath, _) = WritePair(InlineContent);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(inlinePath, null, AssetKind.Blackboard);

            string resultA = sanitizer.Sanitize(request).SanitizedText;
            string resultB = sanitizer.Sanitize(request).SanitizedText;

            Assert.Equal(resultA, resultB);
        }
        finally { DeletePair(inlinePath, null); }
    }

    [Fact]
    public void Sanitize_InlinePlusHeavy_SameFileTwice_ByteIdentical()
    {
        var (inlinePath, heavyPath) = WritePair(InlineContent, HeavyContent);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(inlinePath, null, AssetKind.Blackboard);

            string resultA = sanitizer.Sanitize(request).SanitizedText;
            string resultB = sanitizer.Sanitize(request).SanitizedText;

            Assert.Equal(resultA, resultB);
        }
        finally { DeletePair(inlinePath, heavyPath); }
    }

    [Fact]
    public void Sanitize_TwoIndependentSanitizerInstances_SameFiles_ByteIdentical()
    {
        var (inlinePath, heavyPath) = WritePair(InlineContent, HeavyContent);
        try
        {
            var sanitizerA = new BlackboardComparisonSanitizer();
            var sanitizerB = new BlackboardComparisonSanitizer();
            var request    = new AssetExportRequest(inlinePath, null, AssetKind.Blackboard);

            string resultA = sanitizerA.Sanitize(request).SanitizedText;
            string resultB = sanitizerB.Sanitize(request).SanitizedText;

            Assert.Equal(resultA, resultB);
        }
        finally { DeletePair(inlinePath, heavyPath); }
    }
}
