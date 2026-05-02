using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Scenario;
using Hrot.Common.Serializers;
using Hrot.Map.Common.Components;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests.Integration;

/// <summary>
/// CS025-T06 -- Genesis scenario serialization round-trip.
///
/// Verifies that after a save/reload cycle the <see cref="GenesisMaterializationSystem"/>
/// correctly reconstructs the commander/subordinate hierarchy from the deserialized
/// <see cref="InitialUnitSubordinateIntent"/>, regardless of entity creation order.
/// </summary>
public sealed class HierarchySerializationIntegrationTests : IDisposable
{
    private readonly EntityRepository _repo1;
    private readonly EntityRepository _repo2;

    // Register all components needed by GenesisMaterializationSystem in each repo.
    private static void RegisterAllComponents(EntityRepository repo)
    {
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<UnitRoster>();
        repo.RegisterComponent<UnitSubordinate>();
        repo.RegisterComponent<PassengerBuffer>();
        repo.RegisterComponent<IsEmbarkedTag>();
        repo.RegisterComponent<VisHierarchyNode>();
        repo.RegisterComponent<PersonalRouteRef>();
        repo.RegisterManagedComponent<InitialUnitSubordinateIntent>();
        repo.RegisterManagedComponent<InitialPassengersIntent>();
        repo.RegisterManagedComponent<InitialVehicleIntent>();
        repo.RegisterManagedComponent<InitialHierarchyIntent>();
        repo.RegisterManagedComponent<InitialRouteIntent>();
        repo.RegisterManagedComponent<InitialTargetsIntent>();
    }

    public HierarchySerializationIntegrationTests()
    {
        _repo1 = new EntityRepository();
        RegisterAllComponents(_repo1);

        _repo2 = new EntityRepository();
        RegisterAllComponents(_repo2);
    }

    public void Dispose()
    {
        _repo1.Dispose();
        _repo2.Dispose();
    }

    // CS025-T06
    [Fact]
    public void Serialize_ThenDeserialize_ReconstitutesHierarchy()
    {
        // -- Arrange: build source repo with commander + subordinate --

        const long commanderNetId = 42L;

        var commander = _repo1.CreateEntity();
        _repo1.SetComponent(commander, new NetworkIdentity { Value = commanderNetId });

        var subordinate = _repo1.CreateEntity();
        _repo1.SetComponent(subordinate, new UnitSubordinate
        {
            Commander   = commander,
            Designation = TacticalDesignation.Wingman,
        });

        // Build the serializer AFTER registering component types so FdpAutoSerializer
        // compiles delegates for all saveable types (including NetworkIdentity).
        var serializer = HrotScenarioSerializerFactory.Build(new BehaviorRegistry());

        // -- Act: serialize source repo to JSON, then deserialize into fresh repo --

        var dom  = serializer.Serialize(_repo1, new ScenarioHeader(HrotSubsystemTypes.Scenario));
        var json = dom.ToJsonString();

        serializer.Deserialize(_repo2, json);

        // -- Assert (post-deserialize): subordinate2 carries InitialUnitSubordinateIntent --

        // Find commander2 in repo2 by its NetworkIdentity value.
        Entity commander2 = Entity.Null;
        foreach (var entity in _repo2.Query().With<NetworkIdentity>().Build())
        {
            if (_repo2.GetComponent<NetworkIdentity>(entity).Value == commanderNetId)
            {
                commander2 = entity;
                break;
            }
        }

        Assert.False(commander2.IsNull, "Commander entity not found in repo2 after deserialization.");

        // Find subordinate2 -- it should have InitialUnitSubordinateIntent but no UnitSubordinate yet.
        Entity subordinate2 = Entity.Null;
        foreach (var entity in _repo2.Query().Build())
        {
            if (_repo2.HasManagedComponent<InitialUnitSubordinateIntent>(entity))
            {
                subordinate2 = entity;
                break;
            }
        }

        Assert.False(subordinate2.IsNull, "Subordinate entity with InitialUnitSubordinateIntent not found after deserialization.");

        var intent = ((ISimulationView)_repo2).GetManagedComponentRO<InitialUnitSubordinateIntent>(subordinate2);
        Assert.Equal(commanderNetId, intent.CommanderNetworkId);
        Assert.Equal(TacticalDesignation.Wingman, intent.Designation);

        // -- Act: register commander2 in entity map and run genesis system --

        var entityMap = new NetworkEntityMap();
        entityMap.Register(commanderNetId, commander2);

        new GenesisMaterializationSystem(entityMap).Execute(_repo2, 0.016f);

        // -- Assert (post-genesis): UnitSubordinate is reconstituted; intent is removed --

        Assert.False(
            _repo2.HasManagedComponent<InitialUnitSubordinateIntent>(subordinate2),
            "InitialUnitSubordinateIntent should be removed after genesis.");

        Assert.True(
            _repo2.HasComponent<UnitSubordinate>(subordinate2),
            "Subordinate should have UnitSubordinate after genesis.");

        var sub2 = _repo2.GetComponent<UnitSubordinate>(subordinate2);
        Assert.Equal(commander2, sub2.Commander);
        Assert.Equal(TacticalDesignation.Wingman, sub2.Designation);

        // Commander2 should now have a UnitRoster with the subordinate registered.
        Assert.True(
            _repo2.HasComponent<UnitRoster>(commander2),
            "Commander should have UnitRoster after genesis.");

        var roster = _repo2.GetComponent<UnitRoster>(commander2);
        Assert.Equal(1, roster.Count);
    }
}
