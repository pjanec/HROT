using System;
using System.Threading;

namespace CarKinem.Road
{
    /// <summary>
    /// Thread-safe carrier for the CURRENT road graph, for consumers that cannot reach the
    /// <see cref="ZoneEnvironmentData"/> ECS singleton — and the owner of the retired graphs' memory.
    ///
    /// <para><b>⭐ Why this exists — it is not a second source of truth.</b>
    /// <see cref="ZoneEnvironmentData"/> remains THE source; this is a thread-safe projection published by
    /// the same writer at the same moment. The projection is needed because <c>ISimulationView</c> — the
    /// only thing a module's <c>Tick</c> receives — exposes <b>no singleton API at all</b>.
    /// <c>CarKinematicsSystem</c> reaches singletons by downcasting the view to <c>EntityRepository</c> and
    /// <i>throwing</i> when it is not one, which is legal only because its module is
    /// <c>ExecutionPolicy.Synchronous()</c>. <c>NavigationSolverModule</c> is
    /// <c>SlowBackground(10)</c> ⇒ <c>DataStrategy.SoD</c>, so on that path the downcast fails by design.</para>
    ///
    /// <para><b>⛔⛔ THE LIFETIME HAZARD THIS CLASS EXISTS TO CLOSE, and it is not hypothetical.</b>
    /// 📐 Measured: the existing production swap (<c>ZoneManagerService.LoadZones</c>) calls
    /// <c>existingRoad.Dispose()</c> <b>synchronously, immediately before</b> publishing the replacement.
    /// <see cref="RoadNetworkBlob"/> is a struct of <c>NativeArray</c>s, so that frees the very memory a
    /// 10 Hz background solver may be mid-traversal inside — a cross-thread use-after-free with no
    /// exception and no log. It does not bite today only because nothing constructs
    /// <c>NavigationSolverModule</c> in production yet; wiring it is what makes this live.</para>
    ///
    /// <para>⚠ <b>An atomic reference swap does NOT solve this.</b> Publishing a new reference makes the
    /// POINTER change safely; it says nothing about when the OLD blob's memory may be freed. Those are two
    /// different problems and only the second one crashes.</para>
    ///
    /// <para><b>⭐ The mechanism: generations + leases.</b> A reader <see cref="Borrow"/>s for the duration
    /// of its traversal. <see cref="Publish"/> retires the current generation but frees it only when its
    /// last lease is released — so a retired graph outlives every reader that is still inside it, and is
    /// then reclaimed rather than leaked. Publishing is O(1) and never blocks a reader beyond the short
    /// bookkeeping lock.</para>
    ///
    /// <para><b>⛔ OWNERSHIP IS TRANSFERRED.</b> <see cref="Publish"/> hands the blob to the holder, which
    /// disposes it once retired and unused. ⚠ The caller must NOT also dispose it: <c>RoadNetworkBlob</c>
    /// is a STRUCT, so a disposed copy does not clear the original's <c>IsCreated</c> flags and a second
    /// dispose would double-free. Pass <c>takeOwnership: false</c> when the caller genuinely keeps the
    /// blob alive itself (a test fixture, a host with a statically owned graph).</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.4 (as-built) — this is the "two things this did NOT
    ///    solve" ① that batch ① deferred and batch ② closes.
    /// </summary>
    public sealed class RoadNetworkHolder : IDisposable
    {
        // internal, not private: the public Lease's internal constructor takes one, and C# requires the
        // parameter type to be at least as accessible as the constructor.
        internal sealed class Generation
        {
            public RoadNetworkBlob Blob;
            public int  Readers;    // live leases on THIS generation
            public bool Retired;    // superseded by a later Publish (or the holder was disposed)
            public bool Owned;      // the holder is responsible for freeing Blob
        }

        private readonly object _gate = new();
        private Generation _current;
        private bool _disposed;

        /// <summary>Creates a holder carrying <paramref name="initial"/> (may be <c>default</c>).</summary>
        /// <param name="takeOwnership">
        /// When <c>true</c> the holder disposes <paramref name="initial"/> once it is retired and unused.
        /// Defaults to <c>false</c> so that constructing a holder never silently claims a blob the caller
        /// already owns — ownership is taken explicitly, at <see cref="Publish"/>.
        /// </param>
        public RoadNetworkHolder(RoadNetworkBlob initial = default, bool takeOwnership = false)
            => _current = new Generation { Blob = initial, Owned = takeOwnership };

        /// <summary>
        /// The graph as of the most recent <see cref="Publish"/>, WITHOUT taking a lease.
        /// <para>⚠ Safe only when the caller reads it and finishes before any swap can occur — i.e. on the
        /// main thread, where the swap also happens. ⛔ A BACKGROUND reader must use <see cref="Borrow"/>,
        /// or the memory it is walking can be freed under it.</para>
        /// </summary>
        public RoadNetworkBlob Current => Volatile.Read(ref _current).Blob;

        /// <summary>How many generations are still alive (current + retired-but-leased). For rails.</summary>
        public int LiveGenerations
        {
            get { lock (_gate) return 1 + _retiredAlive; }
        }
        private int _retiredAlive;

        /// <summary>
        /// Borrows the current graph for the duration of a traversal. Dispose the lease when done —
        /// the retired generation it pins cannot be freed until you do.
        /// </summary>
        public Lease Borrow()
        {
            lock (_gate)
            {
                _current.Readers++;
                return new Lease(this, _current);
            }
        }

        /// <summary>
        /// Makes <paramref name="next"/> the current graph and retires the previous one, freeing it when
        /// its last reader releases.
        /// </summary>
        /// <param name="takeOwnership">
        /// <c>true</c> (the default) transfers <paramref name="next"/> to the holder. ⛔ Do not dispose it
        /// yourself afterwards.
        /// </param>
        public void Publish(RoadNetworkBlob next, bool takeOwnership = true)
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(RoadNetworkHolder));

                var old = _current;
                Volatile.Write(ref _current, new Generation { Blob = next, Owned = takeOwnership });

                old.Retired = true;
                if (old.Readers == 0) FreeLocked(old);
                else _retiredAlive++;
            }
        }

        private void Release(Generation gen)
        {
            lock (_gate)
            {
                gen.Readers--;
                if (gen.Retired && gen.Readers == 0)
                {
                    FreeLocked(gen);
                    _retiredAlive--;
                }
            }
        }

        /// <summary>Frees a retired, unreferenced generation. Caller holds <see cref="_gate"/>.</summary>
        private static void FreeLocked(Generation gen)
        {
            if (gen.Owned) gen.Blob.Dispose();
            gen.Owned = false;      // idempotent: a second free must not double-dispose the struct copy
        }

        /// <summary>
        /// Retires the current generation. ⚠ A generation still under lease is freed by its last reader,
        /// so disposing the holder while a background tick is running is safe.
        /// </summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;

                _current.Retired = true;
                if (_current.Readers == 0) FreeLocked(_current);
                else _retiredAlive++;
            }
        }

        /// <summary>A borrowed graph. Dispose to release it.</summary>
        public readonly struct Lease : IDisposable
        {
            private readonly RoadNetworkHolder _holder;
            private readonly Generation _gen;

            internal Lease(RoadNetworkHolder holder, Generation gen)
            {
                _holder = holder;
                _gen    = gen;
            }

            /// <summary>The graph this lease pins. Valid until the lease is disposed.</summary>
            public RoadNetworkBlob Value => _gen.Blob;

            /// <inheritdoc/>
            public void Dispose() => _holder?.Release(_gen);
        }
    }
}
