using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Systems
{
    /// <summary>
    /// ⭐ CE-3293 S5 (R-3036 §1b; docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §8, approved "split") — the paused-time half
    /// of <see cref="CarKinematicsSystem"/>: a vehicle or person DRAGGED while time stands still lands on the surface under it at
    /// once, as it did when the movement model ran on paused frames too. On a frame that advances the movement model itself holds
    /// it to the surface, so this system does nothing then. Same bodies (owned, not carried, not descending) and the same per-entity
    /// clamping flag as the movement model.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class GroundClampWhilePausedSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime > 0f || view is not EntityRepository repo) return;   // advancing: the movement model clamps
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(repo);
            if (terrain == null) return;
            bool clampFlags = repo.IsComponentTypeRegistered<Fdp.Modules.Geographic.Components.GroundClampingConfig>();
            foreach (var e in CarKinematicsSystem.MovedBodies(repo))   // built per run, as the movement model does (tables come late)
            {
                if (!CarKinematicsSystem.IsClamped(repo, e, clampFlags)) continue;
                ref var tf = ref repo.GetComponentRW<SimTransform>(e);
                float z = terrain.SurfaceZ(tf.Position.X, tf.Position.Y, tf.Position.Z);
                if (z != tf.Position.Z) tf.Position.Z = z;
            }
        }
    }
}
