using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for BPF-015: DebugProbe.NodeEnter / PinValueChanged must be emitted as
/// real C# method calls, not as comment strings.
/// </summary>
public sealed class BPF015_DebugProbeEmitTests
{
    private static string EmitAiPrimitiveInDebugMode()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ProbeAction")
            .WithHostings(Hrot.Blueprints.Core.Assets.AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Delay(0.5f).Return())
            .Build();

        var opts = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        Stage2_Validate.Run(asset, ctx);
        var norm    = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(norm, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        // Stage6 inserts DebugProbe ops in non-Release mode.
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(lowered, CompilerMode.Debug, sink);

        if (sink.HasErrors)
            throw new InvalidOperationException(
                $"Emit errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");
        return src;
    }

    /// <summary>
    /// BPF-015: DebugProbe.NodeEnter must appear as a real call (not inside a // comment)
    /// in the emitted code when compiling in Debug mode.
    /// </summary>
    [Fact]
    public void DebugProbeNodeEnter_EmittedAsRealCall_NotComment()
    {
        var src = EmitAiPrimitiveInDebugMode();

        // Find every non-comment line that references DebugProbe.NodeEnter.
        var callLines = src.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//") && l.Contains("DebugProbe.NodeEnter("))
            .ToList();

        Assert.True(callLines.Count > 0,
            "Expected at least one non-comment line with DebugProbe.NodeEnter(...)");
    }

    /// <summary>
    /// BPF-015: The emitted NodeEnter call must be a syntactically valid statement --
    /// it must end with a semicolon (after trimming whitespace).
    /// </summary>
    [Fact]
    public void DebugProbeNodeEnter_EmittedLineEndsWithSemicolon()
    {
        var src = EmitAiPrimitiveInDebugMode();

        var callLines = src.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//") && l.Contains("DebugProbe.NodeEnter("))
            .ToList();

        Assert.True(callLines.Count > 0, "No non-comment NodeEnter call found in emitted code.");

        foreach (var line in callLines)
            Assert.True(line.TrimEnd().EndsWith(";"),
                $"DebugProbe.NodeEnter call line does not end with ';': {line.Trim()}");
    }

    /// <summary>
    /// BPF-015: The emitted source must NOT contain the old comment-only format
    /// '// [DebugProbe] NodeEnter' on any line -- that was the buggy form.
    /// </summary>
    [Fact]
    public void DebugProbeNodeEnter_NoLegacyCommentForm()
    {
        var src = EmitAiPrimitiveInDebugMode();

        Assert.DoesNotContain("// [DebugProbe] NodeEnter", src);
    }
}
