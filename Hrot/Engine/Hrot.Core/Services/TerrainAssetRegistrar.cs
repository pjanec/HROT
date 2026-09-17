using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Map.Common.Services
{
    /// <summary>
    /// ⭐⭐⭐ <b>D3 — the ONE place every ECS host registers its terrain/zone op handler.</b>
    /// Mirrors <see cref="SerializeLocalRegistrar"/>, for the same reason it exists: hosts that register
    /// the same concern ad-hoc diverge, and the divergence is invisible until the one host that needed it
    /// turns out to be the one that skipped it.
    ///
    /// <para>⛔⛔ <b>UNCONDITIONAL.</b> Every ECS host calls this, with whatever it has — a host that
    /// composed no terrain loader passes <see langword="null"/> and still gets a handler that ACKs
    /// (§8.3). ⚠ That is the whole point: it removes the question "does this host participate?" from the
    /// protocol, where it caused stalls, and answers it at composition, where it is already answered.</para>
    ///
    /// <para>⭐ <b>It also replaces per-host dummy handlers.</b> <c>IgZoneDummyHandler</c> existed only to
    /// ACK <c>PrepareZone</c>/<c>CommitZone</c> so IG would not stall a round; this does that by
    /// construction on every host, with no bespoke class.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §8.3, §3.1.
    /// </summary>
    public static class TerrainAssetRegistrar
    {
        /// <summary>
        /// Registers the terrain/zone op handler on <paramref name="slave"/>.
        /// </summary>
        /// <param name="slave">The node's cluster slave. Required.</param>
        /// <param name="service">
        /// The host's terrain loader, or <see langword="null"/> when it composed none. Null is a host
        /// with nothing to make resident, not a host that opts out of the protocol.
        /// </param>
        /// <param name="world">
        /// The host's repository, or <see langword="null"/> on a no-ECS host. ⚠ A production ECS caller
        /// that HAS a world must pass it — <c>ClusterSlave</c> commits with <c>repo: null</c>, so the
        /// injected world is the only one the handler ever sees.
        /// </param>
        /// <param name="nodeId">This node's id — diagnostics, and the name a failed identity check reports.</param>
        /// <param name="localStagingRoot">
        /// The node's local staging root, where the prefetched scenario header lands. Arms the D5
        /// terrain-identity check (§8.3 N4). ⚠ Null leaves the check DISARMED on this host.
        /// </param>
        public static void Register(
            ClusterSlave slave,
            TerrainLoadService? service,
            EntityRepository? world,
            long nodeId = 0,
            string? localStagingRoot = null)
        {
            if (slave is null) throw new ArgumentNullException(nameof(slave));
            slave.RegisterHandler(new TerrainAssetHandler(service, world, nodeId, localStagingRoot));
        }
    }
}
