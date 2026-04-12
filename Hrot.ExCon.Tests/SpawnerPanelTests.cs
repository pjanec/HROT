using Hrot.Core.Mission;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.Facades;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="SpawnerPanel"/>.
///
/// Tests drive the panel through its public API
/// (<see cref="SpawnerPanel.SearchFilter"/>,
/// <see cref="SpawnerPanel.HandleTypeSelected"/>,
/// <see cref="SpawnerPanel.HandleAffiliationChange"/>,
/// <see cref="SpawnerPanel.HandleActivatePlacementTool"/>,
/// <see cref="SpawnerPanel.FilteredEntries"/>)
/// without requiring an active ImGui render frame.
/// </summary>
public class SpawnerPanelTests
{
    // ── Test catalog data ─────────────────────────────────────────────────────

    private static readonly TkbCatalogEntry[] SampleCatalog =
    {
        new TkbCatalogEntry(100, "M1 Abrams"),
        new TkbCatalogEntry(101, "M2 Bradley IFV"),
        new TkbCatalogEntry(102, "HMMWV"),
        new TkbCatalogEntry(103, "T-72"),
        new TkbCatalogEntry(200, "Infantry Rifleman"),
    };

    // ── Constructor / initial state ───────────────────────────────────────────

    [Fact]
    public void DefaultConstructor_FilteredEntries_IsEmpty()
    {
        var panel = new SpawnerPanel();
        Assert.Empty(panel.FilteredEntries);
    }

    [Fact]
    public void Constructor_WithCatalog_FilteredEntriesContainsAll()
    {
        var panel = new SpawnerPanel(SampleCatalog);
        Assert.Equal(SampleCatalog.Length, panel.FilteredEntries.Count);
    }

    [Fact]
    public void SelectedType_Default_IsZero()
    {
        var panel = new SpawnerPanel();
        Assert.Equal(0L, panel.SelectedType);
    }

    [Fact]
    public void SelectedAffiliation_Default_IsForceFriendly()
    {
        var panel = new SpawnerPanel();
        Assert.Equal(eForceIdentifier.FORCE_FRIENDLY, panel.SelectedAffiliation);
    }

    // ── Filter – empty / null ─────────────────────────────────────────────────

    [Fact]
    public void SearchFilter_Empty_ReturnsAllEntries()
    {
        var panel = new SpawnerPanel(SampleCatalog) { SearchFilter = "" };
        Assert.Equal(SampleCatalog.Length, panel.FilteredEntries.Count);
    }

    [Fact]
    public void SearchFilter_Null_ReturnsAllEntries()
    {
        var panel = new SpawnerPanel(SampleCatalog) { SearchFilter = null! };
        Assert.Equal(SampleCatalog.Length, panel.FilteredEntries.Count);
    }

    // ── Filter – matching ─────────────────────────────────────────────────────

    [Fact]
    public void SearchFilter_PartialMatch_ReturnsMatchingEntries()
    {
        var panel = new SpawnerPanel(SampleCatalog) { SearchFilter = "Bradley" };

        Assert.Single(panel.FilteredEntries);
        Assert.Equal(101L, panel.FilteredEntries[0].TkbId);
    }

    [Fact]
    public void SearchFilter_CaseInsensitive_LowerFilter_MatchesUpperName()
    {
        var panel = new SpawnerPanel(SampleCatalog) { SearchFilter = "m1 abrams" };

        Assert.Single(panel.FilteredEntries);
        Assert.Equal(100L, panel.FilteredEntries[0].TkbId);
    }

    [Fact]
    public void SearchFilter_CaseInsensitive_UpperFilter_MatchesLowerName()
    {
        var panel = new SpawnerPanel(SampleCatalog) { SearchFilter = "INFANTRY" };

        Assert.Single(panel.FilteredEntries);
        Assert.Equal(200L, panel.FilteredEntries[0].TkbId);
    }

