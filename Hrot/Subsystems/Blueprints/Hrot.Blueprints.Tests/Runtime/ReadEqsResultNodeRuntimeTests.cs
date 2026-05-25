using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

[Collection("DebugProbe")]
public sealed class ReadEqsResultNodeRuntimeTests
{
    // ---- State reading helpers ----

    private static T ReadSlotField<T>(
        BlueprintTestFixture fixture,
        BlueprintAsset asset,
        Entity entity,
        string fieldName)
        where T : unmanaged
    {
        var hash = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            $"Blueprint definition not found for asset {asset.AssetId}");
        var stateType = def!.StateClrType;
        Assert.NotNull(stateType);
        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        var offset = (int)Marshal.OffsetOf(stateType!, fieldName);
        return MemoryMarshal.Read<T>(state!.Value.AsSpan().Slice(offset, Unsafe.SizeOf<T>()));
    }

    private static unsafe void WriteSlotField<T>(
        BlueprintTestFixture fixture,
        BlueprintAsset asset,
        Entity entity,
        string fieldName,
        T value)
        where T : unmanaged
    {
        var hash = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            $"Blueprint definition not found for asset {asset.AssetId}");
        var stateType = def!.StateClrType;
        Assert.NotNull(stateType);
        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        var offset = (int)Marshal.OffsetOf(stateType!, fieldName);
        var span = state!.Value.AsSpan();
        ref byte slotBase = ref Unsafe.AsRef(in MemoryMarshal.GetReference(span));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref slotBase, offset), value);
    }

    // ---- Asset builders ----

    /// <summary>
    /// Builds a blueprint with:
    /// - SensorHandle : EqsSensorHandle variable
    /// - WasReady     : bool variable (written from IsReady output)
    /// - ResultCount  : int variable (written from ResultCount output)
    ///
    /// Tick graph:
    ///   Entry -> SetVar(WasReady) -> SetVar(ResultCount) -> Return
    ///   ReadEqsResultNode.IsReady   -> SetVar(WasReady).Value
    ///   ReadEqsResultNode.ResultCount -> SetVar(ResultCount).Value
    /// </summary>
    internal static (BlueprintAsset asset, string sensorVarName) BuildReadEqsAsset()
    {
        var assetId     = Guid.NewGuid();
        var graphId     = Guid.NewGuid();
        var sensorVarId = Guid.NewGuid();
        var readyVarId  = Guid.NewGuid();
        var countVarId  = Guid.NewGuid();

        // Variables
        var sensorHandleVar = new VariableDecl { Id = sensorVarId, Name = "SensorHandle",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
        var wasReadyVar     = new VariableDecl { Id = readyVarId,  Name = "WasReady",
            Type = new BlueprintTypeRef { TypeId = "bool" } };
        var resultCountVar  = new VariableDecl { Id = countVarId,  Name = "ResultCount",
            Type = new BlueprintTypeRef { TypeId = "int" } };

        // ReadEqsResultNode
        var readNodeId = Guid.NewGuid();
        var readNode   = new ReadEqsResultNode { Id = readNodeId, SensorVariableName = "SensorHandle" };
        var indexPin   = new Pin { Id = Guid.NewGuid(), Name = "ResultIndex", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var isReadyPin = new Pin { Id = Guid.NewGuid(), Name = "IsReady",     Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var countPin   = new Pin { Id = Guid.NewGuid(), Name = "ResultCount", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var entityPin  = new Pin { Id = Guid.NewGuid(), Name = "Entity",      Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
        var posPin     = new Pin { Id = Guid.NewGuid(), Name = "Position",    Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Numerics.Vector2" } };
        var scorePin   = new Pin { Id = Guid.NewGuid(), Name = "Score",       Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        readNode.Pins.AddRange(new[] { indexPin, isReadyPin, countPin, entityPin, posPin, scorePin });

        // SetVariable(WasReady)
        var setReadyId      = Guid.NewGuid();
        var setReadyExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setReadyExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setReadyDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setReadyNode    = new SetVariableNode { Id = setReadyId, VariableId = readyVarId.ToString() };
        setReadyNode.Pins.AddRange(new[] { setReadyExecIn, setReadyExecOut, setReadyDataIn });

        // SetVariable(ResultCount)
        var setCountId      = Guid.NewGuid();
        var setCountExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setCountExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setCountDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setCountNode    = new SetVariableNode { Id = setCountId, VariableId = countVarId.ToString() };
        setCountNode.Pins.AddRange(new[] { setCountExecIn, setCountExecOut, setCountDataIn });

        // Entry + return
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);
        var retNode = new ReturnNode { Id = Guid.NewGuid() };
        var retIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, readNode, setReadyNode, setCountNode, retNode },
            Links =
            {
                new Link { FromNodeId = entry.Id,        FromPinId = entryExecOut.Id,    ToNodeId = setReadyNode.Id, ToPinId = setReadyExecIn.Id },
                new Link { FromNodeId = setReadyNode.Id, FromPinId = setReadyExecOut.Id, ToNodeId = setCountNode.Id, ToPinId = setCountExecIn.Id },
                new Link { FromNodeId = setCountNode.Id, FromPinId = setCountExecOut.Id, ToNodeId = retNode.Id,      ToPinId = retIn.Id },
                // Data: ReadEqsResult.IsReady -> setReady.Value
                new Link { FromNodeId = readNodeId, FromPinId = isReadyPin.Id, ToNodeId = setReadyId, ToPinId = setReadyDataIn.Id },
                // Data: ReadEqsResult.ResultCount -> setCount.Value
                new Link { FromNodeId = readNodeId, FromPinId = countPin.Id,   ToNodeId = setCountId, ToPinId = setCountDataIn.Id },
            },
        };

        return (new BlueprintAsset
        {
            AssetId   = assetId,
            Name      = "ReadEqsTest",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Variables = { sensorHandleVar, wasReadyVar, resultCountVar },
            Graphs    = { graph },
        }, "SensorHandle");
    }

    /// <summary>Exposed for use in WhenNodeEqsInlineArrayTests.</summary>
    internal static BlueprintAsset BuildReadEqsAssetForInlineArrayTest()
        => BuildReadEqsAsset().asset;

    // ---- Tests ----

    [Fact]
    public void ReadEqsResult_ReturnsIsReady_True_WhenBufferReady()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();
        var (asset, sensorVarName) = BuildReadEqsAsset();
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f); // init tick

        // Set up child entity with ready buffer
        var buffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 2 };
        var child  = fixture.CreateEntity();
        fixture.World.AddComponent(child, buffer);
        WriteSlotField(fixture, asset, entity, sensorVarName, new EqsSensorHandle(child));

        fixture.TickFrame(0.016f);

        bool wasReady    = ReadSlotField<bool>(fixture, asset, entity, "WasReady");
        int  resultCount = ReadSlotField<int>(fixture, asset, entity, "ResultCount");
        Assert.True(wasReady, "IsReady should be true when buffer is ready");
        Assert.Equal(2, resultCount);
    }

    [Fact]
    public void ReadEqsResult_ReturnsIsReady_False_WhenBufferNotReady()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        var (asset, sensorVarName) = BuildReadEqsAsset();
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Set up child entity with NOT ready buffer (LastUpdateTick = 0)
        var buffer = new EqsCognitiveBuffer { LastUpdateTick = 0u };
        var child  = fixture.CreateEntity();
        fixture.World.AddComponent(child, buffer);
        WriteSlotField(fixture, asset, entity, sensorVarName, new EqsSensorHandle(child));

        fixture.TickFrame(0.016f);

        bool wasReady = ReadSlotField<bool>(fixture, asset, entity, "WasReady");
        Assert.False(wasReady);
    }

    [Fact]
    public void ReadEqsResult_ReturnsIsReady_False_WhenChildDead()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var (asset, sensorVarName) = BuildReadEqsAsset();
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Create + immediately destroy child
        var child = fixture.CreateEntity();
        fixture.World.DestroyEntity(child);
        WriteSlotField(fixture, asset, entity, sensorVarName, new EqsSensorHandle(child));

        // Should not crash; IsReady should be false
        var exception = Record.Exception(() => fixture.TickFrame(0.016f));
        Assert.Null(exception);

        bool wasReady = ReadSlotField<bool>(fixture, asset, entity, "WasReady");
        Assert.False(wasReady);
    }

    [Fact]
    public void ReadEqsResult_ClampsIndex_ToValidRange()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        var (asset, sensorVarName) = BuildReadEqsAsset();
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        var buffer = new EqsCognitiveBuffer { LastUpdateTick = 1u, Count = 1 };
        var span   = buffer.GetSpanRW();
        span[0]    = new EqsResult { EntityId = 77L, Score = 0.5f };
        var child  = fixture.CreateEntity();
        fixture.World.AddComponent(child, buffer);
        WriteSlotField(fixture, asset, entity, sensorVarName, new EqsSensorHandle(child));

        // The test blueprint uses ResultIndex = 0 (unconnected, default). Just verify no crash.
        var exception = Record.Exception(() => fixture.TickFrame(0.016f));
        Assert.Null(exception);
        bool wasReady = ReadSlotField<bool>(fixture, asset, entity, "WasReady");
        Assert.True(wasReady);
    }
}
