using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// Unit tests for <see cref="DoctrineCatalog"/>.
/// Verifies correct doctrine lists per TKB type and no per-call allocation.
/// </summary>
public class DoctrineCatalogTests
{
    // ── Test 1 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Insurgent entities must have "Ambush" available and must NOT have "WanderCivil".
    /// </summary>
    [Fact]
    public void GetValidDoctrines_Insurgent_ContainsAmbush_NotWanderCivil()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.Insurgent);

        Assert.Contains("Ambush", doctrines);
        Assert.DoesNotContain("WanderCivil", doctrines);
    }

    // ── Test 2 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Civilian pedestrian entities must have "WanderCivil" and must NOT have "Ambush".
    /// </summary>
    [Fact]
    public void GetValidDoctrines_CivilianPedestrian_ContainsWanderCivil_NotAmbush()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.CivilianPedestrian);

        Assert.Contains("WanderCivil", doctrines);
        Assert.DoesNotContain("Ambush", doctrines);
    }

    // ── Test 3 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Unknown TKB types must fall back to a default list that includes "MoveToLocation".
    /// </summary>
    [Fact]
    public void GetValidDoctrines_UnknownTkbType_ReturnsFallbackListWithMoveToLocation()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(-999L);

        Assert.NotNull(doctrines);
        Assert.Contains("MoveToLocation", doctrines);
    }

    // ── Test 4 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Repeated calls for the same TKB type must return the same list instance
    /// (backed by a static readonly field — no per-call allocation).
    /// </summary>
    [Fact]
    public void GetValidDoctrines_SameListInstanceReturnedOnRepeatedCalls()
    {
        var first  = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.InfantrySoldier);
        var second = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.InfantrySoldier);

        Assert.True(ReferenceEquals(first, second),
            "Expected the same list instance on repeated calls to avoid per-call allocation.");
    }

    // ── Test 5 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Civilian car entities share the same doctrine list as civilian pedestrians
    /// (both map to the s_civilianDoctrines static field).
    /// Verifies "WanderCivil" and "PanicFlee" are present.
    /// </summary>
    [Fact]
    public void GetValidDoctrines_CivilianCar_ContainsWanderCivilAndPanicFlee()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.CivilianCar);

        Assert.Contains("WanderCivil", doctrines);
        Assert.Contains("PanicFlee", doctrines);
    }

    // ── Test 6 ────────────────────────────────────────────────────────────
    /// <summary>
    /// MilitaryApc must have "ConvoyEscort" and must NOT have civilian doctrines.
    /// </summary>
    [Fact]
    public void GetValidDoctrines_MilitaryApc_ContainsConvoyEscort_NotCivilian()
    {
        var doctrines = DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.MilitaryApc);

        Assert.Contains("ConvoyEscort", doctrines);
        Assert.DoesNotContain("WanderCivil", doctrines);
        Assert.DoesNotContain("PanicFlee", doctrines);
    }
}
