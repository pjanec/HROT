using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Helpers for composition roots that fuse more than one role's system list.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists (<c>CE-165</c>).</b> A node's capability set is the <i>union</i> of its
    /// roles, and two roles routinely carry the same role-independent system — <c>UnitHierarchySystem</c>
    /// and <c>EqsResultUpdateSystem</c> are in both <c>CgfLogicPack</c> (Brain) and
    /// <c>SimHostCoreLogicPack</c> (MuscleGround). Concatenating the two lists therefore registers each
    /// twice unless the root deduplicates. Four roots fuse those two packs; three deduplicated by type and
    /// each had written its own copy of the loop, which is why the fourth being wrong went unnoticed.</para>
    ///
    /// <para><b>First wins.</b> Order is preserved and the first occurrence is kept, matching what the
    /// existing roots already did — the earlier list is the one whose scheduling neighbours were reasoned
    /// about.</para>
    ///
    /// <para><b>This is the belt, not the braces.</b> <see cref="SingleInstanceAttribute"/> is the braces:
    /// deduplicating here keeps a legitimate union from tripping the guard, while the guard still catches a
    /// root that forgets to. Neither replaces the other — a root that silently drops duplicates without the
    /// guard hides composition defects, and a guard without this makes every union throw.</para>
    /// </remarks>
    public static class SystemComposition
    {
        /// <summary>
        /// Concatenates system sequences, keeping only the FIRST instance of each concrete system type.
        /// </summary>
        public static IEnumerable<IEcsModuleSystem> DistinctByType(
            params IEnumerable<IEcsModuleSystem>[] sequences)
        {
            if (sequences == null) throw new ArgumentNullException(nameof(sequences));

            var seen = new HashSet<Type>();
            foreach (var sequence in sequences)
            {
                if (sequence == null) continue;
                foreach (var system in sequence)
                {
                    if (system == null) continue;
                    if (seen.Add(system.GetType()))
                        yield return system;
                }
            }
        }
    }
}
