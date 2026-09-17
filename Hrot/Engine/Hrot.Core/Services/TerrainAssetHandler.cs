using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;

namespace Hrot.Map.Common.Services
{
    /// <summary>
    /// ⭐⭐⭐ <b>D3 — THE ONE terrain/zone op handler, registered on EVERY ECS host.</b>
    ///
    /// <para>It serves both halves of the protocol: the per-zone round
    /// (<c>PrepareZone</c>/<c>CommitZone</c>, §9.3) and the asset build
    /// (<c>PrepareTerrainAsset</c>/<c>CommitTerrainAsset</c>, §3.1). Both land in the same
    /// <see cref="TerrainLoadService"/> the scenario-load path uses, so "reload" and "load on scenario
    /// open" cannot drift apart.</para>
    ///
    /// <para>⛔⛔ <b>THE ACK IS UNCONDITIONAL, and there is NO role × kind matrix</b> (§8.3 N6).
    /// 🔒 User: <i>"Host not taking active part should always ack to avoid blocking, why a matrix is
    /// needed?"</i> A host with static terrain satisfies the zone-load postcondition — <i>the terrain
    /// covering this zone is resident</i> — TRIVIALLY, because all of it already is. Static is not a
    /// degraded mode; there is nothing to advertise because nothing is missing. Whether WORK happens is
    /// decided by ONE thing only: the loaders this host actually composed. Role filtering already
    /// happened at composition time, and re-applying it here would be a second mechanism for one
    /// decision (ruling 9).</para>
    ///
    /// <para>⚠ <b>The two-phase split is forced by the interface.</b> <see cref="PrepareAsync"/> must not
    /// mutate ECS, and it is where the time goes; <see cref="Commit"/> is main-thread and is where the
    /// markers land. So: plan + build in prepare, stamp in commit. ⛔ It therefore does NOT call
    /// <c>EnsureLoaded</c>, which does both — but it does not re-derive staleness either: that branch
    /// lives once, in <c>TerrainLoadService.Plan</c>.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3.1, §8.3, §9.3.
    /// </summary>
    public sealed class TerrainAssetHandler : IClusterStateHandler
    {
        private sealed class Staged
        {
            public IReadOnlyList<ZoneLoadItem> Planned = Array.Empty<ZoneLoadItem>();
            public IReadOnlyList<ZoneLoadItem> Failed  = Array.Empty<ZoneLoadItem>();
        }

        private readonly TerrainLoadService? _service;
        private readonly EntityRepository?   _world;
        private readonly long                _nodeId;
        private readonly string?             _stagingRoot;

        // Keyed by transaction so two overlapping zone rounds (§9.3 — one op per zone, so overlap is the
        // NORMAL case) cannot clobber each other's plan.
        private readonly Dictionary<Guid, Staged> _staged = new();
        private readonly object _stagedGate = new();

        /// <summary>The round an in-flight <c>AbortTransaction</c> names, handed from prepare to commit.</summary>
        private Guid? _pendingAbort;

