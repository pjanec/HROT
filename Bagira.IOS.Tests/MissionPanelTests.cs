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

    // ── HandleConflictResult / HasConflictAlert / DismissConflict (IOS.10.3) ──

    [Fact]
    public void HasConflictAlert_InitiallyFalse()
    {
        var panel = new MissionPanel();
        Assert.False(panel.HasConflictAlert);
    }

    [Fact]
    public void ConflictMessage_InitiallyNull()
    {
        var panel = new MissionPanel();
        Assert.Null(panel.ConflictMessage);
    }

    [Fact]
    public void HandleConflictResult_VersionConflict_SetsHasConflictAlertTrue()
    {
        var panel  = new MissionPanel();
        var result = new MissionCommitResult
        {
            Success      = false,
            ErrorCode    = PanelConstants.VersionConflictErrorCode,
            ErrorMessage = PanelConstants.VersionConflictErrorMessage
        };

        panel.HandleConflictResult(result);

        Assert.True(panel.HasConflictAlert);
    }

    [Fact]
    public void HandleConflictResult_VersionConflict_StoresErrorMessage()
    {
        var panel  = new MissionPanel();
        var result = new MissionCommitResult
        {
            Success      = false,
            ErrorCode    = PanelConstants.VersionConflictErrorCode,
            ErrorMessage = PanelConstants.VersionConflictErrorMessage
        };

        panel.HandleConflictResult(result);

        Assert.Equal(PanelConstants.VersionConflictErrorMessage, panel.ConflictMessage);
    }

    [Fact]
    public void HandleConflictResult_SuccessResult_DoesNotSetAlert()
    {
        var panel  = new MissionPanel();
        var result = new MissionCommitResult
        {
            Success   = true,
            ErrorCode = 0,
            NewVersion = 2
        };

        panel.HandleConflictResult(result);

        Assert.False(panel.HasConflictAlert);
    }

    [Fact]
    public void HandleConflictResult_FailureWithOtherErrorCode_DoesNotSetConflictAlert()
    {
        // Error code != 7 (e.g. timeout, permission error) should NOT trigger
        // the version-conflict modal.
        var panel  = new MissionPanel();
        var result = new MissionCommitResult
        {
            Success      = false,
            ErrorCode    = 1,
            ErrorMessage = "Some other error"
        };

        panel.HandleConflictResult(result);

        Assert.False(panel.HasConflictAlert);
    }

    [Fact]
    public void HandleConflictResult_NullResult_Throws()
    {
        var panel = new MissionPanel();
        Assert.Throws<ArgumentNullException>(() => panel.HandleConflictResult(null!));
    }

    [Fact]
    public void HandleConflictResult_ConflictWithNullMessage_FallsBackToConstant()
    {
        var panel  = new MissionPanel();
        var result = new MissionCommitResult
        {
            Success      = false,
            ErrorCode    = PanelConstants.VersionConflictErrorCode,
            ErrorMessage = null   // null message from ACK
        };

        panel.HandleConflictResult(result);

        Assert.True(panel.HasConflictAlert);
        Assert.Equal(PanelConstants.VersionConflictErrorMessage, panel.ConflictMessage);
    }

    [Fact]
    public void DismissConflict_ClearsAlertAndMessage()
    {
        var panel  = new MissionPanel();
        panel.HandleConflictResult(new MissionCommitResult
        {
            Success   = false,
            ErrorCode = PanelConstants.VersionConflictErrorCode,
            ErrorMessage = PanelConstants.VersionConflictErrorMessage
        });
        Assert.True(panel.HasConflictAlert);

        panel.DismissConflict();

        Assert.False(panel.HasConflictAlert);
        Assert.Null(panel.ConflictMessage);
    }

    [Fact]
    public void DismissConflict_WhenNoAlertActive_IsNoOp()
    {
        var panel = new MissionPanel();

        var ex = Record.Exception(() => panel.DismissConflict());

        Assert.Null(ex);
        Assert.False(panel.HasConflictAlert);
    }
}
