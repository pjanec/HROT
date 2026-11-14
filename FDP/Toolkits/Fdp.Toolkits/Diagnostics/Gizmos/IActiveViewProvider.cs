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

    /// <summary>
    /// ⭐ R-4211 (§4 hazard) — the view a THREADED node's UI-side gizmo managers draw: its presentation world. The data-breakpoint manager is the
    /// SIM side's (its live view is the simulation world; a hit pauses the sim, whose held state the staging publishes to the UI like any tick),
    /// so the UI's gizmos must never be handed it.
    /// </summary>
    public sealed class PresentationViewProvider : IActiveViewProvider
    {
        private readonly System.Func<ISimulationView> _view;
        public PresentationViewProvider(System.Func<ISimulationView> view) => _view = view ?? throw new System.ArgumentNullException(nameof(view));
        public ISimulationView ActiveView => _view();
    }
}
