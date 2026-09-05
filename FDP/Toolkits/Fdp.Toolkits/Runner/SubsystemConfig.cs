namespace Fdp.Toolkit.Runner
{
    /// <summary>
    /// Configuration passed to each subsystem during <c>Initialize()</c>.
    /// Tells the subsystem whether it should create its own window/ImGui context
    /// or share the one owned by the orchestrator.
    /// </summary>
    public class SubsystemConfig
    {
        /// <summary>DDS domain ID the subsystem should use.</summary>
        public int DomainId { get; set; }

        /// <summary>When <c>true</c> the subsystem must skip all Raylib and ImGui calls.</summary>
        public bool Headless { get; set; }

        /// <summary>
        /// When <c>true</c> the subsystem is responsible for creating its own Raylib window.
        /// When <c>false</c> the orchestrator owns the window and the subsystem must NOT call
        /// <c>Raylib.InitWindow()</c> or <c>rlImGui.Setup()</c>.
        /// </summary>
        public bool OwnWindow { get; set; }

        /// <summary>Human-readable name of the subsystem (e.g. "SimHost", "IG", "ExCon").</summary>
        public string SubsystemName { get; set; } = string.Empty;

        /// <summary>Resolved node ID for this subsystem instance (0 = use legacy constants inside the subsystem).</summary>
        public int NodeId { get; set; }

        /// <summary>When <c>true</c>, <see cref="ScenarioSubsystem"/> uses
        /// <c>SteppingTimeController</c> instead of wall-clock time.</summary>
        public bool Deterministic { get; set; }

        /// <summary>Fixed step in seconds. Used only when <see cref="Deterministic"/> is
        /// <c>true</c>. Default = 1/60 s.</summary>
        public float FixedDeltaSeconds { get; set; } = 1.0f / 60.0f;

        /// <summary>
        /// Returns <c>true</c> when this subsystem is currently the active map owner.
        /// Injected by <see cref="SubsystemOrchestrator"/> during <c>Initialize()</c>.
        /// Defaults to <c>() => true</c> so standalone subsystems (non-ClusterRunner) are unaffected.
        /// </summary>
        public Func<bool> IsActiveMapOwner { get; set; } = () => true;

        /// <summary>
        /// Asks the host to leave its frame loop gracefully, as though the window's [X] had been
        /// approved: the loop finishes the current frame, then falls into its <c>finally</c> and
        /// runs <c>Shutdown()</c> on every subsystem.
        /// </summary>
        /// <remarks>
        /// Injected by <see cref="SubsystemOrchestrator"/> during <c>Initialize()</c>, where it is
        /// bound to <see cref="SubsystemOrchestrator.Stop"/>. Both host loops honour it — the
        /// orchestrator's own <c>Run()</c> and the Composition Root's render loop, which polls
        /// <see cref="SubsystemOrchestrator.IsRunning"/> each frame.
        /// <para>Safe to call from any thread (the flag it sets is volatile), which is what lets a
        /// subsystem's control plane — e.g. the editor's <c>POST /shutdown</c> — stop the process
        /// without doing its own <c>Environment.Exit</c> and losing the ordered teardown.</para>
        /// Defaults to a no-op so a standalone subsystem outside the orchestrator is unaffected.
        /// </remarks>
        public Action RequestAppExit { get; set; } = () => { };
    }
}
