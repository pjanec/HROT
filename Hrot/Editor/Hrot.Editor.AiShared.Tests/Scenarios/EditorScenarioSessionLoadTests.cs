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
}
