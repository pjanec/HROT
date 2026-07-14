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

        private readonly BehaviorRegistry _behaviorRegistry;

        // DIAGNOSTIC (temporary): dedup set so the "?" miss log fires once per distinct hash.
        private static readonly System.Collections.Generic.HashSet<int> _loggedMissingHashes = new();

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

            // All three lines share the same world anchor point. Screen-pixel vertical spacing
            // is applied via lineOffsetPx (carried in AnchorGeneration), so the block stays
            // fixed-size and zoom-independent. Negative offsets place the block ABOVE the
            // entity (top-right), stacked HP (top) / behavior / id (just above the entity).

            // Bottom line: Network ID (white), 16 px above the entity.
            draw.DrawText(baseX, baseY, new FixedString32($"{netId.Value}"), IdColor,
                fontSizePx: 13f, lineOffsetPx: -16f);

            // Middle line: Active behavior name (yellow), 30 px above the entity.
            if (view.HasComponent<BehaviorState>(entity))
            {
                ref readonly var bs = ref view.GetComponentRO<BehaviorState>(entity);
                if (_behaviorRegistry.TryGetName(bs.ActiveBehaviorHash, out string? behaviorName)
                    && behaviorName != null)
                {
                    string truncated = behaviorName.Length > 20
                        ? behaviorName.Substring(0, 20)
                        : behaviorName;
                    draw.DrawTextLong(baseX, baseY, truncated, BehaviorColor,
                        fontSizePx: 13f, lineOffsetPx: -30f);
                }
                else
                {
                    // DIAGNOSTIC (temporary): the label could not resolve this entity's
                    // ActiveBehaviorHash to a name — this is the "?" regression. Log the failing
                    // hash, the exact registry instance this gizmo reads, and its full contents,
                    // ONCE per distinct hash so it cannot flood. Compare the instance id here
                    // against the one the ingress/coordinator logs to tell "missing entry" apart
                    // from "wrong registry instance"; the name=id list reveals id/hash mismatch.
                    if (_loggedMissingHashes.Add(bs.ActiveBehaviorHash))
                    {
                        Console.WriteLine(
                            $"[LabelGizmo] '?' — no name for ActiveBehaviorHash={bs.ActiveBehaviorHash} " +
                            $"(NetId={netId.Value}). Reading {_behaviorRegistry.DebugDump()}");
                    }

                    draw.DrawText(baseX, baseY, new FixedString32("?"), BehaviorColor,
                        fontSizePx: 13f, lineOffsetPx: -30f);
                }
            }

            // Top line: HP current/max (coloured by ratio), 44 px above the entity.
            if (view.HasComponent<Health>(entity))
            {
                ref readonly var hp = ref view.GetComponentRO<Health>(entity);
                float ratio = hp.Max > 0f ? hp.Current / hp.Max : 0f;
                Rgba32 hpColor = ratio >= 0.66f ? HpGreen
                               : ratio >= 0.33f ? HpYellow
                               : HpRed;
                draw.DrawTextLong(baseX, baseY, $"HP:{hp.Current:F0}/{hp.Max:F0}", hpColor,
                    fontSizePx: 13f, lineOffsetPx: -44f);
            }
        }
    }
}
