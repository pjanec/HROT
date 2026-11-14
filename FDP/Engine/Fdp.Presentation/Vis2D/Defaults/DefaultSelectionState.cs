using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Fdp.Toolkit.Vis2D.Defaults
{
    /// <summary>
    /// The in-memory <see cref="ISelectionState"/> — a plain set with no world behind it.
    ///
    /// <para>⛔⛔ <b>NOT for a host that has an ECS world.</b> 📄 <c>docs/UX/UX_Feature_Selection.md</c>
    /// §2.7.4 (<c>UXI-11</c> slice <c>S-1</c>): on such a host the truth is the <c>SelectionState</c>
    /// component, and a parallel <see cref="HashSet{T}"/> here is the second store that made the map
    /// ring and the panels disagree. 📌 Measured <c>2026-09-20</c>: the editor and CGF each held one
    /// of these while <c>SelectionInteractionSystem</c> wrote the component — so a map click moved the
    /// ring and not the Mission Editor, and an inspector click moved neither. ⭐ Both now use
    /// <c>Hrot.ScenarioEditor.Selection.EcsSelectionState</c>, a read-through over the component.</para>
    ///
    /// <para>⭐ <b>It survives for the hosts that genuinely have no world</b> — it is the seed of §2.7.1's
    /// <c>DdsBackedSelectionState</c> (ExCon) — and as the cheap fake for tests that do not need one.
    /// ⚠ <c>NoProductionHostKeepsAParallelSelectionStoreTests</c> pins that no host with a world
    /// constructs it; ⛔ if that rail ever has to be relaxed, the store split is back.</para>
    /// </summary>
    public class DefaultSelectionState : ISelectionState
    {
        private readonly HashSet<Entity> _selectedEntities = new HashSet<Entity>();
        private Entity? _primarySelected;

        public bool IsSelected(Entity entity)
        {
            return _selectedEntities.Contains(entity);
        }

        public IReadOnlyCollection<Entity> SelectedEntities => _selectedEntities;

        /// <inheritdoc/>
        public int Version { get; private set; }

        /// <inheritdoc/>
        public Entity? PrimarySelected
        {
            get => _primarySelected;
            set
            {
                // Setting primary resets the selection to just that one -- "click to select" without
                // the shift/ctrl modifier logic, which belongs to input handling.
                if (_primarySelected != value)
                {
                    _primarySelected = value;
                    _selectedEntities.Clear();
                    if (value.HasValue && value.Value != Entity.Null)
                    {
                        _selectedEntities.Add(value.Value);
                    }
                    Version++;
                }
            }
        }

        public Entity? HoveredEntity { get; set; }

        /// <inheritdoc/>
        public void Add(Entity entity)
        {
            if (entity == Entity.Null) return;
            bool added = _selectedEntities.Add(entity);
            if (added || _primarySelected != entity)
            {
                _primarySelected = entity;
                Version++;
            }
        }

        /// <inheritdoc/>
        public void Remove(Entity entity)
        {
            if (!_selectedEntities.Remove(entity)) return;
            if (_primarySelected == entity)
                _primarySelected = _selectedEntities.Count > 0 ? _selectedEntities.First() : (Entity?)null;
            Version++;
        }

        /// <inheritdoc/>
        public void SetMultiple(IReadOnlyCollection<Entity> entities)
        {
            _selectedEntities.Clear();
            // ⚠ The primary is the FIRST ENTITY GIVEN, tracked as we go -- ⛔ NOT _selectedEntities
            //   .First(). A HashSet does not promise insertion order, so reading the primary back off
            //   it would make "the first becomes primary" true by luck. 📌 The contract is on the
            //   interface; honouring it by accident is how a rail passes until it does not.
            Entity? first = null;
            if (entities != null)
            {
                foreach (var e in entities)
                {
                    if (e == Entity.Null) continue;
                    if (_selectedEntities.Add(e) && first == null) first = e;
                }
            }
            _primarySelected = first;
            Version++;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            if (_selectedEntities.Count == 0 && _primarySelected == null) return;
            _selectedEntities.Clear();
            _primarySelected = null;
            Version++;
        }
    }
}
