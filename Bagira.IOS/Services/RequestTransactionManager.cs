namespace Bagira.IOS.Services;

/// <summary>
/// Thread-safe implementation of <see cref="IRequestTransactionManager"/>.
///
/// Requests are stored in an internal dictionary keyed on <see cref="Guid"/>.
/// <see cref="CheckTimeouts"/> should be called once per frame from the main
/// update loop.
/// </summary>
public sealed class RequestTransactionManager : IRequestTransactionManager
{
    // ── Constants ────────────────────────────────────────────────────────────

    /// <summary>
    /// How long (in milliseconds) a request may be pending before it is
    /// considered timed-out and automatically failed.
    /// </summary>
    public const double DefaultTimeoutMs = 5000;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<Guid, PendingRequest> _pending = new();
    private readonly ITimeProvider _clock;

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Creates a manager that uses the real system clock.</summary>
    public RequestTransactionManager()
        : this(SystemTimeProvider.Instance) { }

    /// <summary>
    /// Creates a manager with an injectable clock, enabling deterministic
    /// unit tests without Thread.Sleep().
    /// </summary>
    public RequestTransactionManager(ITimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    // ── IRequestTransactionManager ────────────────────────────────────────────

    /// <inheritdoc/>
    public void TrackRequest(Guid requestId, string description)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID must not be empty.", nameof(requestId));

        _pending[requestId] = new PendingRequest
        {
            RequestId   = requestId,
            Description = description ?? string.Empty,
            SentTime    = _clock.UtcNow
        };
    }

    /// <inheritdoc/>
    public void CompleteRequest(Guid requestId, bool success, string? message = null)
    {
        if (!_pending.Remove(requestId, out var req))
            return; // Unknown or already resolved – silently ignore.

        req.IsResolved        = true;
        req.Succeeded         = success;
        req.ResolutionMessage = message;
    }

    /// <inheritdoc/>
    public IEnumerable<PendingRequest> GetPendingRequests()
        // Return a snapshot so callers cannot mutate the internal collection.
        => _pending.Values.ToList();

    /// <inheritdoc/>
    public void CheckTimeouts()
    {
        var now = _clock.UtcNow;

        // Collect IDs first to avoid modifying the dictionary while iterating.
        var timedOut = _pending.Values
            .Where(r => (now - r.SentTime).TotalMilliseconds > DefaultTimeoutMs)
            .Select(r => r.RequestId)
            .ToList();

        foreach (var id in timedOut)
            CompleteRequest(id, false, "Timeout");
    }
}
