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

    // =========================================================================
    // BP-113 regression lock: a peer function with >1 output must fan the carrier back
    // out ACROSS the asset boundary, not just within the same asset.
    //
    // BP-73 gave an N-output Function graph a ValueTuple carrier + fan-out for the SAME-asset
    // FunctionCall node, but never touched CallPeerBlueprint -- the CROSS-asset call to a Library's
    // exported function. So an N-output function worked when called from inside its own asset and
    // silently lost every output past Outputs[0] the moment another blueprint called it -- exactly
    // backwards for a Function Library, whose entire purpose is being called from elsewhere.
    //
    // This is only testable end-to-end at all because BP-110 (above) made a peer call able to
    // compile and run in the first place; before that fix Roslyn couldn't even resolve the call
    // target, so there was no cross-asset call site to fan a carrier out of.
    // =========================================================================

    /// <summary>
    /// Library asset exporting one Function graph "Split" with TWO declared outputs
    /// ("Lo", "Hi", both System.Int32) -- EventEntry -&gt; Return(Lo=7, Hi=99). Two DIFFERENT
    /// literal values is the point: a carrier that silently collapsed both outputs onto one slot,
    /// or transposed them, would be caught by asserting both values independently rather than just
    /// a pin count.
    /// </summary>
    private static BlueprintAsset BuildMultiOutputLibraryPeer(Guid peerAssetId)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var litLo    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
        var litLoOut = DataPin("value", "Out", "System.Int32");
        litLo.Pins.Add(litLoOut);

        var litHi    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "99" };
        var litHiOut = DataPin("value", "Out", "System.Int32");
        litHi.Pins.Add(litHiOut);

        var ret       = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn = ExecPin("ExecIn", "In");
        // Return's data-IN pins pair POSITIONALLY with Graph.Outputs -- "Lo" first, "Hi" second,
        // matching the outputs list below.
        var retLoIn = DataPin("Lo", "In", "System.Int32");
        var retHiIn = DataPin("Hi", "In", "System.Int32");
        ret.Pins.AddRange(new[] { retExecIn, retLoIn, retHiIn });

        var graph = new Graph
        {
            Id      = Guid.NewGuid(),
            Name    = "Split",
            Kind    = GraphKind.Function,
            Outputs = { Decl("Lo", "System.Int32"), Decl("Hi", "System.Int32") },
            Nodes   = { entry, litLo, litHi, ret },
            Links   =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id, ToPinId = retExecIn.Id },
                new Link { FromNodeId = litLo.Id,  FromPinId = litLoOut.Id, ToNodeId = ret.Id, ToPinId = retLoIn.Id },
                new Link { FromNodeId = litHi.Id,  FromPinId = litHiOut.Id, ToNodeId = ret.Id, ToPinId = retHiIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = peerAssetId,
            Name     = "SplitLib",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// Caller Instance asset for the multi-output peer: EventEntry -&gt; CallPeerBlueprint("Split")
    /// -&gt; SetVariable(CapturedLo) -&gt; SetVariable(CapturedHi) -&gt; Return. The call node carries TWO
    /// data-OUT pins ("Lo", "Hi", both System.Int32) in declaration order, one wired to each
    /// SetVariable's Value pin -- exactly the shape Stage5's <c>EmitCarrierFanOut</c> pairs
    /// positionally against the peer signature's declared outputs.
    /// </summary>
    private static BlueprintAsset BuildMultiOutputCaller(
        Guid peerAssetId, out VariableDecl capturedLo, out VariableDecl capturedHi)
    {
        capturedLo = new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "CapturedLo",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValueJson = "0",
        };
        capturedHi = new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "CapturedHi",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValueJson = "0",
        };

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var callNode  = new CallPeerBlueprintNode
        {
            Id              = Guid.NewGuid(),
            PeerBlueprintId = peerAssetId.ToString(),
            FunctionRef     = "Split",
        };
        var callIn   = ExecPin("In", "In");
        var callOut  = ExecPin("Out", "Out");
        var callLo   = DataPin("Lo", "Out", "System.Int32");
        var callHi   = DataPin("Hi", "Out", "System.Int32");
        callNode.Pins.AddRange(new[] { callIn, callOut, callLo, callHi });

        var setLo      = new SetVariableNode { Id = Guid.NewGuid(), VariableId = capturedLo.Id.ToString() };
        var setLoIn    = ExecPin("In", "In");
        var setLoOut   = ExecPin("Out", "Out");
        var setLoValue = DataPin("Value", "In", "System.Int32");
        setLo.Pins.AddRange(new[] { setLoIn, setLoOut, setLoValue });

        var setHi      = new SetVariableNode { Id = Guid.NewGuid(), VariableId = capturedHi.Id.ToString() };
        var setHiIn    = ExecPin("In", "In");
        var setHiOut   = ExecPin("Out", "Out");
        var setHiValue = DataPin("Value", "In", "System.Int32");
        setHi.Pins.AddRange(new[] { setHiIn, setHiOut, setHiValue });

        var ret       = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retExecIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, callNode, setLo, setHi, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id, ToNodeId = callNode.Id, ToPinId = callIn.Id },
                new Link { FromNodeId = callNode.Id, FromPinId = callOut.Id,  ToNodeId = setLo.Id,     ToPinId = setLoIn.Id },
                new Link { FromNodeId = callNode.Id, FromPinId = callLo.Id,   ToNodeId = setLo.Id,     ToPinId = setLoValue.Id },
                new Link { FromNodeId = setLo.Id,    FromPinId = setLoOut.Id, ToNodeId = setHi.Id,     ToPinId = setHiIn.Id },
                new Link { FromNodeId = callNode.Id, FromPinId = callHi.Id,   ToNodeId = setHi.Id,     ToPinId = setHiValue.Id },
                new Link { FromNodeId = setHi.Id,    FromPinId = setHiOut.Id, ToNodeId = ret.Id,       ToPinId = retExecIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId       = Guid.NewGuid(),
            Name          = "CallPeerMultiOutputCaller",
            Dispatch      = BlueprintDispatchKind.Instance,
            Variables     = { capturedLo, capturedHi },
            CallablePeers = { peerAssetId },
            Graphs        = { graph },
        };
        return asset;
    }

    private static BlueprintSignature MakeMultiOutputPeerSignature(BlueprintAsset peer) => new(
        Path:                  "",
        AssetId:               peer.AssetId,
        Name:                  peer.Name,
        SanitizedName:         peer.Name,
        BlueprintId:           BlueprintIdHash.Compute(peer.AssetId),
        Dispatch:              peer.Dispatch,
        ExportedFunctions:     new[]
        {
            new BlueprintFunctionSig(
                "Split",
                Array.Empty<BlueprintParamSig>(),
                new[]
                {
                    new BlueprintParamSig("Lo", "System.Int32"),
                    new BlueprintParamSig("Hi", "System.Int32"),
                }),
        },
        Hostings:              Array.Empty<AiPrimitiveHosting>(),
        DeclaredCallablePeers: Array.Empty<Guid>());

    /// <summary>
    /// <b>BP-113 — a Library function with >1 output must fan its carrier out across the asset
    /// boundary, not just within the same asset.</b>
    /// <para>
    /// BP-73 gave an N-output Function graph a ValueTuple carrier plus a fan-out at every call site
    /// -- but only for the SAME-asset <c>FunctionCall</c> node. It never touched
    /// <c>CallPeerBlueprint</c>, the CROSS-asset call to another blueprint's exported function. The
    /// result: an N-output function worked perfectly when called from inside its own asset, and
    /// silently dropped every output past <c>Outputs[0]</c> the instant another blueprint called
    /// it through a Library export -- which is backwards, since being callable from elsewhere is
    /// the entire reason a Function Library exists.
    /// </para>
    /// <para>
    /// This test is only possible at all because of the BP-110 fix above: before BP-110, Roslyn
    /// could not even resolve the generated call to <c>__Peer_{hash}_Bp</c>, so there was no
    /// compiling cross-asset call site to fan a carrier out of in the first place.
    /// </para>
    /// <para>
    /// Asserting <b>two different</b> peer-returned values (<c>Lo=7</c>, <c>Hi=99</c>) -- rather than
    /// just a data-OUT pin count -- is deliberate: a pin-count-only assertion is exactly the kind of
    /// check that let the original defect ship (the editor projected the right number of pins while
    /// the compiler still collapsed them to one underneath), and asserting a single captured value
    /// could pass even if the carrier's fields were collapsed or silently aliased onto each other.
    /// Two distinct values landing in two distinct captured variables is the only way to prove the
    /// fan-out actually ran.
    /// </para>
    /// </summary>
    [Fact]
    public void CallPeerBlueprint_MultiOutputPeerFunction_ReturnsBothValuesAcrossTheAssetBoundary()
    {
        var peerAssetId = Guid.NewGuid();
        var peer   = BuildMultiOutputLibraryPeer(peerAssetId);
        var caller = BuildMultiOutputCaller(peerAssetId, out var capturedLo, out var capturedHi);
        var options = MakeOptions(new[] { MakeMultiOutputPeerSignature(peer) });

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Throws with the Roslyn diagnostics if the generated C# does not compile.
        fixture.CompileAndLoadMany(new[] { peer, caller }, options);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(caller, entity);
        fixture.TickFrame(0.016f);

        int lo = ReadSlotField<int>(fixture, caller, entity, capturedLo.Name);
        int hi = ReadSlotField<int>(fixture, caller, entity, capturedHi.Name);

        // Two DIFFERENT values proves the fan-out actually happened -- a same-value coincidence
        // (e.g. both fields defaulting to 0, or both aliasing the SAME carrier field) would slip
        // past an assertion of only one value.
        Assert.True(lo == 7 && hi == 99,
            $"expected Lo=7 and Hi=99 as two DISTINCT values fanned out of the carrier across the " +
            $"asset boundary, but got Lo={lo}, Hi={hi}. Asserting two different values (rather than " +
            $"just a pin count, or a single value) is the whole point -- a collapsed or transposed " +
            $"carrier could still make ONE of these pass.");
    }

    /// <summary>
    /// <b>BP-113 — the OTHER projection: <c>Stage0_Rehydrate</c>, not the editor's.</b>
    /// <para>
    /// The peer-call pin shape is written down in two places —
    /// <c>NodePinSchema.CallPeerBlueprintPins</c> (what the editor draws) and
    /// <c>Stage0_Rehydrate.EnrichCallPeerBlueprintPins</c> (what the compiler reconstructs for an
    /// asset that persisted no pins). ⚠ <b>Both had to move together, and every other test in this
    /// file hands the compiler explicit pins — which exercises neither enricher.</b> That is trap #9
    /// exactly: two halves of one contract, each covered alone, the seam never crossed. It is how
    /// BP-113 itself shipped.
    /// </para>
    /// <para>
    /// So this caller's <c>CallPeerBlueprintNode</c> carries <b>no pins at all</b>
    /// (<c>Stage0_Rehydrate</c> only rehydrates a node whose <c>Pins</c> list is empty — <c>:69</c>),
    /// and its links address the pins by their <b>deterministic</b> GUIDs,
    /// <c>DeterministicIds.PinId(nodeId, name, direction)</c>. Those GUIDs are derived from the pin
    /// <i>name</i>, so the links resolve only if Stage 0 reconstructs pins actually named
    /// <c>Lo</c> and <c>Hi</c>. Were it still projecting a single <c>Return</c>, the two data links
    /// would bind to nothing and the captured values would not arrive.
    /// </para>
    /// </summary>
    [Fact]
    public void CallPeerBlueprint_MultiOutputPeer_PinsRehydratedByStage0_StillReturnsBothValues()
    {
        var peerAssetId = Guid.NewGuid();
        var peer = BuildMultiOutputLibraryPeer(peerAssetId);

        var capturedLo = new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "CapturedLo",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValueJson = "0",
        };
        var capturedHi = new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "CapturedHi",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValueJson = "0",
        };

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        // ⭐ No pins. Stage 0 must project them from the peer signature.
        var callNode = new CallPeerBlueprintNode
        {
            Id              = Guid.NewGuid(),
            PeerBlueprintId = peerAssetId.ToString(),
            FunctionRef     = "Split",
        };

        var setLo      = new SetVariableNode { Id = Guid.NewGuid(), VariableId = capturedLo.Id.ToString() };
        var setLoIn    = ExecPin("In", "In");
        var setLoOut   = ExecPin("Out", "Out");
        var setLoValue = DataPin("Value", "In", "System.Int32");
        setLo.Pins.AddRange(new[] { setLoIn, setLoOut, setLoValue });

        var setHi      = new SetVariableNode { Id = Guid.NewGuid(), VariableId = capturedHi.Id.ToString() };
        var setHiIn    = ExecPin("In", "In");
        var setHiOut   = ExecPin("Out", "Out");
        var setHiValue = DataPin("Value", "In", "System.Int32");
        setHi.Pins.AddRange(new[] { setHiIn, setHiOut, setHiValue });

        var ret       = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retExecIn);

        // Address the not-yet-existing pins by the GUIDs Stage 0 will deterministically give them.
        Guid CallPin(string name, string dir) => DeterministicIds.PinId(callNode.Id, name, dir);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, callNode, setLo, setHi, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,           ToNodeId = callNode.Id, ToPinId = CallPin("In", "In") },
                new Link { FromNodeId = callNode.Id, FromPinId = CallPin("Out", "Out"), ToNodeId = setLo.Id,    ToPinId = setLoIn.Id },
                new Link { FromNodeId = callNode.Id, FromPinId = CallPin("Lo",  "Out"), ToNodeId = setLo.Id,    ToPinId = setLoValue.Id },
                new Link { FromNodeId = setLo.Id,    FromPinId = setLoOut.Id,           ToNodeId = setHi.Id,    ToPinId = setHiIn.Id },
                new Link { FromNodeId = callNode.Id, FromPinId = CallPin("Hi",  "Out"), ToNodeId = setHi.Id,    ToPinId = setHiValue.Id },
                new Link { FromNodeId = setHi.Id,    FromPinId = setHiOut.Id,           ToNodeId = ret.Id,      ToPinId = retExecIn.Id },
            },
        };

        var caller = new BlueprintAsset
        {
            AssetId       = Guid.NewGuid(),
            Name          = "CallPeerRehydratedCaller",
            Dispatch      = BlueprintDispatchKind.Instance,
            Variables     = { capturedLo, capturedHi },
            CallablePeers = { peerAssetId },
            Graphs        = { graph },
        };

        var options = MakeOptions(new[] { MakeMultiOutputPeerSignature(peer) });

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoadMany(new[] { peer, caller }, options);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(caller, entity);
        fixture.TickFrame(0.016f);

        int lo = ReadSlotField<int>(fixture, caller, entity, capturedLo.Name);
        int hi = ReadSlotField<int>(fixture, caller, entity, capturedHi.Name);

        Assert.True(lo == 7 && hi == 99,
            $"expected Lo=7 and Hi=99 from pins reconstructed by Stage0_Rehydrate (the node persisted " +
            $"none), but got Lo={lo}, Hi={hi}. This is the half of the contract the editor-side tests " +
            $"cannot reach: if Stage 0 still projected one pin named 'Return', the deterministic " +
            $"'Lo'/'Hi' link GUIDs would bind to nothing.");
    }
}
