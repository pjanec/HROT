using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// <see cref="IGuidResolver"/> implementation for federated replay.
    /// Save and load maps are hot-swappable via <see cref="SetSaveMap"/> and
    /// <see cref="SetLoadMap"/>, allowing the same resolver instance to be reused
    /// across multiple serialize/deserialize operations.
    /// <para>
    /// Unlike the engine's internal <c>LoadResolver</c>, <see cref="Resolve(string)"/>
    /// returns <see cref="Entity.Null"/> on a cache miss rather than throwing.
    /// </para>
    /// </summary>
    public sealed class FederatedGuidResolver : IGuidResolver
    {
        private Dictionary<Entity, string>? _saveMap;
        private Dictionary<string, Entity>? _loadMap;

        /// <summary>Replaces the save-phase map used by <see cref="Resolve(Entity)"/>.</summary>
        public void SetSaveMap(Dictionary<Entity, string> map) => _saveMap = map;

        /// <summary>Replaces the load-phase map used by <see cref="Resolve(string)"/>.</summary>
        public void SetLoadMap(Dictionary<string, Entity> map) => _loadMap = map;

        /// <summary>
        /// Save phase: returns the pre-computed GUID string for <paramref name="entity"/>,
        /// or the literal string <c>"null"</c> if the entity is not in the save map.
        /// </summary>
        public string Resolve(Entity entity)
        {
            if (_saveMap != null && _saveMap.TryGetValue(entity, out var s))
                return s;
            return "null";
        }

        /// <summary>
        /// Load phase: returns the <see cref="Entity"/> mapped to <paramref name="guidStr"/>,
        /// or <see cref="Entity.Null"/> if the string is not in the load map.
        /// Does NOT throw on miss.
        /// </summary>
        public Entity Resolve(string guidStr)
        {
            if (_loadMap != null && _loadMap.TryGetValue(guidStr, out var entity))
                return entity;
            return Entity.Null;
        }
    }
}
