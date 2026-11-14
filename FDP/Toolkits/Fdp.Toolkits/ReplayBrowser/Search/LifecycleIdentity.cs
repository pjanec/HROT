using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ RB-5 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) — <b>ONE answer to "is THIS entity the one with network id N?"</b>, for a
    /// lifecycle condition identified by <see cref="EntityIdentifierType.NetworkId"/>: the replay search and the live breakpoint ask it
    /// the same way. Asked of the entity's own component — O(1), it runs per entity per frame (BP-512's reasoning).
    /// </summary>
    /// <remarks>
    /// 🔴 The replay search had a second copy that compared the target with the entity's INDEX ("simplified … for test compatibility"),
    /// so <c>POST /replay/search {lifecycle: {IdentifierType: "NetworkId", TargetValue: "1042"}}</c> matched the wrong unit or none.
    /// </remarks>
    public static class LifecycleIdentity
    {
        public static bool IsNetworkId(EntityRepository repo, Entity entity, string targetValue)
            => long.TryParse(targetValue, out long wanted)
               && wanted != 0
               && repo.IsComponentTypeRegistered<NetworkIdentity>()
               && repo.HasComponent<NetworkIdentity>(entity)
               && repo.GetComponentRO<NetworkIdentity>(entity).Value == wanted;
    }
}
