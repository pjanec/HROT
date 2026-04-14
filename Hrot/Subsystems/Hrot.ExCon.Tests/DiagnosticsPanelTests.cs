using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using Fdp.Toolkit.DER;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="DiagnosticsPanel"/>.
///
/// <para>Tests cover:
/// <list type="bullet">
///   <item><see cref="DiagnosticsPanel.GetEntityCount"/> against real and mock repos.</item>
///   <item><see cref="DiagnosticsPanel.GetPendingRequestSnapshot"/> against mock managers.</item>
///   <item>Rolling event-rate calculation via <see cref="DiagnosticsPanel.RecordEvent"/>
///   and <see cref="DiagnosticsPanel.Update"/>.</item>
/// </list>
/// No ImGui context is required.</para>
/// </summary>
public class DiagnosticsPanelTests
{
    // ── GetEntityCount ────────────────────────────────────────────────────────

    [Fact]
    public void GetEntityCount_EmptyRepo_ReturnsZero()
    {
        var repo = new DerRepo();

        Assert.Equal(0, DiagnosticsPanel.GetEntityCount(repo));
    }

    [Fact]
    public void GetEntityCount_RepoWithThreeEntities_ReturnsThree()
    {
        var repo = new DerRepo();
        repo.CreateEntity(1, 100);
        repo.CreateEntity(2, 100);
        repo.CreateEntity(3, 100);

        Assert.Equal(3, DiagnosticsPanel.GetEntityCount(repo));
    }

    [Fact]
    public void GetEntityCount_NullRepo_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DiagnosticsPanel.GetEntityCount(null!));
    }

    // ── GetPendingRequestSnapshot ─────────────────────────────────────────────

    [Fact]
    public void GetPendingRequestSnapshot_NoRequests_ReturnsEmptyList()
    {
        var txMgr = new RequestTransactionManager();

        var snapshot = DiagnosticsPanel.GetPendingRequestSnapshot(txMgr);

        Assert.Empty(snapshot);
    }

    [Fact]
    public void GetPendingRequestSnapshot_TwoRequests_ReturnsBothIds()
    {
        var txMgr = new RequestTransactionManager();
        var id1   = Guid.NewGuid();
        var id2   = Guid.NewGuid();
        txMgr.TrackRequest(id1, "Req A");
        txMgr.TrackRequest(id2, "Req B");

        var snapshot = DiagnosticsPanel.GetPendingRequestSnapshot(txMgr);

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, r => r.RequestId == id1);
        Assert.Contains(snapshot, r => r.RequestId == id2);
    }

    [Fact]
    public void GetPendingRequestSnapshot_AfterRequestCompleted_DoesNotIncludeCompleted()
    {
        var txMgr = new RequestTransactionManager();
        var id    = Guid.NewGuid();
        txMgr.TrackRequest(id, "Req");
        txMgr.CompleteRequest(id, success: true);

        var snapshot = DiagnosticsPanel.GetPendingRequestSnapshot(txMgr);

        Assert.Empty(snapshot);
    }

    [Fact]
    public void GetPendingRequestSnapshot_NullTxMgr_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DiagnosticsPanel.GetPendingRequestSnapshot(null!));
    }

    [Fact]
    public void GetPendingRequestSnapshot_UsesInterface_WorksWithMock()
    {
        var id  = Guid.NewGuid();
        var req = new PendingRequest { RequestId = id, Description = "Mock req", SentTime = DateTime.UtcNow };

        var mockMgr = new Mock<IRequestTransactionManager>();
        mockMgr.Setup(m => m.GetPendingRequests()).Returns(new[] { req });

        var snapshot = DiagnosticsPanel.GetPendingRequestSnapshot(mockMgr.Object);

        Assert.Single(snapshot);
        Assert.Equal(id, snapshot[0].RequestId);
    }

    // ── EventsPerSecond / RecordEvent / Update ────────────────────────────────

    [Fact]
    public void EventsPerSecond_InitiallyZero()
    {
        var panel = new DiagnosticsPanel();

        Assert.Equal(0f, panel.EventsPerSecond);
    }

    [Fact]
    public void Update_ZeroDt_DoesNotAdvanceWindowOrChangeRate()
    {
        var panel = new DiagnosticsPanel();
        panel.RecordEvent();

        panel.Update(0f);

        Assert.Equal(0f, panel.EventsPerSecond);
    }

    [Fact]
    public void Update_NegativeDt_DoesNotAdvanceWindowOrChangeRate()
    {
        var panel = new DiagnosticsPanel();
        panel.RecordEvent();

        panel.Update(-1f);

        Assert.Equal(0f, panel.EventsPerSecond);
    }

    [Fact]
    public void Update_BelowWindowThreshold_DoesNotCommitRate()
    {
        var panel = new DiagnosticsPanel();
        panel.RecordEvent();
        panel.RecordEvent();

        // Advance by less than the full window.
        panel.Update(PanelConstants.DiagnosticsEventRateSampleWindowS * 0.5f);

        Assert.Equal(0f, panel.EventsPerSecond);
    }

    [Fact]
    public void Update_ExceedsWindowThreshold_CommitsCorrectRate()
    {
        var panel      = new DiagnosticsPanel();
        const int evts = 10;
        for (int i = 0; i < evts; i++) panel.RecordEvent();

        // Advance exactly one full window in one step.
        panel.Update(PanelConstants.DiagnosticsEventRateSampleWindowS);

        // rate = 10 events / 5.0 s = 2.0 events/s
        Assert.Equal(2.0f, panel.EventsPerSecond, precision: 4);
    }

    [Fact]
    public void Update_MultipleSmallStepsSummingToWindow_CommitsRate()
    {
        var panel = new DiagnosticsPanel();
        panel.RecordEvent();
        panel.RecordEvent();

        float step = PanelConstants.DiagnosticsEventRateSampleWindowS / 5f;
        for (int i = 0; i < 5; i++) panel.Update(step);

        // 2 events / 5 s = 0.4 events/s
        Assert.Equal(0.4f, panel.EventsPerSecond, precision: 4);
    }

    [Fact]
    public void Update_AfterWindowCommitted_ResetsCounter()
    {
        var panel = new DiagnosticsPanel();
        panel.RecordEvent();
        panel.Update(PanelConstants.DiagnosticsEventRateSampleWindowS);
        float rateAfterFirst = panel.EventsPerSecond;

        // Second window: no new events.
        panel.Update(PanelConstants.DiagnosticsEventRateSampleWindowS);
        float rateAfterSecond = panel.EventsPerSecond;

        Assert.True(rateAfterFirst > 0f);
        Assert.Equal(0f, rateAfterSecond);
    }

    // ── Draw (smoke test) ─────────────────────────────────────────────────────

    [Fact]
    public void Draw_WithMockLogic_DoesNotThrow()
    {
        var panel = new DiagnosticsPanel();
        var logic = new Mock<IExConLogic>();

        var ex = Record.Exception(() => panel.Draw(logic.Object));

        Assert.Null(ex);
    }
}
