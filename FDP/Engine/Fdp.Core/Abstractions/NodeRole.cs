namespace Fdp.Core
{
    /// <summary>
    /// Defines the logical role of a simulation node in a distributed deployment.
    ///
    /// <para>⭐⭐⭐ <b>WHY THIS LIVES IN <c>Fdp.Core</c> AND NOT IN THE APPLICATION</b>
    /// 🔒 <b>User ruling, <c>2026-09-12</c>:</b> <i>"roles has nothing to do with concrete network, role
    /// enums should be defined independently on network and can be mimicked in specific network data
    /// model if needed… roles can be defined in fdp if needed as they are pretty generic."</i>
    /// ⚠ It was previously declared in <c>Hrot.Common</c> (the <c>Hrot.Core</c> project), which made it
    /// invisible to the engine — and <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.8 needs an engine
    /// seam keyed on roles.</para>
    ///
    /// <para>⛔⛔ <b>WHAT THE ENGINE MAY AND MAY NOT DO WITH THIS.</b> 🔒 Same ruling: <i>"the bitmask for
    /// components is correct approach, fdp should not understand what a brain and muscle really
    /// mean."</i> ⇒ ⭐ the engine may hold, compare and pass these <b>labels</b>; ⛔ it may <b>never</b>
    /// map one to a set of components, a system, or a behaviour. That mapping is a <c>BitMask512</c> of
    /// component ids supplied by the APPLICATION at its composition root
    /// (<c>IRoleAffinityPolicy</c>'s per-role mask table). ⭐ A role here is a name with no semantics
    /// attached — which is exactly what makes it safe to share.</para>
    ///
    /// <para>⛔ <b>NOT a networking concept.</b> A network stack may mirror these values in its own wire
    /// model if it needs to (<c>NodeCapability</c> does), ⛔ but nothing here depends on a participant, a
    /// descriptor or a heartbeat, and a networkless node has roles like any other.</para>
    ///
    /// <para>⚠ <b>The <see cref="ImageGenerator"/> NAME IS KNOWN-WRONG and its rename is OWNED
    /// ELSEWHERE</b> — <c>CE-212</c> / <c>docs/DESIGN_Stride_Node_Modes.md</c> §S10 renames it to
    /// <c>Map2D</c> (semantics unchanged; a vocabulary fix). ⛔ Do not rename it opportunistically: these
    /// names are a CLI surface (<c>SimHostApp.ParseRole</c> does <c>Enum.TryParse</c> on
    /// <c>--role &lt;value&gt;</c>), and that design requires a Roslyn rename run twice and unioned.</para>
    ///
    /// <para>Roles determine which simulation modules and translator packs are
    /// instantiated by <see cref="NodeBootstrapper"/>:</para>
    /// <list type="table">
    ///   <listheader><term>Role</term><description>Installed subsystems</description></listheader>
    ///   <item>
    ///     <term><see cref="Brain"/></term>
    ///     <description>MissionControl + CognitiveRuntime + ActionDispatch + Combat.
    ///     No ground kinematics — entity movement is commanded via <c>NavigationIntent</c>
    ///     to a remote Muscle node.</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="MuscleGround"/></term>
    ///     <description>ActionDispatch + GroundKinematics + Combat.
    ///     No behavior or BTree — movement orders arrive as <c>NavigationIntent</c>
    ///     from a remote Brain node.</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="ImageGenerator"/></term>
    ///     <description>Presentation-only node (IG renderer, no simulation logic).</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="Perception"/></term>
    ///     <description>Autonomous perception systems (LOS, broadphase, threat evaluation).
    ///     Receives sensor requests from Brain and publishes sensor targets back.</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="NavigationSolver"/></term>
    ///     <description>On-demand pathfinding solver. Receives path requests from Brain and
    ///     returns computed routes via DDS.</description>
    ///   </item>
    /// </list>
    /// </summary>
    [System.Flags]
    public enum NodeRole
    {
        /// <summary>No role assigned.</summary>
        None = 0,

        /// <summary>Brain tier: behavior, mission planning, AI, and cognitive dispatch.</summary>
        Brain = 1 << 0,

        /// <summary>Muscle tier: ground kinematics and navigation execution.</summary>
        MuscleGround = 1 << 1,

        /// <summary>Image-generator presentation node; no simulation logic.</summary>
        ImageGenerator = 1 << 2,

        /// <summary>Perception solver node: LOS, broadphase, and threat evaluation.</summary>
        Perception = 1 << 3,

        /// <summary>Navigation solver node: on-demand pathfinding.</summary>
        NavigationSolver = 1 << 4,
    }
}
