using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    [GizmoProjector(typeof(TargetMemory), typeof(SimTransform), Family = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory)]
    public sealed class LineOfSightGizmo : IStatelessGizmo
    {
        public unsafe void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.Contacts)) return;   // ⭐ CE-1203 — its own checkbox (§5e FL-1), split from Perception
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var mem = ref view.GetComponentRO<TargetMemory>(entity);

            if (mem.Count == 0) return;

            uint currentTick = view.Tick;

            var perceiverPos = tf.Position;
            for (int i = 0; i < mem.Count; i++)
            {
                uint ageTicks = currentTick >= mem.LastSeenTick[i] ? currentTick - mem.LastSeenTick[i] : 0u;
                if (ageTicks > 60 && currentTick > 0u)
                    continue;

                float ageFade = 1.0f - System.Math.Clamp(ageTicks / 60.0f, 0f, 1f);
                // ⭐ CE-3117 — at the stored height (a unit on a roof is not on the ground), and a HEARD (anonymous) slot drawn as what
                //   it is: an estimate — dashed, with its uncertainty circle. A sighted slot stays a solid line.
                var targetPos = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                bool heard = TargetMemory.IsAnonymous(in mem, i);

                ContactLine.Draw(draw, perceiverPos, targetPos, ageFade, heard, mem.Radius[i], DebugTraceLayers.Contacts);   // ⭐ CE-1318 BV-52
            }
        }
    }
}
