using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Executors;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// Bridges ECS component state to the IAnimationBackend (ANC-P3-05, DD-1 §10, §17).
    /// Runs in Simulation (mid, after MontageQueueAdvanceSystem).
    /// First tick per entity: registers with backend, stores real backend handle.
    /// Per-tick: applies staged executor state, calls backend.Tick.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class AnimationRuntimeBridgeSystem : IEcsModuleSystem
    {
        private readonly IAnimationBackend _backend;
        private readonly BakedAnimationCache _cache;

        // Which entities THIS process's backend has registered — derived (R-3039 S1, docs/DESIGN_System_State_Inventory.md §6 #15): an empty
        // set (a fresh process, a checkpoint restored into it) registers each character again, under its class (ClassOf).
        private readonly HashSet<ulong> _registered = new();

        /// <summary>
        /// ⭐ <c>R-3039</c> S1 — the class a character registers under. <c>BackendHandle</c> holds the TKB template id (the translator's)
        /// until a registration encodes the backend handle into it (generation ≥ 1 in the high 32 bits — the rule the reporters read). A
        /// handle encoded by an EARLIER process (a checkpoint restored into a fresh one) names no class, so the class is the entity's own
        /// <c>TkbIdentity.TkbType</c> — the same template id the translator used. ⛔ Never the encoded handle read as a class.
        /// </summary>
        public static long ClassOf(EntityRepository repo, Entity entity, in CharacterAnimationDefRuntime def)
        {
            if ((def.BackendHandle >> 32) == 0) return def.BackendHandle;
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()
                && repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity))
                return repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity).TkbType;
            throw new InvalidOperationException(
                $"Character {entity} carries a backend handle from another process and no TkbIdentity: its animation class cannot be recovered.");
        }

        public AnimationRuntimeBridgeSystem(IAnimationBackend backend, BakedAnimationCache cache)
        {
            _backend = backend;
            _cache = cache;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(AnimationRuntimeBridgeSystem)} requires direct EntityRepository access.");

            var q = repo.Query()
                .With<CharacterAnimationDefRuntime>()
                .With<AnimationExecutorState>()
                .Build();

            foreach (var entity in q)
            {
                ref var def = ref repo.GetComponentRW<CharacterAnimationDefRuntime>(entity);

                // First tick: if BackendHandle looks like a classId (not yet a real backend handle),
                // register the entity with the backend.
                // Convention: BackendHandle is a classId when it's the raw TKB template ID (set by translator).
                // After registration, encode the AnimationBackendHandle into BackendHandle as a long.
                bool needsRegistration = !_registered.Contains(entity.PackedValue);
                if (needsRegistration)
                {
                    long classId = ClassOf(repo, entity, in def);
                    _registered.Add(entity.PackedValue);

                    var backendHandle = _backend.RegisterEntity((uint)entity.Index, classId);
                    // Encode handle into BackendHandle (Index in low 32 bits, Generation in high 32 bits)
                    def.BackendHandle = ((long)backendHandle.Generation << 32) | backendHandle.Index;
                }

                // Decode the backend handle
                var handle = new AnimationBackendHandle
                {
                    Index = (uint)(def.BackendHandle & 0xFFFFFFFF),
                    Generation = (uint)((def.BackendHandle >> 32) & 0xFFFFFFFF),
                };

                // Apply staged executor state (from PlayMontageExecutor / StopMontageExecutor)
                ApplyStagedExecutorState(entity, ref def, handle, repo);

                // Tick the backend once per frame (after all per-entity updates)
                // Note: Tick is called once for all entities in the final step below
            }

            // Tick all entities at once
            _backend.Tick(deltaTime);
        }

        private unsafe void ApplyStagedExecutorState(
            Entity entity,
            ref CharacterAnimationDefRuntime def,
            AnimationBackendHandle handle,
            EntityRepository repo)
        {
            if (!repo.HasComponent<AnimationExecutorState>(entity))
                return;

            ref var execState = ref repo.GetComponentRW<AnimationExecutorState>(entity);

            fixed (byte* ptr = execState.SlotsData)
            {
                var staged = (StagedPlayIntent*)ptr;

                if (staged->HasPendingPlay != 0)
                {
                    var p = new PlayMontageParams
                    {
                        MontageId = staged->MontageId,
                        PlayRate = staged->PlayRate,
                        BlendInTime = staged->BlendInTime,
                        BlendOutTime = staged->BlendOutTime,
                        StartSectionIndex = staged->StartSectionIndex,
                    };
                    _backend.PlayMontageOnSlot(handle, in p);
                    staged->HasPendingPlay = 0;
                }

                if (staged->HasPendingStop != 0)
                {
                    var p = new StopMontageParams { BlendOutTime = staged->StopBlendOutTime };
                    _backend.StopMontageOnSlot(handle, in p);
                    staged->HasPendingStop = 0;
                }
            }
        }
    }
}
