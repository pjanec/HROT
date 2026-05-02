using System;
using System.Collections.Generic;
using Fdp.Core.Logging;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Replication.Events;
using Hrot.Core.Network;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.ExCon.Adapters;

/// <summary>
/// Implements <see cref="IOrbatDataProvider"/> and <see cref="IOrbatController"/>
/// for the ExCon operator station by reading the <see cref="IDerRepo"/> DER entity
/// repository and dispatching commands through <see cref="IExConLogic"/>.
///
/// <para>
/// Tree hierarchy is derived from <see cref="EntityInfoDescriptor.CommanderId"/> (0 = root).
/// </para>
///
/// <para>
/// <b>No ECS imports:</b> this adapter relies solely on <see cref="IDerRepo"/> and
/// never imports <c>Fdp.Core</c>, <c>EntityRepository</c>, or <c>ComponentSystem</c>.
/// </para>
/// </summary>
public sealed class ExConOrbatAdapter : IOrbatDataProvider, IOrbatController
{
    private readonly IDerRepo      _repo;
    private readonly IExConLogic   _logic;
    private readonly ICommandGateway _gateway;
    private readonly HashSet<int>  _expandedNodes = new();

    /// <summary>
    /// Creates an <see cref="ExConOrbatAdapter"/>.
    /// </summary>
    /// <param name="repo">DER entity repository from which the ORBAT tree is built.</param>
    /// <param name="logic">ExCon application-logic facade used for selection and placement commands.</param>
    /// <param name="gateway">Command gateway for dispatching hierarchy changes over DDS.</param>
    public ExConOrbatAdapter(IDerRepo repo, IExConLogic logic, ICommandGateway gateway)
    {
        _repo    = repo     ?? throw new ArgumentNullException(nameof(repo));
        _logic   = logic    ?? throw new ArgumentNullException(nameof(logic));
        _gateway = gateway  ?? throw new ArgumentNullException(nameof(gateway));
    }

    // ── IOrbatDataProvider ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(
        string filterText, HashSet<int> expandedNodes)
    {
        // ── Build parent → children map in a single O(n) pass ────────────────
        var children    = new Dictionary<int, List<int>>(); // commanderId → child entity IDs
        var rootIds     = new List<int>();

        foreach (var entity in _repo.GetAllEntities())
        {
            if (!entity.HasDescriptor<EntityInfoDescriptor>()) continue;
            var info = entity.GetDescriptor<EntityInfoDescriptor>();

            if (info.CommanderId == 0)
                rootIds.Add(entity.EntityId);
            else
            {
                if (!children.TryGetValue(info.CommanderId, out var siblings))
                {
                    siblings = new List<int>();
                    children[info.CommanderId] = siblings;
                }
                siblings.Add(entity.EntityId);
            }
        }

        // ── BFS walk from root entities ───────────────────────────────────────
        var result = new List<OrbatNodeViewModel>();
        var queue  = new Queue<(int entityId, int depth)>();

        foreach (var rootId in rootIds)
            queue.Enqueue((rootId, 0));

        var visited = new HashSet<int>(); // cycle guard

        while (queue.Count > 0)
        {
            var (entityId, depth) = queue.Dequeue();

            if (!visited.Add(entityId)) continue;

            var entity = _repo.GetEntity(entityId);
            if (entity is null || !entity.HasDescriptor<EntityInfoDescriptor>()) continue;

            var info = entity.GetDescriptor<EntityInfoDescriptor>();
            string name = info.Name ?? string.Empty;

            bool passesFilter = string.IsNullOrEmpty(filterText)
                || name.Contains(filterText, StringComparison.OrdinalIgnoreCase);

            bool hasChildren = children.TryGetValue(entityId, out var childList) && childList.Count > 0;

            if (passesFilter)
            {
                result.Add(new OrbatNodeViewModel(
                    EntityId:        entityId,
                    Name:            name,
                    Depth:           depth,
                    HasChildren:     hasChildren,
                    IsPendingDelete: _logic.IsEntityPendingDelete(entityId),
                    CanAcceptSubordinates: IsCompositeType(entity.TkbType)));
            }

            // Recurse into children when filter is active (scan full subtree) or node is expanded.
            bool shouldExpand = !string.IsNullOrEmpty(filterText)
                || expandedNodes.Contains(entityId);

            if (shouldExpand && childList is not null)
            {
                foreach (var childId in childList)
                    queue.Enqueue((childId, depth + 1));
            }
        }

        return result;
    }

    // ── IOrbatController ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void SelectEntity(int entityId) => _logic.SelectEntity(entityId);

    /// <inheritdoc/>
    public void CreateUnit(long tkbType) => _logic.StartPlacementMode(tkbType, (string?)null);

    /// <inheritdoc/>
    public void ToggleExpanded(int entityId)
    {
        if (!_expandedNodes.Remove(entityId))
            _expandedNodes.Add(entityId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ExCon embarkation is not yet implemented over DDS.
    /// This is a no-op; see Phase 7 for the full implementation.
    /// </remarks>
    public void RequestEmbark(int passengerEntityId, int vehicleEntityId)
    {
        FdpLog<ExConOrbatAdapter>.Warn(
            "[ExConOrbatAdapter] ExCon embarkation not yet implemented over DDS " +
            "(passenger={0}, vehicle={1}).", passengerEntityId, vehicleEntityId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ExCon disembarkation is not yet implemented over DDS.
    /// This is a no-op; see Phase 7 for the full implementation.
    /// </remarks>
    public void RequestDisembark(int passengerEntityId)
    {
        FdpLog<ExConOrbatAdapter>.Warn(
            "[ExConOrbatAdapter] ExCon disembarkation not yet implemented over DDS " +
            "(passenger={0}).", passengerEntityId);
    }

    /// <inheritdoc/>
    public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)
    {
        _ = _gateway.SendUpdateAttributeAsync(new UpdateEntityAttributeCommand
        {
            NetworkId          = subordinateEntityId,
            AttributePatchJson = $"{{\"CommanderId\":{commanderEntityId}}}",
        });
    }

    /// <inheritdoc/>
    public void RequestRemoveSubordinate(int subordinateEntityId)
    {
        _ = _gateway.SendUpdateAttributeAsync(new UpdateEntityAttributeCommand
        {
            NetworkId          = subordinateEntityId,
            AttributePatchJson = "{\"CommanderId\":0}",
        });
    }

    private static bool IsCompositeType(long tkbType)
    {
        // 0 = unknown/not yet resolved; treat as non-composite.
        return tkbType != 0;
    }
}
