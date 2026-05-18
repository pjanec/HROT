using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// Controls whether a gizmo type is visible globally and per-entity.
    /// Evaluated once per frame at the global level, and once per candidate entity.
    /// </summary>
    public interface IGizmoVisibilityPolicy
    {
        /// <summary>
        /// Returns false to suppress all gizmos of this type for the entire frame,
        /// without visiting individual entities.
        /// </summary>
        bool IsGloballyEnabled(ISimulationView view);

        /// <summary>
        /// Returns false to suppress this gizmo for a specific entity even when
        /// <see cref="IsGloballyEnabled"/> returns true.
        /// </summary>
        bool IsEntityVisible(ISimulationView view, Entity entity);
    }

    /// <summary>
    /// Always-visible policy: both methods unconditionally return <c>true</c>.
    /// Use as the default policy for gizmos that should always render when active.
    /// </summary>
    public sealed class AlwaysVisiblePolicy : IGizmoVisibilityPolicy
    {
        /// <summary>Singleton instance.</summary>
        public static readonly AlwaysVisiblePolicy Instance = new AlwaysVisiblePolicy();

        private AlwaysVisiblePolicy() { }

        public bool IsGloballyEnabled(ISimulationView view) => true;
        public bool IsEntityVisible(ISimulationView view, Entity entity) => true;
    }

    /// <summary>
    /// Never-visible policy: both methods unconditionally return <c>false</c>.
    /// Useful for temporarily disabling a gizmo type without unregistering it.
    /// </summary>
    public sealed class NeverVisiblePolicy : IGizmoVisibilityPolicy
    {
        /// <summary>Singleton instance.</summary>
        public static readonly NeverVisiblePolicy Instance = new NeverVisiblePolicy();

        private NeverVisiblePolicy() { }

        public bool IsGloballyEnabled(ISimulationView view) => false;
        public bool IsEntityVisible(ISimulationView view, Entity entity) => false;
    }
}
