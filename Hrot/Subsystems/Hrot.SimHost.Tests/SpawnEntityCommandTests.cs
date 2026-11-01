using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Map.Common;
using Hrot.SimHost.Configuration;
using Hrot.SimHost.UI;
using CarKinem.Core;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Verifies INTS-P1-002: <see cref="SimHostScenarioManager.SpawnVehicle"/> asks for a networked entity through
/// <c>EntityCreation.RequestEntityCreation</c> (⭐ CE-259ax — it used to publish a <see cref="SpawnEntityCommand"/> order) so that entities are
/// visible on the network (not just local ghosts).
///
/// Uses the production creation pack over a bare world â€” no DDS or Raylib required.
/// </summary>
public class SpawnEntityCommandTests
{
    // â”€â”€ Test constants (Â§CODE-STANDARDS Â§1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static readonly Vector2 DefaultPos     = new(100f, 200f);
    private static readonly Vector2 DefaultHeading = Vector2.UnitX; // East

    // â”€â”€ Bus stub â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>The production creation pack's world: SpawnVehicle's request lands on its bus (⭐ CE-259ax).</summary>
    private sealed class Rig
    {
        public required EntityRepository World;
    }

    private const int NodeId = 7;

    private static (SimHostScenarioManager Sut, Rig Bus) CreateSut()
    {
        var world = new EntityRepository();
        var creation = Hrot.TestShared.TestEntityCreation.For(world, Hrot.Map.Common.HrotEnvironment.CreateTkb(), NodeId);

        // Road/traj/formations are only used by demo helpers (SpawnRoamers etc.), not by SpawnVehicle.
        var sut = new SimHostScenarioManager(
            repo:       world,
            road:       default(RoadNetworkBlob),
            traj:       null!,
            formations: null!,
            creation:   creation);

        return (sut, new Rig { World = world });
    }

    private static EntityCreationRequest ExtractCommand(Rig bus)
    {
        bus.World.Bus.SwapBuffers();
        return Assert.Single(((Fdp.ModuleHost.Abstractions.ISimulationView)bus.World).ReadManagedEvents<EntityCreationRequest>().ToList());
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // P1-002-T1: TKB type mapping
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>Tank vehicle class must map to Tank_M1Abrams TKB type.</summary>
    [Fact]
    public void SpawnVehicle_Tank_PublishesCommand_WithTankM1AbramsTkbType()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading, VehicleClass.Tank);

        var cmd = ExtractCommand(bus);
        Assert.Equal(TkbEntityTypes.Tank_M1Abrams, cmd.TkbType);
    }

    /// <summary>Pedestrian vehicle class must map to Infantry_Rifleman TKB type.</summary>
    [Fact]
    public void SpawnVehicle_Pedestrian_PublishesCommand_WithInfantryRiflemanTkbType()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading, VehicleClass.Pedestrian);

        var cmd = ExtractCommand(bus);
        Assert.Equal(TkbEntityTypes.Infantry_Rifleman, cmd.TkbType);
    }

    /// <summary>Default vehicle class must fall back to Truck_HMMWV TKB type.</summary>
    [Fact]
    public void SpawnVehicle_Default_PublishesCommand_WithTruckHMMWVTkbType()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading); // default = PersonalCar â†’ maps to HMMWV

        var cmd = ExtractCommand(bus);
        Assert.Equal(TkbEntityTypes.Truck_HMMWV, cmd.TkbType);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // P1-002-T2: Command structure
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>The request must not pre-allocate a network id: the spawn system assigns one.</summary>
    [Fact]
    public void SpawnVehicle_PublishesCommand_WithNetworkId_Zero()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading);

        var cmd = ExtractCommand(bus);
        Assert.Equal(0L, cmd.PreAllocatedNetworkId);
    }

    /// <summary>Published request must be owned by this node (the pack's NodeId).</summary>
    [Fact]
    public void SpawnVehicle_PublishesCommand_WithLocalNodeId()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading);

        var cmd = ExtractCommand(bus);
        Assert.Equal(NodeId, cmd.OwnerAppInstanceId);   // ⭐ CE-259ax — 'mine' is the pack's own node id
    }

    /// <summary>InitialComponents must contain exactly one SimTransform.</summary>
    [Fact]
    public void SpawnVehicle_PublishesCommand_InitialComponents_ContainsSimTransform()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading);

        var cmd = ExtractCommand(bus);
        Assert.NotNull(cmd.InitialComponents);
        Assert.Contains(cmd.InitialComponents, c => c is SimTransform);
    }

    /// <summary>SimTransform inside the command must reflect the provided spawn position.</summary>
    [Fact]
    public void SpawnVehicle_SimTransform_HasCorrectPosition()
    {
        var (sut, bus) = CreateSut();
        var pos = new Vector2(500f, 750f);

        sut.SpawnVehicle(pos, DefaultHeading);

        var cmd = ExtractCommand(bus);
        var transform = cmd.InitialComponents.OfType<SimTransform>().First();

        Assert.Equal(pos.X, transform.Position.X, precision: 3);
        Assert.Equal(pos.Y, transform.Position.Y, precision: 3);
        Assert.Equal(0f,    transform.Position.Z, precision: 3);
    }

    /// <summary>Published command must carry Hrot.NED.Descriptors.EntityInfo in InitialComponents so the IG renders the entity.</summary>
    [Fact]
    public void SpawnVehicle_PublishesCommand_WithEntityInfoInInitialComponents()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading, VehicleClass.Tank);

        var cmd = ExtractCommand(bus);
        Assert.NotNull(cmd.InitialComponents);
        Assert.Contains(cmd.InitialComponents, c => c is EntityInfo);
    }
}
