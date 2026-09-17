using System.Threading;

namespace CarKinem.Road
{
    /// <summary>
    /// Thread-safe carrier for the CURRENT road graph, for consumers that cannot reach the
    /// <see cref="ZoneEnvironmentData"/> ECS singleton.
    ///
    /// <para><b>⭐ Why this exists — it is not a second source of truth.</b>
    /// <see cref="ZoneEnvironmentData"/> remains THE source; this is a thread-safe projection of it,
    /// published by the same writer at the same moment. The projection is needed because
    /// <c>ISimulationView</c> — the only thing a module's <c>Tick</c> receives — exposes <b>no singleton
    /// API at all</b> (components, queries, events and a command buffer, and nothing else).
    /// <c>CarKinematicsSystem</c> reaches singletons by downcasting the view to <c>EntityRepository</c>
    /// and <i>throwing</i> when it is not one, which is legal only because its module is
    /// <c>ExecutionPolicy.Synchronous()</c> (<c>DataStrategy.Direct</c> ⇒ the view IS the live repo).</para>
    ///
    /// <para><b>⛔ Why the solver cannot copy that pattern.</b>
    /// <c>NavigationSolverModule</c> declares <c>ExecutionPolicy.SlowBackground(10)</c>, which is
    /// <c>DataStrategy.SoD</c> — and <c>ExecutionPolicy.Validate</c> actively forbids <c>Direct</c> off the
    /// main thread ("background threads need snapshot"). So on that path the view is a snapshot, the
    /// downcast fails, and a copied <c>CarKinematicsSystem</c>-style read would throw on the module's own
    /// production path rather than return a stale blob.</para>
    ///
    /// <para><b>Publication safety.</b> <see cref="RoadNetworkBlob"/> is a multi-field struct wrapping
    /// native allocations, so a bare struct field could be read torn from another thread. The value is
    /// therefore held in an immutable box behind a single reference write, which is atomic: a reader sees
    /// either the whole previous graph or the whole new one, never a mixture.</para>
    ///
    /// <para><b>⚠ Lifetime — the hazard this class does NOT solve.</b> Publishing a new graph does not
    /// make the old one safe to dispose: a background solver may still be mid-traversal inside it, and
    /// the native arrays would be freed under it. Whoever swaps must keep the previous blob alive until
    /// no background tick can still be reading it. That is a batch-② (loader/commit) concern and is
    /// called out here so the commit path cannot be written in ignorance of it.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.4 (R2) — this is the "holder becomes necessary"
    ///    branch that section names as the outcome if a navigation module runs on a background thread.
    /// </summary>
    public sealed class RoadNetworkHolder
    {
        private sealed class Box
        {
            public readonly RoadNetworkBlob Value;
            public Box(RoadNetworkBlob value) => Value = value;
        }

        private Box _current;

        /// <summary>Creates a holder carrying <paramref name="initial"/> (may be <c>default</c>).</summary>
        public RoadNetworkHolder(RoadNetworkBlob initial = default) => _current = new Box(initial);

        /// <summary>The graph as of the most recent <see cref="Publish"/>. Safe from any thread.</summary>
        public RoadNetworkBlob Current => Volatile.Read(ref _current).Value;

        /// <summary>
        /// Makes <paramref name="next"/> the current graph. ⚠ Does not dispose the previous blob — see
        /// the lifetime note on the class.
        /// </summary>
        public void Publish(RoadNetworkBlob next) => Volatile.Write(ref _current, new Box(next));
    }
}
