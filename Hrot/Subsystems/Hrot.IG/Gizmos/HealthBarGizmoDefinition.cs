using System;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.IG.Components;

namespace Hrot.IG.Gizmos
{
    public sealed class HealthBarGizmoDefinition : IGizmoDefinition
    {
        private readonly GizmoSettingsRegistry _settings;

        public HealthBarGizmoDefinition(GizmoSettingsRegistry settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // Requires IgHealthState component (ComponentId 165).
        public Type[] RequiredComponents => new[] { typeof(IgHealthState) };

        // Always visible — rendering-only gizmo, no selection dependency.
        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IStatefulGizmo CreateInstance() => new HealthBarGizmoInstance(_settings);
    }
}