    [Fact]
    public void SearchFilter_MixedCase_Matches()
    {
        var panel = new SpawnerPanel(SampleCatalog) { SearchFilter = "hMmWv" };

        Assert.Single(panel.FilteredEntries);
        Assert.Equal(102L, panel.FilteredEntries[0].TkbId);
    }

    [Fact]
    public void SearchFilter_NoMatch_ReturnsEmpty()
    {
        var panel = new SpawnerPanel(SampleCatalog) { SearchFilter = "Helicopter" };
        Assert.Empty(panel.FilteredEntries);
    }

    [Fact]
    public void SearchFilter_MultipleMatches_ReturnsAllMatching()
    {
        // Use a dedicated catalog so the test controls exactly which names match.
        var catalog = new[]
        {
            new TkbCatalogEntry(1, "Tank Alpha"),
            new TkbCatalogEntry(2, "Tank Bravo"),
            new TkbCatalogEntry(3, "Truck"),
        };
        var panel = new SpawnerPanel(catalog) { SearchFilter = "tank" };

        var ids = panel.FilteredEntries.Select(e => e.TkbId).ToList();
        Assert.Contains(1L, ids);
        Assert.Contains(2L, ids);
        Assert.DoesNotContain(3L, ids);
        // Verify exactly the two matching entries are present (and nothing else)
        Assert.Collection(panel.FilteredEntries,
            e => Assert.Equal(1L, e.TkbId),
            e => Assert.Equal(2L, e.TkbId));
    }

    [Fact]
    public void SearchFilter_TargetedFilter_ReturnsOnlyMatching()
    {
        // "Bradley" is unique in the catalog
        var panel = new SpawnerPanel(SampleCatalog) { SearchFilter = "Bradley" };
        var entry = Assert.Single(panel.FilteredEntries);
        Assert.Equal("M2 Bradley IFV", entry.Name);
    }

    // ── Filter changes are immediately reflected ───────────────────────────────

    [Fact]
    public void SearchFilter_ChangedTwice_ReflectsLatestFilter()
    {
        var panel = new SpawnerPanel(SampleCatalog);

        panel.SearchFilter = "Tank"; // no entries in sample have "Tank"
        Assert.Empty(panel.FilteredEntries);

        panel.SearchFilter = ""; // clear filter
        Assert.Equal(SampleCatalog.Length, panel.FilteredEntries.Count);
    }

    // ── HandleTypeSelected ────────────────────────────────────────────────────

    [Fact]
    public void HandleTypeSelected_SetsSelectedType()
    {
        var panel = new SpawnerPanel(SampleCatalog);
        panel.HandleTypeSelected(103L);
        Assert.Equal(103L, panel.SelectedType);
    }

    [Fact]
    public void HandleTypeSelected_OverwritesPreviousSelection()
    {
        var panel = new SpawnerPanel(SampleCatalog);
        panel.HandleTypeSelected(100L);
        panel.HandleTypeSelected(200L);
        Assert.Equal(200L, panel.SelectedType);
    }

    // ── HandleAffiliationChange ───────────────────────────────────────────────

    [Fact]
    public void HandleAffiliationChange_ToOpposing_SetsAffiliation()
    {
        var panel = new SpawnerPanel();
        panel.HandleAffiliationChange(eForceIdentifier.FORCE_OPPOSING);
        Assert.Equal(eForceIdentifier.FORCE_OPPOSING, panel.SelectedAffiliation);
    }

    [Fact]
    public void HandleAffiliationChange_ToFriendly_SetsAffiliation()
    {
        var panel = new SpawnerPanel();
        panel.HandleAffiliationChange(eForceIdentifier.FORCE_OPPOSING); // change first
        panel.HandleAffiliationChange(eForceIdentifier.FORCE_FRIENDLY); // change back
        Assert.Equal(eForceIdentifier.FORCE_FRIENDLY, panel.SelectedAffiliation);
    }

