using Hrot.NED.Descriptors;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using Hrot.Map.Common.Dds;
using FDP.Toolkit.DER;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Time;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests;

public class ExConLogicTimeTests
{
    private static ExConLogic MakeLogic(Mock<IDdsWriter<ClusterOpRequest>>? sysOpWriterMock = null)
    {
        return new ExConLogic(
            repo:                   new DerRepo(),
            missionEditorService:   Mock.Of<IMissionEditorService>(),
            contextMenuLogic:       Mock.Of<IContextMenuLogic>(),
            transactionManager:     new RequestTransactionManager(),
            configWriter:           Mock.Of<IDdsWriter<MapInteractionConfig>>(),
            createEntityWriter:     Mock.Of<IDdsWriter<CreateEntityRequest>>(),
            clickQueue:             new ConcurrentEventQueue<MapClickEvent>(),
            selectionQueue:         new ConcurrentEventQueue<SelectionChangedEvent>(),
            interactionPanel:       new InteractionPanel(),
            createEntityAckQueue:   new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>(),
            sysOpWriter:            sysOpWriterMock?.Object);
    }

    [Fact]
    public void OnTimePulse_UpdatesTimeProperties()
    {
        var logic = MakeLogic();
        var pulse = new TimePulseDescriptor
        {
            SimTimeSnapshot = 42.5,
            MasterWallTicks = 12345L,
            TimeScale       = 2.0f
        };

        logic.OnTimePulse(pulse);

        Assert.Equal(42.5,   logic.MasterSimTime,   precision: 5);
        Assert.Equal(12345L, logic.MasterWallTicks);
        Assert.Equal(2.0f,   logic.MasterTimeScale);
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
    public void RequestPause_WritesClusterOpRequest()
    {
        var writerMock = new Mock<IDdsWriter<ClusterOpRequest>>();
        var logic = MakeLogic(writerMock);

        logic.RequestPause();

        writerMock.Verify(w => w.Write(It.Is<ClusterOpRequest>(r =>
            r.OperationType == ClusterOpType.PauseTime)), Times.Once);
    }

    [Fact]
    public void RequestResume_WritesClusterOpRequest()
    {
        var writerMock = new Mock<IDdsWriter<ClusterOpRequest>>();
        var logic = MakeLogic(writerMock);

        logic.RequestResume();

        writerMock.Verify(w => w.Write(It.Is<ClusterOpRequest>(r =>
            r.OperationType == ClusterOpType.ResumeTime)), Times.Once);
    }

    [Fact]
    public void RequestStep_WritesClusterOpRequest()
    {
        var writerMock = new Mock<IDdsWriter<ClusterOpRequest>>();
        var logic = MakeLogic(writerMock);

        logic.RequestStep();

        writerMock.Verify(w => w.Write(It.Is<ClusterOpRequest>(r =>
            r.OperationType == ClusterOpType.StepTime)), Times.Once);
    }

    [Fact]
    public void SetTimeScale_WritesClusterOpRequestWithPayload()
    {
        var writerMock = new Mock<IDdsWriter<ClusterOpRequest>>();
        var logic = MakeLogic(writerMock);

        logic.SetTimeScale(0.5f);

        writerMock.Verify(w => w.Write(It.Is<ClusterOpRequest>(r =>
            r.OperationType == ClusterOpType.SetTimeScale &&
            r.PayloadJson.Contains("0.5"))), Times.Once);
    }

    [Fact]
    public void TimeCommands_WithNoWriter_DoNotThrow()
    {
        var logic = MakeLogic(sysOpWriterMock: null);

        // Should silently no-op
        logic.RequestPause();
        logic.RequestResume();
        logic.RequestStep();
        logic.SetTimeScale(1.0f);
    }
}
