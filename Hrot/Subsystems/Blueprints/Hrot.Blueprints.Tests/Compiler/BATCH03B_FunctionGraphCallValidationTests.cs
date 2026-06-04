using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using FunctionCallNode      = Hrot.Blueprints.Core.Assets.FunctionCallNode;
using ParameterDecl         = Hrot.Blueprints.Core.Assets.ParameterDecl;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BATCH-03B: Negative tests for V_FunctionGraphCallRules diagnostics
/// BP1651, BP1652, BP1653, BP1654 plus a positive control.
/// </summary>
public sealed class BATCH03B_FunctionGraphCallValidationTests
{
    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<Diagnostic> RunStage2(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        var opts = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        var ctx = new ValidationContext(sink, opts);
        Stage2_Validate.Run(asset, ctx);
        return sink.All;
    }

    /// <summary>
    /// Creates a minimal valid Instance BlueprintAsset skeleton (no graphs).
    /// Graphs are added per test.
    /// </summary>
    private static BlueprintAsset MakeAsset(params Graph[] graphs) => new BlueprintAsset
    {
        AssetId          = Guid.NewGuid(),
        Name             = "TestAsset",
        Dispatch         = BlueprintDispatchKind.Instance,
        Parameters       = new(),
        WorkingState     = new(),
        Variables        = new(),
        EventDispatchers = new(),
        CustomEvents     = new(),
        CallablePeers    = new(),
        Graphs           = new List<Graph>(graphs),
        Header           = new Header(),
    };

