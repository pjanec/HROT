using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class WhenNodeEqsLoweringTests
{
    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>Runs Stage 5 then Stage 6; returns the lowered IrAsset.</summary>
    private static IrAsset RunLower(BlueprintAsset asset, DiagnosticSink sink)
    {
        var opts  = DefaultOptions();
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ctx = new ValidationContext(sink, opts);
        var ir  = Stage5_Schedule.Run(typed, ctx);
        return Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
    }

    /// <summary>Runs all stages (skipping Stage 2) and returns the generated C# source.</summary>
    private static string? Compile(BlueprintAsset asset)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        asset  = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(asset, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, opts.Mode, sink);
        var (source, _) = Stage7_Emit.Run(lowered, opts.Mode, sink);
        return sink.HasErrors ? null : source;
    }

    /// <summary>
    /// Builds a minimal WhenNode for an EqsResult scenario.
    /// </summary>
    private static WhenNode MakeEqsResultNode(
        Guid nodeId,
        EqsTrigger trigger,
        string sensorVarName,
        WhenEdge edges = WhenEdge.RisingEdge,
        float scoreThreshold = 0f,
        float maxAge = 5f)
    {
        var node = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.EqsResult,
            Edges = edges,
            EqsResult = new EqsResultPayload
            {
                SensorVariableName = sensorVarName,
                Trigger            = trigger,
                ScoreThreshold     = scoreThreshold,
                MaxAgeSeconds      = maxAge,
            },
        };
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        if ((edges & WhenEdge.RisingEdge) != 0)
            node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });
        if ((edges & WhenEdge.FallingEdge) != 0)
            node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnEnded", Direction = "Out", IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
        return node;
    }

    /// <summary>Builds a minimal BlueprintAsset with an EventEntryNode wired to a WhenNode.</summary>
    private static BlueprintAsset BuildAsset(
        WhenNode whenNode,
        string assetName = "EqsWhenTest",
        VariableDecl? sensorVar = null)
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = assetName,
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        if (sensorVar is not null)
            asset.Variables.Add(sensorVar);

        return asset;
    }

    private static VariableDecl MakeSensorVar(string name = "CoverQuery") => new VariableDecl
    {
        Id   = Guid.NewGuid(),
        Name = name,
        Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Lower_EqsResult_UsesChildEntityRead()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.TopChanged, "CoverQuery");
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId)", src);
        Assert.DoesNotContain("GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(self)", src);
    }

    [Fact]
    public void Lower_EqsResult_LivenessGuardPrecedesReads()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.TopChanged, "CoverQuery");
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        int livenessIdx = src!.IndexOf("IsAlive(handle.ChildId)", StringComparison.Ordinal);
        int bufferIdx   = src.IndexOf("GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer", StringComparison.Ordinal);
        Assert.True(livenessIdx >= 0, "Expected IsAlive guard not found");
        Assert.True(bufferIdx   >= 0, "Expected buffer component read not found");
        Assert.True(livenessIdx < bufferIdx, "IsAlive guard must precede buffer component read");
    }

    [Fact]
    public void Lower_EqsResult_TopChanged_UsesGetSpanRO()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.TopChanged, "CoverQuery");
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("buffer.GetSpanRO()", src);
    }

    [Fact]
    public void Lower_EqsResult_TopChanged_EpochGated()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.TopChanged, "CoverQuery");
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("sensor.Epoch != prev.LastEvaluatedEpoch", src);
    }

    [Fact]
    public void Lower_EqsResult_PositionalHash_OnTheFly()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.TopChanged, "CoverQuery");
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("top.EntityId != 0L", src);
        // Positional identity must be DETERMINISTIC across nodes and hot-reloads:
        // System.HashCode.Combine is per-process randomized and must NOT be used. The
        // emitter bit-packs the two float coordinates into the tracking id instead.
        Assert.DoesNotContain("HashCode.Combine", src);
        Assert.Contains("SingleToInt32Bits(top.PositionX)", src);
        Assert.Contains("SingleToInt32Bits(top.PositionY)", src);
    }

    [Fact]
    public void Lower_EqsResult_FirstReady_DistinctStateStruct()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.FirstReady, "CoverQuery");
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        // The struct type name includes "WhenEqsFirstReady"
        Assert.Contains("struct _WhenEqsFirstReady_", src);
        // FirstReady struct has only LastEvaluatedEpoch — not PrevTopId or PrevTopScore
        Assert.Contains("LastEvaluatedEpoch", src);
        Assert.DoesNotContain("PrevTopId", src);
        Assert.DoesNotContain("PrevTopScore", src);
    }

    [Fact]
    public void Lower_EqsResult_ScoreCrossed_EmitsConstThreshold()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.ScoreCrossed, "CoverQuery",
            scoreThreshold: 0.75f);
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("_whenScoreThreshold_", src);
        Assert.Contains("0.75", src);
    }

    [Fact]
    public void Lower_EqsResult_BecomesStale_UsesSimTime()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.BecomesStale, "CoverQuery",
            maxAge: 3.0f);
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("time - buffer.LastUpdateTimeSeconds", src);
        Assert.Contains("_whenMaxAge_", src);
        Assert.Contains("3", src);
    }

    [Fact]
    public void Lower_EqsResult_BecomesStale_NotEpochGated()
    {
        var nodeId  = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.BecomesStale, "CoverQuery",
            maxAge: 3.0f);
        var asset   = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.DoesNotContain("sensor.Epoch", src);
    }

    [Fact]
    public void Lower_StructureHashDiffersWithEqsResult()
    {
        // Asset without WhenNode
        var emptyAssetId = Guid.NewGuid();
        var emptyGraph   = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = { new EventEntryNode { Id = Guid.NewGuid() } },
        };
        // Add exec-out pin to entry
        ((EventEntryNode)emptyGraph.Nodes[0]).Pins
            .Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var emptyAsset = new BlueprintAsset
        {
            AssetId  = emptyAssetId,
            Name     = "Empty",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { emptyGraph },
        };

        // Asset with EqsResult WhenNode
        var nodeId   = Guid.NewGuid();
        var whenNode = MakeEqsResultNode(nodeId, EqsTrigger.TopChanged, "CoverQuery");
        var eqsAsset = BuildAsset(whenNode, sensorVar: MakeSensorVar());

        var sink1 = new DiagnosticSink();
        var sink2 = new DiagnosticSink();
        var loweredEmpty = RunLower(emptyAsset, sink1);
        var loweredEqs   = RunLower(eqsAsset,   sink2);

        Assert.False(sink1.HasErrors);
        Assert.False(sink2.HasErrors);
        Assert.NotEqual(loweredEmpty.StructureHash, loweredEqs.StructureHash);
    }
}
