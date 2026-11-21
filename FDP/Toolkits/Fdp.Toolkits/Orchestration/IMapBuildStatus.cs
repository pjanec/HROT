namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>R-1046</c> (CE-4078, <c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.9 "the seam") — <b>what a node's 3-D map build tells its load chain</b>: a status the chain's map step POLLS on the frame thread
    /// (the chain's existing shape, as <see cref="ILoadProgressSource"/> is read), beside it. ui's <c>Map3DBuild</c> implements it; backend's step resolves on <see cref="IsBuilt"/>, fails the prepare with <see cref="Failure"/>
    /// and feeds <see cref="Step"/> to the heartbeat.
    /// </summary>
    /// <remarks>⭐ <b>Per load:</b> <see cref="IsBuilt"/> refers to the terrain (and SUMO net) the map CURRENTLY holds — a new terrain makes it <see langword="false"/> until its mesh is rebuilt, never a map built for the previous scenario.
    /// A map with nothing to build (no terrain yet, or a host without a window) is <see langword="true"/>: there is nothing to wait for.</remarks>
    public interface IMapBuildStatus
    {
        /// <summary>Every 3-D layer that has a source holds its uploaded mesh for that source's current identity.</summary>
        bool IsBuilt { get; }

        /// <summary>The first layer's build failure (its exception message), or <see langword="null"/>.</summary>
        string? Failure { get; }

        /// <summary>What is building now (<i>"3-D map: terrain"</i>), or empty when nothing is.</summary>
        string Step { get; }

        /// <summary>
        /// ⭐ <c>CE-3354</c> ③ (cross-lane: ui's interface) — ask for the 3-D map NOW: starts every layer's build for the terrain the map holds (idempotent). The load chain's map step calls it at its
        /// commit — after the terrain step's — for a node whose load must wait for its map (the master's map-in-load set, or the node's profile); a node that does not wait never calls it.
        /// <c>Map3DBuild.Request3D</c> already is this member.
        /// </summary>
        void Request3D();
    }
}
