using Fdp.Core;
using Fdp.Toolkit.Time.Controllers;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.Editor.UI
{
    /// <summary>
    /// Status-bar section that renders transport-control buttons (play/pause, step, stop)
    /// and a sim-time / time-rate display for the editor preview.
    ///
    /// <para>Rendered format: [Play/Pause] [Step] [Stop] | HH:MM:SS.SSS | 1.5x</para>
    ///
    /// <para>Registered by <see cref="EditorSubsystem"/> via
    /// <see cref="Fdp.Presentation.WindowManager.StatusBarManager.RegisterSection"/>
    /// and bound to the "Editor" perspective so it is hidden when switching away.</para>
    ///
    /// <para>Rendering is delegated to the shared <see cref="ClusterTimeControlStatusBarSection"/>
    /// via an <see cref="EditorTimeTransportFacade"/>.</para>
    ///
    /// <para>This used to build its own <c>EditorTimeTransportAdapter</c> — a byte-for-byte copy of
    /// the facade but for its name, its accessibility and its missing null-guards. Both were live
    /// (the copy here for the status bar, the facade eight lines later in
    /// <c>EditorSubsystem</c> for the main toolbar), so this was two implementations of one
    /// interface serving two surfaces, not a dead class beside a live one. The surfaces both stay;
    /// the second implementation does not.</para>
    /// </summary>
    internal sealed class TimeControlStatusBarSection
    {
        private readonly ClusterTimeControlStatusBarSection _inner;

        internal TimeControlStatusBarSection(
            IPreviewController   preview,
            MasterSyncController timeCtrl,
            EntityRepository     world,
            Fdp.Toolkit.Time.ITimeCommands? commands = null)
        {
            _inner = new ClusterTimeControlStatusBarSection(
                new EditorTimeTransportFacade(preview, timeCtrl, world, commands));
        }

        /// <summary>
        /// Called each frame by the <see cref="Fdp.Presentation.WindowManager.StatusBarManager"/>.
        /// Must be called inside an active ImGui frame and inside the status-bar window.
        /// </summary>
        public void Render() => _inner.Render();
    }
}
