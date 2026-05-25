using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

[Collection("DebugProbe")]
public sealed class WhenNodeEqsInlineArrayTests
{
    private static string? CompileToSource(BlueprintAsset asset, CompileOptions? options = null)
    {
        var opts = options ?? new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        var sink    = new DiagnosticSink();
        var ctx     = new ValidationContext(sink, opts);
        var norm    = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(norm, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, opts.Mode, sink);
        var (source, _) = Stage7_Emit.Run(lowered, opts.Mode, sink);
        return sink.HasErrors ? null : source;
    }

    // ---- Minimal asset builders (duplicated from lowering tests) ----

    private static BlueprintAsset BuildEqsWhenAsset(EqsTrigger trigger)
    {
        var nodeId       = Guid.NewGuid();
        var sensorVarId  = Guid.NewGuid();
        const string sensorVarName = "SensorHandle";

        var whenNode = new WhenNode
        {
            Id        = nodeId,
            Mode      = WhenMode.EqsResult,
            Edges     = WhenEdge.RisingEdge,
            EqsResult = new EqsResultPayload
            {
                SensorVariableName = sensorVarName,
                Trigger            = trigger,
                ScoreThreshold     = 0f,
                MaxAgeSeconds      = 5f,
            },
        };
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var sensorVar = new VariableDecl
        {
            Id   = sensorVarId,
            Name = sensorVarName,
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };

        var graph = new Graph
        {
            Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = entryExecOut.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "EqsWhenInlineTest",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Variables = { sensorVar },
            Graphs    = { graph },
        };
    }

    // ---- Tests ----

    [Fact]
    public void EqsTopChanged_Generated_UsesGetSpanRO()
    {
        var asset  = BuildEqsWhenAsset(EqsTrigger.TopChanged);
        var source = CompileToSource(asset);
        Assert.NotNull(source);
        Assert.Contains("GetSpanRO()", source!);
    }

    [Fact]
    public void EqsTopChanged_Generated_DoesNotUseDirectResultsIndex()
    {
        var asset  = BuildEqsWhenAsset(EqsTrigger.TopChanged);
        var source = CompileToSource(asset);
        Assert.NotNull(source);
        // Direct indexer ".Results[" would cause defensive copy issues.
        // Generated code must not contain this pattern (it must use GetSpanRO() instead).
        Assert.DoesNotContain(".Results[", source!);
    }

    [Fact]
    public void ReadEqsResult_Generated_UsesGetSpanRO()
    {
        var asset  = ReadEqsResultNodeRuntimeTests.BuildReadEqsAssetForInlineArrayTest();
        var source = CompileToSource(asset);
        Assert.NotNull(source);
        Assert.Contains("GetSpanRO()", source!);
    }

    [Fact]
    public void ReadEqsResult_Generated_ClampsIndex()
    {
        var asset  = ReadEqsResultNodeRuntimeTests.BuildReadEqsAssetForInlineArrayTest();
        var source = CompileToSource(asset);
        Assert.NotNull(source);
        Assert.Contains("Math.Clamp", source!);
    }
}