    [Fact]
    public void HandleAffiliationChange_ToNeutral_SetsAffiliation()
    {
        var panel = new SpawnerPanel();
        panel.HandleAffiliationChange(eForceIdentifier.FORCE_NEUTRAL);
        Assert.Equal(eForceIdentifier.FORCE_NEUTRAL, panel.SelectedAffiliation);
    }

    // ── HandleActivatePlacementTool ───────────────────────────────────────────

    [Fact]
    public void HandleActivatePlacementTool_CallsStartPlacementMode()
    {
        var spawn = new Mock<ISpawnController>();
        var panel = new SpawnerPanel(SampleCatalog);
        panel.HandleTypeSelected(103L);
        panel.HandleAffiliationChange(eForceIdentifier.FORCE_OPPOSING);

        panel.HandleActivatePlacementTool(spawn.Object);

        spawn.Verify(s => s.StartPlacementMode(103L, It.Is<string?>(p => p != null && p.Contains("FORCE_OPPOSING"))), Times.Once);
    }

    [Fact]
    public void HandleActivatePlacementTool_PassesCorrectTkbType()
    {
        var spawn = new Mock<ISpawnController>();
        var panel = new SpawnerPanel(SampleCatalog);
        panel.HandleTypeSelected(200L);

        panel.HandleActivatePlacementTool(spawn.Object);

        spawn.Verify(s => s.StartPlacementMode(200L, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void HandleActivatePlacementTool_PassesCorrectAffiliation()
    {
        var spawn = new Mock<ISpawnController>();
        var panel = new SpawnerPanel(SampleCatalog);
        panel.HandleAffiliationChange(eForceIdentifier.FORCE_FRIENDLY);

        panel.HandleActivatePlacementTool(spawn.Object);

        spawn.Verify(s => s.StartPlacementMode(It.IsAny<long>(), It.Is<string?>(p => p != null && p.Contains("FORCE_FRIENDLY"))), Times.Once);
    }

    [Fact]
    public void HandleActivatePlacementTool_NullLogic_Throws()
    {
        var panel = new SpawnerPanel();
        Assert.Throws<ArgumentNullException>(() => panel.HandleActivatePlacementTool(null!));
    }

    // ── HandleStartAreaAuthoring ─────────────────────────────────────────────

    [Fact]
    public void HandleStartAreaAuthoring_CallsStartAreaAuthoringMode()
    {
        var spawn = new Mock<ISpawnController>();
        var panel = new SpawnerPanel(SampleCatalog);

        panel.HandleStartAreaAuthoring(spawn.Object);

        spawn.Verify(s => s.StartAreaAuthoringMode(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void HandleStartAreaAuthoring_NullLogic_Throws()
    {
        var panel = new SpawnerPanel();
        Assert.Throws<ArgumentNullException>(() => panel.HandleStartAreaAuthoring(null!));
    }

    // ── HandleStartRouteAuthoring ────────────────────────────────────────────

    [Fact]
    public void HandleStartRouteAuthoring_CallsStartRouteAuthoringMode()
    {
        var spawn = new Mock<ISpawnController>();
        var panel = new SpawnerPanel(SampleCatalog);

        panel.HandleStartRouteAuthoring(spawn.Object);

        spawn.Verify(s => s.StartRouteAuthoringMode(), Times.Once);
    }

    [Fact]
    public void HandleStartRouteAuthoring_NullLogic_Throws()
    {
        var panel = new SpawnerPanel();
        Assert.Throws<ArgumentNullException>(() => panel.HandleStartRouteAuthoring(null!));
    }

    // ── Negative: unselected type still forwarded ─────────────────────────────

    [Fact]
    public void HandleActivatePlacementTool_NoTypeSelected_PassesZeroTkbId()
    {
        var spawn = new Mock<ISpawnController>();
        var panel = new SpawnerPanel(SampleCatalog);
        // No HandleTypeSelected called — _selectedType stays at default 0

        panel.HandleActivatePlacementTool(spawn.Object);

        spawn.Verify(s => s.StartPlacementMode(0L, It.IsAny<string?>()), Times.Once);
    }
}
