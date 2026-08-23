using Fdp.Core.Logging;
using Fdp.Diagnostics.Contracts.Panels;
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
    ///
    /// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> 📄 the queue's gotcha table:
    /// <c>MessageLogPanel</c> is a plain <c>*Panel</c> with no window identity of its own; this window
    /// supplies the address (its own <see cref="ManagedWindow.Id"/>) and the kind.</para>
    /// </summary>
    public sealed class MessageLogWindow : ManagedWindow
    {
        /// <summary>⭐ <c>U-obs-5</c> — THE KIND. ⛔ Single-host, single instance: stays a local literal.</summary>
        internal const string Kind = "message-log";

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

            // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
            PanelSnapshot.DeclareInstrumented(Id);
        }

        /// <summary>
        /// Returns <c>true</c> if any underlying message log tab has unseen
        /// Warning/Error/Critical messages that are not suppressed by the current filters.
        /// </summary>
        public bool HasUnobservedAttention => _panel.HasUnobservedAttention;

        /// <summary>
        /// Instructs the panel to switch to the first tab reporting unobserved
        /// attention on the next rendered frame.
        /// </summary>
        public void FocusFirstAttentionTab() => _panel.FocusFirstAttentionTab();

        /// <summary>
        /// Instructs the panel to switch to a specific tab by its SourceId on the
        /// next rendered frame.
        /// </summary>
        public void SelectTab(string sourceId) => _panel.SelectTab(sourceId);

        /// <summary>
        /// ⭐⭐⭐ <b>U-obs-5: BUILD · CAPTURE.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>
        /// §Example. ⛔⛔ No ImGui here — <see cref="MessageLogPanel.BuildViewModel"/> is pure, so this is
        /// published <b>before</b> <see cref="MessageLogPanel.DrawContent"/> ever touches ImGui, mirroring
        /// the pilot's capture-before-the-guard deviation.
        /// </summary>
        private MessageLogPanelViewModel BuildAndPublish()
        {
            var vm = _panel.BuildViewModel(Id, Kind);
            if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
            return vm;
        }

        /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
        internal MessageLogPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

        protected override void DrawClientArea()
        {
            BuildAndPublish();
            _panel.DrawContent();
        }
    }
}
