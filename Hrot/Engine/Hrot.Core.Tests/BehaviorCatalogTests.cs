using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// Unit tests for <see cref="BehaviorCatalog"/>.
/// Verifies correct behavior lists per TKB type and no per-call allocation.
/// </summary>
public class BehaviorCatalogTests
{
    // ── Test 1 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Insurgent entities must have "Ambush" available and must NOT have "WanderCivil".
    /// </summary>
    [Fact]
    public void GetValidBehaviors_Insurgent_ContainsAmbush_NotWanderCivil()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.Insurgent);

        Assert.Contains("Ambush", behaviors);
        Assert.DoesNotContain("WanderCivil", behaviors);
    }

    // ── Test 2 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Civilian pedestrian entities must have "WanderCivil" and must NOT have "Ambush".
    /// </summary>
    [Fact]
    public void GetValidBehaviors_CivilianPedestrian_ContainsWanderCivil_NotAmbush()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianPedestrian);

        Assert.Contains("WanderCivil", behaviors);
        Assert.DoesNotContain("Ambush", behaviors);
    }

    // ── Test 3 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Unknown TKB types must fall back to a default list that includes "MoveToLocation".
    /// </summary>
    [Fact]
    public void GetValidBehaviors_UnknownTkbType_ReturnsFallbackListWithMoveToLocation()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(-999L);

        Assert.NotNull(behaviors);
        Assert.Contains("MoveToLocation", behaviors);
    }

    // ── Test 4 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Repeated calls for the same TKB type must return the same list instance
    /// (backed by a static readonly field — no per-call allocation).
    /// </summary>
    [Fact]
    public void GetValidBehaviors_SameListInstanceReturnedOnRepeatedCalls()
    {
        var first  = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.InfantrySoldier);
        var second = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.InfantrySoldier);

        Assert.True(ReferenceEquals(first, second),
            "Expected the same list instance on repeated calls to avoid per-call allocation.");
    }

    // ── Test 5 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Civilian car entities share the same behavior list as civilian pedestrians
    /// (both map to the s_civilianBehaviors static field).
    /// Verifies "WanderCivil" and "PanicFlee" are present.
    /// </summary>
    [Fact]
    public void GetValidBehaviors_CivilianCar_ContainsWanderCivilAndPanicFlee()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianCar);

        Assert.Contains("WanderCivil", behaviors);
        Assert.Contains("PanicFlee", behaviors);
    }

    // ── Test 6 ────────────────────────────────────────────────────────────
    /// <summary>
    /// MilitaryApc must have "ConvoyEscort" and must NOT have civilian behaviors.
    /// </summary>
    [Fact]
    public void GetValidBehaviors_MilitaryApc_ContainsConvoyEscort_NotCivilian()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.MilitaryApc);

        Assert.Contains("ConvoyEscort", behaviors);
        Assert.DoesNotContain("WanderCivil", behaviors);
        Assert.DoesNotContain("PanicFlee", behaviors);
    }
}
