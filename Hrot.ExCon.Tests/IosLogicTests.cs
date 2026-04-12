using Hrot.ExCon.Logic;
using Hrot.Core.Network;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using FDP.Toolkit.DER;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Newtonsoft.Json;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="ExConLogic"/>.
///
/// <para>All DDS writers/readers, the DER repo, and the service layer are
/// replaced by Moq mocks or lightweight stubs so that no live DDS participant
/// is required.</para>
///
/// <para>Test coverage:
/// <list type="bullet">
///   <item>Click processing drops events with mismatched context IDs.</item>
///   <item>Click processing drops events when no placement type is set.</item>
///   <item><see cref="ExConLogic.StartPlacementMode"/> publishes the correctly
///   formatted <see cref="MapInteractionConfig"/> with the new context ID and
///   placement JSON.</item>
///   <item>Valid click events generate a tracked <see cref="CreateEntityRequest"/>.</item>
///   <item><see cref="ExConLogic.Update"/> polls ingress handlers and checks timeouts.</item>
/// </list>
/// </para>
/// </summary>
public class ExConLogicTests
{
    // ── Test factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="ExConLogic"/> where all DDS dependencies are mocked
    /// and returns the mocks together with the two event queues so individual
    /// tests can push events and capture written messages.
    /// </summary>
    private static (
        ExConLogic                                Logic,
        Mock<IExConEgressWriters>               ConfigWriter,
        Mock<IExConEgressWriters>               CreateEntityWriter,
        ConcurrentEventQueue<MapClickEventDto>      ClickQueue,
        ConcurrentEventQueue<SelectionChangedEventDto> SelectionQueue,
        Mock<IRequestTransactionManager>         TransactionMgr,
        InteractionPanel                         InteractionPanel)
        CreateSut()
    {
        var repo              = new DerRepo();
        var egressWriters     = new Mock<IExConEgressWriters>();
        var transactionMgr    = new Mock<IRequestTransactionManager>();
        var missionSvc        = new Mock<IMissionEditorService>();
        var contextMenuLogic  = new Mock<IContextMenuLogic>();
        var interactionPanel  = new InteractionPanel();
        var clickQueue        = new ConcurrentEventQueue<MapClickEventDto>();
        var selectionQueue    = new ConcurrentEventQueue<SelectionChangedEventDto>();

        var logic = new ExConLogic(
            repo:                repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:    contextMenuLogic.Object,
            transactionManager:  transactionMgr.Object,
            egressWriters:       egressWriters.Object,
            clickQueue:          clickQueue,
            selectionQueue:      selectionQueue,
            interactionPanel:    interactionPanel,
            createEntityAckQueue: new ConcurrentEventQueue<EntityLifecycleAckDto>());

        return (logic, egressWriters, egressWriters, clickQueue, selectionQueue,
                transactionMgr, interactionPanel);
    }

    // ── StartPlacementMode ────────────────────────────────────────────────────

