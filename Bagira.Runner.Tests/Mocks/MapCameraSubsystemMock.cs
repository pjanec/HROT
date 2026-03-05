using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;
using FDP.Toolkit.Vis2D.Components;

namespace Bagira.Runner.Tests.Mocks
{
    /// <summary>
    /// Test double that acts as both <see cref="ISubsystem"/> and
    /// <see cref="IMapCameraProvider"/>.  Lets orchestrator tests verify that camera
    /// state is synchronised when map ownership changes.
    /// </summary>
    public class MapCameraSubsystemMock : ISubsystem, IMapCameraProvider
    {
        public string Name { get; }

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
    }
}
