using System;
using System.IO;
using Hrot.Editor.AiShared.Comparison;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Comparison;

/// <summary>
/// Verifies that BlackboardComparisonSanitizer produces byte-identical output on repeated
/// invocations across multiple fixture shapes (design §10.3 determinism requirement).
/// </summary>
public sealed class BlackboardSanitizationDeterminismTests
{
    private static BlackboardComparisonSanitizer MakeSanitizer() =>
        new BlackboardComparisonSanitizer();

    private static string N(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    // ---- Fixture shapes ----

    // Shape 1: minimal inline-only, single field.
    private const string Shape1Inline = """
        // OwningAssetId: a1000000-0000-0000-0000-000000000001
        // OwningAssetName: Shape1_BT

        namespace Test;

        public partial struct Shape1_BT_Blackboard
        {
            public int Counter;
        }
        """;

    // Shape 2: inline-only, several fields with XML doc comments.
    private const string Shape2Inline = """
        // OwningAssetId: a2000000-0000-0000-0000-000000000001
        // OwningAssetName: Shape2_BT

        namespace Test;

        public partial struct Shape2_BT_Blackboard
        {
            /// <summary>Current ammo count.</summary>
            public int AmmoCount;

            /// <summary>Whether target is visible.</summary>
            public bool TargetVisible;

            /// <summary>Alert level (0-5).</summary>
            public byte AlertLevel;
        }
        """;

    // Shape 3: inline-only, no doc comments, many primitive fields.
    private const string Shape3Inline = """
        // OwningAssetId: a3000000-0000-0000-0000-000000000001
        // OwningAssetName: Shape3_BT

        namespace Test;

        public partial struct Shape3_BT_Blackboard
        {
            public float Speed;
            public float TurnRate;
            public int EnemyCount;
            public bool IsPatrolling;
        }
        """;

    // Heavy companion for shape 2.
    private const string Shape2Heavy = """
        // OwningAssetId: a2000000-0000-0000-0000-000000000001
        // OwningAssetName: Shape2_BT

        namespace Test;

        public partial struct Shape2_BT_Blackboard
        {
            /// <summary>Overflow: large path cache.</summary>
            public float PathCache;
        }
        """;

    // ---- Helpers ----

    private static (string inlinePath, string? heavyPath) WritePair(
        string inlineContent, string? heavyContent = null)
    {
        string dir      = Path.GetTempPath();
        string baseName = $"bb_det_{Guid.NewGuid():N}";
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
    public void Sanitize_Shape1_InlineOnly_TenRuns_ByteIdentical()
    {
        var (inlinePath, heavyPath) = WritePair(Shape1Inline);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(inlinePath, null, AssetKind.Blackboard);
            string first  = sanitizer.Sanitize(request).SanitizedText;

            for (int i = 0; i < 9; i++)
                Assert.Equal(first, sanitizer.Sanitize(request).SanitizedText);
        }
        finally { DeletePair(inlinePath, heavyPath); }
    }

    [Fact]
    public void Sanitize_Shape2_InlineOnly_TenRuns_ByteIdentical()
    {
        var (inlinePath, heavyPath) = WritePair(Shape2Inline);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(inlinePath, null, AssetKind.Blackboard);
            string first  = sanitizer.Sanitize(request).SanitizedText;

            for (int i = 0; i < 9; i++)
                Assert.Equal(first, sanitizer.Sanitize(request).SanitizedText);
        }
        finally { DeletePair(inlinePath, heavyPath); }
    }

    [Fact]
    public void Sanitize_Shape3_InlineOnly_TenRuns_ByteIdentical()
    {
        var (inlinePath, heavyPath) = WritePair(Shape3Inline);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(inlinePath, null, AssetKind.Blackboard);
            string first  = sanitizer.Sanitize(request).SanitizedText;

            for (int i = 0; i < 9; i++)
                Assert.Equal(first, sanitizer.Sanitize(request).SanitizedText);
        }
        finally { DeletePair(inlinePath, heavyPath); }
    }

    [Fact]
    public void Sanitize_Shape2_WithHeavy_TenRuns_ByteIdentical()
    {
        var (inlinePath, heavyPath) = WritePair(Shape2Inline, Shape2Heavy);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(inlinePath, null, AssetKind.Blackboard);
            string first  = sanitizer.Sanitize(request).SanitizedText;

            for (int i = 0; i < 9; i++)
                Assert.Equal(first, sanitizer.Sanitize(request).SanitizedText);
        }
        finally { DeletePair(inlinePath, heavyPath); }
    }
}
