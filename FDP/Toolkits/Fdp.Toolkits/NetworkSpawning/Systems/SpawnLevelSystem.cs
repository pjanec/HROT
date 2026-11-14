using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.NetworkSpawning.Systems
{
    /// <summary>
    /// ⭐ <c>CE-3271</c> (Q94 D, R-3018 — 📄 docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md §5.2a) — applies every waiting
    /// <see cref="SpawnLevelRequest"/> on the node that owns the entity's position. PostSimulation (a global system may not run in
    /// Simulation), before Export: after the spawn (8b) and after the
    /// creator's yield cleared its <c>SimTransform</c> authority (both <c>BeforeSync</c>), so a brain that grants the position away never
    /// reads its terrain for it, while the editor, a muscle-creator or a single node resolve it the frame it is born. Built by
    /// <c>EntityCreationPack</c> and scheduled on every host.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class SpawnLevelSystem : IEcsModuleSystem
    {
        private readonly List<Entity> _waiting = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<SpawnLevelRequest>()) return;
            _waiting.Clear();
            foreach (var e in repo.Query().With<SpawnLevelRequest>().WithLifecycle(EntityLifecycle.All).Build())
                if (repo.GetComponentRO<SpawnLevelRequest>(e).Resolved == 0) _waiting.Add(e);
            foreach (var e in _waiting) SpawnLevels.TryResolve(repo, e);
        }
    }
}
