using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager (Saga) that owns the async <see cref="StorageGatewayModule.PrefetchScenarioAsync"/>
/// call for prefetch operations. Reads <see cref="ExecutePrefetchIntent"/> from the bus, calls the
/// gateway, and publishes <see cref="PrefetchStagingCompletedEvent"/> when the task finishes.
/// <see cref="ClusterMaster"/> reads the completed event to fan out
/// <see cref="NodeOpType.PrefetchFiles"/> or report failure.
/// </summary>
public sealed class AssetPrefetchProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly StorageGatewayModule _gateway;
    private readonly string _nasBasePath;
    private readonly string _localStagingRoot;

    /// <param name="bus">Shared event bus.</param>
    /// <param name="gateway">Storage gateway for prefetch operations.</param>
    /// <param name="nasBasePath">NAS root from which scenario files are copied.</param>
    /// <param name="localStagingRoot">
    /// Local staging root used to build per-node destination paths.
    /// Defaults to <see cref="OrchestrationConstants.DefaultStagingDirectory"/>.
    /// </param>
    public AssetPrefetchProcessManager(
        FdpEventBus bus,
        StorageGatewayModule gateway,
        string nasBasePath,
        string? localStagingRoot = null)
    {
        _bus              = bus              ?? throw new ArgumentNullException(nameof(bus));
        _gateway          = gateway          ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath      = nasBasePath      ?? throw new ArgumentNullException(nameof(nasBasePath));
        _localStagingRoot = localStagingRoot ?? OrchestrationConstants.DefaultStagingDirectory;
    }

    /// <summary>
    /// Reads <see cref="ExecutePrefetchIntent"/> from the bus and starts the gateway task.
    /// Uses <c>ContinueWith</c> to publish <see cref="PrefetchStagingCompletedEvent"/> reactively.
    /// Call once per frame before <see cref="ClusterMaster.Tick"/>.
    /// </summary>
    public void Tick()
    {
        foreach (var intent in _bus.ReadManaged<ExecutePrefetchIntent>())
        {
            if (string.IsNullOrWhiteSpace(_nasBasePath))
            {
                FdpLog<AssetPrefetchProcessManager>.Info(
                    "[AssetPrefetchProcessManager] PrefetchScenario for '{0}' skipped — no NAS base path. " +
                    "Assuming files are pre-staged locally.", intent.ScenarioId);
                _bus.PublishManaged(new PrefetchStagingCompletedEvent
                {
                    RequestId     = intent.RequestId,
                    ScenarioId    = intent.ScenarioId,
                    IsSuccess     = true,
                    ActiveNodeIds = intent.ActiveNodeIds,
                });
                continue;
            }

            var targets = BuildNodeDistributionTargets(intent.ActiveNodeIds, intent.ScenarioId);

            FdpLog<AssetPrefetchProcessManager>.Info(
                "[AssetPrefetchProcessManager] PrefetchScenario started for '{0}' (requestId={1}).",
                intent.ScenarioId, intent.RequestId);

            var capturedIntent = intent;
            _ = _gateway.PrefetchScenarioAsync(intent.ScenarioId, targets, _nasBasePath)
                .ContinueWith(task =>
                {
                    bool isSuccess = !task.IsFaulted && !task.IsCanceled
                                     && task.Result.FailureCount == 0;
                    if (!isSuccess)
                    {
                        var reason = task.IsFaulted
                            ? task.Exception?.GetBaseException().Message ?? "task faulted"
                            : task.IsCanceled
                                ? "task cancelled"
                                : $"{task.Result.FailureCount} file(s) failed to copy";
                        FdpLog<AssetPrefetchProcessManager>.Error(
                            "[AssetPrefetchProcessManager] PrefetchScenario for '{0}' failed ({1}).",
                            capturedIntent.ScenarioId, reason);
                    }

                    _bus.PublishManaged(new PrefetchStagingCompletedEvent
                    {
                        RequestId     = capturedIntent.RequestId,
                        ScenarioId    = capturedIntent.ScenarioId,
                        IsSuccess     = isSuccess,
                        ActiveNodeIds = capturedIntent.ActiveNodeIds,
                    });
                }, System.Threading.Tasks.TaskScheduler.Default);
        }

        // React to PrefetchStagingCompletedEvent: fan out PrefetchFiles on success,
        // or publish a Timeout failure so the requester is notified.
        foreach (var ev in _bus.ReadManaged<PrefetchStagingCompletedEvent>())
        {
            if (!ev.IsSuccess)
            {
                FdpLog<AssetPrefetchProcessManager>.Error(
                    "[AssetPrefetchProcessManager] PrefetchScenario for '{0}' failed — publishing Timeout for request {1}.",
                    ev.ScenarioId, ev.RequestId);
                _bus.PublishManaged(new ClusterOpCompletedEvent
                {
                    RequestId  = ev.RequestId,
                    StatusCode = OrchestrationStatusCode.Timeout,
                });
                continue;
            }

            FdpLog<AssetPrefetchProcessManager>.Info(
                "[AssetPrefetchProcessManager] PrefetchScenario for '{0}' succeeded — fanning out PrefetchFiles to {1} node(s).",
                ev.ScenarioId, ev.ActiveNodeIds.Count);
            var txId = Guid.NewGuid();
            foreach (var nodeId in ev.ActiveNodeIds)
            {
                _bus.PublishManaged(new ExecuteNodeOpIntent
                {
                    TransactionId = txId,
                    TargetNodeId  = nodeId,
                    Operation     = Fdp.Toolkit.Orchestration.NodeOpType.PrefetchFiles,
                    DomainPayload = new PrefetchHandlerPayload(ev.ScenarioId),
                });
            }
        }
    }

    private List<NodeDistributionTarget> BuildNodeDistributionTargets(List<int> nodeIds, string scenarioId)
    {
        var targets = new List<NodeDistributionTarget>(nodeIds.Count);
        foreach (var nodeId in nodeIds)
        {
            targets.Add(new NodeDistributionTarget
            {
                NodeId          = nodeId,
                DestinationPath = Path.Combine(_localStagingRoot, scenarioId),
            });
        }
        return targets;
    }
}
