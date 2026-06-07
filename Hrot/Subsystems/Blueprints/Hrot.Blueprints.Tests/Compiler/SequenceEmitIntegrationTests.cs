using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Runtime;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// SEQ2 integration tests: verify Sequence-with-latent and cross-block
/// value emit correctness by compiling the generated C# source through
/// Roslyn and asserting zero diagnostics (no CS0162, CS0164, CS0103).
/// </summary>
[Collection("DebugProbe")]
public sealed class SequenceEmitIntegrationTests
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

    private static BlueprintAsset BuildInstanceBlueprint(
        string name, List<VariableDecl>? variables, Graph graph)
    {
        return new BlueprintAsset
        {
            AssetId     = Guid.NewGuid(),
            Name        = name,
            Dispatch    = BlueprintDispatchKind.Instance,
            Parameters  = new(),
            WorkingState = new(),
            Variables   = variables ?? new(),
            EventDispatchers = new(),
            CustomEvents = new(),
            CallablePeers = new(),
            Graphs      = new() { graph },
            Header      = new Header(),
        };
    }

    private static BlueprintAsset BuildAiPrimitiveBlueprint(
        string name, Graph graph)
    {
        return new BlueprintAsset
        {
            AssetId     = Guid.NewGuid(),
            Name        = name,
            Dispatch    = BlueprintDispatchKind.AiPrimitive,
            Primitive   = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = new List<AiPrimitiveHosting> { AiPrimitiveHosting.BTreeAction },
            },
            Parameters  = new(),
            WorkingState = new(),
            Variables   = new(),
            EventDispatchers = new(),
            CustomEvents = new(),
            CallablePeers = new(),
            Graphs      = new() { graph },
            Header      = new Header(),
        };
    }

    /// <summary>
    /// Runs the full pipeline (Stage 2-8) including Roslyn compilation.
    /// Returns true if the compiled source has zero Roslyn errors.
    /// </summary>
    private static bool FullCompileSucceeds(BlueprintAsset asset, CompileOptions opts)
    {
        var result = new BlueprintCompiler().Compile(asset, opts);
        return result.Succeeded;
    }

    // ----------------------------------------------------------------
    // Test 1: Sequence with latent branch compiles cleanly (Instance).
    // ----------------------------------------------------------------

    [Fact]
    public void Sequence_LatentBranch_GeneratedSourceCompilesCleanly()
    {
        var asset = BuildSeqLatentAsset("SeqLatent");
        var opts  = DefaultOptions();
        Assert.True(FullCompileSucceeds(asset, opts),
            "Generated source must compile without errors (no CS0162, CS0164, CS0103).");
    }

    // ----------------------------------------------------------------
    // Test 2: Fresh tick runs pre-latent branch (Count == 1).
    // ----------------------------------------------------------------

    [Fact]
    public void Sequence_LatentBranch_FreshTick_RunsPreLatentBranch()
    {
        var asset = BuildSeqLatentAsset("SeqLatentRun");

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(asset);

        var harness = new BlueprintRunHarness(fixture);
        var entity = harness.SpawnAndAttach(asset);
        harness.Pump(1, 0.016f);

        Assert.Equal(1, harness.ReadIntField(entity, asset, "Count"));
    }

    // ----------------------------------------------------------------
    // Test 3: Cross-block data value compiles cleanly.
    // ----------------------------------------------------------------

    [Fact]
    public void Sequence_DataValueCrossesBranchBlocks_CompilesCleanly()
    {
        var asset = BuildTwoSyncAsset("SeqXBlock");
        var opts  = DefaultOptions();
        Assert.True(FullCompileSucceeds(asset, opts),
            "Generated source must compile without CS0103 errors.");
    }

    // ----------------------------------------------------------------
    // Test 4: Two synchronous branches both run side effects.
    // ----------------------------------------------------------------

    [Fact]
    public void Sequence_TwoSyncBranches_BothSideEffectsRun()
    {
        var asset = BuildTwoSyncAsset("SeqBothRun");

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(asset);

        var harness = new BlueprintRunHarness(fixture);
        var entity = harness.SpawnAndAttach(asset);
        harness.Pump(1, 0.016f);

        Assert.Equal(1, harness.ReadIntField(entity, asset, "A"));
        Assert.Equal(2, harness.ReadIntField(entity, asset, "B"));
    }

    // ----------------------------------------------------------------
    // Test 5: AiPrimitive parity — Sequence with latent compiles cleanly.
    // ----------------------------------------------------------------

    [Fact]
    public void Sequence_LatentBranch_AiPrimitive_CompilesCleanly()
    {
        var asset = BuildAiPrimSeqLatentAsset("SeqLatentAi");
        var opts  = DefaultOptions();
        Assert.True(FullCompileSucceeds(asset, opts),
            "AiPrimitive generated source must compile without errors.");
    }

    // ----------------------------------------------------------------
    // Asset builders
    // ----------------------------------------------------------------

    private static BlueprintAsset BuildSeqLatentAsset(string name)
    {
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var seqId   = Guid.NewGuid();
        var litId   = Guid.NewGuid();
        var svId    = Guid.NewGuid();
        var delayId = Guid.NewGuid();
        var retId   = Guid.NewGuid();

        var peOut    = Guid.NewGuid();
        var psIn     = Guid.NewGuid();
        var psThen0  = Guid.NewGuid();
        var psThen1  = Guid.NewGuid();
        var pLitIn   = Guid.NewGuid();
        var pLitOut  = Guid.NewGuid();
        var pSvIn    = Guid.NewGuid();
        var pSvOut   = Guid.NewGuid();
        var pSvVal   = Guid.NewGuid();
        var pDelIn   = Guid.NewGuid();
        var pDelOut  = Guid.NewGuid();
        var pRetIn   = Guid.NewGuid();

        var countVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Count",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = seqId,
                    Pins = new()
                    {
                        new Pin { Id = psIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    } },
                new LiteralNode { Id = litId, TypeId = "System.Int32", ValueJson = "1",
                    Pins = new()
                    {
                        new Pin { Id = pLitIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pLitOut, Name = "DataOut", Direction = "Out", IsExec = false, TypeRef = new() },
                    } },
                new SetVariableNode { Id = svId, VariableId = countVar.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() },
                    } },
                new LatentDelayNode { Id = delayId,
                    Pins = new()
                    {
                        new Pin { Id = pDelIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pDelOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    } },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = peOut,   ToNodeId = seqId,   ToPinId = psIn    },
                new() { FromNodeId = seqId,   FromPinId = psThen0, ToNodeId = svId,    ToPinId = pSvIn    },
                new() { FromNodeId = litId,   FromPinId = pLitOut, ToNodeId = svId,    ToPinId = pSvVal   },
                new() { FromNodeId = svId,    FromPinId = pSvOut,   ToNodeId = retId,   ToPinId = pRetIn   },
                new() { FromNodeId = seqId,   FromPinId = psThen1, ToNodeId = delayId, ToPinId = pDelIn   },
                new() { FromNodeId = delayId, FromPinId = pDelOut, ToNodeId = retId,   ToPinId = pRetIn   },
            },
        };

        return BuildInstanceBlueprint(name, new List<VariableDecl> { countVar }, graph);
    }

    private static BlueprintAsset BuildTwoSyncAsset(string name)
    {
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var seqId   = Guid.NewGuid();
        var litAId  = Guid.NewGuid();
        var svAId   = Guid.NewGuid();
        var litBId  = Guid.NewGuid();
        var svBId   = Guid.NewGuid();
        var retAId  = Guid.NewGuid();
        var retBId  = Guid.NewGuid();

        var peOut    = Guid.NewGuid();
        var psIn     = Guid.NewGuid();
        var psThen0  = Guid.NewGuid();
        var psThen1  = Guid.NewGuid();
        var pLitAIn  = Guid.NewGuid();
        var pLitAOut = Guid.NewGuid();
        var pSvAIn   = Guid.NewGuid();
        var pSvAOut  = Guid.NewGuid();
        var pSvAVal  = Guid.NewGuid();
        var pLitBIn  = Guid.NewGuid();
        var pLitBOut = Guid.NewGuid();
        var pSvBIn   = Guid.NewGuid();
        var pSvBOut  = Guid.NewGuid();
        var pSvBVal  = Guid.NewGuid();
        var pRetAIn  = Guid.NewGuid();
        var pRetBIn  = Guid.NewGuid();

        var varA = new VariableDecl { Id = Guid.NewGuid(), Name = "A", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var varB = new VariableDecl { Id = Guid.NewGuid(), Name = "B", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = seqId,
                    Pins = new()
                    {
                        new Pin { Id = psIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    } },
                new LiteralNode { Id = litAId, TypeId = "System.Int32", ValueJson = "1",
                    Pins = new()
                    {
                        new Pin { Id = pLitAIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pLitAOut, Name = "DataOut", Direction = "Out", IsExec = false, TypeRef = new() },
                    } },
                new SetVariableNode { Id = svAId, VariableId = varA.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvAIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvAOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvAVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() },
                    } },
                new LiteralNode { Id = litBId, TypeId = "System.Int32", ValueJson = "2",
                    Pins = new()
                    {
                        new Pin { Id = pLitBIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pLitBOut, Name = "DataOut", Direction = "Out", IsExec = false, TypeRef = new() },
                    } },
                new SetVariableNode { Id = svBId, VariableId = varB.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvBIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvBOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvBVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() },
                    } },
                new ReturnNode { Id = retAId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetAIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
                new ReturnNode { Id = retBId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetBIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = peOut,    ToNodeId = seqId,  ToPinId = psIn     },
                new() { FromNodeId = seqId,   FromPinId = psThen0,  ToNodeId = svAId,  ToPinId = pSvAIn   },
                new() { FromNodeId = litAId,  FromPinId = pLitAOut, ToNodeId = svAId,  ToPinId = pSvAVal  },
                // Then0 ends without Return - falls through to Then1 naturally.
                new() { FromNodeId = seqId,   FromPinId = psThen1,  ToNodeId = svBId,  ToPinId = pSvBIn   },
                new() { FromNodeId = litBId,  FromPinId = pLitBOut, ToNodeId = svBId,  ToPinId = pSvBVal  },
                new() { FromNodeId = svBId,   FromPinId = pSvBOut,  ToNodeId = retBId, ToPinId = pRetBIn  },
            },
        };

        return BuildInstanceBlueprint(name, new List<VariableDecl> { varA, varB }, graph);
    }

    private static BlueprintAsset BuildAiPrimSeqLatentAsset(string name)
    {
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var seqId   = Guid.NewGuid();
        var delayId = Guid.NewGuid();
        var retId   = Guid.NewGuid();

        var peOut    = Guid.NewGuid();
        var psIn     = Guid.NewGuid();
        var psThen0  = Guid.NewGuid();
        var psThen1  = Guid.NewGuid();
        var pDelIn   = Guid.NewGuid();
        var pDelOut  = Guid.NewGuid();
        var pRetIn   = Guid.NewGuid();

        var graph = new Graph
        {
            Id = graphId, Name = "Main", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = seqId,
                    Pins = new()
                    {
                        new Pin { Id = psIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    } },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
                new LatentDelayNode { Id = delayId,
                    Pins = new()
                    {
                        new Pin { Id = pDelIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pDelOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                    } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = peOut,    ToNodeId = seqId,   ToPinId = psIn    },
                new() { FromNodeId = seqId,   FromPinId = psThen0,  ToNodeId = retId,   ToPinId = pRetIn   },
                new() { FromNodeId = seqId,   FromPinId = psThen1,  ToNodeId = delayId, ToPinId = pDelIn   },
            },
        };

        return BuildAiPrimitiveBlueprint(name, graph);
    }
    // ----------------------------------------------------------------
    // Test 6 (C4): latent Delay inside a Sequence loops — Count climbs
    //             by 1 per completed delay period (not frozen at 1).
    // ----------------------------------------------------------------

    [Fact]
    public void Sequence_LatentDelay_LoopsAndReincrements()
    {
        // Duration pin is wired; use 0 delay for instant-completion loop test.
        // Each period takes 2 ticks: one to start delay+suspend, one to check+reset+return.
        const float delaySeconds = 0.0f;
        var asset = BuildSeqLatentWithDelayAsset("SeqLoop", delaySeconds, "Count");

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(asset);

        var harness = new BlueprintRunHarness(fixture);
        var entity = harness.SpawnAndAttach(asset);

        // Tick 1: fresh -> Count=1, delay(0) starts, suspend.
        fixture.TickFrame(0.016f);
        Assert.Equal(1, harness.ReadIntField(entity, asset, "Count"));

        // Tick 2: resume -> delay elapsed -> cursor reset -> return. Count still 1.
        fixture.TickFrame(0.016f);
        Assert.Equal(1, harness.ReadIntField(entity, asset, "Count"));

        // Tick 3: fresh -> Count=2.
        fixture.TickFrame(0.016f);
        Assert.Equal(2, harness.ReadIntField(entity, asset, "Count"));

        // Tick 4: resume -> elapsed -> return. Count still 2.
        fixture.TickFrame(0.016f);
        Assert.Equal(2, harness.ReadIntField(entity, asset, "Count"));

        // Tick 5: fresh -> Count=3.
        fixture.TickFrame(0.016f);
        Assert.Equal(3, harness.ReadIntField(entity, asset, "Count"));
    }

    /// <summary>
    /// Builds an Instance blueprint: EventEntry → Sequence(Then0 → Count=Count+1, Then1 → Delay).
    /// Uses GetVariable(Count) → AddInt(Count,1) → SetVariable(Count) for the increment.
    /// </summary>
    private static BlueprintAsset BuildSeqLatentWithDelayAsset(string name, float delaySeconds, string countVarName)
    {
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var seqId   = Guid.NewGuid();
        var gvId    = Guid.NewGuid();
        var litId   = Guid.NewGuid();
        var addId   = Guid.NewGuid();
        var svId    = Guid.NewGuid();
        var durLitId = Guid.NewGuid();
        var delayId = Guid.NewGuid();
        var retId   = Guid.NewGuid();

        var peOut    = Guid.NewGuid();
        var psIn     = Guid.NewGuid();
        var psThen0  = Guid.NewGuid();
        var psThen1  = Guid.NewGuid();
        var pGvIn    = Guid.NewGuid();
        var pGvOut   = Guid.NewGuid();
        var pLitIn   = Guid.NewGuid();
        var pLitOut  = Guid.NewGuid();
        var pAddIn   = Guid.NewGuid();
        var pAddOut  = Guid.NewGuid();
        var pAddA    = Guid.NewGuid();
        var pAddB    = Guid.NewGuid();
        var pSvIn    = Guid.NewGuid();
        var pSvOut   = Guid.NewGuid();
        var pSvVal   = Guid.NewGuid();
        var pDurIn   = Guid.NewGuid();
        var pDurOut  = Guid.NewGuid();
        var pDelIn   = Guid.NewGuid();
        var pDelOut  = Guid.NewGuid();
        var pDelDur  = Guid.NewGuid();
        var pRetIn   = Guid.NewGuid();

        var countVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = countVarName,
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = seqId,
                    Pins = new()
                    {
                        new Pin { Id = psIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    } },
                new GetVariableNode { Id = gvId, VariableId = countVar.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pGvIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pGvOut, Name = "DataOut", Direction = "Out", IsExec = false, TypeRef = new() },
                    } },
                new LiteralNode { Id = litId, TypeId = "System.Int32", ValueJson = "1",
                    Pins = new()
                    {
                        new Pin { Id = pLitIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pLitOut, Name = "DataOut", Direction = "Out", IsExec = false, TypeRef = new() },
                    } },
                new FunctionCallNode { Id = addId, IsPure = true, TargetTypeId = "Fdp.Toolkit.Blueprints.BlueprintMath", MethodName = "AddInt",
                    Pins = new()
                    {
                        new Pin { Id = pAddIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pAddOut, Name = "DataOut", Direction = "Out", IsExec = false, TypeRef = new() },
                        new Pin { Id = pAddA,   Name = "a",       Direction = "In",  IsExec = false, TypeRef = new() },
                        new Pin { Id = pAddB,   Name = "b",       Direction = "In",  IsExec = false, TypeRef = new() },
                    } },
                new SetVariableNode { Id = svId, VariableId = countVar.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() },
                    } },
                new LiteralNode { Id = durLitId, TypeId = "System.Single", ValueJson = $"{delaySeconds}f",
                    Pins = new()
                    {
                        new Pin { Id = pDurIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pDurOut, Name = "DataOut", Direction = "Out", IsExec = false, TypeRef = new() },
                    } },
                new LatentDelayNode { Id = delayId,
                    Pins = new()
                    {
                        new Pin { Id = pDelIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pDelOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pDelDur, Name = "Duration", Direction = "In",  IsExec = false, TypeRef = new() },
                    } },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = peOut,   ToNodeId = seqId,  ToPinId = psIn    },
                new() { FromNodeId = seqId,   FromPinId = psThen0, ToNodeId = svId,   ToPinId = pSvIn    },
                new() { FromNodeId = gvId,    FromPinId = pGvOut,  ToNodeId = addId,  ToPinId = pAddA    },
                new() { FromNodeId = litId,   FromPinId = pLitOut, ToNodeId = addId,  ToPinId = pAddB    },
                new() { FromNodeId = addId,   FromPinId = pAddOut, ToNodeId = svId,   ToPinId = pSvVal   },
                new() { FromNodeId = delayId, FromPinId = pDelOut, ToNodeId = retId,  ToPinId = pRetIn   },
                new() { FromNodeId = durLitId, FromPinId = pDurOut, ToNodeId = delayId,ToPinId = pDelDur  },
                new() { FromNodeId = seqId,   FromPinId = psThen1, ToNodeId = delayId,ToPinId = pDelIn   },
            },
        };

        return BuildInstanceBlueprint(name, new List<VariableDecl> { countVar }, graph);
    }

    // ----------------------------------------------------------------
    // DELAYTIME: Delay waits a RELATIVE duration (time + d), not absolute.
    // ----------------------------------------------------------------

    [Fact]
    public void Sequence_LatentDelay_WaitsFullDurationEachPeriod()
    {
        const float d = 1.0f;
        var asset = BuildSeqLatentWithDelayAsset("SeqDelayRel", d, "Count");

        // Verify the duration Literal is wired correctly.
        var graph = asset.Graphs[0];
        var delayNode = graph.Nodes.OfType<LatentDelayNode>().First();
        var durPin = delayNode.Pins.First(p => !p.IsExec && p.Direction == "In");
        var link = graph.Links.FirstOrDefault(l => l.ToNodeId == delayNode.Id && l.ToPinId == durPin.Id);
        Assert.NotNull(link); // This MUST exist for the delay to have non-zero duration

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(asset);

        var harness = new BlueprintRunHarness(fixture);
        var entity = harness.SpawnAndAttach(asset);

        // Start at a large absolute sim time to expose absolute-vs-relative bug.
        fixture.View.AdvanceTime(100.0f);
        fixture.World.SetSimulationTime(fixture.View.Time);

        // Step 1: Tick(time=100.0) -> Count == 1, suspended.
        fixture.TickFrame(0.0f);
        Assert.Equal(1, harness.ReadIntField(entity, asset, "Count"));

        // Step 2: Tick(time=100.5) half a period later -> Count == 1 (still waiting).
        fixture.TickFrame(0.5f);
        Assert.Equal(1, harness.ReadIntField(entity, asset, "Count"));

        // Step 3: Tick(time=101.01) just past 100+d -> delay elapsed, cursor resets.
        fixture.TickFrame(0.51f);
        Assert.Equal(1, harness.ReadIntField(entity, asset, "Count"));

        // Step 4: Tick(time=101.02) -> Count == 2 (second period started).
        fixture.TickFrame(0.01f);
        Assert.Equal(2, harness.ReadIntField(entity, asset, "Count"));

        // Step 5: Tick(time=101.5) half of the SECOND period -> Count == 2 (still waiting).
        fixture.TickFrame(0.48f);
        Assert.Equal(2, harness.ReadIntField(entity, asset, "Count"));

        // Step 6: Tick(time=102.03) -> delay just elapsed, cursor reset -> Count == 2.
        // (Increment to 3 happens on the NEXT tick after fresh-start.)
        fixture.TickFrame(0.53f);
        Assert.Equal(2, harness.ReadIntField(entity, asset, "Count"));
    }
}
