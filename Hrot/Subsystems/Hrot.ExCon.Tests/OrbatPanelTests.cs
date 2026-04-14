using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Panels;
using Hrot.Core.Network;
using FDP.Toolkit.DER;
using Xunit;
using Moq;

namespace Hrot.ExCon.Tests;

public class OrbatPanelTests
{
    [Fact]
    public void HandleNewUnitClick_WithSelectedType_CallsStartPlacementModeWithCorrectParameters()
    {
        // Arrange
        var catalog = new[]
        {
            new TkbCatalogEntry(303, "Platoon Auto"),
            new TkbCatalogEntry(301, "Platoon Empty")
        };
        var panel = new OrbatPanel(catalog);
        var mockLogic = new Mock<IExConLogic>();

        // Act
        panel.HandleNewUnitClick(mockLogic.Object);

        // Assert
        // We expect it to use the first catalog item's selected type (303) and valid JSON string for properties
        mockLogic.Verify(l => l.StartPlacementMode(
            It.Is<long>(type => type == 303),
            It.Is<string>(json => json.Contains("FORCE_FRIENDLY"))),
            Times.Once);
    }

    // ── BUG2-U002 – ORBAT tree indentation ───────────────────────────────────

    [Fact]
    public void GetVisibleNodes_SubordinateHasGreaterDepthThanParent()
    {
        var repo = new DerRepo();

        var parent = repo.CreateEntity(1, 100);
        parent.SetDescriptor(new EntityInfoDescriptor
        {
            EntityId    = 1,
            Name        = "HQ",
            CommanderId = 0,
            Affiliation = "FORCE_FRIENDLY",
        });

        var child = repo.CreateEntity(2, 101);
        child.SetDescriptor(new EntityInfoDescriptor
        {
            EntityId    = 2,
            Name        = "Tank1",
            CommanderId = 1,
            Affiliation = "FORCE_FRIENDLY",
        });

        var panel = new OrbatPanel();
        panel.ToggleExpanded(1); // expand parent so child becomes visible

        var nodes = panel.GetVisibleNodes(repo);

        var parentNode = nodes.Single(n => n.EntityId == 1);
        var childNode  = nodes.Single(n => n.EntityId == 2);
        Assert.True(childNode.Depth > parentNode.Depth,
            $"Expected child depth ({childNode.Depth}) > parent depth ({parentNode.Depth})");
    }
}

