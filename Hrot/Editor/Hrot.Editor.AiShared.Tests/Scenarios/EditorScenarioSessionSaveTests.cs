using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Scenarios;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Scenarios;

/// <summary>
/// ⭐⭐⭐ CE-275 ③ — the editor (and CGF, via this same class) SAVES THROUGH THE CLUSTER, never with a local
/// direct file write. Proves <see cref="EditorScenarioSession.SaveAs"/> / <see cref="EditorScenarioSession.SaveCurrent"/>
/// publish a <see cref="StorageOpType.SaveScenario"/> storage-op intent carrying a scenario NAME (not a
/// path) and touch no filesystem. 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4.
/// </summary>
public sealed class EditorScenarioSessionSaveTests
{
    private static readonly string FakeRoot = Path.Combine(Path.GetTempPath(), "hrot-scnroot-should-not-be-written");

    private static EditorScenarioSession NewSession(FdpEventBus bus)
    {
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        return new EditorScenarioSession(fileService, bus, () => new Fdp.Toolkit.Orchestration.ClusterView(bus), new EntityRepository(), () => FakeRoot);
    }

    /// <summary>
    /// ⭐ CE-3394 (presets D10) — a world built with a variant ≠ 0 is NOT saved: <see cref="IScenarioSession.SaveDisabledReason"/> is the gate's reason (naming the variant; the File menu
    /// greys Save / Save As with it) and SaveAs / SaveCurrent publish nothing; an as-authored world saves as before.
    /// 🔴 Red-proof (compiles): <c>SaveDisabledReason</c> answering null ⇒ the intent is published ⇒ red.
    /// </summary>
    [Fact]
    public void AVariantWorld_IsNotSaved_AndTheReasonNamesTheVariant()
    {
        var bus = new FdpEventBus();
        var world = new EntityRepository();
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        var session = new EditorScenarioSession(fileService, bus, () => new Fdp.Toolkit.Orchestration.ClusterView(bus), world, () => FakeRoot);
        Assert.Null(((IScenarioSession)session).SaveDisabledReason);

        world.RegisterManagedComponent<VariantForms>();
        world.SetManagedComponent(world.CreateEntity(), new VariantForms { Variant = 5 });
        world.Tick();   // a load moves the world's version (the reason is cached per version)
        Assert.Contains("variant 5", ((IScenarioSession)session).SaveDisabledReason);
        session.SaveAs("area/variant_copy");
        bus.SwapBuffers();
        Assert.Empty(bus.ReadManaged<ExecuteStorageOpIntent>());
    }

    [Fact]
    public void SaveAs_PublishesSaveScenarioJsonIntent_WithTheName_AndWritesNoFile()
    {
        if (Directory.Exists(FakeRoot)) Directory.Delete(FakeRoot, recursive: true);

        var bus     = new FdpEventBus();
        var session = NewSession(bus);

        session.SaveAs("area/my_scenario");
        bus.SwapBuffers();

        var intent = Assert.Single(bus.ReadManaged<ExecuteStorageOpIntent>());
        Assert.Equal(StorageOpType.SaveScenario, intent.Operation);
        Assert.Equal("area/my_scenario", intent.ScenarioName);

        // ⛔ No local/direct write: the scenarios root must be untouched — the cluster handler writes, not this.
        Assert.False(Directory.Exists(FakeRoot));
    }

    [Fact]
    public void SaveCurrent_PublishesForTheLoadedName()
    {
        var bus     = new FdpEventBus();
        var session = NewSession(bus);

        session.SaveAs("keeper");   // sets the loaded name
        bus.SwapBuffers();
        _ = bus.ReadManaged<ExecuteStorageOpIntent>().ToArray();   // drain

        session.SaveCurrent();
        bus.SwapBuffers();

        var intent = Assert.Single(bus.ReadManaged<ExecuteStorageOpIntent>());
        Assert.Equal(StorageOpType.SaveScenario, intent.Operation);
        Assert.Equal("keeper", intent.ScenarioName);
    }

    [Fact]
    public void SaveCurrent_WithNoLoadedScenario_PublishesNothing()
    {
        var bus     = new FdpEventBus();
        var session = NewSession(bus);

        session.SaveCurrent();
        bus.SwapBuffers();

        Assert.Empty(bus.ReadManaged<ExecuteStorageOpIntent>());
    }

