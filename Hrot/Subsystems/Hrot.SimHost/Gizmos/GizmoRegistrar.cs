using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.SimHost.Gizmos
{
    // Hand-written partial that wraps the source-generated RegisterAll() so
    // SimHostApp.cs has a stable entry point regardless of which gizmos exist.
    public static partial class GizmoRegistrar
    {
        public static void Register(
            GizmoRegistry          gizmoRegistry,
            StatelessGizmoRegistry statelessRegistry,
            GizmoSettingsRegistry  settings)
        {
            RegisterAll(gizmoRegistry, statelessRegistry, settings);
        }
    }
}
