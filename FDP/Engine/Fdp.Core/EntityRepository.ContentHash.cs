using System;

namespace Fdp.Core
{
    public sealed partial class EntityRepository
    {
        /// <summary>
        /// ⭐ N-11 (CE-4473) — a 64-bit FNV-1a digest of the world's CONTENT: every live entity (index, generation, component mask) and the bytes
        /// of every UNMANAGED component it holds, in entity-index then type-id order. Two worlds that ran the same steps from the same start agree
        /// on it exactly when their unmanaged state is identical — the determinism gate (same hash at 1 / 2 / 4 / 8 threads, across runs).
        /// ⚠ Managed components count by PRESENCE only (their content is objects, not bytes). Not a hot-path call: it reads the whole world.
        /// </summary>
        public ulong ContentHash() => ContentHash(null);

        /// <summary><see cref="ContentHash()"/>, reporting each row's own digest to <paramref name="perRow"/> (entity index, type id, digest; type id
        /// -1 = the entity's header) — a determinism rail diffs two runs with it to name the first entity and component that differ.</summary>
        public unsafe ulong ContentHash(Action<int, int, ulong>? perRow)
        {
            const ulong Prime = 1099511628211UL;
            ulong h = 14695981039346656037UL;
            void Mix(ulong v) { for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= Prime; } }

            int max = _entityIndex.MaxIssuedIndex;
            var tables = _tableCache;
            for (int i = 0; i <= max; i++)
            {
                var e = new Entity(i, _entityIndex.GetMetadataUnsafe(i).Generation);
                if (!IsAlive(e)) continue;
                Mix((ulong)i); Mix(e.Generation);
                perRow?.Invoke(i, -1, e.Generation);
                ref var mask = ref _entityIndex.GetComponentMask(i);
                for (int id = 0; id < tables.Length && id < 512; id++)
                {
                    if (!mask.IsSet(id)) continue;
                    Mix((ulong)id);
                    if (tables[id] is FlightRecorder.IUnmanagedComponentTable && tables[id] is IComponentTable t)
                    {
                        byte* p = (byte*)GetComponentPointer(e, id);   // a read: the raw row, no version stamp
                        ulong row = 14695981039346656037UL;
                        for (int k = 0; k < t.ComponentSize; k++) { h ^= p[k]; h *= Prime; row ^= p[k]; row *= Prime; }
                        perRow?.Invoke(i, id, row);
                    }
                }
            }
            return h;
        }
    }
}
