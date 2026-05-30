using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for BPF-016: Event method signature must not include float deltaTime.
/// The event thunk must pass default(T) for each input instead of deltaTime.
/// </summary>
public sealed class BPF016_EventMethodEmitTests
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

    private static string EmitInstanceWithEventGraph()
    {
        var asset = BlueprintAssetBuilder
            .Instance("EventInstance")
            .WithEventGraph("OnHit", g => g.Entry().Return())
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
    /// BPF-016: The Event_ method signature must not include a 'float deltaTime' parameter.
    /// Design §14.1 mandates the signature ends with 'float time' (no deltaTime).
    /// Note: Tick and TickThunk legitimately have deltaTime; we only check Event_OnHit impl.
    /// </summary>
    [Fact]
    public void EventMethod_SignatureDoesNotContain_DeltaTime()
    {
        var src = EmitInstanceWithEventGraph();
        var lines = src.Split('\n');

        // Find the Event_OnHit implementation method (not the _Thunk overload).
        int startIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("public static void Event_OnHit(") && !lines[i].Contains("_Thunk("))
            {
                startIdx = i;
                break;
            }
        }
        Assert.True(startIdx >= 0, "Event_OnHit method declaration not found in emitted source.");

        // Collect lines from the declaration until the opening '{' (parameter list).
        var sigLines = new List<string>();
        for (int i = startIdx; i < lines.Length; i++)
        {
            sigLines.Add(lines[i]);
            if (lines[i].TrimStart().StartsWith("{")) break;
        }

        var badLines = sigLines.Where(l => l.Contains("float deltaTime")).ToList();
        Assert.True(badLines.Count == 0,
            $"'float deltaTime' must not appear in Event_OnHit parameter list. " +
            $"Found: {string.Join(", ", badLines.Select(l => l.Trim()))}");
    }

    /// <summary>
    /// BPF-016: The thunk body must call Event_ without deltaTime (and with default() for inputs
    /// when there are none).  Verify the thunk body contains an Event_OnHit call.
    /// </summary>
    [Fact]
    public void EventThunk_CallsEventMethod_WithoutDeltaTime()
    {
        var src = EmitInstanceWithEventGraph();

        // The thunk should contain a call to Event_OnHit.
        var callLines = src.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//") && l.Contains("Event_OnHit("))
            .ToList();

        Assert.True(callLines.Count > 0, "Expected at least one non-comment Event_OnHit( call in thunk.");

        foreach (var line in callLines)
            Assert.DoesNotContain("deltaTime", line);
    }
}
