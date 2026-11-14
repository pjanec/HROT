using System;
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
        // ⭐ CE-3330 — tracked entities by raw index: the generation seen (−1 = not tracked) and the frame its payload was last read. Replaces a
        //   HashSet<Entity> probe and a payload-dictionary probe per entity per frame; an index REUSED with a new generation is a departure
        //   plus an arrival, never silence.
        private int[] _gen = Array.Empty<int>();
        private int[] _remembered = Array.Empty<int>();
        private readonly List<Entity> _departed = new();
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

        private EntityRepository? _destructionRepo;   // ⭐ CE-3335 — the world this scanner's destruction mark belongs to
        private long _destructionMark;

        /// <summary>Scans one played frame. ⚠ Reads the repository's destruction log and leaves it for the caller to clear.</summary>
        public void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results, TargetEntityFilter? entityFilter = null)
        {
            int typeId = ComponentTypeRegistry.GetId(_predicate.ComponentType);
            if (typeId < 0) return;

            int maxIdx = repo.MaxEntityIndex;
            if (_gen.Length <= maxIdx)
            {
                int old = _gen.Length, size = Math.Max(maxIdx + 1, old * 2);
                Array.Resize(ref _gen, size); Array.Resize(ref _remembered, size);
                for (int k = old; k < size; k++) _gen[k] = -1;
            }
            _departed.Clear();
            for (int i = 0; i <= maxIdx; i++)
            {
                ref var mask = ref repo.GetComponentMask(i);
                ref var meta = ref repo.GetMetadata(i);
                bool active = meta.IsActive;
                int tracked = _gen[i];
                // a tracked entity that is gone (destroyed, or its index reused by another generation) departs here — no IsAlive sweep, and it
                //   needs no destruction log (it catches a death the log no longer holds — a scanner that started late)
                if (tracked >= 0 && (!active || meta.Generation != tracked))
                {
                    _departed.Add(new Entity(i, (ushort)tracked));
                    _gen[i] = -1; tracked = -1;
                }
                if (!active) continue;
                // ⚠ every active entity, not a delta query: playback's RestoreChunkFromBuffer does not move chunk versions.
                Entity entity = new Entity(i, meta.Generation);
                if (entityFilter != null && !entityFilter.Passes(repo, entity)) continue;

                bool present = IsPresent(ref mask, ref meta, typeId, _predicate.AuthorityRequirement);
                bool was = tracked >= 0;
                if (present && !was)
                {
                    _gen[i] = meta.Generation;
                    if (ReportsAdded) results.Add(Result(repo, entity, frame, ticks, _gained, refresh: true));
                    else if (_projection != null) Remember(repo, entity, frame);
                }
                else if (!present && was)
                {
                    _gen[i] = -1;
                    if (ReportsRemoved) results.Add(Result(repo, entity, frame, ticks, _lost, refresh: false));
                    _last.Remove(entity);
                }
                else if (present && _projection != null && frame - _remembered[i] >= RefreshFrames)
                    Remember(repo, entity, frame);
            }

            // departures the destruction log names come first (the log's order), then the ones it never showed — the order the report always had
            // ⭐ CE-3335 — by this scanner's own mark: a live world's log is shared (the recorder no longer empties it), a playback world's
            //   owner clears it per frame — both read the same way
            if (!ReferenceEquals(repo, _destructionRepo)) { _destructionRepo = repo; _destructionMark = 0; }
            var destroyed = repo.ReadDestructions(ref _destructionMark);
            for (int i = 0; i < destroyed.Length; i++)
            {
                Entity dead = destroyed[i];
                int at = _departed.IndexOf(dead);
                if (at >= 0) _departed.RemoveAt(at);
                else if (dead.Index >= 0 && dead.Index < _gen.Length && _gen[dead.Index] == dead.Generation) _gen[dead.Index] = -1;
                else continue;
                if (ReportsRemoved) results.Add(Result(repo, dead, frame, ticks, _lost + " (Destroyed)", refresh: false));
                _last.Remove(dead);
            }
            foreach (var dead in _departed)
            {
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
            _remembered[entity.Index] = frame;
        }

        internal static bool IsPresent(ref BitMask512 mask, ref EntityMetadataCold meta, int typeId, AuthorityRequirement req) => req switch
        {
            AuthorityRequirement.RequireAuthority => mask.IsSet(typeId) && meta.AuthorityMask.IsSet(typeId),
            AuthorityRequirement.RequireGhost => mask.IsSet(typeId) && !meta.AuthorityMask.IsSet(typeId),
            _ => mask.IsSet(typeId),
        };
    }
}
