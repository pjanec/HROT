namespace Fdp.Toolkit.Runner
{
    /// <summary>
    /// Generic runtime options for the <see cref="SubsystemOrchestrator"/>.
    /// These are the framework-level flags that any host project needs to supply.
    /// Project-specific flags (e.g. <c>--mode</c>, <c>--role</c>) are added by
    /// extending this class in the composition root.
    /// </summary>
    public class RunnerOptions
    {
        /// <summary>When <c>true</c>, the orchestrator skips all Raylib and ImGui calls.</summary>
        public bool Headless { get; set; }

        /// <summary>DDS domain ID forwarded to each subsystem during <see cref="ISubsystem.Initialize"/>.</summary>
        public int DomainId { get; set; }

        /// <summary>Initial window width; ignored in headless mode.</summary>
        public int WindowWidth { get; set; } = 2200;

        /// <summary>Initial window height; ignored in headless mode.</summary>
        public int WindowHeight { get; set; } = 1200;

        /// <summary>Target frame rate cap; ignored in headless mode.</summary>
        public int TargetFps { get; set; } = 60;

        /// <summary>Base node ID forwarded to the orchestrator for per-subsystem offset resolution (0 = use legacy constants).</summary>
        public int NodeId { get; set; }

        /// <summary>When <c>true</c>, the orchestrator passes <see cref="FixedDeltaSeconds"/> to
        /// <c>Update()</c> instead of <c>Raylib.GetFrameTime()</c>. Use for CI / deterministic tests.</summary>
        public bool Deterministic { get; set; }

        /// <summary>Fixed simulation delta in seconds used when <see cref="Deterministic"/> is
        /// <c>true</c>. Default = 1/60 s (60 Hz).</summary>
        public float FixedDeltaSeconds { get; set; } = 1.0f / 60.0f;

        /// <summary>
        /// Optional delegate used by <see cref="SubsystemOrchestrator"/> to resolve the
        /// per-subsystem node ID from the base <see cref="NodeId"/>.
        /// <para>
        /// The delegate receives <c>(subsystemName, baseNodeId)</c> and must return the
        /// concrete node ID to assign to that subsystem.  When <c>null</c>, every
        /// subsystem receives the raw <see cref="NodeId"/> value unchanged.
        /// </para>
        /// </summary>
        public Func<string, int, int>? NodeIdResolver { get; set; }
    }
}
