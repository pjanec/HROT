using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Combat.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    [GizmoProjector(typeof(Health))]
    public sealed class HealthBarGizmo : IStatelessGizmo
    {
        private readonly GizmoSettingsRegistry _settings;

        public HealthBarGizmo(GizmoSettingsRegistry settings)
        {
            _settings = settings;
            HealthBarGizmoSettings.Register(settings);
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder)
        {
            if (!view.HasComponent<Health>(entity)) return;

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

            // Read settings for bar dimensions (defaults used if not yet written).
            float barWidth  = _settings.Read(GizmoSettingsRegistry.ComputeHash(HealthBarGizmoSettings.BarWidthKey)).FloatValue;
            float barHeight = _settings.Read(GizmoSettingsRegistry.ComputeHash(HealthBarGizmoSettings.BarHeightKey)).FloatValue;

            // Use default sizes when settings return zero (not yet registered).
            if (barWidth  <= 0f) barWidth  = HealthBarGizmoSettings.DefaultBarWidth.FloatValue;
            if (barHeight <= 0f) barHeight = HealthBarGizmoSettings.DefaultBarHeight.FloatValue;

            // Color: green=healthy, yellow=damaged, red=critical.
            Rgba32 color = healthPct >= 0.66f ? Rgba32.Green
                         : healthPct >= 0.33f ? Rgba32.Yellow
                         : Rgba32.Red;

            // Draw entity badge showing health percentage.
            var text = new FixedString32($"{(int)(healthPct * 100)}%");
            drawBuilder.DrawEntityBadge(entity, text);
        }
    }
}
