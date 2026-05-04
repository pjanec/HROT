using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.SimHost;
using Fdp.Toolkit.Tkb;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Verifies INTS-P1-001: <see cref="NedTkbCatalog.RegisterAll"/> populates a
/// <see cref="TkbDatabase"/> with all canonical entity types so that
/// <c>NetworkSpawningSystem</c> can resolve them before the first simulation tick.
/// </summary>
public class TkbRegistrationTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const long UnknownType = 9999L;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TkbDatabase CreatePopulated()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        return db;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-001-T1: Ground platforms
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>M1 Abrams tank must be discoverable after RegisterAll.</summary>
    [Fact]
    public void RegisterAll_Populates_TankM1Abrams()
    {
        var db = CreatePopulated();
        Assert.True(db.TryGetByType(TkbEntityTypes.Tank_M1Abrams, out _),
            $"TkbType {TkbEntityTypes.Tank_M1Abrams} (Tank_M1Abrams) was not registered.");
    }

    /// <summary>HMMWV truck must be discoverable after RegisterAll.</summary>
    [Fact]
    public void RegisterAll_Populates_TruckHMMWV()
    {
        var db = CreatePopulated();
        Assert.True(db.TryGetByType(TkbEntityTypes.Truck_HMMWV, out _),
            $"TkbType {TkbEntityTypes.Truck_HMMWV} (Truck_HMMWV) was not registered.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-001-T2: Lifeforms
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Infantry rifleman must be discoverable after RegisterAll.</summary>
    [Fact]
    public void RegisterAll_Populates_InfantryRifleman()
    {
        var db = CreatePopulated();
        Assert.True(db.TryGetByType(TkbEntityTypes.Infantry_Rifleman, out _),
            $"TkbType {TkbEntityTypes.Infantry_Rifleman} (Infantry_Rifleman) was not registered.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-001-T3: Counter-test — fresh database has no entries
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A freshly constructed <see cref="TkbDatabase"/> without calling
    /// <see cref="NedTkbCatalog.RegisterAll"/> must not contain any known type.
    /// Proves that <c>RegisterAll</c> is necessary.
    /// </summary>
    [Fact]
    public void FreshDatabase_DoesNotContain_TankM1Abrams()
    {
        var db = new TkbDatabase();
        Assert.False(db.TryGetByType(TkbEntityTypes.Tank_M1Abrams, out _),
            "TkbDatabase should be empty before RegisterAll is called.");
    }

    /// <summary>Unknown type 9999 must never be found regardless of registration.</summary>
    [Fact]
    public void TryGetByType_UnknownType_ReturnsFalse()
    {
        var db = CreatePopulated();
        Assert.False(db.TryGetByType(UnknownType, out _),
            "TkbDatabase should not contain an unregistered type.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SC-HA014: Hill Attack TKB Blueprint Validation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// SC-HA014-1: The Unit_TankPlatoon commander blueprint must be registered and
    /// must include a <see cref="Blackboard1024"/> component so that
    /// <c>HillAttackMutableState</c> can be projected onto it at runtime.
    /// </summary>
    [Fact]
    public void SC_HA014_1_Unit_TankPlatoon_Blueprint_HasBlackboard1024()
    {
        var db = CreatePopulated();
        Assert.True(db.TryGetByType(TkbEntityTypes.Unit_TankPlatoon, out var template),
            $"Unit_TankPlatoon (TkbType {TkbEntityTypes.Unit_TankPlatoon}) was not registered.");

        // Apply the template to a test entity and verify Blackboard1024 is stamped on it.
        var world = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(world);
        Entity entity = world.CreateEntity();
        template!.ApplyTo(world, entity, preserveExisting: false);

        Assert.True(world.HasComponent<Blackboard1024>(entity),
            $"Unit_TankPlatoon blueprint must include Blackboard1024 for HillAttackMutableState projection. " +
            $"Call WithHeavyMemory({TkbEntityTypes.Unit_TankPlatoon}) in BdcTkbCatalog.RegisterAll.");
    }

    /// <summary>
    /// SC-HA014-2: The Unit_TankPlatoon_Auto (auto-spawn variant) blueprint must also
    /// include <see cref="Blackboard1024"/> so that both platoon variants can execute
    /// <c>PlatoonHillAttack</c>.
    /// </summary>
    [Fact]
    public void SC_HA014_2_Unit_TankPlatoon_Auto_Blueprint_HasBlackboard1024()
    {
        var db = CreatePopulated();
        Assert.True(db.TryGetByType(TkbEntityTypes.Unit_TankPlatoon_Auto, out var template),
            $"Unit_TankPlatoon_Auto (TkbType {TkbEntityTypes.Unit_TankPlatoon_Auto}) was not registered.");

        var world = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(world);
        Entity entity = world.CreateEntity();
        template!.ApplyTo(world, entity, preserveExisting: false);

        Assert.True(world.HasComponent<Blackboard1024>(entity),
            $"Unit_TankPlatoon_Auto blueprint must include Blackboard1024 for HillAttackMutableState projection.");
    }

    /// <summary>
    /// SC-HA014-3: The Tank_M1Abrams subordinate blueprint must include
    /// <see cref="TargetMemory"/> (required by <c>Condition_HasTarget</c>) and
    /// <see cref="BehaviorState"/> (required by <c>BTreeTickSystem</c>).
    /// Both components must be present after blueprint application.
    /// </summary>
    [Fact]
    public void SC_HA014_3_Tank_M1Abrams_Blueprint_HasTargetMemoryAndBehaviorState()
    {
        var db = CreatePopulated();
        Assert.True(db.TryGetByType(TkbEntityTypes.Tank_M1Abrams, out var template),
            $"Tank_M1Abrams (TkbType {TkbEntityTypes.Tank_M1Abrams}) was not registered.");

        var world = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(world);
        Entity entity = world.CreateEntity();
        template!.ApplyTo(world, entity, preserveExisting: false);

        Assert.True(world.HasComponent<TargetMemory>(entity),
            "Tank_M1Abrams blueprint must include TargetMemory for Condition_HasTarget.");

        Assert.True(world.HasComponent<BehaviorState>(entity),
            "Tank_M1Abrams blueprint must include BehaviorState for BTreeTickSystem orchestration.");
    }
}
