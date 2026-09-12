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
    /// <para>✅ <b>All three are now present</b> *(`2026-09-12`)*: <see cref="IdAllocator"/>,
    /// <see cref="EntityMap"/>/<see cref="EntityMapFromRepository"/> and <see cref="LifecycleModule"/>.
    /// ⚠⚠ <b>Until then this summary said "THREE" and the class shipped TWO</b> — the ELM was deferred as
    /// <c>HN-018</c> and the gap was invisible from here. 📌 A count in prose is not a count in code.</para>
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

        /// <summary>
        /// ⭐⭐⭐ <b>The THIRD participant §2b enumerated — the ELM's in-flight construction/destruction
        /// queues.</b> 📄 <c>HN-018</c>; the plan is <c>docs/designs/replay-and-modules/DESIGN.md</c> §2.1m.
        ///
        /// <para>⭐⭐ <b>It does NOT restore a snapshot, and that is the point.</b> <c>HN-018</c> was deferred
        /// because <i>"a non-empty queue cannot be restored by a plain copy — the keys are
        /// <see cref="Fdp.Core.Entity"/> handles the repo rewind invalidates, so a correct participant needs
        /// the rewind's identity mapping, not a snapshot."</i> ⇒ 🔒 <b>this one CLEARS and RE-DERIVES from
        /// the restored world</b> *(recorded <c>LifecycleState</c> + <c>TkbIdentity</c>)*, so **no handle
        /// crosses the boundary at all** and that objection never applies.</para>
        ///
        /// <para>⛔⛔ <b><see cref="IPreviewRewindable.Capture"/> MUST return NON-NULL here.</b> 📐
        /// <c>PreviewStateBracket.Capture</c> adds a <c>null</c>-token participant to
        /// <c>UnrestorableParticipants</c> and <c>Restore</c> then SKIPS it ⇒ a <c>null</c> would make this
        /// fix silently never run, and would falsely report the node as unable to guarantee reproducibility
        /// on every preview. ⭐ The token is a marker, not a snapshot — there is nothing to copy.</para>
        ///
        /// <para>⚠ The same participant serves the REPLAY boundaries, not just preview — the contract is
        /// "the world was replaced", and the bracket's name is historical.</para>
        /// </summary>
        public static IPreviewRewindable LifecycleModule(Fdp.Toolkit.Lifecycle.EntityLifecycleModule elm)
            => new LifecycleModuleRewind(elm ?? throw new ArgumentNullException(nameof(elm)));

        // ── the adapters ──────────────────────────────────────────────────────

        private sealed class LifecycleModuleRewind : IPreviewRewindable
        {
            /// <summary>⭐ A marker, not a snapshot — see the factory's remarks on why it may not be null.</summary>
            private static readonly object ClearAndReDeriveMarker = new object();

            private readonly Fdp.Toolkit.Lifecycle.EntityLifecycleModule _elm;
            public LifecycleModuleRewind(Fdp.Toolkit.Lifecycle.EntityLifecycleModule elm) => _elm = elm;

            public string Name => "entity-lifecycle-module";

            public object? Capture() => ClearAndReDeriveMarker;

            public void Restore(object snapshot)
            {
                _elm.ClearForWorldReplacement();
                _elm.ArmResumeFromRestoredWorld();
            }
        }

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
