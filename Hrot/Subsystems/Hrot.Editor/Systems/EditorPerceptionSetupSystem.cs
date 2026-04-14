using Fdp.Kernel;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;

namespace Hrot.Editor.Systems;

/// <summary>
/// ECS system that processes <see cref="SeedTargetCommand"/> events in the editor world,
/// injecting manually nominated targets into a perceiver entity's
/// <see cref="TargetMemory"/>.
/// </summary>
public sealed unsafe class EditorPerceptionSetupSystem : ComponentSystem
{
    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        var cmds = World.Bus.Consume<SeedTargetCommand>();
        for (int i = 0; i < cmds.Length; i++)
        {
            ref readonly var cmd = ref cmds[i];

            if (!World.IsAlive(cmd.Perceiver) || !World.IsAlive(cmd.Target)) continue;
            if (!World.HasComponent<TargetMemory>(cmd.Perceiver)) continue;
            if (!World.HasComponent<SimTransform>(cmd.Target)) continue;

            ref var mem              = ref World.GetComponentRW<TargetMemory>(cmd.Perceiver);
            ref readonly var xfm     = ref World.GetComponent<SimTransform>(cmd.Target);

            uint tick = World.HasSingleton<GlobalTime>()
                ? (uint)World.GetSingletonUnmanaged<GlobalTime>().FrameNumber
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
