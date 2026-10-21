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

    /// <summary>
    /// ⭐⭐ <c>CE-3293</c> (R-3036) — when a system or a module runs. 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c> §1.
    /// <para>⛔ There is no default: every system and every module declares one with <see cref="TickModeAttribute"/>, because 82 of the
    /// 202 production systems must run while time is paused and 116 must not — whichever default, a new system silently gets the wrong one.</para>
    /// </summary>
    public enum TickPolicy
    {
        /// <summary>A group or a wrapper that only forwards to other systems — they decide. Never written in a <see cref="TickModeAttribute"/>.</summary>
        Forwarding = 0,

        /// <summary>Runs only on a frame whose sim time advances — live play or a step; never on a paused frame. Simulation work.</summary>
        WhenTimeAdvances = 1,

        /// <summary>Runs on every rendered frame, paused or not (its delta is 0 while paused): editing, network, operator UI, recording.</summary>
        EveryFrame = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3293</c> (R-3036) — declares a system's or a module's <see cref="TickPolicy"/>. ⛔ Not inherited: a base class cannot hand
    /// its mode down, so every concrete system states its own.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class TickModeAttribute : Attribute
    {
        public TickPolicy Policy { get; }

        public TickModeAttribute(TickPolicy policy)
        {
            if (policy == TickPolicy.Forwarding)
                throw new ArgumentException("Forwarding is not declared: a group or a wrapper carries no TickMode (its members decide).", nameof(policy));
            Policy = policy;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3293</c> — reads a type's <see cref="TickPolicy"/>, throwing (with the type's name) when a system or module declares none.
    /// </summary>
    public static class TickModes
    {
        /// <summary>The policy a system declares; <see cref="TickPolicy.Forwarding"/> for a group or a wrapper.</summary>
        /// <exception cref="InvalidOperationException">the system type carries no <see cref="TickModeAttribute"/>.</exception>
        public static TickPolicy OfSystem(Type systemType)
        {
            if (typeof(ISystemGroup).IsAssignableFrom(systemType) || typeof(IForwardingSystem).IsAssignableFrom(systemType))
                return TickPolicy.Forwarding;
            return Declared(systemType)
                ?? throw new InvalidOperationException(
                    $"System {systemType.FullName} must declare [TickMode(TickPolicy.WhenTimeAdvances)] or [TickMode(TickPolicy.EveryFrame)] " +
                    "(CE-3293, R-3036 — there is no default; see docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md Appendix A).");
        }

        /// <summary>The policy a module declares for its own <c>Tick</c> (and, for a background module, its dispatch).</summary>
        /// <exception cref="InvalidOperationException">the module type carries no <see cref="TickModeAttribute"/>.</exception>
        public static TickPolicy OfModule(Type moduleType)
            => Declared(moduleType)
               ?? throw new InvalidOperationException(
                   $"Module {moduleType.FullName} must declare [TickMode(TickPolicy.WhenTimeAdvances)] or [TickMode(TickPolicy.EveryFrame)] " +
                   "(CE-3293, R-3036 — there is no default; see docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md Appendix B).");

        private static TickPolicy? Declared(Type type)
            => ((TickModeAttribute?)Attribute.GetCustomAttribute(type, typeof(TickModeAttribute), inherit: false))?.Policy;
    }

    /// <summary>
    /// ⭐ <c>CE-3293</c> — a system that only forwards to another (a profiling wrapper): it carries no <see cref="TickModeAttribute"/>;
    /// the system it forwards to decides.
    /// </summary>
    public interface IForwardingSystem : IEcsModuleSystem
    {
    }
}
