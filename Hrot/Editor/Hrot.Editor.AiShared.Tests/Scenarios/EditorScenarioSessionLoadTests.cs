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
/// ⭐ <c>CE-295</c> — the session's loads go through the shared sequence: from a loaded cluster it asks for Idle first and
/// sends the load once the cluster reports Idle. <see cref="EditorScenarioSession.LoadForLive"/> used to send one request,
/// which from Live planned an EMPTY path and only copied the files. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §9.
/// </summary>
public sealed class EditorScenarioSessionLoadTests
{
    private static EditorScenarioSession NewSession(FdpEventBus bus)
    {
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        return new EditorScenarioSession(fileService, bus, new EntityRepository(),
                                         () => Path.Combine(Path.GetTempPath(), "hrot-scnroot-should-not-be-written"));
    }

    private static TransitionStateIntent[] Drain(FdpEventBus bus)
    {
        bus.SwapBuffers();
        return bus.ReadManaged<TransitionStateIntent>().ToArray();
    }

    private static void ClusterIs(FdpEventBus bus, EditorScenarioSession session, ClusterState state)
    {
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state });
        bus.SwapBuffers();
        session.Update();
    }

    [Fact]
    public void LoadForLive_FromLive_UnloadsFirst_ThenLoads()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        ClusterIs(bus, session, ClusterState.OperatingLive);

        session.LoadForLive("b");
        var first = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.Idle, first.TargetState);

        ClusterIs(bus, session, ClusterState.Idle);
        var load = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.OperatingLive, load.TargetState);
        Assert.Equal("b", load.ScenarioId);
        Assert.Equal("b", session.LoadedScenarioName);
    }

    [Fact]
    public void LoadForLive_FromIdle_LoadsAtOnce()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);

        session.LoadForLive("a");
        var load = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.OperatingLive, load.TargetState);
        Assert.Equal("a", load.ScenarioId);
    }

    [Fact]
    public void OpenForEdit_FromEdit_UnloadsFirst_ThenLoadsForEdit()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        ClusterIs(bus, session, ClusterState.OperatingEdit);

        session.OpenForEdit("c");
        Assert.Equal(ClusterState.Idle, Assert.Single(Drain(bus)).TargetState);

        ClusterIs(bus, session, ClusterState.Idle);
        var load = Assert.Single(Drain(bus));
        Assert.Equal(ClusterState.OperatingEdit, load.TargetState);
        Assert.Equal("c", load.ScenarioId);
    }

    [Fact]
    public void NewExercise_DropsAWaitingLoad()
    {
        var bus = new FdpEventBus();
        var session = NewSession(bus);
        ClusterIs(bus, session, ClusterState.OperatingLive);

        session.LoadForLive("b");
        session.NewExercise();
        Drain(bus);

        ClusterIs(bus, session, ClusterState.Idle);
        Assert.Empty(Drain(bus));
    }

    /// <summary>⭐ <c>CE-1025</c> (R-1031) — every load request (and New Exercise) leaves the host's dry run FIRST, before anything is asked
    /// of the cluster. 🔴 Red-proof: drop the <c>_leaveRun</c> call in <c>LoadForLive</c> ⇒ red.</summary>
    [Fact]
    public void CE1025_EveryLoad_LeavesTheDryRunFirst()
    {
        var bus = new FdpEventBus();
        int left = 0;
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        var session = new EditorScenarioSession(fileService, bus, new EntityRepository(),
            () => Path.Combine(Path.GetTempPath(), "hrot-scnroot-should-not-be-written"), leaveRun: () => left++);

        session.LoadForLive("b");
        Assert.Equal(1, left);
        session.OpenForEdit("c");
        Assert.Equal(2, left);
        session.NewExercise();
        Assert.Equal(3, left);
        session.LoadForLive(" ");                                              // no scenario, no load: the run is not touched
        Assert.Equal(3, left);
    }
}
