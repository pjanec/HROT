using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐⭐ <b>WHAT a node must make resident — declared ONCE, from its ROLES.</b>
///
/// <para>🔒 <b>User, <c>2026-09-18</c>:</b> <i>"the goal is to unify the handling of the loading phase
/// across host by binding it to the host role, not to the host bootstrap code, while keeping the
/// possibility for each host to override the HOW the handling is done (like stride-simhost loads
/// different data than SimHost because they implement their muscle/perception/navigation/etc… role
/// differently)."</i></para>
///
/// <para>⭐⭐ <b>The split this table exists to enforce:</b> the ROLE declares the <b>WHAT</b> (here, once);
/// the HOST supplies the <b>HOW</b> (an <see cref="ILoadPartProvider"/> per part it must satisfy). ⛔ A
/// host bootstrap decides neither — it can pick a different implementation, never a shorter list.</para>
///
/// <para>📄 The requirement table is OWNED by <c>docs/DESIGN_Node_Roles_And_Policies.md</c> §3.2, which
/// carries the measured consumers; this type is that table in code.
/// 📄 The mechanism is <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.1b.</para>
/// </summary>
public static class RoleLoadRequirements
{
    /// <summary>
    /// ⭐⭐⭐ Required by <b>every ECS node</b>, whatever its roles — ⛔ not role-derived at all.
    ///
    /// <para>🔒 <b>User, <c>2026-09-18</c>:</b> <i>"every ECS enable node should be able to create entities
    /// so every needs the TKB loaded."</i> ⭐ It follows from <c>Q65-A′</c>: every ECS node composes the
    /// FULL genesis pipeline with no opt-out, so a node that can be asked to create an entity must be able
    /// to resolve its template. A node without the scenario's knowledge base holds a creation pipeline it
    /// cannot actually use, and silently resolves against whatever catalogue it built locally.</para>
    /// </summary>
    public static readonly IReadOnlyList<LoadPart> UniversalParts = new[] { LoadPart.KnowledgeBase };

    /// <summary>
    /// ⭐⭐ The parts a node with <paramref name="roles"/> must make resident, in the order they must run:
    /// knowledge base, then terrain, then the scenario's entities.
    ///
    /// <para>⚠ <b>The order is load-bearing, not cosmetic.</b> Entities resolve templates from the
    /// knowledge base and are clamped/collided against terrain, so both must precede them.</para>
    ///
    /// <para>⛔ <b>This is NOT a permission gate</b> — 📄 <c>DESIGN_Node_Roles_And_Policies.md</c> §3.1: a
    /// role never denies a capability. It is the set a correct deployment makes resident, not a list of
    /// what the node is allowed to touch.</para>
    /// </summary>
    public static IReadOnlyList<LoadPart> PartsFor(NodeRole roles)
    {
        var parts = new List<LoadPart>(3);

        // ⭐ Unconditional, and FIRST — see UniversalParts.
        parts.AddRange(UniversalParts);

        // ⭐ Role-derived: the two roles with a measured road-graph consumer.
        //   📐 CarKinematicsSystem (MuscleGround) and PathfindingSolverSystem / NavigationSolverModule
        //   (NavigationSolver). ⛔ Brain, Perception and Map2D have NO reader — Map2D even holds a
        //   RoadNetworkHolder nothing on that host reads. 🔒 "load nothing where nothing reads it".
        if (roles.HasFlag(NodeRole.MuscleGround) || roles.HasFlag(NodeRole.NavigationSolver))
            parts.Add(LoadPart.Terrain);

        // ⭐ Role-derived: Brain is the only role that reads the scenario file, because it is the only one
        //   that also edits and saves it. Every other role receives a replicated world.
        if (roles.HasFlag(NodeRole.Brain))
            parts.Add(LoadPart.ScenarioEntities);

        return parts;
    }
}
