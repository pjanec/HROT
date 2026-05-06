using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    // Registers all concrete gizmo definitions with the GizmoRegistry.
    // Call once after IgApplication.Initialize().
    public static class GizmoRegistrar
    {
        public static void Register(GizmoRegistry registry, GizmoSettingsRegistry settings)
        {
            // Register HealthBar settings defaults.
            settings.RegisterSetting(HealthBarGizmoSettings.BarHeightKey, HealthBarGizmoSettings.DefaultBarHeight);
            settings.RegisterSetting(HealthBarGizmoSettings.BarWidthKey,  HealthBarGizmoSettings.DefaultBarWidth);

            // Register the gizmo definition.
            registry.Register(new HealthBarGizmoDefinition(settings));

            // Entity rotation gizmo.
            EntityRotationGizmoSettings.Register(settings);
            registry.Register(new EntityRotationGizmoDefinition(settings));

            // Visibility cone gizmo (no extra settings).
            registry.Register(new VisibilityConeGizmoDefinition());

            // Hill attack platoon gizmo.
            HillAttackGizmoSettings.Register(settings);
            registry.Register(new HillAttackGizmoDefinition(settings));
        }
    }
}
