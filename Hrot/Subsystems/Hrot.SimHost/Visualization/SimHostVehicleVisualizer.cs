using System.Numerics;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Vis2D.Adapters;
using Fdp.Toolkit.Vis2D.Shapes;
using Raylib_cs;

namespace Hrot.SimHost.Visualization
{
    /// <summary>
    /// Renders each vehicle entity as a colour-coded oriented silhouette,
    /// colour-coded by navigation mode / formation role.
    ///
    /// <para>Shape geometry is driven by the entity's DIS type via
    /// <see cref="DefaultEntityShapeLibrary"/>.  Physical dimensions are read
    /// from <see cref="VehicleParams"/> (length × width), giving each vehicle a
    /// correctly-sized footprint on the map.</para>
    /// </summary>
    public class SimHostVehicleVisualizer : PerspectiveEntityVisualizerBase
    {
        // ── Colour palette (matches CarKinem example) ─────────────────────────
        private static readonly Color ColFormationMember  = new(0,   200, 255, 255); // cyan
        private static readonly Color ColFormationLeader  = new(255, 0,   255, 255); // magenta
        private static readonly Color ColRoadNav          = new(50,  100, 255, 255); // blue
        private static readonly Color ColTrajectoryNav    = new(173, 255, 47,  255); // green-yellow
        private static readonly Color ColDefault          = new(200, 200, 200, 255); // light-grey

        /// <param name="shapeLibrary">Shared entity shape library (injected by the composition root).</param>
        public SimHostVehicleVisualizer(IEntityShapeLibrary shapeLibrary)
            : base(shapeLibrary)
        {
        }

        // ── Domain-specific implementations ──────────────────────────────────

        /// <inheritdoc/>
        protected override Color ResolveColor(ISimulationView view, Entity entity)
        {
            if (view.HasComponent<FormationController>(entity)) return ColFormationLeader;
            if (view.HasComponent<FormationFollower>(entity)) return ColFormationMember;

            if (view.HasComponent<NavState>(entity))
            {
                var nav = view.GetComponentRO<NavState>(entity);
                return nav.Mode switch
                {
                    KinematicsMode.RoadGraph        => ColRoadNav,
                    KinematicsMode.CustomTrajectory => ColTrajectoryNav,
                    _                               => ColDefault,
                };
            }

            if (view.HasComponent<VehicleParams>(entity))
            {
                ref readonly var prm = ref view.GetComponentRO<VehicleParams>(entity);
                var (r, g, b) = VehiclePresets.GetColor(prm.Class);
                return new Color(r, g, b, (byte)255);
            }

            return ColDefault;
        }

        /// <inheritdoc/>
        protected override EntityShapeCondition ResolveCondition(ISimulationView view, Entity entity)
            => EntityShapeCondition.None;

        /// <inheritdoc/>
        public override string? GetHoverLabel(ISimulationView view, Entity entity)
        {
            if (!view.HasComponent<NavState>(entity)) return null;
            var nav = view.GetComponentRO<NavState>(entity);
            string mode = nav.Mode switch
            {
                KinematicsMode.RoadGraph        => "Road",
                KinematicsMode.CustomTrajectory => "Trajectory",
                _                               => nav.Mode.ToString(),
            };
            return $"Nav: {mode}";
        }
    }
}

