using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Time.Domain;
using Hrot.Common.Infrastructure;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S2 ⑨ (📄 docs/DESIGN_Scenario_Rules.md §00.9 S2-e) — pause-on-end: the exercise ended (<see cref="ExerciseEndedEvent"/>), so ask the cluster to pause —
    /// <b>once per Ended</b>, never a toggle: one <see cref="PauseTimeIntent"/> on the node's bus, the cluster's ONE pause request (the same intent
    /// <c>ClusterTimeTransportAdapter.TogglePlayPause</c> and <c>CgfClusterDebugTimeController.RequestPause</c> publish; <c>ClusterOpEgressTranslator</c> forwards it to the master).
    /// Switchable: <c>[NodeFeature("exercise.pause-on-end")]</c>, ON by default (<see cref="Feature"/>) — an instructor who wants the world to run on after the end turns it off.
    /// Registered by <c>CgfLogicPack</c> (every Brain node and the editor — where the objectives run and <c>EndExercise</c> raises the event).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    [NodeFeature(FeatureId)]
    public sealed class ExercisePauseOnEndSystem : IEcsModuleSystem
    {
        public const string FeatureId = "exercise.pause-on-end";

        /// <summary>The declaration both Brain capabilities (CGF and the editor) put in their <c>Features</c> — declared ⇔ available (F-3), exactly once per node.</summary>
        public static readonly FeatureDeclaration Feature = FeatureDeclaration.Switch(FeatureId,
            "The exercise ends (ExerciseEnded) ⇒ the cluster is asked to pause (once)", FeatureChange.Live, _ => true);

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.Bus.IsRegistered<ExerciseEndedEvent>()) return;
            int ended = repo.ReadEvents<ExerciseEndedEvent>().Length;
            if (ended == 0) return;
            if (!repo.Bus.IsRegisteredManaged<PauseTimeIntent>()) repo.Bus.RegisterManaged<PauseTimeIntent>();
            repo.Bus.PublishManaged(new PauseTimeIntent());   // once per frame that carries an end — however many events (the event is raised once per node anyway)
        }
    }
}
