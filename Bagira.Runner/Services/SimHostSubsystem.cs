using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// Stub subsystem for SimHost.
    /// Replace with full SimHost integration in a later batch.
    /// </summary>
    internal sealed class SimHostSubsystem : ISubsystem
    {
        public string Name => "SimHost";

        public void Initialize(SubsystemConfig config) { }
        public void Update(float deltaTime) { }
        public void DrawWorld() { }
        public void DrawUI() { }
        public void Shutdown() { }
    }
}
