using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using FDP.Toolkit.DER;

namespace Bagira.IOS.Tests;

// ── Test infrastructure ───────────────────────────────────────────────────────

/// <summary>
/// Holds the components wired for a single IOS client in a multi-client test.
/// </summary>
internal sealed class IosClient : IDisposable
{
    public IosLogic                              Logic          { get; }
    public MissionEditorService                  MissionSvc     { get; }
    public CapturingWriter<MissionControlRequest> RequestWriter  { get; }
    public ConcurrentEventQueue<MissionControlAck> AckQueue      { get; }
    public MissionPanel                          MissionPanel   { get; }

    public IosClient(
        IosLogic logic,
        MissionEditorService missionSvc,
        CapturingWriter<MissionControlRequest> requestWriter,
        ConcurrentEventQueue<MissionControlAck> ackQueue,
        MissionPanel missionPanel)
    {
        Logic         = logic;
        MissionSvc    = missionSvc;
        RequestWriter = requestWriter;
        AckQueue      = ackQueue;
        MissionPanel  = missionPanel;
    }

    /// <summary>
    /// Delivers a successful ACK for the last written request (simulating
    /// SimHost accepting the commit).
    /// </summary>
    public void DeliverAck(long newVersion = 2)
    {
        var requestId = RequestWriter.Written.Last().RequestId;
        AckQueue.Enqueue(new MissionControlAck
        {
            RequestId  = requestId,
            ErrorCode  = 0,
            NewVersion = newVersion
        });
        MissionSvc.Poll();
    }

    /// <summary>
    /// Delivers a version-conflict rejection ACK for the last written request
    /// (simulating SimHost detecting a stale base version).
    /// </summary>
    public void DeliverVersionConflict()
    {
        var requestId = RequestWriter.Written.Last().RequestId;
        AckQueue.Enqueue(new MissionControlAck
        {
            RequestId    = requestId,
            ErrorCode    = PanelConstants.VersionConflictErrorCode,
            ErrorMessage = PanelConstants.VersionConflictErrorMessage,
            NewVersion   = 0
        });
        MissionSvc.Poll();
    }

    public void Dispose() => Logic.Dispose();
}

// ── Factory ───────────────────────────────────────────────────────────────────

internal static class MultiIosFactory
{
    private const int CommitTimeoutMs = 500;

