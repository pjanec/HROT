using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ EXPERIMENT — POC 2 of the threaded editor (CE-4439, <c>exp/threaded-poc-2</c>; 📄 docs/DESIGN_Threaded_Simulation_Presentation.md §12 / §14):
    /// the two-world copies. The SIMULATION ticks World S on the worker; the PRESENTATION keeps World A (the object every binding is keyed by).
    /// <list type="bullet">
    /// <item><see cref="CloneForSimulation"/> — S is built ONCE from A: every component table, the entity index, both clocks and EVERY singleton
    /// (an unmanaged one copied by value, a managed one as the same object in S's own table).</item>
    /// <item><see cref="SyncPresentationFrom"/> — after each tick, A takes S's entity index and the TABLE-masked component tables, and keeps the
    /// component bits it owns itself (types outside the mask — UI-only state like the selection) on every entity still alive with the same
    /// generation. Singletons are copied S→A except the ones A rebuilds for itself.</item>
    /// <item><see cref="SyncRowsFrom"/> — the ROW copy: every component of the listed entities (the selected / inspected ones), whatever the mask.</item>
    /// </list>
    /// ⛔ An instrument of the experiment, behind <c>HROT_THREADED_KERNEL=2</c>; nothing calls it with the switch off.
    /// </summary>
    public sealed partial class EntityRepository
    {
        private BitMask512[]? _keepMasks;          // A's own component bits, per entity, across one sync (grown, never shrunk)
        private uint[]? _keepGen;
        private bool[]? _keepActive;

        /// <summary>A new repository holding a full copy of this one (all tables, the index, the clocks) and its OWN copy of every singleton.</summary>
        public EntityRepository CloneForSimulation()
        {
            var s = new EntityRepository();
            s.SyncFrom(this, GetSnapshotableMask(true), includeTransient: true);
            s.CopySingletonsFrom(this, default);
            s.Bus.CopyRegistrationsFrom(Bus);
            s.CopyClockFrom(this);
            return s;
        }

        /// <summary>Copies every singleton of <paramref name="source"/> into tables THIS repository owns (never borrowed), except the ids in
        /// <paramref name="except"/>. Unmanaged values are copied; a managed singleton is the same object in a table of this repository.</summary>
        public void CopySingletonsFrom(EntityRepository source, in BitMask512 except)
        {
            var src = source._singletons;
            if (src == null) return;
            for (int id = 0; id < src.Length; id++)
            {
                if (src[id] is not IComponentTable st || except.IsSet(id)) continue;
                EnsureSingletonCapacity(id);
                if (_singletons[id] is not IComponentTable mine || _borrowedSingletons.IsSet(id) || mine.GetType() != st.GetType())
                {
                    mine = (IComponentTable)Activator.CreateInstance(st.GetType())!;   // first sync only (a type change or a borrowed slot)
                    _singletons[id] = mine;
                    _borrowedSingletons.ClearBit(id);
                }
                mine.SyncFrom(st);
            }
        }

        /// <summary>
        /// The S→A sync of one tick (the caller holds A's lock). The entity index and the <paramref name="tableMask"/> tables come from
        /// <paramref name="source"/>; the component bits OUTSIDE the mask that this repository had on an entity are kept while that entity is still
        /// the same one (alive, same generation) — A's own UI state survives. Singletons: copied except <paramref name="singletonExcept"/>.
        /// </summary>
        public void SyncPresentationFrom(EntityRepository source, in BitMask512 tableMask, in BitMask512 singletonExcept)
        {
            if (AccessTrace.On) AccessTrace.Mute();   // the sync is the ONE legitimate writer of A from the sim thread (WorldGuard)
            try { SyncPresentationCore(source, tableMask, singletonExcept); }
            finally { if (AccessTrace.On) AccessTrace.Unmute(); }
        }

        private void SyncPresentationCore(EntityRepository source, in BitMask512 tableMask, in BitMask512 singletonExcept)
        {
            // 1. remember A's own bits (outside the mask) per live entity
            int max = _entityIndex.MaxIssuedIndex;
            EnsureKeepCapacity(max + 1);
            for (int i = 0; i <= max; i++)
            {
                ref readonly var meta = ref _entityIndex.GetMetadata(i);
                _keepActive![i] = meta.IsActive;
                if (!meta.IsActive) continue;
                _keepGen![i] = meta.Generation;
                var m = _entityIndex.GetComponentMask(i);
                m.BitwiseAndNot(tableMask);
                _keepMasks![i] = m;
            }

            // 2. the structure and the masked tables from S (SyncFrom filters every mask to tableMask)
            SyncFromCore(source, tableMask, includeTransient: true, excludeTypes: null);

            // 3. put A's own bits back on the entities that are still the same ones
            int newMax = _entityIndex.MaxIssuedIndex;
            int upto = Math.Min(max, newMax);
            for (int i = 0; i <= upto; i++)
            {
                if (!_keepActive![i]) continue;
                ref readonly var meta = ref _entityIndex.GetMetadata(i);
                if (!meta.IsActive || meta.Generation != _keepGen![i]) continue;
                ref var m = ref _entityIndex.GetComponentMask(i);
                m.BitwiseOr(_keepMasks![i]);
            }

            // 4. singletons
            CopySingletonsFrom(source, singletonExcept);
        }

        /// <summary>The ROW copy: for each entity in <paramref name="entities"/> (alive in both), every component <paramref name="source"/> has on it
        /// is copied into this repository's table row, and its mask bits set. Used for the selected / inspected entities — never widens the table mask.</summary>
        public void SyncRowsFrom(EntityRepository source, ReadOnlySpan<Entity> entities)
        {
            if (AccessTrace.On) AccessTrace.Mute();
            try { SyncRowsCore(source, entities); }
            finally { if (AccessTrace.On) AccessTrace.Unmute(); }
        }

        private void SyncRowsCore(EntityRepository source, ReadOnlySpan<Entity> entities)
        {
            for (int k = 0; k < entities.Length; k++)
            {
                var e = entities[k];
                if (!source.IsAlive(e) || !IsAlive(e)) continue;
                var srcMask = source._entityIndex.GetComponentMask(e.Index);
                ref var myMask = ref _entityIndex.GetComponentMask(e.Index);
                foreach (var kvp in source._componentTables)
                {
                    var st = kvp.Value;
                    int id = st.ComponentTypeId;
                    if (!srcMask.IsSet(id)) continue;
                    if (!_componentTables.TryGetValue(kvp.Key, out var mine)) continue;   // a type A never registered: the next table sync brings it
                    mine.CopyRowFrom(st, e.Index, _globalVersion);
                    myMask.SetBit(id);
                }
            }
        }

        private void EnsureKeepCapacity(int n)
        {
            if (_keepMasks != null && _keepMasks.Length >= n) return;
            int cap = Math.Max(n, (_keepMasks?.Length ?? 0) * 2);
            Array.Resize(ref _keepMasks, cap);
            Array.Resize(ref _keepGen, cap);
            Array.Resize(ref _keepActive, cap);
        }
    }
}