        /// <param name="service">
        /// The node's terrain loader, or <see langword="null"/> on a host that composed none. ⚠ Null is
        /// NOT an error and NOT a silent failure: it means this host has nothing to make resident, which
        /// is the trivially-satisfied case (§8.3). It still ACKs.
        /// </param>
        /// <param name="world">
        /// 🔴 The node's repository. <b>Required in production on an ECS host</b> — <c>ClusterSlave</c>
        /// commits with <c>repo: null</c> at both of its dispatch sites (<c>ClusterSlave.cs:271</c>,
        /// <c>:432</c>), so a handler that writes only through that parameter writes nothing at all.
        /// </param>
        /// <param name="localStagingRoot">
        /// The node's local staging root, where the prefetched <c>TKB/ScenarioHeader.json</c> lands.
        /// Enables the D5 terrain-identity check; <see langword="null"/> disables it.
        /// ⚠ EVERY host registers a prefetch handler, so every host HAS this — a null here is a host that
        /// has not been wired for the check, and that is a known gap, not a design choice.
        /// </param>
        public TerrainAssetHandler(
            TerrainLoadService? service,
            EntityRepository? world,
            long nodeId = 0,
            string? localStagingRoot = null)
        {
            _service     = service;
            _world       = world;
            _nodeId      = nodeId;
            _stagingRoot = localStagingRoot;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 🔴 <b><c>AbortTransaction</c> is claimed here deliberately, and the measurement is the
        /// reason.</b> <c>IClusterStateHandler.Abort</c> has <b>zero</b> production callers —
        /// <c>ClusterSlave</c> never invokes it — and measured <c>2026-09-17</c>, <b>no handler in the
        /// tree claimed <c>NodeOpType.AbortTransaction</c> either</b>. So the master's abort fan-out
        /// reached every node, found no handler, and auto-ACKed <c>Success</c> while nothing rolled
        /// back. ⛔ Leaving it that way would make this design's own abort arm the silent no-op it is
        /// meant to prevent (<c>R-133</c>).
        /// </remarks>
        /// <inheritdoc/>
        public bool CanHandle(NodeOpType operation) =>
            operation is NodeOpType.PrepareZone
                      or NodeOpType.CommitZone
                      or NodeOpType.PrepareTerrainAsset
                      or NodeOpType.CommitTerrainAsset
                      or NodeOpType.AbortTransaction;

        /// <inheritdoc/>
        /// <remarks>
        /// ⛔ No ECS mutation — the interface's contract, and the reason the markers are stamped in
        /// <see cref="Commit"/> instead of here.
        /// </remarks>
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            // The commit half of either pair has nothing to prepare; it consumes what its prepare staged.
            if (intent.Operation is NodeOpType.CommitZone or NodeOpType.CommitTerrainAsset)
                return Task.FromResult<object?>(null);

            // An abort names the round to roll back. The rollback itself is an ECS write, so it waits
            // for Commit — which ClusterSlave calls immediately after this returns.
            if (intent.Operation == NodeOpType.AbortTransaction)
            {
                _pendingAbort = (intent.DomainPayload as AbortTransactionPayload?)?.TargetTransactionId;
                return Task.FromResult<object?>(null);
            }

            // ⛔⛔ D5 — THE TERRAIN-IDENTITY CHECK, and it runs BEFORE the "nothing composed" ACK below.
            //   §8.3 N4: loading the terrain a scenario names is MANDATORY, so a node must verify it
            //   actually holds that terrain and fail loudly NAMING ITSELF if not. ⚠ Order matters: a host
            //   with no loader composed is the exact host this is meant to catch. If the check sat after
            //   the ACK, the one case it exists for would be the one case it skipped.
            VerifyTerrainIdentity(intent);

            // ⭐ Nothing composed, or no world ⇒ nothing to make resident ⇒ the postcondition already
            //   holds. ACK. This is the "static terrain" host, and it is not degraded.
            if (_service == null || _world == null)
            {
                FdpLog<TerrainAssetHandler>.Debug(
                    "[Node-{0}] {1}: no terrain loader composed — nothing to do, ACK.",
                    _nodeId, intent.Operation);
                return Task.FromResult<object?>(null);
            }

            string? zoneFilter = intent.Operation == NodeOpType.PrepareZone
                ? (intent.DomainPayload as ZoneOpPayload?)?.ZoneId
                : null;   // the asset build sweeps every zone

            var planned = _service.Plan(_world, zoneFilter);
            var failed  = _service.BuildStaged(planned);

            lock (_stagedGate)
                _staged[intent.TransactionId] = new Staged { Planned = planned, Failed = failed };

            if (failed.Count > 0)
            {
                // ⛔ FAIL LOUDLY. A zone whose tiles did not become resident is a node that cannot render
                //   or path that area correctly; reporting Success would make the round green and the
                //   node wrong (R-133). The faulted task becomes a Failure ACK, and the master aborts.
                throw new InvalidOperationException(
                    $"Node {_nodeId}: {failed.Count} of {planned.Count} zone(s) failed to become resident "
                  + $"for operation {intent.Operation}.");
            }

            FdpLog<TerrainAssetHandler>.Info(
                "[Node-{0}] {1}: {2} zone(s) staged resident{3}.",
                _nodeId, intent.Operation, planned.Count,
                zoneFilter == null ? "" : $" (zone '{zoneFilter}')");

            return Task.FromResult<object?>(null);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// ⭐ The markers land here and nowhere else. A host that planned nothing stamps nothing — and
        /// that is not a gap: with nothing stale there was nothing to record.
        /// </remarks>
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (intent.Operation == NodeOpType.AbortTransaction)
            {
                RollBack(_pendingAbort, repo);
                _pendingAbort = null;
                return;
            }

            // ⛔ Only the COMMIT half consumes the staging. The slave calls Commit after a successful
            //    PrepareZone too, and stamping there would record residency before the cluster barrier —
            //    a node that committed while another node's prepare was still failing.
            if (intent.Operation is not (NodeOpType.CommitZone or NodeOpType.CommitTerrainAsset)) return;

            var staged = TakeStaged(intent.TransactionId);
            if (staged == null || staged.Planned.Count == 0) return;

            var targetRepo = repo ?? _world;
            if (targetRepo == null || _service == null) return;

            _service.Stamp(targetRepo, staged.Planned, LoadPhase.Loaded);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// ⚠ Provided for symmetry and for direct callers (tests, a future dispatcher). In production the
        /// rollback arrives as an <c>AbortTransaction</c> NodeOp and lands in <see cref="Commit"/> —
        /// <c>ClusterSlave</c> never calls this method on any handler.
        /// </remarks>
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
            => RollBack(intent.TransactionId, repo);

        /// <summary>
        /// Discards a round's staging and records what genuinely failed.
        ///
        /// <para>⚠ An abort reaches EVERY node, including ones whose prepare succeeded. Those must NOT be
        /// marked <c>Failed</c> — their tiles really are resident; they simply must not record a commit
        /// that never happened, so their markers are left exactly as they were. Only the zones that
        /// failed on THIS node are stamped, so an operator can see which one broke.</para>
        /// </summary>
        private void RollBack(Guid? transactionId, EntityRepository? repo)
        {
            if (transactionId == null) return;

            var staged = TakeStaged(transactionId.Value);
            if (staged == null || staged.Failed.Count == 0) return;

            var targetRepo = repo ?? _world;
            if (targetRepo == null || _service == null) return;

            _service.Stamp(targetRepo, staged.Failed, LoadPhase.Failed);
        }

        /// <summary>
        /// ⛔⛔ <b>D5 — the node must HOLD the terrain its scenario names, and say WHICH node it is when
        /// it does not.</b>
        ///
        /// <para>🔒 User (§8.3): <i>"no host should be fully static, in a sense that it can never load
        /// another terrain. The ability to load terrain which is defined in the scenario is mandatory.
        /// Maybe right now some hosts like stride do not support it but this is more a bug and
        /// unimplemented feature than something we can live with."</i></para>
        ///
        /// <para>⭐ That ruling is what makes this a THROW and not a warning. A host that cannot hold the
        /// scenario's terrain is BROKEN, not static — the two must not look the same, and today they do:
        /// a host with no terrain loader passes every op silently. §8.3 N4 names exactly this as how
        /// "Stride's present gap should surface instead of silently passing".</para>
        ///
        /// <para>⚠ It does NOT fire when the scenario names no terrain (legal, §2.1e ①) or when the node
        /// has no staging root wired (the check is simply not armed there, and that is stated at the
        /// constructor rather than hidden).</para>
        /// </summary>
        private void VerifyTerrainIdentity(ExecuteNodeOpIntent intent)
        {
            string? required = ScenarioTerrainName.Read(_stagingRoot);
            if (string.IsNullOrWhiteSpace(required)) return;   // no terrain named — nothing to verify

            string? held = _world != null && _world.HasSingletonManaged<TerrainDefinition>()
                ? _world.GetSingletonManaged<TerrainDefinition>()?.Name
                : null;

            if (string.Equals(held, required, StringComparison.OrdinalIgnoreCase)) return;

            throw new InvalidOperationException(
                $"[TerrainAsset] Node {_nodeId} does not hold the terrain its scenario requires: "
              + $"scenario names '{required}', node holds "
              + $"{(held == null ? "NO terrain at all" : $"'{held}'")}. Operation {intent.Operation} "
              + "cannot be satisfied. Loading the terrain a scenario names is mandatory — a node that "
              + "cannot is BROKEN, not statically-terrained, and must not pass silently.");
        }

        private Staged? TakeStaged(Guid transactionId)
        {
            lock (_stagedGate)
            {
                if (!_staged.Remove(transactionId, out var staged)) return null;
                return staged;
            }
        }
    }
}
