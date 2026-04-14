using Hrot.ExCon.Services;
using Hrot.Core.Network;
using Hrot.Core.Mission;
using FDP.Toolkit.DER;
using Moq;

namespace Hrot.ExCon.Tests;

// -- Tests --------------------------------------------------------------------

public class MissionEditorServiceTests
{
    private const int TestTimeoutMs = 200;

    private static (MissionEditorService Svc, Mock<ICommandGateway> Gateway, DerRepo Repo)
        CreateSut(int timeoutMs = TestTimeoutMs)
    {
        var repo    = new DerRepo();
        var gateway = new Mock<ICommandGateway>();
        var svc     = new MissionEditorService(repo, gateway.Object, timeoutMs);
        return (svc, gateway, repo);
    }

    // -- GetMissionSnapshot ---------------------------------------------------

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
        var entity      = repo.CreateEntity(1, 100);
        var expectedPlan = new MissionPlan { ActiveTaskId = Guid.NewGuid(), Tasks = new List<MissionTask>() };
        entity.SetDescriptor(new EntityMissionDescriptor { EntityId = 1, Plan = expectedPlan });

        var (plan, _) = svc.GetMissionSnapshot(1);

        Assert.NotNull(plan);
        Assert.Equal(expectedPlan.ActiveTaskId, plan!.ActiveTaskId);
    }

    [Fact]
    public void GetMissionSnapshot_EntityWithVersion_ReturnsCorrectVersion()
    {
        var (svc, _, repo) = CreateSut();
        var entity = repo.CreateEntity(2, 100);
        entity.SetDescriptor(new EntityMissionDescriptor { EntityId = 2, Version = 7 });

        var (_, version) = svc.GetMissionSnapshot(2);

        Assert.Equal(7, version);
    }

    // -- CommitMissionAsync ---------------------------------------------------

    [Fact]
    public async Task CommitMissionAsync_SuccessfulAck_ReturnsSuccess()
    {
        var (svc, gateway, _) = CreateSut();
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };
        gateway.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionCommitResult { Success = true, NewVersion = 4 });

        var result = await svc.CommitMissionAsync(entityId: 10, newPlan: plan, baseVersion: 3);

        Assert.True(result.Success);
        Assert.Equal(4, result.NewVersion);
        Assert.True(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task CommitMissionAsync_FailureAck_ReturnsFailure()
    {
        var (svc, gateway, _) = CreateSut();
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };
        gateway.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionCommitResult { Success = false, ErrorMessage = "ERR_VERSION_CONFLICT" });

        var result = await svc.CommitMissionAsync(entityId: 10, newPlan: plan, baseVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("ERR_VERSION_CONFLICT", result.ErrorMessage);
    }

    [Fact]
    public async Task CommitMissionAsync_SendsCorrectEntityIdAndBaseVersion()
    {
        var (svc, gateway, _) = CreateSut();
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };
        MissionControlCommand? captured = null;
        gateway.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .Callback<MissionControlCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new MissionCommitResult { Success = true });

        await svc.CommitMissionAsync(entityId: 42, newPlan: plan, baseVersion: 99);

        Assert.NotNull(captured);
        Assert.Equal(42, captured!.EntityId);
        Assert.Equal(99, captured.BaseVersion);
        Assert.Equal(eMissionCommandType.CMD_REPLACE_MISSION, captured.CommandType);
    }

    // -- CommitMissionAsync timeout -------------------------------------------

    [Fact]
    public async Task CommitMissionAsync_Timeout_ReturnsFailureWithoutThrowing()
    {
        var (svc, gateway, _) = CreateSut(timeoutMs: 50);
        var plan = new MissionPlan { Tasks = new List<MissionTask>() };
        gateway.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await svc.CommitMissionAsync(entityId: 5, newPlan: plan, baseVersion: 0);

        Assert.False(result.Success);
        Assert.Equal("Timeout", result.ErrorMessage);
    }

    // -- SendControlCommandAsync ----------------------------------------------

    [Fact]
    public async Task SendControlCommandAsync_SendsCorrectCommandType()
    {
        var (svc, gateway, _) = CreateSut();
        var taskId = Guid.NewGuid();
        MissionControlCommand? captured = null;
        gateway.Setup(g => g.SendMissionControlRequestAsync(It.IsAny<MissionControlCommand>(), It.IsAny<CancellationToken>()))
            .Callback<MissionControlCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new MissionCommitResult { Success = true });

        await svc.SendControlCommandAsync(entityId: 7, eMissionCommandType.CMD_ABORT_ALL, taskId);

        Assert.NotNull(captured);
        Assert.Equal(7, captured!.EntityId);
        Assert.Equal(eMissionCommandType.CMD_ABORT_ALL, captured.CommandType);
    }
}