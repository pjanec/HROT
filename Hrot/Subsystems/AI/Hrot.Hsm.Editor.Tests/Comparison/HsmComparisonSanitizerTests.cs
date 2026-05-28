using System;
using System.IO;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Hsm.Editor.Comparison;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Comparison;

public sealed class HsmComparisonSanitizerTests
{
    // ---- Helpers ----

    private static HsmComparisonSanitizer MakeSanitizer() =>
        new HsmComparisonSanitizer(new FakeCatalog());

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hsm_test_{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, content);
        return path;
    }

    private static SanitizationResult RunOnText(string content)
    {
        string path = WriteTemp(content);
        try
        {
            var sanitizer = MakeSanitizer();
            return sanitizer.Sanitize(new AssetExportRequest(path, null, AssetKind.Hsm));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Tests ----

    [Fact]
    public void Sanitize_SimpleStateMachine_HoistsStateAndTransitionComments()
    {
        // Two states, one with a comment in the layout; one transition with a comment.
        const string input = @"// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: a1000000-0000-0000-0000-000000000001

using System;
using System.Numerics;
using Hrot.Editor.AiShared.Layout;

namespace Test;

public static class SM
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder(""SM"");

        var s0 = builder.State(""Idle"", stableId: new Guid(""aa000000-0000-0000-0000-000000000001""));
        s0.On(""Go"").GoTo(""Active"", visualId: new Guid(""cc000000-0000-0000-0000-000000000001""));

        builder.State(""Active"", stableId: new Guid(""bb000000-0000-0000-0000-000000000001""));

        return builder;
    }

    [HsmDefinition(""SM"", AssetId = ""a1000000-0000-0000-0000-000000000001"")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout(""a1000000-0000-0000-0000-000000000001"")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .State(""aa000000-0000-0000-0000-000000000001"", new Vector2(100f, 100f),
               comment: ""waiting for user input"")
        .State(""bb000000-0000-0000-0000-000000000001"", new Vector2(300f, 100f))
        .Transition(""cc000000-0000-0000-0000-000000000001"",
                    new Vector2[] { new Vector2(200f, 100f) },
                    comment: ""triggered by external event"")
        .Build();
}
";
        var result = RunOnText(input);
        string text = result.SanitizedText;

        // Comment for Idle state must be hoisted above its builder.State(...) call.
        var outputLines = text.Split('\n');
        int commentLineIdx  = Array.FindIndex(outputLines, l => l.Contains("// waiting for user input"));
        int stateIdleIdx    = Array.FindIndex(outputLines, l => l.Contains("builder.State(\"Idle\""));

        Assert.True(commentLineIdx >= 0, "state comment not found in output");
        Assert.True(stateIdleIdx  >= 0, "builder.State(\"Idle\") not found in output");
        Assert.True(commentLineIdx < stateIdleIdx, "comment must appear before builder.State(\"Idle\")");

        // Comment for transition must be hoisted above the .On(...).GoTo(...) line.
        int transCommentIdx = Array.FindIndex(outputLines, l => l.Contains("// triggered by external event"));
        int onCallIdx       = Array.FindIndex(outputLines, l => l.TrimStart().StartsWith("s0.On("));

        Assert.True(transCommentIdx >= 0, "transition comment not found in output");
        Assert.True(onCallIdx       >= 0, ".On( call not found in output");
        Assert.True(transCommentIdx < onCallIdx, "transition comment must appear before .On( call");

        // Layout method must be stripped.
        Assert.DoesNotContain("[HsmLayout(", text);
        Assert.DoesNotContain("Vector2(100f", text);

        // Header suffix stripped.
        Assert.DoesNotContain("manual edits to this file", text);
    }

    [Fact]
    public void Sanitize_ParallelRegions_HoistsRegionComments()
    {
        // A parallel state with two child regions; each region has a comment in the layout.
        const string input = @"// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: b2000000-0000-0000-0000-000000000001

using System;
using System.Numerics;
using Hrot.Editor.AiShared.Layout;

namespace Test;

public static class Parallel
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder(""Parallel"");

        var running = builder.State(""Running"", stableId: new Guid(""30000000-0000-0000-0000-000000000001""));
        running.Child(""MotionTrack"", sb2 =>
        {
            sb2.Initial();
        }, stableId: new Guid(""40000000-0000-0000-0000-000000000001""));
        running.Child(""AnimTrack"", sb2 =>
        {
            sb2.Initial();
        }, stableId: new Guid(""50000000-0000-0000-0000-000000000001""));

        return builder;
    }

    [HsmDefinition(""Parallel"", AssetId = ""b2000000-0000-0000-0000-000000000001"")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout(""b2000000-0000-0000-0000-000000000001"")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .State(""30000000-0000-0000-0000-000000000001"", new Vector2(200f, 100f),
               comment: ""parallel execution state"")
        .Region(""40000000-0000-0000-0000-000000000001"", 0, new Vector2(210f, 150f),
                comment: ""handles movement updates"")
        .Region(""50000000-0000-0000-0000-000000000001"", 1, new Vector2(350f, 150f),
                comment: ""handles animation blending"")
        .Build();
}
";
        var result = RunOnText(input);
        string text = result.SanitizedText;

        var outputLines = text.Split('\n');

        // Region comment for MotionTrack must appear before running.Child("MotionTrack", ...
        int motionCommentIdx = Array.FindIndex(outputLines, l => l.Contains("// handles movement updates"));
        int motionChildIdx   = Array.FindIndex(outputLines, l => l.Contains("running.Child(\"MotionTrack\""));

        Assert.True(motionCommentIdx >= 0, "MotionTrack region comment not found");
        Assert.True(motionChildIdx   >= 0, "running.Child(\"MotionTrack\") not found");
        Assert.True(motionCommentIdx < motionChildIdx, "MotionTrack comment must appear before its Child call");

        // Region comment for AnimTrack must appear before running.Child("AnimTrack", ...
        int animCommentIdx = Array.FindIndex(outputLines, l => l.Contains("// handles animation blending"));
        int animChildIdx   = Array.FindIndex(outputLines, l => l.Contains("running.Child(\"AnimTrack\""));

        Assert.True(animCommentIdx >= 0, "AnimTrack region comment not found");
        Assert.True(animChildIdx   >= 0, "running.Child(\"AnimTrack\") not found");
        Assert.True(animCommentIdx < animChildIdx, "AnimTrack comment must appear before its Child call");
    }

    [Fact]
    public void Sanitize_GlobalTransitionWithComment_HoistsCommentAboveGlobalTransitionCall()
    {
        const string input = @"// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: c3000000-0000-0000-0000-000000000001

using System;
using System.Numerics;
using Hrot.Editor.AiShared.Layout;

namespace Test;

public static class WithGlobal
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder(""WithGlobal"");

        builder.Event(""Reset"", 1, 0, false, false);

        builder.State(""Running"", stableId: new Guid(""aa000000-0000-0001-0000-000000000001""));

        builder.GlobalTransition(""Reset"", ""Running"", visualId: new Guid(""bb000000-0000-0001-0000-000000000001""));

        return builder;
    }

    [HsmDefinition(""WithGlobal"", AssetId = ""c3000000-0000-0000-0000-000000000001"")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout(""c3000000-0000-0000-0000-000000000001"")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .State(""aa000000-0000-0001-0000-000000000001"", new Vector2(200f, 200f))
        .Transition(""bb000000-0000-0001-0000-000000000001"",
                    new Vector2[] { new Vector2(100f, 50f) },
                    comment: ""emergency reset from any state"")
        .Build();
}
";
        var result = RunOnText(input);
        string text = result.SanitizedText;

        var outputLines = text.Split('\n');
        int commentIdx    = Array.FindIndex(outputLines, l => l.Contains("// emergency reset from any state"));
        int globalTransIdx = Array.FindIndex(outputLines, l => l.Contains("builder.GlobalTransition("));

        Assert.True(commentIdx    >= 0, "global transition comment not found in output");
        Assert.True(globalTransIdx >= 0, "builder.GlobalTransition not found in output");
        Assert.True(commentIdx < globalTransIdx, "comment must appear before builder.GlobalTransition");
    }

    [Fact]
    public void Sanitize_TransitionViaOnGoToWithComment_HoistsCommentAboveOnCall()
    {
        const string input = @"// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: d4000000-0000-0000-0000-000000000001

using System;
using System.Numerics;
using Hrot.Editor.AiShared.Layout;

namespace Test;

public static class WithTransition
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder(""WithTransition"");

        builder.Event(""Trigger"", 1, 0, false, false);

        var s1 = builder.State(""StateA"", stableId: new Guid(""aa100000-0000-0000-0000-000000000001""));
        s1.On(""Trigger"").GoTo(""StateB"", visualId: new Guid(""cc100000-0000-0000-0000-000000000001""));

        builder.State(""StateB"", stableId: new Guid(""bb100000-0000-0000-0000-000000000001""));

        return builder;
    }

    [HsmDefinition(""WithTransition"", AssetId = ""d4000000-0000-0000-0000-000000000001"")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout(""d4000000-0000-0000-0000-000000000001"")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .State(""aa100000-0000-0000-0000-000000000001"", new Vector2(100f, 100f))
        .State(""bb100000-0000-0000-0000-000000000001"", new Vector2(300f, 100f))
        .Transition(""cc100000-0000-0000-0000-000000000001"",
                    new Vector2[] { new Vector2(200f, 100f) },
                    comment: ""moves to next phase"")
        .Build();
}
";
        var result = RunOnText(input);
        string text = result.SanitizedText;

        var outputLines = text.Split('\n');
        int commentIdx = Array.FindIndex(outputLines, l => l.Contains("// moves to next phase"));
        int onCallIdx  = Array.FindIndex(outputLines, l => l.TrimStart().StartsWith("s1.On("));

        Assert.True(commentIdx >= 0, "transition comment not found");
        Assert.True(onCallIdx  >= 0, "s1.On( call not found");
        Assert.True(commentIdx < onCallIdx, "comment must appear before .On( call");
    }

    [Fact]
    public void Sanitize_RunTenTimes_ProducesByteIdenticalOutput()
    {
        const string input = @"// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: e5000000-0000-0000-0000-000000000001

using System;
using System.Numerics;
using Hrot.Editor.AiShared.Layout;

namespace Test;

public static class DetSM
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder(""DetSM"");
        builder.State(""S1"", stableId: new Guid(""11110000-0000-0000-0000-000000000001""));
        builder.State(""S2"", stableId: new Guid(""22220000-0000-0000-0000-000000000001""));
        return builder;
    }

    [HsmDefinition(""DetSM"", AssetId = ""e5000000-0000-0000-0000-000000000001"")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout(""e5000000-0000-0000-0000-000000000001"")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .State(""11110000-0000-0000-0000-000000000001"", new Vector2(100f, 100f),
               comment: ""first state"")
        .State(""22220000-0000-0000-0000-000000000001"", new Vector2(300f, 100f),
               comment: ""second state"")
        .Build();
}
";
        string path = WriteTemp(input);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(path, null, AssetKind.Hsm);
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
        const string input = @"// AssetId: f6000000-0000-0000-0000-000000000001

