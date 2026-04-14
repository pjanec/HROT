using Hrot.ExCon.Panels;
using FDP.Toolkit.DER;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for OC1-I001 through OC1-I006 — OrbatPanel context menu infrastructure
/// and the <see cref="OrbatPanel.IsSimulatedEntity"/> helper.
///
/// <para>All tests operate entirely on data-layer methods (<see cref="OrbatPanel.IsSimulatedEntity"/>)
/// or on mock <see cref="IExConLogic"/> interactions, so no ImGui context is required.</para>
/// </summary>
public class OrbatPanelContextMenuTests
{
    // ── IsSimulatedEntity (OC1-I001) ─────────────────────────────────────────

    /// <summary>
    /// OC1-I001 SC1: entity with TkbType below 8000 must be considered simulated.
    /// </summary>
    [Fact]
    public void IsSimulatedEntity_LowTkbType_ReturnsTrue()
    {
        var repo = new DerRepo();
        repo.CreateEntity(10, tkbType: 1001);

        Assert.True(OrbatPanel.IsSimulatedEntity(10, repo));
    }

    /// <summary>
    /// OC1-I001 SC2: entity with TkbType of 8802 (TacGraphic_Route) must not be
    /// considered simulated.
    /// </summary>
    [Fact]
    public void IsSimulatedEntity_RouteTkbType_ReturnsFalse()
    {
        var repo = new DerRepo();
        repo.CreateEntity(20, tkbType: 8802); // TacGraphic_Route

        Assert.False(OrbatPanel.IsSimulatedEntity(20, repo));
    }

    /// <summary>
    /// OC1-I001 SC3: entity not present in the repo must return false.
    /// </summary>
    [Fact]
    public void IsSimulatedEntity_MissingEntity_ReturnsFalse()
    {
        var repo = new DerRepo();

        Assert.False(OrbatPanel.IsSimulatedEntity(99, repo));
    }

    /// <summary>
    /// OC1-I001 SC6 (boundary): TkbType exactly 8000 is in the map-graphic range
    /// and must not be considered simulated.
    /// </summary>
    [Fact]
    public void IsSimulatedEntity_BoundaryTkbType8000_ReturnsFalse()
    {
        var repo = new DerRepo();
        repo.CreateEntity(30, tkbType: 8000);

        Assert.False(OrbatPanel.IsSimulatedEntity(30, repo));
    }

    // ── HandleEntityClick delegates to SelectEntity ───────────────────────────

    /// <summary>
    /// OC1-I001: left-clicking an entity row must call <see cref="IExConLogic.SelectEntity"/>.
    /// </summary>
    [Fact]
    public void HandleEntityClick_CallsSelectEntity()
    {
        var panel = new OrbatPanel();
        var logic = new Mock<IExConLogic>();

        panel.HandleEntityClick(42, logic.Object);

        logic.Verify(l => l.SelectEntity(42), Times.Once);
    }
}
