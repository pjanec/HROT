using System;
using System.IO;
using Hrot.BTree.Editor.Comparison;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Comparison;

public sealed class BTreeComparisonSanitizerTests
{
    // ---- Helpers ----

    private static BTreeComparisonSanitizer MakeSanitizer(params IEditableAsset[] assets) =>
        new BTreeComparisonSanitizer(new FakeCatalog(assets));

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"btree_test_{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, content);
        return path;
    }

    private static SanitizationResult RunOnText(string content, IAssetCatalog? catalog = null)
    {
        string path = WriteTemp(content);
        try
        {
            var sanitizer = new BTreeComparisonSanitizer(catalog ?? new FakeCatalog());
            var request   = new AssetExportRequest(path, null, AssetKind.BTree);
            return sanitizer.Sanitize(request);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- §3.3 "before" and "after" strings ----

    // Exact text from design §3.3 "Before" example (CRLF normalized to LF by sanitizer).
    private const string OrcGuardBefore = """
        // HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
        // AssetId: f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21

        using Hrot.Game.Combat;
        using Fbt.Compiler;

        namespace Hrot.AI.Behaviors.Trees;

        public static class OrcGuard
        {
            public static BTreeBuilder<OrcGuard_BT_Blackboard, BTreeContext> CreateBuilder() =>
                new BTreeBuilder<OrcGuard_BT_Blackboard, BTreeContext>()
                    .Sequence(s => s
                        .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat,
                                   visualId: new Guid("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29"))
                        .Action(dto => dto.AmmoCount, CombatActions.AimAndFire,
                                visualId: new Guid("c5e8b471-7a44-4d6e-9b1c-8f7a6e5d4c3b")),
                        visualId: new Guid("f7c01188-1188-4c5d-9e3a-7b6c5d4e3f21"));

            [BTreeDefinition("OrcGuard_BT", AssetId = "f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21")]
            public static BehaviorTreeBlob Build() => CreateBuilder().Compile("OrcGuard_BT");

            [BTreeLayout("f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21")]
            public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
                .Canvas(panOffset: new Vector2(12f, -34f), zoomLevel: 1.0f)
                .Node("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29",
                      position: new Vector2(120f, 340f),
                      comment: "must see enemy before engaging")
                .Node("c5e8b471-7a44-4d6e-9b1c-8f7a6e5d4c3b",
                      position: new Vector2(280f, 480f),
                      expressionTarget: "AmmoCount",
                      comment: "burst fire pattern")
                .Node("f7c01188-1188-4c5d-9e3a-7b6c5d4e3f21",
                      position: new Vector2(400f, 60f))
                .Build();
        }
        """;

    // Exact expected output from design §3.3 "After" example.
    private const string OrcGuardAfter = """
        // HROT_EDITOR_GENERATED — managed by AI editor.
        // AssetId: f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21

        using Hrot.Game.Combat;
        using Fbt.Compiler;

        namespace Hrot.AI.Behaviors.Trees;

        public static class OrcGuard
        {
            public static BTreeBuilder<OrcGuard_BT_Blackboard, BTreeContext> CreateBuilder() =>
                new BTreeBuilder<OrcGuard_BT_Blackboard, BTreeContext>()
                    .Sequence(s => s
                        // must see enemy before engaging
                        .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat,
                                   visualId: new Guid("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29"))
                        // burst fire pattern
                        .Action(dto => dto.AmmoCount, CombatActions.AimAndFire,
                                visualId: new Guid("c5e8b471-7a44-4d6e-9b1c-8f7a6e5d4c3b")),
                        visualId: new Guid("f7c01188-1188-4c5d-9e3a-7b6c5d4e3f21"));

            [BTreeDefinition("OrcGuard_BT", AssetId = "f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21")]
            public static BehaviorTreeBlob Build() => CreateBuilder().Compile("OrcGuard_BT");
        }
        """;

    // Normalize a raw-string-literal (which may have trailing indent on the closing delimiter)
    // to LF-only line endings, trimming the raw-string indentation.
    private static string N(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    // ---- Tests ----

    [Fact]
    public void Sanitize_Section33Before_ProducesSection33After_ByteForByte()
    {
        string before = N(OrcGuardBefore);
        // Raw string literals exclude the newline before their closing delimiter,
        // but the sanitizer always terminates the output with '\n'.
        string expected = N(OrcGuardAfter) + "\n";

        var result = RunOnText(before);

        Assert.Equal(expected, result.SanitizedText);
    }

    [Fact]
    public void Sanitize_SubtreeWithSyncAndCatalog_HoistsCommentSyncAndHumanizesGuid()
    {
        // Fixture using the actual emitter format for SubtreeSyncField
        // (syncIn: bool, syncOut: bool rather than the design doc's conceptual direction: SyncDirection).
        const string before = @"// HROT_EDITOR_GENERATED - test
// AssetId: 11111111-0000-0000-0000-000000000001

namespace Test;

public static class MasterBT
{
    public static BTreeBuilder<MasterBlackboard, BTreeContext> CreateBuilder() =>
        new BTreeBuilder<MasterBlackboard, BTreeContext>()
            .Subtree(""00000000-aaaa-0001-0000-000000000005"",
                     visualId: new Guid(""d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f""));

    [BTreeLayout(""11111111-0000-0000-0000-000000000001"")]
    public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
        .Node(""d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f"",
              position: new Vector2(0f, 0f),
              comment: ""delegate to shoot subtree"")
        .SubtreeSyncField(""d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f"", ""TargetNetworkId"", masterVar: ""SharedTarget"", syncIn: true, syncOut: false)
        .SubtreeSyncField(""d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f"", ""StatusOut"", masterVar: ""LastFireStatus"", syncIn: false, syncOut: true)
        .Build();
}
";
        var shootBtAsset = new FakeAsset
        {
            AssetId = new Guid("00000000-aaaa-0001-0000-000000000005"),
            Name    = "Shoot_BT",
            Kind    = AssetKind.BTree,
        };

        var result = RunOnText(before, new FakeCatalog(shootBtAsset));
        string text = result.SanitizedText;

        // Comment hoisted above the .Subtree(...) call.
        Assert.Contains("// delegate to shoot subtree", text);
        // Sync-in binding hoisted.
        Assert.Contains("// sync (in):", text);
        Assert.Contains("TargetNetworkId", text);
        Assert.Contains("<--", text);
        Assert.Contains("SharedTarget", text);
        // Sync-out binding hoisted.
        Assert.Contains("// sync (out):", text);
        Assert.Contains("StatusOut", text);
        Assert.Contains("-->", text);
        Assert.Contains("LastFireStatus", text);
        // Asset GUID humanized inline on the .Subtree(...) line.
        Assert.Contains("// -> Shoot_BT (BTree)", text);

        // D-02 fix: verify that comment appears before sync-in, sync-in before sync-out,
        // and all three appear before the .Subtree(...) builder call.
        string[] outputLines = text.Split('\n');
        int commentIdx   = Array.FindIndex(outputLines, l => l.Contains("// delegate to shoot subtree"));
        int syncInIdx    = Array.FindIndex(outputLines, l => l.Contains("// sync (in):"));
        int syncOutIdx   = Array.FindIndex(outputLines, l => l.Contains("// sync (out):"));
        int subtreeIdx   = Array.FindIndex(outputLines, l => l.TrimStart().StartsWith(".Subtree("));

        Assert.True(commentIdx  >= 0, "comment line not found");
        Assert.True(syncInIdx   >= 0, "sync-in line not found");
        Assert.True(syncOutIdx  >= 0, "sync-out line not found");
        Assert.True(subtreeIdx  >= 0, ".Subtree( call not found");
        Assert.True(commentIdx  < syncInIdx,   "comment must appear before sync-in");
        Assert.True(syncInIdx   < syncOutIdx,  "sync-in must appear before sync-out");
        Assert.True(syncOutIdx  < subtreeIdx,  "sync-out must appear before .Subtree call");
    }

    [Fact]
    public void Sanitize_SubtreeGuidNotInCatalog_ProducesAssetNotFoundComment()
    {
        const string before = @"// AssetId: 22222222-0000-0000-0000-000000000001

namespace Test;

public static class BT
{
    public static BTreeBuilder<BB, Ctx> CreateBuilder() =>
        new BTreeBuilder<BB, Ctx>()
            .Subtree(""ffffffff-ffff-ffff-ffff-ffffffffffff"",
                     visualId: new Guid(""aaaaaaaa-0000-0000-0000-000000000001""));

    [BTreeLayout(""22222222-0000-0000-0000-000000000001"")]
    public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
        .Build();
}
";
        var result = RunOnText(before, new FakeCatalog()); // empty catalog

        Assert.Contains("// -> (asset not found in catalog)", result.SanitizedText);
    }

    [Fact]
    public void Sanitize_RunTenTimes_ProducesByteIdenticalOutput()
    {
        const string input = @"// AssetId: 33333333-0000-0000-0000-000000000001

namespace Test;

public static class BT
{
    public static BTreeBuilder<BB, Ctx> CreateBuilder() =>
        new BTreeBuilder<BB, Ctx>()
            .Sequence(s => s
                .Action(SomeActions.DoThing,
                        visualId: new Guid(""aaaabbbb-0000-0000-0000-000000000001"")),
                visualId: new Guid(""ccccdddd-0000-0000-0000-000000000001""));

    [BTreeLayout(""33333333-0000-0000-0000-000000000001"")]
    public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
        .Node(""aaaabbbb-0000-0000-0000-000000000001"",
              position: new Vector2(0f, 0f),
              comment: ""do something important"")
        .Build();
}
";
        string path = WriteTemp(input);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(path, null, AssetKind.BTree);
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
    public void Sanitize_NoLayoutMethod_ReturnsInputVerbatimWithWarning()
    {
        const string input = @"// AssetId: 44444444-0000-0000-0000-000000000001

namespace Test;

public static class BT
{
    public static BTreeBuilder<BB, Ctx> CreateBuilder() =>
        new BTreeBuilder<BB, Ctx>();
}
";
        var result = RunOnText(input);

        // SanitizedText should be the (normalized) input unchanged.
        Assert.Contains("public static class BT", result.SanitizedText);
        // Must have a warning.
        Assert.Single(result.Warnings);
        Assert.Contains("Layout method not found", result.Warnings[0].Message);
    }

    [Fact]
    public void Sanitize_MalformedFile_ReturnsResultWithWarning_NeverThrows()
    {
        // Deliberately malformed: unbalanced braces and truncated content.
        const string malformed = @"namespace Test {
    public static class BT {
        // missing everything
";
        var result = RunOnText(malformed);

        // Must return a result (not throw) and must have at least one warning.
        Assert.NotNull(result);
        Assert.NotEmpty(result.Warnings);
    }

    // ---- C-32: NoComments fixture -------------------------------------------

    [Fact]
    public void NoComments_Asset_SanitizesWithoutError()
    {
        // Fixture: a BTree file with no // comments at all.
        string fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(BTreeComparisonSanitizerTests).Assembly.Location)!,
            "Comparison", "Fixtures", "NoCommentsBTree.cs");

        var sanitizer = MakeSanitizer();
        var request   = new AssetExportRequest(fixturePath, null, AssetKind.BTree);
        var result    = sanitizer.Sanitize(request);

        // No // comment lines should be hoisted (input had none).
        Assert.DoesNotContain("//", result.SanitizedText);
        // No warnings expected for a valid (if comment-free) file.
        Assert.Empty(result.Warnings);
    }

    // ---- C-32: MalformedFile_NoCSharpClass inline ----------------------------

    [Fact]
    public void MalformedFile_NoCSharpClass_ReturnsFallback()
    {
        // Not a valid C# BTree file.
        const string notCSharp = "this is not valid csharp and has no class";

        string path = Path.Combine(Path.GetTempPath(), $"malformed_{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, notCSharp);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(path, null, AssetKind.BTree);
            var result    = sanitizer.Sanitize(request);

            // Must not throw; result is not null.
            Assert.NotNull(result);
            // Either the sanitized text is minimal, or at least one warning is present.
            Assert.True(
                string.IsNullOrEmpty(result.SanitizedText) || result.Warnings.Count > 0,
                "Expected empty sanitized text or at least one warning for a malformed file.");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
