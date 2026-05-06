using System;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;

namespace Hrot.IG.Gizmos
{
    public sealed class VisibilityConeGizmoDefinition : IGizmoDefinition
    {
        public static readonly VisibilityConeGizmoDefinition Instance = new();

        // Requires both a spatial transform and a perception receptor.
        public Type[] RequiredComponents => new[] { typeof(SimTransform), typeof(PerceptionReceptor) };

        public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;

        public IStatefulGizmo CreateInstance() => new VisibilityConeGizmoInstance();
    }
}
