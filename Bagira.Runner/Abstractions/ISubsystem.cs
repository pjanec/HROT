using Bagira.Runner.Models;

namespace Bagira.Runner.Abstractions
{
    /// <summary>
    /// Interface for Runner subsystems (SimHost, IG, IOS).
    /// The orchestrator calls lifecycle methods in strict order:
    /// <list type="bullet">
    ///   <item><description><see cref="Initialize"/> — once, before the render loop.</description></item>
    ///   <item><description><see cref="Update"/> — every frame, before rendering.</description></item>
    ///   <item><description><see cref="DrawWorld"/> — every frame, inside <c>Raylib.BeginDrawing()</c> but before ImGui.</description></item>
    ///   <item><description><see cref="DrawUI"/> — every frame, inside <c>rlImGui.Begin()</c>.</description></item>
    ///   <item><description><see cref="Shutdown"/> — once, in reverse registration order.</description></item>
    /// </list>
    /// Subsystems must NOT call <c>Raylib.InitWindow()</c> or <c>rlImGui.Setup()</c>
    /// unless <see cref="SubsystemConfig.OwnWindow"/> is <c>true</c>.
    /// </summary>
    public interface ISubsystem
    {
        /// <summary>Human-readable subsystem name shown in diagnostics.</summary>
        string Name { get; }

        /// <summary>
        /// Performs one-time initialisation.  The orchestrator passes a
        /// <see cref="SubsystemConfig"/> describing whether window ownership
        /// and headless mode are active.
        /// </summary>
        void Initialize(SubsystemConfig config);

        /// <summary>
        /// Advances simulation / game logic by <paramref name="deltaTime"/> seconds.
        /// Must not contain rendering calls.
        /// </summary>
        void Update(float deltaTime);

        /// <summary>
        /// Renders the 3-D / world layer.  Called inside
        /// <c>Raylib.BeginDrawing()</c> before ImGui.
        /// No-op in headless mode.
        /// </summary>
        void DrawWorld();

        /// <summary>
        /// Renders the ImGui UI layer.  Called inside <c>rlImGui.Begin()</c>.
        /// No-op in headless mode.
        /// </summary>
        void DrawUI();

        /// <summary>
        /// Releases all subsystem resources.  Called in reverse registration order.
        /// </summary>
        void Shutdown();
    }
}
