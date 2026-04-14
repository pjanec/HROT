using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration;
using Hrot.Orchestrator;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using Hrot.Common.Scenario;
using FDP.Toolkit.Scenario;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.SimHost.Integration.Tests;

/// <summary>
/// Component for episode injection tests (ComponentId 210 = PreviewTestPos is already taken;
/// use 215 for episode tests).
/// </summary>
[ComponentId(215)]
internal struct EpisodeTestPos
{
    public float X;
    public float Y;
    public float Z;
}

/// <summary>
/// Integration tests for CGF1-S0308: Runtime Episode Injection &amp; Deletion.
///
/// <para>Tests exercise <see cref="ReferenceEpisodeLoadHandler"/> and
    /// <see cref="ClusterMasterPlanner.PlanManageEpisode"/> directly — no DDS or kernel required.</para>
/// </summary>
public sealed class EpisodeInjectionTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ScenarioSerializer _serializer;
    private readonly EntityRepository   _repo;

    public EpisodeInjectionTests()
    {
        _tempRoot  = Path.Combine(Path.GetTempPath(), "episode_inj_" + Guid.NewGuid().ToString("N"));
        _repo      = new EntityRepository();
        _repo.RegisterComponent<EpisodeTestPos>();
        _repo.RegisterComponent<EpisodeTag>();
        _serializer = new ScenarioSerializerBuilder("Hrot.SimHost").Build();
    }

    public void Dispose()
    {
        _repo.Dispose();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a scenario JSON file under <c>_tempRoot/scenarioId/Hrot.SimHost.json</c>
    /// with <paramref name="entityCount"/> entities each having a <see cref="EpisodeTestPos"/>.
    /// </summary>
    private async Task<string> WriteEpisodeScenario(string scenarioId, int entityCount)
    {
        var dir = Path.Combine(_tempRoot, scenarioId);
        Directory.CreateDirectory(dir);

        // Build a temp repo to serialize from.
        var buildRepo = new EntityRepository();
        buildRepo.RegisterComponent<EpisodeTestPos>();
        buildRepo.RegisterComponent<EpisodeTag>();

        for (int i = 0; i < entityCount; i++)
        {
            var e = buildRepo.CreateEntity();
            buildRepo.SetComponent(e, new EpisodeTestPos { X = i, Y = 0f, Z = 0f });
        }

        var dom  = _serializer.Serialize(buildRepo, new ScenarioHeader("Hrot.SimHost"));
        var path = Path.Combine(dir, "Hrot.SimHost.json");
        await File.WriteAllTextAsync(path, dom.ToJsonString());

        buildRepo.Dispose();
        return scenarioId;
    }

    // ── Test 1 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Injecting a episode with a known <paramref name="episodeId"/> spawns 3 new entities,
    /// each stamped with <see cref="EpisodeTag.EpisodeId"/> == <paramref name="episodeId"/>.
    /// </summary>
    [Fact]
    public async Task StartEpisode_EntitiesSpawnedWithEpisodeTag()
    {
        var episodeId    = Guid.NewGuid();
        var scenarioId = await WriteEpisodeScenario("episode_start_01", entityCount: 3);

        var handler = new ReferenceEpisodeLoadHandler(
            _serializer,
            new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType));
        var cmd     = MakeCmd((int)FDP.Toolkit.Orchestration.NodeOpType.StartEpisode, episodeId, scenarioId);

        await handler.PrepareAsync(cmd, CancellationToken.None);
        handler.Commit(cmd, _repo);

        Assert.Equal(3, _repo.EntityCount);
        var query = _repo.Query().With<EpisodeTag>().Build();
        foreach (var e in query)
        {
            ref readonly var tag = ref _repo.GetComponentRO<EpisodeTag>(e);
            Assert.Equal(episodeId, tag.EpisodeId);
        }
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// After injecting a episode with 3 entities, stopping the same episode destroys all 3.
    /// </summary>
    [Fact]
    public async Task StopEpisode_EntitiesDestroyedByEpisodeTag()
    {
        var episodeId    = Guid.NewGuid();
        var scenarioId = await WriteEpisodeScenario("episode_stop_01", entityCount: 3);

        var handler = new ReferenceEpisodeLoadHandler(
            _serializer,
            new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType));

        // Start
        var startCmd = MakeCmd((int)FDP.Toolkit.Orchestration.NodeOpType.StartEpisode, episodeId, scenarioId);
        await handler.PrepareAsync(startCmd, CancellationToken.None);
        handler.Commit(startCmd, _repo);
        Assert.Equal(3, _repo.EntityCount);

        // Stop
        var stopCmd = MakeStopCmd(episodeId);
        await handler.PrepareAsync(stopCmd, CancellationToken.None);
        handler.Commit(stopCmd, _repo);

        Assert.Equal(0, _repo.EntityCount);
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the episode scenario file has <c>SubsystemType = "Hrot.CGF"</c> (not matching
    /// this SimHost handler), <see cref="ReferenceEpisodeLoadHandler.IsParticipatingForTest"/> is
    /// <c>false</c> and <see cref="EntityRepository.EntityCount"/> is unchanged.
    /// </summary>
    [Fact]
    public async Task StartEpisode_NonMatchingSubsystem_IsParticipatingFalse()
    {
        var episodeId    = Guid.NewGuid();
        var scenarioId = "episode_nomatch_01";
        var dir        = Path.Combine(_tempRoot, scenarioId);
        Directory.CreateDirectory(dir);

        // Write a CGF-typed scenario file (not matching the SimHost serializer).
        var cgfJson = "{\"Header\":{\"SubsystemType\":\"Hrot.CGF\",\"SchemaVersion\":1},\"Entities\":{}}";
        await File.WriteAllTextAsync(Path.Combine(dir, "Hrot.CGF.json"), cgfJson);

        var handler = new ReferenceEpisodeLoadHandler(
            _serializer,
            new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType));
        var cmd     = MakeCmd((int)FDP.Toolkit.Orchestration.NodeOpType.StartEpisode, episodeId, scenarioId);

        await handler.PrepareAsync(cmd, CancellationToken.None);
        handler.Commit(cmd, _repo);

        Assert.False(handler.IsParticipatingForTest);
        Assert.Equal(0, _repo.EntityCount);
    }

    // ── Test 4 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="ClusterMasterPlanner.PlanManageEpisode"/> throws when the cluster is not in
    /// <see cref="ClusterState.OperatingLive"/>.
    /// </summary>
    [Fact]
    public void ManageEpisode_RejectedWhen_NotInRunningLive()
    {
        var planner = new ClusterMasterPlanner(HrotStateGraph.Build());
        var intent  = new ManageEpisodeIntent
        {
            TransactionId = Guid.NewGuid(),
            IsStart       = true,
            EpisodeId     = Guid.NewGuid(),
            ScenarioId    = "scenario_01",
        };

        // Should reject for any state that is not OperatingLive.
        Assert.Throws<InvalidOperationException>(() =>
            planner.PlanManageEpisode(ClusterState.Idle, intent));
        Assert.Throws<InvalidOperationException>(() =>
            planner.PlanManageEpisode(ClusterState.OperatingEdit, intent));
        Assert.Throws<InvalidOperationException>(() =>
            planner.PlanManageEpisode(ClusterState.OperatingReplay, intent));
    }

    // ── Test 5 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two episodes with different IDs can coexist; stopping one leaves the other intact.
    /// </summary>
    [Fact]
    public async Task MultipleEpisodesCoexist_IndependentDeletion()
    {
        var s1Id = Guid.NewGuid();
        var s2Id = Guid.NewGuid();

        var sc1 = await WriteEpisodeScenario("episode_multi_s1", entityCount: 3);
        var sc2 = await WriteEpisodeScenario("episode_multi_s2", entityCount: 2);

        var handler = new ReferenceEpisodeLoadHandler(
            _serializer,
            new HrotScenarioLoader(new LocalDiskStorageProvider(_tempRoot), _serializer.SubsystemType));

        // Inject episode 1.
        var start1 = MakeCmd((int)FDP.Toolkit.Orchestration.NodeOpType.StartEpisode, s1Id, sc1);
        await handler.PrepareAsync(start1, CancellationToken.None);
        handler.Commit(start1, _repo);
        Assert.Equal(3, _repo.EntityCount);

        // Inject episode 2.
        var start2 = MakeCmd((int)FDP.Toolkit.Orchestration.NodeOpType.StartEpisode, s2Id, sc2);
        await handler.PrepareAsync(start2, CancellationToken.None);
        handler.Commit(start2, _repo);
        Assert.Equal(5, _repo.EntityCount);

        // Stop episode 1 — only its 3 entities should be gone.
        var stop1 = MakeStopCmd(s1Id);
        await handler.PrepareAsync(stop1, CancellationToken.None);
        handler.Commit(stop1, _repo);
        Assert.Equal(2, _repo.EntityCount);

        // Episode 2 entities still present.
        var query = _repo.Query().With<EpisodeTag>().Build();
        foreach (var e in query)
        {
            ref readonly var tag = ref _repo.GetComponentRO<EpisodeTag>(e);
            Assert.Equal(s2Id, tag.EpisodeId);
        }
    }

    // ── Factories ─────────────────────────────────────────────────────────────

    private static ExecuteNodeOpIntent MakeCmd(int op, Guid episodeId, string scenarioId) =>
        new()
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = (FDP.Toolkit.Orchestration.NodeOpType)op,
            DomainPayload = new EpisodeHandlerPayload(episodeId, scenarioId, IsStart: true),
        };

    private static ExecuteNodeOpIntent MakeStopCmd(Guid episodeId) =>
        new()
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = FDP.Toolkit.Orchestration.NodeOpType.StopEpisode,
            DomainPayload = new EpisodeHandlerPayload(episodeId, ScenarioId: null, IsStart: false),
        };
}
