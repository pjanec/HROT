using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Orchestration.Preview
{
    /// <summary>
    /// ⭐⭐⭐ <b>The THREE participants §2b enumerated, each wrapped as an <see cref="IPreviewRewindable"/>.</b>
    /// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §2b.
    ///
    /// <para>⛔⛔ <b>All three, or none.</b> 📐 §2b's finding: restoring the ALLOCATOR alone makes things
    /// WORSE — <c>NetworkEntityMap.Register</c> throws <c>"NetworkId {id} already registered"</c> on a
    /// duplicate, and the editor never prunes the map, so exact id repetition turns a silent drift into a
    /// thrown exception on the second preview. ⇒ ⭐ <b>the drift was the only thing hiding the map leak</b>,
    /// and these ship together.</para>
    /// </summary>
    public static class PreviewParticipants
    {
        /// <summary>
        /// ⭐ The id allocator's issuing position — the user's requirement.
        /// ⚠ Reports itself unrestorable when the allocator does not implement
        /// <see cref="IRestorableIdAllocator"/>, rather than pretending.
        /// </summary>
        public static IPreviewRewindable IdAllocator(INetworkIdAllocator allocator)
            => new AllocatorRewind(allocator ?? throw new ArgumentNullException(nameof(allocator)));

        /// <summary>⭐ The network-id → entity map. ⛔ Mandatory alongside the allocator — see the class remarks.</summary>
        public static IPreviewRewindable EntityMap(NetworkEntityMap map)
            => new EntityMapRewind(map ?? throw new ArgumentNullException(nameof(map)));

        /// <summary>
        /// ⭐⭐ <b>The same map, resolved LATE from the repository's managed singleton.</b>
        ///
        /// <para>📐 <b>Measured `2026-08-23` — why this overload has to exist.</b> <c>SimHostApp</c> calls
        /// <c>SetSingletonManaged&lt;NetworkEntityMap&gt;</c> <b>after</b> <c>NodeBootstrapper.BuildOrchestration</c>
        /// has already registered the preview handler ⇒ ⛔ an eager <see cref="EntityMap"/> at the registration
        /// site would throw *("Singleton NetworkEntityMap not set")*. ⭐ Resolving at <c>Capture()</c> time —
        /// which happens on preview ENTER, long after startup — is the ordering-safe form.</para>
        ///
        /// <para>⚠ <b>Reports itself unrestorable when the singleton is absent</b>, rather than inventing an
        /// empty map: a node with no map has nothing to put back, and the bracket must be able to say so.</para>
        ///
        /// <para>⛔⛔ <b>And <c>SyncFrom</c> does NOT rescue it.</b> 📐 <c>EntityRepository.Sync.cs</c> syncs
        /// component tables and <b>only the EQS solver's singleton tables</b> — a managed singleton like the
        /// map is NOT part of a snapshot rewind. ⇒ ⭐ being a repo singleton does not make the map
        /// preview-safe; this participant is what makes it preview-safe.</para>
        /// </summary>
        public static IPreviewRewindable EntityMapFromRepository(EntityRepository repository)
            => new RepositoryEntityMapRewind(repository ?? throw new ArgumentNullException(nameof(repository)));

        // ── the adapters ──────────────────────────────────────────────────────

        private sealed class AllocatorRewind : IPreviewRewindable
        {
            private readonly INetworkIdAllocator _allocator;
            public AllocatorRewind(INetworkIdAllocator a) => _allocator = a;

            public string Name => "id-allocator";

            public object? Capture()
                => _allocator is IRestorableIdAllocator r ? r.CaptureIssuingPosition() : null;

            public void Restore(object snapshot)
            {
                if (_allocator is IRestorableIdAllocator r) r.RestoreIssuingPosition(snapshot);
            }
        }

        private sealed class RepositoryEntityMapRewind : IPreviewRewindable
        {
            private readonly EntityRepository _repo;
            public RepositoryEntityMapRewind(EntityRepository r) => _repo = r;

            public string Name => "network-entity-map";

            public object? Capture()
                => _repo.HasSingletonManaged<NetworkEntityMap>()
                   ? _repo.GetSingletonManaged<NetworkEntityMap>()?.CaptureState()
                   : null;

            public void Restore(object snapshot)
            {
                if (snapshot is NetworkEntityMap.State s && _repo.HasSingletonManaged<NetworkEntityMap>())
                    _repo.GetSingletonManaged<NetworkEntityMap>()?.RestoreState(s);
            }
        }

        private sealed class EntityMapRewind : IPreviewRewindable
        {
            private readonly NetworkEntityMap _map;
            public EntityMapRewind(NetworkEntityMap m) => _map = m;

            public string Name => "network-entity-map";

            public object? Capture() => _map.CaptureState();

            public void Restore(object snapshot)
            {
                if (snapshot is NetworkEntityMap.State s) _map.RestoreState(s);
            }
        }
    }
}
