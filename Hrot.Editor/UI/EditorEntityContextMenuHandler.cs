using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Vis2D.Abstractions;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Events;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Menus;

namespace Hrot.Editor.UI;

/// <summary>
/// Populates the entity right-click context menu with editor-specific actions
/// (centre, rename, delete, edit overlay/route, and target seeding).
///
/// <para>Implements both <see cref="IEntityContextMenuHandler"/> (called by the
/// panel to build the menu) and <see cref="IEntityActionController"/> (called
/// back by <see cref="SharedContextMenuPopulator"/> to execute the actions).</para>
/// </summary>
public sealed class EditorEntityContextMenuHandler : IEntityContextMenuHandler, IEntityActionController
{
    private readonly EntityRepository _repo;
    private readonly IEditorLogic     _logic;
    private readonly FdpEventBus      _bus;
    private readonly IMapPickService  _pick;
    private readonly ISelectionState  _selection;

    /// <summary>
    /// Initialises the handler with all required dependencies.
    /// </summary>
    public EditorEntityContextMenuHandler(
        EntityRepository repo,
        IEditorLogic     logic,
        FdpEventBus      bus,
        IMapPickService  pick,
        ISelectionState  selection)
    {
        _repo      = repo      ?? throw new ArgumentNullException(nameof(repo));
        _logic     = logic     ?? throw new ArgumentNullException(nameof(logic));
        _bus       = bus       ?? throw new ArgumentNullException(nameof(bus));
        _pick      = pick      ?? throw new ArgumentNullException(nameof(pick));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
    }

    // ── IEntityContextMenuHandler ─────────────────────────────────────────────

    /// <inheritdoc/>
    public void PopulateMenu(Entity entity, IContextMenuBuilder builder)
    {
        if (!_repo.IsAlive(entity)) return;

        // Resolve network identity.
        long networkId = _repo.HasComponent<NetworkIdentity>(entity)
            ? _repo.GetComponent<NetworkIdentity>(entity).Value
            : 0L;

        // Resolve TKB type.
        long tkbType = _repo.HasComponent<TkbIdentity>(entity)
            ? _repo.GetComponent<TkbIdentity>(entity).TkbType
            : 0L;

        bool hasPolyline = _repo.HasManagedComponent<EditablePolyline>(entity);
        bool hasRoute    = _repo.HasManagedComponent<RoutePlan>(entity);

        SharedContextMenuPopulator.PopulateEntityMenu(
            networkId, tkbType, hasPolyline, hasRoute, builder, actions: this);

        // Perception seeding items — only when entity has TargetMemory.
        if (_repo.HasComponent<TargetMemory>(entity))
        {
            int perceiverCount = _selection.SelectedEntities.Count;

            builder.AddSeparator();

            builder.AddItem(
                $"Mark Target for {perceiverCount} Units...",
                async void () =>
                {
                    int targetNetId = await _pick.PickEntityAsync();
                    Entity target   = FindEntityByNetworkId(targetNetId);
                    if (!_repo.IsAlive(target)) return;

                    foreach (var perceiver in _selection.SelectedEntities)
                    {
                        _bus.Publish(new SeedTargetCommand
                        {
                            Perceiver   = perceiver,
                            Target      = target,
                            ScoreBoost  = 1.0f,
                        });
                    }
                });

            builder.AddItem(
                $"Mark Area Targets for {perceiverCount} Units...",
                async void () =>
                {
                    IReadOnlyList<int> targetNetIds = await _pick.PickAreaEntitiesAsync();
                    foreach (var perceiver in _selection.SelectedEntities)
                    {
                        foreach (int netId in targetNetIds)
                        {
                            Entity target = FindEntityByNetworkId(netId);
                            if (!_repo.IsAlive(target)) continue;

                            _bus.Publish(new SeedTargetCommand
                            {
                                Perceiver  = perceiver,
                                Target     = target,
                                ScoreBoost = 1.0f,
                            });
                        }
                    }
                });
        }
    }

    // ── IEntityActionController ───────────────────────────────────────────────

    /// <inheritdoc/>
    public void CenterOnEntity(long entityId) => _logic.CenterOnEntity(entityId);

    /// <inheritdoc/>
    public void DeleteEntity(long entityId) =>
        _bus.PublishManaged(new DestroyEntityCommand { NetworkId = entityId, Reason = "EditorContextMenu" });

    /// <inheritdoc/>
    public void EditOverlay(long entityId)
    {
        _logic.SelectEntity(entityId);
        _logic.ActivateTool(EditorTool.Edit);
    }

    /// <inheritdoc/>
    public void EditRoute(long entityId)
    {
        _logic.SelectEntity(entityId);
        _logic.ActivateTool(EditorTool.Route);
    }

    /// <inheritdoc/>
    public void Rename(long entityId) => _logic.OpenRenameDialog(entityId);

    /// <inheritdoc/>
    public void ActivateMeasureTool() => _logic.ActivateTool(EditorTool.Measure);

    // ── Private helpers ───────────────────────────────────────────────────────

    private Entity FindEntityByNetworkId(long networkId)
    {
        var query = _repo.Query().With<NetworkIdentity>().Build();
        foreach (var e in query)
        {
            if (_repo.GetComponent<NetworkIdentity>(e).Value == networkId)
                return e;
        }
        return default;
    }
}
