namespace Bagira.IOS.Services;

/// <summary>
/// Immutable snapshot of a request that has been submitted and is awaiting
/// a matching acknowledgment.
/// </summary>
public sealed class PendingRequest
{
    public Guid RequestId { get; init; }

    /// <summary>Human-readable description for diagnostics/logging.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>UTC timestamp at which the request was tracked.</summary>
    public DateTime SentTime { get; init; }

    /// <summary>
    /// True once the request has been resolved (either by an ACK or a timeout).
    /// Resolved requests are removed from the pending dictionary immediately;
    /// this flag is here for callers that hold a snapshot reference.
    /// </summary>
    public bool IsResolved { get; internal set; }

    /// <summary>True if the request resolved successfully.</summary>
    public bool Succeeded { get; internal set; }

    /// <summary>The resolution message (e.g. error reason or "Timeout").</summary>
    public string? ResolutionMessage { get; internal set; }
}
