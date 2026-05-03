using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager (Saga) that handles NAS pulls for <c>DumpDiagnostics</c> cluster
/// operations.
///
/// <para>
/// Reacts to <see cref="ExecuteDiagnosticDumpIntent"/> to record pending request IDs,
/// and to <see cref="ClusterOpCompletedEvent"/> to decide whether to initiate a NAS pull
/// (on success) or publish an immediate failure status (on abort/rejection).
/// </para>
///
/// <para>
/// Holds a reference to <see cref="DiagnosticsConsensusAggregator"/> to call
/// <see cref="DiagnosticsConsensusAggregator.TakeFullManifest"/> after the cluster op
/// succeeds, obtaining the paths with SourceUnc populated for
/// <see cref="StorageGatewayModule.PullToNasAsync"/>.
/// </para>
///
/// <para>Call <see cref="Tick"/> once per frame after <see cref="ClusterMaster.Tick"/>.</para>
/// </summary>
public sealed class DiagnosticsDumpProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly StorageGatewayModule _gateway;
    private readonly string _nasBasePath;
    private readonly DiagnosticsConsensusAggregator _aggregator;

    // Tracks request IDs for in-flight DumpDiagnostics operations.
    private readonly HashSet<Guid> _pendingDumpRequestIds = new();

    /// <param name="bus">Shared event bus.</param>
    /// <param name="gateway">Storage gateway for NAS pull operations.</param>
    /// <param name="nasBasePath">Root directory on the NAS for diagnostic files.</param>
    /// <param name="aggregator">Aggregator that holds the full manifest (with SourceUnc).</param>
    public DiagnosticsDumpProcessManager(
        FdpEventBus bus,
        StorageGatewayModule gateway,
        string nasBasePath,
        DiagnosticsConsensusAggregator aggregator)
    {
        _bus         = bus         ?? throw new ArgumentNullException(nameof(bus));
        _gateway     = gateway     ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath = nasBasePath ?? throw new ArgumentNullException(nameof(nasBasePath));
        _aggregator  = aggregator  ?? throw new ArgumentNullException(nameof(aggregator));
    }

    /// <summary>
    /// Processes pending diagnostics dump intents and completed cluster op events.
    /// Call once per frame in Phase 3, after <see cref="ClusterMaster.Tick"/>.
    /// </summary>
    public void Tick()
    {
        // Track in-flight dump requests published by ClusterOpMasterTranslator.
        foreach (var intent in _bus.ReadManaged<ExecuteDiagnosticDumpIntent>())
            _pendingDumpRequestIds.Add(intent.RequestId);

        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (!_pendingDumpRequestIds.Contains(ev.RequestId)) continue;
            _pendingDumpRequestIds.Remove(ev.RequestId);

            if (ev.StatusCode != OrchestrationStatusCode.Success)
            {
                // Abort / rejection: publish failure immediately without NAS pull.
                _bus.PublishManaged(new ClusterOpCompletedEvent
                {
                    RequestId  = ev.RequestId,
                    StatusCode = OrchestrationStatusCode.Failure,
                });
                continue;
            }

            // Obtain the full manifest from the aggregator (SourceUnc present).
            var fullManifest = _aggregator.TakeFullManifest();
            if (fullManifest == null || fullManifest.Count == 0) continue;

            var requestId        = ev.RequestId;
            var strippedManifest = ev.ResultPayload as List<FileManifestEntry>;

            _ = _gateway.PullToNasAsync(fullManifest, _nasBasePath)
                .ContinueWith(pullTask =>
                {
                    // PullToNasAsync swallows per-file errors internally (to avoid aborting
                    // parallel copies mid-flight); check both task fault AND partial failures
                    // via the returned FailureCount.
                    bool succeeded = pullTask.IsCompletedSuccessfully
                                     && pullTask.Result.FailureCount == 0;

                    if (succeeded)
                    {
                        _bus.PublishManaged(new ClusterOpCompletedEvent
                        {
                            RequestId     = requestId,
                            StatusCode    = OrchestrationStatusCode.Success,
                            ResultPayload = strippedManifest,
                        });
                    }
                    else
                    {
                        FdpLog<DiagnosticsDumpProcessManager>.Error(
                            "[DiagnosticsDumpProcessManager] NAS pull failed: {0}",
                            pullTask.Exception?.GetBaseException().Message ?? "unknown error");
                        _bus.PublishManaged(new ClusterOpCompletedEvent
                        {
                            RequestId  = requestId,
                            StatusCode = OrchestrationStatusCode.Failure,
                        });
                    }
                }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }
}
