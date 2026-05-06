using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    public sealed class HillAttackGizmoDefinition : IGizmoDefinition
    {
        private readonly GizmoSettingsRegistry _settings;

        public HillAttackGizmoDefinition(GizmoSettingsRegistry settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // Requires BrainBlackboard, BehaviorState, and SimTransform.
        public Type[] RequiredComponents => new[]
        {
            typeof(BrainBlackboard),
            typeof(BehaviorState),
            typeof(SimTransform)
        };

        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IStatefulGizmo CreateInstance() => new HillAttackGizmoInstance(_settings);
    }
}
