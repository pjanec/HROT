using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Core.Tkb;
using Fdp.Toolkit.Behavior;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.MuscleCharacter.Animation.Translators
{
    /// <summary>
    /// TKB entity translator for animation descriptors (DD-4 §4, DD-1 §5.3).
    /// Projects CharacterAnimationDefDto into replicated and Muscle-internal components
    /// during ghost promotion. Manages per-class baked data cache and hot-reload invalidation.
    /// </summary>
    public sealed class AnimationTkbTranslator : ITkbEntityTranslator, IDisposable
    {
        /// <summary>
        /// Per-class baked animation cache, keyed by class ID.
        /// </summary>
        private readonly BakedAnimationCache _cache;

        /// <summary>
        /// Initialize the translator with hot-reload support.
        /// </summary>
        /// <param name="hotReloadEvents">Hot-reload event service (can be null if not available).</param>
        public AnimationTkbTranslator(ITkbHotReloadEvents? hotReloadEvents)
        {
            _cache = new BakedAnimationCache(hotReloadEvents);
        }

        /// <summary>
        /// Report the descriptor types consumed by this translator.
        /// </summary>
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(CharacterAnimationDefDto);
        }

        /// <summary>
        /// Project animation descriptor onto a promoted entity.
        /// Injects replicated channel components, stance components, queue components,
        /// and internal executor state components. All injections are guarded by
        /// IsComponentTypeRegistered checks per DD-4 §4.2.
        /// </summary>
        /// <param name="repo">Entity repository.</param>
        /// <param name="entity">Target entity being promoted.</param>
        /// <param name="template">TKB template carrying the descriptor.</param>
        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            if (repo == null)
                throw new ArgumentNullException(nameof(repo));

            if (template == null)
                throw new ArgumentNullException(nameof(template));

            // Try to get the animation descriptor from the template
            var def = template.GetDescriptor<CharacterAnimationDefDto>();
            if (def == null)
                return;  // Template doesn't have animation data; non-humanoid or disabled

            // --- Replicated/contractual components (DD-1 §5.1) ---

            if (repo.IsComponentTypeRegistered<AnimationChannel>())
            {
                repo.AddComponent(entity, new AnimationChannel
                {
                    ActiveAction = 0,
                    BehaviorInstanceId = 0,
                    ActionInstanceId = 0,
                    DispatchedInstanceId = 0,
                    Status = NodeStatus.Failure,
                });
            }

            // LookAtChannel only if AimConfig is present
            if (repo.IsComponentTypeRegistered<LookAtChannel>() && def.AimConfig != null)
            {
                repo.AddComponent(entity, new LookAtChannel
                {
                    ActiveAction = 0,
                    BehaviorInstanceId = 0,
                    ActionInstanceId = 0,
                    DispatchedInstanceId = 0,
                    Status = NodeStatus.Failure,
                });
            }

            // Stance components
            if (repo.IsComponentTypeRegistered<StanceIntent>())
            {
                repo.AddComponent(entity, new StanceIntent
                {
                    TargetStance = (StanceId)def.SupportedStances[0],  // Default to first declared stance
                    BlendTime = 0.3f,
                    Version = 0,
                });
            }

            if (repo.IsComponentTypeRegistered<StanceStatus>())
            {
                repo.AddComponent(entity, new StanceStatus
                {
                    CurrentStance = (StanceId)def.SupportedStances[0],
                    Phase = StanceTransitionPhase.Idle,
                    TransitionProgress = 0f,
                    AckVersion = 0,
                });
            }

            // Montage queue components
            if (repo.IsComponentTypeRegistered<AnimationMontageQueue>())
            {
                repo.AddComponent(entity, default(AnimationMontageQueue));
            }

            if (repo.IsComponentTypeRegistered<AnimationMontageQueueState>())
            {
                repo.AddComponent(entity, new AnimationMontageQueueState
                {
                    CurrentEntryIndex = 0xFF,
                    EntryElapsedSeconds = 0f,
                    InBlendOutWindow = false,
                    ObservedQueueVersion = 0,
                });
            }

            // --- Runtime-only components (DD-1 §5.2) ---

            if (repo.IsComponentTypeRegistered<CharacterAnimationDefRuntime>())
            {
                // Bake the DTO into runtime-friendly form using the cache
                var templateId = template.TkbType;  // Use TKB template ID as class key
                var bakedData = _cache.GetOrBake(templateId, def);

                // BackendHandle is used to identify which baked data to use
                // For now, use the templateId as the handle
                repo.AddComponent(entity, new CharacterAnimationDefRuntime
                {
                    BackendHandle = templateId,
                    StanceCount = (byte)def.SupportedStances.Count,
                    SlotCount = (byte)def.Slots.Count,
                });
            }

            if (repo.IsComponentTypeRegistered<AnimationExecutorState>())
            {
                repo.AddComponent(entity, default(AnimationExecutorState));
            }

            // LookAtExecutorState only if AimConfig is present
            if (repo.IsComponentTypeRegistered<LookAtExecutorState>() && def.AimConfig != null)
            {
                repo.AddComponent(entity, default(LookAtExecutorState));
            }
        }

        /// <summary>
        /// Dispose the translator and release resources.
        /// </summary>
        public void Dispose()
        {
            _cache?.Dispose();
        }
    }
}
