using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.CarKinem.Systems;

namespace FDP.Toolkit.CarKinem.Modules
{
    /// <summary>
    /// Grouping for ground-vehicle physics and spatial management systems
    /// (the "Ground Muscle" layer of the Brain/Muscle decomposition).
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="number">
    ///   <item><see cref="SpatialHashSystem"/> — builds spatial grid from SimTransform positions</item>
    ///   <item><see cref="FormationTargetSystem"/> — computes formation slot targets</item>
    ///   <item><see cref="VehicleCommandSystem"/> — processes high-level vehicle command events</item>
    ///   <item><see cref="CarKinematicsSystem"/> — vehicle physics for wheeled/tracked entities</item>
    ///   <item><see cref="NavigationExecutionSystem"/> — writes NavigationStatus based on arrival/frustration</item>
    /// </list>
    ///
    /// <para>
    /// Note: <c>LinearKinematicsSystem</c> (from <c>FDP.Toolkit.Physics</c>) cannot be
    /// included here due to a circular project reference
    /// (<c>FDP.Toolkit.Physics</c> already references <c>FDP.Toolkit.CarKinem</c>).
    /// It is registered separately by the <c>SimulationLogicModule</c> facade.
    /// </para>
    ///
    /// <para>
    /// All queries within <see cref="CarKinematicsSystem"/> use
    /// <c>.WithOwned&lt;SimTransform&gt;()</c> rather than manual
    /// <c>NetworkOwnership</c> checks, ensuring correct distributed split-authority
    /// behavior (MOD1 §3.2.5).
    /// </para>
    /// </summary>
    public sealed class GroundKinematicsModule
    {
        private readonly RoadNetworkBlob          _roadNetwork;
        private readonly TrajectoryPoolManager     _trajectoryPool;
        private readonly FormationTemplateManager  _formationTemplates;

        /// <param name="roadNetwork">
        ///   Road network blob for <see cref="CarKinematicsSystem"/>.
        ///   A default (empty) blob is valid for tests and maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        ///   Shared trajectory pool for <see cref="CarKinematicsSystem"/> and
        ///   <see cref="FormationTargetSystem"/>. A new pool is created when <c>null</c>.
        /// </param>
        /// <param name="formationTemplates">
        ///   Formation layout templates for <see cref="FormationTargetSystem"/>.
        ///   A new manager (with default templates) is created when <c>null</c>.
        /// </param>
        public GroundKinematicsModule(
            RoadNetworkBlob roadNetwork = default,
            TrajectoryPoolManager? trajectoryPool = null,
            FormationTemplateManager? formationTemplates = null)
        {
            _roadNetwork        = roadNetwork;
            _trajectoryPool     = trajectoryPool     ?? new TrajectoryPoolManager();
            _formationTemplates = formationTemplates ?? new FormationTemplateManager();
        }

        /// <summary>Shared trajectory pool (exposed for visualization and test observation).</summary>
        public TrajectoryPoolManager TrajectoryPool => _trajectoryPool;

        /// <summary>Shared formation-template manager.</summary>
        public FormationTemplateManager FormationTemplates => _formationTemplates;

        /// <summary>
        /// Registers the ground kinematics systems into the provided simulation group.
        /// </summary>
        public void RegisterSystems(SystemGroup group)
        {
            group.AddSystem(new SpatialHashSystem());
            group.AddSystem(new FormationTargetSystem(_formationTemplates, _trajectoryPool));
            group.AddSystem(new VehicleCommandSystem());
            group.AddSystem(new CarKinematicsSystem(_roadNetwork, _trajectoryPool));
            group.AddSystem(new NavigationExecutionSystem());
            // LinearKinematicsSystem was previously registered directly in SimulationLogicModule
            // because FDP.Toolkit.Physics→CarKinem made it circular.
            // Now that the system lives in FDP.Toolkit.CarKinem.Systems, it can be
            // owned here natively (CT-MOD1-F).
            group.AddSystem(new LinearKinematicsSystem());
        }
    }
}
