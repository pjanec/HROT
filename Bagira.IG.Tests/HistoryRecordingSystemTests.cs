using System.Numerics;
using Bagira.IG.Components;
using Bagira.IG.Systems;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for Task IG.4.1: <see cref="HistoryRecordingSystem"/> and the
/// <see cref="HistoryTrail"/> circular buffer.
///
/// Validates:
/// <list type="bullet">
///   <item>Circular-buffer correctly evicts oldest points when <see cref="HistoryTrailConstants.MaxTrailPoints"/> is exceeded.</item>
///   <item>Entities with <see cref="ResolvedStyle.ShowTrail"/> = <c>true</c> accumulate samples.</item>
///   <item>Entities with <see cref="ResolvedStyle.ShowTrail"/> = <c>false</c> never accumulate samples.</item>
///   <item><see cref="HistoryTrail.SampleInterval"/> prevents over-sampling.</item>
///   <item>Sub-frame timing is preserved (remainder carried forward).</item>
/// </list>
///
/// No DDS or Raylib window context required.
/// </summary>
public class HistoryRecordingSystemTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const float SmallDt         = 0.1f;  // Below the default 0.5 s interval
    private const float SufficientDt    = 0.5f;  // Equals the default interval → triggers sample
    private const float EntityX         = 100f;
    private const float EntityY         = 200f;
    private const float MovedX          = 150f;
    private const float MovedY          = 250f;

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<ResolvedStyle>();
        repo.RegisterComponent<HistoryTrail>();
        return repo;
    }

    /// <summary>Creates an entity at (x, y) with a HistoryTrail and the given ShowTrail flag.</summary>
    private static Entity CreateEntity(EntityRepository repo, float x, float y, bool showTrail,
        float sampleInterval = HistoryTrailConstants.DefaultSampleIntervalSeconds)
    {
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(x, y, 0f),
            Rotation = Quaternion.Identity,
        });

        var style = ResolvedStyle.CreateDefault();
        style.ShowTrail = showTrail;
        repo.AddComponent(entity, style);

        repo.AddComponent(entity, HistoryTrail.Create(sampleInterval));
        return entity;
    }

    private static void RunSystem(EntityRepository repo, HistoryRecordingSystem system, float dt)
    {
        system.Execute(repo, dt);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Circular-buffer boundary tests (required by IG.4.1 acceptance criteria)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the buffer is filled to capacity and one more point is added, Count
    /// must remain at MaxTrailPoints (not grow beyond the fixed limit).
    /// </summary>
    [Fact]
    public void HistoryTrail_AddBeyondMax_CountStaysAtMax()
    {
        var trail = HistoryTrail.Create(sampleInterval: 0f);

        for (int i = 0; i < HistoryTrailConstants.MaxTrailPoints + 5; i++)
            trail.AddPoint(i, i);

        Assert.Equal(HistoryTrailConstants.MaxTrailPoints, trail.Count);
    }

    /// <summary>
    /// After filling the buffer exactly to MaxTrailPoints, the newest point must
    /// be accessible at index Count − 1.
    /// </summary>
    [Fact]
    public void HistoryTrail_WhenFull_NewestPointIsLast()
    {
        var trail = HistoryTrail.Create(sampleInterval: 0f);

        for (int i = 0; i < HistoryTrailConstants.MaxTrailPoints; i++)
            trail.AddPoint(i * 10f, 0f);

        float expectedX = (HistoryTrailConstants.MaxTrailPoints - 1) * 10f;
        var   (lastX, _) = trail.GetPoint(trail.Count - 1);

        Assert.Equal(expectedX, lastX);
    }

    /// <summary>
    /// After the buffer is full and one more point is added, the oldest (first)
    /// point must be the second-ever point, not the first.
    /// </summary>
    [Fact]
    public void HistoryTrail_WhenFull_OldestPointEvictedOnOverflow()
    {
        var trail = HistoryTrail.Create(sampleInterval: 0f);

        // Fill the buffer: points 0..MaxTrailPoints-1
        for (int i = 0; i < HistoryTrailConstants.MaxTrailPoints; i++)
            trail.AddPoint(i, 0f);

        // Add one more — point 0 (value 0.0 f) should be evicted.
        float overflowX = 999f;
        trail.AddPoint(overflowX, 0f);

        // Oldest is now point index 1 (value 1.0 f).
        var (oldestX, _) = trail.GetPoint(0);
        Assert.Equal(1f, oldestX);

        // Newest is the overflow point.
        var (newestX, _) = trail.GetPoint(trail.Count - 1);
        Assert.Equal(overflowX, newestX);
    }

    /// <summary>
    /// After adding N &lt; MaxTrailPoints points, Count should equal N and the
    /// points should be readable in insertion order (oldest at 0).
    /// </summary>
    [Fact]
    public void HistoryTrail_PartialFill_OrderedCorrectly()
    {
        const int PointsToAdd = 5;
        var trail = HistoryTrail.Create(sampleInterval: 0f);

        for (int i = 0; i < PointsToAdd; i++)
            trail.AddPoint(i * 100f, i * 50f);

        Assert.Equal(PointsToAdd, trail.Count);

        for (int i = 0; i < PointsToAdd; i++)
        {
            var (x, y) = trail.GetPoint(i);
            Assert.Equal(i * 100f, x);
            Assert.Equal(i * 50f,  y);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HistoryRecordingSystem tests
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// With ShowTrail = true and a delta equal to the sample interval, the system
    /// must append exactly one point on each Execute call.
    /// </summary>
    [Fact]
    public void Execute_ShowTrailTrue_RecordsPointEachInterval()
    {
        var repo   = CreateRepo();
        var entity = CreateEntity(repo, EntityX, EntityY, showTrail: true);
        var system = new HistoryRecordingSystem();

        RunSystem(repo, system, SufficientDt);

        var trail = repo.GetComponent<HistoryTrail>(entity);
        Assert.Equal(1, trail.Count);
        var (x, y) = trail.GetPoint(0);
        Assert.Equal(EntityX, x);
        Assert.Equal(EntityY, y);
    }

    /// <summary>
    /// With ShowTrail = false the system must never append any point regardless
    /// of how many ticks pass.
    /// </summary>
    [Fact]
    public void Execute_ShowTrailFalse_NeverRecords()
    {
        var repo   = CreateRepo();
        var entity = CreateEntity(repo, EntityX, EntityY, showTrail: false);
        var system = new HistoryRecordingSystem();

        // Run several ticks — well above the sample interval.
        for (int i = 0; i < 10; i++)
            RunSystem(repo, system, SufficientDt);

        var trail = repo.GetComponent<HistoryTrail>(entity);
        Assert.Equal(0, trail.Count);
    }

    /// <summary>
    /// A delta smaller than the sample interval must not produce a sample.
    /// </summary>
    [Fact]
    public void Execute_DeltaBelowInterval_DoesNotSample()
    {
        var repo   = CreateRepo();
        var entity = CreateEntity(repo, EntityX, EntityY, showTrail: true,
            sampleInterval: SufficientDt); // interval = 0.5 s
        var system = new HistoryRecordingSystem();

        // 0.1 s < 0.5 s → no sample expected.
        RunSystem(repo, system, SmallDt);

        var trail = repo.GetComponent<HistoryTrail>(entity);
        Assert.Equal(0, trail.Count);
    }

    /// <summary>
    /// After an interval elapses, if the entity has moved, the recorded point
    /// must reflect the entity's position at sample time.
    /// </summary>
    [Fact]
    public void Execute_EntityMoved_RecordedPositionMatchesTransform()
    {
        var repo   = CreateRepo();
        var entity = CreateEntity(repo, EntityX, EntityY, showTrail: true);
        var system = new HistoryRecordingSystem();

        // Move entity before sampling.
        repo.SetComponent(entity, new SimTransform
        {
            Position = new Vector3(MovedX, MovedY, 0f),
            Rotation = Quaternion.Identity,
        });

        RunSystem(repo, system, SufficientDt);

        var trail    = repo.GetComponent<HistoryTrail>(entity);
        var (x, y)   = trail.GetPoint(0);
        Assert.Equal(MovedX, x);
        Assert.Equal(MovedY, y);
    }

    /// <summary>
    /// Two calls each with dt = half the interval must not produce a sample
    /// after the first, but must produce one after the second call when the
    /// accumulated elapsed time reaches the interval.
    /// </summary>
    [Fact]
    public void Execute_TwoHalfIntervalTicks_SamplesOnSecondTick()
    {
        float halfInterval = HistoryTrailConstants.DefaultSampleIntervalSeconds / 2f;

        var repo   = CreateRepo();
        var entity = CreateEntity(repo, EntityX, EntityY, showTrail: true);
        var system = new HistoryRecordingSystem();

        RunSystem(repo, system, halfInterval); // 0.25 s — below interval
        Assert.Equal(0, repo.GetComponent<HistoryTrail>(entity).Count);

        RunSystem(repo, system, halfInterval); // 0.50 s — hits interval
        Assert.Equal(1, repo.GetComponent<HistoryTrail>(entity).Count);
    }

    /// <summary>
    /// Running the system many times must never cause Count to exceed MaxTrailPoints.
    /// </summary>
    [Fact]
    public void Execute_ManyTicks_CountNeverExceedsMax()
    {
        var repo   = CreateRepo();
        var entity = CreateEntity(repo, EntityX, EntityY, showTrail: true);
        var system = new HistoryRecordingSystem();

        int iterations = HistoryTrailConstants.MaxTrailPoints * 3;
        for (int i = 0; i < iterations; i++)
            RunSystem(repo, system, SufficientDt);

        var trail = repo.GetComponent<HistoryTrail>(entity);
        Assert.Equal(HistoryTrailConstants.MaxTrailPoints, trail.Count);
    }
}
