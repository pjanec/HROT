using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ <c>CE-1446</c> — a 3-D layer whose mesh is built in the background (<see cref="BackgroundMeshBuild{TStreams}"/>): what <see cref="Map3DBuild"/> drives and reads. Implemented by <see cref="TerrainLayer3D"/> and the SUMO
    /// <c>TrafficLayer3D</c>.
    /// </summary>
    public interface IMeshLayer3D
    {
        /// <summary>What the layer builds (<i>"terrain"</i>, <i>"roads"</i>) — the <see cref="IMapBuildStatus.Step"/> text.</summary>
        string BuildName { get; }

        /// <summary>The layer has something to build NOW (a terrain / a net is present).</summary>
        bool HasSource { get; }

        /// <summary>The layer's uploaded mesh is the one for its source's CURRENT identity (⭐ per load).</summary>
        bool IsBuilt { get; }

        /// <summary>The layer's build failure message, or <see langword="null"/>.</summary>
        string? BuildFailure { get; }

        /// <summary>Starts the CPU build for the current source's identity on a worker (idempotent).</summary>
        void RequestBuild();

        /// <summary>FRAME THREAD: uploads a finished result, if one is waiting.</summary>
        void PumpBuild();
    }

    /// <summary>When the 3-D map's meshes are built.</summary>
    public enum Map3DBuildPolicy
    {
        /// <summary>Only after the first request for 3-D (the view switch asks): a host that starts in 2-D (R-1046: CGF, SimHost, the editor).</summary>
        OnFirst3DRequest,
        /// <summary>As soon as a source identity appears (from <see cref="Map3DBuild.Update"/>): a node whose load waits on the map (R-1046: the player).</summary>
        Eager,
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1446</c> (R-1046, <c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.9) — <b>one per map</b>: the 3-D map's build, over its layers. It is the map's <see cref="IMapBuildStatus"/> (the load step's poll: per load, see
    /// <see cref="IsBuilt"/>), applies the <see cref="Map3DBuildPolicy"/> (when the layers' builds start) and is what the <see cref="MapViewSwitch"/> asks before going to 3-D.
    /// A layer (it is one itself, so the canvas updates it every frame — in 2-D too) pumps the finished results onto the GPU from the FRAME thread.
    /// </summary>
    /// <remarks>
    /// ⭐ A host with no window (headless: nothing can be uploaded) has no 3-D map to build: <see cref="IsBuilt"/> is true and nothing starts (§11.9 ④). ⭐ A map with no source (no terrain yet) is built vacuously.
    /// </remarks>
    public sealed class Map3DBuild : IMapLayer, IMapBuildStatus
    {
        private readonly IReadOnlyList<IMeshLayer3D> _layers;
        private readonly Func<bool> _available;

        /// <param name="layers">The map's background-built layers.</param>
        /// <param name="policy">When the builds start.</param>
        /// <param name="available">Whether a 3-D map can be built here at all (a GPU context to upload into); default: Raylib has a window.</param>
        public Map3DBuild(IReadOnlyList<IMeshLayer3D> layers, Map3DBuildPolicy policy = Map3DBuildPolicy.OnFirst3DRequest, Func<bool>? available = null)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            Policy = policy;
            _available = available ?? (() => Raylib.IsWindowReady());
        }

        /// <summary>When the builds start.</summary>
        public Map3DBuildPolicy Policy { get; }

        /// <summary>A 3-D request has been made (the first one starts the <see cref="Map3DBuildPolicy.OnFirst3DRequest"/> builds).</summary>
        public bool Requested { get; private set; }

        /// <summary>Asks for the 3-D map: starts every layer's build (the view switch's first request; idempotent).</summary>
        public void Request3D()
        {
            Requested = true;
            if (!_available()) return;
            foreach (var layer in _layers)
                if (layer.HasSource) layer.RequestBuild();
        }

        /// <inheritdoc/>
        public bool IsBuilt => !_available() || _layers.All(l => !l.HasSource || l.IsBuilt);

        /// <inheritdoc/>
        public string? Failure => _layers.Select(l => l.BuildFailure).FirstOrDefault(f => f != null);

        /// <inheritdoc/>
        public string Step => _available() && _layers.FirstOrDefault(l => l.HasSource && !l.IsBuilt) is { } l ? $"3-D map: {l.BuildName}" : string.Empty;

        /// <summary>The per-frame step (the canvas calls it; a node whose canvas is gated calls it itself): starts the policy's builds and uploads what is finished.</summary>
        public void Update(float dt)
        {
            if (!_available()) return;
            if (Policy == Map3DBuildPolicy.Eager || Requested)
                foreach (var layer in _layers)
                    if (layer.HasSource) layer.RequestBuild();
            foreach (var layer in _layers) layer.PumpBuild();
        }

        string IMapLayer.Name => "3-D map build";
        int IMapLayer.LayerBitIndex => -1;
        void IMapLayer.Draw(RenderContext ctx) { }
        bool IMapLayer.HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        Entity? IMapLayer.PickEntity(Vector2 worldPos) => null;
    }
}
