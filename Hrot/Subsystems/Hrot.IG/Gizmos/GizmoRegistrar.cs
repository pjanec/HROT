using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    // Registers all concrete gizmo definitions with the GizmoRegistry and StatelessGizmoRegistry.
    // Call once after IgApplication.Initialize().
    public static partial class GizmoRegistrar
    {
        public static void Register(
            GizmoRegistry registry,
            StatelessGizmoRegistry statelessRegistry,
            GizmoSettingsRegistry settings)
        {
            // Generated registrars handle all classes decorated with [GizmoProjector].
            Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar.RegisterAll(registry, statelessRegistry, settings);
            Hrot.AI.Behaviors.Gizmos.GizmoRegistrar.RegisterAll(registry, statelessRegistry, settings);

            // GZ058: IG-local effect gizmo + ScenarioEditor gizmos (entity, route, map overlay).
            RegisterAll(registry, statelessRegistry, settings);
            Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll(registry, statelessRegistry, settings);

            // Measure tool gizmo adapter settings (not a migrated gizmo; stays in Hrot.IG).
            MeasureToolGizmoSettings.Register(settings);
        }
    }
}
