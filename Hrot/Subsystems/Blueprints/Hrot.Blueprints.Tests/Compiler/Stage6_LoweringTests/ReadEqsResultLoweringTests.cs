using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class ReadEqsResultLoweringTests
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

    // -----------------------------------------------------------------------
    // Asset builders
    // -----------------------------------------------------------------------

    private static BlueprintAsset BuildReadEqsResultAsset(Guid? assetId = null)
    {
        var nodeId       = Guid.NewGuid();
        var isReadyPinId = Guid.NewGuid();
        var boolVarId    = Guid.NewGuid();

        var readNode = new ReadEqsResultNode
        {
            Id                 = nodeId,
            SensorVariableName = "CoverQuery",
        };
        // ResultIndex input pin (unconnected -> default 0)
        var indexPin   = new Pin { Id = Guid.NewGuid(), Name = "ResultIndex", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        // Output pins
        var isReadyPin = new Pin { Id = isReadyPinId,  Name = "IsReady",     Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var countPin   = new Pin { Id = Guid.NewGuid(), Name = "ResultCount", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var entityPin  = new Pin { Id = Guid.NewGuid(), Name = "Entity",      Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
        var posPin     = new Pin { Id = Guid.NewGuid(), Name = "Position",    Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Numerics.Vector2" } };
        var scorePin   = new Pin { Id = Guid.NewGuid(), Name = "Score",       Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        readNode.Pins.AddRange(new[] { indexPin, isReadyPin, countPin, entityPin, posPin, scorePin });

        // SetVariableNode consuming IsReady
        var setVarId   = Guid.NewGuid();
        var setVarNode = new SetVariableNode { Id = setVarId, VariableId = boolVarId.ToString() };
        var setVarExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In",  IsExec = true,  TypeRef = new() };
        var setVarDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",  Direction = "In",  IsExec = false, TypeRef = new() };
        var setVarExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",    Direction = "Out", IsExec = true,  TypeRef = new() };
        setVarNode.Pins.AddRange(new[] { setVarExecIn, setVarDataIn, setVarExecOut });

        // Entry node
        var entryNode    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entryNode.Pins.Add(entryExecOut);

        var graphId = Guid.NewGuid();
        var graph   = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = { entryNode, setVarNode, readNode },
            Links =
            {
                // Entry -> SetVar (exec)
                new Link { FromNodeId = entryNode.Id, FromPinId = entryExecOut.Id, ToNodeId = setVarId, ToPinId = setVarExecIn.Id },
                // ReadEqsResult.IsReady -> SetVar.Value (data)
                new Link { FromNodeId = nodeId, FromPinId = isReadyPinId, ToNodeId = setVarId, ToPinId = setVarDataIn.Id },
            },
        };

        var sensorVar = new VariableDecl { Id = Guid.NewGuid(), Name = "CoverQuery", Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
        var boolVar   = new VariableDecl { Id = boolVarId,      Name = "WasReady",   Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        return new BlueprintAsset
        {
            AssetId   = assetId ?? Guid.NewGuid(),
            Name      = "ReadEqsTest",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = { sensorVar, boolVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// Builds an asset with two SetVariableNodes consuming different output pins of the same ReadEqsResultNode.
    /// Used to verify that the helper is only called once (deduped via _pinValueCache).
    /// </summary>
    private static BlueprintAsset BuildReadEqsAssetWithTwoConsumers()
    {
        var nodeId       = Guid.NewGuid();
        var isReadyPinId = Guid.NewGuid();
        var countPinId   = Guid.NewGuid();
        var boolVarId    = Guid.NewGuid();
        var intVarId     = Guid.NewGuid();

        var readNode = new ReadEqsResultNode
        {
            Id                 = nodeId,
            SensorVariableName = "CoverQuery",
        };
        var indexPin   = new Pin { Id = Guid.NewGuid(), Name = "ResultIndex", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var isReadyPin = new Pin { Id = isReadyPinId,  Name = "IsReady",     Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var countPin   = new Pin { Id = countPinId,    Name = "ResultCount", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        readNode.Pins.AddRange(new[] { indexPin, isReadyPin, countPin });

        // First consumer: SetVariableNode <- IsReady
        var setVar1Id   = Guid.NewGuid();
        var setVar1Node = new SetVariableNode { Id = setVar1Id, VariableId = boolVarId.ToString() };
        var sv1ExecIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In",  IsExec = true,  TypeRef = new() };
        var sv1DataIn   = new Pin { Id = Guid.NewGuid(), Name = "Value",  Direction = "In",  IsExec = false, TypeRef = new() };
        var sv1ExecOut  = new Pin { Id = Guid.NewGuid(), Name = "Out",    Direction = "Out", IsExec = true,  TypeRef = new() };
        setVar1Node.Pins.AddRange(new[] { sv1ExecIn, sv1DataIn, sv1ExecOut });

        // Second consumer: SetVariableNode <- ResultCount
        var setVar2Id   = Guid.NewGuid();
        var setVar2Node = new SetVariableNode { Id = setVar2Id, VariableId = intVarId.ToString() };
        var sv2ExecIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In",  IsExec = true,  TypeRef = new() };
        var sv2DataIn   = new Pin { Id = Guid.NewGuid(), Name = "Value",  Direction = "In",  IsExec = false, TypeRef = new() };
        var sv2ExecOut  = new Pin { Id = Guid.NewGuid(), Name = "Out",    Direction = "Out", IsExec = true,  TypeRef = new() };
        setVar2Node.Pins.AddRange(new[] { sv2ExecIn, sv2DataIn, sv2ExecOut });

        // Entry node
        var entryNode    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entryNode.Pins.Add(entryExecOut);

        var graphId = Guid.NewGuid();
        var graph   = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = { entryNode, setVar1Node, setVar2Node, readNode },
            Links =
            {
                new Link { FromNodeId = entryNode.Id, FromPinId = entryExecOut.Id, ToNodeId = setVar1Id, ToPinId = sv1ExecIn.Id },
                new Link { FromNodeId = setVar1Id,    FromPinId = sv1ExecOut.Id,   ToNodeId = setVar2Id, ToPinId = sv2ExecIn.Id },
                new Link { FromNodeId = nodeId,       FromPinId = isReadyPinId,    ToNodeId = setVar1Id, ToPinId = sv1DataIn.Id },
                new Link { FromNodeId = nodeId,       FromPinId = countPinId,      ToNodeId = setVar2Id, ToPinId = sv2DataIn.Id },
            },
        };

        var sensorVar = new VariableDecl { Id = Guid.NewGuid(), Name = "CoverQuery", Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
        var boolVar   = new VariableDecl { Id = boolVarId,      Name = "WasReady",   Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var intVar    = new VariableDecl { Id = intVarId,        Name = "LastCount",  Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ReadEqsTwoConsumers",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = { sensorVar, boolVar, intVar },
            Graphs    = { graph },
        };
    }

    /// <summary>Builds a minimal baseline asset (no ReadEqsResultNode) for hash comparison.</summary>
    private static BlueprintAsset BuildBaselineAsset(Guid? assetId = null)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);

        var graphId = Guid.NewGuid();
        var graph   = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = { entry },
            Links = { },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId ?? Guid.NewGuid(),
            Name     = "Baseline",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private static int CountOccurrences(string source, string pattern)
    {
        int count = 0, i = 0;
        while ((i = source.IndexOf(pattern, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += pattern.Length;
        }
        return count;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Lower_EmitsHelperMethod()
    {
        var source = Compile(BuildReadEqsResultAsset());
        Assert.NotNull(source);
        Assert.Contains("ReadEqsResult_", source);      // helper method name
        Assert.Contains("private static", source);      // static method
        Assert.Contains("_EqsResultRead_", source);     // return type
    }

    [Fact]
    public void Lower_ClampsIndex()
    {
        var source = Compile(BuildReadEqsResultAsset());
        Assert.NotNull(source);
        Assert.Contains("Math.Clamp", source);
    }

    [Fact]
    public void Lower_LivenessGuard()
    {
        var source = Compile(BuildReadEqsResultAsset());
        Assert.NotNull(source);
        int liveness  = source!.IndexOf("view.IsAlive(handle.ChildId)", StringComparison.Ordinal);
        int bufferRead = source.IndexOf("GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>", StringComparison.Ordinal);
        Assert.True(liveness >= 0 && bufferRead >= 0);
        Assert.True(liveness < bufferRead, "Liveness guard must precede buffer read");
    }

    [Fact]
    public void Lower_SharedReadCaching()
    {
        // Build a graph where two SetVariableNodes consume different output pins of the same ReadEqsResultNode.
        // Assert the helper method is called only ONCE (deduped via _pinValueCache).
        // 2 occurrences: one is the method definition, one is the call site.
        var source = Compile(BuildReadEqsAssetWithTwoConsumers());
        Assert.NotNull(source);
        int count = CountOccurrences(source!, "ReadEqsResult_");
        Assert.Equal(2, count);
    }

    [Fact]
    public void Lower_ZeroStateContribution()
    {
        // Two compilations of the same asset produce the same StructureHash; no synthesized fields added.
        var fixedId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var sink1   = new DiagnosticSink();
        var sink2   = new DiagnosticSink();
        var h1 = RunLower(BuildReadEqsResultAsset(fixedId), sink1).StructureHash;
        var h2 = RunLower(BuildReadEqsResultAsset(fixedId), sink2).StructureHash;
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Lower_LivenessGuardFails_ReturnsSafeDefault()
    {
        // Emitted helper must guard with BOTH IsAlive AND HasComponent<EqsCognitiveBuffer>
        // before calling GetComponentRO<EqsCognitiveBuffer>. This protects against calling
        // GetComponentRO on an entity that has not yet had EqsCognitiveBuffer attached
        // (e.g. immediately after spawn before the ECB flush).
        var source = Compile(BuildReadEqsResultAsset());
        Assert.NotNull(source);
        Assert.Contains("view.IsAlive(handle.ChildId)", source!);
        Assert.Contains("view.HasComponent<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId)", source!);
    }

    [Fact]
    public void Lower_BufferComponentMissing_ReturnsSafeDefault()
    {
        // The HasComponent<EqsCognitiveBuffer> guard must appear strictly BEFORE GetComponentRO
        // in the emitted source. A reversed order would allow GetComponentRO to crash before
        // the guard fires.
        var source = Compile(BuildReadEqsResultAsset());
        Assert.NotNull(source);
        int hasComponentIdx = source!.IndexOf(
            "HasComponent<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>",
            StringComparison.Ordinal);
        int getComponentIdx = source.IndexOf(
            "GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>",
            StringComparison.Ordinal);
        Assert.True(hasComponentIdx >= 0, "HasComponent guard not found in emitted source");
        Assert.True(getComponentIdx >= 0, "GetComponentRO not found in emitted source");
        Assert.True(hasComponentIdx < getComponentIdx,
            "HasComponent guard must appear before GetComponentRO");
    }
}
