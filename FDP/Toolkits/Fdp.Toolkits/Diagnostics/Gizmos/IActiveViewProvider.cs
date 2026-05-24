using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// Provides the active simulation view for gizmo rendering.
    /// When a data breakpoint is paused, the active view is the pre-tick snapshot;
    /// otherwise it is the live repository.
    /// Implemented by DataBreakpointManager so gizmo systems can render against the
    /// correct snapshot without creating a circular assembly dependency.
    /// </summary>
    public interface IActiveViewProvider
    {
        /// <summary>
        /// The simulation view that gizmos should read from this frame.
        /// </summary>
        ISimulationView ActiveView { get; }
    }
}
