using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using Moq;

namespace Bagira.IOS.Tests;

/// <summary>
/// Unit tests for <see cref="MissionPanel"/>.
///
/// Tests drive the panel through its public API
/// (<see cref="MissionPanel.HandleJump"/>,
/// <see cref="MissionPanel.HandleAbort"/>,
/// <see cref="MissionPanel.GetTaskIcon"/>,
/// <see cref="MissionPanel.SelectedEntityId"/>)
/// without requiring an active ImGui render frame.
/// </summary>
public class MissionPanelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (MissionPanel Panel, Mock<IIosLogic> Logic, Mock<IMissionEditorService> MissionSvc)
        CreateSut(int selectedEntityId = 0)
    {
        var missionSvc = new Mock<IMissionEditorService>();
        var logic      = new Mock<IIosLogic>();
        logic.Setup(l => l.MissionEditorService).Returns(missionSvc.Object);

        var panel = new MissionPanel { SelectedEntityId = selectedEntityId };
        return (panel, logic, missionSvc);
    }

    // ── SelectedEntityId ──────────────────────────────────────────────────────

    [Fact]
    public void SelectedEntityId_DefaultIsZero()
    {
        var panel = new MissionPanel();
        Assert.Equal(0, panel.SelectedEntityId);
    }

    [Fact]
    public void SelectedEntityId_SetAndGet_RoundTrips()
    {
        var panel = new MissionPanel { SelectedEntityId = 9999 };
        Assert.Equal(9999, panel.SelectedEntityId);
    }

    // ── GetTaskIcon ───────────────────────────────────────────────────────────

    [Fact]
    public void GetTaskIcon_ActiveTask_ReturnsPlayIcon()
    {
        var task = new MissionTask { State = eTaskState.TASK_PLANNED };
        Assert.Equal("▶", MissionPanel.GetTaskIcon(task, isActive: true));
    }

    [Fact]
    public void GetTaskIcon_DoneTask_NotActive_ReturnsCheckmark()
    {
        var task = new MissionTask { State = eTaskState.TASK_DONE };
        Assert.Equal("✓", MissionPanel.GetTaskIcon(task, isActive: false));
    }

    [Fact]
    public void GetTaskIcon_FailedTask_ReturnsXMark()
    {
        var task = new MissionTask { State = eTaskState.TASK_FAILED };
        Assert.Equal("✗", MissionPanel.GetTaskIcon(task, isActive: false));
    }

    [Fact]
    public void GetTaskIcon_SkippedTask_ReturnsSkipIcon()
    {
        var task = new MissionTask { State = eTaskState.TASK_SKIPPED };
        Assert.Equal("⏭", MissionPanel.GetTaskIcon(task, isActive: false));
    }

    [Fact]
    public void GetTaskIcon_PlannedTask_ReturnsStopIcon()
    {
        var task = new MissionTask { State = eTaskState.TASK_PLANNED };
        Assert.Equal("⏹", MissionPanel.GetTaskIcon(task, isActive: false));
    }

    [Fact]
    public void GetTaskIcon_ActiveOverridesState_EvenIfDone()
    {
        // isActive=true always wins, regardless of State
        var task = new MissionTask { State = eTaskState.TASK_DONE };
        Assert.Equal("▶", MissionPanel.GetTaskIcon(task, isActive: true));
    }

    // ── HandleJump ────────────────────────────────────────────────────────────

    [Fact]
    public void HandleJump_WithSelection_CallsSendControlCommandJump()
    {
        var (panel, logic, missionSvc) = CreateSut(selectedEntityId: 5);

        panel.HandleJump(logic.Object);

        missionSvc.Verify(s => s.SendControlCommand(
            5L,
            eMissionCommandType.CMD_JUMP_TO_TASK,
            Guid.Empty),
            Times.Once);
    }

    [Fact]
    public void HandleJump_NoSelection_DoesNotCallService()
    {
        var (panel, logic, missionSvc) = CreateSut(selectedEntityId: 0);

        panel.HandleJump(logic.Object);

        missionSvc.Verify(s => s.SendControlCommand(
            It.IsAny<long>(),
            It.IsAny<eMissionCommandType>(),
            It.IsAny<Guid>()),
            Times.Never);
    }

    [Fact]
    public void HandleJump_NullLogic_Throws()
    {
        var (panel, _, _) = CreateSut(selectedEntityId: 5);
        Assert.Throws<ArgumentNullException>(() => panel.HandleJump(null!));
    }

    // ── HandleAbort ───────────────────────────────────────────────────────────

    [Fact]
    public void HandleAbort_WithSelection_CallsSendControlCommandAbortAll()
    {
        var (panel, logic, missionSvc) = CreateSut(selectedEntityId: 7);

        panel.HandleAbort(logic.Object);

        missionSvc.Verify(s => s.SendControlCommand(
            7L,
            eMissionCommandType.CMD_ABORT_ALL,
            Guid.Empty),
            Times.Once);
    }

    [Fact]
    public void HandleAbort_NoSelection_DoesNotCallService()
    {
        var (panel, logic, missionSvc) = CreateSut(selectedEntityId: 0);

        panel.HandleAbort(logic.Object);

        missionSvc.Verify(s => s.SendControlCommand(
            It.IsAny<long>(),
            It.IsAny<eMissionCommandType>(),
            It.IsAny<Guid>()),
            Times.Never);
    }

    [Fact]
    public void HandleAbort_NullLogic_Throws()
    {
        var (panel, _, _) = CreateSut(selectedEntityId: 5);
        Assert.Throws<ArgumentNullException>(() => panel.HandleAbort(null!));
    }

    // ── Jump and Abort send different command types ───────────────────────────

    [Fact]
    public void HandleJump_SendsJumpToTask_NotAbortAll()
    {
        var (panel, logic, missionSvc) = CreateSut(selectedEntityId: 3);

        panel.HandleJump(logic.Object);

        missionSvc.Verify(s => s.SendControlCommand(
            It.IsAny<long>(),
            eMissionCommandType.CMD_ABORT_ALL,
            It.IsAny<Guid>()),
            Times.Never);
        missionSvc.Verify(s => s.SendControlCommand(
            It.IsAny<long>(),
            eMissionCommandType.CMD_JUMP_TO_TASK,
            It.IsAny<Guid>()),
            Times.Once);
    }

    [Fact]
    public void HandleAbort_SendsAbortAll_NotJumpToTask()
    {
        var (panel, logic, missionSvc) = CreateSut(selectedEntityId: 3);

        panel.HandleAbort(logic.Object);

        missionSvc.Verify(s => s.SendControlCommand(
            It.IsAny<long>(),
            eMissionCommandType.CMD_JUMP_TO_TASK,
            It.IsAny<Guid>()),
            Times.Never);
        missionSvc.Verify(s => s.SendControlCommand(
            It.IsAny<long>(),
            eMissionCommandType.CMD_ABORT_ALL,
            It.IsAny<Guid>()),
            Times.Once);
    }
}
