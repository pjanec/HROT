using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Minimal storage back-end interface required by <see cref="EventDrivenStorageGateway"/>.
/// Implement this interface in the application layer or via a test stub.
/// </summary>
public interface IArchiveStorageBackend
{
    /// <summary>Exports the archive for <paramref name="exerciseId"/> to the NAS.</summary>
    Task ExportArchiveAsync(string? exerciseId, CancellationToken ct);

    /// <summary>Imports the archive for <paramref name="exerciseId"/> from the NAS.</summary>
    Task ImportArchiveAsync(string? exerciseId, CancellationToken ct);

    /// <summary>Saves the current scenario state.</summary>
    Task SaveScenarioAsync(CancellationToken ct);
}

/// <summary>
/// Bus-driven service that dispatches async storage operations (archive export/import,
/// scenario save) in response to <see cref="ExecuteStorageOpIntent"/> events and
/// supports in-flight cancellation via <see cref="CancelOperationIntent"/>.
///
/// <para>On completion, publishes a <see cref="StorageOpCompletedEvent"/> onto the bus.</para>
/// </summary>
public sealed class EventDrivenStorageGateway
{
    private readonly FdpEventBus             _bus;
    private readonly IArchiveStorageBackend  _storage;

    /// <summary>Active cancellation tokens keyed by <c>RequestId</c>.</summary>
    private readonly System.Collections.Generic.Dictionary<Guid, CancellationTokenSource>
        _activeCancellations = new();

    /// <summary>Initialises a new <see cref="EventDrivenStorageGateway"/>.</summary>
    public EventDrivenStorageGateway(
        FdpEventBus            bus,
        IArchiveStorageBackend storage)
    {
        _bus     = bus     ?? throw new ArgumentNullException(nameof(bus));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// Processes one frame: dispatches pending storage operations and handles
    /// cancellation requests.
    /// </summary>
    public void Tick()
    {
        // ── Dispatch new storage operations ────────────────────────────────
        foreach (var intent in _bus.ConsumeManaged<ExecuteStorageOpIntent>())
        {
            var cts = new CancellationTokenSource();
            _activeCancellations[intent.RequestId] = cts;
            // Fire-and-forget; completion publishes back to the bus.
            _ = ExecuteStorageOpAsync(intent, cts.Token);
        }

        // ── Handle cancellation requests ────────────────────────────────────
        foreach (var cancel in _bus.ConsumeManaged<CancelOperationIntent>())
        {
            if (_activeCancellations.TryGetValue(cancel.TargetRequestId, out var cts))
                cts.Cancel();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task ExecuteStorageOpAsync(ExecuteStorageOpIntent intent, CancellationToken ct)
    {
        try
        {
            switch (intent.Operation)
            {
                case StorageOpType.Export:
                    await _storage.ExportArchiveAsync(intent.ExerciseId, ct).ConfigureAwait(false);
                    break;
                case StorageOpType.Import:
                    await _storage.ImportArchiveAsync(intent.ExerciseId, ct).ConfigureAwait(false);
                    break;
                case StorageOpType.SaveScenario:
                    await _storage.SaveScenarioAsync(ct).ConfigureAwait(false);
                    break;
            }

            _bus.PublishManaged(new StorageOpCompletedEvent
            {
                RequestId    = intent.RequestId,
                StatusCode   = OrchestrationStatusCode.Success,
                SuccessCount = 1,
                FailureCount = 0,
            });
        }
        catch (OperationCanceledException)
        {
            _bus.PublishManaged(new StorageOpCompletedEvent
            {
                RequestId    = intent.RequestId,
                StatusCode   = OrchestrationStatusCode.Cancelled,
                SuccessCount = 0,
                FailureCount = 1,
            });
        }
        catch
        {
            _bus.PublishManaged(new StorageOpCompletedEvent
            {
                RequestId    = intent.RequestId,
                StatusCode   = OrchestrationStatusCode.Failure,
                SuccessCount = 0,
                FailureCount = 1,
            });
        }
        finally
        {
            _activeCancellations.Remove(intent.RequestId);
        }
    }
}