namespace Test;

public static class NoLayout
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder(""NoLayout"");
        builder.State(""S1"", stableId: new Guid(""11000000-0000-0000-0000-000000000001""));
        return builder;
    }
}
";
        var result = RunOnText(input);

        Assert.Contains("public static class NoLayout", result.SanitizedText);
        Assert.Single(result.Warnings);
        Assert.Contains("Layout method not found", result.Warnings[0].Message);
    }

    [Fact]
    public void Sanitize_MalformedFile_ReturnsResultWithWarning_NeverThrows()
    {
        const string malformed = @"namespace Test {
    public static class BadHsm {
        // deliberately missing everything
";
        var result = RunOnText(malformed);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Warnings);
    }

    // ---- C-32: ParallelHsm fixture ------------------------------------------

    [Fact]
    public void ParallelRegions_WithGlobalTransitions_SanitizesCorrectly()
    {
        string fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(HsmComparisonSanitizerTests).Assembly.Location)!,
            "Comparison", "Fixtures", "ParallelHsm.cs");

        var sanitizer = MakeSanitizer();
        var request   = new AssetExportRequest(fixturePath, null, AssetKind.Hsm);
        var result1   = sanitizer.Sanitize(request);

        // Output must be non-empty and must not contain layout coordinates.
        Assert.NotEmpty(result1.SanitizedText);
        Assert.DoesNotContain("Vector2(", result1.SanitizedText);
        Assert.DoesNotContain("[HsmLayout(", result1.SanitizedText);

        // Determinism: second run produces identical output.
        var result2 = sanitizer.Sanitize(request);
        Assert.Equal(result1.SanitizedText, result2.SanitizedText);
    }

    // ---- D-05: stableId comment AND visualId transition on the same state ----

    [Fact]
    public void StableId_And_VisualId_SameState_NeitherConfused()
    {
        // State Patrol has both a Layout comment (hoisted above builder.State) and
        // an outgoing transition with a visualId. Verify neither injection is confused
        // with the other and no comment is duplicated.
        const string input = @"// HROT_EDITOR_GENERATED
// AssetId: f7050000-0000-0000-0000-000000000001

using System;
using System.Numerics;
using Hrot.Editor.AiShared.Layout;

namespace Test;

public static class PatrolSM
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder(""PatrolSM"");

        var patrol = builder.State(""Patrol"", stableId: new Guid(""aa050000-0000-0000-0000-000000000001""));
        patrol.On(""EnemySpotted"").GoTo(""Chase"", visualId: new Guid(""cc050000-0000-0000-0000-000000000001""));

        builder.State(""Chase"", stableId: new Guid(""bb050000-0000-0000-0000-000000000001""));

        return builder;
    }

    [HsmDefinition(""PatrolSM"", AssetId = ""f7050000-0000-0000-0000-000000000001"")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout(""f7050000-0000-0000-0000-000000000001"")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .State(""aa050000-0000-0000-0000-000000000001"", new Vector2(100f, 100f),
               comment: ""patrol area"")
        .State(""bb050000-0000-0000-0000-000000000001"", new Vector2(300f, 100f))
        .Transition(""cc050000-0000-0000-0000-000000000001"",
                    new Vector2[] { new Vector2(200f, 100f) },
                    comment: ""enemy spotted"")
        .Build();
}
";
        string path = WriteTemp(input);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(path, null, AssetKind.Hsm);
            var result1   = sanitizer.Sanitize(request);
            string text   = result1.SanitizedText;

            // State comment for Patrol must appear exactly once (not duplicated).
            Assert.Equal(1, CountOccurrences(text, "// patrol area"));

            // The GoTo("Chase"...) transition call must appear exactly once.
            Assert.Equal(1, CountOccurrences(text, ".GoTo(\"Chase\""));

            // Determinism: second run produces identical output.
            var result2 = sanitizer.Sanitize(request);
            Assert.Equal(text, result2.SanitizedText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- D-07: 3-level nested Child, all levels extracted -------------------

    [Fact]
    public void ThreeLevelNestedChild_AllLevelsExtracted()
    {
        // Three levels of Child nesting: StateA > StateB > StateC > StateD.
        // Verifies the brace-depth scanner handles 3 levels without confusion.
        const string input = @"// HROT_EDITOR_GENERATED
// AssetId: f8070000-0000-0000-0000-000000000001

using System;
using System.Numerics;
using Hrot.Editor.AiShared.Layout;

namespace Test;

public static class NestedSM
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder(""NestedSM"");

        var stateA = builder.State(""StateA"", stableId: new Guid(""aa070000-0000-0000-0000-000000000001""));
        stateA.Child(""StateB"", sb2 =>
        {
            sb2.Initial();
            sb2.Child(""StateC"", sb3 =>
            {
                sb3.Initial();
                sb3.Child(""StateD"", sb4 =>
                {
                    sb4.Initial();
                }, stableId: new Guid(""dd070000-0000-0000-0000-000000000001""));
            }, stableId: new Guid(""cc070000-0000-0000-0000-000000000001""));
        }, stableId: new Guid(""bb070000-0000-0000-0000-000000000001""));

        return builder;
    }

    [HsmDefinition(""NestedSM"", AssetId = ""f8070000-0000-0000-0000-000000000001"")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout(""f8070000-0000-0000-0000-000000000001"")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .State(""aa070000-0000-0000-0000-000000000001"", new Vector2(100f, 100f),
               comment: ""level A"")
        .State(""bb070000-0000-0000-0000-000000000001"", new Vector2(200f, 100f),
               comment: ""level B"")
        .State(""cc070000-0000-0000-0000-000000000001"", new Vector2(300f, 100f),
               comment: ""level C"")
        .State(""dd070000-0000-0000-0000-000000000001"", new Vector2(400f, 100f))
        .Build();
}
";
        string path = WriteTemp(input);
        try
        {
            var sanitizer = MakeSanitizer();
            var request   = new AssetExportRequest(path, null, AssetKind.Hsm);
            var result1   = sanitizer.Sanitize(request);
            string text   = result1.SanitizedText;

            // All four state names must appear in the sanitized output.
            Assert.Contains("\"StateA\"", text);
            Assert.Contains("\"StateB\"", text);
            Assert.Contains("\"StateC\"", text);
            Assert.Contains("\"StateD\"", text);

            // Determinism: second run produces identical output.
            var result2 = sanitizer.Sanitize(request);
            Assert.Equal(text, result2.SanitizedText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Helper ----

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