    /// <summary>
    /// Creates <paramref name="count"/> independent IOS clients that all share
    /// the same <see cref="DerRepo"/> instance (simulating separate operator
    /// terminals connected to the same entity state).
    /// </summary>
    public static (IosClient[] Clients, DerRepo SharedRepo) CreateClients(int count)
    {
        var repo    = new DerRepo();
        var clients = new IosClient[count];

        for (int i = 0; i < count; i++)
        {
            var writer   = new CapturingWriter<MissionControlRequest>();
            var ackQueue = new ConcurrentEventQueue<MissionControlAck>();
            var msnSvc   = new MissionEditorService(
                repo,
                writer,
                commitTimeoutMs: CommitTimeoutMs,
                ackQueue:        ackQueue);

            var configWriter  = new CapturingWriter<MapInteractionConfig>();
            var createWriter  = new CapturingWriter<CreateEntityRequest>();
            var clickQueue    = new ConcurrentEventQueue<MapClickEvent>();
            var selectQueue   = new ConcurrentEventQueue<SelectionChangedEvent>();
            var txMgr         = new RequestTransactionManager();
            var ctxMenu       = new ContextMenuLogic(repo, new CapturingWriter<ContextActionsUpdate>());
            var interPanel    = new InteractionPanel();
            var missionPanel  = new MissionPanel();

            var logic = new IosLogic(
                repo:                repo,
                missionEditorService: msnSvc,
                contextMenuLogic:    ctxMenu,
                transactionManager:  txMgr,
                configWriter:        configWriter,
                createEntityWriter:  createWriter,
                clickQueue:          clickQueue,
                selectionQueue:      selectQueue,
                interactionPanel:    interPanel,
                ingressHandlers:     new[] { (IIngressHandler)msnSvc });

            clients[i] = new IosClient(logic, msnSvc, writer, ackQueue, missionPanel);
        }

        return (clients, repo);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// IOS.10.4 — Multi-IOS Synchronisation Tests
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scenario: two IOS operator terminals (<c>clientA</c>, <c>clientB</c>)
/// connect to the same entity state via a shared <see cref="DerRepo"/>.
/// Both read the same mission snapshot at version N, then issue concurrent
/// <c>CommitMissionAsync</c> requests.  The SimHost (simulated inline) accepts
/// Client A's commit and rejects Client B's with
/// <c>ERR_VERSION_CONFLICT</c>.  Tests verify that:
/// <list type="bullet">
///   <item>Client A's result is successful with an incremented version.</item>
///   <item>Client B's result is a failure with the conflict error code.</item>
///   <item>The <see cref="MissionPanel"/> correctly surfaces the conflict
///   alert to the operator after <see cref="MissionPanel.HandleConflictResult"/>.</item>
/// </list>
/// </summary>
[Collection("Integration")]
public class MultiIosIntegrationTests
{
    // ── Setup helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a shared repo with one entity at version 1 and provisions two
    /// IOS clients attached to it.
    /// </summary>
    private static (IosClient ClientA, IosClient ClientB, IDerEntity Entity, long InitialVersion)
        SetupTwoClients()
    {
        var (clients, repo) = MultiIosFactory.CreateClients(2);

        var entity = repo.CreateEntity(entityId: 1, tkbType: 100);
        entity.SetDescriptor(new EntityMaster { EntityId = 1, TkbType = 100 });
        entity.SetDescriptor(new EntityInfo   { EntityId = 1, Name    = "Alpha-1" });
        entity.SetDescriptor(new DescriptorOptimisticLock
        {
            EntityId       = 1,
            CurrentVersion = 1
        });
        entity.SetDescriptor(new EntityMission
        {
            EntityId = 1,
            Plan     = new MissionPlan { Tasks = new List<MissionTask>() }
        });

        return (clients[0], clients[1], entity, initialVersion: 1);
    }

    // ── Both clients read the same snapshot version ───────────────────────────

    [Fact]
    public void TwoClients_BothReadSameSnapshotVersion_BeforeAnyCommit()
    {
        var (clientA, clientB, _, initialVersion) = SetupTwoClients();
        using (clientA) using (clientB)
        {
            var (_, vA) = clientA.MissionSvc.GetMissionSnapshot(1);
            var (_, vB) = clientB.MissionSvc.GetMissionSnapshot(1);

            Assert.Equal(initialVersion, vA);
            Assert.Equal(initialVersion, vB);
        }
    }

    // ── Client A succeeds; Client B gets conflict ─────────────────────────────

    [Fact]
    public async Task TwoClients_ClientACommitsFirst_ClientBReceivesVersionConflict()
    {
        var (clientA, clientB, _, _) = SetupTwoClients();
        using (clientA) using (clientB)
        {
            var plan = new MissionPlan { Tasks = new List<MissionTask>() };

            var (_, vA) = clientA.MissionSvc.GetMissionSnapshot(1);
            var (_, vB) = clientB.MissionSvc.GetMissionSnapshot(1);

            // Both fire commits concurrently at the same base version.
            var taskA = clientA.MissionSvc.CommitMissionAsync(1, plan, vA);
            var taskB = clientB.MissionSvc.CommitMissionAsync(1, plan, vB);

            // SimHost: accept A, reject B.
            clientA.DeliverAck(newVersion: 2);
            clientB.DeliverVersionConflict();

            var resultA = await taskA;
            var resultB = await taskB;

            // Client A committed successfully.
            Assert.True(resultA.Success);
            Assert.Equal(2L, resultA.NewVersion);
            Assert.Equal(0, resultA.ErrorCode);

            // Client B was rejected with a version conflict.
            Assert.False(resultB.Success);
            Assert.Equal(PanelConstants.VersionConflictErrorCode, resultB.ErrorCode);
            Assert.Equal(PanelConstants.VersionConflictErrorMessage, resultB.ErrorMessage);
        }
    }

    [Fact]
    public async Task TwoClients_ClientACommitsFirst_ClientBResultHasZeroNewVersion()
    {
        var (clientA, clientB, _, _) = SetupTwoClients();
        using (clientA) using (clientB)
        {
            var plan = new MissionPlan { Tasks = new List<MissionTask>() };

            var taskA = clientA.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);
            var taskB = clientB.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);

            clientA.DeliverAck(newVersion: 2);
            clientB.DeliverVersionConflict();

            var resultB = await taskB;

            Assert.Equal(0L, resultB.NewVersion);
        }
    }

