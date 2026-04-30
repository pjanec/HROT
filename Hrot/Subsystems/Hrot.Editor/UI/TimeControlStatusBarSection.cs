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
    /// via an <see cref="EditorTimeTransportAdapter"/> facade.</para>
    /// </summary>
    internal sealed class TimeControlStatusBarSection
    {
        private readonly ClusterTimeControlStatusBarSection _inner;

        internal TimeControlStatusBarSection(
            IPreviewController   preview,
            MasterSyncController timeCtrl,
            EntityRepository     world)
        {
            _inner = new ClusterTimeControlStatusBarSection(
                new EditorTimeTransportAdapter(preview, timeCtrl, world));
        }

        /// <summary>
        /// Called each frame by the <see cref="Fdp.Presentation.WindowManager.StatusBarManager"/>.
        /// Must be called inside an active ImGui frame and inside the status-bar window.
        /// </summary>
        public void Render() => _inner.Render();
    }
}
