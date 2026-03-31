namespace Hrot.SimHost
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
    ///     No doctrine or BTree — movement orders arrive as <c>NavigationIntent</c>
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
    ///   <item>
    ///     <term><see cref="AllInOne"/></term>
    ///     <description>All subsystems in a single process — the default standalone mode.</description>
    ///   </item>
    /// </list>
    /// </summary>
    public enum NodeRole
    {
        /// <summary>Brain tier: doctrine, mission planning, AI, and cognitive dispatch.</summary>
        Brain,

        /// <summary>Muscle tier: ground kinematics and navigation execution.</summary>
        MuscleGround,

        /// <summary>Image-generator presentation node; no simulation logic.</summary>
        ImageGenerator,

        /// <summary>Perception solver node: LOS, broadphase, and threat evaluation.</summary>
        Perception,

        /// <summary>Navigation solver node: on-demand pathfinding.</summary>
        NavigationSolver,

        /// <summary>All-in-one monolithic node; default for standalone execution.</summary>
        AllInOne,
    }
}
