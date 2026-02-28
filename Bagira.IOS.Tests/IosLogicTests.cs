using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using Bagira.Map.Common.Dds;
using FDP.Toolkit.DER;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Newtonsoft.Json;

namespace Bagira.IOS.Tests;

/// <summary>
/// Unit tests for <see cref="IosLogic"/>.
///
/// <para>All DDS writers/readers, the DER repo, and the service layer are
/// replaced by Moq mocks or lightweight stubs so that no live DDS participant
/// is required.</para>
///
/// <para>Test coverage:
/// <list type="bullet">
///   <item>Click processing drops events with mismatched context IDs.</item>
///   <item>Click processing drops events when no placement type is set.</item>
///   <item><see cref="IosLogic.StartPlacementMode"/> publishes the correctly
///   formatted <see cref="MapInteractionConfig"/> with the new context ID and
///   placement JSON.</item>
///   <item>Valid click events generate a tracked <see cref="CreateEntityRequest"/>.</item>
///   <item><see cref="IosLogic.Update"/> polls ingress handlers and checks timeouts.</item>
/// </list>
/// </para>
/// </summary>
public class IosLogicTests
{
    // ── Test factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="IosLogic"/> where all DDS dependencies are mocked
    /// and returns the mocks together with the two event queues so individual
    /// tests can push events and capture written messages.
    /// </summary>
    private static (
        IosLogic                                Logic,
        Mock<IDdsWriter<MapInteractionConfig>>  ConfigWriter,
        Mock<IDdsWriter<CreateEntityRequest>>   CreateEntityWriter,
        ConcurrentEventQueue<MapClickEvent>      ClickQueue,
        ConcurrentEventQueue<SelectionChangedEvent> SelectionQueue,
        Mock<IRequestTransactionManager>         TransactionMgr,
        InteractionPanel                         InteractionPanel)
        CreateSut()
    {
        var repo              = new DerRepo();
        var configWriter      = new Mock<IDdsWriter<MapInteractionConfig>>();
        var createWriter      = new Mock<IDdsWriter<CreateEntityRequest>>();
        var transactionMgr    = new Mock<IRequestTransactionManager>();
        var missionSvc        = new Mock<IMissionEditorService>();
        var contextMenuLogic  = new Mock<IContextMenuLogic>();
        var interactionPanel  = new InteractionPanel();
        var clickQueue        = new ConcurrentEventQueue<MapClickEvent>();
        var selectionQueue    = new ConcurrentEventQueue<SelectionChangedEvent>();

        var logic = new IosLogic(
            repo:                repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:    contextMenuLogic.Object,
            transactionManager:  transactionMgr.Object,
            configWriter:        configWriter.Object,
            createEntityWriter:  createWriter.Object,
            clickQueue:          clickQueue,
            selectionQueue:      selectionQueue,
            interactionPanel:    interactionPanel);

        return (logic, configWriter, createWriter, clickQueue, selectionQueue,
                transactionMgr, interactionPanel);
    }

    // ── StartPlacementMode ────────────────────────────────────────────────────