    /// <summary>
    /// Creates a minimal well-formed Function graph (Entry → Return, no latent, no inputs).
    /// </summary>
    private static Graph MakeFunctionGraph(string name, Guid id,
        List<ParameterDecl>? inputs = null, List<ParameterDecl>? outputs = null)
    {
        var entryId    = Guid.NewGuid();
        var returnId   = Guid.NewGuid();
        var entryExec  = Guid.NewGuid();
        var retExecIn  = Guid.NewGuid();
        return new Graph
        {
            Id      = id,
            Name    = name,
            Kind    = GraphKind.Function,
            Inputs  = inputs  ?? new(),
            Outputs = outputs ?? new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryExec, Name = "ExecOut", Direction = "Out",
                                IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode
                {
                    Id   = returnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = retExecIn, Name = "ExecIn", Direction = "In",
                                IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = entryExec,
                        ToNodeId   = returnId, ToPinId   = retExecIn },
            },
        };
    }

    /// <summary>
    /// Creates a caller graph (Function kind) that contains a FunctionCallNode
    /// with the given TargetGraphId and the given data-IN pins.
    /// </summary>
    private static Graph MakeCallerGraph(string name, Guid id, string targetGraphId,
        List<Pin>? extraDataInPins = null)
    {
        var entryId      = Guid.NewGuid();
        var callId       = Guid.NewGuid();
        var returnId     = Guid.NewGuid();
        var entryExec    = Guid.NewGuid();
        var callExIn     = Guid.NewGuid();
        var callExOut    = Guid.NewGuid();
        var retExecIn    = Guid.NewGuid();

        var callPins = new List<Pin>
        {
            new() { Id = callExIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
            new() { Id = callExOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
        };
        if (extraDataInPins != null) callPins.AddRange(extraDataInPins);

        return new Graph
        {
            Id      = id,
            Name    = name,
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
                        new() { Id = entryExec, Name = "ExecOut", Direction = "Out",
                                IsExec = true, TypeRef = new() },
                    },
                },
                new FunctionCallNode
                {
                    Id            = callId,
                    TargetTypeId  = "",
                    MethodName    = "",
                    IsPure        = false,
                    TargetGraphId = targetGraphId,
                    Pins          = callPins,
                },
                new ReturnNode
                {
                    Id   = returnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = retExecIn, Name = "ExecIn", Direction = "In",
                                IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId,  FromPinId = entryExec,  ToNodeId = callId,    ToPinId = callExIn  },
                new() { FromNodeId = callId,   FromPinId = callExOut,  ToNodeId = returnId,  ToPinId = retExecIn },
            },
        };
    }

    // -----------------------------------------------------------------------
    // BP1651 — target graph not found
    // -----------------------------------------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1651")]
    public void BP1651_TargetGraphId_PointsToNonExistentGuid_EmitsBP1651()
    {
        var nonExistentId = Guid.NewGuid();
        var callerGraph   = MakeCallerGraph("Caller", Guid.NewGuid(), nonExistentId.ToString());

        var diagnostics = RunStage2(MakeAsset(callerGraph));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.BP1651);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.BP1650);
    }

    [Fact]
    [CoversDiagnosticCode("BP1651")]
    public void BP1651_TargetGraphId_PointsToEventGraph_EmitsBP1651()
    {
        // Build an Event graph (not Function) and a caller pointing to it.
        // The Event graph must be structurally valid so V_GraphStructure does not
        // abort the pipeline (HasFatalErrors) before V_FunctionGraphCallRules runs.
        var eventGraphId  = Guid.NewGuid();
        var evEntryId     = Guid.NewGuid();
        var evReturnId    = Guid.NewGuid();
        var evEntryExec   = Guid.NewGuid();
        var evRetExecIn   = Guid.NewGuid();

        var eventGraph = new Graph
        {
            Id      = eventGraphId,
            Name    = "SomeEvent",
            Kind    = GraphKind.Event,         // NOT Function
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = evEntryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = evEntryExec, Name = "ExecOut", Direction = "Out",
                                IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode
                {
                    Id   = evReturnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = evRetExecIn, Name = "ExecIn", Direction = "In",
                                IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                // Proper exec link so V_GraphStructure doesn't abort the pipeline.
                new() { FromNodeId = evEntryId, FromPinId = evEntryExec,
                        ToNodeId   = evReturnId, ToPinId  = evRetExecIn },
            },
        };

        var callerGraph = MakeCallerGraph("Caller", Guid.NewGuid(), eventGraphId.ToString());

        var diagnostics = RunStage2(MakeAsset(callerGraph, eventGraph));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.BP1651);
    }

    // -----------------------------------------------------------------------
    // BP1652 — argument count mismatch
    // -----------------------------------------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1652")]
    public void BP1652_CallerHasOneArgPin_TargetHasTwoInputs_EmitsBP1652()
    {
        var targetId = Guid.NewGuid();
        var targetGraph = MakeFunctionGraph("Target", targetId,
            inputs: new List<ParameterDecl>
            {
                new() { Id = Guid.NewGuid(), Name = "a", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
                new() { Id = Guid.NewGuid(), Name = "b", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            });

        // Caller passes only 1 data-IN pin, but target has 2 inputs.
        var oneArgPin = new List<Pin>
        {
            new() { Id = Guid.NewGuid(), Name = "a", Direction = "In", IsExec = false,
                    TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
        };
        var callerGraph = MakeCallerGraph("Caller", Guid.NewGuid(), targetId.ToString(), oneArgPin);

        var diagnostics = RunStage2(MakeAsset(callerGraph, targetGraph));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.BP1652);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.BP1653);
    }

    // -----------------------------------------------------------------------
    // BP1653 — argument type mismatch
    // -----------------------------------------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1653")]
    public void BP1653_CallerArgTypeInt32_TargetInputTypeSingle_EmitsBP1653()
    {
        var targetId = Guid.NewGuid();
        var targetGraph = MakeFunctionGraph("Target", targetId,
            inputs: new List<ParameterDecl>
            {
                new() { Id = Guid.NewGuid(), Name = "x",
                        Type = new BlueprintTypeRef { TypeId = "System.Single" } }, // float
            });

        // Caller passes System.Int32 where target expects System.Single — clear mismatch.
        var mismatchPin = new List<Pin>
        {
            new() { Id = Guid.NewGuid(), Name = "x", Direction = "In", IsExec = false,
                    TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },  // int
        };
        var callerGraph = MakeCallerGraph("Caller", Guid.NewGuid(), targetId.ToString(), mismatchPin);

        var diagnostics = RunStage2(MakeAsset(callerGraph, targetGraph));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.BP1653);
        // Exactly BP1653, not BP1651 or BP1652.
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.BP1651);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.BP1652);
    }

    // -----------------------------------------------------------------------
    // BP1654 — cycle detection
    // -----------------------------------------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1654")]
    public void BP1654_SelfRecursion_EmitsBP1654()
    {
        // A calls itself: A → A
        var aId = Guid.NewGuid();

        // Build graph A manually: it has a FunctionCallNode pointing at itself.
        var entryId   = Guid.NewGuid();
        var callId    = Guid.NewGuid();
        var returnId  = Guid.NewGuid();
        var entryExec = Guid.NewGuid();
        var callExIn  = Guid.NewGuid();
        var callExOut = Guid.NewGuid();
        var retExecIn = Guid.NewGuid();

        var graphA = new Graph
        {
            Id      = aId,
            Name    = "SelfRecursive",
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
                        new() { Id = entryExec, Name = "ExecOut", Direction = "Out",
                                IsExec = true, TypeRef = new() },
                    },
                },
                new FunctionCallNode
                {
                    Id            = callId,
                    TargetTypeId  = "",
                    MethodName    = "",
                    IsPure        = false,
                    TargetGraphId = aId.ToString(),  // self-reference
                    Pins          = new List<Pin>
                    {
                        new() { Id = callExIn,  Name = "ExecIn",  Direction = "In",
                                IsExec = true, TypeRef = new() },
                        new() { Id = callExOut, Name = "ExecOut", Direction = "Out",
                                IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode
                {
                    Id   = returnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = retExecIn, Name = "ExecIn", Direction = "In",
                                IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = entryExec, ToNodeId = callId,   ToPinId = callExIn  },
                new() { FromNodeId = callId,  FromPinId = callExOut, ToNodeId = returnId, ToPinId = retExecIn },
            },
        };

        var diagnostics = RunStage2(MakeAsset(graphA));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.BP1654);
    }

    [Fact]
    [CoversDiagnosticCode("BP1654")]
    public void BP1654_TransitiveCycle_ACallsB_BCallsA_EmitsBP1654()
    {
        // A → B → A  (transitive cycle)
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        var graphA = MakeCallerGraph("GraphA", aId, bId.ToString()); // A calls B
        var graphB = MakeCallerGraph("GraphB", bId, aId.ToString()); // B calls A

        var diagnostics = RunStage2(MakeAsset(graphA, graphB));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.BP1654);
        // The cycle message should name both graphs.
        var cycleMsg = diagnostics.First(d => d.Code == DiagnosticCodes.BP1654).Message;
        Assert.Contains("GraphA", cycleMsg);
        Assert.Contains("GraphB", cycleMsg);
    }

    // -----------------------------------------------------------------------
    // Positive control — valid call, no BP165x expected
    // -----------------------------------------------------------------------

    [Fact]
    public void PositiveControl_ValidFunctionCall_NoBP165x()
    {
        // Caller passes 1 data-IN pin typed System.Int32; target has 1 input typed System.Int32.
        var targetId = Guid.NewGuid();
        var targetGraph = MakeFunctionGraph("Target", targetId,
            inputs: new List<ParameterDecl>
            {
                new() { Id = Guid.NewGuid(), Name = "value",
                        Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            });

        var matchingPin = new List<Pin>
        {
            new() { Id = Guid.NewGuid(), Name = "value", Direction = "In", IsExec = false,
                    TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
        };
        var callerGraph = MakeCallerGraph("Caller", Guid.NewGuid(), targetId.ToString(), matchingPin);

        var diagnostics = RunStage2(MakeAsset(callerGraph, targetGraph));

        // No function-graph-call diagnostics should be emitted.
        var bp165x = diagnostics.Where(d => d.Code.StartsWith("BP165")).ToList();
        Assert.True(bp165x.Count == 0,
            $"Expected no BP165x diagnostics but got: {string.Join(", ", bp165x.Select(d => $"{d.Code}: {d.Message}"))}");
    }
}
