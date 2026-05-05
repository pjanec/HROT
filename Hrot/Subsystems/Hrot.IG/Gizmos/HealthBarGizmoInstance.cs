using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.IG.Components;

namespace Hrot.IG.Gizmos
{
    internal sealed class HealthBarGizmoInstance : IStatefulGizmo
    {
        private readonly GizmoSettingsRegistry _settings;

        public HealthBarGizmoInstance(GizmoSettingsRegistry settings)
        {
            _settings = settings;
        }

        public void OnInitialize(ISimulationView view, Entity entity) { }

        public void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder drawBuilder)
        {
            if (!view.HasComponent<IgHealthState>(entity)) return;

            ref readonly var health = ref view.GetComponentRO<IgHealthState>(entity);
            float damage     = health.Damage;
            float healthPct  = 1f - (damage / 100f);

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

        public void OnTeardown() { }
    }
}
