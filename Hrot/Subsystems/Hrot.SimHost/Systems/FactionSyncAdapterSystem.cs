using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Main-thread adapter for FactionSyncSystem.
    /// </summary>
    public sealed class FactionSyncAdapterSystem : ComponentSystem
    {
        private readonly FactionSyncSystem _factionSync = new FactionSyncSystem();

        protected override void OnUpdate()
        {
            var view = (ISimulationView)World;
            _factionSync.Execute(view, DeltaTime);
        }
    }
}
