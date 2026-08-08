using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Xunit;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using ParameterDecl         = Hrot.Blueprints.Core.Assets.ParameterDecl;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>CallPeerBlueprint's generated cross-asset call must actually resolve through Roslyn.</b>
/// <para>
/// <c>StatementEmitter</c>'s <c>IrOp_PeerCall</c> case (Compiler/Emit/StatementEmitter.cs:345-355)
/// emits <c>__Peer_{hash:X8}_Bp.MethodName(...)</c> as the call target, but every emitter that
/// actually DECLARES a blueprint class names it <c>{SanitizedName}_{BlueprintId:X8}_Bp</c>
/// (<c>LibraryEmitter.cs:9</c>, <c>InstanceEmitter.cs:9</c>, <c>AiPrimitiveEmitter.cs:10</c>,
/// <c>CSharpEmitter.cs</c>'s registrar). Nothing aliased the bare <c>__Peer_</c> name to that real
/// class -- see <c>NodeCoverageTests.BuildCallPeerBlueprintMinimalAsset</c>'s doc comment, which is
/// the reason that fixture stops at <see cref="CoverageMode.ValidateOnlyStage1To7"/> instead of a
/// real Roslyn compile.
/// </para>
/// <para>
/// This test drives a caller + peer through <see cref="BlueprintTestFixture.CompileAndLoadMany"/> --
/// the overload that merges BOTH generated sources into ONE compilation, exactly like production's
/// Hrot.AI.Behaviors build compiles all sibling blueprints together -- so it is the closest an
/// isolated unit test gets to the real cross-asset scenario. Before the alias fix, Roslyn cannot
/// resolve <c>__Peer_{hash:X8}_Bp</c> even though the peer's REAL class IS present in the same
/// merged compilation, proving the bug is the missing name, not a missing sibling.
/// </para>
/// </summary>
public sealed class CallPeerBlueprintRoslynTests
{
    private static CompileOptions MakeOptions(BlueprintSignature[] siblings) => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: siblings);

    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    private static ParameterDecl Decl(string name, string typeId) => new()
    {
        Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = typeId },
    };

    /// <summary>
    /// Library asset exporting one Function graph "GetAnswer" -- EventEntry -&gt; Return(42), with a
    /// single declared System.Int32 output ("Result"). Mirrors
    /// <c>BP73_MultipleFunctionOutputsTests.MakeMultiOutputFunction</c>'s shape exactly (Return pin
    /// order pairs positionally with <c>Graph.Outputs</c>).
    /// </summary>
    private static BlueprintAsset BuildLibraryPeer(Guid peerAssetId)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var literal    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "42" };
        var literalOut = DataPin("value", "Out", "System.Int32");
        literal.Pins.Add(literalOut);

        var ret        = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn  = ExecPin("ExecIn", "In");
        var retValueIn = DataPin("Result", "In", "System.Int32");
        ret.Pins.AddRange(new[] { retExecIn, retValueIn });

        var graph = new Graph
        {
            Id      = Guid.NewGuid(),
            Name    = "GetAnswer",
            Kind    = GraphKind.Function,
            Outputs = { Decl("Result", "System.Int32") },
            Nodes   = { entry, literal, ret },
            Links   =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,   ToNodeId = ret.Id, ToPinId = retExecIn.Id },
                new Link { FromNodeId = literal.Id, FromPinId = literalOut.Id, ToNodeId = ret.Id, ToPinId = retValueIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = peerAssetId,
            Name     = "AnswerLib",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// Caller Instance asset: EventEntry -&gt; CallPeerBlueprint("GetAnswer") -&gt; SetVariable(Captured)
    /// -&gt; Return. <paramref name="capturedVar"/> is returned so the test can read the variable's
    /// final value back out of the attached entity's state after ticking.
    /// </summary>
    private static BlueprintAsset BuildCaller(Guid peerAssetId, out VariableDecl capturedVar)
    {
        capturedVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Captured",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
            DefaultValueJson = "0",
        };

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var callNode   = new CallPeerBlueprintNode
        {
            Id              = Guid.NewGuid(),
            PeerBlueprintId = peerAssetId.ToString(),
            FunctionRef     = "GetAnswer",
        };
        var callIn      = ExecPin("In", "In");
        var callOut     = ExecPin("Out", "Out");
        var callReturn  = DataPin("Return", "Out", "System.Int32");
        callNode.Pins.AddRange(new[] { callIn, callOut, callReturn });

        var setVar     = new SetVariableNode { Id = Guid.NewGuid(), VariableId = capturedVar.Id.ToString() };
        var setIn      = ExecPin("In", "In");
        var setOut     = ExecPin("Out", "Out");
        var setValueIn = DataPin("Value", "In", "System.Int32");
        setVar.Pins.AddRange(new[] { setIn, setOut, setValueIn });

        var ret       = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retExecIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, callNode, setVar, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,  ToNodeId = callNode.Id, ToPinId = callIn.Id },
                new Link { FromNodeId = callNode.Id, FromPinId = callOut.Id,   ToNodeId = setVar.Id,   ToPinId = setIn.Id },
                new Link { FromNodeId = callNode.Id, FromPinId = callReturn.Id, ToNodeId = setVar.Id,  ToPinId = setValueIn.Id },
                new Link { FromNodeId = setVar.Id,   FromPinId = setOut.Id,    ToNodeId = ret.Id,      ToPinId = retExecIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId       = Guid.NewGuid(),
            Name          = "CallPeerCaller",
            Dispatch      = BlueprintDispatchKind.Instance,
            Variables     = { capturedVar },
            CallablePeers = { peerAssetId },
            Graphs        = { graph },
        };
        return asset;
    }

    private static BlueprintSignature MakePeerSignature(BlueprintAsset peer) => new(
        Path:                  "",
        AssetId:               peer.AssetId,
        Name:                  peer.Name,
        SanitizedName:         peer.Name,
        BlueprintId:           BlueprintIdHash.Compute(peer.AssetId),
        Dispatch:              peer.Dispatch,
        ExportedFunctions:     new[]
        {
            new BlueprintFunctionSig(
                "GetAnswer",
                Array.Empty<BlueprintParamSig>(),
                new[] { new BlueprintParamSig("Result", "System.Int32") }),
        },
        Hostings:              Array.Empty<AiPrimitiveHosting>(),
        DeclaredCallablePeers: Array.Empty<Guid>());

    /// <summary>
    /// Reads an <c>unmanaged</c> field out of a blueprint's attached live state, mirroring
    /// <c>WhenNodeRuntimeTests.ReadSlotField</c>.
    /// </summary>
    private static unsafe T ReadSlotField<T>(
        BlueprintTestFixture fixture, BlueprintAsset asset, Fdp.Core.Entity entity, string fieldName)
        where T : unmanaged
    {
        var hash = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            $"Blueprint definition not found for asset {asset.AssetId}");
        var stateType = def!.StateClrType;
        Assert.NotNull(stateType);
        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        var offset = (int)System.Runtime.InteropServices.Marshal.OffsetOf(stateType!, fieldName);
        return System.Runtime.InteropServices.MemoryMarshal.Read<T>(
            state!.Value.AsSpan().Slice(offset, System.Runtime.CompilerServices.Unsafe.SizeOf<T>()));
    }

    // =========================================================================
    // BP-110 regression lock: the cross-asset call compiles, RUNS, and returns the
    // peer's value. Before the fix this threw
    //   BP7001: Roslyn: CS0103 The name '__Peer_6928BFD5_Bp' does not exist in the current context
    // even with caller and peer in the SAME merged compilation.
    // =========================================================================

    [Fact]
    public void CallPeerBlueprint_CallerAndPeerCompiledTogether_ReturnsPeerValue()
    {
        var peerAssetId = Guid.NewGuid();
        var peer   = BuildLibraryPeer(peerAssetId);
        var caller = BuildCaller(peerAssetId, out var capturedVar);
        var options = MakeOptions(new[] { MakePeerSignature(peer) });

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Throws with the Roslyn diagnostics if the generated C# does not compile.
        fixture.CompileAndLoadMany(new[] { peer, caller }, options);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(caller, entity);
        fixture.TickFrame(0.016f);

        int captured = ReadSlotField<int>(fixture, caller, entity, capturedVar.Name);
        Assert.Equal(42, captured);
    }
}