    // ── ⭐ CE-3407 (AQ108 §0 D27, R-3083) — ONE "can I save" answer: the cluster (an error, any state but Edit) before the variant gate ──────────────

    private static (EditorScenarioSession Session, ClusterView View, FdpEventBus Bus) FollowingSession()
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        return (new EditorScenarioSession(fileService, bus, () => view, new EntityRepository(), () => FakeRoot), view, bus);
    }

    private static void ClusterIs(FdpEventBus bus, ClusterView view, ClusterState state, string? error = null)
    {
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ErrorReason = error });
        bus.SwapBuffers();
        view.Update();
    }

    /// <summary>
    /// ⭐ CE-3407 — only Edit saves the scenario: Preview, Live, DAR, AAR, Idle and the moves between them each give their reason; a healthy Edit gives none.
    /// 🔴 Red-proof (compiles): <c>SaveDisabledReason</c> without its <c>SaveRefusal.Of</c> line ⇒ every non-Edit row answers null ⇒ red.
    /// </summary>
    [Theory]
    [InlineData(ClusterState.OperatingEdit, null)]
    [InlineData(ClusterState.OperatingPreview, SaveRefusal.Preview)]
    [InlineData(ClusterState.OperatingLive, SaveRefusal.Live)]
    [InlineData(ClusterState.OperatingReview, SaveRefusal.Review)]
    [InlineData(ClusterState.OperatingReplay, SaveRefusal.Replay)]
    [InlineData(ClusterState.Idle, SaveRefusal.NoScene)]
    [InlineData(ClusterState.LoadingLive, SaveRefusal.Busy)]
    public void CE3407_OnlyEditSavesTheScenario_EveryOtherStateSaysWhy(ClusterState state, string? why)
    {
        var (session, view, bus) = FollowingSession();
        ClusterIs(bus, view, state);
        Assert.Equal(why, ((IScenarioSession)session).SaveDisabledReason);
    }

    /// <summary>⭐ CE-3407 — a cluster in error (CE-3345) refuses the save even in Edit, and names the error; the error wins over the variant.</summary>
    [Fact]
    public void CE3407_AClusterInError_RefusesTheSave_AndNamesTheError()
    {
        var (session, view, bus) = FollowingSession();
        ClusterIs(bus, view, ClusterState.OperatingEdit, "node 3 failed to load terrain");
        Assert.Equal("the cluster is in error: node 3 failed to load terrain", ((IScenarioSession)session).SaveDisabledReason);
        ClusterIs(bus, view, ClusterState.OperatingEdit);
        Assert.Null(((IScenarioSession)session).SaveDisabledReason);
    }

    /// <summary>
    /// ⭐ CE-3407 — the cluster half gates the SURFACES only: <c>SaveAs</c> still publishes while the cluster is Idle, because <c>New from a recipe</c> calls it the moment its
    /// open went out (the master orders the save after the load). 🔴 Red-proof: <c>SaveAs</c> guarding on <c>SaveDisabledReason</c> ⇒ nothing published ⇒ red.
    /// </summary>
    [Fact]
    public void CE3407_SaveAsRightAfterAnOpen_StillGoesOut_WhileTheClusterIsIdle()
    {
        var (session, view, bus) = FollowingSession();
        ClusterIs(bus, view, ClusterState.Idle);
        Assert.NotNull(((IScenarioSession)session).SaveDisabledReason);   // the menu greys it …
        session.SaveAs("area/new_from_seed");                            // … but the seed's save-as is not a surface
        bus.SwapBuffers();
        Assert.Single(bus.ReadManaged<ExecuteStorageOpIntent>());
    }

    /// <summary>⭐ CE-3407 — the toolbar reads the reason every frame: 0 B per steady read (R-2084), in Edit and in a run.</summary>
    [Fact]
    public void CE3407_ReadingTheReason_AllocatesNothing()
    {
        var (session, view, bus) = FollowingSession();
        foreach (var state in new[] { ClusterState.OperatingEdit, ClusterState.OperatingLive })
        {
            ClusterIs(bus, view, state);
            _ = ((IScenarioSession)session).SaveDisabledReason;   // warm (the variant gate's first walk)
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) _ = ((IScenarioSession)session).SaveDisabledReason;
            Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
