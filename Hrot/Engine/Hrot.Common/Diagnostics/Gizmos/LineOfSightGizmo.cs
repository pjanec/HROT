using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    [GizmoProjector(typeof(TargetMemory), typeof(SimTransform))]
    public sealed class LineOfSightGizmo : IStatelessGizmo
    {
        public unsafe void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var mem = ref view.GetComponentRO<TargetMemory>(entity);

            if (mem.Count == 0) return;

            uint currentTick = view is EntityRepository repo && repo.HasSingleton<GlobalTime>()
                ? (uint)repo.GetSingletonUnmanaged<GlobalTime>().FrameNumber
                : 0u;

            var perceiverPos = tf.Position;
            for (int i = 0; i < mem.Count; i++)
            {
                uint ageTicks = currentTick >= mem.LastSeenTick[i] ? currentTick - mem.LastSeenTick[i] : 0u;
                if (ageTicks > 60 && currentTick > 0u)
                    continue;

                float ageFade = 1.0f - System.Math.Clamp(ageTicks / 60.0f, 0f, 1f);
                byte startAlpha = (byte)(255 * ageFade);
                byte endAlpha = (byte)(64 * ageFade);
                var targetPos = new Vector3(mem.PositionsX[i], mem.PositionsY[i], 0f);

                draw.DrawLineGradient(
                    perceiverPos,
                    targetPos,
                    new Rgba32(255, 60, 60, startAlpha),
                    new Rgba32(255, 60, 60, endAlpha),
                    thickness: 1.5f,
                    sizeMode: SizeMode.ScreenPixels,
                    target: PipelineTarget.Map2D,
                    layer: 3,
                    style: LineStyle.Dashed);
            }
        }
    }
}