    [Fact]
    public void StartPlacementMode_SetsActiveContextId_ToNewNonEmptyGuid()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);

        Assert.NotEqual(Guid.Empty, logic.ActiveContextId);
    }

    [Fact]
    public void StartPlacementMode_SetPlacementType()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(200L, eForceIdentifier.FORCE_OPPOSING);

        Assert.Equal(200L, logic.PlacementType);
    }

    [Fact]
    public void StartPlacementMode_WritesMapInteractionConfig_WithMatchingContextId()
    {
        var (logic, configWriter, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);

        configWriter.Verify(w => w.Write(
            It.Is<MapInteractionConfig>(c =>
                c.ActiveContextId == logic.ActiveContextId)),
            Times.Once);
    }

    [Fact]
    public void StartPlacementMode_WritesMapInteractionConfig_ContainsPlacementToolName()
    {
        var (logic, configWriter, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);

        configWriter.Verify(w => w.Write(
            It.Is<MapInteractionConfig>(c =>
                c.ConfigurationJson.Contains("PLACEMENT"))),
            Times.Once);
    }

    [Fact]
    public void StartPlacementMode_WritesMapInteractionConfig_ContainsTkbType()
    {
        var (logic, configWriter, _, _, _, _, _) = CreateSut();
        const long tkbType = 999L;

        logic.StartPlacementMode(tkbType, eForceIdentifier.FORCE_FRIENDLY);

        configWriter.Verify(w => w.Write(
            It.Is<MapInteractionConfig>(c =>
                c.ConfigurationJson.Contains(tkbType.ToString()))),
            Times.Once);
    }

    [Fact]
    public void StartPlacementMode_CalledTwice_GeneratesDifferentContextIds()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);
        var first = logic.ActiveContextId;

        logic.StartPlacementMode(101L, eForceIdentifier.FORCE_FRIENDLY);
        var second = logic.ActiveContextId;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void PlacementFlow_EmitsTraceSequence()
    {
        using var logScope = new LogCaptureScope();
        var (logic, _, _, clickQueue, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);

        clickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = logic.ActiveContextId,
            Position = new GeoPosition { Latitude = 45.0, Longitude = 12.0 }
        });

        logic.Update();
        LogManager.Flush();

        var expected = new[]
        {
            "[TRACE-IOS] Placement Mode ON.",
            "[TRACE-IOS] MapClickEvent ContextId=",
        };

        Assert.True(
            ContainsInOrder(logScope.Target.Logs, expected),
            BuildFailureMessage(logScope.Target.Logs, expected));
    }

    // ── Click processing – drops ──────────────────────────────────────────────

    [Fact]
    public void Update_Click_MismatchedContextId_DropsEvent_NoCreateRequest()
    {
        var (logic, _, createWriter, clickQueue, _, _, _) = CreateSut();

        // Activate placement with one context ID
        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);

        // Push a click with a DIFFERENT context ID (stale click)
        clickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = Guid.NewGuid(),   // <── different
            Position             = new GeoPosition { Latitude = 45.0, Longitude = 12.0 }
        });

        logic.Update();

        createWriter.Verify(w => w.Write(It.IsAny<CreateEntityRequest>()), Times.Never);
    }

    [Fact]
    public void Update_Click_EmptyContextId_WhenNoPlacementActive_DropsEvent()
    {
        var (logic, _, createWriter, clickQueue, _, _, _) = CreateSut();

        // No StartPlacementMode called → ActiveContextId == Guid.Empty
        clickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = Guid.NewGuid(),   // anything non-empty
            Position             = new GeoPosition()
        });

        logic.Update();

        createWriter.Verify(w => w.Write(It.IsAny<CreateEntityRequest>()), Times.Never);
    }

    [Fact]
    public void Update_Click_PlacementTypeZero_DropsEvent()
    {
        // Arrange: manually set an active context ID but leave PlacementType = 0
        // by bypassing StartPlacementMode.  We can do this by using a second logic
        // instance that has placement activated then resetting the context via
        // a fresh queue entry with a matching context but no type.
        //
        // Simpler approach: use a click with matching empty context when
        // ActiveContextId is Guid.Empty and context is also Guid.Empty.
        var (logic, _, createWriter, clickQueue, _, _, _) = CreateSut();

        // ActiveContextId = Guid.Empty, PlacementType = 0.
        // Enqueue click with matching (empty) context ID but no placement mode.
        clickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = Guid.Empty,
            Position             = new GeoPosition()
        });

        logic.Update();

        // Even though the context matched (both empty), PlacementType=0 → drop.
        createWriter.Verify(w => w.Write(It.IsAny<CreateEntityRequest>()), Times.Never);
    }

    // ── Click processing – valid ──────────────────────────────────────────────

    [Fact]
    public void Update_ValidClick_WritesCreateEntityRequest()
    {
        var (logic, _, createWriter, clickQueue, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);

        clickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = logic.ActiveContextId,
            Position             = new GeoPosition { Latitude = 45.0, Longitude = 12.0 }
        });

        logic.Update();

        createWriter.Verify(w => w.Write(It.IsAny<CreateEntityRequest>()), Times.Once);
    }

    [Fact]
    public void Update_ValidClick_TracksRequestWithTransactionManager()
    {
        var (logic, _, _, clickQueue, _, transactionMgr, _) = CreateSut();

        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);

        clickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = logic.ActiveContextId,
            Position             = new GeoPosition()
        });

        logic.Update();

        transactionMgr.Verify(
            m => m.TrackRequest(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public void Update_ValidClick_CreateRequestContainsCorrectTkbType()
    {
        var (logic, _, createWriter, clickQueue, _, _, _) = CreateSut();
        const long tkbType = 777L;

        logic.StartPlacementMode(tkbType, eForceIdentifier.FORCE_FRIENDLY);

        clickQueue.Enqueue(new MapClickEvent
        {
            InteractionContextId = logic.ActiveContextId,
            Position             = new GeoPosition { Latitude = 10.0, Longitude = 20.0 }
        });

        logic.Update();

        createWriter.Verify(w => w.Write(
            It.Is<CreateEntityRequest>(r =>
                r.InitialDescriptors.Any(d =>
                    d._d == EDescriptorType.dtEntityMaster &&
                    d.EntityMaster.TkbType == tkbType))),
            Times.Once);
    }

    // ── Update – timeout check ────────────────────────────────────────────────

    [Fact]
    public void Update_CallsCheckTimeouts()
    {
        var (logic, _, _, _, _, transactionMgr, _) = CreateSut();

        logic.Update();

        transactionMgr.Verify(m => m.CheckTimeouts(), Times.Once);
    }

    // ── SelectEntity ──────────────────────────────────────────────────────────

    [Fact]
    public void SelectEntity_SetsSelectedEntityId()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.SelectEntity(42);

        Assert.Equal(42, logic.SelectedEntityId);
    }

    // ── OpenSpawner ───────────────────────────────────────────────────────────

    [Fact]
    public void OpenSpawner_SetsSpawnerRequested()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.OpenSpawner();

        Assert.True(logic.SpawnerRequested);
    }

    [Fact]
    public void ConsumeSpawnerRequest_ClearsFlag()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.OpenSpawner();
        logic.ConsumeSpawnerRequest();

        Assert.False(logic.SpawnerRequested);
    }

    // ── SendConfigPatch ───────────────────────────────────────────────────────

    [Fact]
    public void SendConfigPatch_WritesMapInteractionConfig_WithSuppliedJson()
    {
        var (logic, configWriter, _, _, _, _, _) = CreateSut();
        const string patch = @"{""view"":{""layers"":{""satellite"":true}}}";

        logic.SendConfigPatch(patch);

        configWriter.Verify(w => w.Write(
            It.Is<MapInteractionConfig>(c => c.ConfigurationJson == patch)),
            Times.Once);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();
        logic.Dispose();
        var ex = Record.Exception(() => logic.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Update_AfterDispose_ThrowsObjectDisposedException()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();
        logic.Dispose();
        Assert.Throws<ObjectDisposedException>(() => logic.Update());
    }

    // ── Interaction-log entries (DEBT-034 drain path) ─────────────────────────

    [Fact]
    public void Update_AfterStartPlacementMode_DrainedLogContainsEntry()
    {
        var (logic, _, _, _, _, _, interactionPanel) = CreateSut();

        logic.StartPlacementMode(100L, eForceIdentifier.FORCE_FRIENDLY);
        logic.Update(); // drains pending logs

        Assert.True(interactionPanel.EntryCount > 0);
    }

    private static bool ContainsInOrder(IList<string> logs, IReadOnlyList<string> expected)
    {
        int index = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            string needle = expected[i];
            while (index < logs.Count && !logs[index].Contains(needle, StringComparison.Ordinal))
                index++;

            if (index >= logs.Count)
                return false;

            index++;
        }

        return true;
    }

    private static string BuildFailureMessage(IList<string> logs, IReadOnlyList<string> expected)
    {
        var message = string.Join("\n", logs);
        return "Missing expected trace sequence.\n" +
               "Expected fragments:\n" + string.Join("\n", expected) +
               "\nCaptured logs:\n" + message;
    }

    private sealed class LogCaptureScope : IDisposable
    {
        private readonly LoggingConfiguration? _originalConfig;
        public MemoryTarget Target { get; }

        public LogCaptureScope()
        {
            _originalConfig = LogManager.Configuration;
            Target = new MemoryTarget("traceCapture") { Layout = "${message}" };
            var config = new LoggingConfiguration();
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, Target);
            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }

        public void Dispose()
        {
            LogManager.Flush();
            LogManager.Configuration = _originalConfig;
            LogManager.ReconfigExistingLoggers();
        }
    }
}
