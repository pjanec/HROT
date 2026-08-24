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
            // ST-031: ONE reflection call replaces the hand-rolled family list. This host used to name
            // five generated registrars (Common, AI.Behaviors, its own, ScenarioEditor, Presentation) and
            // therefore declared a curated SUBSET -- it was missing SimHost's and CGF's families, which is
            // what the uniform-membership ruling forbids: "support all and decide on current presence of
            // component".
            //
            // Reflection is what makes that possible at all. A compile-time pack would have to reference
            // Hrot.SimHost and Hrot.CGF, and both already reference Hrot.Common, so there is no assembly
            // that can hold it (ST-028). Discovering the projectors instead has no compile-time edge.
            GizmoReflectionRegistrar.RegisterAll(registry, statelessRegistry, settings);

            // Measure tool gizmo adapter settings (not a migrated gizmo; stays in Hrot.IG).
            MeasureToolGizmoSettings.Register(settings);
        }
    }
}
