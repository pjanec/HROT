using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-1303</c> P1 (docs/DESIGN_Director_Timeline_Editor.md §11j) — the film director is ONE assembly (<c>Hrot.Presentation</c>) behind ONE
/// registrar, and it needs no editor: a host with no extension and no tabs gets a working runner.
/// </summary>
public sealed class DirectorPackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ce1303-{Guid.NewGuid():N}");

    public void Dispose()
    {
        FilmStudio.Reset();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>The smallest film host: a world-less one that loads nothing and never steps.</summary>
    private sealed class BareHost : IDirectorHost
    {
        public BareHost(TakeStore takes) => Takes = takes;
        public EntityRepository? World => null;
        public void LoadScenario(string scenario) { }
        public bool IsOperatingEdit => true;
        public int? EntityCount => null;
        public bool InPreview { get; private set; }
        public void EnterPreview(bool startPaused) { InPreview = true; Paused = startPaused; }
        public void ExitPreview() => InPreview = false;
        public bool Paused { get; private set; }
        public void Pause() => Paused = true;
        public void StepBy(float seconds) { }
        public double SimTime => 0d;
        public double WallNow => 0d;
        public void Hold(double seconds) { }
        public string? BehaviourName(int hash) => null;
        public TakeStore Takes { get; }
    }

    [Fact]
    public void CE1303_P1_TheDirectorAssembly_HasNoEditorDependency()
    {
        // ① the assembly that holds the director references no editor assembly
        var asm = typeof(DirectorPack).Assembly;
        var editorRefs = asm.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("Hrot.Editor", StringComparison.Ordinal)).Select(a => a.Name).ToList();
        Assert.True(editorRefs.Count == 0, "Hrot.Presentation references " + string.Join(", ", editorRefs));

        // ② every type of the director lives in that namespace of that assembly
        foreach (var t in new[] { typeof(DirectorRunner), typeof(IDirectorHost), typeof(IRunExtension), typeof(DirectorTakeSession), typeof(DirectorWindow),
                                  typeof(WindowCinema), typeof(MapLayerOverride), typeof(AudioDirectorSound), typeof(FilmFrameSink), typeof(DirectorPack),
                                  typeof(DirectorPackHost), typeof(IDirectorWindowTab) })
        {
            Assert.Equal(asm, t.Assembly);
            Assert.Equal("Hrot.UI.Common.Map3D.Director", t.Namespace);
        }

        // ③ a host with NO extension and NO tabs: Attach yields a runner that plays a trivial reel to its first phase
        var store = new TakeStore(() => _root, () => Path.Combine(_root, "films"));
        Directory.CreateDirectory(Path.Combine(_root, "s-a"));
        File.WriteAllText(Path.Combine(_root, "s-a", "scenario.json"), "{\"name\":\"s-a\"}");
        store.Save(new Take { Scenario = "s-a", Name = "main", Title = "A", Shots = new[] { new ShotSpec { At = new AtExpr(0.5), Seconds = 0.5, Camera = new CameraSpec { LookAt = new System.Numerics.Vector3(1, 2, 3) } } } });
        var timeline = new Fdp.Presentation.Panels.Timeline.TimelinePanel();
        var pack = DirectorPack.Attach(new DirectorPackHost(new BareHost(store), () => null, () => null, Headless: true, Pilot: () => null, Timeline: timeline));

        Assert.NotNull(timeline.Tracks);                     // the pack wired the host's timeline to the open take
        Assert.Empty(pack.Session.Tracks);
        Assert.Equal(DirectorPhase.Idle, pack.Runner.Phase);
        var problems = pack.Runner.Play(new PlayRequest(Reel.Of("s-a", "main")));
        Assert.True(problems.Count == 0, string.Join("; ", problems));
        Assert.Equal(DirectorPhase.Loading, pack.Runner.Phase);
        pack.Tick();                                         // the pack's one per-frame call drives the runner
        Assert.True(pack.Runner.Busy);
        Assert.Null(pack.Runner.Extension);
        Assert.Empty(pack.Window.Tabs);
    }
}
