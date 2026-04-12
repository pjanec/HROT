using System.Numerics;
using FDP.Toolkit.Vis2D.Components;

namespace Hrot.ClusterRunner.Tests.Mocks
{
    /// <summary>
    /// Test double that acts as both <see cref="ISubsystem"/> and
    /// <see cref="IMapCameraProvider"/>.  Lets orchestrator tests verify that camera
    /// state is synchronised when map ownership changes.
    /// </summary>
    public class MapCameraSubsystemMock : ISubsystem, IMapCameraProvider
    {
        public string Name { get; }

        /// <inheritdoc/>
        public Vector4 TitleBarColor => new Vector4(0.2f, 0.5f, 0.8f, 1f);

        /// <summary>The camera returned by <see cref="GetMapCamera"/>.</summary>
        public MapCamera Camera { get; }

        public MapCameraSubsystemMock(string name, MapCamera camera)
        {
            Name   = name;
            Camera = camera;
        }

        public void Initialize(SubsystemConfig config) { }
        public void Update(float deltaTime) { }
        public void DrawWorld() { }
        public void DrawUI() { }
        public void Shutdown() { }

        public MapCamera? GetMapCamera() => Camera;

        // ── IMapCameraProvider ────────────────────────────────────────────────

        public MapCameraView? GetCameraView() => Camera?.GetCameraView();

        public void ApplyCameraView(MapCameraView view) => Camera?.ApplyCameraView(view);
    }
}
