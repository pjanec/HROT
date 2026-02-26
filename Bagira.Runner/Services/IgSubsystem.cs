using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// Stub subsystem for the Image Generator (IG).
    /// Replace with full IG integration in a later batch.
    /// </summary>
    internal sealed class IgSubsystem : ISubsystem
    {
        public string Name => "IG";

        public void Initialize(SubsystemConfig config) { }
        public void Update(float deltaTime) { }
        public void DrawWorld() { }
        public void DrawUI() { }
        public void Shutdown() { }
    }
}
