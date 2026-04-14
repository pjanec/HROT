using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.SimHost.Configuration;
using Hrot.SimHost.UI;
using CarKinem.Core;
using CarKinem.Road;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using Fdp.ModuleHost_Core.Network.Interfaces;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Verifies INTS-P1-002: <see cref="SimHostScenarioManager.SpawnVehicle"/> publishes a
/// <see cref="SpawnEntityCommand"/> so that entities are visible on the network
/// (not just local ghosts).
///
/// Uses a lightweight <see cref="IEventBus"/> stub — no DDS or Raylib required.
/// </summary>
public class SpawnEntityCommandTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private static readonly Vector2 DefaultPos     = new(100f, 200f);
    private static readonly Vector2 DefaultHeading = Vector2.UnitX; // East

    // ── Bus stub ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Capturing IEventBus implementation that records all managed publications.
    /// </summary>
    private sealed class CapturingBus : IEventBus
    {
        public readonly List<object> ManagedEvents = new();

        /// <inheritdoc/>
        public void Publish<T>(T evt) where T : unmanaged
        { /* unmanaged events not relevant for these tests */ }

        /// <inheritdoc/>
        public void PublishManaged<T>(T evt) => ManagedEvents.Add(evt!);
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    private static (SimHostScenarioManager Sut, CapturingBus Bus) CreateSut()
    {
        var bus  = new CapturingBus();
        var repo = new EntityRepository();

        // Road/traj/formations are only used by demo helpers (SpawnRoamers etc.),
        // not by SpawnVehicle which only touches _spawnBus.
        var sut = new SimHostScenarioManager(
            repo:       repo,
            road:       default(RoadNetworkBlob),
            traj:       null!,
            formations: null!,
            spawnBus:   bus);

        return (sut, bus);
    }

    private static SpawnEntityCommand ExtractCommand(CapturingBus bus)
    {
        Assert.Single(bus.ManagedEvents);
        Assert.IsType<SpawnEntityCommand>(bus.ManagedEvents[0]);
        return (SpawnEntityCommand)bus.ManagedEvents[0];
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-002-T1: TKB type mapping
    // ═══════════════════════════════════════════════════════════════════════════

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

        sut.SpawnVehicle(DefaultPos, DefaultHeading); // default = PersonalCar → maps to HMMWV

        var cmd = ExtractCommand(bus);
        Assert.Equal(TkbEntityTypes.Truck_HMMWV, cmd.TkbType);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-002-T2: Command structure
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Published command must have NetworkId = 0 so DdsIdAllocator assigns one.</summary>
    [Fact]
    public void SpawnVehicle_PublishesCommand_WithNetworkId_Zero()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading);

        var cmd = ExtractCommand(bus);
        Assert.Equal(0, cmd.NetworkId);
    }

    /// <summary>Published command must carry the local SimHost node ID.</summary>
    [Fact]
    public void SpawnVehicle_PublishesCommand_WithLocalNodeId()
    {
        var (sut, bus) = CreateSut();

        sut.SpawnVehicle(DefaultPos, DefaultHeading);

        var cmd = ExtractCommand(bus);
        Assert.Equal(SimHostNetworkConstants.LocalNodeId, cmd.OwnerNodeId);
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
