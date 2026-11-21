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
                // ⭐ CE-1480 G4 — READ first, take the write reference only when the height differs: GetComponentRW stamps the table's chunk
                //   version, and a paused world re-stamped every clamped body's chunk every frame though nothing moved — which kept every
                //   change gate downstream (map culling, the recorder, the view copy) permanently "changed".
                ref readonly var at = ref repo.GetComponentRO<SimTransform>(e);
                float z = terrain.SurfaceZ(at.Position.X, at.Position.Y, at.Position.Z);
                if (z != at.Position.Z) repo.GetComponentRW<SimTransform>(e).Position.Z = z;
            }
        }
    }
}
