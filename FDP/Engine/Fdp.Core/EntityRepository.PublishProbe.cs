using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Fdp.Core
{
    /// <summary>⭐ EXPERIMENT (threaded kernel X-1, exp/threaded-kernel) — one table's share of a measured sync.</summary>
    public readonly record struct PublishProbeRow(string Type, bool Managed, double Ms, int DirtyChunks, long Bytes, int Entries);

    /// <summary>⭐ EXPERIMENT — a measured <see cref="EntityRepository.SyncFrom"/>: what a published snapshot of the live world costs.</summary>
    public readonly record struct PublishProbeResult(double TotalMs, double IndexMs, int Tables, int ManagedTables, long DirtyBytes, long CommittedBytes,
                                                     long ManagedEntries, int Entities, long AllocatedBytes);

    public sealed partial class EntityRepository
    {
        /// <summary>
        /// ⭐ EXPERIMENT (threaded kernel X-1) — <see cref="SyncFrom"/> with the same mask semantics, timed per table: the entity index, each
        /// unmanaged table (dirty chunks it copied, its committed bytes), each managed table (its references). <paramref name="perType"/> null = totals only.
        /// ⛔ Not a production path: it allocates its rows.
        /// </summary>
        public PublishProbeResult ProbeSyncFrom(EntityRepository source, BitMask512 effectiveMask, List<PublishProbeRow>? perType)
        {
            // pass 1 (untimed): which tables, how many chunks each will copy
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var plan = new List<(Type Key, IComponentTable Src, IComponentTable Mine, bool Managed, int Dirty)>();
            foreach (var kvp in source._componentTables)
            {
                var src = kvp.Value;
                if (!effectiveMask.IsSet(src.ComponentTypeId)) continue;
                if (!_componentTables.TryGetValue(kvp.Key, out var mine))
                {
                    typeof(EntityRepository).GetMethod(nameof(RegisterComponent))!.MakeGenericMethod(kvp.Key).Invoke(this, new object[] { null! });
                    mine = _componentTables[kvp.Key];
                }
                bool isManaged = mine is IManagedChunkedTable;
                int dirty = !isManaged && mine.GetType().GetMethod("ProbeStats", flags)?.Invoke(mine, new object[] { src }) is ValueTuple<int, long> st ? st.Item1 : 0;
                plan.Add((kvp.Key, src, mine, isManaged, dirty));
            }
            // pass 2 (timed): the copy only
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            _entityIndex.SyncFrom(source._entityIndex);
            _entityIndex.ApplyComponentFilter(effectiveMask);
            double indexMs = sw.Elapsed.TotalMilliseconds;
            var ms = new double[plan.Count];
            for (int i = 0; i < plan.Count; i++)
            {
                sw.Restart();
                plan[i].Mine.SyncFrom(plan[i].Src);
                ms[i] = sw.Elapsed.TotalMilliseconds;
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            _globalVersion = source._globalVersion;
            _simulationTick = source._simulationTick;
            // pass 3 (untimed): sizes
            double total = indexMs; int managed = 0; long dirtyBytes = 0, committed = 0, entries = 0;
            int max = source._entityIndex.MaxIssuedIndex;
            for (int i = 0; i < plan.Count; i++)
            {
                var (key, src, mine, isManaged, dirty) = plan[i];
                total += ms[i];
                long bytes = 0; int count = 0;
                if (!isManaged)
                {
                    if (mine.GetType().GetMethod("ProbeStats", flags)?.Invoke(mine, new object[] { src }) is ValueTuple<int, long> st) bytes = st.Item2;
                    dirtyBytes += (long)dirty * FdpConfig.CHUNK_SIZE_BYTES; committed += bytes;
                }
                else
                {
                    managed++;
                    for (int e = 0; e <= max; e++) if (src.GetRawObject(e) != null) count++;
                    entries += count;
                }
                perType?.Add(new PublishProbeRow(key.Name, isManaged, ms[i], dirty, isManaged ? count * 8L : bytes, count));
            }
            return new PublishProbeResult(total, indexMs, plan.Count, managed, dirtyBytes, committed, entries, source._entityIndex.ActiveCount, alloc);
        }

        /// <summary>⭐ EXPERIMENT (X-1) — the live world's singleton slots: how many are filled, how many a snapshot would share by reference today.</summary>
        public (int Filled, int SharedBySyncFrom, List<string> Names) SingletonCensus()
        {
            var names = new List<string>();
            int filled = 0;
            for (int i = 0; i < _singletons.Length; i++)
            {
                if (_singletons[i] is not { } t) continue;
                filled++;
                var ga = t.GetType().GetGenericArguments();
                names.Add(ga.Length > 0 ? ga[0].Name : t.GetType().Name);
            }
            return (filled, 12, names);
        }
    }
}
