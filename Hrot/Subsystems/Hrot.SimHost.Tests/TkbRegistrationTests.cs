using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
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
}
