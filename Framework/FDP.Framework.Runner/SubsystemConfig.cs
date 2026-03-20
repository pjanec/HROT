namespace FDP.Framework.Runner
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

        /// <summary>Human-readable name of the subsystem (e.g. "SimHost", "IG", "IOS").</summary>
        public string SubsystemName { get; set; } = string.Empty;

        /// <summary>Resolved node ID for this subsystem instance (0 = use legacy constants inside the subsystem).</summary>
        public int NodeId { get; set; }

        /// <summary>When <c>true</c>, <see cref="ScenarioSubsystem"/> uses
        /// <c>SteppingTimeController</c> instead of wall-clock time.</summary>
        public bool Deterministic { get; set; }

        /// <summary>Fixed step in seconds. Used only when <see cref="Deterministic"/> is
        /// <c>true</c>. Default = 1/60 s.</summary>
        public float FixedDeltaSeconds { get; set; } = 1.0f / 60.0f;
    }
}
