using Fdp.Core.Logging;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows
{
    /// <summary>
    /// Global <see cref="ManagedWindow"/> that hosts the <see cref="MessageLogPanel"/>.
    ///
    /// <para>Uses <see cref="WindowScope.Global"/> so the window is visible in every
    /// perspective (IG, SimHost, ExCon, CGF, Editor) without pinning.</para>
    ///
    /// <para>Instantiate once and register with <c>WindowManager.RegisterWindow</c>.
    /// Additional <see cref="IMessageLogSource"/> instances can be pushed to the
    /// underlying <see cref="MessageLogRegistry"/> at any time; new tabs appear on
    /// the next rendered frame.</para>
    /// </summary>
    public sealed class MessageLogWindow : ManagedWindow
    {
        private readonly MessageLogPanel _panel;

        /// <param name="registry">
        /// The shared registry that drives the tab list. Pass the same instance to
        /// <c>WindowManager.MessageLogRegistry</c> so subsystems can register
        /// additional sources via their <c>RegisterWindows</c> override.
        /// </param>
        public MessageLogWindow(MessageLogRegistry registry)
            : base("fdp_message_log", "Message Log", string.Empty, WindowScope.Global)
        {
            _panel = new MessageLogPanel(registry);
            IsOpen = true;
        }

        protected override void DrawClientArea() => _panel.DrawContent();
    }
}
