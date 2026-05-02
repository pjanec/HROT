namespace Hrot.Common
{
    /// <summary>
    /// Defines the logical role of a simulation node in a distributed deployment.
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
