using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Common.Infrastructure;
using Hrot.Editor.AiShared.Rules;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Rules;

/// <summary>
/// ⭐⭐ R-6 / CE-3418 (AQ108 §0 D32, story ⑩; LC-18; PH-8) — <b>Player seats open their OWN summary the moment the exercise ends</b>: the transition into <c>LiveEnded</c> (the run choice's derived fact, Live + the replicated result's
/// phase Ended — not a second query), once per end, the user may close it. WHICH window: the scenario's PROFILE = the node's feature set (the HUD's own <c>TrainingProfile</c> / <c>GameProfile</c>) → the trainee debrief under training,
/// the game summary under game; a window with nothing to say is not opened beside one that has; when neither has content the first profile-on window opens (its why-not shows).
/// 🔴 Mutations: the observer's <c>HasEnded</c> ⇒ false (nothing opens) · the profile ignored (both always) ⇒ the profile rows · the rising-edge guard dropped ⇒ the "once" row · <c>ended</c> read from the phase instead of <c>LiveEnded</c> ⇒ the "not in Live" row.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class ThePlayersSummaryOpensAtTheEndTests
{
    private static readonly Guid Run = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private sealed class Rig
    {
        public readonly EntityRepository World = new();
        public readonly FdpEventBus Bus = new();
        public readonly ClusterView View;
        public readonly ClusterTimeTransportAdapter Facade;
        public readonly RulesBundle Bundle;
        public readonly Fdp.Presentation.WindowManager.WindowManager Windows = new(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));
        private Entity _result; private int _next = 100;

        public Rig(ClusterState state, Func<string, bool>? featureOn, bool game)
        {
            World.RegisterComponent<ExerciseResult>(); World.RegisterComponent<NetworkIdentity>(); World.RegisterComponent<EntityInfo>(); World.RegisterComponent<ObjectiveStatus>(); World.RegisterComponent<ObjectiveLinks>();
            _result = World.CreateEntity(); World.AddComponent(_result, new ExerciseResult { Phase = ExercisePhase.Running });
            if (game) GameScore();
            View = new ClusterView(Bus);
            Facade = new ClusterTimeTransportAdapter(Bus, () => View, () => 5);
            Facade.AttachRunChoice(null);
            Hear(state);
            Bundle = new RulesBundle(new RulesHostServices(World: () => World, TimelineSource: () => null, Cursor: () => null, IdPrefix: "p_", Perspective: "Player", Colour: System.Numerics.Vector4.One,
                SelectEntity: _ => { }, ShowOnMap: _ => { }, Seat: () => Seat.Player, TimeTransport: () => Facade, TraineeOnly: true, FeatureOn: featureOn));
            Bundle.RegisterInto(new Fdp.Toolkit.Runner.UiBundleContext(Windows));
        }

        public void Hear(ClusterState state)
        {
            Bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ExerciseId = (Fdp.Core.ExerciseId)Run });
            Bus.SwapBuffers(); View.Update(); Facade.Update(); Bus.SwapBuffers();
        }

        public void Frame() { Bus.SwapBuffers(); View.Update(); Facade.Update(); Bus.SwapBuffers(); Bundle.WatchTheRun(); }
        public void SetPhase(ExercisePhase p) => World.GetComponentRW<ExerciseResult>(_result).Phase = p;

        private int Man(string name, ForceId side)
        {
            var e = World.CreateEntity(); int net = _next++;
            World.AddComponent(e, new NetworkIdentity(net)); World.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
            return net;
        }
        private int Rule(string title, string kind, ObjectiveRole role, int parent, int subject, float value = 0, bool scored = true)
        {
            var e = World.CreateEntity(); int id = _next++;
            World.AddComponent(e, new NetworkIdentity(id));
            World.AddComponent(e, new ObjectiveStatus { Title = new FixedString64(title), Kind = new FixedString32(kind), Role = role, State = ObjectiveState.Active, Value = value, Scored = (byte)(scored ? 1 : 0) });
            World.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
            return id;
        }
        /// <summary>A game scoreboard (a team root with one man's Points) — the game summary has content; the trainee debrief (no standards) has none.</summary>
        private void GameScore()
        {
            int blue = Man("Blue", ForceId.Friend), hawk = Man("Hawk", ForceId.Friend);
            int team = Rule("Blue score", "MemberEvaluation", ObjectiveRole.Objective, 0, blue, 100);
            int pts = Rule("Score", "Points", ObjectiveRole.Measure, team, hawk, 100);
            Rule("Kills", "Count", ObjectiveRole.Measure, pts, hawk, 1);
        }
    }

    private static readonly Func<string, bool> AllOn = _ => true;
    private static Func<string, bool> Only(bool training, bool game) => id =>
        id.StartsWith("game.") ? game : id is HudFeatures.Objectives or HudFeatures.OwnEvaluation ? training : id != HudFeatures.KillFeed || true;

    /// <summary>The Player registers ONLY its own windows (the trainee debrief and the game summary) and the run's ender becomes an observer that cannot end anything.</summary>
    [Fact]
    public void ThePlayersSet_IsHisTwoSummaries_AndHisEnderOnlyObserves()
    {
        var r = new Rig(ClusterState.OperatingLive, AllOn, game: true);
        Assert.True(r.Windows.TryGetWindow("p_trainee_debrief", out _));
        Assert.True(r.Windows.TryGetWindow("p_game_summary", out _));
        foreach (var id in new[] { "p_rules", "p_mission_summary", "p_game_master" }) Assert.False(r.Windows.TryGetWindow(id, out _), id);
        r.Frame();
        Assert.IsType<ExerciseObserver>(r.Facade.Ender);
        Assert.False(r.Facade.Ender!.CanEnd); Assert.False(r.Facade.Ender.End());
    }

    /// <summary>The end opens the game summary under the game profile (it has the content), once; the trainee debrief (nothing to say) stays shut; closing it keeps it closed.
    /// 🔴 Mutations: observer <c>HasEnded</c> ⇒ false ⇒ red at the first open; rising edge dropped ⇒ the count row.</summary>
    [Fact]
    public void R6_TheEnd_OpensTheProfilesSummary_Once_AndTheUserMayCloseIt()
    {
        var r = new Rig(ClusterState.OperatingLive, AllOn, game: true);
        for (int i = 0; i < 3; i++) r.Frame();
        Assert.False(r.Bundle.GameSummary.IsOpen); Assert.False(r.Bundle.TraineeDebrief.IsOpen); Assert.Equal(0, r.Bundle.SummaryOpenings);   // running: nothing
        r.SetPhase(ExercisePhase.Ended);
        for (int i = 0; i < 5; i++) r.Frame();
        Assert.True(r.Facade.RunChoice!.LiveEnded);                          // the ONE derived fact
        Assert.True(r.Bundle.GameSummary.IsOpen);
        Assert.False(r.Bundle.TraineeDebrief.IsOpen);                        // no standards: not opened beside the one that has content
        Assert.Equal(1, r.Bundle.SummaryOpenings);                           // once per end, not every frame
        r.Bundle.GameSummary.IsOpen = false;
        for (int i = 0; i < 5; i++) r.Frame();
        Assert.False(r.Bundle.GameSummary.IsOpen);                           // the user closed it: it stays closed
        Assert.Equal(1, r.Bundle.SummaryOpenings);
    }

    /// <summary>The profile is the node's feature set (PH-8): training only ⇒ the debrief (why-not shown, no content anywhere); game off ⇒ no game summary even with a scoreboard; both off ⇒ nothing.
    /// 🔴 Mutation: the profile ignored ⇒ the game summary opens in the training-only row.</summary>
    [Fact]
    public void R6_TheProfileIsTheFeatureSet()
    {
        var training = new Rig(ClusterState.OperatingLive, Only(training: true, game: false), game: true);
        training.SetPhase(ExercisePhase.Ended); for (int i = 0; i < 3; i++) training.Frame();
        Assert.True(training.Bundle.TraineeDebrief.IsOpen); Assert.False(training.Bundle.GameSummary.IsOpen);

        var game = new Rig(ClusterState.OperatingLive, Only(training: false, game: true), game: false);
        game.SetPhase(ExercisePhase.Ended); for (int i = 0; i < 3; i++) game.Frame();
        Assert.True(game.Bundle.GameSummary.IsOpen); Assert.False(game.Bundle.TraineeDebrief.IsOpen);   // game profile, nothing to say: its why-not shows

        var none = new Rig(ClusterState.OperatingLive, Only(training: false, game: false), game: true);
        none.SetPhase(ExercisePhase.Ended); for (int i = 0; i < 3; i++) none.Frame();
        Assert.False(none.Bundle.TraineeDebrief.IsOpen); Assert.False(none.Bundle.GameSummary.IsOpen); Assert.Equal(0, none.Bundle.SummaryOpenings);
    }

    /// <summary>It follows <c>LiveEnded</c>, not the bare phase: a result left Ended in a cluster that is not in Live (an old world, Edit) opens nothing. 🔴 Mutation: read the phase ⇒ red.</summary>
    [Fact]
    public void R6_ItReadsLiveEnded_NotTheBarePhase()
    {
        var r = new Rig(ClusterState.OperatingEdit, AllOn, game: true);
        r.SetPhase(ExercisePhase.Ended);
        for (int i = 0; i < 5; i++) r.Frame();
        Assert.False(r.Facade.RunChoice!.LiveEnded);
        Assert.False(r.Bundle.GameSummary.IsOpen); Assert.False(r.Bundle.TraineeDebrief.IsOpen); Assert.Equal(0, r.Bundle.SummaryOpenings);
    }
}
