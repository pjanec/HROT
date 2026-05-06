using System;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    public sealed class EntityRotationGizmoDefinition : IGizmoDefinition
    {
        private readonly GizmoSettingsRegistry _settings;

        public EntityRotationGizmoDefinition(GizmoSettingsRegistry settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // Requires SimTransform component (ComponentId 0).
        public Type[] RequiredComponents => new[] { typeof(SimTransform) };

        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IStatefulGizmo CreateInstance() => new EntityRotationGizmoInstance(_settings);
    }
}
