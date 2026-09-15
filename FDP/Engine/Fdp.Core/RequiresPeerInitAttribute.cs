using System;

namespace Fdp.Core
{
    /// <summary>
    /// Marks a component type whose data a PEER node must INITIALISE (do node-local work for) before the
    /// entity is fully live — e.g. a ground unit whose terrain-clamp baseline the Muscle must sample, or a
    /// navigable unit whose navmesh a Muscle must load. It is the per-component half of the reliable-init
    /// cross-node construction barrier's role-filter (CE-283 piece C):
    /// <c>DESIGN_Cross_Node_Construction_Barrier.md §3b</c>.
    ///
    /// <para>⛔ <b>Role-agnostic by design</b> — it says a component NEEDS peer init, never WHICH role
    /// provides it (roles are labels; <c>DESIGN_Role_Affinity_Ownership</c>). The role→"provides the init"
    /// mapping lives in <c>HrotRoleComponentSets.Initialises</c>. The set of ids is aggregated once,
    /// process-wide, by <see cref="ComponentAttributeSets"/>, exactly as <see cref="BirthCriticalAttribute"/>.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RequiresPeerInitAttribute : Attribute
    {
    }
}