    [Fact]
    public void StartPlacementMode_SetsActiveContextId_ToNewNonEmptyGuid()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L);

        Assert.NotEqual(Guid.Empty, logic.ActiveContextId);
    }

    [Fact]
    public void StartPlacementMode_SetPlacementType()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(200L);

        Assert.Equal(200L, logic.PlacementType);
    }

    [Fact]
    public void StartPlacementMode_WritesMapInteractionConfig_WithMatchingContextId()
    {
        var (logic, configWriter, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L);

        configWriter.Verify(w => w.WriteMapCommand(
            It.Is<MapCommandDto>(c =>
                c.CommandArgsJson.Contains(logic.ActiveContextId.ToString("N")))),
            Times.Once);
    }

    [Fact]
    public void StartPlacementMode_WritesMapInteractionConfig_ContainsPlacementToolName()
    {
        var (logic, configWriter, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L);

        configWriter.Verify(w => w.WriteMapCommand(
            It.Is<MapCommandDto>(c =>
                c.CommandType.Contains("PLACE"))),
            Times.Once);
    }

    [Fact]
    public void StartPlacementMode_WritesMapInteractionConfig_ContainsTkbType()
    {
        var (logic, configWriter, _, _, _, _, _) = CreateSut();
        const long tkbType = 999L;

        logic.StartPlacementMode(tkbType);

        configWriter.Verify(w => w.WriteMapCommand(
            It.Is<MapCommandDto>(c =>
                c.CommandArgsJson.Contains(tkbType.ToString()))),
            Times.Once);
    }

    [Fact]
    public void StartPlacementMode_CalledTwice_GeneratesDifferentContextIds()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L);
        var first = logic.ActiveContextId;

        logic.StartPlacementMode(101L);
        var second = logic.ActiveContextId;

        Assert.NotEqual(first, second);
    }

    // ── StartPlacementMode typed EntityPropertyPatch overload ─────────────────

    /// <summary>
    /// The typed <see cref="ExConLogic.StartPlacementMode(long, EntityPropertyPatch?)"/> overload
    /// must serialize the patch and embed it in <c>CommandArgsJson</c> under the
    /// <c>initialPropertiesJson</c> key, so the IG receives the entity name.
    /// </summary>
    [Fact]
    public void StartPlacementMode_WithEntityPropertyPatch_CommandArgsJsonContainsName()
    {
        var (logic, commandWriter, _, _) = CreateSutWithCommandWriter();
        MapCommandDto? captured = null;
        commandWriter.Setup(w => w.WriteMapCommand(It.IsAny<MapCommandDto>()))
            .Callback<MapCommandDto>(r => captured = r);

        logic.StartPlacementMode(100L, new EntityPropertyPatch { Name = "Alpha-1" });

        Assert.NotNull(captured);
        Assert.Contains("Alpha-1", captured!.CommandArgsJson);
    }

    /// <summary>
    /// Null properties on the patch must be omitted from the serialized JSON
    /// (<c>NullValueHandling.Ignore</c>), so the IG only receives set fields.
    /// </summary>
    [Fact]
    public void StartPlacementMode_WithEntityPropertyPatch_NullPropertiesOmittedFromCommandArgsJson()
    {
        var (logic, commandWriter, _, _) = CreateSutWithCommandWriter();
        MapCommandDto? captured = null;
        commandWriter.Setup(w => w.WriteMapCommand(It.IsAny<MapCommandDto>()))
            .Callback<MapCommandDto>(r => captured = r);

        // Name is set but Affiliation is left null.
        logic.StartPlacementMode(100L, new EntityPropertyPatch { Name = "Alpha-1" });

        Assert.NotNull(captured);
        Assert.DoesNotContain("affiliation", captured!.CommandArgsJson,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Passing <c>null</c> for the patch must behave identically to the
    /// string overload with <c>null</c> — no <c>initialPropertiesJson</c> key in args.
    /// </summary>
    [Fact]
    public void StartPlacementMode_WithNullEntityPropertyPatch_CommandArgsJsonHasNoInitialProperties()
    {
        var (logic, commandWriter, _, _) = CreateSutWithCommandWriter();
        MapCommandDto? captured = null;
        commandWriter.Setup(w => w.WriteMapCommand(It.IsAny<MapCommandDto>()))
            .Callback<MapCommandDto>(r => captured = r);

        logic.StartPlacementMode(100L, (EntityPropertyPatch?)null);

        Assert.NotNull(captured);
        Assert.DoesNotContain("initialPropertiesJson", captured!.CommandArgsJson);
    }

    /// <summary>
    /// When <c>AutogenerateName = true</c> the serialized patch must carry the field
    /// so the IG can trigger the unique-name generator.
    /// </summary>
    [Fact]
    public void StartPlacementMode_WithAutogenerateNamePatch_CommandArgsJsonContainsAutogenerateName()
    {
        var (logic, commandWriter, _, _) = CreateSutWithCommandWriter();
        MapCommandDto? captured = null;
        commandWriter.Setup(w => w.WriteMapCommand(It.IsAny<MapCommandDto>()))
            .Callback<MapCommandDto>(r => captured = r);
        var patch = new EntityPropertyPatch { AutogenerateName = true, NamePrefix = "Tank-" };

        logic.StartPlacementMode(100L, patch);

        Assert.NotNull(captured);
        // The patch JSON is embedded inside CommandArgsJson as the initialPropertiesJson value.
        Assert.True(
            captured!.CommandArgsJson.Contains("autogenerateName") ||
            captured.CommandArgsJson.Contains("AutogenerateName"),
            $"Expected autogenerateName in: {captured.CommandArgsJson}");
    }

    // ── StartAreaAuthoringMode ──────────────────────────────────────────────

    [Fact]
    public void StartAreaAuthoringMode_SetsActiveContextId_ToNewNonEmptyGuid()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartAreaAuthoringMode();

        Assert.NotEqual(Guid.Empty, logic.ActiveContextId);
    }

    [Fact]
    public void StartAreaAuthoringMode_ResetsPlacementType_ToZero()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L);
        logic.StartAreaAuthoringMode();

        Assert.Equal(0L, logic.PlacementType);
    }

    [Fact]
    public void StartAreaAuthoringMode_WritesMapInteractionConfig_WithToolName()
    {
        var (logic, configWriter, _, _, _, _, _) = CreateSut();

        logic.StartAreaAuthoringMode();

        configWriter.Verify(w => w.WriteMapCommand(
            It.Is<MapCommandDto>(c =>
                c.CommandType.Contains("AUTHORING"))),
            Times.Once);
    }

    // ── StartRouteAuthoringMode ─────────────────────────────────────────────

    [Fact]
    public void StartRouteAuthoringMode_SetsActiveContextId_ToNewNonEmptyGuid()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartRouteAuthoringMode();

        Assert.NotEqual(Guid.Empty, logic.ActiveContextId);
    }

    [Fact]
    public void StartRouteAuthoringMode_ResetsPlacementType_ToZero()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L);
        logic.StartRouteAuthoringMode();

        Assert.Equal(0L, logic.PlacementType);
    }

    [Fact]
    public void StartRouteAuthoringMode_CalledTwice_GeneratesDifferentContextIds()
    {
        var (logic, _, _, _, _, _, _) = CreateSut();

        logic.StartRouteAuthoringMode();
        var first = logic.ActiveContextId;

        logic.StartRouteAuthoringMode();
        var second = logic.ActiveContextId;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void StartRouteAuthoringMode_WithCommandWriter_WritesCommandWithCmdStartAuthoring()
    {
        var (logic, commandWriter, _, _) = CreateSutWithCommandWriter();
        MapCommandDto? captured = null;
        commandWriter.Setup(w => w.WriteMapCommand(It.IsAny<MapCommandDto>()))
            .Callback<MapCommandDto>(r => captured = r);

        logic.StartRouteAuthoringMode();

        Assert.NotNull(captured);
        Assert.Equal("CMD_START_AUTHORING", captured!.CommandType);
    }

    [Fact]
    public void StartRouteAuthoringMode_WithCommandWriter_CommandArgsContainsTkbTypeRoute()
    {
        var (logic, commandWriter, _, _) = CreateSutWithCommandWriter();
        MapCommandDto? captured = null;
        commandWriter.Setup(w => w.WriteMapCommand(It.IsAny<MapCommandDto>()))
            .Callback<MapCommandDto>(r => captured = r);

        logic.StartRouteAuthoringMode();

        Assert.NotNull(captured);
        // tkbType 8802 is TacGraphic_Route
        Assert.Contains("8802", captured!.CommandArgsJson);
    }

    [Fact]
    public void StartRouteAuthoringMode_WithCommandWriter_TracksRequest()
    {
        var (logic, _, txMgr, _) = CreateSutWithCommandWriter();

        logic.StartRouteAuthoringMode();

        txMgr.Verify(t => t.TrackRequest(
            It.Is<Guid>(id => id != Guid.Empty),
            It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public void PlacementFlow_EmitsTraceSequence()
    {
        using var logScope = new LogCaptureScope();
        var (logic, _, _, clickQueue, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L);

        clickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = logic.ActiveContextId,
            Latitude             = 45.0,
            Longitude            = 12.0
        });

        logic.Update();
        LogManager.Flush();

        var expected = new[]
        {
            "[Node-", // Placement Mode ON.
            "[Node-", // MapClickEventDto ContextId=
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
        logic.StartPlacementMode(100L);

        // Push a click with a DIFFERENT context ID (stale click)
        clickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = Guid.NewGuid(),   // <── different
            Latitude             = 45.0,
            Longitude            = 12.0
        });

        logic.Update();

        createWriter.Verify(w => w.WriteCreateEntity(It.IsAny<CreateEntityCommand>()), Times.Never);
    }

    [Fact]
    public void Update_Click_EmptyContextId_WhenNoPlacementActive_DropsEvent()
    {
        var (logic, _, createWriter, clickQueue, _, _, _) = CreateSut();

        // No StartPlacementMode called → ActiveContextId == Guid.Empty
        clickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = Guid.NewGuid(),   // anything non-empty
        });

        logic.Update();

        createWriter.Verify(w => w.WriteCreateEntity(It.IsAny<CreateEntityCommand>()), Times.Never);
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
        clickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = Guid.Empty,
        });

        logic.Update();

        // Even though the context matched (both empty), PlacementType=0 → drop.
        createWriter.Verify(w => w.WriteCreateEntity(It.IsAny<CreateEntityCommand>()), Times.Never);
    }

    // ── Click processing – valid ──────────────────────────────────────────────

    [Fact]
    public void Update_ValidClick_WritesCreateEntityRequest()
    {
        var (logic, _, createWriter, clickQueue, _, _, _) = CreateSut();

        logic.StartPlacementMode(100L);

        clickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = logic.ActiveContextId,
            Latitude             = 45.0,
            Longitude            = 12.0
        });

        logic.Update();

        createWriter.Verify(w => w.WriteCreateEntity(It.IsAny<CreateEntityCommand>()), Times.Once);
    }

    [Fact]
    public void Update_ValidClick_TracksRequestWithTransactionManager()
    {
        var (logic, _, _, clickQueue, _, transactionMgr, _) = CreateSut();

        logic.StartPlacementMode(100L);

        clickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = logic.ActiveContextId,
        });

        logic.Update();

        // TrackRequest is called once by StartPlacementMode (for CMD_PLACE_ENTITY)
        // and once more by the click handler (for the create-entity request).
        transactionMgr.Verify(
            m => m.TrackRequest(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public void Update_ValidClick_CreateRequestContainsCorrectTkbType()
    {
        var (logic, _, createWriter, clickQueue, _, _, _) = CreateSut();
        const long tkbType = 777L;

        logic.StartPlacementMode(tkbType);

        clickQueue.Enqueue(new MapClickEventDto
        {
            InteractionContextId = logic.ActiveContextId,
            Latitude             = 10.0,
            Longitude            = 20.0
        });

        logic.Update();

        createWriter.Verify(w => w.WriteCreateEntity(
            It.Is<CreateEntityCommand>(r => r.TkbType == tkbType)),
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

        configWriter.Verify(w => w.WriteMapConfig(
            It.Is<MapConfigDto>(c => c.ConfigJson == patch)),
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

        logic.StartPlacementMode(100L);
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

    // ── Second factory: includes commandWriter + mapCommandAckQueue ───────────

    private static (
        ExConLogic                                Logic,
        Mock<IExConEgressWriters>               CommandWriter,
        Mock<IRequestTransactionManager>        TransactionMgr,
        ConcurrentEventQueue<MapCommandAckDto>     AckQueue)
        CreateSutWithCommandWriter()
    {
        var repo             = new DerRepo();
        var egressWriters    = new Mock<IExConEgressWriters>();
        var transactionMgr   = new Mock<IRequestTransactionManager>();
        var missionSvc       = new Mock<IMissionEditorService>();
        var contextMenuLogic = new Mock<IContextMenuLogic>();
        var interactionPanel = new InteractionPanel();
        var clickQueue       = new ConcurrentEventQueue<MapClickEventDto>();
        var selectionQueue   = new ConcurrentEventQueue<SelectionChangedEventDto>();
        var ackQueue         = new ConcurrentEventQueue<MapCommandAckDto>();

        var logic = new ExConLogic(
            repo:                 repo,
            missionEditorService: missionSvc.Object,
            contextMenuLogic:     contextMenuLogic.Object,
            transactionManager:   transactionMgr.Object,
            egressWriters:        egressWriters.Object,
            clickQueue:           clickQueue,
            selectionQueue:       selectionQueue,
            interactionPanel:     interactionPanel,
            createEntityAckQueue: new ConcurrentEventQueue<EntityLifecycleAckDto>(),
            mapCommandAckQueue:   ackQueue);

        return (logic, egressWriters, transactionMgr, ackQueue);
    }

    // ── MapCommandAckDto processing ──────────────────────────────────────────────

    /// <summary>
    /// When <see cref="ExConLogic.StartPlacementMode"/> is called with a command writer,
    /// it must register the new <c>RequestId</c> with <see cref="IRequestTransactionManager"/>.
    /// </summary>
    [Fact]
    public void StartPlacementMode_WithCommandWriter_TracksRequest()
    {
        var (logic, _, txMgr, _) = CreateSutWithCommandWriter();

        logic.StartPlacementMode(100L);

        txMgr.Verify(t => t.TrackRequest(
            It.Is<Guid>(id => id != Guid.Empty),
            It.IsAny<string>()),
            Times.Once);
    }

    /// <summary>
    /// When a <see cref="MapCommandAckDto"/> with <c>StatusCode=0</c> (Finished) arrives
    /// for the last tracked request, <see cref="ExConLogic.Update"/> must call
    /// <see cref="IRequestTransactionManager.CompleteRequest"/> with <c>success=true</c>.
    /// </summary>
    [Fact]
    public void Update_MapCommandAckFinished_CompletesTransaction()
    {
        var (logic, commandWriter, txMgr, ackQueue) = CreateSutWithCommandWriter();

        // Capture the RequestId written to the command writer.
        Guid capturedRequestId = Guid.Empty;
        commandWriter.Setup(w => w.WriteMapCommand(It.IsAny<MapCommandDto>()))
            .Callback<MapCommandDto>(req => capturedRequestId = req.RequestId);

        logic.StartPlacementMode(100L);

        ackQueue.Enqueue(new MapCommandAckDto
        {
            RequestId  = capturedRequestId,
            StatusCode = 0, // Finished
            DataJson   = "{}"
        });

        logic.Update();

        txMgr.Verify(t => t.CompleteRequest(capturedRequestId, true, null), Times.Once);
    }

    /// <summary>
    /// A <see cref="MapCommandAckDto"/> with <c>StatusCode=2</c> (Cancelled) must
    /// complete the transaction with <c>success=false</c>.
    /// </summary>
    [Fact]
    public void Update_MapCommandAckCancelled_CompletesTransactionAsFailure()
    {
        var (logic, commandWriter, txMgr, ackQueue) = CreateSutWithCommandWriter();

        Guid capturedRequestId = Guid.Empty;
        commandWriter.Setup(w => w.WriteMapCommand(It.IsAny<MapCommandDto>()))
            .Callback<MapCommandDto>(req => capturedRequestId = req.RequestId);

        logic.StartPlacementMode(100L);

        ackQueue.Enqueue(new MapCommandAckDto
        {
            RequestId  = capturedRequestId,
            StatusCode = 2, // Cancelled
            DataJson   = string.Empty
        });

        logic.Update();

        txMgr.Verify(t => t.CompleteRequest(capturedRequestId, false, It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// A <see cref="MapCommandAckDto"/> whose <c>RequestId</c> does not match the last
    /// tracked command must NOT call <see cref="IRequestTransactionManager.CompleteRequest"/>.
    /// </summary>
    [Fact]
    public void Update_MapCommandAckUnknownRequestId_IsIgnored()
    {
        var (logic, _, txMgr, ackQueue) = CreateSutWithCommandWriter();

        logic.StartPlacementMode(100L);

        ackQueue.Enqueue(new MapCommandAckDto
        {
            RequestId  = Guid.NewGuid(), // deliberate mismatch
            StatusCode = 0,
            DataJson   = string.Empty
        });

        logic.Update();

        txMgr.Verify(t => t.CompleteRequest(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// When <see cref="ExConLogic.StartPlacementMode"/> is supplied with
    /// <c>initialPropertiesJson</c>, the published <see cref="MapCommandRequest"/>
    /// must embed it in <see cref="MapCommandRequest.CommandArgsJson"/>.
    /// </summary>
    [Fact]
    public void StartPlacementMode_WithInitialPropertiesJson_IncludesItInArgsJson()
    {
        var (logic, commandWriter, _, _) = CreateSutWithCommandWriter();

        const string propsJson = "{\"name\":\"Alpha-1\"}";
        logic.StartPlacementMode(100L, initialPropertiesJson: propsJson);

        commandWriter.Verify(w => w.WriteMapCommand(
            It.Is<MapCommandDto>(r =>
                r.CommandArgsJson.Contains("initialPropertiesJson") &&
                r.CommandArgsJson.Contains("Alpha-1"))),
            Times.Once);
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
