using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for CMC-S015: <see cref="EventDrivenStorageGateway"/>.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class EventDrivenStorageGatewayTests
{
    // ── Stub backend ─────────────────────────────────────────────────────────

    private sealed class StubStorageBackend : IArchiveStorageBackend
    {
        public string?            LastExportedExerciseId;
        public bool               SaveScenarioCalled;
        public CancellationToken? LastCancellationToken;

        // Signals used to block the async operation in-flight
        private TaskCompletionSource<bool>? _blocker;

        /// <summary>Makes the next ExportArchiveAsync block until <see cref="Unblock"/> is called.</summary>
        public void BlockNext() => _blocker = new TaskCompletionSource<bool>();

        /// <summary>Releases the blocked operation.</summary>
        public void Unblock() => _blocker?.TrySetResult(true);

        public async Task ExportArchiveAsync(string? exerciseId, CancellationToken ct)
        {
            LastExportedExerciseId = exerciseId;
            LastCancellationToken  = ct;
            if (_blocker != null)
            {
                using var reg = ct.Register(() => _blocker.TrySetCanceled());
                await _blocker.Task;
            }
            ct.ThrowIfCancellationRequested();
        }

        public Task ImportArchiveAsync(string? exerciseId, CancellationToken ct)
        {
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }

        public Task SaveScenarioAsync(CancellationToken ct)
        {
            SaveScenarioCalled    = true;
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }
    }

    // ── Test 1: Publishes ExecuteStorageOpIntent → ExportArchiveAsync called ─

    [Fact(Timeout = 10_000)]
    public void Tick_ExportIntent_CallsExportArchiveAsync()
    {
        var bus     = new FdpEventBus();
        var backend = new StubStorageBackend();
        var gateway = new EventDrivenStorageGateway(bus, backend);

        bus.PublishManaged(new ExecuteStorageOpIntent
        {
            RequestId  = Guid.NewGuid(),
            Operation  = StorageOpType.Export,
            ExerciseId = "X",
        });
        bus.SwapBuffers();

        gateway.Tick();

        // Give the background task a chance to run
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (backend.LastExportedExerciseId == null && DateTime.UtcNow < deadline)
            Thread.Sleep(20);

        Assert.Equal("X", backend.LastExportedExerciseId);
    }

    // ── Test 2: When export completes → StorageOpCompletedEvent on bus ───────

    [Fact(Timeout = 10_000)]
    public void ExportComplete_PublishesStorageOpCompletedEvent()
    {
        var bus     = new FdpEventBus();
        var backend = new StubStorageBackend();
        var gateway = new EventDrivenStorageGateway(bus, backend);

        var reqId = Guid.NewGuid();
        bus.PublishManaged(new ExecuteStorageOpIntent
        {
            RequestId  = reqId,
            Operation  = StorageOpType.Export,
            ExerciseId = "Y",
        });
        bus.SwapBuffers();

        gateway.Tick();

        // Wait for the async completion to publish back to the bus
        StorageOpCompletedEvent? completedEvent = null;
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (completedEvent == null && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
            bus.SwapBuffers();
            var events = bus.ReadManaged<StorageOpCompletedEvent>();
            foreach (var ev in events)
            {
                if (ev.RequestId == reqId)
                {
                    completedEvent = ev;
                    break;
                }
            }
        }

        Assert.NotNull(completedEvent);
        Assert.Equal(reqId, completedEvent!.Value.RequestId);
        Assert.Equal(OrchestrationStatusCode.Success, completedEvent!.Value.StatusCode);
    }

    // ── Test 3: CancelOperationIntent cancels in-flight export ───────────────

    [Fact(Timeout = 10_000)]
    public void CancelIntent_CancelsInflightExport()
    {
        var bus     = new FdpEventBus();
        var backend = new StubStorageBackend();
        var gateway = new EventDrivenStorageGateway(bus, backend);

        // Make the export block indefinitely until cancelled
        backend.BlockNext();

        var reqId = Guid.NewGuid();
        bus.PublishManaged(new ExecuteStorageOpIntent
        {
            RequestId  = reqId,
            Operation  = StorageOpType.Export,
            ExerciseId = "Z",
        });
        bus.SwapBuffers();

        gateway.Tick();

        // Wait until the export is in-flight (backend has captured the CancellationToken)
        var startDeadline = DateTime.UtcNow.AddSeconds(3);
        while (backend.LastCancellationToken == null && DateTime.UtcNow < startDeadline)
            Thread.Sleep(20);

        Assert.NotNull(backend.LastCancellationToken);
        Assert.False(backend.LastCancellationToken!.Value.IsCancellationRequested);

        // Publish cancel intent
        bus.PublishManaged(new CancelOperationIntent { TargetRequestId = reqId });
        bus.SwapBuffers();

        gateway.Tick();

        // Token should now be cancelled
        var cancelDeadline = DateTime.UtcNow.AddSeconds(3);
        while (!backend.LastCancellationToken!.Value.IsCancellationRequested &&
               DateTime.UtcNow < cancelDeadline)
            Thread.Sleep(20);

        Assert.True(backend.LastCancellationToken!.Value.IsCancellationRequested);
    }
}
