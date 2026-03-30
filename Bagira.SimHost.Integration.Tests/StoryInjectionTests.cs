using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using Bagira.Orchestrator;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Scenario;
using Xunit;

namespace Bagira.SimHost.Integration.Tests;

/// <summary>
/// Component for story injection tests (ComponentId 210 = DryRunTestPos is already taken;
/// use 215 for story tests).
/// </summary>
[ComponentId(215)]
internal struct StoryTestPos
{
    public float X;
    public float Y;
    public float Z;
}

/// <summary>
/// Integration tests for CGF1-S0308: Runtime Story Injection &amp; Deletion.
///
/// <para>Tests exercise <see cref="ReferenceStoryLoadHandler"/> and
    /// <see cref="DrillMasterPlanner.PlanManageStory"/> directly — no DDS or kernel required.</para>
/// </summary>
public sealed class StoryInjectionTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ScenarioSerializer _serializer;
    private readonly EntityRepository   _repo;

    public StoryInjectionTests()
    {
        _tempRoot  = Path.Combine(Path.GetTempPath(), "story_inj_" + Guid.NewGuid().ToString("N"));
        _repo      = new EntityRepository();
        _repo.RegisterComponent<StoryTestPos>();
        _repo.RegisterComponent<StoryTag>();
        _serializer = new ScenarioSerializerBuilder("Bagira.SimHost").Build();
    }

    public void Dispose()
    {
        _repo.Dispose();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a scenario JSON file under <c>_tempRoot/scenarioId/Bagira.SimHost.json</c>
    /// with <paramref name="entityCount"/> entities each having a <see cref="StoryTestPos"/>.
    /// </summary>
    private async Task<string> WriteStoryScenario(string scenarioId, int entityCount)
    {
        var dir = Path.Combine(_tempRoot, scenarioId);
        Directory.CreateDirectory(dir);

        // Build a temp repo to serialize from.
        var buildRepo = new EntityRepository();
        buildRepo.RegisterComponent<StoryTestPos>();
        buildRepo.RegisterComponent<StoryTag>();

        for (int i = 0; i < entityCount; i++)
        {
            var e = buildRepo.CreateEntity();
            buildRepo.SetComponent(e, new StoryTestPos { X = i, Y = 0f, Z = 0f });
        }

        var dom  = _serializer.Serialize(buildRepo, new ScenarioHeader("Bagira.SimHost"));
        var path = Path.Combine(dir, "Bagira.SimHost.json");
        await File.WriteAllTextAsync(path, dom.ToJsonString());

        buildRepo.Dispose();
        return scenarioId;
    }

    // ── Test 1 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Injecting a story with a known <paramref name="storyId"/> spawns 3 new entities,
    /// each stamped with <see cref="StoryTag.StoryId"/> == <paramref name="storyId"/>.
    /// </summary>
    [Fact]
    public async Task StartStory_EntitiesSpawnedWithStoryTag()
    {
        var storyId    = Guid.NewGuid();
        var scenarioId = await WriteStoryScenario("story_start_01", entityCount: 3);

        var handler = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot));
        var cmd     = MakeCmd(ReferenceStoryLoadHandler.StartStoryOperationId, storyId, scenarioId);

        await handler.PrepareAsync(cmd, CancellationToken.None);
        handler.Commit(cmd, _repo);

        Assert.Equal(3, _repo.EntityCount);
        var query = _repo.Query().With<StoryTag>().Build();
        foreach (var e in query)
        {
            ref readonly var tag = ref _repo.GetComponentRO<StoryTag>(e);
            Assert.Equal(storyId, tag.StoryId);
        }
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// After injecting a story with 3 entities, stopping the same story destroys all 3.
    /// </summary>
    [Fact]
    public async Task StopStory_EntitiesDestroyedByStoryTag()
    {
        var storyId    = Guid.NewGuid();
        var scenarioId = await WriteStoryScenario("story_stop_01", entityCount: 3);

        var handler = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot));

        // Start
        var startCmd = MakeCmd(ReferenceStoryLoadHandler.StartStoryOperationId, storyId, scenarioId);
        await handler.PrepareAsync(startCmd, CancellationToken.None);
        handler.Commit(startCmd, _repo);
        Assert.Equal(3, _repo.EntityCount);

        // Stop
        var stopCmd = MakeStopCmd(storyId);
        await handler.PrepareAsync(stopCmd, CancellationToken.None);
        handler.Commit(stopCmd, _repo);

        Assert.Equal(0, _repo.EntityCount);
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the story scenario file has <c>SubsystemType = "Bagira.CGF"</c> (not matching
    /// this SimHost handler), <see cref="ReferenceStoryLoadHandler.IsParticipatingForTest"/> is
    /// <c>false</c> and <see cref="EntityRepository.EntityCount"/> is unchanged.
    /// </summary>
    [Fact]
    public async Task StartStory_NonMatchingSubsystem_IsParticipatingFalse()
    {
        var storyId    = Guid.NewGuid();
        var scenarioId = "story_nomatch_01";
        var dir        = Path.Combine(_tempRoot, scenarioId);
        Directory.CreateDirectory(dir);

        // Write a CGF-typed scenario file (not matching the SimHost serializer).
        var cgfJson = "{\"Header\":{\"SubsystemType\":\"Bagira.CGF\",\"SchemaVersion\":1},\"Entities\":{}}";
        await File.WriteAllTextAsync(Path.Combine(dir, "Bagira.CGF.json"), cgfJson);

        var handler = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot));
        var cmd     = MakeCmd(ReferenceStoryLoadHandler.StartStoryOperationId, storyId, scenarioId);

        await handler.PrepareAsync(cmd, CancellationToken.None);
        handler.Commit(cmd, _repo);

        Assert.False(handler.IsParticipatingForTest);
        Assert.Equal(0, _repo.EntityCount);
    }

    // ── Test 4 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="DrillMasterPlanner.PlanManageStory"/> throws when the cluster is not in
    /// <see cref="DSMState.RunningLive"/>.
    /// </summary>
    [Fact]
    public void ManageStory_RejectedWhen_NotInRunningLive()
    {
        var planner = new DrillMasterPlanner(BagiraStateGraph.Build());
        var req     = new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.ManageStory,
            PayloadJson   = JsonSerializer.Serialize(new
            {
                Mode       = "Start",
                StoryId    = Guid.NewGuid().ToString("D"),
                ScenarioId = "scenario_01",
            }),
        };

        // Should reject for any state that is not RunningLive.
        Assert.Throws<InvalidOperationException>(() =>
            planner.PlanManageStory(DSMState.Standby, req));
        Assert.Throws<InvalidOperationException>(() =>
            planner.PlanManageStory(DSMState.RunningEdit, req));
        Assert.Throws<InvalidOperationException>(() =>
            planner.PlanManageStory(DSMState.RunningReplay, req));
    }

    // ── Test 5 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two stories with different IDs can coexist; stopping one leaves the other intact.
    /// </summary>
    [Fact]
    public async Task MultipleStoriesCoexist_IndependentDeletion()
    {
        var s1Id = Guid.NewGuid();
        var s2Id = Guid.NewGuid();

        var sc1 = await WriteStoryScenario("story_multi_s1", entityCount: 3);
        var sc2 = await WriteStoryScenario("story_multi_s2", entityCount: 2);

        var handler = new ReferenceStoryLoadHandler(_serializer, new LocalDiskStorageProvider(_tempRoot));

        // Inject story 1.
        var start1 = MakeCmd(ReferenceStoryLoadHandler.StartStoryOperationId, s1Id, sc1);
        await handler.PrepareAsync(start1, CancellationToken.None);
        handler.Commit(start1, _repo);
        Assert.Equal(3, _repo.EntityCount);

        // Inject story 2.
        var start2 = MakeCmd(ReferenceStoryLoadHandler.StartStoryOperationId, s2Id, sc2);
        await handler.PrepareAsync(start2, CancellationToken.None);
        handler.Commit(start2, _repo);
        Assert.Equal(5, _repo.EntityCount);

        // Stop story 1 — only its 3 entities should be gone.
        var stop1 = MakeStopCmd(s1Id);
        await handler.PrepareAsync(stop1, CancellationToken.None);
        handler.Commit(stop1, _repo);
        Assert.Equal(2, _repo.EntityCount);

        // Story 2 entities still present.
        var query = _repo.Query().With<StoryTag>().Build();
        foreach (var e in query)
        {
            ref readonly var tag = ref _repo.GetComponentRO<StoryTag>(e);
            Assert.Equal(s2Id, tag.StoryId);
        }
    }

    // ── Factories ─────────────────────────────────────────────────────────────

    private static OrchestrationCommand MakeCmd(int op, Guid storyId, string scenarioId) =>
        new OrchestrationCommand(
            TransactionId: Guid.NewGuid(),
            TargetNodeId:  0,
            OperationId:   op,
            PayloadJson:   JsonSerializer.Serialize(new
            {
                StoryId    = storyId.ToString("D"),
                ScenarioId = scenarioId,
            }));

    private static OrchestrationCommand MakeStopCmd(Guid storyId) =>
        new OrchestrationCommand(
            TransactionId: Guid.NewGuid(),
            TargetNodeId:  0,
            OperationId:   ReferenceStoryLoadHandler.StopStoryOperationId,
            PayloadJson:   JsonSerializer.Serialize(new
            {
                StoryId = storyId.ToString("D"),
            }));
}
