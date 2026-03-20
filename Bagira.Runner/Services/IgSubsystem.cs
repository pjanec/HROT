using Bagira.IG;
using FDP.Toolkit.Vis2D.Components;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the Image Generator (IG).
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — creates <see cref="IgApplication"/> and calls
    ///   <see cref="IgApplication.InitializeEmbedded"/>; the orchestrator owns the window.</item>
    ///   <item><see cref="Update"/> — delegates to <see cref="IgApplication.Update"/>.</item>
    ///   <item><see cref="DrawWorld"/> — delegates to <see cref="IgApplication.DrawWorld"/>
    ///   (2-D map canvas + debug overlay, inside <c>Raylib.BeginDrawing</c>).</item>
    ///   <item><see cref="DrawUI"/> — delegates to <see cref="IgApplication.DrawUI"/>
    ///   (ImGui panels, inside <c>rlImGui.Begin</c>).</item>
    ///   <item><see cref="Shutdown"/> — releases IG resources without closing the window
    ///   (the orchestrator manages window lifetime).</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class IgSubsystem : ISubsystem, IMapCameraProvider
    {
        /// <inheritdoc/>
        public string Name => "IG";

        /// <inheritdoc/>
        /// <remarks>Forest green — distinct from SimHost (red) and IOS (violet).</remarks>
        public System.Numerics.Vector4 TitleBarColor =>
            new System.Numerics.Vector4(0.08f, 0.40f, 0.08f, 1f);

        private IgApplication? _app;
        private bool _headless;

        /// <summary>
        /// Internal test hook for integration tests.
        /// </summary>
        internal IgApplication App => _app ?? throw new InvalidOperationException("Not initialized");

        /// <inheritdoc/>
        public MapCamera? GetMapCamera() => _app?.GetMapCamera();

        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;
            _app = new IgApplication();
            // Orchestrator owns the Raylib window; IG sets up ECS + DDS only.
            int? domainOverride = config.DomainId;
            _app.InitializeEmbedded(headless: config.Headless, domainIdOverride: domainOverride, nodeIdOverride: config.NodeId);
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            _app?.Update(deltaTime);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders the 2-D map canvas and debug text overlay.
        /// Called inside <c>Raylib.BeginDrawing()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawWorld()
        {
            if (!_headless)
                _app?.DrawWorld();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders ImGui debug, inspector, mini-IOS, and performance panels.
        /// Called inside <c>rlImGui.Begin()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawUI()
        {
            if (!_headless)
                _app?.DrawUI();
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            // ownsWindow=false: orchestrator manages Raylib window/ImGui teardown.
            _app?.Shutdown(ownsWindow: false);
            _app = null;
        }
    }
}
