using System;

namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// Specifies which phase a system executes in.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class UpdateInPhaseAttribute : Attribute
    {
        public SystemPhase Phase { get; }
        
        public UpdateInPhaseAttribute(SystemPhase phase)
        {
            Phase = phase;
        }
    }

    /// <summary>
    /// Declares that at most ONE instance of this system type may be registered on a node.
    /// A second registration throws instead of silently running the system twice per frame.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists (<c>CE-165</c>).</b> Composition is moving to role-based selection, where a
    /// node's capability set is the <i>union</i> of its roles. A union double-counts anything two roles both
    /// carry, so the guard is a hard prerequisite for that work rather than a nicety — but it is not
    /// hypothetical: the running <c>Hrot.Editor</c> concatenates <c>CgfLogicPack</c> (Brain) and
    /// <c>SimHostCoreLogicPack</c> (MuscleGround) with no deduplication, and both packs carry
    /// <c>UnitHierarchySystem</c> and <c>EqsResultUpdateSystem</c>.</para>
    ///
    /// <para><b>Why it throws rather than skipping.</b> A duplicate registration is a composition defect at
    /// the root, and silently dropping the second copy would hide it — which is how this one survived: three
    /// of the four roots that fuse the two packs deduplicate by type and the fourth does not, so nothing ever
    /// disagreed out loud. The throw names the type and points at the composition root.</para>
    ///
    /// <para><b>Why it is opt-in.</b> Plenty of systems are legitimately registered more than once — per-arm
    /// wrappers, toggled duplicates in the editor's split simulation groups. Only a system that is a
    /// singleton <i>by design</i>, because a second tick corrupts state rather than wasting time, carries
    /// this attribute. <c>UnitHierarchySystem</c> is the measured case: its second pass over the same
    /// non-destructively-read <c>CmdAssignSubordinate</c> events falls through to an unguarded roster append,
    /// inflating <c>UnitRoster.Count</c> until legitimate assignments are rejected at capacity.</para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class SingleInstanceAttribute : Attribute
    {
    }
}
