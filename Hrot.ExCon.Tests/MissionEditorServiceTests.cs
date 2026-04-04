using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.ExCon.Services;
using Hrot.Map.Common.Dds;
using Hrot.Common.Events;
using FDP.Toolkit.DER;
using Fdp.Kernel;
using Moq;

namespace Hrot.ExCon.Tests;

// ─── Writer stub ──────────────────────────────────────────────────────────────

/// <summary>
/// Captures written DDS samples for assertion.
/// </summary>
internal sealed class CapturingWriter<T> : IDdsWriter<T>
{
    public List<T> Written { get; } = new();
    public void Write(T sample) => Written.Add(sample);
    public void DisposeInstance(T key) { }
}

// ─── Tests ────────────────────────────────────────────────────────────────────

public class MissionEditorServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Short timeout used in tests to keep the suite fast.
    private const int TestTimeoutMs = 200;

    private static (MissionEditorService Svc, FdpEventBus Bus, DerRepo Repo)
        CreateSut(int timeoutMs = TestTimeoutMs)
    {
        var repo = new DerRepo();
        var bus  = new FdpEventBus();
        var svc  = new MissionEditorService(repo, bus, timeoutMs);
        return (svc, bus, repo);
    }

    // ── Helper: drain intent published by last Commit/SendControl call ─────────

    private static MissionControlIntent DrainIntent(FdpEventBus bus)
    {
        bus.SwapBuffers();
        return bus.ConsumeManaged<MissionControlIntent>().Single();
    }

    // ── GetMissionSnapshot ────────────────────────────────────────────────────

    [Fact]
    public void GetMissionSnapshot_EntityNotFound_ReturnsNullZero()
    {
        var (svc, _, _) = CreateSut();
        var (plan, version) = svc.GetMissionSnapshot(99);

        Assert.Null(plan);
        Assert.Equal(0, version);
    }

    [Fact]
    public void GetMissionSnapshot_EntityWithMission_ReturnsPlan()
    {
        var (svc, _, repo) = CreateSut();
        var entity = repo.CreateEntity(1, 100);

        var expectedPlan = new MissionPlan
        {
            ActiveTaskId = Guid.NewGuid(),
            Tasks        = new List<MissionTask>()
        };
        entity.SetDescriptor(new EntityMission { EntityId = 1, Plan = expectedPlan });

        var (plan, _) = svc.GetMissionSnapshot(1);

        Assert.NotNull(plan);
        Assert.Equal(expectedPlan.ActiveTaskId, plan!.Value.ActiveTaskId);
    }

    [Fact]
    public void GetMissionSnapshot_EntityWithVersion_ReturnsCorrectVersion()
    {
        var (svc, _, repo) = CreateSut();
        var entity = repo.CreateEntity(2, 100);
        entity.SetDescriptor(new DescriptorOptimisticLock { EntityId = 2, CurrentVersion = 7 });

        var (_, version) = svc.GetMissionSnapshot(2);

        Assert.Equal(7, version);
    }

    // ── CommitMissionAsync – successful ACK ───────────────────────────────────

    [Fact]
    public async Task CommitMissionAsync_SuccessfulAck_ReturnsSuccess()
    {
        var (svc, bus, _) = CreateSut();
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };

        var commitTask = svc.CommitMissionAsync(entityId: 10, newPlan: plan, baseVersion: 3);

        // Retrieve the requestId from the intent published to the bus.
        var intent = DrainIntent(bus);
        svc.OnAckReceived(new MissionControlAckEvent
        {
            RequestId  = intent.RequestId,
            ErrorCode  = 0,
            NewVersion = 4
        });

        var result = await commitTask;

        Assert.True(result.Success);
        Assert.Equal(4, result.NewVersion);
        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task CommitMissionAsync_FailureAck_ReturnsFailure()
    {
        var (svc, bus, _) = CreateSut();
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };

        var commitTask = svc.CommitMissionAsync(entityId: 10, newPlan: plan, baseVersion: 1);
        var intent     = DrainIntent(bus);

        svc.OnAckReceived(new MissionControlAckEvent
        {
            RequestId  = intent.RequestId,
            ErrorCode  = 7,           // ERR_VERSION_CONFLICT
            NewVersion = 0
        });

        var result = await commitTask;

        Assert.False(result.Success);
        Assert.Equal("ERR_VERSION_CONFLICT", result.ErrorMessage);
    }

    [Fact]
    public async Task CommitMissionAsync_SendsCorrectEntityIdAndBaseVersion()
    {
        var (svc, bus, _) = CreateSut();
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };

        var commitTask = svc.CommitMissionAsync(entityId: 42, newPlan: plan, baseVersion: 99);
        var intent     = DrainIntent(bus);

        // Complete to avoid hanging.
        svc.OnAckReceived(new MissionControlAckEvent { RequestId = intent.RequestId });
        await commitTask;

        Assert.Equal(42L, intent.TargetEntityId);
        Assert.Equal(99L, intent.BaseVersion);
        Assert.Equal(eMissionCommandType.CMD_REPLACE_MISSION, intent.Payload._d);
    }

    // ── CommitMissionAsync – timeout ──────────────────────────────────────────

    [Fact]
    public async Task CommitMissionAsync_Timeout_ReturnsFailureWithoutThrowing()
    {
        // Use a very short timeout so the test stays fast.
        var (svc, _, _) = CreateSut(timeoutMs: 50);
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };

        // No ACK is delivered → should time out.
        var result = await svc.CommitMissionAsync(entityId: 5, newPlan: plan, baseVersion: 0);

        Assert.False(result.Success);
        Assert.Equal("Timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task CommitMissionAsync_Timeout_NoPendingRequestLeaked()
    {
        // After a timeout the internal TCS should be cleaned up so a late ACK
        // for the same ID does not resurrect the result.
        var (svc, bus, _) = CreateSut(timeoutMs: 50);
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };

        var result = await svc.CommitMissionAsync(entityId: 5, newPlan: plan, baseVersion: 0);
        Assert.False(result.Success); // Timed out.

        // Late ACK for the timed-out request must not throw.
        var intent = DrainIntent(bus);
        var ex = Record.Exception(() =>
            svc.OnAckReceived(new MissionControlAckEvent { RequestId = intent.RequestId, ErrorCode = 0, NewVersion = 1 }));

        Assert.Null(ex); // No exception expected.
    }

    // ── OnAckReceived – unknown ID ────────────────────────────────────────────

    [Fact]
    public void OnAckReceived_UnknownRequestId_DoesNotThrow()
    {
        var (svc, _, _) = CreateSut();

        var ex = Record.Exception(() =>
            svc.OnAckReceived(new MissionControlAckEvent { RequestId = Guid.NewGuid() }));

        Assert.Null(ex);
    }

    // ── SendControlCommand ────────────────────────────────────────────────────

    [Fact]
    public void SendControlCommand_WritesCorrectCommandType()
    {
        var (svc, bus, _) = CreateSut();
        var taskId = Guid.NewGuid();

        svc.SendControlCommand(entityId: 7, eMissionCommandType.CMD_ABORT_ALL, taskId);

        bus.SwapBuffers();
        var intent = bus.ConsumeManaged<MissionControlIntent>().Single();
        Assert.Equal(7L, intent.TargetEntityId);
        Assert.Equal(eMissionCommandType.CMD_ABORT_ALL, intent.Payload._d);
    }
}
