using System;
using System.Collections.Generic;

namespace Fdp.Core.Diagnostics
{
    /// <summary>
    /// Captured event snapshot stored in the circular history buffer.
    /// </summary>
    public record CapturedEventDto(
        uint Frame,
        string ProviderName,
        string TypeName,
        bool IsManaged,
        string Summary,
        object? RawEvent);

    /// <summary>
    /// Headless service that maintains a thread-safe circular buffer of the most-recent
    /// simulation events. Populated by <c>EventHistoryCaptureSystem</c> in the
    /// <c>PostSimulation</c> phase; read by <c>EventBrowserPanel</c> and the cluster dump handler.
    /// </summary>
    public interface IDiagnosticEventHistoryService
    {
        /// <summary>
        /// Reads all active event streams from <paramref name="eventBus"/> and appends
        /// captured events to the circular buffer. Intended to be called once per simulation
        /// tick from a <c>PostSimulation</c> system.
        /// </summary>
        void Capture(string providerName, FdpEventBus eventBus, uint currentFrame);

        /// <summary>
        /// Returns a stable snapshot of the current buffer contents.
        /// Uses copy-under-lock: the lock is released before returning so callers can
        /// serialise the result without stalling the simulation writer thread.
        /// </summary>
        /// <param name="providerFilter">
        /// When non-null and non-empty, only events from matching provider names
        /// are included.
        /// </param>
        CapturedEventDto[] GetHistory(IReadOnlyList<string>? providerFilter = null);

        /// <summary>Clears all entries from the circular buffer.</summary>
        void ClearHistory();
    }
}
