namespace Bagira.IOS.Services;

/// <summary>
/// Tracks in-flight DDS requests and their correlation IDs. Provides timeout
/// detection so the IOS UI can surface stale/failed transactions.
/// </summary>
public interface IRequestTransactionManager
{
    /// <summary>Begin tracking a new outgoing request.</summary>
    void TrackRequest(Guid requestId, string description);

    /// <summary>
    /// Mark an outstanding request as resolved.
    /// No-op if the requestId is not currently tracked.
    /// </summary>
    /// <param name="success">Whether the matching ACK indicated success.</param>
    /// <param name="message">Optional detail (error reason, etc.).</param>
    void CompleteRequest(Guid requestId, bool success, string? message = null);

    /// <summary>Returns a snapshot of all currently pending (unresolved) requests.</summary>
    IEnumerable<PendingRequest> GetPendingRequests();

    /// <summary>
    /// Scans pending requests and completes any that have exceeded
    /// <see cref="RequestTransactionManager.DefaultTimeoutMs"/>
    /// with success=false and message="Timeout".
    /// Designed to be called from the main update loop.
    /// </summary>
    void CheckTimeouts();
}
