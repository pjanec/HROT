using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

[Collection("DebugProbe")]
public sealed class SpawnEqsSensorRuntimeTests
{
    // ---- EQS template catalog stub ----

    private sealed class AlwaysContainsCatalog : IEqsTemplateCatalog
    {
        public bool Contains(Guid assetId) => true;
    }

    // ---- Helpers ----

    private static CompileOptions MakeEqsOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>(),
        EqsTemplates:      new AlwaysContainsCatalog());

    private static void RegisterEqsComponents(BlueprintTestFixture fixture)
    {
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();
        fixture.World.RegisterComponent<PartMetadata>();
    }

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

    /// <summary>
    /// Collects all entities with component T into a list.
    /// </summary>
    private static List<Entity> QueryEntities<T>(BlueprintTestFixture fixture)
        where T : unmanaged
    {
        var result = new List<Entity>();
        fixture.World.Query().With<T>().Build().ForEach(e => result.Add(e));
        return result;
    }

    // ---- Asset builder ----

    /// <summary>
    /// Builds a blueprint with a SpawnEqsSensorNode in the Tick graph.
    /// The node's Handle output is stored in a "MySensor" variable.
    /// </summary>
    private static (BlueprintAsset asset, Guid templateId, Guid nodeId) BuildSpawnAsset()
    {
        var assetId    = Guid.NewGuid();
        var graphId    = Guid.NewGuid();
        var nodeId     = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        // SpawnEqsSensorNode
        var spawnNode = new SpawnEqsSensorNode { Id = nodeId, TemplateAssetId = templateId };
        var execIn    = new Pin { Id = Guid.NewGuid(), Name = "In",             Direction = "In",  IsExec = true,  TypeRef = new() };
        var execOut   = new Pin { Id = Guid.NewGuid(), Name = "Out",            Direction = "Out", IsExec = true,  TypeRef = new() };
        var handleOut = new Pin { Id = Guid.NewGuid(), Name = "Handle",         Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
        var srPin     = new Pin { Id = Guid.NewGuid(), Name = "SearchRadius",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var ffPin     = new Pin { Id = Guid.NewGuid(), Name = "FactionFilter",  Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.UInt32" } };
        var ttPin     = new Pin { Id = Guid.NewGuid(), Name = "ThreatThreshold",Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var ppPin     = new Pin { Id = Guid.NewGuid(), Name = "PublishPolicy",  Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
        var prPin     = new Pin { Id = Guid.NewGuid(), Name = "Priority",       Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte" } };
        spawnNode.Pins.AddRange(new[] { execIn, execOut, handleOut, srPin, ffPin, ttPin, ppPin, prPin });

        // SetVariable(MySensor = Handle)
        var sensorVarId  = Guid.NewGuid();
        var setId        = Guid.NewGuid();
        var setExecIn    = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var setExecOut   = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() };
        var setDataIn    = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() };
        var setNode      = new SetVariableNode { Id = setId, VariableId = sensorVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setDataIn });

        // Entry + return
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
            Name      = "SpawnSensorTest",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Variables = { sensorVar },
            Graphs    = { graph },
        }, templateId, nodeId);
    }

    /// <summary>
    /// Builds a blueprint where SearchRadius is wired from a literal 5.0f node.
    /// Returns (asset, radiusVarId, nodeId) where radiusVarId is unused (Guid.Empty).
    /// </summary>
    private static (BlueprintAsset asset, Guid radiusVarId, Guid nodeId) BuildSpawnAssetWithWiredRadius()
    {
        var assetId    = Guid.NewGuid();
        var graphId    = Guid.NewGuid();
        var nodeId     = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var spawnNode = new SpawnEqsSensorNode { Id = nodeId, TemplateAssetId = templateId };
        var execIn    = new Pin { Id = Guid.NewGuid(), Name = "In",           Direction = "In",  IsExec = true,  TypeRef = new() };
        var execOut   = new Pin { Id = Guid.NewGuid(), Name = "Out",          Direction = "Out", IsExec = true,  TypeRef = new() };
        var srPin     = new Pin { Id = Guid.NewGuid(), Name = "SearchRadius", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var handleOut = new Pin { Id = Guid.NewGuid(), Name = "Handle",       Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } };
        spawnNode.Pins.AddRange(new[] { execIn, execOut, srPin, handleOut });

        // LiteralNode provides SearchRadius = 5.0f
        var litNode = new LiteralNode { Id = Guid.NewGuid(), ValueJson = "5" };
        var litOut  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        litNode.Pins.Add(litOut);

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);

        var retNode = new ReturnNode { Id = Guid.NewGuid() };
        var retIn   = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        retNode.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, litNode, spawnNode, retNode },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id, ToNodeId = spawnNode.Id, ToPinId = execIn.Id },
                new Link { FromNodeId = spawnNode.Id, FromPinId = execOut.Id, ToNodeId = retNode.Id, ToPinId = retIn.Id },
                new Link { FromNodeId = litNode.Id, FromPinId = litOut.Id,  ToNodeId = spawnNode.Id, ToPinId = srPin.Id },
            },
        };

        return (new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "WiredRadiusTest",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        }, Guid.Empty, nodeId);
    }

    // ---- Tests ----

    [Fact]
    public void Spawn_CreatesChildEntity()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        // After tick (ECB played back), the child entity should have PartMetadata
        var childEntities = QueryEntities<PartMetadata>(fixture);
        Assert.NotEmpty(childEntities);
    }

    [Fact]
    public void Spawn_AttachesPartMetadata_WithParent()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var parentEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, parentEntity);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<PartMetadata>(fixture);
        Assert.True(childEntities.Count >= 1);
        var meta = fixture.World.GetComponentRO<PartMetadata>(childEntities[0]);
        Assert.Equal(parentEntity, meta.ParentEntity);
    }

    [Fact]
    public void Spawn_AttachesEqsSensor_WithCorrectTemplate()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, templateId, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<EqsSensor>(fixture);
        Assert.True(childEntities.Count >= 1);
        var sensor = fixture.World.GetComponentRO<EqsSensor>(childEntities[0]);
        uint expectedBpId = (uint)BlueprintIdHash.Compute(templateId);
        Assert.Equal(expectedBpId, sensor.BlueprintId);
    }

    [Fact]
    public void Spawn_AttachesCognitiveBuffer_ZeroInit()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<EqsCognitiveBuffer>(fixture);
        Assert.True(childEntities.Count >= 1);
        var buffer = fixture.World.GetComponentRO<EqsCognitiveBuffer>(childEntities[0]);
        Assert.False(buffer.IsReady, "Buffer should start not-ready");
    }

    [Fact]
    public void Spawn_PopulatesHandleOutput()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        // After tick, the MySensor variable should hold a valid handle
        var handle = ReadSlotField<EqsSensorHandle>(fixture, asset, entity, "MySensor");
        Assert.True(handle.IsValid, "MySensor handle should point to a valid entity");
        Assert.True(fixture.World.IsAlive(handle.ChildId), "Handle's ChildId should be alive");
    }

    [Fact]
    public void Spawn_EmitsEqsSensor_WithEpochOne()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<EqsSensor>(fixture);
        Assert.True(childEntities.Count >= 1);
        var sensor = fixture.World.GetComponentRO<EqsSensor>(childEntities[0]);
        Assert.Equal(1u, sensor.Epoch);
    }

    [Fact]
    public void Spawn_AllFiveFields_HaveExpectedDefaults()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<EqsSensor>(fixture);
        var sensor = fixture.World.GetComponentRO<EqsSensor>(childEntities[0]);
        Assert.Equal(0f,      sensor.SearchRadius);
        Assert.Equal(0u,      sensor.FactionFilter);
        Assert.Equal(0f,      sensor.ThreatThreshold);
        Assert.Equal((byte)0, sensor.PublishPolicy);
        Assert.Equal((byte)0, sensor.Priority);
    }

    [Fact]
    public void Spawn_PartMetadataInstanceId_IsNonZero()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, nodeId) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<PartMetadata>(fixture);
        var meta = fixture.World.GetComponentRO<PartMetadata>(childEntities[0]);
        // InstanceId is derived from BlueprintIdHash.Compute(nodeId) baked at compile time
        int expectedId = (int)BlueprintIdHash.Compute(nodeId);
        Assert.Equal(expectedId, meta.InstanceId);
    }

    [Fact]
    public void Spawn_MultipleInvocations_CreateDistinctEntities()
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());

        // Spawn from two parent entities
        var entity1 = fixture.CreateEntity(); fixture.AttachBlueprint(asset, entity1);
        var entity2 = fixture.CreateEntity(); fixture.AttachBlueprint(asset, entity2);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<EqsSensor>(fixture);
        Assert.True(childEntities.Count >= 2, "Two parents should produce two child sensor entities");
        Assert.NotEqual(childEntities[0], childEntities[1]);
    }

    [Fact]
    public void Spawn_LiteralParameters_AppliedCorrectly()
    {
        // When all pins are unconnected (no wired data), SearchRadius defaults to 0f.
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAsset();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<EqsSensor>(fixture);
        Assert.NotEmpty(childEntities);
        var sensor = fixture.World.GetComponentRO<EqsSensor>(childEntities[0]);
        Assert.Equal(0f, sensor.SearchRadius);
    }

    [Fact]
    public void Spawn_WiredParameters_ReadFromExpression()
    {
        // When a literal 5.0f node is wired to SearchRadius, the spawned sensor should have
        // SearchRadius == 5.0f (the upstream expression value is used, not a default).
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);
        var (asset, _, _) = BuildSpawnAssetWithWiredRadius();
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var childEntities = QueryEntities<EqsSensor>(fixture);
        Assert.NotEmpty(childEntities);
        var sensor = fixture.World.GetComponentRO<EqsSensor>(childEntities[0]);
        Assert.Equal(5.0f, sensor.SearchRadius);
    }
}
