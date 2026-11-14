using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Runner;
using Xunit;

namespace Hrot.ReplayBrowser.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6003</c> (city battle O-1; 📄 docs/DESIGN_Peek_And_Fire.md §9.9) — the Replay Browser and a REMOVED static obstacle. Its own file (not
/// <c>ReplayBrowserSubsystemTests</c>, which the parallel programme is changing) with its own small fixture: a flat base terrain, a recording.
/// </summary>
public sealed class ObstacleRemovalReplayTests : IDisposable
{
    private readonly ReplayBrowserSubsystem _subsystem = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"RBObstacle_{Guid.NewGuid():N}");

    public ObstacleRemovalReplayTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        _subsystem.Shutdown();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static SubsystemConfig HeadlessConfig() => new SubsystemConfig
    {
        DomainId = 0, Headless = true, OwnWindow = false, NodeId = (Fdp.Core.NodeId)(0), SubsystemName = "ReplayBrowser",
    };

    /// <summary>A terrain root holding one flat terrain, <c>base-t</c> (one building, so the base world is not empty).</summary>
    private string MakeTerrain()
    {
        string root = Path.Combine(_tempDir, "terrains");
        string folder = Path.Combine(root, "base-t");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "terrain.json"), """{"schemaVersion":2,"world":"w.geojson"}""");
        File.WriteAllText(Path.Combine(folder, "w.geojson"), """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,200,200],"groundZ":0},"features":[
              {"type":"Feature","properties":{"kind":"building","label":"B","height":5},"geometry":{"type":"Polygon","coordinates":[[[10,10],[20,10],[20,20],[10,20],[10,10]]]}}]}
            """);
        return root;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-6003</c> (city battle O-1, Peek and Fire §9.9) — a REMOVED obstacle is gone in the replay: a concrete block recorded from the first
    /// frame and destroyed at frame 10 (its owner's teardown, the entity leaves the world) — a seek PAST the removal shows a terrain without its prism,
    /// a seek back before it shows the prism again, and a seek past it once more removes it again. 🔴 Red-proof: drop <c>BakeRecordedObstacles</c> from the jump
    /// branch (<c>ReplayBrowserSubsystem.DebugSurface.cs</c>, a load or a seek) ⇒ red.
    /// </summary>
    [Fact]
    public void CE6003_ARemovedObstacle_IsGoneAfterASeekPastItsRemoval_AndBackBeforeIt()
    {
        const int removal = 10, frames = 30;
        var root = MakeTerrain();
        string path = Path.Combine(_tempDir, "removed.fdp");
        using (var repo = new EntityRepository())
        {
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainDefinition>();
            repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainDefinition { ResolvedName = "base-t" });
            repo.RegisterComponent<Fdp.Toolkit.Terrain.StaticObstacle>(); repo.RegisterComponent<Fdp.Toolkit.Terrain.ObstacleShape>(); repo.RegisterComponent<Fdp.Core.SimTransform>();
            repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var block = repo.CreateEntity();
            repo.AddComponent(block, new Fdp.Toolkit.Terrain.StaticObstacle { Material = new Fdp.Core.FixedString32("concrete") });
            repo.AddComponent(block, new Fdp.Toolkit.Terrain.ObstacleShape { Length = 2f, Width = 1f, Height = 1f });
            repo.AddComponent(block, new Fdp.Core.SimTransform { Position = new System.Numerics.Vector3(150f, 150f, 0f), Rotation = System.Numerics.Quaternion.Identity });
            repo.AddComponent(block, new Fdp.Toolkit.Combat.Components.Health { Current = 600f, Max = 600f });
            using var rec = new AsyncRecorder(path, new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)(Guid.NewGuid()), NodeId = (Fdp.Core.NodeId)(1) });
            Fdp.Toolkit.Replay.RecorderTickSystem.StampWorld(repo, rec.Metadata);
            for (int f = 0; f < frames; f++)
            {
                if (f == removal - 1) repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(block).Current = 0f;   // the burst empties it
                if (f == removal) repo.DestroyEntity(block);                                                            // its owner's teardown ends
                repo.Tick();
                long t = TimeSpan.FromSeconds(1 + f).Ticks;
                if (f == 0) rec.CaptureKeyframe(repo, t, blocking: true, eventBus: repo.Bus);
                else rec.CaptureFrame(repo, (uint)(f - 1), t, blocking: true, eventBus: repo.Bus);
                repo.Bus.SwapBuffers();
            }
        }
        _subsystem.TerrainCatalog = new Fdp.Toolkit.Terrain.TerrainCatalog(new[] { root });
        _subsystem.Initialize(HeadlessConfig());
        _subsystem.LoadFdpViaManager(path);   // the load path that bakes the recorded obstacles (O-8); the seeks below are the point

        int Obstacles() => _subsystem.ActiveRepo!.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>().Prisms.Count(p => p.Label!.StartsWith(Fdp.Toolkit.Terrain.TerrainObstacles.LabelPrefix));
        int Entities() { int n = 0; foreach (var _ in _subsystem.ActiveRepo!.Query().With<Fdp.Toolkit.Terrain.StaticObstacle>().Build()) n++; return n; }
        int AllLife() { int n = 0; foreach (var _ in _subsystem.ActiveRepo!.Query().With<Fdp.Toolkit.Terrain.StaticObstacle>().WithLifecycle(Fdp.Core.EntityLifecycle.All).Build()) n++; return n; }
        Assert.True(Obstacles() == 1, $"at open: prisms {Obstacles()}, active obstacles {Entities()}, any-lifecycle {AllLife()}, frame {_subsystem.PrimaryNodeCurrentFrame()}, seconds {_subsystem.ReplaySeconds}");
        _subsystem.SeekToSeconds(20.0);
        Assert.Equal(0, Entities());
        Assert.Equal(0, Obstacles());
        _subsystem.SeekToSeconds(3.0);
        Assert.Equal(1, Entities());
        Assert.Equal(1, Obstacles());
        _subsystem.SeekToSeconds(25.0);
        Assert.Equal(0, Obstacles());
    }

}
