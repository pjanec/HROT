using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// Stub subsystem for IOS.
    /// Replace with full IOS integration in a later batch.
    /// </summary>
    internal sealed class IosSubsystem : ISubsystem
    {
        public string Name => "IOS";

        public void Initialize(SubsystemConfig config) { }
        public void Update(float deltaTime) { }
        public void DrawWorld() { }
        public void DrawUI() { }
        public void Shutdown() { }
    }
}
