using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Hrot.IG.Components;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="IOrbatDataProvider"/> and <see cref="IOrbatController"/>
    /// for the offline editor by reading the <see cref="EntityRepository"/> directly.
    ///
    /// <para>
    /// Tree hierarchy is derived from <see cref="EntityInfo.CommanderId"/> (0 = root).
    /// <see cref="GetVisibleNodes"/> rebuilds the entity-index cache on every call so
    /// embark/disembark operations can always locate the correct <see cref="Entity"/>
    /// handle.
    /// </para>
    ///
    /// No DDS or CycloneDDS references.
    /// </summary>
    public sealed class EditorOrbatAdapter : IOrbatDataProvider, IOrbatController
    {
        private readonly EntityRepository _world;
        private readonly FdpEventBus      _bus;
        private readonly IEditorLogic     _logic;
        private readonly ISpawnController _spawn;

        private readonly HashSet<int>         _expandedNodes = new();
        private readonly Dictionary<int, Entity> _indexCache = new();

        /// <param name="world">Entity repository.</param>
        /// <param name="bus">Local FDP event bus for publishing embark/disembark commands.</param>
        /// <param name="logic">Editor logic façade for tool activation and entity selection.</param>
        /// <param name="spawn">
        /// Spawn controller delegated to by <see cref="CreateUnit"/>.
        /// </param>
        public EditorOrbatAdapter(
            EntityRepository world,
            FdpEventBus      bus,
            IEditorLogic     logic,
            ISpawnController spawn)
        {
            _world = world;
            _bus   = bus;
            _logic = logic;
            _spawn = spawn;
        }

        // ── IOrbatDataProvider ────────────────────────────────────────────────

        /// <inheritdoc/>
        public IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(
            string filterText, HashSet<int> expandedNodes)
        {
            // ── Rebuild entity index cache ────────────────────────────────────
            _indexCache.Clear();
            var q = _world.Query().With<EntityInfo>().Build();
            foreach (var entity in q)
                _indexCache[entity.Index] = entity;

            // ── Build parent → children map ──────────────────────────────────
            var children = new Dictionary<int, List<int>>(); // parentIndex → child indices
            var rootIndices = new List<int>();

            foreach (var (idx, _) in _indexCache)
            {
                var entity = _indexCache[idx];
                var info   = _world.GetComponent<EntityInfo>(entity);
                int cmdId  = info.CommanderId;

                if (cmdId == 0)
                {
                    rootIndices.Add(idx);
                }
                else
                {
                    if (!children.ContainsKey(cmdId))
                        children[cmdId] = new List<int>();
                    children[cmdId].Add(idx);
                }
            }

            // ── BFS walk from roots ──────────────────────────────────────────
            var result = new List<OrbatNodeViewModel>();
            var queue  = new Queue<(int entityIdx, int depth)>();

            foreach (var root in rootIndices)
                queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (idx, depth) = queue.Dequeue();

                if (!_indexCache.TryGetValue(idx, out var entity)) continue;
                var info = _world.GetComponent<EntityInfo>(entity);
                string name = info.Name.ToString();

                // Apply filter
                bool passesFilter = string.IsNullOrEmpty(filterText)
                    || name.Contains(filterText, System.StringComparison.OrdinalIgnoreCase);

                if (passesFilter)
                {
                    bool hasChildren = children.ContainsKey(idx) && children[idx].Count > 0;
                    result.Add(new OrbatNodeViewModel(
                        EntityId:       idx,
                        Name:           name,
                        Depth:          depth,
                        HasChildren:    hasChildren,
                        IsPendingDelete: false));
                }

                // Expand children only if this node is expanded (or no expand filter is active)
                bool expanded = expandedNodes.Count == 0 || expandedNodes.Contains(idx) || !passesFilter;
                if (expanded && children.TryGetValue(idx, out var childList))
                {
                    foreach (var child in childList)
                        queue.Enqueue((child, depth + 1));
                }
            }

            return result;
        }

        // ── IOrbatController ─────────────────────────────────────────────────

        /// <inheritdoc/>
        public void SelectEntity(int entityId)
        {
            _logic.ActivateTool(EditorTool.Select);
        }

        /// <inheritdoc/>
        public void CreateUnit(long tkbType)
        {
            _spawn.StartPlacementMode(tkbType, null);
        }

        /// <inheritdoc/>
        public void ToggleExpanded(int entityId)
        {
            if (!_expandedNodes.Remove(entityId))
                _expandedNodes.Add(entityId);
        }

        /// <inheritdoc/>
        public void RequestEmbark(int passengerEntityId, int vehicleEntityId)
        {
            if (!_indexCache.TryGetValue(passengerEntityId, out var passenger) ||
                !_indexCache.TryGetValue(vehicleEntityId,   out var vehicle))
                return;

            _bus.Publish(new EmbarkEntityCommand
            {
                Passenger = passenger,
                Vehicle   = vehicle,
            });
        }

        /// <inheritdoc/>
        public void RequestDisembark(int passengerEntityId)
        {
            if (!_indexCache.TryGetValue(passengerEntityId, out var passenger))
                return;

            _bus.Publish(new DisembarkEntityCommand
            {
                Passenger = passenger,
            });
        }
    }
}
