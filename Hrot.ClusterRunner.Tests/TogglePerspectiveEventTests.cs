using Hrot.Common;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Tests for WM-S502: <see cref="TogglePerspectiveEvent"/> record type.
/// Verifies value equality semantics and immutability guaranteed by the record declaration.
/// </summary>
public class TogglePerspectiveEventTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S502.T1: Value equality (record comparison)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoEvents_WithSameValues_AreEqual()
    {
        var e1 = new TogglePerspectiveEvent("IG", "SimHost");
        var e2 = new TogglePerspectiveEvent("IG", "SimHost");

        Assert.Equal(e1, e2);
    }

    [Fact]
    public void TwoEvents_WithDifferentValues_AreNotEqual()
    {
        var e1 = new TogglePerspectiveEvent("IG",      "SimHost");
        var e2 = new TogglePerspectiveEvent("SimHost", "IG");

        Assert.NotEqual(e1, e2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S502.T2: Immutability (no public setters on init-only record properties)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Properties_ReturnConstructorValues()
    {
        var evt = new TogglePerspectiveEvent("OldP", "NewP");

        Assert.Equal("OldP", evt.OldPerspective);
        Assert.Equal("NewP", evt.NewPerspective);
    }

    [Fact]
    public void Record_SupportsDeconstruct()
    {
        var evt = new TogglePerspectiveEvent("A", "B");
        var (old, next) = evt;

        Assert.Equal("A", old);
        Assert.Equal("B", next);
    }
}
