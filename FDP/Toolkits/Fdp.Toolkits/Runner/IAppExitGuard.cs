namespace Fdp.Toolkit.Runner
{
    /// <summary>Disposition returned by an <see cref="IAppExitGuard"/> when the app is asked to exit.</summary>
    public enum ExitDisposition
    {
        /// <summary>Nothing blocks exit — the render loop may terminate now.</summary>
        CanExit,

        /// <summary>
        /// The guard has unsaved work and is now showing a prompt. The render loop must keep running
        /// (so the guard can draw its modal) until <see cref="IAppExitGuard.ExitApproved"/> flips true
        /// or the user cancels.
        /// </summary>
        Deferred,
    }

    /// <summary>
    /// OPTIONAL interface a subsystem MAY implement to veto/defer application exit — e.g. to prompt
    /// the user about unsaved documents before the window closes. The composition root's render loop
    /// queries every registered guard when the OS window-close is requested (window [X]).
    /// <para>
    /// Contract:
    /// <list type="bullet">
    ///   <item><see cref="OnExitRequested"/> is called once on the frame the close is requested. It
    ///   returns <see cref="ExitDisposition.CanExit"/> to allow the loop to terminate, or
    ///   <see cref="ExitDisposition.Deferred"/> to keep it running while the guard renders a prompt
    ///   (in its normal <c>DrawUI</c> pass).</item>
    ///   <item><see cref="ExitApproved"/> is polled every frame after rendering; when any guard
    ///   returns true the loop terminates.</item>
    /// </list>
    /// Implementations must be idempotent: a second <see cref="OnExitRequested"/> while a prompt is
    /// already open should not reset or duplicate it.
    /// </para>
    /// </summary>
    public interface IAppExitGuard
    {
        /// <summary>
        /// Called on the frame the user requests app exit (window [X]). Returns
        /// <see cref="ExitDisposition.CanExit"/> when there is nothing to block exit, or
        /// <see cref="ExitDisposition.Deferred"/> when the guard has opened a prompt.
        /// </summary>
        ExitDisposition OnExitRequested();

        /// <summary>
        /// True once the user has resolved a deferred prompt in favour of exiting. The loop polls
        /// this each frame (after rendering) and terminates when it becomes true.
        /// </summary>
        bool ExitApproved { get; }
    }
}
