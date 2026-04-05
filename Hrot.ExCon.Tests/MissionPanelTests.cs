using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Moq;

namespace Hrot.ExCon.Tests;

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

    private static (MissionPanel Panel, Mock<IMissionEditorService> MissionSvc, Mock<IMapPickService> PickSvc)
        CreateSut(int selectedEntityId = 0)
    {
        var missionSvc = new Mock<IMissionEditorService>();
        var pickSvc    = new Mock<IMapPickService>();

        var panel = new MissionPanel { SelectedEntityId = selectedEntityId };
        return (panel, missionSvc, pickSvc);
    }

    private static MissionPlan BuildPlan(params string[] behaviorIds)
    {
        var tasks = new List<MissionTask>(behaviorIds.Length);
        for (int i = 0; i < behaviorIds.Length; i++)
        {
            tasks.Add(new MissionTask
            {
                TaskId         = Guid.NewGuid(),
                ExecutingEngine = string.Empty,
                BehaviorId     = behaviorIds[i],
                BehaviorParams = string.Empty,
                Triggers       = new List<MissionTrigger>(),
                State          = eTaskState.TASK_PLANNED
            });
        }

        return new MissionPlan
        {
            ActiveTaskId = Guid.Empty,
            Tasks        = tasks
        };
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
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 5);
        missionSvc.Setup(s => s.SendControlCommandAsync(
            It.IsAny<long>(), It.IsAny<eMissionCommandType>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MissionCommitResult(true, 1));

        panel.HandleJump(missionSvc.Object);

        missionSvc.Verify(s => s.SendControlCommandAsync(
            5L,
            eMissionCommandType.CMD_JUMP_TO_TASK,
            Guid.Empty),
            Times.Once);
    }

    [Fact]
    public void HandleJump_NoSelection_DoesNotCallService()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 0);

        panel.HandleJump(missionSvc.Object);

        missionSvc.Verify(s => s.SendControlCommandAsync(
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
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 7);
        missionSvc.Setup(s => s.SendControlCommandAsync(
            It.IsAny<long>(), It.IsAny<eMissionCommandType>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MissionCommitResult(true, 1));

        panel.HandleAbort(missionSvc.Object);

        missionSvc.Verify(s => s.SendControlCommandAsync(
            7L,
            eMissionCommandType.CMD_ABORT_ALL,
            Guid.Empty),
            Times.Once);
    }

    [Fact]
    public void HandleAbort_NoSelection_DoesNotCallService()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 0);

        panel.HandleAbort(missionSvc.Object);

        missionSvc.Verify(s => s.SendControlCommandAsync(
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
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 3);
        missionSvc.Setup(s => s.SendControlCommandAsync(
            It.IsAny<long>(), It.IsAny<eMissionCommandType>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MissionCommitResult(true, 1));

        panel.HandleJump(missionSvc.Object);

        missionSvc.Verify(s => s.SendControlCommandAsync(
            It.IsAny<long>(),
            eMissionCommandType.CMD_ABORT_ALL,
            It.IsAny<Guid>()),
            Times.Never);
        missionSvc.Verify(s => s.SendControlCommandAsync(
            It.IsAny<long>(),
            eMissionCommandType.CMD_JUMP_TO_TASK,
            It.IsAny<Guid>()),
            Times.Once);
    }

    [Fact]
    public void HandleAbort_SendsAbortAll_NotJumpToTask()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 3);
        missionSvc.Setup(s => s.SendControlCommandAsync(
            It.IsAny<long>(), It.IsAny<eMissionCommandType>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MissionCommitResult(true, 1));

        panel.HandleAbort(missionSvc.Object);

        missionSvc.Verify(s => s.SendControlCommandAsync(
            It.IsAny<long>(),
            eMissionCommandType.CMD_JUMP_TO_TASK,
            It.IsAny<Guid>()),
            Times.Never);
        missionSvc.Verify(s => s.SendControlCommandAsync(
            It.IsAny<long>(),
            eMissionCommandType.CMD_ABORT_ALL,
            It.IsAny<Guid>()),
            Times.Once);
    }

    // ── HandleConflictResult / HasConflictAlert / DismissConflict (ExCon.10.3) ──

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
    public void HandleConflictResult_FailureResult_SetsHasConflictAlertTrue()
    {
        var panel  = new MissionPanel();
        var result = new MissionCommitResult(false, 0, PanelConstants.VersionConflictErrorMessage);

        panel.HandleConflictResult(result);

        Assert.True(panel.HasConflictAlert);
    }

    [Fact]
    public void HandleConflictResult_FailureResult_StoresErrorMessage()
    {
        var panel  = new MissionPanel();
        var result = new MissionCommitResult(false, 0, PanelConstants.VersionConflictErrorMessage);

        panel.HandleConflictResult(result);

        Assert.Equal(PanelConstants.VersionConflictErrorMessage, panel.ConflictMessage);
    }

    [Fact]
    public void HandleConflictResult_SuccessResult_DoesNotSetAlert()
    {
        var panel  = new MissionPanel();
        var result = new MissionCommitResult(true, 2);

        panel.HandleConflictResult(result);

        Assert.False(panel.HasConflictAlert);
    }

    [Fact]
    public void HandleConflictResult_AnyFailure_SetsConflictAlert()
    {
        // Any non-success result (regardless of message) triggers the conflict alert.
        var panel  = new MissionPanel();
        var result = new MissionCommitResult(false, 0, "Some other error");

        panel.HandleConflictResult(result);

        Assert.True(panel.HasConflictAlert);
    }

    [Fact]
    public void HandleConflictResult_NullResult_Throws()
    {
        var panel = new MissionPanel();
        Assert.Throws<ArgumentNullException>(() => panel.HandleConflictResult(null!));
    }

    [Fact]
    public void HandleConflictResult_FailureWithNullMessage_FallsBackToConstant()
    {
        var panel  = new MissionPanel();
        var result = new MissionCommitResult(false, 0, null);

        panel.HandleConflictResult(result);

        Assert.True(panel.HasConflictAlert);
        Assert.Equal(PanelConstants.VersionConflictErrorMessage, panel.ConflictMessage);
    }

    [Fact]
    public void DismissConflict_ClearsAlertAndMessage()
    {
        var panel  = new MissionPanel();
        panel.HandleConflictResult(new MissionCommitResult(false, 0, PanelConstants.VersionConflictErrorMessage));
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

    // ── Task-list editing ───────────────────────────────────────────────────

    [Fact]
    public void AddTask_AppendsToDraftPlan()
    {
        var panel = new MissionPanel { SelectedEntityId = 1 };

        panel.HandleAddTask();

        var plan = panel.DraftPlan;
        Assert.NotNull(plan);
        Assert.NotNull(plan!.Value.Tasks);
        Assert.Single(plan.Value.Tasks!);
    }

    [Fact]
    public void DeleteTask_RemovesFromDraftPlan()
    {
        var panel = new MissionPanel { SelectedEntityId = 1 };
        panel.SetDraftPlan(BuildPlan("A", "B", "C"), baseVersion: 0);

        panel.HandleDeleteTask(1);

        var tasks = panel.DraftPlan!.Value.Tasks!;
        Assert.Equal(2, tasks.Count);
        Assert.Equal("A", tasks[0].BehaviorId);
        Assert.Equal("C", tasks[1].BehaviorId);
    }

    [Fact]
    public void ReorderTask_ChangesPosition()
    {
        var panel = new MissionPanel { SelectedEntityId = 1 };
        panel.SetDraftPlan(BuildPlan("A", "B"), baseVersion: 0);

        panel.HandleMoveTask(0, 1);

        var tasks = panel.DraftPlan!.Value.Tasks!;
        Assert.Equal("B", tasks[0].BehaviorId);
        Assert.Equal("A", tasks[1].BehaviorId);
    }

    // ── Behavior editing ────────────────────────────────────────────────────

    [Fact]
    public void EditBehaviorId_UpdatesDraftTask()
    {
        var panel = new MissionPanel { SelectedEntityId = 1 };
        panel.SetDraftPlan(BuildPlan(""), baseVersion: 0);

        panel.HandleEditBehaviorId(0, "MoveToLocation");

        var task = panel.DraftPlan!.Value.Tasks![0];
        Assert.Equal("MoveToLocation", task.BehaviorId);
    }

    [Fact]
    public void EditBehaviorParams_UpdatesDraftTask()
    {
        var panel = new MissionPanel { SelectedEntityId = 1 };
        panel.SetDraftPlan(BuildPlan("MoveToLocation"), baseVersion: 0);

        panel.HandleEditBehaviorParams(0, "{\"speed\":15}");

        var task = panel.DraftPlan!.Value.Tasks![0];
        Assert.Equal("{\"speed\":15}", task.BehaviorParams);
    }

    // ── Commit ─────────────────────────────────────────────────────────────

    [Fact]
    public void Commit_CallsMissionEditorService()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 7);
        panel.SetDraftPlan(BuildPlan("MoveToLocation"), baseVersion: 2);

        missionSvc
            .Setup(s => s.CommitMissionAsync(7, It.IsAny<MissionPlan>(), 2))
            .ReturnsAsync(new MissionCommitResult(true, 3));

        panel.HandleCommit(missionSvc.Object);

        missionSvc.Verify(s => s.CommitMissionAsync(
            7,
            It.Is<MissionPlan>(p => p.Tasks != null && p.Tasks.Count == 1),
            2),
            Times.Once);
    }

    [Fact]
    public void Commit_DisabledWhileInFlight()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 7);
        panel.SetDraftPlan(BuildPlan("MoveToLocation"), baseVersion: 2);

        var tcs = new TaskCompletionSource<MissionCommitResult>();
        missionSvc
            .Setup(s => s.CommitMissionAsync(7, It.IsAny<MissionPlan>(), 2))
            .Returns(tcs.Task);

        panel.HandleCommit(missionSvc.Object);

        Assert.True(panel.CommitInFlight);
        Assert.False(panel.CommitButtonEnabled);
    }

    // ── BUG1-M001: DoctrineFinished default trigger ───────────────────────────

    [Fact]
    public void AddTask_NewTask_HasDoctrineFinishedTrigger()
    {
        // Each newly-added task must carry exactly one default trigger of type "DoctrineFinished"
        var panel = new MissionPanel { SelectedEntityId = 1 };

        panel.HandleAddTask();

        var task = panel.DraftPlan!.Value.Tasks![0];
        Assert.NotNull(task.Triggers);
        Assert.Single(task.Triggers!);
        Assert.Equal("DoctrineFinished", task.Triggers![0].Type);
    }

    [Fact]
    public void AddTask_MultipleTasksEach_HaveDoctrineFinishedTrigger()
    {
        var panel = new MissionPanel { SelectedEntityId = 1 };

        panel.HandleAddTask();
        panel.HandleAddTask();
        panel.HandleAddTask();

        foreach (var task in panel.DraftPlan!.Value.Tasks!)
        {
            Assert.NotNull(task.Triggers);
            Assert.Single(task.Triggers!);
            Assert.Equal("DoctrineFinished", task.Triggers![0].Type);
        }
    }

    // ── BUG1-M002: HandleAbort/Jump set CommitInFlight ───────────────────────

    [Fact]
    public void HandleJump_WithSelection_SetsCommitInFlight()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 5);
        missionSvc.Setup(s => s.SendControlCommandAsync(
            It.IsAny<long>(), It.IsAny<eMissionCommandType>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MissionCommitResult(true, 1));

        panel.HandleJump(missionSvc.Object);

        Assert.True(panel.CommitInFlight);
    }

    [Fact]
    public void HandleAbort_WithSelection_SetsCommitInFlight()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 5);
        missionSvc.Setup(s => s.SendControlCommandAsync(
            It.IsAny<long>(), It.IsAny<eMissionCommandType>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MissionCommitResult(true, 1));

        panel.HandleAbort(missionSvc.Object);

        Assert.True(panel.CommitInFlight);
    }

    // ── BUG2-M002 – Trigger selection UI handlers ─────────────────────────────

    private static MissionPanel CreatePanelWithDraftTask(string initialTriggerType = "DoctrineFinished")
    {
        var panel = new MissionPanel { SelectedEntityId = 1 };
        var plan  = BuildPlan("MoveToLocation");
        plan.Tasks![0] = plan.Tasks[0] with
        {
            Triggers = new List<MissionTrigger>
            {
                new MissionTrigger { Type = initialTriggerType, Params = MissionPanel.GetDefaultTriggerParams(initialTriggerType) }
            }
        };
        panel.SetDraftPlan(plan, baseVersion: 0);
        return panel;
    }

    [Theory]
    [InlineData("DoctrineFinished",   "")]
    [InlineData("TimerElapsed",       "10.0")]
    [InlineData("ReachedDestination", "")]
    [InlineData("HealthCritical",     "0.25")]
    [InlineData("UnderAttack",        "")]
    public void GetDefaultTriggerParams_KnownTypes_ReturnExpectedDefaults(string type, string expected)
    {
        Assert.Equal(expected, MissionPanel.GetDefaultTriggerParams(type));
    }

    [Fact]
    public void HandleEditTriggerType_UpdatesTriggerTypeAndResetsParams()
    {
        var panel = CreatePanelWithDraftTask("DoctrineFinished");

        panel.HandleEditTriggerType(0, 0, "TimerElapsed");

        var trigger = panel.DraftPlan!.Value.Tasks![0].Triggers![0];
        Assert.Equal("TimerElapsed", trigger.Type);
        Assert.Equal("10.0", trigger.Params); // default params for TimerElapsed
    }

    [Fact]
    public void HandleEditTriggerParams_UpdatesParams()
    {
        var panel = CreatePanelWithDraftTask("TimerElapsed");

        panel.HandleEditTriggerParams(0, 0, "30.0");

        var trigger = panel.DraftPlan!.Value.Tasks![0].Triggers![0];
        Assert.Equal("30.0", trigger.Params);
    }

    [Fact]
    public void HandleAddTrigger_AppendsTriggerWithDefaultParams()
    {
        var panel = new MissionPanel { SelectedEntityId = 1 };
        var plan  = BuildPlan("MoveToLocation");
        plan.Tasks![0] = plan.Tasks[0] with { Triggers = new List<MissionTrigger>() };
        panel.SetDraftPlan(plan, baseVersion: 0);

        panel.HandleAddTrigger(0, "DoctrineFinished");

        var triggers = panel.DraftPlan!.Value.Tasks![0].Triggers!;
        Assert.Single(triggers);
        Assert.Equal("DoctrineFinished", triggers[0].Type);
        Assert.Equal("", triggers[0].Params);
    }

    // ── BUG2-M004 – Inline version-conflict resolution ────────────────────────

    [Fact]
    public void HandleForceCommit_CallsCommitWithBaseVersionZero()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 3);
        panel.SetDraftPlan(BuildPlan("MoveToLocation"), baseVersion: 99);
        missionSvc.Setup(s => s.CommitMissionAsync(
                It.IsAny<long>(), It.IsAny<MissionPlan>(), It.IsAny<long>()))
            .ReturnsAsync(new MissionCommitResult(true, 100));

        panel.HandleForceCommit(missionSvc.Object);

        missionSvc.Verify(
            s => s.CommitMissionAsync(3L, It.IsAny<MissionPlan>(), 0L),
            Times.Once);
    }

    [Fact]
    public void HandleForceCommit_DismissesConflictAlert()
    {
        var (panel, missionSvc, _) = CreateSut(selectedEntityId: 2);
        panel.SetDraftPlan(BuildPlan("MoveToLocation"), baseVersion: 1);
        panel.HandleConflictResult(new MissionCommitResult(false, 0, PanelConstants.VersionConflictErrorMessage));
        Assert.True(panel.HasConflictAlert);
        missionSvc.Setup(s => s.CommitMissionAsync(
                It.IsAny<long>(), It.IsAny<MissionPlan>(), It.IsAny<long>()))
            .ReturnsAsync(new MissionCommitResult(true, 2));

        panel.HandleForceCommit(missionSvc.Object);

        Assert.False(panel.HasConflictAlert);
    }

    [Fact]
    public void DiscardDraftAndDismissConflict_ClearsConflictAndDraft()
    {
        var (panel, _, _) = CreateSut(selectedEntityId: 2);
        panel.SetDraftPlan(BuildPlan("MoveToLocation"), baseVersion: 1);
        panel.HandleConflictResult(new MissionCommitResult(false, 0, PanelConstants.VersionConflictErrorMessage));
        Assert.True(panel.HasConflictAlert);
        Assert.True(panel.DraftPlan.HasValue);

        panel.TestHook_ClearDraftAndDismissConflict();

        Assert.False(panel.HasConflictAlert);
        Assert.False(panel.DraftPlan.HasValue);
    }

    // ── BATCH-02 new tests: DrawContent calls GetAvailableBehaviors ───────────

    /// <summary>
    /// Verifies that DrawContent calls GetAvailableBehaviors with the currently
    /// selected entity ID before any ImGui rendering. This test works without an
    /// ImGui context because the service call precedes all ImGui calls.
    /// </summary>
    [Fact]
    public void DrawContent_CallsGetAvailableBehaviors_WithSelectedEntityId()
    {
        var (panel, missionSvc, pickSvc) = CreateSut(selectedEntityId: 42);
        missionSvc.Setup(s => s.GetAvailableBehaviors(42L))
            .Returns(new List<string> { "Ambush" }.AsReadOnly());

        // DrawContent calls GetAvailableBehaviors before the ImGui guard.
        panel.DrawContent(missionSvc.Object, pickSvc.Object);

        missionSvc.Verify(s => s.GetAvailableBehaviors(42L), Times.Once);
    }

    /// <summary>
    /// Verifies that DrawContent calls GetAvailableBehaviors even when there is
    /// no active mission plan (empty entity). Uses a counting mock.
    /// </summary>
    [Fact]
    public void DrawContent_NoSelection_StillCallsGetAvailableBehaviors()
    {
        var (panel, missionSvc, pickSvc) = CreateSut(selectedEntityId: 0);
        missionSvc.Setup(s => s.GetAvailableBehaviors(0L))
            .Returns(Array.Empty<string>());

        panel.DrawContent(missionSvc.Object, pickSvc.Object);

        // Even with no selection the service is called to get behaviors for entity 0.
        missionSvc.Verify(s => s.GetAvailableBehaviors(0L), Times.Once);
    }
}

