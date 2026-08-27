using Hrot.Common;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior.Events;
using Hrot.Common.Events;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Adapters
{
    /// <summary>
    /// Implements <see cref="IOrbatDataProvider"/> and <see cref="IOrbatController"/>
    /// for the offline editor by reading the <see cref="EntityRepository"/> directly.
    ///
    /// <para>
    /// Tree hierarchy is derived from <c>UnitSubordinate.Commander</c> (no component = root).
    /// <see cref="GetVisibleNodes"/> rebuilds the entity-index cache on every call so
    /// embark/disembark operations can always locate the correct <see cref="Entity"/>
    /// handle.
    /// </para>
    ///
    /// No DDS or CycloneDDS references.
    /// </summary>
    public sealed class ScenarioOrbatAdapter : IOrbatDataProvider, IOrbatController
    {
        private readonly EntityRepository _world;
        private readonly FdpEventBus      _bus;
        private readonly ISpawnController _spawn;

        private readonly HashSet<int>         _expandedNodes = new();
        private readonly Dictionary<int, Entity> _indexCache = new();

        /// <param name="world">Entity repository.</param>
        /// <param name="bus">Local FDP event bus for publishing embark/disembark commands.</param>
        // ⭐ CE-060 — the `IEditorLogic logic` parameter is GONE: its one use is now two shared bus
        //   events (see SelectEntity), which is what makes this adapter host-agnostic.
        /// <param name="spawn">
        /// Spawn controller delegated to by <see cref="CreateUnit"/>.
        /// </param>
        public ScenarioOrbatAdapter(
            EntityRepository world,
            FdpEventBus      bus,
            ISpawnController spawn)
        {
            _world = world;
            _bus   = bus;
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
                Entity commander = Entity.Null;
                if (_world.HasComponent<UnitSubordinate>(entity))
                {
                    commander = _world.GetComponent<UnitSubordinate>(entity).Commander;
                }

                if (commander.IsNull || !_world.IsAlive(commander))
                {
                    rootIndices.Add(idx);
                }
                else
                {
                    int cmdId = commander.Index;
                    if (!children.ContainsKey(cmdId))
                        children[cmdId] = new List<int>();
                    children[cmdId].Add(idx);
                }
            }

            // ── DFS walk from roots ──────────────────────────────────────────
            var result = new List<OrbatNodeViewModel>();
            var visited = new HashSet<int>();

            void CollectNodes(int idx, int depth)
            {
                // Cycle guard
                if (!visited.Add(idx)) return;
                if (!_indexCache.TryGetValue(idx, out var entity)) return;

                var info = _world.GetComponent<EntityInfo>(entity);
                string name = info.Name.ToString();

                // Apply filter
                bool passesFilter = string.IsNullOrEmpty(filterText)
                    || name.Contains(filterText, System.StringComparison.OrdinalIgnoreCase);

                bool hasChildren = children.ContainsKey(idx) && children[idx].Count > 0;

                if (passesFilter)
                {
                    result.Add(new OrbatNodeViewModel(
                        EntityId:       idx,
                        Name:           name,
                        Depth:          depth,
                        HasChildren:    hasChildren,
                        IsPendingDelete: false,
                        CanAcceptSubordinates: _world.HasComponent<UnitRoster>(entity)));
                }

                // Expand children only if this node is expanded (or no expand filter is active)
                bool expanded = expandedNodes.Count == 0 || expandedNodes.Contains(idx) || !passesFilter;
                if (expanded && hasChildren)
                {
                    foreach (var child in children[idx])
                        CollectNodes(child, depth + 1);
                }
            }

            foreach (var root in rootIndices)
                CollectNodes(root, 0);

            return result;
        }

        // ── IOrbatController ─────────────────────────────────────────────────

        /// <inheritdoc/>
        /// <remarks>
        /// ⭐⭐⭐ <c>CE-060</c> — <b>this was the adapter's ONLY editor-facade call, and it also did not do
        /// what its name says.</b> 📐 Measured <c>2026-08-27</c>: the body was
        /// <c>_logic.ActivateTool(EditorTool.Select)</c> — it activated the SELECT TOOL and
        /// ⛔ <b>ignored <paramref name="entityId"/> entirely</b>, so clicking an ORBAT row selected
        /// nothing. ⚠ That is the same class of defect <c>CE-051</c> found in
        /// <c>SelectEntityCommand</c> being an unhandled no-op.
        ///
        /// <para>⭐⭐ Both halves now go through the seams <c>E3</c> built and BOTH hosts register:
        /// <c>ActivateEditorToolEvent</c> → <c>ToolActivationDrainSystem</c>, and
        /// <c>SelectEntityCommand</c> → <c>SelectEntitySystem</c>. ⇒ ⭐ the <c>IEditorLogic</c> dependency
        /// disappears — which is what let this adapter move out of <c>Hrot.Editor</c> at all — and the
        /// method finally selects the entity it was handed.</para>
        ///
        /// <para>⚠ <b>The id is a NETWORK id.</b> <c>IOrbatDataProvider</c>'s nodes carry the network
        /// index, which is what <c>SelectEntityCommand.NetworkId</c> takes and what
        /// <c>SelectEntitySystem</c> resolves — ⛔ not a raw <c>Entity</c> handle.</para>
        /// </remarks>
        public void SelectEntity(int entityId)
        {
            _bus.Publish(new ActivateEditorToolEvent(EditorTool.Select));
            _bus.Publish(new SelectEntityCommand { NetworkId = entityId });
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

        /// <inheritdoc/>
        public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)
        {
            if (!_indexCache.TryGetValue(subordinateEntityId, out var sub) ||
                !_indexCache.TryGetValue(commanderEntityId,   out var cmd))
            {
                FdpLog<ScenarioOrbatAdapter>.Warn(
                    "[ScenarioOrbatAdapter] RequestAssignSubordinate: entity not in cache " +
                    "(subordinate={0}, commander={1}).", subordinateEntityId, commanderEntityId);
                return;
            }

            _bus.Publish(new CmdAssignSubordinate
            {
                Subordinate = sub,
                Commander   = cmd,
                Designation = TacticalDesignation.Undefined,
            });
        }

        /// <inheritdoc/>
        public void RequestRemoveSubordinate(int subordinateEntityId)
        {
            if (!_indexCache.TryGetValue(subordinateEntityId, out var sub))
            {
                FdpLog<ScenarioOrbatAdapter>.Warn(
                    "[ScenarioOrbatAdapter] RequestRemoveSubordinate: entity not in cache " +
                    "(subordinate={0}).", subordinateEntityId);
                return;
            }

            _bus.Publish(new CmdRemoveSubordinate { Subordinate = sub });
        }
    }
}