    // ── Conflict alert surfaces correctly in MissionPanel ─────────────────────

    [Fact]
    public async Task ConflictingClient_MissionPanel_HasConflictAlertAfterHandling()
    {
        var (clientA, clientB, _, _) = SetupTwoClients();
        using (clientA) using (clientB)
        {
            var plan = new MissionPlan { Tasks = new List<MissionTask>() };

            var taskA = clientA.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);
            var taskB = clientB.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);

            clientA.DeliverAck(newVersion: 2);
            clientB.DeliverVersionConflict();

            await taskA;
            var resultB = await taskB;

            clientB.MissionPanel.HandleConflictResult(resultB);

            Assert.True(clientB.MissionPanel.HasConflictAlert);
            Assert.Equal(PanelConstants.VersionConflictErrorMessage,
                         clientB.MissionPanel.ConflictMessage);
        }
    }

    [Fact]
    public async Task ConflictingClient_SuccessfulClient_MissionPanelNoAlert()
    {
        var (clientA, clientB, _, _) = SetupTwoClients();
        using (clientA) using (clientB)
        {
            var plan = new MissionPlan { Tasks = new List<MissionTask>() };

            var taskA = clientA.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);
            var taskB = clientB.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);

            clientA.DeliverAck(newVersion: 2);
            clientB.DeliverVersionConflict();

            var resultA = await taskA;

            // Client A received a success; the conflict panel should not activate.
            clientA.MissionPanel.HandleConflictResult(resultA);

            Assert.False(clientA.MissionPanel.HasConflictAlert);
        }
    }

    [Fact]
    public async Task ConflictAlert_AfterDismiss_HasConflictAlertIsFalse()
    {
        var (clientA, clientB, _, _) = SetupTwoClients();
        using (clientA) using (clientB)
        {
            var plan = new MissionPlan { Tasks = new List<MissionTask>() };

            var taskA = clientA.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);
            var taskB = clientB.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);

            clientA.DeliverAck(newVersion: 2);
            clientB.DeliverVersionConflict();

            await taskA;
            var resultB = await taskB;

            clientB.MissionPanel.HandleConflictResult(resultB);
            Assert.True(clientB.MissionPanel.HasConflictAlert);

            // Operator dismisses the modal.
            clientB.MissionPanel.DismissConflict();

            Assert.False(clientB.MissionPanel.HasConflictAlert);
        }
    }

    // ── Sequential commits (no conflict) ─────────────────────────────────────

    [Fact]
    public async Task TwoClients_SequentialCommits_BothSucceed()
    {
        var (clientA, clientB, entity, _) = SetupTwoClients();
        using (clientA) using (clientB)
        {
            var plan = new MissionPlan { Tasks = new List<MissionTask>() };

            // Client A commits at version 1 → accepted, new version 2.
            var taskA = clientA.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);
            clientA.DeliverAck(newVersion: 2);
            var resultA = await taskA;
            Assert.True(resultA.Success);

            // Simulate SimHost updating the DER entity's optimistic-lock version.
            entity.SetDescriptor(new DescriptorOptimisticLock
            {
                EntityId = 1, CurrentVersion = 2
            });

            // Client B now reads the updated version and commits at version 2.
            var (_, vB) = clientB.MissionSvc.GetMissionSnapshot(1);
            Assert.Equal(2L, vB);

            var taskB = clientB.MissionSvc.CommitMissionAsync(1, plan, baseVersion: vB);
            clientB.DeliverAck(newVersion: 3);
            var resultB = await taskB;

            Assert.True(resultB.Success);
            Assert.Equal(3L, resultB.NewVersion);
        }
    }

    // ── Dispose safety ────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoClients_DisposedDuringPendingCommit_ResolvesWithFailure()
    {
        var (clientA, clientB, _, _) = SetupTwoClients();

        var plan  = new MissionPlan { Tasks = new List<MissionTask>() };
        var taskA = clientA.MissionSvc.CommitMissionAsync(1, plan, baseVersion: 1);

        // Dispose client A before the ACK arrives.
        clientA.Dispose();

        var result = await taskA;

        // Dispose should have flushed the pending TCS with Success=false
        // (IOS-DEBT-032 resolved in BATCH-04).
        Assert.False(result.Success);

        // Client B is not affected.
        clientB.Dispose();
    }
}
