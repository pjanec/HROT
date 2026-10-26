using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ The ONE evaluation of a <see cref="StructuralPredicateDto"/> over played frames — the Replay Search's and the timeline's
    /// (§9c ②: the Life lanes are structural predicates on <c>NetworkIdentity</c>). Stateful: one instance per scan. An entity gaining the
    /// component reports <c>Added</c>; losing it, or being destroyed while it had it, reports <c>Removed</c>.
    /// <para>⚠ <b>As built `2026-10-26` (CE-1302):</b> a destroyed entity is reported only when the predicate asks for removals — before, the
    /// search reported "Lost (Destroyed)" for an <c>Added</c>-only predicate too.</para>
    /// <para>With a projection read off a component (a position), a departing entity carries what was last read of it — refreshed every
    /// <see cref="RefreshFrames"/> frames, since a destroyed entity can no longer be read.</para>
    /// </summary>
    public sealed class StructuralScanner
    {
        public const int RefreshFrames = 30;

        private readonly StructuralPredicateDto _predicate;
        private readonly CompiledProjection? _projection;
        private readonly HashSet<Entity> _has = new();
        private readonly Dictionary<Entity, (SearchResultPayload Payload, int Frame)> _last = new();
        private readonly string _gained, _lost;

        public StructuralScanner(StructuralPredicateDto predicate, ResultProjectionDto? projection = null)
        {
            _predicate = predicate;
            if (projection != null) _projection = new CompiledProjection(projection, predicate.ComponentType);
            _gained = "Gained " + predicate.ComponentType.Name;
            _lost = "Lost " + predicate.ComponentType.Name;
        }

        private bool ReportsAdded => _predicate.ModificationType is StructuralModification.Added or StructuralModification.AnyChange;
        private bool ReportsRemoved => _predicate.ModificationType is StructuralModification.Removed or StructuralModification.AnyChange;

        /// <summary>Scans one played frame. ⚠ Reads the repository's destruction log and leaves it for the caller to clear.</summary>
        public void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results, TargetEntityFilter? entityFilter = null)
        {
            int typeId = ComponentTypeRegistry.GetId(_predicate.ComponentType);
            if (typeId < 0) return;

            int maxIdx = repo.MaxEntityIndex;
            for (int i = 0; i <= maxIdx; i++)
            {
                ref var mask = ref repo.GetComponentMask(i);
                ref var meta = ref repo.GetMetadata(i);
                if (!meta.IsActive) continue;
                // ⚠ every active entity, not a delta query: playback's RestoreChunkFromBuffer does not move chunk versions.
                Entity entity = repo.GetEntityByIndex(i);
                if (entity.IsNull) continue;
                if (entityFilter != null && !entityFilter.Passes(repo, entity)) continue;

                bool present = IsPresent(ref mask, ref meta, typeId, _predicate.AuthorityRequirement);
                bool was = _has.Contains(entity);
                if (present && !was)
                {
                    _has.Add(entity);
                    if (ReportsAdded) results.Add(Result(repo, entity, frame, ticks, _gained, refresh: true));
                    else if (_projection != null) Remember(repo, entity, frame);
                }
                else if (!present && was)
                {
                    _has.Remove(entity);
                    if (ReportsRemoved) results.Add(Result(repo, entity, frame, ticks, _lost, refresh: false));
                    _last.Remove(entity);
                }
                else if (present && _projection != null && _last.TryGetValue(entity, out var last) && frame - last.Frame >= RefreshFrames)
                    Remember(repo, entity, frame);
            }

            var destroyed = repo.GetDestructionLog();
            for (int i = 0; i < destroyed.Count; i++)
            {
                Entity dead = destroyed[i];
                if (!_has.Remove(dead)) continue;
                if (ReportsRemoved) results.Add(Result(repo, dead, frame, ticks, _lost + " (Destroyed)", refresh: false));
                _last.Remove(dead);
            }
        }

        private SearchResultDto Result(EntityRepository repo, Entity entity, int frame, long ticks, string message, bool refresh)
        {
            if (_projection == null) return new SearchResultDto(frame, ticks, entity, message);
            if (refresh || !_last.ContainsKey(entity)) Remember(repo, entity, frame);
            return new SearchResultDto(frame, ticks, entity, message, _last[entity].Payload);
        }

        private void Remember(EntityRepository repo, Entity entity, int frame)
        {
            object? matched = _projection!.SourceComponent == null ? null : ComponentBox.Read(repo, entity, _projection.SourceComponent);
            var payload = _projection.Apply(repo, matched, entity, out _);
            _last[entity] = (payload, frame);
        }

        internal static bool IsPresent(ref BitMask512 mask, ref EntityMetadataCold meta, int typeId, AuthorityRequirement req) => req switch
        {
            AuthorityRequirement.RequireAuthority => mask.IsSet(typeId) && meta.AuthorityMask.IsSet(typeId),
            AuthorityRequirement.RequireGhost => mask.IsSet(typeId) && !meta.AuthorityMask.IsSet(typeId),
            _ => mask.IsSet(typeId),
        };
    }
}
