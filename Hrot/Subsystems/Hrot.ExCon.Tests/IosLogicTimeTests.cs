using Hrot.Core.Network;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests;

public class ExConLogicTimeTests
{
    private static ExConLogic MakeLogic(Mock<ITimeControlGateway>? timeControlMock = null)
    {
        return new ExConLogic(
            repo:                   new DerRepo(),
            missionEditorService:   Mock.Of<IMissionEditorService>(),
            contextMenuLogic:       Mock.Of<IContextMenuLogic>(),
            transactionManager:     new RequestTransactionManager(),
            egressWriters:          Mock.Of<IExConEgressWriters>(),
            clickQueue:             new ConcurrentEventQueue<MapClickEventDto>(),
            selectionQueue:         new ConcurrentEventQueue<SelectionChangedEventDto>(),
            interactionPanel:       new InteractionPanel(),
            createEntityAckQueue:   new ConcurrentEventQueue<EntityLifecycleAckDto>(),
            timeControl:            timeControlMock?.Object);
    }

    [Fact]
    public void OnTimeMode_Deterministic_SetsIsPausedTrue()
    {
        var logic = MakeLogic();
        var dto = new SwitchTimeModeWireDto { TargetModeInt = (int)TimeMode.Deterministic };

        logic.OnTimeMode(dto);

        Assert.True(logic.IsPaused);
    }

    [Fact]
    public void OnTimeMode_Continuous_SetsIsPausedFalse()
    {
        var logic = MakeLogic();
        // First pause it
        logic.OnTimeMode(new SwitchTimeModeWireDto { TargetModeInt = (int)TimeMode.Deterministic });
        // Then resume
        logic.OnTimeMode(new SwitchTimeModeWireDto { TargetModeInt = (int)TimeMode.Continuous });

        Assert.False(logic.IsPaused);
    }

    [Fact]
    public void RequestPause_CallsTimeControlGateway()
    {
        var tcMock = new Mock<ITimeControlGateway>();
        var logic = MakeLogic(tcMock);

        logic.RequestPause();

        tcMock.Verify(t => t.RequestPause(), Times.Once);
    }

    [Fact]
    public void RequestResume_CallsTimeControlGateway()
    {
        var tcMock = new Mock<ITimeControlGateway>();
        var logic = MakeLogic(tcMock);

        logic.RequestResume();

        tcMock.Verify(t => t.RequestResume(), Times.Once);
    }

    [Fact]
    public void RequestStep_CallsTimeControlGateway()
    {
        var tcMock = new Mock<ITimeControlGateway>();
        var logic = MakeLogic(tcMock);

        logic.RequestStep();

        tcMock.Verify(t => t.RequestStep(), Times.Once);
    }

    [Fact]
    public void SetTimeScale_CallsTimeControlGateway()
    {
        var tcMock = new Mock<ITimeControlGateway>();
        var logic = MakeLogic(tcMock);

        logic.SetTimeScale(0.5f);

        tcMock.Verify(t => t.SetTimeScale(0.5f), Times.Once);
    }

    [Fact]
    public void TimeCommands_WithNoTimeControl_DoNotThrow()
    {
        var logic = MakeLogic(timeControlMock: null);

        // Should silently no-op
        logic.RequestPause();
        logic.RequestResume();
        logic.RequestStep();
        logic.SetTimeScale(1.0f);
    }

    // ── T5: the three time properties that were never written ────────────────
    //
    // MasterSimTime / MasterWallTicks / MasterTimeScale had NO assignment anywhere in the repo, so
    // they reported 0 / 0 / 1 forever while IExConLogic and docs/projects/.../Hrot.ExCon.md both
    // documented them as live readings. The values were arriving all along on the very DTO that fed
    // IsPaused; only the mode was being read off it.

    /// <summary>
    /// THE rail for TM-033: one pause message, and all four properties answer from it. Before the
    /// fix only the first assertion passed.
    /// </summary>
    [Fact]
    public void OnTimeMode_FeedsEveryTimeProperty_NotJustTheMode()
    {
        var logic = MakeLogic();

        logic.OnTimeMode(new SwitchTimeModeWireDto
        {
            TargetModeInt    = (int)TimeMode.Deterministic,
            SimTimeSnapshot  = 42.5,
            BarrierWallTicks = 123456789L,
            TimeScale        = 2.0f,
        });

        Assert.True(logic.IsPaused);
        Assert.Equal(42.5,       logic.MasterSimTime, precision: 3);
        Assert.Equal(123456789L, logic.MasterWallTicks);
        Assert.Equal(2.0f,       logic.MasterTimeScale);
    }

    /// <summary>
    /// ExCon has no clock of its own to advance between messages, so it must adopt the PAUSE
    /// snapshot too — not only the resume anchor. A console that reset to the last resume time on
    /// every pause would run backwards on screen.
    /// </summary>
    [Fact]
    public void APauseSnapshot_IsAdopted_BecauseExConHasNoClockOfItsOwn()
    {
        var logic = MakeLogic();

        logic.OnTimeMode(new SwitchTimeModeWireDto
        {
            TargetModeInt   = (int)TimeMode.Continuous,
            SimTimeSnapshot = 10.0,
        });
        logic.OnTimeMode(new SwitchTimeModeWireDto
        {
            TargetModeInt   = (int)TimeMode.Deterministic,
            SimTimeSnapshot = 25.0,
        });

        Assert.Equal(25.0, logic.MasterSimTime, precision: 3);
    }

    /// <summary>
    /// A Continuous event carries FixedDelta = 0, not a zero SCALE. Latching that as a stop would
    /// show the cluster frozen at 0x on every resume.
    /// </summary>
    [Fact]
    public void AZeroTimeScaleOnResume_DoesNotClearTheScale()
    {
        var logic = MakeLogic();

        logic.OnTimeMode(new SwitchTimeModeWireDto
        {
            TargetModeInt = (int)TimeMode.Deterministic,
            TimeScale     = 4.0f,
        });
        logic.OnTimeMode(new SwitchTimeModeWireDto
        {
            TargetModeInt = (int)TimeMode.Continuous,
            TimeScale     = 0f,
        });

        Assert.Equal(4.0f, logic.MasterTimeScale);
    }
}
