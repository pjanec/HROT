using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Perception.Survey
{
    /// <summary>
    /// ⭐ <c>CE-3249</c> S3 — the brain's half of the <c>WorldSurvey</c> sensor: APPLY each answer (<see cref="WorldSurveyResultEvent"/>,
    /// epoch-checked) into the child's <see cref="WorldSurveyStatus"/> + <see cref="WorldSurveyAnswer"/>, and announce it — a
    /// <see cref="SensorChangedEvent"/> <see cref="SensorChange.SurveyReady"/> on the first answer of an epoch and whenever the content changed
    /// (WS-10). A repeat of the same content only refreshes the age (the heartbeat, WS-7). 📄 docs/DESIGN_World_Survey_Sensor.md §3.
    /// </summary>
    [SingleInstance]
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class WorldSurveyApplySystem : IEcsModuleSystem
    {
        /// <summary>Answers dropped for an old epoch (a re-pointed sensor) since this system was built (diagnostics / rails).</summary>
        public int StaleEpochCount { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<WorldSurveyStatus>() || !repo.IsComponentTypeRegistered<EqsSensor>()) return;
            if (!repo.Bus.IsRegisteredManaged<WorldSurveyResultEvent>()) return;
            bool announce = repo.Bus.IsRegistered<SensorChangedEvent>();
            float now = (float)view.Time;

            foreach (var evt in repo.Bus.ReadManaged<WorldSurveyResultEvent>())
            {
                if (evt is null) continue;
                var child = evt.Observer.IsNull ? Resolve(repo, evt) : evt.Observer;
                if (child.IsNull || !repo.IsAlive(child)) continue;
                if (!repo.HasComponent<EqsSensor>(child) || !repo.HasComponent<WorldSurveyStatus>(child)) continue;
                if (evt.Epoch != repo.GetComponentRO<EqsSensor>(child).Epoch) { StaleEpochCount++; continue; }   // for older inputs

                ref var status = ref repo.GetComponentRW<WorldSurveyStatus>(child);
                bool first = status.LastUpdateTick == 0;
                bool changed = evt.ContentHash != status.ContentHash;
                if (first || changed)
                {
                    repo.SetManagedComponent(child, new WorldSurveyAnswer { Payload = WorldSurveyRequestJson.DecodeAnswer(evt.PayloadJson) });
                }
                if (changed) status.Version++;
                status.ContentHash = evt.ContentHash;
                status.LastUpdateTick = evt.RefreshTick != 0 ? evt.RefreshTick : 1u;
                status.LastUpdateTimeSeconds = now;

                if ((first || changed) && announce)
                {
                    var unit = repo.HasComponent<PartMetadata>(child) ? repo.GetComponentRO<PartMetadata>(child).ParentEntity : Entity.Null;
                    repo.Bus.Publish(new SensorChangedEvent
                    {
                        Unit = unit, Sensor = child, Target = Entity.Null, Kind = SensorModality.WorldSurvey, What = SensorChange.SurveyReady,
                    });
                }
            }
        }

        // The ONE wire-key rule (EqsSensorKey), as the danger-area sensor's local path matches.
        private static Entity Resolve(EntityRepository repo, WorldSurveyResultEvent evt)
        {
            foreach (var candidate in repo.Query().With<EqsSensor>().With<WorldSurveyStatus>().Build())
            {
                if (evt.ParentNetworkId == 0)
                {
                    if (candidate.Index == evt.LocalChildIndex) return candidate;
                    continue;
                }
                if (EqsSensorKey.Resolve(repo, candidate, out long net, out int index, out _) is EqsSensorKeyKind.Child or EqsSensorKeyKind.Legacy
                    && net == evt.ParentNetworkId && index == evt.LocalChildIndex)
                    return candidate;
            }
            return Entity.Null;
        }
    }
}
