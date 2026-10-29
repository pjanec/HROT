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
    internal sealed class FakeControl : IRunScenarioControl, ISaveInterception
    {
        public Func<bool>? SaveIntercept { get; set; }
        public string? LoadedScenarioName { get; set; } = "hill-attack";
        public readonly List<string> Calls = new();
        public void LoadForLive(string scenarioName) => Calls.Add($"LoadForLive:{scenarioName}");
        public void OpenForEdit(string scenarioName) => Calls.Add($"OpenForEdit:{scenarioName}");
        public void OpenRecording(Guid exerciseId) => Calls.Add($"OpenRecording:{exerciseId}");
        public void OpenRecording(Guid exerciseId, double? atSimSeconds) => Calls.Add($"OpenRecording:{exerciseId}@{atSimSeconds?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
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

    /// <summary>⭐ CE-4097 part two — what a host's cluster read model answers about the check rounds: observes or not, and the outcome per request id (filled by the test).</summary>
    internal sealed class FakeReads
    {
        public bool Observes = true;
        public readonly Dictionary<Guid, UnsavedCheckOutcome> Outcomes = new();
        public UnsavedCheckReads Reads => new(() => Observes, id => Outcomes.TryGetValue(id, out var o) ? o : null);
        /// <summary>The master "answers" the round the facade published last (<paramref name="changed"/> or, with null, a refusal).</summary>
        public void Answer(IEnumerable<CheckUnsavedIntent> asked, bool? changed)
        {
            foreach (var i in asked)
                Outcomes[i.RequestId] = changed is { } c
                    ? new UnsavedCheckOutcome(new UnsavedCheckResult(c, c ? new[] { 1 } : System.Array.Empty<int>()), null)
                    : new UnsavedCheckOutcome(null, UnsavedCheckOutcome.NotEditingReason);
        }
    }

    internal sealed record CheckRig(ClusterTimeTransportAdapter F, FdpEventBus Bus, FakeControl C, RunModeChoice Choice, FakeReads R);

    /// <summary>⭐ CE-4097 part two — a REAL adapter on a real bus whose run choice asks the production <see cref="ClusterUnsavedChangesQuery"/> (the master's check round),
    /// reading its answer through <see cref="FakeReads"/>; the cluster is heard in <paramref name="state"/>.</summary>
    internal static CheckRig WithCheckRound(ClusterState state, RunArmed hostDefault = RunArmed.Live, string? loadedName = "hill-attack", bool observes = true)
    {
        var bus = new FdpEventBus();
        var facade = new ClusterTimeTransportAdapter(bus, () => 0);
        var control = new FakeControl { LoadedScenarioName = loadedName };
        var reads = new FakeReads { Observes = observes };
        var choice = facade.AttachRunChoice(hostDefault, control, unsavedReads: reads.Reads);
        bus.PublishManaged(new AssetInventoryUpdateEvent { LocalExercises = new[] { new ExerciseInventoryItem(RunId, DateTime.UtcNow, TimeSpan.Zero, "hill-attack") } });
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ExerciseId = RunId });
        bus.SwapBuffers(); facade.Update(); bus.SwapBuffers();
        return new CheckRig(facade, bus, control, choice, reads);
    }

    /// <summary>One frame of the facade (<c>Update</c> between swaps), as the host runs it — delivers a check answer that has arrived.</summary>
    internal static void Frame(CheckRig rig) { rig.Bus.SwapBuffers(); rig.F.Update(); rig.Bus.SwapBuffers(); }

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
    internal static List<T> Sent<T>(Rig rig) => Sent<T>(rig.Bus);
    internal static List<T> Sent<T>(CheckRig rig) => Sent<T>(rig.Bus);
    internal static List<T> Sent<T>(FdpEventBus bus) { bus.SwapBuffers(); return bus.ReadManaged<T>().ToList(); }
}
