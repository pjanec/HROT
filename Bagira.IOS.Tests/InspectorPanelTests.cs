using Bagira.BDC.SSTD;
using Bagira.IOS.Panels;
using FDP.Toolkit.DER;

namespace Bagira.IOS.Tests;

/// <summary>
/// Unit tests for <see cref="InspectorPanel"/>.
///
/// <para>All tests operate through the public API
/// (<see cref="InspectorPanel.NotifySelectionChanged"/>,
/// <see cref="InspectorPanel.BuildDescriptorLines"/>,
/// <see cref="InspectorPanel.CachedLines"/>) using real
/// <see cref="DerRepo"/> / <see cref="DerEntity"/> instances.
/// No ImGui context is required.</para>
/// </summary>
public class InspectorPanelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DerRepo Repo, IDerEntity Entity) CreateEntityWithInfo(
        int entityId = 1,
        string name = "Alpha-1",
        eForceIdentifier force = eForceIdentifier.FORCE_FRIENDLY)
    {
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(entityId, 100);
        entity.SetDescriptor(new EntityInfo
        {
            EntityId        = entityId,
            Name            = name,
            ForceIdentifier = force,
            CommanderId     = 0
        });
        return (repo, entity);
    }

    // ── NotifySelectionChanged ────────────────────────────────────────────────

    [Fact]
    public void NotifySelectionChanged_Null_ClearsCacheAndSetsNoSelection()
    {
        var panel       = new InspectorPanel();
        var (_, entity) = CreateEntityWithInfo();

        panel.NotifySelectionChanged(entity);
        Assert.NotEmpty(panel.CachedLines);

        panel.NotifySelectionChanged(null);

        Assert.Equal(PanelConstants.InspectorNoSelection, panel.CachedEntityId);
        Assert.Empty(panel.CachedLines);
    }

    [Fact]
    public void NotifySelectionChanged_Entity_SetsCorrectCachedEntityId()
    {
        var panel       = new InspectorPanel();
        var (_, entity) = CreateEntityWithInfo(entityId: 42);

        panel.NotifySelectionChanged(entity);

        Assert.Equal(42, panel.CachedEntityId);
    }

    [Fact]
    public void NotifySelectionChanged_Entity_PopulatesCachedLines()
    {
        var panel       = new InspectorPanel();
        var (_, entity) = CreateEntityWithInfo();

        panel.NotifySelectionChanged(entity);

        Assert.NotEmpty(panel.CachedLines);
    }

    [Fact]
    public void NotifySelectionChanged_SameEntityCalledTwice_RetainsSameLines()
    {
        var panel       = new InspectorPanel();
        var (_, entity) = CreateEntityWithInfo(entityId: 7);

        panel.NotifySelectionChanged(entity);
        var firstLines = panel.CachedLines.ToList();

        // Second call with the same entity should not change the cache.
        panel.NotifySelectionChanged(entity);
        var secondLines = panel.CachedLines.ToList();

        Assert.Equal(firstLines.Count, secondLines.Count);
        for (int i = 0; i < firstLines.Count; i++)
            Assert.Equal(firstLines[i], secondLines[i]);
    }

    [Fact]
    public void NotifySelectionChanged_DifferentEntity_UpdatesCache()
    {
        var panel     = new InspectorPanel();
        var repo      = new DerRepo();
        var entityA   = repo.CreateEntity(1, 100);
        var entityB   = repo.CreateEntity(2, 200);

        entityA.SetDescriptor(new EntityInfo { EntityId = 1, Name = "Alpha" });
        entityB.SetDescriptor(new EntityInfo { EntityId = 2, Name = "Bravo" });

        panel.NotifySelectionChanged(entityA);
        Assert.Equal(1, panel.CachedEntityId);

        panel.NotifySelectionChanged(entityB);
        Assert.Equal(2, panel.CachedEntityId);

        var nameLine = panel.CachedLines.FirstOrDefault(l =>
            l.Category == "EntityInfo" && l.Field == "Name");
        Assert.NotNull(nameLine);
        Assert.Equal("Bravo", nameLine!.Value);
    }

    // ── BuildDescriptorLines ──────────────────────────────────────────────────

    [Fact]
    public void BuildDescriptorLines_EntityWithNoDescriptors_ReturnsEmptyList()
    {
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);

        var lines = InspectorPanel.BuildDescriptorLines(entity);

        Assert.Empty(lines);
    }

    [Fact]
    public void BuildDescriptorLines_EntityWithEntityInfo_ContainsEntityIdField()
    {
        var (_, entity) = CreateEntityWithInfo(entityId: 5);

        var lines = InspectorPanel.BuildDescriptorLines(entity);

        var entityIdLine = lines.FirstOrDefault(l =>
            l.Category == "EntityInfo" && l.Field == "EntityId");
        Assert.NotNull(entityIdLine);
        Assert.Equal("5", entityIdLine!.Value);
    }

    [Fact]
    public void BuildDescriptorLines_EntityWithEntityInfo_ContainsNameField()
    {
        var (_, entity) = CreateEntityWithInfo(name: "Bravo-7");

        var lines = InspectorPanel.BuildDescriptorLines(entity);

        var nameLine = lines.FirstOrDefault(l =>
            l.Category == "EntityInfo" && l.Field == "Name");
        Assert.NotNull(nameLine);
        Assert.Equal("Bravo-7", nameLine!.Value);
    }

    [Fact]
    public void BuildDescriptorLines_EntityWithEntityInfo_ContainsForceIdentifierField()
    {
        var (_, entity) = CreateEntityWithInfo(force: eForceIdentifier.FORCE_OPPOSING);

        var lines = InspectorPanel.BuildDescriptorLines(entity);

        var forceLine = lines.FirstOrDefault(l =>
            l.Category == "EntityInfo" && l.Field == "ForceIdentifier");
        Assert.NotNull(forceLine);
        Assert.Equal(eForceIdentifier.FORCE_OPPOSING.ToString(), forceLine!.Value);
    }

    [Fact]
    public void BuildDescriptorLines_MultipleDescriptors_LinesCoverAllCategories()
    {
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);
        entity.SetDescriptor(new EntityInfo  { EntityId = 1, Name = "T-72" });
        entity.SetDescriptor(new EntityMaster { EntityId = 1, TkbType = 100 });

        var lines = InspectorPanel.BuildDescriptorLines(entity);

        var categories = lines.Select(l => l.Category).Distinct().ToList();
        Assert.Contains("EntityInfo",   categories);
        Assert.Contains("EntityMaster", categories);
    }

    [Fact]
    public void BuildDescriptorLines_NullEntity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InspectorPanel.BuildDescriptorLines(null!));
    }

    [Fact]
    public void BuildDescriptorLines_LineCount_DoesNotExceedMaxTotalLines()
    {
        // Populate an entity with many descriptors to hit the cap.
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);

        // EntityInfo, EntityMaster, GeoSpatial, EntityDamage cover typical fields
        entity.SetDescriptor(new EntityInfo   { EntityId = 1, Name = "Unit" });
        entity.SetDescriptor(new EntityMaster { EntityId = 1, TkbType = 100 });
        entity.SetDescriptor(new GeoSpatial   { EntityId = 1 });
        entity.SetDescriptor(new EntityDamage { EntityId = 1, Damage = 50f });

        var lines = InspectorPanel.BuildDescriptorLines(entity);

        Assert.True(lines.Count <= PanelConstants.InspectorMaxTotalLines);
    }

    // ── Draw (smoke test) ─────────────────────────────────────────────────────

    [Fact]
    public void Draw_WithMockLogic_DoesNotThrow()
    {
        var panel = new InspectorPanel();
        var logic = new Moq.Mock<IIosLogic>();

        var ex = Record.Exception(() => panel.Draw(logic.Object));

        Assert.Null(ex);
    }
}
