using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.RunMode;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ <c>CE-4097</c> — the ONE rig the run-choice rails share (<c>TheOneTimeFacadeTests</c>: the facade; <c>MainToolbarTimeControlTests</c>: the indicator, menu and
/// modal): a REAL <see cref="ClusterTimeTransportAdapter"/> on a REAL bus with a run choice attached, a fake scenario session
/// (<see cref="IRunScenarioControl"/>) and a fake unsaved-changes seam. The cluster is "heard" by publishing the master's broadcast.
/// </summary>
internal static class RunChoiceRig
{
    internal sealed class FakeControl : IRunScenarioControl
    {
        public string? LoadedScenarioName { get; set; } = "hill-attack";
        public readonly List<string> Calls = new();
        public void LoadForLive(string scenarioName) => Calls.Add($"LoadForLive:{scenarioName}");
        public void OpenForEdit(string scenarioName) => Calls.Add($"OpenForEdit:{scenarioName}");
        public void OpenRecording(Guid exerciseId) => Calls.Add($"OpenRecording:{exerciseId}");
        public void SaveCurrent() => Calls.Add("SaveCurrent");
        public void TakeCheckpoint() => Calls.Add("TakeCheckpoint");
    }

    internal sealed class FakeUnsaved : IUnsavedChangesQuery
    {
        public UnsavedAnswer Answer = UnsavedAnswer.Unchanged;
        public int Asked;
        public void Ask(Action<UnsavedAnswer> reply) { Asked++; reply(Answer); }
    }

    internal static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    internal sealed record Rig(ClusterTimeTransportAdapter F, FdpEventBus Bus, FakeControl C, FakeUnsaved Q, RunModeChoice Choice);

    /// <summary>A real adapter on a real bus with a run choice attached; the cluster is heard in <paramref name="state"/> (exercise <see cref="RunId"/>).</summary>
    internal static Rig With(ClusterState state, RunArmed hostDefault = RunArmed.Preview, UnsavedAnswer answer = UnsavedAnswer.Unchanged,
                             bool running = false, double clock = 0, bool withControl = true, string? inventoryScenario = "hill-attack",
                             bool runNotRecorded = false, bool recordLiveRunsOff = false)
    {
        var bus = new FdpEventBus();
        var facade = new ClusterTimeTransportAdapter(bus, () => clock);
        var control = new FakeControl();
        var unsaved = new FakeUnsaved { Answer = answer };
        var choice = facade.AttachRunChoice(hostDefault, withControl ? control : null, unsaved);
        var rig = new Rig(facade, bus, control, unsaved, choice);
        bus.Publish(new SwitchTimeModeEvent { TargetMode = running ? Fdp.ModuleHost.Time.TimeMode.Continuous : Fdp.ModuleHost.Time.TimeMode.Deterministic });
        if (inventoryScenario != null)
            bus.PublishManaged(new AssetInventoryUpdateEvent { LocalExercises = new[] { new ExerciseInventoryItem(RunId, DateTime.UtcNow, TimeSpan.Zero, inventoryScenario) } });
        Hear(rig, state, runNotRecorded, recordLiveRunsOff);
        return rig;
    }

    /// <summary>The cluster state changes (as the master's broadcast says), then the facade's frame runs.</summary>
    internal static void Hear(Rig rig, ClusterState state, bool runNotRecorded = false, bool recordLiveRunsOff = false)
    {
        rig.Bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ExerciseId = RunId, RunNotRecorded = runNotRecorded, RecordLiveRunsOff = recordLiveRunsOff });
        rig.Bus.SwapBuffers();
        rig.F.Update();
        rig.Bus.SwapBuffers();
    }

    /// <summary>The resume intents the facade published DURING <see cref="Hear"/>'s frame (its closing swap already exposed them — no second swap).</summary>
    internal static List<ResumeTimeIntent> Resumes(Rig rig) => rig.Bus.ReadManaged<ResumeTimeIntent>().ToList();

    /// <summary>Everything of type <typeparamref name="T"/> the facade published since the last look.</summary>
    internal static List<T> Sent<T>(Rig rig) { rig.Bus.SwapBuffers(); return rig.Bus.ReadManaged<T>().ToList(); }
}
