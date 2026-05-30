using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for BPF-014: Instance LatentDelay resume must read s.Cursor.WaitUntilTime,
/// not ws.__waitUntilTime (the AiPrimitive working-state field).
/// </summary>
public sealed class BPF014_LatentDelayEmitTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static string EmitInstanceWithDelay()
    {
        var asset = BlueprintAssetBuilder
            .Instance("DelayInstance")
            .WithGraph("Tick", g => g.Entry().Delay(1.0f).Return())
            .Build();

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);
        var norm    = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(norm, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(lowered, CompilerMode.Debug, sink);

        if (sink.HasErrors)
            throw new InvalidOperationException(
                $"Emit errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");
        return src;
    }

    /// <summary>
    /// BPF-014: The resume-check block for LatentDelay in an Instance blueprint must
    /// read the cursor field (s.Cursor.WaitUntilTime), not the working-state scratch
    /// field (ws.__waitUntilTime).
    /// A test that checks for "s.Cursor.WaitUntilTime" in string presence alone is
    /// insufficient -- a commented-out reference would still pass.  We check that the
    /// string appears on at least one non-comment source line.
    /// </summary>
    [Fact]
    public void LatentDelay_Instance_EmitsCursorWaitUntilTime_NotInComment()
    {
        var src = EmitInstanceWithDelay();

        // At least one non-comment line must reference s.Cursor.WaitUntilTime.
        var nonCommentLines = src.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//"))
            .ToList();

        Assert.Contains(nonCommentLines, l => l.Contains("s.Cursor.WaitUntilTime"));
    }

    /// <summary>
    /// BPF-014: The emitted code must NOT reference the working-state time field on
    /// any non-comment line.  Presence of that field indicates the bug is still present.
    /// </summary>
    [Fact]
    public void LatentDelay_Instance_DoesNotEmitWorkingStateWaitUntilTime()
    {
        var src = EmitInstanceWithDelay();

        var nonCommentLines = src.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//"))
            .ToList();

        Assert.DoesNotContain(nonCommentLines, l => l.Contains("ws.__waitUntilTime"));
    }
}
