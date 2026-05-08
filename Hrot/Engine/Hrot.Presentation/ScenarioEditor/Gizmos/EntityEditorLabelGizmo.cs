using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.ScenarioEditor.Gizmos
{
    // No [GizmoProjector] because the constructor requires BehaviorRegistry.
    // Registered manually in EditorSubsystem with required types: SimTransform + NetworkIdentity.
    // Emits three world-space text labels east of the entity showing:
    //   Line 1: NetworkIdentity.Value
    //   Line 2: Active behavior name (truncated to 20 chars)
    //   Line 3: HP current/max coloured by ratio
    public sealed class EntityEditorLabelGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 IdColor     = new Rgba32(255, 255, 255, 255);
        private static readonly Rgba32 BehaviorColor = new Rgba32(255, 255,   0, 255);
        private static readonly Rgba32 HpGreen     = new Rgba32(  0, 255,   0, 255);
        private static readonly Rgba32 HpYellow    = new Rgba32(255, 255,   0, 255);
        private static readonly Rgba32 HpRed       = new Rgba32(255,   0,   0, 255);

        // Horizontal offset east of the entity (world metres).
        private const float LabelOffsetX = 12f;
        // Vertical spacing between lines (world metres).
        private const float LineSpacing = 6f;

        private readonly BehaviorRegistry _behaviorRegistry;

        public EntityEditorLabelGizmo(BehaviorRegistry behaviorRegistry)
        {
            _behaviorRegistry = behaviorRegistry ?? throw new ArgumentNullException(nameof(behaviorRegistry));
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<SimTransform>(entity))      return;
            if (!view.HasComponent<NetworkIdentity>(entity))   return;

            ref readonly var tf    = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            float baseX = tf.Position.X + LabelOffsetX;
            float baseY = tf.Position.Y;

            // Line 1: Network ID.
            draw.DrawText(baseX, baseY, new FixedString32($"{netId.Value}"), IdColor);

            // Line 2: Active behavior name.
            float line2Y = baseY - LineSpacing;
            if (view.HasComponent<BehaviorState>(entity))
            {
                ref readonly var bs = ref view.GetComponentRO<BehaviorState>(entity);
                if (_behaviorRegistry.TryGetName(bs.ActiveBehaviorHash, out string? behaviorName)
                    && behaviorName != null)
                {
                    string truncated = behaviorName.Length > 20
                        ? behaviorName.Substring(0, 20)
                        : behaviorName;
                    draw.DrawTextLong(baseX, line2Y, truncated, BehaviorColor);
                }
                else
                {
                    draw.DrawText(baseX, line2Y, new FixedString32("?"), BehaviorColor);
                }
            }

            // Line 3: HP current/max.
            float line3Y = baseY - 2f * LineSpacing;
            if (view.HasComponent<Health>(entity))
            {
                ref readonly var hp = ref view.GetComponentRO<Health>(entity);
                float ratio = hp.Max > 0f ? hp.Current / hp.Max : 0f;
                Rgba32 hpColor = ratio >= 0.66f ? HpGreen
                               : ratio >= 0.33f ? HpYellow
                               : HpRed;
                draw.DrawTextLong(baseX, line3Y, $"HP:{hp.Current:F0}/{hp.Max:F0}", hpColor);
            }
        }
    }
}
