using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// ⭐ G-7 / CE-4354 (docs/UX/DESIGN_Review_Perspective.md §6.8 M1, §6.9 M2) — the managed components that hold a RAW <see cref="Entity"/>:
    /// a managed component is shared BY REFERENCE into the merged world (M5), except these — they are IMMUTABLE records (CLAUDE.md,
    /// <i>NEW COMPONENTS</i>), so the merge builds a NEW instance with every handle remapped and leaves the sandbox's object alone.
    /// <para>⛔ A new managed type that holds an <see cref="Entity"/> and is not listed here would reach the merged world with a node-local handle:
    /// the rail <c>M2_EveryComponentWithARawEntity_HasARemapEntry</c> walks every registered component type and fails on it.</para>
    /// </summary>
    public static class ManagedEntityRemap
    {
        private static readonly Dictionary<Type, Func<object, Func<Entity, Entity>, object>> Remappers = new()
        {
            // ChildMap.InstanceToEntity : IReadOnlyDictionary<int, Entity>
            [typeof(ChildMap)] = (o, map) =>
            {
                var c = (ChildMap)o;
                var d = new Dictionary<int, Entity>(c.InstanceToEntity.Count);
                foreach (var kv in c.InstanceToEntity) d[kv.Key] = map(kv.Value);
                return c with { InstanceToEntity = d };
            },
            // SubordinateOrders.Orders : IReadOnlyList<Entry>, Entry.Member : Entity
            [typeof(SubordinateOrders)] = (o, map) =>
            {
                var s = (SubordinateOrders)o;
                var arr = new SubordinateOrders.Entry[s.Orders.Count];
                for (int i = 0; i < arr.Length; i++)
                {
                    var e = s.Orders[i];
                    e.Member = map(e.Member);
                    arr[i] = e;
                }
                return s with { Orders = arr };
            },
        };

        /// <summary>The remapper of <paramref name="managedType"/> — a new instance with every raw <see cref="Entity"/> mapped — or false (the type holds none, or is not known).</summary>
        public static bool TryGet(Type managedType, out Func<object, Func<Entity, Entity>, object> remap)
            => Remappers.TryGetValue(managedType, out remap!);

        /// <summary>The managed types that have a remapper.</summary>
        public static IEnumerable<Type> Types => Remappers.Keys;
    }
}
