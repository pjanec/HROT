using System;
using System.Collections.Generic;
using System.Text;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Diagnostics
{
    /// <summary>
    /// ⭐ An INSTRUMENT (opt-in <c>HROT_NAN_CHECK=1</c>; off ⇒ a <c>static readonly</c> test the JIT folds away, 0 B): after every system the scheduler
    /// runs, the world's <see cref="SimTransform"/> rows are checked for a non-finite position or rotation, and the FIRST system that leaves one on an
    /// entity is named with the entity's components — so a NaN found far downstream (a utility input asserting on a distance) is traced to its
    /// writer in one run. Found while building the threaded editor (CE-4462): a NaN position killed every Debug end-to-end run, switch on or off.
    /// </summary>
    public static class NonFiniteTransformCheck
    {
        /// <summary>The switch, read once.</summary>
        public static readonly bool On = EnvVars.Read(EnvVars.HrotNanCheck) == "1";

        private static readonly HashSet<long> s_reported = new();
        private static readonly Dictionary<EntityRepository, EntityQuery> s_queries = new(ReferenceEqualityComparer.Instance);
        private static readonly object s_lock = new();

        /// <summary>After <paramref name="systemName"/> ran on <paramref name="view"/>.</summary>
        public static void AfterSystem(ISimulationView view, string systemName)
        {
            if (view is not EntityRepository repo) return;
            lock (s_lock)
            {
                if (!s_queries.TryGetValue(repo, out var q)) s_queries[repo] = q = repo.Query().With<SimTransform>().Build();
                foreach (var e in q)
                {
                    ref readonly var t = ref repo.GetComponentRO<SimTransform>(e);
                    if (Finite(t.Position.X) && Finite(t.Position.Y) && Finite(t.Position.Z)
                        && Finite(t.Rotation.X) && Finite(t.Rotation.Y) && Finite(t.Rotation.Z) && Finite(t.Rotation.W)) continue;
                    long key = ((long)repo.GetHashCode() << 32) | (uint)e.Index;
                    if (!s_reported.Add(key)) continue;
                    var sb = new StringBuilder();
                    sb.Append("[NAN-CHECK] entity ").Append(e.Index).Append(" SimTransform ").Append(t.Position).Append(' ').Append(t.Rotation)
                      .Append(" — first seen after system ").Append(systemName).Append(" · components: ");
                    var mask = repo.EntityIndexUntraced.GetComponentMask(e.Index);
                    for (int id = 0; id < 512; id++)
                        if (mask.IsSet(id) && ComponentTypeRegistry.GetType(id) is { } type) sb.Append(type.Name).Append(' ');
                    Console.WriteLine(sb.ToString());
                }
            }
        }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
