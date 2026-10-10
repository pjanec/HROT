using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// The health bar. ⭐ CE-1033 S5b (docs/DESIGN_Map_3D_Mode.md §3.6, M14) — re-targeted from a text badge to ROW 10 of the
    /// entity's CARD: a dark bar the card's width and a coloured one <c>Current / Max</c> of it, on the Labels layer — the same in
    /// 2-D and 3-D, in-process and on a remote IG.
    /// </summary>
    [GizmoProjector(typeof(Health), typeof(NetworkIdentity))]
    public sealed class HealthBarGizmo : IStatelessGizmo
    {
        /// <summary>The card row the bar sits in (§3.6: above the name, row 20).</summary>
        public const byte Row = 10;
        private static readonly Rgba32 Back = new(40, 40, 40, 220);

        private readonly GizmoSettingsRegistry _settings;

        public HealthBarGizmo(GizmoSettingsRegistry settings)
        {
            _settings = settings;
            HealthBarGizmoSettings.Register(settings);
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder)
        {
            if (!view.HasComponent<Health>(entity) || !view.HasComponent<NetworkIdentity>(entity)) return;
            long net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0) return;   // a card is keyed by the network id — none yet, no card

            // ⭐ Derived HERE, from the authority's own Current/Max, rather than read from a
            //   precomputed percentage. The pair travels on the EntityDamage descriptor and the
            //   fraction is a rendering concern — one representation of health, derived at each
            //   consumer. 🔒 User ruling, 2026-09-05: "no precalculated percentages".
            // ⚠ Max <= 0 would make the fraction meaningless (or divide by zero); treat such an
            //   entity as undamaged rather than drawing a bar computed from nonsense.
            ref readonly var health = ref view.GetComponentRO<Health>(entity);
            if (health.Max <= 0f) return;

            float healthPct = health.Current / health.Max;
            if (healthPct < 0f) healthPct = 0f;
            if (healthPct > 1f) healthPct = 1f;

            // The bar's height from the settings (the width is the card's — a fraction of it, §3.6).
            float barHeight = _settings.Read(GizmoSettingsRegistry.ComputeHash(HealthBarGizmoSettings.BarHeightKey)).FloatValue;
            if (barHeight <= 0f) barHeight = HealthBarGizmoSettings.DefaultBarHeight.FloatValue;
            if (barHeight > 12f) barHeight = 12f;

            // Color: green=healthy, yellow=damaged, red=critical.
            Rgba32 color = healthPct >= 0.66f ? Rgba32.Green
                         : healthPct >= 0.33f ? Rgba32.Yellow
                         : Rgba32.Red;

            drawBuilder.Card(net, DebugTraceLayers.Labels).Row(Row).Bar(healthPct, color, Back, barHeight);
        }
    }
}
