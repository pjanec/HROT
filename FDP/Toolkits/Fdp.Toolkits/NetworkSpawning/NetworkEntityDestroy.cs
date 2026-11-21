using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.NetworkSpawning
{
    /// <summary>
    /// ⭐ CE-2468 UF-1 (docs/designs/minefields/DESIGN_Minefields.md §9b.1 K2) — THE way a system ends an entity it owns: a networked entity is destroyed through
    /// <see cref="DestroyEntityCommand"/> (the ELM teardown, the DDS dispose to every ghost, the recorder sees it gone), anything else (an offline world) is destroyed
    /// at once. 📐 The same three lines were written out in four places (obstacles, charges, gas clouds, and now the keyed records); each keeps only its own
    /// extra step around this call.
    /// </summary>
    public static class NetworkEntityDestroy
    {
        /// <summary>
        /// Asks for <paramref name="e"/>'s network teardown and returns true, or returns false when it has no network identity (or the world no destroy command):
        /// the caller then destroys it locally. ⭐ A caller that must stop seeing it at once also marks it <see cref="EntityLifecycle.TearDown"/>.
        /// </summary>
        public static bool Request(EntityRepository repo, Entity e, string reason)
        {
            if (!repo.IsComponentTypeRegistered<NetworkIdentity>() || !repo.HasComponent<NetworkIdentity>(e)
                || !repo.Bus.IsRegisteredManaged<DestroyEntityCommand>())
                return false;
            repo.Bus.PublishManaged(new DestroyEntityCommand { NetworkId = repo.GetComponentRO<NetworkIdentity>(e).Value, Reason = reason });
            return true;
        }
    }
}
