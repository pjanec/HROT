using Fdp.Toolkit.DER;
using Fdp.Presentation.Panels;
using Hrot.Core.Network;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Tests for <see cref="DerEntityInspectorPanel"/> and the underlying
/// <see cref="IDerEntity.GetAllRawDescriptors"/> contract.
///
/// <para>All tests use neutral descriptor types (EntityInfoDescriptor, EntityMissionDescriptor,
/// MapOverlayDescriptor) to exercise the real DER path.  No ImGui context is required
/// because only the non-drawing helpers and the DER API are exercised.</para>
/// </summary>
public class DerEntityInspectorPanelIosTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DerRepo CreateRepoWithEntities(params int[] entityIds)
    {
        var repo = new DerRepo();
        foreach (var id in entityIds)
            repo.CreateEntity(id, 100);
        return repo;
    }

    // ── GetEntityListRows ─────────────────────────────────────────────────────

    [Fact]
    public void GetEntityListRows_EmptyRepo_ReturnsEmpty()
    {
        var repo = new DerRepo();

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo);

        Assert.Empty(rows);
    }

    [Fact]
    public void GetEntityListRows_NoFilter_ReturnsAllEntityIds()
    {
        var repo = CreateRepoWithEntities(1, 2, 3);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo);

        Assert.Equal(3, rows.Count);
        Assert.Contains(1, rows);
        Assert.Contains(2, rows);
        Assert.Contains(3, rows);
    }

    [Fact]
    public void GetEntityListRows_NumericFilter_ReturnsMatchingId()
    {
        var repo = CreateRepoWithEntities(10, 20, 30);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo, "20");

        Assert.Single(rows);
        Assert.Equal(20, rows[0]);
    }

    [Fact]
    public void GetEntityListRows_NumericFilter_NoMatch_ReturnsEmpty()
    {
        var repo = CreateRepoWithEntities(1, 2, 3);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo, "99");

        Assert.Empty(rows);
    }

    [Fact]
    public void GetEntityListRows_NonNumericFilter_ReturnsEmpty()
    {
        var repo = CreateRepoWithEntities(1, 2, 3);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo, "abc");

        Assert.Empty(rows);
    }

    [Fact]
    public void GetEntityListRows_WhitespaceFilter_ReturnsAll()
    {
        var repo = CreateRepoWithEntities(5, 10);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo, "   ");

        Assert.Equal(2, rows.Count);
    }

    // ── GetAllRawDescriptors ──────────────────────────────────────────────────

    [Fact]
    public void GetAllRawDescriptors_EntityWithNoDescriptors_ReturnsEmpty()
    {
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);

        var raw = entity.GetAllRawDescriptors().ToList();

        Assert.Empty(raw);
    }

    [Fact]
    public void GetAllRawDescriptors_SingleDescriptor_ReturnsCorrectType()
    {
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);
        entity.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "Alpha" });

        var raw = entity.GetAllRawDescriptors().ToList();

        Assert.Single(raw);
        Assert.Equal(typeof(EntityInfoDescriptor), raw[0].Type);
    }

    [Fact]
    public void GetAllRawDescriptors_SingleDescriptor_DataIsBoxedStruct()
    {
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);
        entity.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "Bravo" });

        var raw  = entity.GetAllRawDescriptors().ToList();
        var info = (EntityInfoDescriptor)raw[0].Data;

        Assert.Equal("Bravo", info.Name);
    }

    [Fact]
    public void GetAllRawDescriptors_MultipleDescriptors_ReturnsAll()
    {
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);
        entity.SetDescriptor(new EntityInfoDescriptor   { EntityId = 1, Name = "T-72" });
        entity.SetDescriptor(new EntityMissionDescriptor { EntityId = 1 });
        entity.SetDescriptor(new MapOverlayDescriptor   { EntityId = 1 });

        var raw   = entity.GetAllRawDescriptors().ToList();
        var types = raw.Select(r => r.Type).ToHashSet();

        Assert.Equal(3, raw.Count);
        Assert.Contains(typeof(EntityInfoDescriptor),   types);
        Assert.Contains(typeof(EntityMissionDescriptor), types);
        Assert.Contains(typeof(MapOverlayDescriptor),   types);
    }

    [Fact]
    public void GetAllRawDescriptors_AfterUpdate_ReturnsNewReference()
    {
        // Demonstrates the live-update contract: SetDescriptor creates a new
        // boxed object, so the reference returned by GetAllRawDescriptors changes.
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);
        entity.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "OldName" });

        var refBefore = entity.GetAllRawDescriptors()
            .First(r => r.Type == typeof(EntityInfoDescriptor)).Data;

        entity.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "NewName" });

        var refAfter = entity.GetAllRawDescriptors()
            .First(r => r.Type == typeof(EntityInfoDescriptor)).Data;

        // New SetDescriptor call must produce a new boxed object reference.
        Assert.False(ReferenceEquals(refBefore, refAfter),
            "Expected a new boxed reference after SetDescriptor.");

        // The new data must reflect the updated value.
        Assert.Equal("NewName", ((EntityInfoDescriptor)refAfter).Name);
    }

    [Fact]
    public void GetAllRawDescriptors_MultiPartDescriptor_ExposesParts()
    {
        var repo   = new DerRepo();
        var entity = repo.CreateEntity(1, 100);
        entity.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "Part0" }, partId: 0);
        entity.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "Part1" }, partId: 1);

        var raw = entity.GetAllRawDescriptors().ToList();

        Assert.Equal(2, raw.Count);
        Assert.Contains(raw, r => r.PartId == 0);
        Assert.Contains(raw, r => r.PartId == 1);
    }

    // ── Panel construction / registration ─────────────────────────────────────

    [Fact]
    public void DerEntityInspectorPanel_Construct_DoesNotThrow()
    {
        var ex = Record.Exception(() => new DerEntityInspectorPanel());

        Assert.Null(ex);
    }

    [Fact]
    public void RegisterContextMenuHandler_Null_ThrowsArgumentNullException()
    {
        var panel = new DerEntityInspectorPanel();

        Assert.Throws<ArgumentNullException>(() =>
            panel.RegisterContextMenuHandler(null!));
    }

    [Fact]
    public void NoSelection_ConstantIsZero()
    {
        Assert.Equal(0, DerEntityInspectorPanel.NoSelection);
    }
}

