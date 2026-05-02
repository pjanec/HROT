using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.Map.Common.Tests.Replication.Egress;

/// <summary>
/// Unit tests for <see cref="EntityInfoEgressTranslator"/> covering CS010:
/// commander ID and tactical designation are read from <see cref="UnitSubordinate"/>
/// and published to the DDS <c>EntityInfo</c> sample.
/// </summary>
public sealed class EntityInfoEgressTranslatorTests
{
    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class CapturingWriter : IDdsWriter<Hrot.NED.Descriptors.EntityInfo>
    {
        public List<Hrot.NED.Descriptors.EntityInfo> Publishes { get; } = new();
        public void Write(Hrot.NED.Descriptors.EntityInfo sample) => Publishes.Add(sample);
        public void DisposeInstance(Hrot.NED.Descriptors.EntityInfo key) { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        ComponentTypeRegistry.Clear();
        var world = new EntityRepository();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<NetworkAuthority>();
        world.RegisterComponent<Fdp.Core.EntityInfo>();
        world.RegisterComponent<UnitSubordinate>();
        return world;
    }

    private static (EntityInfoEgressTranslator translator, CapturingWriter writer, NetworkEntityMap entityMap) CreateTranslator()
    {
        var writer    = new CapturingWriter();
        var entityMap = new NetworkEntityMap();
        var translator = new EntityInfoEgressTranslator(writer, entityMap, localNodeId: 1);
        return (translator, writer, entityMap);
    }

    private static Entity SpawnAuthoritativeEntity(EntityRepository world, uint netId, ForceId forceId = ForceId.Friend)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new NetworkIdentity(netId));
        world.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(e, new Fdp.Core.EntityInfo { Name = "TestUnit", ForceId = forceId });
        return e;
    }

    // ── CS010 Test 1 ─────────────────────────────────────────────────────────

    /// <summary>
    /// CS010 Test 1: An entity with a <see cref="UnitSubordinate"/> component whose
    /// commander is registered in <see cref="NetworkEntityMap"/> must produce a DDS
    /// sample with the correct <c>CommanderId</c> and <c>TacticalDesignation</c>.
    /// </summary>
    [Fact]
    public void UnitSubordinate_Present_CommanderIdAndDesignationPublished()
    {
        using var world = CreateWorld();
        var (translator, writer, entityMap) = CreateTranslator();

        // Spawn commander (net ID 10) and subordinate (net ID 20).
        var cmdEnt = SpawnAuthoritativeEntity(world, netId: 10);
        var subEnt = SpawnAuthoritativeEntity(world, netId: 20);

        entityMap.Register(10, cmdEnt);
        entityMap.Register(20, subEnt);

        // Assign subordinate under commander with a known designation.
        world.AddComponent(subEnt, new UnitSubordinate
        {
            Commander   = cmdEnt,
            Designation = TacticalDesignation.Wingman,
        });

        world.Tick();
        translator.ScanAndPublish(world);

        // The subordinate entity's EntityInfo DDS sample should carry CommanderId = 10
        // and TacticalDesignation = Wingman.
        Assert.Equal(2, writer.Publishes.Count); // both entities publish on first scan
        int subIdx = writer.Publishes.FindIndex(s => s.EntityId == 20);
        Assert.True(subIdx >= 0, "No DDS sample published for subordinate (EntityId=20).");
        var subSample = writer.Publishes[subIdx];
        Assert.Equal(10, subSample.CommanderId);
        Assert.Equal(eTacticalDesignation.Wingman, subSample.TacticalDesignation);
    }

    // ── CS010 Test 2 ─────────────────────────────────────────────────────────

    /// <summary>
    /// CS010 Test 2: An entity without a <see cref="UnitSubordinate"/> component
    /// must produce a DDS sample with <c>CommanderId == 0</c> and
    /// <c>TacticalDesignation == Undefined</c>.
    /// </summary>
    [Fact]
    public void NoUnitSubordinate_CommanderIdZeroAndDesignationUndefined()
    {
        using var world = CreateWorld();
        var (translator, writer, entityMap) = CreateTranslator();

        var entity = SpawnAuthoritativeEntity(world, netId: 42);
        entityMap.Register(42, entity);

        // No UnitSubordinate added.

        world.Tick();
        translator.ScanAndPublish(world);

        Assert.Single(writer.Publishes);
        Assert.Equal(0, writer.Publishes[0].CommanderId);
        Assert.Equal(eTacticalDesignation.Undefined, writer.Publishes[0].TacticalDesignation);
    }

    // ── CS010 Test 3 ─────────────────────────────────────────────────────────

    /// <summary>
    /// CS010 Test 3: When the entity has a <see cref="UnitSubordinate"/> but the
    /// commander entity is absent from <see cref="NetworkEntityMap"/>, the translator
    /// must publish <c>CommanderId == 0</c> without throwing.
    /// </summary>
    [Fact]
    public void CommanderNotInEntityMap_CommanderIdZeroNoException()
    {
        using var world = CreateWorld();
        var (translator, writer, entityMap) = CreateTranslator();

        // The commander entity exists in ECS but has no NetworkIdentity/EntityInfo,
        // so it is excluded from the ScanAndPublish query and will not be published.
        var cmdEnt = world.CreateEntity();

        var subEnt = SpawnAuthoritativeEntity(world, netId: 55);
        entityMap.Register(55, subEnt);

        world.AddComponent(subEnt, new UnitSubordinate
        {
            Commander   = cmdEnt,
            Designation = TacticalDesignation.Support,
        });

        world.Tick();
        var ex = Record.Exception(() => translator.ScanAndPublish(world));

        Assert.Null(ex);
        Assert.Single(writer.Publishes);
        Assert.Equal(0, writer.Publishes[0].CommanderId);
    }
}
