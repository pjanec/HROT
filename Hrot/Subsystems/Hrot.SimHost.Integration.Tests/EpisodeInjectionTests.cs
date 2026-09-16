using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration;
using Hrot.Orchestrator;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Scenario;
using Fdp.Toolkit.Scenario;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.SimHost.Integration.Tests;

/// <summary>
/// ⭐ Component for episode injection tests.
///
/// <para>🔴 <b>WAS <c>ComponentId(215)</c>, AND THAT COLLIDED WITH PRODUCTION.</b> 📐 Measured
/// <c>2026-09-01</c>: <c>GlobalComponentIds.cs:414</c> declares <c>IPathRegistry = 215</c>, so
/// registration threw <i>"Component ID collision: EpisodeTestPos and IPathRegistry both declare
/// [ComponentId(215)]"</i> — and because the component-type registry is <b>process-global</b>, that one
/// throw failed <b>7 of this project's 13 reds</b> across SIX unrelated classes that never touch
/// episodes.</para>
///
/// <para>⚠⚠ <b>The old comment records exactly how it happened:</b> <i>"ComponentId 210 = PreviewTestPos
/// is already taken; use 215 for episode tests"</i> — a free-slot hunt that landed inside a range
/// <c>GlobalComponentIds.cs:453</c> explicitly reserves: <i>"IDs 215–219, 226–236, 241–255 are reserved
/// for future animation/toolkit components."</i> ⇒ ⛔ the test squatted on reserved space and production
/// later claimed it. The test was always the one in the wrong.</para>
///
/// <para>⭐⭐ <b>The rule this encodes: a TEST component must not sit in a production-reserved range.</b>
/// 506 is at the top of the valid space (<c>MAX_COMPONENT_TYPES = 512</c>, highest production id in use is
/// 505) and is deliberately far from anything production is growing into.</para>
/// </summary>
[ComponentId(506)]
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

    /// <summary>
    /// 🔴 <b>The ONE storage provider — used to WRITE the fixture and to READ it back.</b>
    ///
    /// <para>📌 <b>Why this field exists</b> (measured <c>2026-09-01</c>): these tests used to hand-build
    /// the fixture path as <c>_tempRoot/scenarioId/Hrot.SimHost.json</c>, but
    /// <see cref="LocalDiskStorageProvider"/> reads from
    /// <c>_tempRoot/<b>scenarios</b>/scenarioId/…</c> (<c>LocalDiskStorageProvider.cs:52</c> ·
    /// <c>OrchestrationConstants.ScenariosDirectoryName == "scenarios"</c>). ⇒ the loader enumerated an
    /// empty directory, <c>TryLoadScenarioJson</c> returned <see langword="null"/>, and
    /// <c>CommitStartEpisode</c> returned <b>before deserializing</b> — silently, with no throw. Three
    /// rails then failed as <i>"expected 3, actual 0"</i>, pointing at the episode pipeline rather than
    /// at the fixture.</para>
    ///
    /// <para>⭐⭐ <b>The fix is to stop hand-computing the layout.</b> The fixture is written through
    /// <see cref="IScenarioStorageProvider.EnsureStagingDirectory"/> — the same provider the handler
    /// reads through — so the two halves cannot drift apart again by construction.</para>
    /// </summary>
    private readonly LocalDiskStorageProvider _storage;

    public EpisodeInjectionTests()
    {
        _tempRoot  = Path.Combine(Path.GetTempPath(), "episode_inj_" + Guid.NewGuid().ToString("N"));
        _storage   = new LocalDiskStorageProvider(_tempRoot);
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
    /// Creates a scenario JSON file in the staging directory <see cref="_storage"/> itself resolves for
    /// <paramref name="scenarioId"/>, with <paramref name="entityCount"/> entities each having a
    /// <see cref="EpisodeTestPos"/>. ⛔ Never hand-build this path — see <see cref="_storage"/>.
    /// </summary>
    private async Task<string> WriteEpisodeScenario(string scenarioId, int entityCount)
    {
        var dir = _storage.EnsureStagingDirectory(scenarioId);

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
            new HrotScenarioLoader(_storage, _serializer.SubsystemType));
        var cmd     = MakeCmd((int)Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode, episodeId, scenarioId);

        await handler.PrepareAsync(cmd, CancellationToken.None);
        handler.Commit(cmd, _repo);

        Assert.Equal(3, _repo.EntityCount);
        AssertAllEntitiesHaveEpisodeTag(episodeId);
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
            new HrotScenarioLoader(_storage, _serializer.SubsystemType));

        // Start
        var startCmd = MakeCmd((int)Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode, episodeId, scenarioId);
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
        // ⚠ Until 2026-09-01 this wrote to a path the provider does not read, so the rail PASSED FOR THE
        //   WRONG REASON — the file was never found and the subsystem-mismatch branch it names was never
        //   reached. Staging it correctly is what makes the assertion below non-vacuous.
        var dir        = _storage.EnsureStagingDirectory(scenarioId);

        // Write a CGF-typed scenario file (not matching the SimHost serializer).
        var cgfJson = "{\"Header\":{\"SubsystemType\":\"Hrot.CGF\",\"SchemaVersion\":1},\"Entities\":{}}";
        await File.WriteAllTextAsync(Path.Combine(dir, "Hrot.CGF.json"), cgfJson);

        var handler = new ReferenceEpisodeLoadHandler(
            _serializer,
            new HrotScenarioLoader(_storage, _serializer.SubsystemType));
        var cmd     = MakeCmd((int)Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode, episodeId, scenarioId);

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
            new HrotScenarioLoader(_storage, _serializer.SubsystemType));

        // Inject episode 1.
        var start1 = MakeCmd((int)Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode, s1Id, sc1);
        await handler.PrepareAsync(start1, CancellationToken.None);
        handler.Commit(start1, _repo);
        Assert.Equal(3, _repo.EntityCount);

        // Inject episode 2.
        var start2 = MakeCmd((int)Fdp.Toolkit.Orchestration.NodeOpType.StartEpisode, s2Id, sc2);
        await handler.PrepareAsync(start2, CancellationToken.None);
        handler.Commit(start2, _repo);
        Assert.Equal(5, _repo.EntityCount);

        // Stop episode 1 — only its 3 entities should be gone.
        var stop1 = MakeStopCmd(s1Id);
        await handler.PrepareAsync(stop1, CancellationToken.None);
        handler.Commit(stop1, _repo);
        Assert.Equal(2, _repo.EntityCount);

        // Episode 2 entities still present.
        AssertAllEntitiesHaveEpisodeTag(s2Id);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronous helper: <see cref="EntityQuery"/>'s enumerator is a ref struct and
    /// <see cref="EntityRepository.GetComponentRO{T}"/> returns a by-ref value, neither of
    /// which can live inside an async method's state machine, so this stays synchronous
    /// and is called (without awaiting) from the async test methods.
    /// </summary>
    private void AssertAllEntitiesHaveEpisodeTag(Guid expectedEpisodeId)
    {
        var query = _repo.Query().With<EpisodeTag>().Build();
        foreach (var e in query)
        {
            ref readonly var tag = ref _repo.GetComponentRO<EpisodeTag>(e);
            Assert.Equal(expectedEpisodeId, tag.EpisodeId);
        }
    }

    // ── Factories ─────────────────────────────────────────────────────────────

    private static ExecuteNodeOpIntent MakeCmd(int op, Guid episodeId, string scenarioId) =>
        new()
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = (Fdp.Toolkit.Orchestration.NodeOpType)op,
            DomainPayload = new EpisodeHandlerPayload(episodeId, scenarioId, IsStart: true),
        };

    private static ExecuteNodeOpIntent MakeStopCmd(Guid episodeId) =>
        new()
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = Fdp.Toolkit.Orchestration.NodeOpType.StopEpisode,
            DomainPayload = new EpisodeHandlerPayload(episodeId, ScenarioId: null, IsStart: false),
        };
}
