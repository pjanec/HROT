using Hrot.UI.Common.Panels;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="SharedOrbatPanel"/>.
/// All tests bypass ImGui by calling the panel's <c>internal</c> handler methods
/// directly.  The <c>InternalsVisibleTo</c> attribute on <c>Hrot.UI.Common</c>
/// makes these methods accessible from this test assembly.
/// </summary>
public class SharedOrbatPanelTests
{
    private static SharedOrbatPanel CreatePanel() => new();

    private static Mock<IOrbatController> CreateCtrl() => new(MockBehavior.Strict);

    // ── SelectEntity ──────────────────────────────────────────────────────────

    [Fact]
    public void HandleSelectEntity_CallsSelectEntityWithCorrectId()
    {
        var panel = CreatePanel();
        var ctrl = new Mock<IOrbatController>();

        panel.HandleSelectEntity(42, ctrl.Object);

        ctrl.Verify(c => c.SelectEntity(42), Times.Once);
    }

    [Fact]
    public void HandleSelectEntity_ForSecondNode_CallsSelectEntityWithSecondId()
    {
        var panel = CreatePanel();
        var ctrl = new Mock<IOrbatController>();

        var nodes = new[]
        {
            new OrbatNodeViewModel(10, "Alpha Company", 0, true, false, false),
            new OrbatNodeViewModel(11, "1st Platoon",   1, false, false, false),
        };

        // Simulate click on the second node
        panel.HandleSelectEntity(nodes[1].EntityId, ctrl.Object);

        ctrl.Verify(c => c.SelectEntity(11), Times.Once);
        ctrl.Verify(c => c.SelectEntity(10), Times.Never);
    }

    // ── Drop payload — embarkation ────────────────────────────────────────────

    [Fact]
    public void HandleDropPayload_DifferentIds_CallsRequestEmbark()
    {
        var panel = CreatePanel();
        var ctrl = new Mock<IOrbatController>();

        panel.HandleDropPayload(passengerId: 5, vehicleId: 12, ctrl.Object);

        ctrl.Verify(c => c.RequestEmbark(5, 12), Times.Once);
    }

    [Fact]
    public void HandleDropPayload_SameId_DoesNotCallRequestEmbark()
    {
        var panel = CreatePanel();
        var ctrl = new Mock<IOrbatController>();

        panel.HandleDropPayload(passengerId: 7, vehicleId: 7, ctrl.Object);

        ctrl.Verify(c => c.RequestEmbark(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void HandleDropPayload_CorrectPassengerAndVehicleOrdering()
    {
        var panel = CreatePanel();
        var ctrl = new Mock<IOrbatController>();

        panel.HandleDropPayload(passengerId: 99, vehicleId: 200, ctrl.Object);

        ctrl.Verify(c => c.RequestEmbark(99, 200), Times.Once);
        // Ensure ordering is not swapped
        ctrl.Verify(c => c.RequestEmbark(200, 99), Times.Never);
    }

    // ── CS017: OrbatNodeViewModel.CanAcceptSubordinates ───────────────────────

    [Fact]
    public void OrbatNodeViewModel_CanAcceptSubordinates_RetainsValue()
    {
        var nodeTrue  = new OrbatNodeViewModel(1, "A", 0, false, false, CanAcceptSubordinates: true);
        var nodeFalse = new OrbatNodeViewModel(2, "B", 0, false, false, CanAcceptSubordinates: false);

        Assert.True(nodeTrue.CanAcceptSubordinates);
        Assert.False(nodeFalse.CanAcceptSubordinates);
    }

    // ── CS019: HandleHierarchyDropPayload ─────────────────────────────────────

    [Fact]
    public void HandleHierarchyDropPayload_WhenCanAcceptSubordinates_CallsRequestAssignSubordinate()
    {
        var panel = CreatePanel();
        var ctrl  = new Mock<IOrbatController>();
        ctrl.Setup(c => c.RequestAssignSubordinate(5, 12));

        var targetNode = new OrbatNodeViewModel(12, "Alpha Company", 0, true, false, CanAcceptSubordinates: true);
        panel.HandleHierarchyDropPayload(5, targetNode, ctrl.Object);

        ctrl.Verify(c => c.RequestAssignSubordinate(5, 12), Times.Once);
        ctrl.Verify(c => c.RequestEmbark(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void HandleHierarchyDropPayload_WhenCannotAcceptSubordinates_CallsRequestEmbark()
    {
        var panel = CreatePanel();
        var ctrl  = new Mock<IOrbatController>();
        ctrl.Setup(c => c.RequestEmbark(5, 12));

        var targetNode = new OrbatNodeViewModel(12, "Vehicle", 0, false, false, CanAcceptSubordinates: false);
        panel.HandleHierarchyDropPayload(5, targetNode, ctrl.Object);

        ctrl.Verify(c => c.RequestEmbark(5, 12), Times.Once);
        ctrl.Verify(c => c.RequestAssignSubordinate(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void HandleHierarchyDropPayload_SelfDrop_IsNoOp()
    {
        var panel = CreatePanel();
        var ctrl  = new Mock<IOrbatController>(MockBehavior.Strict);

        var targetNode = new OrbatNodeViewModel(7, "Self", 0, false, false, CanAcceptSubordinates: true);
        // No calls expected — strict mock will throw if any method is invoked.
        panel.HandleHierarchyDropPayload(7, targetNode, ctrl.Object);
    }
}
