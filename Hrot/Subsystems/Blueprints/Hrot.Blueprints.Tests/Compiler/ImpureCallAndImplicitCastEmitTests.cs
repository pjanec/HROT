using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
// Disambiguate: both Hrot.Blueprints.Core.Assets and Fdp.Toolkit.Blueprints define BlueprintDispatchKind.
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using Probes               = Hrot.Blueprints.Tests.Compiler.P7ProbeHelpers;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Game-free, Stage1-7 emit-inspection regression tests locking two real compiler bugs the
/// Hill-attack migration surfaced -- both "never-exercised path" bugs (no prior test drove an
/// EXEC/impure CLR <see cref="FunctionCallNode"/>, nor a numeric implicit-coercion cast):
/// <list type="bullet">
///   <item>
///   A. An impure (exec) <see cref="FunctionCallNode"/> targeting a CLR helper must lower via
///   <c>IrOp_PureCall</c> to <c>global::{TargetTypeId}.{MethodName}(...)</c>, NOT
///   <c>IrOp_LibraryCall(0, ...)</c> (which emitted a nonexistent
///   <c>global::__LibBp_00000000_Bp</c> class -- CS0103). See
///   <c>Stage5_Schedule</c>'s <c>case FunctionCallNode fc when !fc.IsPure</c>.
///   </item>
///   <item>
///   B. A VOID impure call must emit a bare <c>global::...(...);</c> statement, not the
///   uncompilable <c>var __t = {voidCall};</c> a naive lowering would produce.
///   </item>
///   <item>
///   C. An implicit numeric coercion (e.g. <c>System.Byte</c> -&gt; <c>System.Int32</c>) inserted
///   by <c>Stage3_Normalize.InsertImplicitCasts</c> must emit a native C# cast
///   <c>(global::System.Int32)__tN</c>, NOT a call to a nonexistent
///   <c>global::Cast.System.Int32(...)</c> method (CS0400). See
///   <c>StatementEmitter</c>'s <c>IrOp_PureCall</c> case, which intercepts the synthesized
///   <c>"Cast.&lt;Type&gt;"</c> method-FQN.
///   </item>
/// </list>
/// These tests only compile (<see cref="BlueprintCompiler.Compile"/>) and inspect
/// <c>result.GeneratedSource</c> TEXT -- no Roslyn/ALC load of the emitted code is needed to prove
/// the lowering/emit shape is correct.
/// </summary>
public sealed class ImpureCallAndImplicitCastEmitTests
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

    // -----------------------------------------------------------------------
    // A. Impure returning FunctionCall -> IrOp_PureCall -> global::Type.Method(...)
    // -----------------------------------------------------------------------

    [Fact]
    public void ImpureFunctionCall_ReturningValue_LowersToClrCall_NotLibraryCall()
    {
        var asset = BuildImpureCallAsset(
            typeof(Probes).FullName!, nameof(Probes.Identity),
            xValue: 5, voidCall: false);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"))}");
        var src = result.GeneratedSource!;

        Assert.Contains("global::Hrot.Blueprints.Tests.Compiler.P7ProbeHelpers.Identity(", src);
        Assert.DoesNotContain("__LibBp_", src);
        Assert.DoesNotContain("global::Cast.", src);
    }

    // -----------------------------------------------------------------------
    // B. Impure VOID FunctionCall -> bare statement, not `var __t = voidCall()`
    // -----------------------------------------------------------------------

    [Fact]
    public void ImpureFunctionCall_Void_EmitsBareStatement_NotAssignment()
    {
        var asset = BuildImpureCallAsset(
            typeof(Probes).FullName!, nameof(Probes.VoidProbe),
            xValue: 7, voidCall: true);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"))}");
        var src = result.GeneratedSource!;

        const string callFragment = "global::Hrot.Blueprints.Tests.Compiler.P7ProbeHelpers.VoidProbe(";
        Assert.Contains(callFragment, src);
        // Must be a bare statement (`{call};`), never materialized into a local
        // (`var __tN = {call};`) -- a void CLR method invocation used as an expression is CS0029.
        Assert.DoesNotContain("= " + callFragment, src);
    }

    /// <summary>
    /// Builds an Instance-dispatch asset with a single "Tick" Function graph exercising an EXEC
    /// FunctionCallNode (Pins authored EXPLICITLY -- mirrors <c>BATCH03A_FunctionGraphCallTests</c>'
    /// exec-FunctionCall pin shape, since Stage0_Rehydrate skips any node whose Pins are already
    /// non-empty):
    /// <c>EventEntry --exec--&gt; FunctionCall(exec) --exec--&gt; [SetVariable(Result) --exec--&gt;] Return</c>,
    /// with FunctionCall.x &lt;- Literal(<paramref name="xValue"/>).
    /// <para>
    /// When <paramref name="voidCall"/> is false, the FunctionCall has a data-Out "Return" pin
    /// wired into a SetVariable(Result) node between it and Return. When true (void target
    /// method), the FunctionCall has NO data-Out pin and its ExecOut wires directly to Return.
    /// </para>
    /// </summary>
    private static BlueprintAsset BuildImpureCallAsset(
        string targetTypeId, string methodName, int xValue, bool voidCall)
    {
        var assetId     = Guid.NewGuid();
        var resultVarId = Guid.NewGuid();
        var graphId     = Guid.NewGuid();

        var entryId  = Guid.NewGuid();
        var litId    = Guid.NewGuid();
        var callId   = Guid.NewGuid();
        var setVarId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        var entryExecOut = Guid.NewGuid();
        var litOut       = Guid.NewGuid();

        var callExecIn  = Guid.NewGuid();
        var callExecOut = Guid.NewGuid();
        var callXIn     = Guid.NewGuid();
        var callReturnOut = voidCall ? (Guid?)null : Guid.NewGuid();

        var setExecIn  = Guid.NewGuid();
        var setExecOut = Guid.NewGuid();
        var setValueIn = Guid.NewGuid();

        var retExecIn = Guid.NewGuid();

        var callPins = new List<Pin>
        {
            new() { Id = callExecIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
            new() { Id = callExecOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
            new() { Id = callXIn,     Name = "x",        Direction = "In",  IsExec = false,
                    TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
        };
        if (callReturnOut.HasValue)
        {
            callPins.Add(new() { Id = callReturnOut.Value, Name = "Return", Direction = "Out", IsExec = false,
                    TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } });
        }

        var nodes = new List<Node>
        {
            new EventEntryNode
            {
                Id   = entryId,
                Pins = new List<Pin>
                {
                    new() { Id = entryExecOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                },
            },
            new LiteralNode
            {
                Id        = litId,
                TypeId    = "System.Int32",
                ValueJson = xValue.ToString(),
                Pins = new List<Pin>
                {
                    new() { Id = litOut, Name = "value", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                },
            },
            // Pins authored EXPLICITLY (non-empty) -- Stage0_Rehydrate.Run skips nodes whose Pins
            // are already populated, so this exec FunctionCallNode is NOT reflected/rebuilt; the
            // shape above is used as-is (mirrors BATCH03A_FunctionGraphCallTests' exec-FunctionCall
            // authoring convention).
            new FunctionCallNode
            {
                Id            = callId,
                TargetTypeId  = targetTypeId,
                MethodName    = methodName,
                IsPure        = false,
                TargetGraphId = "",
                Pins          = callPins,
            },
            new ReturnNode
            {
                Id   = returnId,
                Pins = new List<Pin>
                {
                    new() { Id = retExecIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                },
            },
        };

        var links = new List<Link>
        {
            new() { FromNodeId = entryId, FromPinId = entryExecOut, ToNodeId = callId, ToPinId = callExecIn },
            new() { FromNodeId = litId,   FromPinId = litOut,       ToNodeId = callId, ToPinId = callXIn },
        };

        var variables = new List<VariableDecl>();

        if (voidCall)
        {
            // FunctionCall.ExecOut -> Return directly (no SetVariable / no data-out pin to wire).
            links.Add(new() { FromNodeId = callId, FromPinId = callExecOut, ToNodeId = returnId, ToPinId = retExecIn });
        }
        else
        {
            var setVarNode = new SetVariableNode
            {
                Id         = setVarId,
                VariableId = resultVarId.ToString(),
                Pins = new List<Pin>
                {
                    new() { Id = setExecIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                    new() { Id = setExecOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    new() { Id = setValueIn, Name = "value",   Direction = "In",  IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                },
            };
            nodes.Add(setVarNode);

            links.Add(new() { FromNodeId = callId,   FromPinId = callExecOut,      ToNodeId = setVarId, ToPinId = setExecIn });
            links.Add(new() { FromNodeId = setVarId, FromPinId = setExecOut,       ToNodeId = returnId, ToPinId = retExecIn });
            links.Add(new() { FromNodeId = callId,   FromPinId = callReturnOut!.Value, ToNodeId = setVarId, ToPinId = setValueIn });

            variables.Add(new() { Id = resultVarId, Name = "Result", Type = new BlueprintTypeRef { TypeId = "System.Int32" } });
        }

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "Tick",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = nodes,
            Links   = links,
        };

        return new BlueprintAsset
        {
            AssetId          = assetId,
            Name             = "ImpureCallEmitTest",
            Dispatch         = BlueprintDispatchKind.Instance,
            Parameters       = new(),
            WorkingState     = new(),
            Variables        = variables,
            EventDispatchers = new(),
            CustomEvents     = new(),
            CallablePeers    = new(),
            Graphs           = new List<Graph> { graph },
            Header           = new Header(),
        };
    }

    // -----------------------------------------------------------------------
    // C. Implicit System.Byte -> System.Int32 coercion -> native C# cast
    // -----------------------------------------------------------------------

    [Fact]
    public void ImplicitByteToIntCoercion_EmitsNativeCast_NotCastHelperCall()
    {
        var asset = BuildByteCastProbeAsset(
            typeof(Probes).FullName!, nameof(Probes.Identity), xValue: 3);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"))}");
        var src = result.GeneratedSource!;

        Assert.Contains("(global::System.Int32)", src);
        Assert.DoesNotContain("global::Cast.", src);
    }

    /// <summary>
    /// Builds an Instance-dispatch asset mirroring <c>P7_FunctionCallContextTests.BuildProbeAsset</c>
    /// (pure FunctionCallNode with EMPTY Pins, rehydrated via CLR reflection -- the real
    /// "loaded from .bp.json" path), except the feeding Literal is typed <c>System.Byte</c> instead
    /// of <c>System.Int32</c>. <see cref="Probes.Identity"/>'s reflected parameter type resolves the
    /// FunctionCall's "x" data-in pin to <c>System.Int32</c>, so the Byte-out -&gt; Int32-in link has
    /// a coercible type mismatch that <c>Stage3_Normalize.InsertImplicitCasts</c> must bridge with a
    /// synthesized <see cref="CastNode"/>.
    /// </summary>
    private static BlueprintAsset BuildByteCastProbeAsset(
        string targetTypeId, string methodName, byte xValue)
    {
        var assetId     = Guid.NewGuid();
        var resultVarId = Guid.NewGuid();
        var graphId     = Guid.NewGuid();

        var entryId  = Guid.NewGuid();
        var litId    = Guid.NewGuid();
        var callId   = Guid.NewGuid();
        var setVarId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        var entryExecOut = Guid.NewGuid();
        var litOut        = Guid.NewGuid();
        // Placeholder link-pin GUIDs for the FunctionCallNode's (not-yet-hydrated) In/Out pins --
        // Stage0's AssignLinkGuids binds these positionally to the rehydrated pin list.
        var callXIn       = Guid.NewGuid();
        var callReturnOut = Guid.NewGuid();
        var setExecIn  = Guid.NewGuid();
        var setExecOut = Guid.NewGuid();
        var setValueIn = Guid.NewGuid();
        var retExecIn  = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "Tick",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryExecOut, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new LiteralNode
                {
                    Id        = litId,
                    TypeId    = "System.Byte",
                    ValueJson = xValue.ToString(),
                    Pins = new List<Pin>
                    {
                        new() { Id = litOut, Name = "value", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } },
                    },
                },
                // Pins EMPTY -- Stage0_Rehydrate resolves this via CLR reflection (the real path);
                // Identity(int x)'s reflected parameter type types the "x" pin System.Int32.
                new FunctionCallNode
                {
                    Id            = callId,
                    TargetTypeId  = targetTypeId,
                    MethodName    = methodName,
                    IsPure        = true,
                    TargetGraphId = "",
                    Pins          = new List<Pin>(),
                },
                new SetVariableNode
                {
                    Id         = setVarId,
                    VariableId = resultVarId.ToString(),
                    Pins = new List<Pin>
                    {
                        new() { Id = setExecIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new() { Id = setExecOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new() { Id = setValueIn, Name = "value",   Direction = "In",  IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id   = returnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = retExecIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId,  FromPinId = entryExecOut, ToNodeId = setVarId, ToPinId = setExecIn  },
                new() { FromNodeId = setVarId, FromPinId = setExecOut,   ToNodeId = returnId,  ToPinId = retExecIn },
                new() { FromNodeId = litId,    FromPinId = litOut,       ToNodeId = callId,    ToPinId = callXIn },
                new() { FromNodeId = callId,   FromPinId = callReturnOut, ToNodeId = setVarId, ToPinId = setValueIn },
            },
        };

        return new BlueprintAsset
        {
            AssetId          = assetId,
            Name             = "ByteCastProbeTest",
            Dispatch         = BlueprintDispatchKind.Instance,
            Parameters       = new(),
            WorkingState     = new(),
            Variables        = new List<VariableDecl>
            {
                new() { Id = resultVarId, Name = "Result", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
            EventDispatchers = new(),
            CustomEvents     = new(),
            CallablePeers    = new(),
            Graphs           = new List<Graph> { graph },
            Header           = new Header(),
        };
    }
}
