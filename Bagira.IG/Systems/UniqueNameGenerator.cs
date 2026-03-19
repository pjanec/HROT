using System;
using Bagira.IG.Components;
using Fdp.Interfaces;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Systems;

/// <summary>
/// Utility for generating unique sequential entity names during a placement session.
///
/// <para>
/// <b>Usage pattern (session counter):</b>
/// <list type="number">
///   <item>
///     At session start, call <see cref="GetMaxIndex"/> once to scan the live ECS
///     world and determine the highest numeric suffix already in use for a given
///     prefix. This establishes the starting point for the new naming sequence.
///   </item>
///   <item>
///     For each entity spawned during the session, add 1 to a per-session counter
///     and append it to the prefix.  Example for "Tank-" with base = 2:
///     first click → "Tank-3", second → "Tank-4", and so on.
///   </item>
/// </list>
/// This approach fixes the naming race condition that occurs when an operator
/// clicks faster than the IG's replication pipeline can confirm the newly
/// created entity, which would cause a pure ECS scan to return the same
/// index repeatedly.
/// </para>
///
/// <para>
/// Thread safety: this class contains no mutable state. The caller is
/// responsible for maintaining the per-session counter in a thread-safe manner
/// (typically a simple captured <c>int</c> inside a lambda, which is always
/// called from the main render/tick thread).
/// </para>
/// </summary>
public static class UniqueNameGenerator
{
    /// <summary>
    /// Scans the live ECS world for <see cref="EntityInfo"/> managed components and
    /// returns the highest numeric suffix found for names that begin with
    /// <paramref name="prefix"/>.
    ///
    /// <para>
    /// Returns <c>0</c> when no matching names are found, meaning the first
    /// generated name will use suffix <c>1</c>.
    /// </para>
    /// </summary>
    /// <param name="world">The entity repository to query. Must not be <c>null</c>.</param>
    /// <param name="prefix">
    /// The name prefix to match (e.g. <c>"Tank-"</c>).
    /// The comparison is case-insensitive.
    /// An empty or whitespace-only prefix falls back to returning <c>0</c>.
    /// </param>
    public static int GetMaxIndex(EntityRepository world, string prefix)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));

        if (string.IsNullOrWhiteSpace(prefix))
            return 0;

        int maxIndex = 0;

        var view  = (ISimulationView)world;
        var query = world.Query().WithManaged<EntityInfo>().Build();
        foreach (var entity in query)
        {
            var data = world.HasManagedComponent<EntityInfo>(entity)
                ? view.GetManagedComponentRO<EntityInfo>(entity)
                : null;
            if (data?.Name == null) continue;

            if (data.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = data.Name.Substring(prefix.Length);
                if (int.TryParse(suffix, out int index) && index > maxIndex)
                    maxIndex = index;
            }
        }

        return maxIndex;
    }

    /// <summary>
    /// Creates a session-scoped name-generator delegate that produces unique
    /// sequential names by combining <paramref name="prefix"/> with an
    /// auto-incrementing suffix.
    ///
    /// <para>
    /// The generator scans the live ECS state once at creation time (via
    /// <see cref="GetMaxIndex"/>) to find the starting offset, then increments a
    /// captured counter on every call so rapid successive invocations never
    /// collide, even before network confirmations arrive.
    /// </para>
    /// </summary>
    /// <param name="world">Live ECS world for the initial index scan.</param>
    /// <param name="prefix">Prefix shared by all names in the session (e.g. <c>"Tank-"</c>).</param>
    /// <returns>
    /// A <c>Func&lt;string&gt;</c> that returns the next name each time it is invoked.
    /// </returns>
    public static Func<string> CreateSessionGenerator(EntityRepository world, string prefix)
    {
        int baseIndex    = GetMaxIndex(world, prefix);
        int sessionCount = 0;

        return () => $"{prefix}{baseIndex + (++sessionCount)}";
    }
}
