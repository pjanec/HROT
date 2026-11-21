namespace Fdp.Toolkit.Runner
{
    /// <summary>
    /// ⭐ <c>CE-1290</c> — OPTIONAL interface a subsystem MAY implement to put a detail in the application window's title, as an editor
    /// names the document it has open (<c>HROT Editor — bt-doors</c>). 🔒 User, 2026-10-23: <i>"i need to see currently loaded scenario
    /// name in the title of the clusterrunner as editors usually do"</i>.
    /// <para>The composition root's render loop reads every source once a frame (the sibling of <see cref="IAppExitGuard"/>: a subsystem
    /// offers, the runner composes) and sets the window title only when the composed text changed.</para>
    /// </summary>
    public interface IWindowTitleSource
    {
        /// <summary>
        /// The detail to show after the application name — the loaded scenario's name — or <c>null</c>/empty when there is nothing to
        /// name (nothing loaded, or a New/cleared scenario). Read every frame on the main thread: keep it a field read.
        /// </summary>
        string? WindowTitleDetail { get; }
    }
}
