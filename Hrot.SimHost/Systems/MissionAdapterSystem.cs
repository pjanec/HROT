using System;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Behavior.Events;
using Hrot.DDS.DataModel;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Legacy adapter that keeps <see cref="DoctrineState"/> aligned with the current
    /// <see cref="MissionPlanQueue"/> phase.
    ///
    /// <para>
    /// When the active phase changes, this system performs two actions:
    /// <list type="number">
    ///   <item>
    ///     <b>Immediate blackboard update:</b> calls <see cref="DoctrineDefinition.ParseParams"/>
    ///     directly so that <see cref="BrainBlackboard"/> contains the correct JSON-parsed
    ///     parameters on the very same frame — independently of how many
    ///     <c>Bus.SwapBuffers()</c> calls the host application performs per tick.
    ///   </item>
    ///   <item>
    ///     <b>Event publication:</b> publishes an <see cref="AssignDoctrineEvent"/> so that
    ///     <see cref="FDP.Toolkit.Behavior.Systems.DoctrineIngressSystem"/> can reset the
    ///     <see cref="BrainBTreeState"/> execution pointer and set
    ///     <c>DoctrineState.BrainTier</c> when the event is consumed on the next frame.
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class MissionAdapterSystem : ComponentSystem
    {
        private readonly DoctrineRegistry _doctrineRegistry;
        private readonly NetworkEntityMap _entityMap;

        public MissionAdapterSystem(DoctrineRegistry doctrineRegistry, NetworkEntityMap entityMap)
        {
            _doctrineRegistry = doctrineRegistry ?? throw new ArgumentNullException(nameof(doctrineRegistry));
            _entityMap        = entityMap        ?? throw new ArgumentNullException(nameof(entityMap));
        }

        protected override unsafe void OnUpdate()
        {
            var query = World.Query()
                .With<MissionPlanQueue>()
                .With<DoctrineState>()
                .Build();

            foreach (var entity in query)
            {
                ref var queue = ref World.GetComponentRW<MissionPlanQueue>(entity);
                ref var doctrine = ref World.GetComponentRW<DoctrineState>(entity);

                if (!World.HasComponent<Hrot.SimHost.Components.MissionAdapterState>(entity))
                    World.AddComponent(entity, new Hrot.SimHost.Components.MissionAdapterState { LastPhase = byte.MaxValue });
                    
                ref var adapterState = ref World.GetComponentRW<Hrot.SimHost.Components.MissionAdapterState>(entity);
                
                var activePlan = World.GetComponent<ActiveMissionPlan>(entity);
                
                // Nothing to do if we are past the end of the queue
                if (queue.CurrentPhase >= queue.PhaseCount)
                    continue;

                Span<MissionPhase> phases = queue.Phases;
                var phase = phases[queue.CurrentPhase];

                string jsonParams = "{}";
                
                if (activePlan?.Plan?.Tasks != null && queue.CurrentPhase < activePlan.Plan.Tasks.Count)
                {
                    var task = activePlan.Plan.Tasks[queue.CurrentPhase];
                    jsonParams = task.BehaviorParams ?? "{}";
                }
                
                uint currentDefHash = (uint)(jsonParams.GetHashCode() ^ phase.DoctrineId);

                if (adapterState.LastPhase == queue.CurrentPhase && adapterState.LastPlanVersion == currentDefHash)
                    continue;

                adapterState.LastPhase = queue.CurrentPhase;
                adapterState.LastPlanVersion = currentDefHash;

                var defName = "Idle";
                if (_doctrineRegistry.TryGetDefinition(phase.DoctrineId, out var def))
                    defName = def.Name;

                // ── Direct blackboard update ───────────────────────────────────────────────
                // Parse the JSON params directly into BrainBlackboard so BTreeTickSystem
                // has the correct data on the SAME frame — without depending on the number
                // of Bus.SwapBuffers() calls the host performs between simulation steps.
                // (DoctrineIngressSystem will still reset BrainBTreeState.State when the
                // AssignDoctrineEvent is consumed on the next frame, which is fine.)
                bool bbOk = false;
                if (def?.ParseParams != null && World.HasComponent<BrainBlackboard>(entity))
                {
                    ref var bb = ref World.GetComponentRW<BrainBlackboard>(entity);
                    fixed (byte* ptr = bb.Memory)
                    {
                        try { def.ParseParams(jsonParams, ptr); bbOk = true; }
                        catch { /* suppress malformed JSON — entity stays on previous params */ }
                    }
                }
                else if (def?.ParseParams == null)
                {
                    bbOk = true; // No params required — doctrine can be applied as-is.
                }

                // ── Direct DoctrineState update ───────────────────────────────────────────
                // Apply the doctrine transition synchronously so BTreeTickSystem can begin
                // execution on the NEXT simulation tick without waiting for AssignDoctrineEvent
                // to survive through the Bus.SwapBuffers() calls that the test harness (and
                // production runners) perform between lifecycle and simulation phases.
                // DoctrineIngressSystem still receives the AssignDoctrineEvent published below
                // and will re-apply the same values (idempotent for activeDoctrineHash/BrainTier;
                // InstanceId will be incremented again, which is harmless).
                if (bbOk && def != null)
                {
                    doctrine.ActiveDoctrineHash = phase.DoctrineId;
                    unchecked { doctrine.InstanceId++; }
                    doctrine.BrainTier = def.BrainTier;

                    if (World.HasComponent<BrainBTreeState>(entity))
                    {
                        ref var btState = ref World.GetComponentRW<BrainBTreeState>(entity);
                        btState.State = default;
                    }
                }

                // Also publish AssignDoctrineEvent so DoctrineIngressSystem can re-apply
                // the transition on the next tick (production pipeline compatibility).
                World.Bus.PublishManaged(new AssignDoctrineEvent 
                {
                    Entity = entity,
                    DoctrineName = defName,
                    JsonParams = jsonParams
                });
            }
        }
    }
}
