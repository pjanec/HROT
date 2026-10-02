using System;
using System.Collections.Concurrent;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Core.Tkb;

namespace Hrot.MuscleCharacter.Animation.Baking
{
    /// <summary>
    /// Per-class baked animation cache with hot-reload support (DD-4 §4.1, §7, §9.1).
    /// Owned by AnimationTkbTranslator to share baked data across all entities of the same class.
    /// Subscribes to ITkbHotReloadEvents to invalidate entries when descriptors change.
    /// </summary>
    public sealed class BakedAnimationCache : IDisposable
    {
        /// <summary>
        /// Per-class cache keyed by class ID (template ID or template-name hash).
        /// </summary>
        private readonly ConcurrentDictionary<long, CharacterAnimationBakedData> _cache =
            new ConcurrentDictionary<long, CharacterAnimationBakedData>();

        /// <summary>
        /// Hot-reload events service.
        /// </summary>
        private readonly ITkbHotReloadEvents? _hotReloadEvents;

        /// <summary>
        /// Subscription to hot-reload events (disposed on Dispose).
        /// </summary>
        private IDisposable? _subscription;

        /// <summary>
        /// Initialize the cache with hot-reload subscription.
        /// </summary>
        /// <param name="hotReloadEvents">Hot-reload events service (can be null if hot reload not supported).</param>
        public BakedAnimationCache(ITkbHotReloadEvents? hotReloadEvents)
        {
            _hotReloadEvents = hotReloadEvents;
            if (hotReloadEvents != null)
            {
                _subscription = hotReloadEvents.Subscribe(OnDescriptorChanged);
            }
        }

        /// <summary>
        /// Get or bake the animation data for a character class.
        /// If already cached, returns the cached entry.
        /// If not cached, bakes the DTO and stores it for future reuse.
        /// </summary>
        /// <param name="classId">Character class ID (template ID).</param>
        /// <param name="dto">Animation descriptor DTO.</param>
        /// <returns>Baked animation data (cached or newly baked).</returns>
        public CharacterAnimationBakedData GetOrBake(long classId, CharacterAnimationDefDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            return _cache.GetOrAdd(classId, _ => BakingUtils.BakeDef(dto));
        }

        /// <summary>
        /// Try to retrieve cached baked data by class ID.
        /// </summary>
        /// <param name="classId">Character class ID.</param>
        /// <param name="bakedData">Cached data if found.</param>
        /// <returns>True if data was cached; false otherwise.</returns>
        public bool TryGetCached(long classId, out CharacterAnimationBakedData? bakedData)
        {
            return _cache.TryGetValue(classId, out bakedData);
        }

        /// <summary>
        /// Called when a TKB descriptor is hot-reloaded.
        /// If the descriptor is "Anim.CharacterDef" and we have cached data for the changed class,
        /// evict the cached entry so the next promotion re-bakes.
        /// </summary>
        private void OnDescriptorChanged(TkbDescriptorChangedEvent evt)
        {
            // Only react to animation descriptor changes
            if (evt.DescriptorName != "Anim.CharacterDef")
                return;

            // Evict the cache entry for this class so the next entity of this class re-bakes
            _cache.TryRemove(evt.ClassId, out _);
        }

        /// <summary>
        /// Dispose and unsubscribe from hot-reload events.
        /// </summary>
        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
            _cache.Clear();
        }
    }
}
