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

        /// <summary>⭐ The node's APPLICATION bus (<c>HrotNodeContext.EventBus</c>) — the one the cluster-op egress reads
        /// (<c>ClusterOpEgressTranslator</c>, built from it at <c>NodeBootstrapper.cs:288</c>). ⛔ NOT the world's <c>repo.Bus</c>: the node builds a
        /// SEPARATE bus (<c>HrotNodeBuilder.cs:143</c>), so a <c>PauseTimeIntent</c> on the world bus never leaves the node — measured live on
        /// <c>sr-triggers-march</c> (2026-11-02: the end ran, the cluster kept running). Null ⇒ the world bus (a single-bus test world).</summary>
        private readonly FdpEventBus? _nodeBus;

        public ExercisePauseOnEndSystem(FdpEventBus? nodeBus = null) => _nodeBus = nodeBus;

        /// <summary>The bus the pause goes out on (the forwarding rail reads it).</summary>
        public FdpEventBus? NodeBus => _nodeBus;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.Bus.IsRegistered<ExerciseEndedEvent>()) return;
            int ended = repo.ReadEvents<ExerciseEndedEvent>().Length;
            if (ended == 0) return;
            var bus = _nodeBus ?? repo.Bus;
            if (!bus.IsRegisteredManaged<PauseTimeIntent>()) bus.RegisterManaged<PauseTimeIntent>();
            bus.PublishManaged(new PauseTimeIntent());   // once per frame that carries an end — however many events (the event is raised once per node anyway)
        }
    }
}
