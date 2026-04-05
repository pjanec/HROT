using Hrot.Common.Orchestration.Handlers;
using Hrot.ScenarioEditor;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="IPreviewController"/> for the offline editor.
    ///
    /// <para>
    /// Bypasses the distributed two-phase commit orchestrator and calls directly into
    /// the local <see cref="PreviewClusterOpHandler"/> to perform an in-memory ECS
    /// snapshot (enter preview) or rewind (exit preview).
    /// </para>
    ///
    /// <para>
    /// <see cref="IsInPreviewMode"/> is determined by reading the current
    /// <see cref="ScenarioEditorState"/> from the injected <see cref="IScenarioStateProvider"/>.
    /// </para>
    ///
    /// No DDS or CycloneDDS references.
    /// </summary>
    public sealed class EditorPreviewAdapter : IPreviewController
    {
        private readonly PreviewClusterOpHandler _handler;
        private readonly IScenarioStateProvider  _stateProvider;

        /// <param name="handler">
        /// The local preview handler that manages the ECS dry-run snapshot/rewind protocol.
        /// </param>
        /// <param name="stateProvider">
        /// Provides the current high-level state of the editor session so the adapter can
        /// report whether preview mode is currently active.
        /// </param>
        public EditorPreviewAdapter(
            PreviewClusterOpHandler handler,
            IScenarioStateProvider  stateProvider)
        {
            _handler       = handler;
            _stateProvider = stateProvider;
        }

        /// <inheritdoc/>
        public bool IsInPreviewMode
            => _stateProvider.CurrentState is
               ScenarioEditorState.LoadingPreview or
               ScenarioEditorState.OperatingPreview;

        /// <inheritdoc/>
        /// <remarks>
        /// Captures an in-memory ECS snapshot so the simulation can run in dry-run mode
        /// without modifying the authoritative editor state.
        /// </remarks>
        public void EnterPreviewMode()
        {
            _handler.TriggerLoadingPreview();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Rewinds the live ECS repository to the previously captured snapshot,
        /// discarding all changes made during the preview session.
        /// </remarks>
        public void ExitPreviewMode()
        {
            _handler.TriggerUnloadingPreview();
        }
    }
}
