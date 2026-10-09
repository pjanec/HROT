using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> T4 (R-241; 📄 docs/DESIGN_Ai_Action_Status_Gizmo.md §2 T4, docs/DESIGN_Peek_And_Fire.md §8) — PeekAndFire's
    /// "thoughts": the remembered firing positions with their heat (a ring per slot, red when burned, <c>h1.7 ×3</c> = heat and
    /// uses) and, while the node runs, its phase and timer above the unit (<c>PF hidden 2.1s</c>), the hide point and the peek
    /// point. Reads <see cref="FiringPositionMemory"/> — recorded unit memory (R-226), so a replay draws it too. Family
    /// <c>Channels</c> ("Actions"): the selected and pinned units by default, like the action status lines. ⭐ In the presentation
    /// assembly every map host loads (R-228) — ⛔ not beside the node in <c>Hrot.AI.Behaviors</c>, which the deployment pre-load
    /// skips (hot reload), so a projector there would draw only on hosts that happen to load it.
    /// </summary>
    [GizmoProjector(typeof(BehaviorState), typeof(SimTransform), Family = AiOverlayFlags.Channels)]
    public sealed class PeekAndFireGizmo : IStatelessGizmo
    {
        /// <summary>The phase is drawn while the node wrote it this recently (sim s) — it stops with the node.</summary>
        public const double FreshSeconds = 1.0;

        /// <summary>The heat at which a ring turns fully hot (the default burn heat).</summary>
        private const float HotHeat = 2.5f;

        private static readonly Rgba32 Burned = new(220, 40, 40, 230);
        private static readonly Rgba32 Cool   = new(230, 210, 60, 200);
        private static readonly Rgba32 Hide   = new(60, 170, 90, 230);
        private static readonly Rgba32 Peek   = new(60, 160, 230, 230);
        private static readonly Rgba32 Phase  = new(230, 230, 230, 255);

        public unsafe void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            var m = UnitMemory.GetInView<FiringPositionMemory>(view, entity);
            if (m.Count == 0 && m.PhaseAt == 0d) return;   // never ran PeekAndFire
            double now = view is EntityRepository repo && repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : m.PhaseAt;

            for (int i = 0; i < m.Count; i++)
            {
                float heat = PositionHeat.HeatNow(ref m, i, now, HeatRules.Default.CoolHalfLifeSeconds);
                bool burned = m.Burned[i] != 0;
                var at = new Vector3(m.X[i], m.Y[i], m.Z[i]);
                draw.DrawSphere(at, 0.6f, burned ? Burned : Mix(heat / HotHeat), thickness: 2f);
                draw.DrawText(at.X, at.Y, new FixedString32(SlotText(heat, m.Uses[i])), burned ? Burned : Cool, fontSizePx: 10f, lineOffsetPx: 12f);
            }

            var text = PhaseText(in m, now);
            if (text == null) return;
            var unit = view.GetComponentRO<SimTransform>(entity).Position;
            draw.DrawText(unit.X, unit.Y, new FixedString32(text), Phase, fontSizePx: 11f, lineOffsetPx: -40f);
            draw.DrawSphere(m.HidePoint, 0.35f, Hide, thickness: 2f);
            if (Vector3.Distance(m.PeekPoint, m.HidePoint) > 0.25f)
            {
                draw.DrawSphere(m.PeekPoint, 0.35f, Peek, thickness: 2f);
                draw.DrawLine(m.HidePoint, m.PeekPoint, Peek, thickness: 1.5f);
            }
        }

        /// <summary>A slot's label: its heat now and its uses (public for the rail).</summary>
        public static string SlotText(float heat, int uses) => $"h{heat:0.0} x{uses}";

        /// <summary>The phase line, or null when the node has not run within <see cref="FreshSeconds"/> (public for the rail).</summary>
        public static string? PhaseText(in FiringPositionMemory m, double now)
        {
            if (m.PhaseAt == 0d || now - m.PhaseAt > FreshSeconds) return null;
            var phase = (PeekPhase)m.Phase;
            string name = phase switch
            {
                PeekPhase.Choose     => "choosing",
                PeekPhase.MoveToHide => "to cover",
                PeekPhase.Hidden     => "hidden",
                PeekPhase.Expose     => "exposing",
                PeekPhase.Aimed      => "aimed fire",
                PeekPhase.Blind      => "blind burst",
                PeekPhase.Recover    => "back to cover",
                _                    => phase.ToString(),
            };
            double left = m.PhaseUntil - now;
            return phase == PeekPhase.Hidden && left > 0d ? $"PF {name} {left:0.0}s" : $"PF {name}";
        }

        private static Rgba32 Mix(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Rgba32((byte)(Cool.R + (Burned.R - Cool.R) * t), (byte)(Cool.G + (Burned.G - Cool.G) * t),
                              (byte)(Cool.B + (Burned.B - Cool.B) * t), 220);
        }
    }
}
