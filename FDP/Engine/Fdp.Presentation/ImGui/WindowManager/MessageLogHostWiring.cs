#nullable enable
using Fdp.Core.Logging;
using Fdp.Presentation.Windows;

namespace Fdp.Presentation.WindowManager
{
    /// <summary>
    /// Shared host wiring for the editor Message Log (BATCH-S2-HOSTWIRE). Both the ClusterRunner host
    /// (LocalWindowController) and the Stride host (StrideInspectorWindow) need an identical
    /// MessageLogRegistry + MessageLogWindow created on the WindowManager BEFORE subsystem RegisterWindows
    /// (so editor RegisterSource calls land), plus a status-bar notifier. Extracted here to avoid
    /// duplicating it per host.
    /// </summary>
    public static class MessageLogHostWiring
    {
        /// <summary>
        /// Creates the MessageLogRegistry (seeded with the shared NLog target), the MessageLogWindow,
        /// registers the window, and sets <c>wm.MessageLogRegistry</c>. Call BEFORE subsystem
        /// RegisterWindows. Returns the window so the caller can add the status-bar notifier.
        /// </summary>
        public static MessageLogWindow CreateAndRegister(WindowManager wm)
        {
            var registry = new MessageLogRegistry();
            registry.RegisterSource(NLogMessageLogTarget.SharedInstance);
            var window = new MessageLogWindow(registry);
            wm.RegisterWindow(window);
            wm.MessageLogRegistry = registry;
            return window;
        }

        /// <summary>Registers the status-bar message-log notifier section (click to open the log).</summary>
        public static void AddStatusBarNotifier(WindowManager wm, MessageLogWindow window)
        {
            var section = new MessageLogStatusBarSection(window, wm);
            wm.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, section.Render);
        }
    }
}
