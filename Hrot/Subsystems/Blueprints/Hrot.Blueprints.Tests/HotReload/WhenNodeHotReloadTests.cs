using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Mocks;
using Xunit;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Hot-reload tests for WhenNode per DESIGN §15.7.
/// Batch 06 scope: EditWhenNodePredicate_SoftReload_DelegateRecompiled.
/// </summary>
[Collection("DebugProbe")]
public sealed class WhenNodeHotReloadTests
{
    // ---- Mocks ----

    private sealed class MockPredicateCompiler : IPredicateCompiler
    {
        public int CompileCallCount { get; private set; }

        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root)
        {
            CompileCallCount++;
            return (_, _) => true;
        }

        public System.Collections.Generic.IReadOnlyList<Type> ExtractMandatoryComponents(
            SearchPredicateDto root)
            => Array.Empty<Type>();
    }

    private sealed class MockSearchPredicateRegistry : ISearchPredicateRegistry { }

    /// <summary>
    /// Mock that throws InvalidOperationException from CompileComponentPredicate,
    /// simulating a broken predicate after a hot reload.
    /// </summary>
    private sealed class ThrowingPredicateCompiler : IPredicateCompiler
    {
        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root)
            => throw new InvalidOperationException("Simulated bad predicate after reload.");

        public System.Collections.Generic.IReadOnlyList<Type> ExtractMandatoryComponents(
            SearchPredicateDto root)
            => Array.Empty<Type>();
    }

    // ---- Asset builder ----

    /// <summary>
    /// Builds a minimal Instance blueprint containing one ConditionMet WhenNode.
    /// The same assetId + nodeId across two calls yields the same StructureHash
    /// (same synthesized-field name), so the second load is a Soft Reload.
    /// </summary>
    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static BlueprintAsset BuildCondMetAsset(Guid assetId, Guid nodeId, double minValue = 10.0)
    {
        var graphId = Guid.NewGuid();

        // Entry node
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut",
                                     Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        // WhenNode in ConditionMet mode
        var whenNode = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.ConditionMet,
            Edges = WhenEdge.RisingEdge,
            ConditionMet = new ConditionMetPayload
            {
                Condition = new PropertyMatchDto
                {
                    ComponentType = typeof(object), // passes BP2009 (non-null)
                    PropertyPath  = "Value",
                    Predicate     = new NumericPredicateDto
                    {
                        MinValue = minValue,
                        MaxValue = double.MaxValue,
                    },
                },
            },
        };
        var whenExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var whenExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };
        var whenOnFired = new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() };
        whenNode.Pins.Add(whenExecIn);
        whenNode.Pins.Add(whenExecOut);
        whenNode.Pins.Add(whenOnFired);

        // Return node
        var retNode    = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        var ret2Node   = new ReturnNode { Id = Guid.NewGuid() };
        var ret2ExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retExecIn);
        ret2Node.Pins.Add(ret2ExecIn);

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, retNode, ret2Node },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id,
                           ToNodeId   = whenNode.Id, ToPinId   = whenExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenExecOut.Id,
                           ToNodeId   = retNode.Id,  ToPinId   = retExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = whenOnFired.Id,
                           ToNodeId   = ret2Node.Id, ToPinId   = ret2ExecIn.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "WhenCondMet",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    // ---- Tests ----

    /// <summary>
    /// Verifies that when the same blueprint (same assetId + nodeId = same StructureHash)
    /// is loaded a second time with a different predicate, InitializePredicates is called
    /// again and the delegate is recompiled (Soft Reload path per DESIGN §10.4 / §15.7).
    /// </summary>
    [Fact]
    public void EditWhenNodePredicate_SoftReload_DelegateRecompiled()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        EditWhenNodePredicate_SoftReload_DelegateRecompiled_Body(out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
        Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed after 50 retries.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EditWhenNodePredicate_SoftReload_DelegateRecompiled_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var mockCompiler = new MockPredicateCompiler();
        fixture.PredicateCompiler = mockCompiler;
        fixture.PredicateRegistry = new MockSearchPredicateRegistry();

        // Same assetId + nodeId guarantees the same StructureHash across both loads
        // (same synthesized field name _when_<id8>_prev).
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        var bpHash  = BlueprintIdHash.Compute(assetId);

        // --- Load v1 (predicate MinValue = 10.0) ---
        var assetV1 = BuildCondMetAsset(assetId, nodeId, minValue: 10.0);
        fixture.CompileAndLoad(assetV1, DefaultOptions());

        Assert.True(fixture.Registry.TryGetById(bpHash, out var defV1),
            "Blueprint not registered after first load.");
        var structHashV1 = defV1!.StructureHash;
        Assert.Equal(1, mockCompiler.CompileCallCount);

        // --- Load v2 (predicate MinValue = 20.0 --- Soft Reload: same StructureHash) ---
        var assetV2 = BuildCondMetAsset(assetId, nodeId, minValue: 20.0);
        fixture.CompileAndLoad(assetV2, DefaultOptions());

        Assert.True(fixture.Registry.TryGetById(bpHash, out var defV2),
            "Blueprint not registered after second load.");
        var structHashV2 = defV2!.StructureHash;

        // StructureHash unchanged -> Soft Reload (predicate edit does not change field layout).
        Assert.Equal(structHashV1, structHashV2);

        // InitializePredicates was called a second time -> delegate recompiled.
        Assert.Equal(2, mockCompiler.CompileCallCount);

        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    // ======================== ConditionMet Degraded-Mode Tests ========================

    /// <summary>
    /// After a hot reload where the predicate compile throws,
    /// InitializePredicates silently nulls the delegate.
    /// The WhenNode no-ops on the next tick without crashing.
    /// Per DESIGN section 15.7 and M3-T3.
    /// </summary>
    [Fact]
    public void BadPredicateAfterReload_DegradedMode_NoCrash()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        BadPredicateAfterReload_DegradedMode_NoCrash_Body(out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
        Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed after 50 retries.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BadPredicateAfterReload_DegradedMode_NoCrash_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // --- Load v1: valid compiler ---
        var mockCompiler = new MockPredicateCompiler(); // returns (_, _) => true
        fixture.PredicateCompiler = mockCompiler;
        fixture.PredicateRegistry = new MockSearchPredicateRegistry();

        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var assetV1 = BuildCondMetAsset(assetId, nodeId, minValue: 10.0);
        var bpHash  = BlueprintIdHash.Compute(assetId);

        fixture.CompileAndLoad(assetV1, DefaultOptions());

        // Create entity with AnotherTestComponent (to satisfy the delegate's potential reads)
        // and attach the blueprint.
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new AnotherTestComponent { X = 10.0f });
        fixture.AttachBlueprint(assetV1, entity);

        // Tick 1: delegate is valid -> ConditionMet check runs (no crash)
        fixture.TickFrame(0.016f);
        Assert.Equal(1, mockCompiler.CompileCallCount); // InitializePredicates called once

        // --- Load v2: throwing compiler (same StructureHash = Soft Reload) ---
        fixture.PredicateCompiler = new ThrowingPredicateCompiler();
        var assetV2 = BuildCondMetAsset(assetId, nodeId, minValue: 10.0); // same structure
        fixture.CompileAndLoad(assetV2, DefaultOptions());

        // Verify delegate is null in the freshly loaded assembly
        Assert.True(fixture.Registry.TryGetById(bpHash, out var defV2));
        var currentAlc = fixture.GetCurrentAlc();
        var v2Assembly = currentAlc!.Assemblies
            .First(a => a.GetName().Name!.StartsWith("Bp_"));
        var v2Type = v2Assembly.GetTypes()
            .FirstOrDefault(t => t.Name.Contains("_Bp") && !t.Name.Contains("Registrar"));
        Assert.NotNull(v2Type);
        var predField = v2Type!.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(f => f.Name.StartsWith("_whenCondPred_"));
        Assert.NotNull(predField);
        Assert.Null(predField!.GetValue(null)); // degraded: exception in compile -> null

        // Tick 2: delegate is null -> null check in generated code skips block -> no crash
        fixture.TickFrame(0.016f); // Must not throw

        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    // ======================== M9-T3: Hot-reload battery helpers ========================

    private sealed class AlwaysContainsCatalog : IEqsTemplateCatalog
    {
        public bool Contains(Guid assetId) => true;
    }

    private static CompileOptions MakeEqsOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>(),
        EqsTemplates:      new AlwaysContainsCatalog());

    private static ulong GetStructureHash(BlueprintTestFixture fixture, Guid assetId)
    {
        var hash = BlueprintIdHash.Compute(assetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            $"Blueprint definition not found for asset {assetId}");
        return def!.StructureHash;
    }

    /// <summary>Minimal Instance blueprint: Entry → Return (no WhenNode).</summary>
    private static BlueprintAsset BuildSimpleAsset(Guid assetId)
    {
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var retNode   = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retExecIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, retNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = entryExecOut.Id,
                                 ToNodeId = retNode.Id, ToPinId = retExecIn.Id } },
        };
        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SimpleTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// Instance blueprint with a WhenNode in ValueChanged mode watching
    /// AnotherTestComponent.<paramref name="propertyPath"/> (float).
    /// Synthesizes a float state field per DESIGN §15.3 → contributes to StructureHash.
    /// </summary>
    private static BlueprintAsset BuildValueChangedAsset(
        Guid assetId, Guid nodeId, string propertyPath = "X")
    {
        var whenNode = new WhenNode
        {
            Id   = nodeId,
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent",
                PropertyPath    = propertyPath,
                Source          = ValueChangedSource.SelfComponent,
                Epsilon         = 0f,
            },
        };
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var ret  = new ReturnNode { Id = Guid.NewGuid() };
        var ret2 = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add( new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });
        ret2.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });

        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");
        var outPin     = whenNode.Pins.First(p => p.IsExec && p.Direction == "Out" && p.Name == "Out");
        var firedPin   = whenNode.Pins.First(p => p.Name == "OnFired");
        var retExecIn  = ret.Pins[0];
        var ret2ExecIn = ret2.Pins[0];

        var graph = new Graph
        {
            Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, ret, ret2 },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = execInPin.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = outPin.Id,       ToNodeId = ret.Id,      ToPinId = retExecIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = firedPin.Id,     ToNodeId = ret2.Id,     ToPinId = ret2ExecIn.Id },
            },
        };
        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "ValueChangedTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// Instance blueprint with a WhenNode in EqsResult mode.
    /// Synthesizes a trigger-specific state struct that contributes to StructureHash.
    /// </summary>
    private static BlueprintAsset BuildEqsResultAsset(
        Guid assetId, Guid nodeId, string sensorVarName, EqsTrigger trigger,
        VariableDecl? sensorVarDecl = null)
    {
        var whenNode = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.EqsResult,
            Edges = WhenEdge.RisingEdge,
            EqsResult = new EqsResultPayload
            {
                SensorVariableName = sensorVarName,
                Trigger            = trigger,
                ScoreThreshold     = 0f,
                MaxAgeSeconds      = 10f,
            },
        };
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
        whenNode.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var ret  = new ReturnNode { Id = Guid.NewGuid() };
        var ret2 = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add( new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });
        ret2.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });

        var execIn = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");
        var outPin = whenNode.Pins.First(p => p.Name == "Out");
        var fired  = whenNode.Pins.First(p => p.Name == "OnFired");

        var graph = new Graph
        {
            Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, whenNode, ret, ret2 },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = whenNode.Id, ToPinId = execIn.Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = outPin.Id,       ToNodeId = ret.Id,      ToPinId = ret.Pins[0].Id },
                new Link { FromNodeId = whenNode.Id, FromPinId = fired.Id,        ToNodeId = ret2.Id,     ToPinId = ret2.Pins[0].Id },
            },
        };
        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "EqsResultTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
        if (sensorVarDecl is not null)
            asset.Variables.Add(sensorVarDecl);
        return asset;
    }

    /// <summary>
    /// Simple blueprint (Entry → ReadEqsResultNode → Return) with the sensor variable declared.
    /// ReadEqsResultNode has zero state contribution, so StructureHash equals that of a plain
    /// simple blueprint with the same variable.
    /// </summary>
    private static BlueprintAsset BuildSimpleAssetWithReadEqsResult(
        Guid assetId, VariableDecl sensorVar)
    {
        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var readNode    = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = sensorVar.Name };
        var readExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",          Direction = "In",  IsExec = true,  TypeRef = new() };
        var readExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out",         Direction = "Out", IsExec = true,  TypeRef = new() };
        var handleIn    = new Pin { Id = Guid.NewGuid(), Name = "Handle",      Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
        var indexIn     = new Pin { Id = Guid.NewGuid(), Name = "ResultIndex", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } };
        readNode.Pins.AddRange(new[] { readExecIn, readExecOut, handleIn, indexIn });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, readNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryExecOut.Id, ToNodeId = readNode.Id, ToPinId = readExecIn.Id },
                new Link { FromNodeId = readNode.Id, FromPinId = readExecOut.Id,  ToNodeId = ret.Id,      ToPinId = retIn.Id },
            },
        };
        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "ReadEqsTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
        asset.Variables.Add(sensorVar);
        return asset;
    }

    /// <summary>
    /// Simple blueprint (Entry → SpawnEqsSensorNode → SetVariable → Return).
    /// SpawnEqsSensorNode has zero state contribution, so StructureHash equals that of a plain
    /// simple blueprint with the same variable.
    /// </summary>
    private static BlueprintAsset BuildSimpleAssetWithSpawnEqsSensor(
        Guid assetId, Guid templateId, VariableDecl sensorVar)
    {
        var spawnNode = new SpawnEqsSensorNode { Id = Guid.NewGuid(), TemplateAssetId = templateId };
        var execIn    = new Pin { Id = Guid.NewGuid(), Name = "In",              Direction = "In",  IsExec = true,  TypeRef = new() };
        var execOut   = new Pin { Id = Guid.NewGuid(), Name = "Out",             Direction = "Out", IsExec = true,  TypeRef = new() };
        var handleOut = new Pin { Id = Guid.NewGuid(), Name = "Handle",          Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
        var srPin     = new Pin { Id = Guid.NewGuid(), Name = "SearchRadius",    Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var ffPin     = new Pin { Id = Guid.NewGuid(), Name = "FactionFilter",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.UInt32" } };
        var ttPin     = new Pin { Id = Guid.NewGuid(), Name = "ThreatThreshold", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var ppPin     = new Pin { Id = Guid.NewGuid(), Name = "PublishPolicy",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
        var prPin     = new Pin { Id = Guid.NewGuid(), Name = "Priority",        Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
        spawnNode.Pins.AddRange(new[] { execIn, execOut, handleOut, srPin, ffPin, ttPin, ppPin, prPin });

        var setNode    = new SetVariableNode { Id = Guid.NewGuid(), VariableId = sensorVar.Id.ToString() };
        var setExecIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setDataIn  = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setDataIn });

        var entry        = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryExecOut);

        var retNode   = new ReturnNode { Id = Guid.NewGuid() };
        var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retExecIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, spawnNode, setNode, retNode },
            Links =
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryExecOut.Id, ToNodeId = spawnNode.Id, ToPinId = execIn.Id },
                new Link { FromNodeId = spawnNode.Id, FromPinId = execOut.Id,      ToNodeId = setNode.Id,   ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,   FromPinId = setExecOut.Id,   ToNodeId = retNode.Id,   ToPinId = retExecIn.Id },
                new Link { FromNodeId = spawnNode.Id, FromPinId = handleOut.Id,    ToNodeId = setNode.Id,   ToPinId = setDataIn.Id },
            },
        };
        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "SpawnSensorTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };
        asset.Variables.Add(sensorVar);
        return asset;
    }

    // ======================== M9-T3: 8 Hot-reload tests ========================

    /// <summary>
    /// Adding a ValueChanged WhenNode synthesizes a new float field in WorkingState
    /// → StructureHash changes → Hard Reload path. DESIGN §15.7.
    /// </summary>
    [Fact]
    public void AddWhenNode_TriggersHardReload()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        // v1: no WhenNode
        var assetV1 = BuildSimpleAsset(assetId);
        fixture.CompileAndLoad(assetV1, DefaultOptions());
        var hashV1 = GetStructureHash(fixture, assetId);

        // v2: add ValueChanged WhenNode (adds synthesized float field to WorkingState)
        var assetV2 = BuildValueChangedAsset(assetId, nodeId);
        fixture.CompileAndLoad(assetV2, DefaultOptions());
        var hashV2 = GetStructureHash(fixture, assetId);

        // StructureHash must differ -> Hard Reload path
        Assert.NotEqual(hashV1, hashV2);
    }

    /// <summary>
    /// Removing a ValueChanged WhenNode removes the synthesized float field
    /// → StructureHash changes → Hard Reload path.
    /// </summary>
    [Fact]
    public void RemoveWhenNode_TriggersHardReload()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        // v1: has ValueChanged WhenNode
        var assetV1 = BuildValueChangedAsset(assetId, nodeId);
        fixture.CompileAndLoad(assetV1, DefaultOptions());
        var hashV1 = GetStructureHash(fixture, assetId);

        // v2: remove WhenNode (simple entry → return)
        var assetV2 = BuildSimpleAsset(assetId);
        fixture.CompileAndLoad(assetV2, DefaultOptions());
        var hashV2 = GetStructureHash(fixture, assetId);

        Assert.NotEqual(hashV1, hashV2);
    }

    /// <summary>
    /// Switching the same WhenNode from ConditionMet mode (no synthesized state) to
    /// ValueChanged mode (synthesized float field) changes the state layout
    /// → StructureHash changes → Hard Reload path.
    /// </summary>
    [Fact]
    public void EditWhenNodeMode_HardReload()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        // v1: ConditionMet mode — no synthesized WorkingState fields
        var assetV1 = BuildCondMetAsset(assetId, nodeId);
        fixture.CompileAndLoad(assetV1, DefaultOptions());
        var hashV1 = GetStructureHash(fixture, assetId);

        // v2: SAME nodeId but ValueChanged mode — adds synthesized float field to WorkingState
        var assetV2 = BuildValueChangedAsset(assetId, nodeId);
        fixture.CompileAndLoad(assetV2, DefaultOptions());
        var hashV2 = GetStructureHash(fixture, assetId);

        Assert.NotEqual(hashV1, hashV2);
    }

    /// <summary>
    /// Changing the watched property from X to Y on the same ValueChanged WhenNode node
    /// keeps the same synthesized field type (float → float) → same StructureHash → Soft Reload.
    /// </summary>
    [Fact]
    public void ValueChangedFieldType_Soft_PreservesPrev()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        // v1: ValueChanged watching AnotherTestComponent.X (float)
        var assetV1 = BuildValueChangedAsset(assetId, nodeId, propertyPath: "X");
        fixture.CompileAndLoad(assetV1, DefaultOptions());
        var hashV1 = GetStructureHash(fixture, assetId);

        // v2: SAME nodeId, ValueChanged watching AnotherTestComponent.Y (also float)
        // Same synthesized field type (float) → same WorkingState layout → same StructureHash
        var assetV2 = BuildValueChangedAsset(assetId, nodeId, propertyPath: "Y");
        fixture.CompileAndLoad(assetV2, DefaultOptions());
        var hashV2 = GetStructureHash(fixture, assetId);

        // Soft Reload: same structure (field type unchanged)
        Assert.Equal(hashV1, hashV2);
    }

    /// <summary>
    /// Switching EqsResult trigger from TopChanged (larger struct) to FirstReady (smaller struct)
    /// changes the synthesized state struct size → different StructureHash → Hard Reload.
    /// </summary>
    [Fact]
    public void EqsTriggerChange_HardReload()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var sensorVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Sensor",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };

        // v1: EqsResult(TopChanged) — TopChanged synthesized struct (~16 bytes)
        var assetV1 = BuildEqsResultAsset(assetId, nodeId, "Sensor", EqsTrigger.TopChanged, sensorVar);
        fixture.CompileAndLoad(assetV1, MakeEqsOptions());
        var hashV1 = GetStructureHash(fixture, assetId);

        // v2: SAME nodeId, EqsResult(FirstReady) — FirstReady synthesized struct (4 bytes)
        // Different struct size → different WorkingState layout → different StructureHash
        var assetV2 = BuildEqsResultAsset(assetId, nodeId, "Sensor", EqsTrigger.FirstReady, sensorVar);
        fixture.CompileAndLoad(assetV2, MakeEqsOptions());
        var hashV2 = GetStructureHash(fixture, assetId);

        Assert.NotEqual(hashV1, hashV2);
    }

    /// <summary>
    /// Adding a ReadEqsResultNode to a blueprint that already declares the sensor variable
    /// adds only pure code (no state fields) → StructureHash unchanged → Soft Reload.
    /// Per DESIGN Lower_ZeroStateContribution.
    /// </summary>
    [Fact]
    public void AddReadEqsResultNode_SoftReload()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        var assetId     = Guid.NewGuid();
        var sensorVarId = Guid.NewGuid();
        var sensorVar   = new VariableDecl
        {
            Id   = sensorVarId,
            Name = "MySensor",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };

        // v1: simple blueprint with sensor variable declared but no ReadEqsResultNode
        var assetV1 = BuildSimpleAsset(assetId);
        assetV1.Variables.Add(sensorVar);
        fixture.CompileAndLoad(assetV1, MakeEqsOptions());
        var hashV1 = GetStructureHash(fixture, assetId);

        // v2: same variable + ReadEqsResultNode (no new state fields)
        var assetV2 = BuildSimpleAssetWithReadEqsResult(assetId, sensorVar);
        fixture.CompileAndLoad(assetV2, MakeEqsOptions());
        var hashV2 = GetStructureHash(fixture, assetId);

        // ReadEqsResultNode adds pure code, not state -> Soft Reload
        Assert.Equal(hashV1, hashV2);
    }

    /// <summary>
    /// Adding a SpawnEqsSensorNode to a blueprint that already declares the sensor variable
    /// adds only pure code (no state fields) → StructureHash unchanged → Soft Reload.
    /// Per DESIGN Lower_ZeroStateContribution.
    /// </summary>
    [Fact]
    public void AddSpawnEqsSensorNode_SoftReload()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();
        fixture.World.RegisterComponent<PartMetadata>();
        var assetId     = Guid.NewGuid();
        var sensorVarId = Guid.NewGuid();
        var sensorVar   = new VariableDecl
        {
            Id   = sensorVarId,
            Name = "MySensor",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };

        // v1: simple blueprint with sensor variable declared
        var assetV1 = BuildSimpleAsset(assetId);
        assetV1.Variables.Add(sensorVar);
        fixture.CompileAndLoad(assetV1, MakeEqsOptions());
        var hashV1 = GetStructureHash(fixture, assetId);

        // v2: same variable + SpawnEqsSensorNode that writes to MySensor
        // SpawnEqsSensorNode has zero state contribution per DESIGN Lower_ZeroStateContribution.
        var templateId = Guid.NewGuid();
        var assetV2 = BuildSimpleAssetWithSpawnEqsSensor(assetId, templateId, sensorVar);
        fixture.CompileAndLoad(assetV2, MakeEqsOptions());
        var hashV2 = GetStructureHash(fixture, assetId);

        Assert.Equal(hashV1, hashV2);
    }

    /// <summary>
    /// Changing the EQS template asset ID on a SpawnEqsSensorNode only changes an emitted
    /// constant in the code, not the state struct layout → StructureHash unchanged → Soft Reload.
    /// </summary>
    [Fact]
    public void EditSpawnTemplate_SoftReload_PreservesHandle()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();
        fixture.World.RegisterComponent<PartMetadata>();
        var assetId     = Guid.NewGuid();
        var sensorVarId = Guid.NewGuid();
        var sensorVar   = new VariableDecl
        {
            Id   = sensorVarId,
            Name = "MySensor",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };

        // v1: SpawnEqsSensor with template A
        var templateA = Guid.NewGuid();
        var assetV1 = BuildSimpleAssetWithSpawnEqsSensor(assetId, templateA, sensorVar);
        fixture.CompileAndLoad(assetV1, MakeEqsOptions());
        var hashV1 = GetStructureHash(fixture, assetId);

        // v2: SpawnEqsSensor with template B (same variable, same node structure)
        // Only the emitted blueprint ID constant changes -> no state change -> Soft Reload
        var templateB = Guid.NewGuid();
        var assetV2 = BuildSimpleAssetWithSpawnEqsSensor(assetId, templateB, sensorVar);
        fixture.CompileAndLoad(assetV2, MakeEqsOptions());
        var hashV2 = GetStructureHash(fixture, assetId);

        // Same structure hash -> Soft Reload path
        Assert.Equal(hashV1, hashV2);
    }
}
