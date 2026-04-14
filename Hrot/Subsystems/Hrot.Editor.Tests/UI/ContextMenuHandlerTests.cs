using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Toolkit.ImGui.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Editor.UI;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Facades;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests.UI;

// ── Shared test doubles ────────────────────────────────────────────────────────

/// <summary>
/// Recording stub for <see cref="IContextMenuBuilder"/> — accumulates label strings
/// so tests can assert which items were added without an active ImGui render frame.
/// </summary>
internal sealed class RecordingContextMenuBuilder : IContextMenuBuilder
{
    public readonly List<string> Items = new();

    public void AddItem(string label, Action callback, bool enabled = true)
        => Items.Add(label);

    public IContextMenuBuilder BeginSubmenu(string label)
    {
        Items.Add($"[submenu:{label}]");
        return this;
    }

    public void EndSubmenu() { }

    public void AddSeparator() => Items.Add("[separator]");
}

/// <summary>
/// Fake <see cref="ISelectionState"/> with a mutable selected-entity collection.
/// </summary>
internal sealed class FakeSelectionState : ISelectionState
{
    private readonly List<Entity> _selected = new();

    public IReadOnlyCollection<Entity> SelectedEntities => _selected;
    public Entity?                      PrimarySelected { get; set; }
    public Entity?                      HoveredEntity   { get; set; }

    public bool IsSelected(Entity entity) => _selected.Contains(entity);

    public void AddSelected(Entity e) => _selected.Add(e);
}

// ═══════════════════════════════════════════════════════════════════════════════
// A006 — EditorEntityContextMenuHandler
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Unit tests for <see cref="EditorEntityContextMenuHandler"/>.
/// </summary>
public sealed class EditorEntityContextMenuHandlerTests : IDisposable
{
    private readonly EntityRepository     _repo;
    private readonly Mock<IEditorLogic>   _mockLogic;
    private readonly Mock<IMapPickService> _mockPick;
    private          FakeSelectionState    _selection;
    private          FdpEventBus           _bus;

    public EditorEntityContextMenuHandlerTests()
    {
        _repo = new EntityRepository();
        _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterComponent<TkbIdentity>();
        _repo.RegisterManagedComponent<EditablePolyline>();
        _repo.RegisterManagedComponent<RoutePlan>();
        _repo.RegisterComponent<TargetMemory>();

        _bus       = _repo.Bus;
        _mockLogic = new Mock<IEditorLogic>();
        _mockPick  = new Mock<IMapPickService>();
        _selection = new FakeSelectionState();
    }

    public void Dispose() => _repo.Dispose();

    private EditorEntityContextMenuHandler CreateHandler()
        => new(_repo, _mockLogic.Object, _bus, _mockPick.Object, _selection);

    // ── Test 1: entity with EditablePolyline → "Edit Shape" present ──────────

    [Fact]
    public void PopulateMenu_EntityWithEditablePolyline_ContainsEditShapeItem()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new NetworkIdentity(42L));
        _repo.AddComponent(entity, new TkbIdentity { TkbType = 100L });
        _repo.AddComponent(entity, new EditablePolyline());

        var handler = CreateHandler();
        var builder = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, builder);

        Assert.Contains("Edit Shape", builder.Items);
    }

    // ── Test 2: entity without overlay or route → no Edit Shape / Edit Route ─

    [Fact]
    public void PopulateMenu_NeitherPolylineNorRoute_NoEditItems()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new NetworkIdentity(10L));
        _repo.AddComponent(entity, new TkbIdentity { TkbType = 100L });

        var handler = CreateHandler();
        var builder = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, builder);

        Assert.DoesNotContain("Edit Shape", builder.Items);
        Assert.DoesNotContain("Edit Route", builder.Items);
    }

    // ── Test 3: dead entity → builder never called ────────────────────────────

    [Fact]
    public void PopulateMenu_DeadEntity_NoItemsAdded()
    {
        var entity = _repo.CreateEntity();
        _repo.DestroyEntity(entity);

        var handler = CreateHandler();
        var builder = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, builder);

        Assert.Empty(builder.Items);
    }

    // ── Test 4: DeleteEntity publishes DestroyEntityCommand ──────────────────

    [Fact]
    public void DeleteEntity_PublishesDestroyEntityCommandWithCorrectId()
    {
        var handler = CreateHandler();
        handler.DeleteEntity(42L);

        _bus.SwapBuffers();
        var cmds = _bus.ConsumeManaged<DestroyEntityCommand>();

        Assert.Single(cmds);
        Assert.Equal(42L, cmds[0].NetworkId);
    }

    // ── Test 5: entity with TargetMemory + 2 perceivers → "Mark Target for 2 Units..." label

    [Fact]
    public void PopulateMenu_EntityWithTargetMemoryAndTwoPerceivers_MarkTargetLabel()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new NetworkIdentity(99L));
        _repo.AddComponent(entity, new TkbIdentity { TkbType = 0L });
        _repo.AddComponent(entity, new TargetMemory());

        var p1 = _repo.CreateEntity();
        var p2 = _repo.CreateEntity();
        _selection.AddSelected(p1);
        _selection.AddSelected(p2);

        var handler = CreateHandler();
        var builder = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, builder);

        Assert.Contains("Mark Target for 2 Units...", builder.Items);
    }
}
