using System;
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.ImGui.Utils;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using Fdp.ModuleHost_Core.Abstractions;
using Xunit;
using FdpInspectorState = FDP.Toolkit.ImGui.Abstractions.InspectorState;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for BUG2-E001: "Delete entity" added to the FDP entity inspector context menus
/// in both <c>IgApplication</c> and <c>SimHostVisualization</c>.
///
/// These tests build the same handler lambda that the production code uses, wiring it to a
/// standalone <see cref="EntityRepository"/> so no Raylib window, DDS participant, or full
/// application initialisation is required.
/// </summary>
public class EntityInspectorContextMenuTests : IDisposable
{
    // ── Shared ECS world ──────────────────────────────────────────────────────

    private readonly EntityRepository _world;
    private readonly FdpInspectorState _inspectorState = new();

    public EntityInspectorContextMenuTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<NetworkIdentity>();
    }

    public void Dispose() => _world.Dispose();

    // ── Handler factory ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="LambdaEntityContextMenuHandler"/> that mirrors the "Delete entity"
    /// logic in <c>IgApplication.DrawUI</c> / <c>SimHostVisualization.Initialize</c>.
    /// </summary>
    private LambdaEntityContextMenuHandler BuildHandler() =>
        new LambdaEntityContextMenuHandler((entity, builder) =>
        {
            // Reproduces the production lambda — delete-entity block only.
            builder.AddItem("Delete entity", () =>
            {
                if (_world.IsAlive(entity))
                {
                    if (_world.HasComponent<NetworkIdentity>(entity))
                    {
                        ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(entity);
                        _world.Bus.PublishManaged(new DestroyEntityCommand
                        {
                            NetworkId = netId.Value,
                            Reason    = "inspector-deleted"
                        });
                    }
                    else
                    {
                        _world.DestroyEntity(entity);
                    }

                    if (_inspectorState.SelectedEntity == entity)
                        _inspectorState.SelectedEntity = null;
                }
            });
        });

    // ── Helper: invoke a named item from the context menu ─────────────────────

    private static void InvokeItem(IEntityContextMenuHandler handler, Entity entity, string label)
    {
        var builder = new CaptureContextMenuBuilder();
        handler.PopulateMenu(entity, builder);
        var cb = builder.FindCallback(label)
            ?? throw new InvalidOperationException($"No context menu item with label '{label}'.");
        cb();
    }

    // ── Test 1: networked entity → DestroyEntityCommand published ────────────

    /// <summary>
    /// When "Delete entity" is invoked for an entity that has a <see cref="NetworkIdentity"/>,
    /// a <see cref="DestroyEntityCommand"/> must be published to the event bus with the
    /// correct <c>NetworkId</c>.
    /// </summary>
    [Fact]
    public void DeleteNetworkedEntity_PublishesDestroyEntityCommand()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new NetworkIdentity { Value = 42L });

        var handler = BuildHandler();

        DestroyEntityCommand? captured = null;
        // Consume the managed event after invoking the action.
        InvokeItem(handler, entity, "Delete entity");
        _world.Bus.SwapBuffers();
        foreach (var cmd in ((ISimulationView)_world).ConsumeManagedEvents<DestroyEntityCommand>())
            captured = cmd;

        Assert.NotNull(captured);
        Assert.Equal(42L, captured!.NetworkId);
    }

    // ── Test 2: local entity → DestroyEntity called directly ─────────────────

    /// <summary>
    /// When "Delete entity" is invoked for an entity without a <see cref="NetworkIdentity"/>,
    /// the entity must be destroyed directly (i.e. <c>_world.IsAlive</c> returns
    /// <c>false</c> after the callback).
    /// </summary>
    [Fact]
    public void DeleteLocalEntity_CallsDestroyEntity()
    {
        var entity = _world.CreateEntity();
        // No NetworkIdentity component → local path.

        var handler = BuildHandler();
        InvokeItem(handler, entity, "Delete entity");

        // After direct destruction the entity is no longer alive.
        Assert.False(_world.IsAlive(entity));
    }

    // ── Test 3: selected entity → inspector selection cleared ────────────────

    /// <summary>
    /// When the deleted entity is the currently selected entity in the FDP inspector,
    /// <see cref="FdpInspectorState.SelectedEntity"/> must be set to <c>null</c>.
    /// </summary>
    [Fact]
    public void DeleteSelectedEntity_ClearsSelection()
    {
        var entity = _world.CreateEntity();
        _inspectorState.SelectedEntity = entity;

        var handler = BuildHandler();
        InvokeItem(handler, entity, "Delete entity");

        Assert.Null(_inspectorState.SelectedEntity);
    }
}

// ── Test infrastructure ───────────────────────────────────────────────────────

/// <summary>
/// An <see cref="IContextMenuBuilder"/> implementation that records all items added by
/// <see cref="PopulateMenu"/> so tests can find and invoke specific callbacks by label.
/// </summary>
internal sealed class CaptureContextMenuBuilder : IContextMenuBuilder
{
    private readonly List<(string Label, Action Callback)> _items = new();

    public void AddItem(string label, Action callback, bool enabled = true)
        => _items.Add((label, callback));

    public IContextMenuBuilder BeginSubmenu(string label) => this;
    public void EndSubmenu() { }
    public void AddSeparator() { }

    /// <summary>Returns the callback for the first item whose label matches, or <c>null</c>.</summary>
    public Action? FindCallback(string label)
    {
        foreach (var (l, cb) in _items)
            if (l == label) return cb;
        return null;
    }
}
