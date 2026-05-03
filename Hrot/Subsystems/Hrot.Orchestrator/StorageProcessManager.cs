using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager (Saga) that handles NAS storage pulls for SerializeLocal operations.
/// Reacts to <see cref="ClusterOpCompletedEvent"/> carrying aggregated file manifests
/// and coordinates the pull to NAS via <see cref="StorageGatewayModule"/>.
/// Prepends the orchestrator's own manifest entry received via
/// <see cref="GlobalContextManifestReadyEvent"/> (TASK-P001).
/// Also handles the ExportArchive NAS pull path, distinguished via
/// <see cref="ExportArchiveBegunEvent"/> published by <see cref="ClusterMaster"/>.
/// </summary>
public sealed class StorageProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly StorageGatewayModule _gateway;
    private readonly string _nasBasePath;

    // Latest orchestrator manifest entry received from GlobalContextProcessManager (TASK-P001).
    private FileManifestEntry? _pendingOrchestratorEntry;

    // Archive export contexts keyed by the SerializeLocal transaction ID.
    // Set via ExportArchiveBegunEvent; consumed when the matching ClusterOpCompletedEvent arrives.
    private readonly Dictionary<Guid, (Guid ArchiveRequestId, CancellationTokenSource Cts)>
        _pendingArchiveExports = new();
    private readonly HashSet<Guid> _pendingSaveScenarios = new();

    /// <param name="bus">Shared event bus.</param>
    /// <param name="gateway">Storage gateway for NAS pull operations.</param>
    /// <param name="nasBasePath">Root directory on the NAS for scenario files.</param>
    public StorageProcessManager(
        FdpEventBus bus,
        StorageGatewayModule gateway,
        string nasBasePath)
    {
        _bus         = bus         ?? throw new ArgumentNullException(nameof(bus));
        _gateway     = gateway     ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath = nasBasePath ?? throw new ArgumentNullException(nameof(nasBasePath));
    }

    /// <summary>
    /// Checks for <see cref="ClusterOpCompletedEvent"/> with aggregated manifest payloads
    /// and initiates NAS pulls. Call once per frame in Phase 3, after
    /// <see cref="ClusterMaster.Tick"/>.
    /// </summary>
    public void Tick()
    {
        // Capture orchestrator manifest entry published by GlobalContextProcessManager (TASK-P001).
        foreach (var mev in _bus.ReadManaged<GlobalContextManifestReadyEvent>())
            _pendingOrchestratorEntry = mev.Entry;

        // Capture archive export contexts so we can route ClusterOpCompletedEvent correctly.
        foreach (var aev in _bus.ReadManaged<ExportArchiveBegunEvent>())
            _pendingArchiveExports[aev.TransactionId] = (aev.ArchiveRequestId, aev.Cts);

        // Track SaveScenario lifecycles so unrelated manifest payloads are not misrouted.
        foreach (var sev in _bus.ReadManaged<ExecuteStorageOpIntent>())
        {
            if (sev.Operation == StorageOpType.SaveScenario)
                _pendingSaveScenarios.Add(sev.RequestId);
        }

        // ImportArchive: prefetch files from NAS to per-node staging directories.
        foreach (var iev in _bus.ReadManaged<ImportArchiveBegunEvent>())
        {
            var importRequestId = iev.RequestId;
            _ = _gateway.PrefetchArchiveAsync(iev.ExerciseId, iev.Targets, _nasBasePath, iev.Cts.Token)
                .ContinueWith(t =>
                {
                    if (t.IsCanceled)
                    {
                        _bus.PublishManaged(new ClusterOpCompletedEvent
                        {
                            RequestId  = importRequestId,
                            StatusCode = OrchestrationStatusCode.Rejected,
                        });
                    }
                    else if (t.IsFaulted)
                    {
                        FdpLog<StorageProcessManager>.Error(
                            "[StorageProcessManager] ImportArchive NAS prefetch failed: {0}",
                            t.Exception?.GetBaseException().Message ?? "unknown error");
                        _bus.PublishManaged(new ClusterOpCompletedEvent
                        {
                            RequestId  = importRequestId,
                            StatusCode = OrchestrationStatusCode.Rejected,
                        });
                    }
                    else
                    {
                        _bus.PublishManaged(new ClusterOpCompletedEvent
                        {
                            RequestId  = importRequestId,
                            StatusCode = OrchestrationStatusCode.Success,
                        });
                    }
                }, System.Threading.Tasks.TaskScheduler.Default);
        }

        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.StatusCode != OrchestrationStatusCode.Success) continue;
            if (ev.ResultPayload is not List<FileManifestEntry> manifest) continue;
            if (manifest.Count == 0) continue;

            // ExportArchive path: use CTS and publish final status for the archive request ID.
            if (_pendingArchiveExports.TryGetValue(ev.RequestId, out var archCtx))
            {
                _pendingArchiveExports.Remove(ev.RequestId);
                var archRequestId = archCtx.ArchiveRequestId;
                var archCts       = archCtx.Cts;

                _ = _gateway.PullToNasAsync(manifest, _nasBasePath, archCts.Token)
                    .ContinueWith(pullTask =>
                    {
                        if (pullTask.IsCanceled)
                        {
                            _bus.PublishManaged(new ClusterOpCompletedEvent
                            {
                                RequestId  = archRequestId,
                                StatusCode = OrchestrationStatusCode.Rejected,
                            });
                        }
                        else if (pullTask.IsFaulted)
                        {
                            FdpLog<StorageProcessManager>.Error(
                                "[StorageProcessManager] ExportArchive NAS pull failed: {0}",
                                pullTask.Exception?.GetBaseException().Message ?? "unknown error");
                            _bus.PublishManaged(new ClusterOpCompletedEvent
                            {
                                RequestId  = archRequestId,
                                StatusCode = OrchestrationStatusCode.Rejected,
                            });
                        }
                        else if (pullTask.Result.IsFullSuccess)
                        {
                            _bus.PublishManaged(new ClusterOpCompletedEvent
                            {
                                RequestId  = archRequestId,
                                StatusCode = OrchestrationStatusCode.Success,
                            });
                        }
                        else
                        {
                            FdpLog<StorageProcessManager>.Error(
                                "[StorageProcessManager] ExportArchive NAS pull partial failure: {0} file(s) failed",
                                pullTask.Result.FailureCount);
                            _bus.PublishManaged(new ClusterOpCompletedEvent
                            {
                                RequestId  = archRequestId,
                                StatusCode = OrchestrationStatusCode.Rejected,
                            });
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
                continue;
            }

            // SaveScenario path only: prepend orchestrator entry if available and pull to NAS.
            if (!_pendingSaveScenarios.Remove(ev.RequestId))
                continue;

            var fullManifest = new List<FileManifestEntry>(manifest);
            if (_pendingOrchestratorEntry != null)
            {
                fullManifest.Insert(0, _pendingOrchestratorEntry);
                _pendingOrchestratorEntry = null;
            }

            if (fullManifest.Count == 0) continue;

            _ = _gateway.PullToNasAsync(fullManifest, _nasBasePath)
                .ContinueWith(pullTask =>
                {
                    if (pullTask.IsCompletedSuccessfully && pullTask.Result.IsFullSuccess)
                    {
                        _ = _gateway.WriteScenarioManifestAsync(fullManifest, _nasBasePath);
                    }
                    else if (pullTask.IsFaulted)
                    {
                        FdpLog<StorageProcessManager>.Error(
                            "[StorageProcessManager] NAS pull failed: {0}",
                            pullTask.Exception?.GetBaseException().Message ?? "unknown error");
                    }
                    else if (pullTask.IsCompletedSuccessfully)
                    {
                        FdpLog<StorageProcessManager>.Error(
                            "[StorageProcessManager] NAS pull partial failure: {0} file(s) failed",
                            pullTask.Result.FailureCount);
                    }
                }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }
}
