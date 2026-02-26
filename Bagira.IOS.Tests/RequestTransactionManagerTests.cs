using Bagira.IOS.Services;

namespace Bagira.IOS.Tests;

// ─── Test clock stub ─────────────────────────────────────────────────────────

/// <summary>
/// Controllable clock that lets tests advance time without Thread.Sleep().
/// </summary>
internal sealed class StubClock : ITimeProvider
{
    public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Advance the clock by the specified number of milliseconds.</summary>
    public void AdvanceMs(double ms) => UtcNow = UtcNow.AddMilliseconds(ms);
}

// ─── Tests ───────────────────────────────────────────────────────────────────

public class RequestTransactionManagerTests
{
    // ── TrackRequest ──────────────────────────────────────────────────────────

    [Fact]
    public void TrackRequest_StoresPendingRequest()
    {
        var sut = new RequestTransactionManager();
        var id  = Guid.NewGuid();

        sut.TrackRequest(id, "Test request");

        var pending = sut.GetPendingRequests().ToList();
        Assert.Single(pending);
        Assert.Equal(id, pending[0].RequestId);
    }

    [Fact]
    public void TrackRequest_StoresDescription()
    {
        var sut  = new RequestTransactionManager();
        var id   = Guid.NewGuid();
        const string desc = "Create T-72 entity";

        sut.TrackRequest(id, desc);

        var req = sut.GetPendingRequests().Single();
        Assert.Equal(desc, req.Description);
    }

    [Fact]
    public void TrackRequest_StoresSentTimeFromClock()
    {
        var clock = new StubClock();
        var sut   = new RequestTransactionManager(clock);
        var id    = Guid.NewGuid();
        var expectedTime = clock.UtcNow;

        sut.TrackRequest(id, "x");

        var req = sut.GetPendingRequests().Single();
        Assert.Equal(expectedTime, req.SentTime);
    }

    [Fact]
    public void TrackRequest_MultipleRequests_AllTracked()
    {
        var sut = new RequestTransactionManager();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        sut.TrackRequest(id1, "A");
        sut.TrackRequest(id2, "B");
        sut.TrackRequest(id3, "C");

        Assert.Equal(3, sut.GetPendingRequests().Count());
    }

    [Fact]
    public void TrackRequest_EmptyGuid_Throws()
    {
        var sut = new RequestTransactionManager();
        Assert.Throws<ArgumentException>(() => sut.TrackRequest(Guid.Empty, "bad"));
    }

    // ── CompleteRequest ───────────────────────────────────────────────────────

    [Fact]
    public void CompleteRequest_RemovesFromPending()
    {
        var sut = new RequestTransactionManager();
        var id  = Guid.NewGuid();
        sut.TrackRequest(id, "req");

        sut.CompleteRequest(id, true);

        Assert.Empty(sut.GetPendingRequests());
    }

    [Fact]
    public void CompleteRequest_Success_True_SetsSucceededFlag()
    {
        var sut = new RequestTransactionManager();
        var id  = Guid.NewGuid();
        sut.TrackRequest(id, "req");

        // Capture ref before completion removes it from pending.
        var req = sut.GetPendingRequests().Single();
        sut.CompleteRequest(id, true, "OK");

        Assert.True(req.Succeeded);
        Assert.Equal("OK", req.ResolutionMessage);
        Assert.True(req.IsResolved);
    }

    [Fact]
    public void CompleteRequest_Failure_False_SetsSucceededFalse()
    {
        var sut = new RequestTransactionManager();
        var id  = Guid.NewGuid();
        sut.TrackRequest(id, "req");
        var req = sut.GetPendingRequests().Single();

        sut.CompleteRequest(id, false, "Server error");

        Assert.False(req.Succeeded);
        Assert.Equal("Server error", req.ResolutionMessage);
    }

    [Fact]
    public void CompleteRequest_UnknownId_DoesNotThrow()
    {
        var sut = new RequestTransactionManager();
        // Should be a no-op – must not throw.
        sut.CompleteRequest(Guid.NewGuid(), true);
    }

    [Fact]
    public void CompleteRequest_DoesNotAffectOtherPendingRequests()
    {
        var sut  = new RequestTransactionManager();
        var id1  = Guid.NewGuid();
        var id2  = Guid.NewGuid();
        sut.TrackRequest(id1, "A");
        sut.TrackRequest(id2, "B");

        sut.CompleteRequest(id1, true);

        var remaining = sut.GetPendingRequests().ToList();
        Assert.Single(remaining);
        Assert.Equal(id2, remaining[0].RequestId);
    }

    // ── CheckTimeouts ─────────────────────────────────────────────────────────

    [Fact]
    public void CheckTimeouts_FreshRequest_NotFlagged()
    {
        var clock = new StubClock();
        var sut   = new RequestTransactionManager(clock);
        var id    = Guid.NewGuid();
        sut.TrackRequest(id, "fresh");

        // Advance just under the threshold.
        clock.AdvanceMs(RequestTransactionManager.DefaultTimeoutMs - 1);
        sut.CheckTimeouts();

        Assert.Single(sut.GetPendingRequests()); // Still pending.
    }

    [Fact]
    public void CheckTimeouts_StaleRequest_FlaggedAsFailed()
    {
        var clock = new StubClock();
        var sut   = new RequestTransactionManager(clock);
        var id    = Guid.NewGuid();
        sut.TrackRequest(id, "stale");
        var req = sut.GetPendingRequests().Single();

        // Advance past the threshold.
        clock.AdvanceMs(RequestTransactionManager.DefaultTimeoutMs + 1);
        sut.CheckTimeouts();

        Assert.Empty(sut.GetPendingRequests()); // Removed from pending.
        Assert.True(req.IsResolved);
        Assert.False(req.Succeeded);
        Assert.Equal("Timeout", req.ResolutionMessage);
    }

    [Fact]
    public void CheckTimeouts_OnlyTimedOutRequests_AreRemoved()
    {
        var clock = new StubClock();
        var sut   = new RequestTransactionManager(clock);
        var staleId = Guid.NewGuid();
        var freshId = Guid.NewGuid();

        sut.TrackRequest(staleId, "stale");
        clock.AdvanceMs(RequestTransactionManager.DefaultTimeoutMs + 1);
        sut.TrackRequest(freshId, "fresh");

        sut.CheckTimeouts();

        var remaining = sut.GetPendingRequests().ToList();
        Assert.Single(remaining);
        Assert.Equal(freshId, remaining[0].RequestId);
    }

    [Fact]
    public void CheckTimeouts_ExactThreshold_NotFlagged()
    {
        // Boundary: elapsed == DefaultTimeoutMs exactly should NOT time out
        // because the condition is strictly greater-than (>).
        var clock = new StubClock();
        var sut   = new RequestTransactionManager(clock);
        var id    = Guid.NewGuid();
        sut.TrackRequest(id, "boundary");

        clock.AdvanceMs(RequestTransactionManager.DefaultTimeoutMs);
        sut.CheckTimeouts();

        Assert.Single(sut.GetPendingRequests());
    }

    // ── GetPendingRequests snapshot ───────────────────────────────────────────

    [Fact]
    public void GetPendingRequests_ReturnsSnapshot_NotLiveReference()
    {
        var sut = new RequestTransactionManager();
        var id  = Guid.NewGuid();
        sut.TrackRequest(id, "x");

        var snapshot = sut.GetPendingRequests().ToList();
        sut.CompleteRequest(id, true); // Mutate internal state.

        // The snapshot taken before completion should still contain the entry.
        Assert.Single(snapshot);
    }
}
