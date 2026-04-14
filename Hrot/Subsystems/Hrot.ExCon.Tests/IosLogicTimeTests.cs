using Hrot.Core.Network;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using FDP.Toolkit.DER;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost.Core.Time;
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
}
