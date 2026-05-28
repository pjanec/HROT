using System;
using System.IO;
using Hrot.Editor.AiShared.Comparison;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class BlackboardComparisonSanitizerTests
{
    // ---- Helpers ----

    private static BlackboardComparisonSanitizer MakeSanitizer() =>
        new BlackboardComparisonSanitizer();

    /// <summary>
    /// Writes the given content to a temp file with the given suffix and returns the path.
    /// The caller is responsible for deleting the file.
    /// </summary>
    private static string WriteTemp(string suffix, string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"bb_test_{Guid.NewGuid():N}{suffix}");
        File.WriteAllText(path, content);
        return path;
    }

    private static SanitizationResult RunOnFile(string mainPath)
    {
        var sanitizer = MakeSanitizer();
        return sanitizer.Sanitize(new AssetExportRequest(mainPath, null, AssetKind.Blackboard));
    }

    // ---- Inline content ----

    private const string SimpleInlineContent = """
        // HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
        // Hand-introduced fields with attributes or non-standard types are preserved verbatim.
        // OwningAssetId: 11000000-0000-0000-0000-000000000001
        // OwningAssetName: Soldier_BT

        using System.Runtime.InteropServices;

        namespace Hrot.AI.Behaviors.Trees;

        [StructLayout(LayoutKind.Sequential)]
        public partial struct Soldier_BT_Blackboard
        {
            /// <summary>Number of ammo shots remaining.</summary>
            public int AmmoCount;

            /// <summary>Whether a valid target is currently in sight.</summary>
            public bool TargetVisible;
        }
        """;

    private const string SimpleHeavyContent = """
        // HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
        // Hand-introduced fields with attributes or non-standard types are preserved verbatim.
        // OwningAssetId: 11000000-0000-0000-0000-000000000001
        // OwningAssetName: Soldier_BT

        using System.Runtime.InteropServices;

        namespace Hrot.AI.Behaviors.Trees;

        [StructLayout(LayoutKind.Sequential)]
        public partial struct Soldier_BT_Blackboard
        {
            /// <summary>Overflow: large payload buffer.</summary>
            public float OverflowField;
        }
        """;

    // Normalize raw-string literal to LF-only line endings.
    private static string N(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    // ---- Tests ----

    [Fact]
    public void Sanitize_InlineOnly_OutputContainsInlineSectionAndNoHeavySection()
    {
        string inlinePath = WriteTemp(".Blackboard.cs", N(SimpleInlineContent));
        try
        {
            var result = RunOnFile(inlinePath);
            string text = result.SanitizedText;

            Assert.Contains("// === Inline blackboard ===", text);
            Assert.DoesNotContain("// === Heavy blackboard", text);
            Assert.Contains("public partial struct Soldier_BT_Blackboard", text);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            File.Delete(inlinePath);
        }
    }

    [Fact]
    public void Sanitize_InlinePlusHeavy_OutputContainsBothLabeledSectionsInOrder()
    {
        // Write paired files: Foo.Blackboard.cs + Foo.HeavyBlackboard.cs
        string dir     = Path.GetTempPath();
        string baseName = $"bb_test_{Guid.NewGuid():N}";
        string inlinePath = Path.Combine(dir, $"{baseName}.Blackboard.cs");
        string heavyPath  = Path.Combine(dir, $"{baseName}.HeavyBlackboard.cs");

        File.WriteAllText(inlinePath, N(SimpleInlineContent));
        File.WriteAllText(heavyPath, N(SimpleHeavyContent));
        try
        {
            var result = RunOnFile(inlinePath);
            string text = result.SanitizedText;

            int inlineIdx = text.IndexOf("// === Inline blackboard ===", StringComparison.Ordinal);
            int heavyIdx  = text.IndexOf("// === Heavy blackboard (overflow) ===", StringComparison.Ordinal);

            Assert.True(inlineIdx >= 0, "inline section header not found");
            Assert.True(heavyIdx  >= 0, "heavy section header not found");
            Assert.True(inlineIdx < heavyIdx, "inline section must appear before heavy section");

            // Both struct bodies present.
            Assert.Contains("public int AmmoCount", text);
            Assert.Contains("public float OverflowField", text);

            // Companion file listed in metadata.
            Assert.Contains(heavyPath, result.Metadata.CompanionFiles);
        }
        finally
        {
            File.Delete(inlinePath);
            if (File.Exists(heavyPath)) File.Delete(heavyPath);
        }
    }

    [Fact]
    public void Sanitize_XmlDocCommentsPreservedVerbatim()
    {
        const string content = """
            // OwningAssetId: 22000000-0000-0000-0000-000000000001
            // OwningAssetName: Test_BT

            namespace Test;

            public partial struct Test_BT_Blackboard
            {
                /// <summary>Number of ammo shots remaining.</summary>
                public int AmmoCount;
            }
            """;

        string path = WriteTemp(".Blackboard.cs", N(content));
        try
        {
            var result = RunOnFile(path);
            Assert.Contains("/// <summary>Number of ammo shots remaining.</summary>", result.SanitizedText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sanitize_MissingMainFile_ReturnsResultWithWarning_NeverThrows()
    {
        string nonExistent = Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.Blackboard.cs");
        var sanitizer = MakeSanitizer();
        var result = sanitizer.Sanitize(new AssetExportRequest(nonExistent, null, AssetKind.Blackboard));

        Assert.NotNull(result);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains("File not found", result.Warnings[0].Message);
    }

    [Fact]
    public void Sanitize_AssetNameAndIdExtractedFromOwningHeaders()
    {
        const string content = """
            // HROT_EDITOR_GENERATED — managed by AI editor.
            // Hand-introduced fields with attributes or non-standard types are preserved verbatim.
            // OwningAssetId: 33000000-0000-0000-0000-000000000001
            // OwningAssetName: Guard_BT

            namespace Test;

            public partial struct Guard_BT_Blackboard
            {
                public bool IsAlert;
            }
            """;

        string path = WriteTemp(".Blackboard.cs", N(content));
        try
        {
            var result = RunOnFile(path);

            Assert.Equal("Guard_BT", result.Metadata.AssetName);
            Assert.Equal(new Guid("33000000-0000-0000-0000-000000000001"), result.Metadata.AssetId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sanitize_RunTenTimes_InlineOnly_ProducesByteIdenticalOutput()
    {
        string path = WriteTemp(".Blackboard.cs", N(SimpleInlineContent));
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(path, null, AssetKind.Blackboard);
            string first  = sanitizer.Sanitize(request).SanitizedText;

            for (int i = 0; i < 9; i++)
            {
                string run = sanitizer.Sanitize(request).SanitizedText;
                Assert.Equal(first, run);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sanitize_RunTenTimes_InlinePlusHeavy_ProducesByteIdenticalOutput()
    {
        string dir      = Path.GetTempPath();
        string baseName = $"bb_det_{Guid.NewGuid():N}";
        string inlinePath = Path.Combine(dir, $"{baseName}.Blackboard.cs");
        string heavyPath  = Path.Combine(dir, $"{baseName}.HeavyBlackboard.cs");

        File.WriteAllText(inlinePath, N(SimpleInlineContent));
        File.WriteAllText(heavyPath, N(SimpleHeavyContent));
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(inlinePath, null, AssetKind.Blackboard);
            string first  = sanitizer.Sanitize(request).SanitizedText;

            for (int i = 0; i < 9; i++)
            {
                string run = sanitizer.Sanitize(request).SanitizedText;
                Assert.Equal(first, run);
            }
        }
        finally
        {
            File.Delete(inlinePath);
            if (File.Exists(heavyPath)) File.Delete(heavyPath);
        }
    }

    // ---- D-06: AssetId: header form -----------------------------------------

    [Fact]
    public void AssetIdHeader_Form_SanitizesCorrectly()
    {
        // Uses the // AssetId: header form instead of // OwningAssetId:.
        // Both forms must be handled without error and with correct metadata extraction.
        const string content = """
            // HROT_EDITOR_GENERATED -- managed by AI editor.
            // AssetId: 11aa0000-0000-0000-0000-000000000001
            // OwningAssetName: Scout_BT

            using System.Runtime.InteropServices;

            namespace Hrot.AI.Behaviors.Trees;

            [StructLayout(LayoutKind.Sequential)]
            public partial struct Scout_BT_Blackboard
            {
                public int HealthPoints;
            }
            """;

        string path = WriteTemp(".Blackboard.cs", N(content));
        try
        {
            var result = RunOnFile(path);

            // Sanitizer must not fail.
            Assert.NotEmpty(result.SanitizedText);
            // Section label must be present.
            Assert.Contains("// === Inline blackboard ===", result.SanitizedText);
            // AssetId header is preserved verbatim in output (sanitizer does not strip content).
            Assert.Contains("AssetId:", result.SanitizedText);
            // GUID is correctly extracted into metadata from the AssetId: header.
            Assert.Equal(new Guid("11aa0000-0000-0000-0000-000000000001"), result.Metadata.AssetId);
            // No warnings.
            Assert.Empty(result.Warnings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- C-32: ReadOnly fixture ----------------------------------------------

    [Fact]
    public void ReadOnly_AllFields_SanitizesSuccessfully()
    {
        // Fixture: a Blackboard file where all fields carry a [ReadOnly] attribute.
        string fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(BlackboardComparisonSanitizerTests).Assembly.Location)!,
            "Comparison", "Fixtures", "ReadOnlyBlackboard.cs");

        // Rename to match the .Blackboard.cs suffix expected by the sanitizer.
        string tempPath = WriteTemp(".Blackboard.cs", File.ReadAllText(fixturePath));
        try
        {
            var result = RunOnFile(tempPath);

            Assert.NotNull(result);
            // Field names must be preserved in output.
            Assert.Contains("TargetX", result.SanitizedText);
            Assert.Contains("TargetEntityId", result.SanitizedText);
            Assert.Contains("ElapsedTime", result.SanitizedText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
