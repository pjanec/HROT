using Fdp.Toolkit.Time;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.Debug
{
    /// <summary>
    /// The editor's <see cref="AiTracerCoordinator"/> — `T4d`, closing control path D.
    ///
    /// <para>The base class's <c>RequestPause</c>, <c>RequestContinue</c> and
    /// <c>RequestStepOneTick</c> are <c>virtual</c> and EMPTY, and production constructed the base
    /// class. So when a BTree or HSM tracer asked the simulation to stop, nothing happened — no
    /// error, no log, no pause. The capability was built, documented and reachable, and simply was
    /// never turned on.</para>
    ///
    /// <para>Subclassing is the prescribed mechanism, not a workaround:
    /// <c>docs/projects/Hrot/Editor/Hrot.Editor.AiShared.md</c> §3 states "Subsystem coordinators
    /// must override AiTracerCoordinator… Pass the subsystem-specific coordinator to
    /// AiDebugSessionBase". It also keeps this change out of <c>Hrot.Editor.AiShared</c>, which is
    /// frozen to the variable-model session.</para>
    ///
    /// <para>The overrides publish INTENTS rather than calling the controller directly, so path D
    /// becomes path A — the same shape the cluster path uses. That is what lets the debugger pause
    /// go cluster-wide later without changing this class: the intent already fans out.</para>
    /// </summary>
    public sealed class EditorAiTracerCoordinator : AiTracerCoordinator
    {
        private readonly ITimeCommands _time;

        public EditorAiTracerCoordinator(ITimeCommands time)
        {
            _time = time ?? throw new System.ArgumentNullException(nameof(time));
        }

        /// <inheritdoc />
        public override void RequestPause() => _time.Pause();

        /// <inheritdoc />
        public override void RequestContinue() => _time.Resume();

        /// <inheritdoc />
        public override void RequestStepOneTick() => _time.StepOneTick();
    }
}
