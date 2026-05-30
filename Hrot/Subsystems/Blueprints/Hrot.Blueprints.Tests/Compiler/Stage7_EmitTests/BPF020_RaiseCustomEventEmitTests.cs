using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for BPF-020: IrOp_RaiseCustomEvent must emit a real C# dispatch call,
/// not a commented-out placeholder.
/// </summary>
public sealed class BPF020_RaiseCustomEventEmitTests
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

    private static string EmitInstanceWithCustomEventCall()
    {
        var asset = BlueprintAssetBuilder
            .Instance("CustomEventInst")
            .WithCustomEvent("OnFire")
            .WithGraph("Tick", g => g.Entry().CallCustomEvent("OnFire").Return())
            .WithEventGraph("OnFire", g => g.Entry().Return())
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
    /// BPF-020: The custom event call must appear as a real statement on a non-comment line.
    /// The old bug emitted: // RaiseCustomEvent: OnFire()
    /// The fix emits:       Event_OnFire(ref s, view, ecb, self, time);
    /// </summary>
    [Fact]
    public void RaiseCustomEvent_EmitsRealCall_NotComment()
    {
        var src = EmitInstanceWithCustomEventCall();

        // Find non-comment lines containing Event_OnFire(.
        var callLines = src.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//") && l.Contains("Event_OnFire("))
            .ToList();

        Assert.True(callLines.Count > 0,
            "Expected a non-comment Event_OnFire( call in emitted source.");
    }

    /// <summary>
    /// BPF-020: The old buggy comment form must not appear.
    /// </summary>
    [Fact]
    public void RaiseCustomEvent_NoLegacyCommentForm()
    {
        var src = EmitInstanceWithCustomEventCall();

        Assert.DoesNotContain("// RaiseCustomEvent:", src);
    }

    /// <summary>
    /// BPF-020: The emitted call must end with a semicolon (valid C# statement).
    /// </summary>
    [Fact]
    public void RaiseCustomEvent_CallLineEndsWithSemicolon()
    {
        var src = EmitInstanceWithCustomEventCall();

        // Match call sites only: exclude declaration lines ("public static void Event_OnFire(")
        // and thunk lines ("Event_OnFire_Thunk(").
        var callLines = src.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//")
                     && l.Contains("Event_OnFire(")
                     && !l.Contains("void Event_OnFire(")
                     && !l.Contains("_Thunk("))
            .ToList();

        Assert.True(callLines.Count > 0, "No Event_OnFire call site found.");

        foreach (var line in callLines)
            Assert.True(line.TrimEnd().EndsWith(";"),
                $"Event_OnFire call does not end with ';': {line.Trim()}");
    }
}
