using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Network.Cyclone.Translators
{
    /// <summary>
    /// ⭐⭐ <c>CE-4007</c> / <c>CE-4000</c> residual (docs/DESIGN_Cluster_Load_Phase.md §8.3) — the ONE record of what an egress writer
    /// PUBLISHED, keyed by network id, and the one place that deletes it again: on the entity's destruction (<see cref="Dispose"/>, called
    /// through the translator by <c>CycloneNetworkCleanupSystem</c>), when the world is wiped (<see cref="OnFrame"/>: <c>WorldEpoch</c> moved),
    /// or never — a hand-over only lets go (<see cref="Release"/>).
    /// <para>📐 Before it, ~62 egress translators each carried their own "epoch moved ⇒ dispose what I wrote" block and a <c>Dispose(id)</c>
    /// that only dropped the cache — so a destroyed entity's last sample stayed on every TransientLocal topic for every late joiner, and the
    /// next wipe could not delete it either (its cache entry was already gone). Measured live 2026-11-05 (the design's table).</para>
    /// <para>⭐ It is also the translator's send-on-change cache (<see cref="Unchanged"/>): the dictionary that remembers the last sample IS the
    /// list of instances to delete. No allocation on the steady frame (R-2084): one dictionary, reused.</para>
    /// <para>⭐ The key is a <c>long</c>: the network id for an entity-keyed topic; a composite (e.g. parent id and child index, packed by the
    /// translator) with an <c>entityOf</c> mapping for a topic that holds several instances per entity — <see cref="Dispose"/> then deletes
    /// every instance of that entity.</para>
    /// </summary>
    /// <typeparam name="T">The DDS sample type.</typeparam>
    public static class PublishedInstances
    {
        /// <summary>
        /// ⭐ An entity in TearDown: an event-driven writer (one that writes from an event, not from an <c>AllButTearDown</c> query) must not
        /// write about it — its delete may already be out, and a write would hand it alive to every late joiner again.
        /// </summary>
        public static bool IsTearingDown(ISimulationView view, Fdp.Core.Entity entity)
            => view is Fdp.Core.EntityRepository r && r.IsAlive(entity) && r.GetLifecycleState(entity) == Fdp.Core.EntityLifecycle.TearDown;
    }

    /// <inheritdoc cref="PublishedInstances"/>
    public sealed class PublishedInstances<T> where T : struct
    {
        private readonly Action<T>? _write, _dispose, _unregister;   // null ⇒ a writer-less test seam: nothing goes on the wire
        private readonly Func<long, T> _keyOf;
        private readonly Func<long, int>? _entityOf;
        private readonly Dictionary<long, T> _last = new();
        private readonly List<long> _scratch = new();
        private int _worldEpoch;

        /// <param name="writer">The translator's writer; null in a writer-less test seam (nothing goes on the wire, the cache still works).</param>
        /// <param name="keyOf">A sample carrying only the key <c>key</c> — what a delete names.</param>
        /// <param name="entityOf">For a topic with several instances per entity: the network id a key belongs to. Null ⇒ the key IS the
        /// network id.</param>
        public PublishedInstances(DdsWriter<T>? writer, Func<long, T> keyOf, Func<long, int>? entityOf = null)
            : this(writer is null ? null : s => writer.Write(s),
                   writer is null ? null : s => writer.DisposeInstance(s),
                   writer is null ? null : s => writer.UnregisterInstance(s), keyOf, entityOf) { }

        /// <summary>
        /// For a translator that writes through its own writer seam (an <c>IDdsWriter&lt;T&gt;</c> adapter, a test double): the three wire
        /// operations as delegates — built ONCE, in the translator's constructor (no allocation per frame). Null delegates ⇒ nothing goes on
        /// the wire.
        /// </summary>
        public PublishedInstances(Action<T>? write, Action<T>? dispose, Action<T>? unregister, Func<long, T> keyOf, Func<long, int>? entityOf = null)
        {
            _write = write;
            _dispose = dispose;
            _unregister = unregister;
            _keyOf = keyOf ?? throw new ArgumentNullException(nameof(keyOf));
            _entityOf = entityOf;
        }

        /// <summary>How many instances this writer holds published.</summary>
        public int Count => _last.Count;

        /// <summary>The keys this writer holds published (diagnostics, rails).</summary>
        public IReadOnlyCollection<long> Keys => _last.Keys;

        /// <summary>
        /// Call first in every <c>ScanAndPublish</c>. When the world was wiped (its <c>WorldEpoch</c> moved) every instance this writer
        /// published is deleted on the wire and forgotten — the next world reuses the ids; returns true on that frame. Otherwise every entity
        /// whose <c>DestructionOrder</c> this world raised has what this writer published about it deleted — owner or not.
        /// </summary>
        public bool OnFrame(ISimulationView view)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) { DisposeAll(); return true; }
            // ⭐⭐ an entity this writer wrote about is being destroyed in THIS world — delete what it wrote, owner or not. 📐 Measured live:
            //   a node that writes about an entity it does not own (its lifecycle status of a replica, an EQS result for another node's unit)
            //   is never handed Dispose by the cleanup system (it disposes OWNED entities), so its last sample stayed on the wire for every
            //   late joiner. The cleanup system's own Dispose call stays: a repeat is a no-op. No allocation: an event span.
            if (_last.Count == 0 || (view is Fdp.Core.EntityRepository r && !r.Bus.IsRegistered<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>()))
                return false;
            foreach (var order in view.ReadEvents<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>())
                if (view.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(order.Entity))
                    Dispose(view.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(order.Entity).Value);
            return false;
        }

        /// <summary>True when <paramref name="sample"/> is byte-for-byte the one last written for <paramref name="key"/> (nothing to send).</summary>
        public bool Unchanged(long key, in T sample) => _last.TryGetValue(key, out var prev) && SampleEquality.Same(prev, sample);

        /// <summary>The sample last written for <paramref name="key"/>.</summary>
        public bool TryGetLast(long key, out T sample) => _last.TryGetValue(key, out sample);

        /// <summary>Writes <paramref name="sample"/> for <paramref name="key"/> and remembers it.</summary>
        public void Write(long key, in T sample)
        {
            _last[key] = sample;
            _write?.Invoke(sample);
        }

        /// <summary>Remembers <paramref name="sample"/> as published without writing it (a translator that writes through its own path).</summary>
        public void Remember(long key, in T sample) => _last[key] = sample;

        /// <summary>
        /// The entity <paramref name="networkId"/> is gone (destroyed, swept by an in-place restore, or wiped): delete every instance of it
        /// on the wire — those THIS writer published — and forget them. A writer that never wrote it writes nothing.
        /// </summary>
        public void Dispose(int networkId)
        {
            if (_entityOf == null) { DisposeKey(networkId); return; }
            _scratch.Clear();
            foreach (var key in _last.Keys) if (_entityOf(key) == networkId) _scratch.Add(key);
            for (int i = 0; i < _scratch.Count; i++) DisposeKey(_scratch[i]);
        }

        /// <summary>Deletes the one instance <paramref name="key"/> (when this writer published it) and forgets it.</summary>
        public void DisposeKey(long key)
        {
            if (!_last.Remove(key)) return;
            var sample = _keyOf(key);
            _dispose?.Invoke(sample);
        }

        /// <summary>
        /// A hand-over (F7, CE-4079 ②): let go of the entity's instances WITHOUT deleting them — the grantee writes them now — and forget
        /// them, so a reclaim republishes.
        /// </summary>
        public void Release(int networkId)
        {
            if (_entityOf == null) { ReleaseKey(networkId); return; }
            _scratch.Clear();
            foreach (var key in _last.Keys) if (_entityOf(key) == networkId) _scratch.Add(key);
            for (int i = 0; i < _scratch.Count; i++) ReleaseKey(_scratch[i]);
        }

        private void ReleaseKey(long key)
        {
            if (!_last.Remove(key)) return;
            var sample = _keyOf(key);
            _unregister?.Invoke(sample);
        }

        /// <summary>Deletes every instance this writer published and forgets them.</summary>
        public void DisposeAll()
        {
            if (_last.Count == 0) return;
            if (_dispose != null)
                foreach (var key in _last.Keys) _dispose(_keyOf(key));
            _last.Clear();
        }

        /// <summary>Forgets <paramref name="key"/> without writing anything (the sample was never really sent, or a translator resends on purpose).</summary>
        public void Forget(long key) => _last.Remove(key);
    }
}
