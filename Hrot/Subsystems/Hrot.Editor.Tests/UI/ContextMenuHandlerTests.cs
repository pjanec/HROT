using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Common.Events;
using Hrot.Editor.UI;
using Hrot.IG.Components;
using Hrot.UI.Common.Facades;
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

    private readonly Dictionary<string, Action> _callbacks = new();

    public void AddItem(string label, Action callback, bool enabled = true)
    {
        Items.Add(label);
        _callbacks[label] = callback;
    }

    /// <summary>Invokes the callback registered for the item with the given label.</summary>
    public void TriggerItem(string label) => _callbacks[label]();

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
// JsonEntityContextMenuHandler
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Unit tests for <see cref="JsonEntityContextMenuHandler"/>.
/// </summary>
public sealed class JsonEntityContextMenuHandlerTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly FdpEventBus      _bus;

    public JsonEntityContextMenuHandlerTests()
    {
        _repo = new EntityRepository();
        _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterManagedComponent<ContextMenuState>();
        _bus = _repo.Bus;
    }

    public void Dispose() => _repo.Dispose();

    private JsonEntityContextMenuHandler CreateHandler() => new(_repo, _bus);

    // ── Test 1: entity with MenuJson → correct labels added ──────────────────

    [Fact]
    public void PopulateMenu_EntityWithMenuJson_AddsLabelItems()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new NetworkIdentity(42L));
        _repo.SetManagedComponent(entity, new ContextMenuState
        {
            MenuJson = """[{"id":1,"label":"Move Here"},{"id":2,"label":"Engage"}]""",
        });

        var handler = CreateHandler();
        var builder = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, builder);

        Assert.Contains("Move Here", builder.Items);
        Assert.Contains("Engage", builder.Items);
    }

    // ── Test 2: item click publishes ContextActionTriggered ──────────────────

    [Fact]
    public void PopulateMenu_ItemClicked_PublishesContextActionTriggered()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new NetworkIdentity(42L));
        _repo.SetManagedComponent(entity, new ContextMenuState
        {
            MenuJson = """[{"id":7,"label":"Engage"}]""",
        });

        var handler  = CreateHandler();
        var recording = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, recording);

        recording.TriggerItem("Engage");

        _bus.SwapBuffers();
        var events = _bus.ReadManaged<ContextActionTriggered>();

        Assert.Single(events);
        Assert.Equal(42, events[0].EntityNetworkId);
        Assert.Equal("7", events[0].ActionName);
    }

    // ── Test 3: dead entity → no items ───────────────────────────────────────

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

    // ── Test 4: entity without ContextMenuState → no items ───────────────────

    [Fact]
    public void PopulateMenu_NoContextMenuState_NoItemsAdded()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new NetworkIdentity(10L));

        var handler = CreateHandler();
        var builder = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, builder);

        Assert.Empty(builder.Items);
    }

    // ── Test 5: empty MenuJson → no items ────────────────────────────────────

    [Fact]
    public void PopulateMenu_EmptyMenuJson_NoItemsAdded()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new NetworkIdentity(10L));
        _repo.SetManagedComponent(entity, new ContextMenuState { MenuJson = string.Empty });

        var handler = CreateHandler();
        var builder = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, builder);

        Assert.Empty(builder.Items);
    }

    // ── Test 6: separator in JSON → separator item in menu ───────────────────

    [Fact]
    public void PopulateMenu_SeparatorInJson_AddsSeparatorItem()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new NetworkIdentity(5L));
        _repo.SetManagedComponent(entity, new ContextMenuState
        {
            MenuJson = """[{"id":1,"label":"A"},{"separator":true},{"id":2,"label":"B"}]""",
        });

        var handler = CreateHandler();
        var builder = new RecordingContextMenuBuilder();
        handler.PopulateMenu(entity, builder);

        Assert.Contains("[separator]", builder.Items);
        Assert.Contains("A", builder.Items);
        Assert.Contains("B", builder.Items);
    }
}
