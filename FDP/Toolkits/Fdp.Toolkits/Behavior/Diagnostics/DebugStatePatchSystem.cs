using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Drains <see cref="PatchDebugStateCommand"/>s from the world bus during the
    /// Input phase, ensures the target entity has a <see cref="DebugState"/>
    /// component, and applies the JSON patch via the expression-tree compiled
    /// setters in <see cref="DebugStatePatchCompiler"/>.
    /// </summary>
    /// <remarks>
    /// Adding the component if missing — never replacing one that already exists —
    /// preserves bits the user has set via other paths (TKB auto-enable, etc.).
    /// Order within <c>SystemPhase.Input</c>: register before any system that
    /// reads <see cref="DebugState"/> in <c>SystemPhase.Simulation</c>.
    /// </remarks>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class DebugStatePatchSystem : IEcsModuleSystem
    {
        public DebugStatePatchSystem()
        {
            // Compile once when the system is constructed. Idempotent.
            DebugStatePatchCompiler.Build();
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(DebugStatePatchSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            foreach (var cmd in repo.Bus.ReadManaged<PatchDebugStateCommand>())
            {
                if (cmd == null) continue;
                if (!repo.IsAlive(cmd.Target)) continue;

                if (!repo.HasComponent<DebugState>(cmd.Target))
                {
                    repo.AddComponent(cmd.Target, new DebugState());
                }

                ref var state = ref repo.GetComponentRW<DebugState>(cmd.Target);
                DebugStatePatchCompiler.ApplyPatch(ref state, cmd.PatchJson);
            }
        }
    }
}
