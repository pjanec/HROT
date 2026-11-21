using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Hashing;
using Fdp.Interfaces;

namespace Hrot.Editor.AiShared.Catalog
{
    /// <summary>
    /// Implementation of animation TKB query API (DD-4 §5, §9.6).
    /// Provides efficient design-time access to animation metadata from CharacterAnimationDefDto.
    /// </summary>
    internal sealed class AnimationTkbQueries : IAnimationTkbQueries
    {
        /// <summary>
        /// TKB database service for descriptor lookup.
        /// </summary>
        private readonly ITkbDatabase _db;

        /// <summary>
        /// Per-class query result cache (entityClass → query type → result).
        /// Cached aggressively because TKB data is immutable between hot-reload events.
        /// </summary>
        private readonly Dictionary<(string, string), object?> _queryCache =
            new Dictionary<(string, string), object?>();

        /// <summary>
        /// Initialize with a TKB database.
        /// </summary>
        /// <param name="db">TKB database service.</param>
        public AnimationTkbQueries(ITkbDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Get the animation descriptor for an entity class.
        /// Returns null if the class has no animation descriptor or the class doesn't exist.
        /// </summary>
        private CharacterAnimationDefDto? GetAnimationDef(string entityClass)
        {
            try
            {
                if (!_db.TryGetByName(entityClass, out var template))
                    return null;

                return template.GetDescriptor<CharacterAnimationDefDto>();
            }
            catch
            {
                // Class not found or descriptor missing
                return null;
            }
        }

        /// <summary>
        /// Invalidate cached query results for a specific class.
        /// Called by hot-reload handlers when descriptors change.
        /// </summary>
        public void InvalidateClass(string entityClass)
        {
            var keysToRemove = _queryCache.Keys
                .Where(k => k.Item1 == entityClass)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _queryCache.Remove(key);
            }
        }

        /// <summary>
        /// Clear all cached query results.
        /// </summary>
        public void ClearCache()
        {
            _queryCache.Clear();
        }

        public IReadOnlyList<MontageDefDto> GetPlayableMontages(string entityClass)
        {
            if (string.IsNullOrEmpty(entityClass))
                return Array.Empty<MontageDefDto>();

            var cacheKey = (entityClass, "playable_montages");
            if (_queryCache.TryGetValue(cacheKey, out var cached))
                return (IReadOnlyList<MontageDefDto>?)cached ?? Array.Empty<MontageDefDto>();

            var def = GetAnimationDef(entityClass);
            if (def == null)
            {
                _queryCache[cacheKey] = null;
                return Array.Empty<MontageDefDto>();
            }

            // Filter out stance-transition montages
            var playable = def.Montages
                .Where(m => !m.IsStanceTransition)
                .ToList();

            _queryCache[cacheKey] = playable;
            return playable;
        }

        public MontageDefDto? GetMontage(string entityClass, string montageName)
        {
            if (string.IsNullOrEmpty(entityClass) || string.IsNullOrEmpty(montageName))
                return null;

            var def = GetAnimationDef(entityClass);
            if (def == null)
                return null;

            return def.Montages.FirstOrDefault(m => m.Name == montageName);
        }

        public IReadOnlyList<StanceId> GetSupportedStances(string entityClass)
        {
            if (string.IsNullOrEmpty(entityClass))
                return Array.Empty<StanceId>();

            var cacheKey = (entityClass, "supported_stances");
            if (_queryCache.TryGetValue(cacheKey, out var cached))
                return (IReadOnlyList<StanceId>?)cached ?? Array.Empty<StanceId>();

            var def = GetAnimationDef(entityClass);
            if (def == null)
            {
                _queryCache[cacheKey] = null;
                return Array.Empty<StanceId>();
            }

            _queryCache[cacheKey] = def.SupportedStances;
            return def.SupportedStances;
        }

        public bool SupportsAim(string entityClass)
        {
            if (string.IsNullOrEmpty(entityClass))
                return false;

            var def = GetAnimationDef(entityClass);
            return def?.AimConfig != null;
        }

        public IReadOnlyList<NotifyMarkerDefDto> GetAvailableMarkers(string entityClass)
        {
            if (string.IsNullOrEmpty(entityClass))
                return Array.Empty<NotifyMarkerDefDto>();

            var cacheKey = (entityClass, "available_markers");
            if (_queryCache.TryGetValue(cacheKey, out var cached))
                return (IReadOnlyList<NotifyMarkerDefDto>?)cached ?? Array.Empty<NotifyMarkerDefDto>();

            var def = GetAnimationDef(entityClass);
            if (def == null)
            {
                _queryCache[cacheKey] = null;
                return Array.Empty<NotifyMarkerDefDto>();
            }

            // Return the NotifyMarkers list directly (it's the union of all markers used by montages)
            var markers = def.NotifyMarkers.ToList();
            _queryCache[cacheKey] = markers;
            return markers;
        }

        public string? GetMarkerName(string entityClass, uint hash)
        {
            if (string.IsNullOrEmpty(entityClass))
                return null;

            var def = GetAnimationDef(entityClass);
            if (def == null)
                return null;

            var marker = def.NotifyMarkers.FirstOrDefault(m => m.Hash == hash);
            return marker?.Name;
        }

        public int ResolveMontageId(string entityClass, string montageName)
        {
            if (string.IsNullOrEmpty(entityClass) || string.IsNullOrEmpty(montageName))
                throw new ArgumentException("Entity class and montage name must not be empty.");

            var cacheKey = (entityClass, $"montage_id_{montageName}");
            if (_queryCache.TryGetValue(cacheKey, out var cached))
            {
                if (cached is int id)
                    return id;
            }

            // Compute the ID using the same algorithm as the runtime
            int montageId = StableIdHasher.ComputeMontageAssetId(montageName);
            _queryCache[cacheKey] = montageId;
            return montageId;
        }
    }
}
