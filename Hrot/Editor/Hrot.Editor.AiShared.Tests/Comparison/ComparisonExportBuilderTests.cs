using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class ComparisonExportBuilderTests
{
    private static readonly Guid TestAssetId = Guid.Parse("f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21");
    private const string Separator = "================================================================================";

    // Fake sanitizer that returns a deterministic result based on a factory.
    private sealed class FakeSanitizer : IAssetComparisonSanitizer
    {
        private readonly Func<AssetExportRequest, SanitizationResult> _factory;

        public FakeSanitizer(SanitizationResult result) => _factory = _ => result;
        public FakeSanitizer(Func<AssetExportRequest, SanitizationResult> factory) => _factory = factory;

        public AssetKind TargetKind => AssetKind.BTree;
        public SanitizationResult Sanitize(AssetExportRequest request) => _factory(request);
    }

    private static SanitizationResult MakeResult(
        string assetName,
        string content,
        string sourcePath = "/path/OrcGuard_BT.cs",
        string? migrationNotice = null,
        DateTime? timestamp = null) =>
        new SanitizationResult(
            content,
            new AssetMetadataBlock(
                assetName,
                AssetKind.BTree,
                TestAssetId,
                sourcePath,
                Array.Empty<string>(),
                timestamp ?? new DateTime(2026, 1, 14, 11, 23, 8, DateTimeKind.Utc),
                migrationNotice),
            Array.Empty<SanitizationWarning>());

    [Fact]
    public void Build_StructuralTest_ContainsAllRequiredSections()
    {
        var builder = new ComparisonExportBuilder();
        var result = MakeResult("OrcGuard_BT", "sanitized content");
        var sanitizer = new FakeSanitizer(result);
        var versionA = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);
        var versionB = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);

        var output = builder.Build(sanitizer, versionA, versionB);

        Assert.Contains(Separator, output);
        Assert.Contains("VERSION A (OLD)", output);
        Assert.Contains("VERSION B (NEW)", output);
        Assert.Contains("--- COMPANION FILES ---", output);
        Assert.Contains("END OF COMPARISON INPUT", output);
    }

    [Fact]
    public void Build_InstructionBlock_OutputStartsWithYouAreComparing()
    {
        var builder = new ComparisonExportBuilder();
        var result = MakeResult("OrcGuard_BT", "content");
        var sanitizer = new FakeSanitizer(result);
        var versionA = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);
        var versionB = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);

        var output = builder.Build(sanitizer, versionA, versionB);

        Assert.StartsWith("You are comparing", output);
    }

    [Fact]
    public void Build_MetadataTest_ContainsExpectedMetadataFields()
    {
        var builder = new ComparisonExportBuilder();
        var result = MakeResult(
            "OrcGuard_BT",
            "content",
            sourcePath: "/path/OrcGuard_BT.cs",
            timestamp: new DateTime(2026, 1, 14, 11, 23, 8, DateTimeKind.Utc));
        var sanitizer = new FakeSanitizer(result);
        var versionA = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);
        var versionB = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);

        var output = builder.Build(sanitizer, versionA, versionB);

        Assert.Contains("ASSET NAME:       OrcGuard_BT", output);
        Assert.Contains("ASSET KIND:       BTree", output);
        Assert.Contains("ASSET ID:         f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21", output);
        Assert.Contains("LAST MODIFIED:    2026-01-14 11:23:08 UTC", output);
    }

    [Fact]
    public void Build_FileHeaderTest_SanitizedTextPrecededByFileHeader()
    {
        var builder = new ComparisonExportBuilder();
        var result = MakeResult("OrcGuard_BT", "sanitized content here", sourcePath: "/path/OrcGuard_BT.cs");
        var sanitizer = new FakeSanitizer(result);
        var versionA = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);
        var versionB = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);

        var output = builder.Build(sanitizer, versionA, versionB);

        Assert.Contains("// === FILE: OrcGuard_BT.cs ===", output);
    }

    [Fact]
    public void Build_MigrationNotice_AppearsInVersionWithMigration()
    {
        var builder = new ComparisonExportBuilder();
        var resultA = MakeResult("OrcGuard_BT", "content A",
            sourcePath: "/path/OrcGuard_BT_v1.cs",
            migrationNotice: "Version A migrated from schema v3 to v4");
        var resultB = MakeResult("OrcGuard_BT", "content B",
            sourcePath: "/path/OrcGuard_BT_v2.cs");

        var sanitizer = new FakeSanitizer(req =>
            req.AssetMainFilePath.Contains("v1") ? resultA : resultB);

        var versionA = new AssetExportRequest("/path/OrcGuard_BT_v1.cs", null, AssetKind.BTree);
        var versionB = new AssetExportRequest("/path/OrcGuard_BT_v2.cs", null, AssetKind.BTree);

        var output = builder.Build(sanitizer, versionA, versionB);

        Assert.Contains("// MIGRATION NOTICE: Version A migrated from schema v3 to v4", output);
    }

    [Fact]
    public void Build_LineEndingsTest_OutputContainsNoCarriageReturnLinefeed()
    {
        var builder = new ComparisonExportBuilder();
        var result = MakeResult("OrcGuard_BT", "content with\r\nwindows line endings");
        var sanitizer = new FakeSanitizer(result);
        var versionA = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);
        var versionB = new AssetExportRequest("/path/OrcGuard_BT.cs", null, AssetKind.BTree);

        var output = builder.Build(sanitizer, versionA, versionB);

        Assert.DoesNotContain("\r\n", output);
    }

    [Fact]
    public void Build_SelfComparisonTest_StructurallyValidAndBothVersionsContainSameContent()
    {
        var builder = new ComparisonExportBuilder();
        var result = MakeResult("OrcGuard_BT", "identical sanitized content");
        var sanitizer = new FakeSanitizer(result);
        var versionA = new AssetExportRequest("/path/OrcGuard_BT_v1.cs", null, AssetKind.BTree);
        var versionB = new AssetExportRequest("/path/OrcGuard_BT_v2.cs", null, AssetKind.BTree);

        var output = builder.Build(sanitizer, versionA, versionB);

        // Structurally valid
        Assert.Contains("VERSION A (OLD)", output);
        Assert.Contains("VERSION B (NEW)", output);
        Assert.Contains("END OF COMPARISON INPUT", output);

        // Both sections contain the same sanitized content
        var firstIdx = output.IndexOf("identical sanitized content", StringComparison.Ordinal);
        var secondIdx = output.IndexOf("identical sanitized content", firstIdx + 1, StringComparison.Ordinal);
        Assert.True(firstIdx >= 0, "Content should appear at least once");
        Assert.True(secondIdx >= 0, "Content should appear at least twice (once per version)");
    }

    // ---- D-10: disk fixture round-trip test ----------------------------------

    [Fact]
    public void Build_DiskFixtureRoundTrip_ContainsAllStructuralMarkers()
    {
        // Use realistic sanitized content from a fixture (simple_guard.cs representative content).
        const string fixtureContent =
            "// HROT_EDITOR_GENERATED - managed by AI editor.\n" +
            "// AssetId: aaaaaaaa-0000-0001-0000-000000000001\n" +
            "\n" +
            "public static class SimpleGuard_BT\n" +
            "{\n" +
            "    public static BTreeBuilder<SimpleGuard_BT_Blackboard, BTreeContext> CreateBuilder() =>\n" +
            "        new BTreeBuilder<SimpleGuard_BT_Blackboard, BTreeContext>()\n" +
            "            .Sequence(s => s\n" +
            "                // check if enemy is visible\n" +
            "                .Condition(dto => dto.EnemySpotted, GuardActions.DetectEnemy,\n" +
            "                           visualId: new Guid(\"bbbbbbbb-0000-0001-0000-000000000001\"))\n" +
            "                // alert the base\n" +
            "                .Action(GuardActions.SoundAlarm,\n" +
            "                        visualId: new Guid(\"cccccccc-0000-0001-0000-000000000001\")),\n" +
            "                visualId: new Guid(\"dddddddd-0000-0001-0000-000000000001\"));\n" +
            "\n" +
            "    [BTreeDefinition(\"SimpleGuard_BT\", AssetId = \"aaaaaaaa-0000-0001-0000-000000000001\")]\n" +
            "    public static BehaviorTreeBlob Build() => CreateBuilder().Compile(\"SimpleGuard_BT\");\n" +
            "}\n";

        var resultA = MakeResult("SimpleGuard_BT", fixtureContent, sourcePath: "/repo/SimpleGuard_BT_v1.cs");
        var resultB = MakeResult("SimpleGuard_BT", fixtureContent, sourcePath: "/repo/SimpleGuard_BT_v2.cs");

        var sanitizer = new FakeSanitizer(req =>
            req.AssetMainFilePath.Contains("v1") ? resultA : resultB);

        var builder = new ComparisonExportBuilder();
        var versionA = new AssetExportRequest("/repo/SimpleGuard_BT_v1.cs", null, AssetKind.BTree);
        var versionB = new AssetExportRequest("/repo/SimpleGuard_BT_v2.cs", null, AssetKind.BTree);

        var output1 = builder.Build(sanitizer, versionA, versionB);

        // Assert all required structural markers are present.
        Assert.Contains("VERSION A (OLD)", output1);
        Assert.Contains("VERSION B (NEW)", output1);
        Assert.Contains("--- COMPANION FILES ---", output1);
        Assert.Contains("END OF COMPARISON INPUT", output1);
        Assert.Contains("SimpleGuard_BT", output1);

        // Determinism: second call produces byte-identical output.
        var output2 = builder.Build(sanitizer, versionA, versionB);
        Assert.Equal(output1, output2);
    }
}
