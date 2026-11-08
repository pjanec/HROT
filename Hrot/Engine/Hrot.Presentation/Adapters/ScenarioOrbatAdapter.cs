using Hrot.Common;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
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

        // ⭐ CE-4213 (HV-4) — the parent rule of each relation; ONE tree builder below serves both.
        private readonly CommandHierarchyAdapter   _command   = new();
        private readonly StructureHierarchyAdapter _structure = new();
        private readonly ScenarioHierarchyAdapter  _scenario  = new();

        /// <summary>⭐ CE-4213 ⑥ — the row keys of the Scenario view's two synthetic group rows (negative: no entity index ever is).</summary>
        public const int GroupSavedId = -1, GroupTransientId = -2;

        /// <summary>⭐ CE-4213 ⑥ — why the last drop was refused (e.g. <i>"from macro test/fire-team — edit the macro or its args"</i>), or null; the panel
        /// shows it. Cleared by the next drop that goes through.</summary>
        public string? LastDropRefusal { get; private set; }

        /// <inheritdoc/>
        public EntityTreeRelation Relation { get; set; } = EntityTreeRelation.Command;

        private IHierarchyAdapter Adapter => Relation switch
        {
            EntityTreeRelation.Structure => _structure,
            EntityTreeRelation.Scenario  => _scenario,
            _                            => _command,
        };

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
            // ⭐ CE-4213 — the members of the selected relation (Command: the named entities; Structure: + parts and embarked ones).
            var adapter = Adapter;
            _indexCache.Clear();
            foreach (var entity in _world.Query().With<EntityInfo>().Build())
                _indexCache[entity.Index] = entity;
            if (Relation == EntityTreeRelation.Scenario && _world.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.MacroExpansionStatus>())
                foreach (var entity in _world.Query().With<Fdp.Toolkit.Scenario.MacroExpansionStatus>().Build())   // ⭐ CE-4213 ⑥ — an instance has a name only if someone gave it one
                    _indexCache[entity.Index] = entity;
            if (Relation == EntityTreeRelation.Structure)
            {
                if (_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>())
                    foreach (var entity in _world.Query().With<Fdp.Toolkit.Replication.Components.PartMetadata>().Build())
                        _indexCache[entity.Index] = entity;
                if (_world.IsComponentTypeRegistered<IsEmbarkedTag>())
                    foreach (var entity in _world.Query().With<IsEmbarkedTag>().Build())
                        _indexCache[entity.Index] = entity;
            }

            // ── Build parent → children map (through the selected relation's parent rule) ───
            var children = new Dictionary<int, List<int>>(); // parentIndex → child indices
            var rootIndices = new List<int>();

            foreach (var (idx, _) in _indexCache)
            {
                var entity = _indexCache[idx];
                if (!adapter.Includes(_world, entity)) continue;
                var commander = adapter.GetParent(_world, entity);

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

            // ⭐ CE-4213 ⑥ — the Scenario view files its roots under two synthetic group rows, Saved and Transient (the rows have no entity: negative keys).
            if (Relation == EntityTreeRelation.Scenario)
            {
                var saved = new List<int>(); var transient = new List<int>();
                foreach (var r in rootIndices)
                    (ScenarioHierarchyAdapter.GroupOf(_world, _indexCache[r]) == ScenarioHierarchyAdapter.Transient ? transient : saved).Add(r);
                children[GroupSavedId] = saved;
                children[GroupTransientId] = transient;
                rootIndices = new List<int>();
                if (saved.Count > 0) rootIndices.Add(GroupSavedId);
                if (transient.Count > 0) rootIndices.Add(GroupTransientId);
            }

            void CollectNodes(int idx, int depth)
            {
                // Cycle guard
                if (!visited.Add(idx)) return;
                if (idx < 0)   // a Scenario-view group row
                {
                    var members = children[idx];
                    result.Add(new OrbatNodeViewModel(idx, $"{(idx == GroupSavedId ? ScenarioHierarchyAdapter.Saved : ScenarioHierarchyAdapter.Transient)} ({members.Count})",
                        depth, HasChildren: members.Count > 0, IsPendingDelete: false, CanAcceptSubordinates: false, IsGroup: true, IconKey: "orbat/group"));
                    if (expandedNodes.Contains(idx) || !string.IsNullOrEmpty(filterText))   // a filter opens the groups so its matches show
                        foreach (var child in members) CollectNodes(child, depth + 1);
                    return;
                }
                if (!_indexCache.TryGetValue(idx, out var entity)) return;

                string name = NameOf(entity, adapter);

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
                        CanAcceptSubordinates: Relation == EntityTreeRelation.Command && _world.HasComponent<UnitRoster>(entity),   // ⭐ CE-4213: Structure has no drop targets
                        NetworkId:      NetworkIdOf(entity),
                        IsDestroyed:    IsDestroyed(entity),   // ⭐ CE-1350
                        IconKey:        IconKeyOf(entity)));   // ⭐ U-4 (CE-4334)
                }

                // Expand children only if this node is expanded (or no expand filter is active)
                // ⭐ CE-1272 — only the nodes the panel expanded (or a filtered-out node, so a matching descendant shows). The old
                //   "nothing expanded ⇒ expand everything" shortcut showed a collapsed platoon's squads again the moment its row was
                //   the last expanded one; the tree now starts collapsed, as ExCon's does.
                bool expanded = expandedNodes.Contains(idx) || !passesFilter;
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

        /// <summary>The row text: the entity's own name; a part with none is named from its parent plus the part kind.</summary>
        private string NameOf(Entity entity, IHierarchyAdapter adapter)
        {
            if (_world.HasComponent<EntityInfo>(entity))
                return _world.GetComponent<EntityInfo>(entity).Name.ToString();
            if (ScenarioHierarchyAdapter.MacroPathOf(_world, entity) is { } macro) return $"macro {macro}";   // ⭐ CE-4213 ⑥ — an unnamed instance

            var parent = adapter.GetParent(_world, entity);
            string? parentName = !parent.IsNull && _world.HasComponent<EntityInfo>(parent)
                ? _world.GetComponent<EntityInfo>(parent).Name.ToString() : null;
            if (_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()
                && _world.HasComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(entity))
                return PartLabels.Describe(_world, entity, parentName);   // ⭐ CE-4213 — the part's KIND from its component (AQ79 §0.10)
            return parentName is null ? $"entity #{entity.Index}" : $"{parentName} · passenger";
        }

        /// <summary>⭐ CE-4213 ⑥ — true (and <see cref="LastDropRefusal"/> set) when the Command view refuses to move <paramref name="child"/>; clears it otherwise.</summary>
        private bool Refused(Entity child, Entity newParent)
        {
            LastDropRefusal = ((IHierarchyAdapter)_command).ReparentRefusal(_world, child, newParent);
            return LastDropRefusal is not null;
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
        /// <para>⭐⭐ <c>CE-1020</c> (<c>2026-10-11</c>) — ⛔ SUPERSEDED: "the id is a NETWORK id". 📐 Measured: the nodes this
        /// provider builds carry the entity INDEX (<see cref="GetVisibleNodes"/>, <c>EntityId: idx</c>), as every other method
        /// here assumes (<c>_indexCache</c>), while <c>SelectEntityCommand</c> is resolved BY NETWORK ID
        /// (<c>SelectionRequestSystem</c>, <c>NetworkIdResolver</c>) ⇒ a click selected nothing, or an unrelated entity whose
        /// network id equalled the row's index. ⭐ The index is resolved here, at the boundary, to the entity's network id.</para>
        /// </remarks>
        public void SelectEntity(int entityId)
        {
            // ⭐ CE-1346 (docs/UX/UX_Feature_Tool_Model.md §4.16) — while the map waits for an entity pick, the row is the pick
            if (PickOffer != null && _indexCache.TryGetValue(entityId, out var offered) && _world.IsAlive(offered) && PickOffer(offered)) return;
            _bus.Publish(new ActivateEditorToolEvent(EditorTool.Select));
            if (_indexCache.TryGetValue(entityId, out var entity) && _world.IsAlive(entity)
                && _world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                && _world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity))
                _bus.Publish(new SelectEntityCommand { NetworkId = _world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity).Value });
        }

        /// <summary>⭐ U-4 (CE-4334, UI Conventions §2) — the row's icon by what the entity IS, from its components (one string constant per kind: nothing is built per frame): a macro instance → the Scenario puzzle piece,
        /// a part of a vehicle → a gear, a unit that takes subordinates → the unit's people, anything else → the entity cube.</summary>
        private string IconKeyOf(Entity entity)
        {
            if (_world.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.MacroExpansionStatus>() && _world.HasComponent<Fdp.Toolkit.Scenario.MacroExpansionStatus>(entity)) return "orbat/scenario";
            if (_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>() && _world.HasComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(entity)) return "orbat/part";
            if (_world.IsComponentTypeRegistered<UnitRoster>() && _world.HasComponent<UnitRoster>(entity)) return "orbat/unit";
            return "thing/entity";
        }

        private bool IsDestroyed(Entity entity)
            => _world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(_world, entity);

        /// <summary>⭐ <c>CE-1350</c> — a double-clicked row: the map's Center View on its entity (the same move as the map menu's).</summary>
        public void CenterOn(int entityId)
        {
            if (_indexCache.TryGetValue(entityId, out var entity) && _world.IsAlive(entity))
                Hrot.ScenarioEditor.Systems.CenterOnEntitySystem.CenterOn(
                    Hrot.ScenarioEditor.Map.MapUiStates.CanvasOf(_world)?.Camera!, _world, entity);
        }

        /// <summary>⭐ <c>CE-1346</c> — offers a clicked row's entity to the map's entity pick in progress (the host sets it to its map pick
        /// adapter's <c>TryOfferEntity</c>); true = the pick took it and the row is NOT selected.</summary>
        public System.Func<Entity, bool>? PickOffer { get; set; }

        /// <inheritdoc/>
        public void CreateUnit(long tkbType)
        {
            _spawn.StartPlacementMode(tkbType, null);
        }

        private int NetworkIdOf(Entity entity)
            => _world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
               && _world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity)
                ? _world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity).Value
                : 0;

        /// <inheritdoc/>
        public void ToggleExpanded(int entityId)
        {
            if (!_expandedNodes.Remove(entityId))
                _expandedNodes.Add(entityId);
        }

        /// <inheritdoc/>
        public void RequestEmbark(int passengerEntityId, int vehicleEntityId)
        {
            if (Relation != EntityTreeRelation.Command) return;   // ⭐ CE-4213: Structure is read-only
            if (!_indexCache.TryGetValue(passengerEntityId, out var passenger) ||
                !_indexCache.TryGetValue(vehicleEntityId,   out var vehicle))
                return;

            Hrot.Common.Orders.EmbarkOrders.Embark(_world, passenger, vehicle);   // ⭐ CE-1230 (OM-3) — the one embark path
        }

        /// <inheritdoc/>
        public void RequestDisembark(int passengerEntityId)
        {
            if (!_indexCache.TryGetValue(passengerEntityId, out var passenger))
                return;

            // ⭐ CE-3110 I-2a — the vehicle is named (the command goes to its owner); ⭐ CE-1230 — through the one disembark path
            Hrot.Common.Orders.EmbarkOrders.Disembark(_world, passenger);
        }

        /// <inheritdoc/>
        public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)
        {
            if (Relation != EntityTreeRelation.Command) return;   // ⭐ CE-4213: Structure is read-only
            if (!_indexCache.TryGetValue(subordinateEntityId, out var sub) ||
                !_indexCache.TryGetValue(commanderEntityId,   out var cmd))
            {
                FdpLog<ScenarioOrbatAdapter>.Warn(
                    "[ScenarioOrbatAdapter] RequestAssignSubordinate: entity not in cache " +
                    "(subordinate={0}, commander={1}).", subordinateEntityId, commanderEntityId);
                return;
            }

            if (Refused(sub, cmd)) return;
            // ⭐ CE-4275 (edit framework F1) — the owner of the subordinate's hierarchy applies it; a non-owner asks it ("CommanderId"), with an outcome.
            //   ⛔ HISTORY: a local CmdAssignSubordinate, which wrote the REPLICA on a non-owner.
            Fdp.Toolkit.Replication.Edits.Edits.Submit(_world, Fdp.Toolkit.Replication.Edits.EditOp.SetCommander(sub, cmd, "Assign to a commander"));
        }

        /// <inheritdoc/>
        public void RequestRemoveSubordinate(int subordinateEntityId)
        {
            if (Relation != EntityTreeRelation.Command) return;   // ⭐ CE-4213: Structure is read-only
            if (!_indexCache.TryGetValue(subordinateEntityId, out var sub))
            {
                FdpLog<ScenarioOrbatAdapter>.Warn(
                    "[ScenarioOrbatAdapter] RequestRemoveSubordinate: entity not in cache " +
                    "(subordinate={0}).", subordinateEntityId);
                return;
            }

            if (Refused(sub, Entity.Null)) return;
            Fdp.Toolkit.Replication.Edits.Edits.Submit(_world, Fdp.Toolkit.Replication.Edits.EditOp.SetCommander(sub, Entity.Null, "Detach from its commander"));   // ⭐ CE-4275 ("CommanderId" 0)
        }
    }
}
