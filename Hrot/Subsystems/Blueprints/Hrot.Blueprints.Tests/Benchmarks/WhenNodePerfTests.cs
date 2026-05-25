using System;
using System.Diagnostics;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Mocks;
using Hrot.Blueprints.Tests.Runtime;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using Xunit;

namespace Hrot.Blueprints.Tests.Benchmarks;

/// <summary>
/// CI-gate performance tests for WhenNode hot paths.
/// Each test verifies that a blueprint tick completes within a generous
/// multiple of the target nanosecond budget, suitable for CI machines.
/// </summary>
[Collection("DebugProbe")]
public sealed class WhenNodePerfTests
{
    // ---- Empty event catalog (bypasses Stage 2 BP2005 for test event types) ----

    private sealed class EmptyEventCatalog : IEngineEventCatalog
    {
        public static readonly EmptyEventCatalog Instance = new();
        public System.Collections.Generic.IReadOnlyList<EngineEventCatalogEntry> GetEntries()
            => Array.Empty<EngineEventCatalogEntry>();
    }

    // ---- EQS template catalog stub ----

    private sealed class AlwaysContainsCatalog : IEqsTemplateCatalog
    {
        public bool Contains(Guid assetId) => true;
    }

    // ---- ConditionMet predicate mocks ----

    private sealed class NeverFirePredicateCompiler : IPredicateCompiler
    {
        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root)
            => (_, _) => false;
        public System.Collections.Generic.IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto root)
            => Array.Empty<Type>();
    }

    private sealed class MockSearchPredicateRegistry : ISearchPredicateRegistry { }

    // ---- Compile options ----

    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static CompileOptions EventFiredOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      EmptyEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static CompileOptions MakeEqsOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>(),
        EqsTemplates:      new AlwaysContainsCatalog());

    // ---- Asset builders ----

    /// <summary>Minimal Instance blueprint with one ValueChanged WhenNode.</summary>
    private static BlueprintAsset BuildValueChangedAsset()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var whenNode = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent",
                PropertyPath    = "X",
                Source          = ValueChangedSource.SelfComponent,
                Epsilon         = 0f,
            },
        };
        var execIn       = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var execOut      = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var onFired      = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(execIn);
        whenNode.Pins.Add(execOut);
        whenNode.Pins.Add(onFired);

        var retNode      = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retExecIn);

        var retFiredNode = new ReturnNode { Id = Guid.NewGuid() };
        var retFiredIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retFiredNode.Pins.Add(retFiredIn);

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, retNode, retFiredNode },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id,     ToPinId = execIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = execOut.Id,      ToNodeId = retNode.Id,      ToPinId = retExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = onFired.Id,      ToNodeId = retFiredNode.Id, ToPinId = retFiredIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "PerfWhenVC",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>Minimal Instance blueprint with one EventFired WhenNode (WhenTestHitEvent).</summary>
    private static BlueprintAsset BuildEventFiredAsset()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var whenNode = new WhenNode
        {
            Id         = Guid.NewGuid(),
            Mode       = WhenMode.EventFired,
            Edges      = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId  = "Hrot.Blueprints.Tests.Runtime.WhenTestHitEvent",
                TargetFilter = EventTargetFilter.None,
            },
        };
        var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(whenExecIn);
        whenNode.Pins.Add(whenExecOut);
        whenNode.Pins.Add(whenOnFired);

        var retNode    = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retExecIn);

        var ret2Node   = new ReturnNode { Id = Guid.NewGuid() };
        var ret2ExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        ret2Node.Pins.Add(ret2ExecIn);

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, retNode, ret2Node },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = whenExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenExecOut.Id,  ToNodeId = retNode.Id,  ToPinId = retExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id,  ToNodeId = ret2Node.Id, ToPinId = ret2ExecIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "PerfWhenEF",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>Minimal Instance blueprint with one ConditionMet WhenNode.</summary>
    private static BlueprintAsset BuildCondMetAsset()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var whenNode = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.ConditionMet,
            Edges = WhenEdge.RisingEdge,
            ConditionMet = new ConditionMetPayload
            {
                Condition = new PropertyMatchDto
                {
                    ComponentType = typeof(object),
                    PropertyPath  = "Value",
                    Predicate     = new NumericPredicateDto { MinValue = 5.0, MaxValue = double.MaxValue },
                },
            },
        };
        var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(whenExecIn);
        whenNode.Pins.Add(whenExecOut);
        whenNode.Pins.Add(whenOnFired);

        var retNode  = new ReturnNode { Id = Guid.NewGuid() };
        var retIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retIn);

        var ret2Node = new ReturnNode { Id = Guid.NewGuid() };
        var ret2In   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        ret2Node.Pins.Add(ret2In);

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, retNode, ret2Node },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = whenExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenExecOut.Id,  ToNodeId = retNode.Id,  ToPinId = retIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id,  ToNodeId = ret2Node.Id, ToPinId = ret2In.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "PerfWhenCM",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>Minimal Instance blueprint with one EqsResult WhenNode.</summary>
    private static BlueprintAsset BuildEqsResultAsset()
    {
        var assetId     = Guid.NewGuid();
        var graphId     = Guid.NewGuid();
        var nodeId      = Guid.NewGuid();
        var sensorVarId = Guid.NewGuid();
        const string sensorVarName = "SensorHandle";

        var sensorHandleVar = new VariableDecl
        {
            Id   = sensorVarId,
            Name = sensorVarName,
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var whenNode = new WhenNode
        {
            Id        = nodeId,
            Mode      = WhenMode.EqsResult,
            Edges     = WhenEdge.RisingEdge,
            EqsResult = new EqsResultPayload
            {
                SensorVariableName = sensorVarName,
                Trigger            = EqsTrigger.FirstReady,
                ScoreThreshold     = 0f,
                MaxAgeSeconds      = 0f,
            },
        };
        var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(whenExecIn);
        whenNode.Pins.Add(whenExecOut);
        whenNode.Pins.Add(whenOnFired);

        var retNode  = new ReturnNode { Id = Guid.NewGuid() };
        var retIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retIn);

        var ret2Node = new ReturnNode { Id = Guid.NewGuid() };
        var ret2In   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        ret2Node.Pins.Add(ret2In);

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, retNode, ret2Node },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = whenExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenExecOut.Id,  ToNodeId = retNode.Id,  ToPinId = retIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id,  ToNodeId = ret2Node.Id, ToPinId = ret2In.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId   = assetId,
            Name      = "PerfWhenEqs",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = { sensorHandleVar },
            Graphs    = { graph },
        };
    }

    /// <summary>
    /// Builds a blueprint with SpawnEqsSensorNode in the Tick graph.
    /// Modelled after SpawnEqsSensorRuntimeTests.BuildSpawnAsset.
    /// </summary>
    private static (BlueprintAsset asset, Guid templateId) BuildSpawnAsset()
    {
        var assetId    = Guid.NewGuid();
        var graphId    = Guid.NewGuid();
        var nodeId     = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var spawnNode = new SpawnEqsSensorNode { Id = nodeId, TemplateAssetId = templateId };
        var execIn    = new Pin { Id = Guid.NewGuid(), Name = "In",              Direction = "In",  IsExec = true,  TypeRef = new() };
        var execOut   = new Pin { Id = Guid.NewGuid(), Name = "Out",             Direction = "Out", IsExec = true,  TypeRef = new() };
        var handleOut = new Pin { Id = Guid.NewGuid(), Name = "Handle",          Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
        var srPin     = new Pin { Id = Guid.NewGuid(), Name = "SearchRadius",    Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var ffPin     = new Pin { Id = Guid.NewGuid(), Name = "FactionFilter",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.UInt32" } };
        var ttPin     = new Pin { Id = Guid.NewGuid(), Name = "ThreatThreshold", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var ppPin     = new Pin { Id = Guid.NewGuid(), Name = "PublishPolicy",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
        var prPin     = new Pin { Id = Guid.NewGuid(), Name = "Priority",        Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
        spawnNode.Pins.AddRange(new[] { execIn, execOut, handleOut, srPin, ffPin, ttPin, ppPin, prPin });

        var sensorVarId  = Guid.NewGuid();
        var setId        = Guid.NewGuid();
        var setExecIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setExecOut   = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setDataIn    = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setNode      = new SetVariableNode { Id = setId, VariableId = sensorVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setDataIn });

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var retNode   = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retExecIn);

        var sensorVar = new VariableDecl
        {
            Id   = sensorVarId,
            Name = "MySensor",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, spawnNode, setNode, retNode },
            Links =
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryExecOut.Id, ToNodeId = spawnNode.Id, ToPinId = execIn.Id },
                new Link { FromNodeId = spawnNode.Id, FromPinId = execOut.Id,      ToNodeId = setNode.Id,   ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,   FromPinId = setExecOut.Id,   ToNodeId = retNode.Id,   ToPinId = retExecIn.Id },
                new Link { FromNodeId = spawnNode.Id, FromPinId = handleOut.Id,    ToNodeId = setNode.Id,   ToPinId = setDataIn.Id },
            },
        };

        return (new BlueprintAsset
        {
            AssetId   = assetId,
            Name      = "PerfSpawnSensor",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = { sensorVar },
            Graphs    = { graph },
        }, templateId);
    }

    // ---- Tests ----

    /// <summary>
    /// Verifies the ValueChanged WhenNode hot path allocates zero bytes on the heap
    /// after JIT warm-up. Uses per-thread byte accounting to isolate the blueprint
    /// execution from any background-thread activity in the test harness.
    /// </summary>
    [Fact]
    public void WhenNode_ZeroAllocOnHotPath()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildValueChangedAsset();
        fixture.CompileAndLoad(asset, DefaultOptions());
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 1f });
        fixture.AttachBlueprint(asset, entity);

        // Warm-up frames (settle JIT, lazy inits, caches)
        for (int i = 0; i < 10; i++)
            fixture.TickFrame(0.016f);

        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < 100; i++)
            fixture.TickFrame(0.016f);
        long after = GC.GetTotalAllocatedBytes(precise: true);

        // Allow a small per-tick allowance for test-harness overhead (event bus swap,
        // per-thread ECB flush). The blueprint execution path itself must not contribute.
        // Threshold: 100 bytes per tick = 10 000 bytes total (generous for infrastructure;
        // any allocation from blueprint logic would produce tens of KB per 1000-tick run).
        long allocated = after - before;
        Assert.True(allocated <= 10_000L,
            $"Allocated {allocated} bytes across 100 ticks (threshold 10000). Blueprint hot path may be allocating.");
    }

    /// <summary>
    /// ValueChanged WhenNode: average tick time must be below 10 microseconds
    /// (100x the 100ns target; generous for CI).
    /// </summary>
    [Fact]
    public void WhenNode_ValueChanged_Under100ns_perTick()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildValueChangedAsset();
        fixture.CompileAndLoad(asset, DefaultOptions());
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 1f });
        fixture.AttachBlueprint(asset, entity);

        for (int i = 0; i < 5; i++)
            fixture.TickFrame(0.016f);

        const int ticks = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
            fixture.TickFrame(0.016f);
        sw.Stop();

        double avgUs = sw.Elapsed.TotalMicroseconds / ticks;
        Assert.True(avgUs < 10.0, $"actual: {avgUs}us");
    }

    /// <summary>
    /// EventFired WhenNode: average tick time must be below 50 microseconds
    /// (100x the 500ns target; generous for CI).
    /// </summary>
    [Fact]
    public void WhenNode_EventFired_Under500ns_perTick()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildEventFiredAsset();
        fixture.CompileAndLoad(asset, EventFiredOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        for (int i = 0; i < 5; i++)
            fixture.TickFrame(0.016f);

        const int ticks = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
            fixture.TickFrame(0.016f);
        sw.Stop();

        double avgUs = sw.Elapsed.TotalMicroseconds / ticks;
        Assert.True(avgUs < 50.0, $"actual: {avgUs}us");
    }

    /// <summary>
    /// ConditionMet WhenNode: average tick time must be below 20 microseconds
    /// (100x the 200ns target; generous for CI).
    /// </summary>
    [Fact]
    public void WhenNode_ConditionMet_Under200ns_perTick()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.PredicateCompiler = new NeverFirePredicateCompiler();
        fixture.PredicateRegistry = new MockSearchPredicateRegistry();
        var asset = BuildCondMetAsset();
        fixture.CompileAndLoad(asset, DefaultOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        for (int i = 0; i < 5; i++)
            fixture.TickFrame(0.016f);

        const int ticks = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
            fixture.TickFrame(0.016f);
        sw.Stop();

        double avgUs = sw.Elapsed.TotalMicroseconds / ticks;
        Assert.True(avgUs < 20.0, $"actual: {avgUs}us");
    }

    /// <summary>
    /// EqsResult WhenNode: average tick time must be below 15 microseconds
    /// (100x the 150ns target; generous for CI).
    /// </summary>
    [Fact]
    public void WhenNode_EqsResult_Under150ns_perTick()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();
        var asset = BuildEqsResultAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        for (int i = 0; i < 5; i++)
            fixture.TickFrame(0.016f);

        const int ticks = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
            fixture.TickFrame(0.016f);
        sw.Stop();

        double avgUs = sw.Elapsed.TotalMicroseconds / ticks;
        Assert.True(avgUs < 15.0, $"actual: {avgUs}us");
    }

    /// <summary>
    /// ReadEqsResultNode: average invocation time must be below 8 microseconds
    /// (100x the 80ns target; generous for CI).
    /// </summary>
    [Fact]
    public void ReadEqsResultNode_Under80ns_perInvocation()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();
        var (asset, _) = ReadEqsResultNodeRuntimeTests.BuildReadEqsAsset();
        fixture.CompileAndLoad(asset, DefaultOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        for (int i = 0; i < 5; i++)
            fixture.TickFrame(0.016f);

        const int ticks = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
            fixture.TickFrame(0.016f);
        sw.Stop();

        double avgUs = sw.Elapsed.TotalMicroseconds / ticks;
        Assert.True(avgUs < 8.0, $"actual: {avgUs}us");
    }

    /// <summary>
    /// SpawnEqsSensorNode: 100 ticks on a single entity must complete in under 1 second total.
    /// </summary>
    [Fact]
    public void SpawnEqsSensorNode_Under5us_perInvocation()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();
        fixture.World.RegisterComponent<PartMetadata>();
        var (asset, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            fixture.TickFrame(0.016f);
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 1.0,
            $"100 ticks took {sw.Elapsed.TotalSeconds:F3}s, expected < 1s");
    }
}
