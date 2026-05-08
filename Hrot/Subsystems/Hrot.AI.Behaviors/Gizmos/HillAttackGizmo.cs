using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.AI.Behaviors.Brains;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.AI.Behaviors.Gizmos
{
    [GizmoProjector(typeof(BrainBlackboard), typeof(BehaviorState), typeof(SimTransform))]
    public sealed class HillAttackGizmo : IStatelessGizmo
    {
        // Hash value of PlatoonHillAttack_BT from BehaviorIds (= 3014).
        private const int PlatoonHillAttack_BT = 3014;

        private readonly GizmoSettingsRegistry _settings;

        public HillAttackGizmo(GizmoSettingsRegistry settings)
        {
            _settings = settings;
            HillAttackGizmoSettings.Register(settings);
        }

        public unsafe void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var bs = ref view.GetComponentRO<BehaviorState>(entity);
            if (bs.ActiveBehaviorHash != PlatoonHillAttack_BT)
                return;

            ref readonly var bb = ref view.GetComponentRO<BrainBlackboard>(entity);

            // Project the first bytes of the blackboard memory as PlatoonHillAttackParams.
            PlatoonHillAttackParams p;
            ref var bbMut = ref Unsafe.AsRef(in bb);
            fixed (byte* mem = &bbMut.Memory[0])
                p = *(PlatoonHillAttackParams*)mem;

            var fireStart = new Vector3(p.StartX,         p.StartY,         0f);
            var fireEnd   = new Vector3(p.EndX,           p.EndY,           0f);
            var baseStart = new Vector3(p.BaselineStartX, p.BaselineStartY, 0f);
            var baseEnd   = new Vector3(p.BaselineEndX,   p.BaselineEndY,   0f);

            var fireColor = new Rgba32(0,   0, 255, 220);  // blue  (firing line)
            var baseColor = new Rgba32(0, 200,   0, 220);  // green (baseline)

            draw.DrawLine(fireStart, fireEnd, fireColor, thickness: 2f,
                sizeMode: SizeMode.ScreenPixels);
            draw.DrawLine(baseStart, baseEnd, baseColor, thickness: 2f,
                sizeMode: SizeMode.ScreenPixels);

            bool showSlots = _settings
                .Read(GizmoSettingsRegistry.ComputeHash(HillAttackGizmoSettings.ShowSlots))
                .BoolValue;

            if (showSlots && p.TankSpacing > 0f)
            {
                DrawSlots(draw, fireStart, fireEnd, p.TankSpacing, fireColor, 'F');
                DrawSlots(draw, baseStart, baseEnd, p.TankSpacing, baseColor, 'B');
            }
        }

        private static void DrawSlots(IDebugDrawBuilder draw, Vector3 start, Vector3 end,
            float spacing, Rgba32 color, char prefix)
        {
            float length = Vector3.Distance(start, end);
            if (length < 0.01f) return;

            int count = Math.Min(8, (int)(length / spacing) + 1);
            for (int i = 0; i < count; i++)
            {
                float t   = count == 1 ? 0f : (float)i / (count - 1);
                var pos   = Vector3.Lerp(start, end, t);
                draw.DrawSphere(pos, 2f, color);
                var label = new FixedString32($"{prefix}{i + 1}");
                draw.DrawText(pos.X, pos.Y, label, color);
            }
        }
    }
}
