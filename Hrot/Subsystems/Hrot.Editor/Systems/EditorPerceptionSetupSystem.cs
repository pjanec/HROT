using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;

namespace Hrot.Editor.Systems;

/// <summary>
/// ECS system that processes <see cref="SeedTargetCommand"/> events in the editor world,
/// injecting manually nominated targets into a perceiver entity's
/// <see cref="TargetMemory"/>.
/// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed unsafe class EditorPerceptionSetupSystem : IEcsModuleSystem
{
    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        var cmds = view.ReadEvents<SeedTargetCommand>();
        for (int i = 0; i < cmds.Length; i++)
        {
            ref readonly var cmd = ref cmds[i];

            if (!view.IsAlive(cmd.Perceiver) || !view.IsAlive(cmd.Target)) continue;
            if (!view.HasComponent<TargetMemory>(cmd.Perceiver)) continue;
            if (!view.HasComponent<SimTransform>(cmd.Target)) continue;

            ref var mem              = ref repo.GetComponentRW<TargetMemory>(cmd.Perceiver);
            ref readonly var xfm     = ref view.GetComponentRO<SimTransform>(cmd.Target);

            uint tick = repo.HasSingleton<GlobalTime>()
                ? (uint)repo.GetSingletonUnmanaged<GlobalTime>().FrameNumber
                : 0u;

            TargetMemory.AddOrUpdateTarget(
                ref mem,
                (long)cmd.Target.PackedValue,
                xfm.Position.X,
                xfm.Position.Y,
                cmd.ScoreBoost,
                tick);
        }
    }
}
