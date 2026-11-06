using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ CE-4206 R1 (<c>docs/blueprints/DESIGN_Map_Picking_Unification.md</c> §6.2, F2) — the reference scope of a RUNNING world: what an editor asks while a
    /// person authors a link — <i>which entity is GUID g / key k here</i>, and the REVERSE (<i>what is this entity's GUID / key / name</i>). The sibling of
    /// <see cref="ScenarioRefScope"/>, which is built once per LOAD and goes stale at the first edit.
    /// <para>Indexes the entities that carry a <see cref="NetworkIdentity"/> (an <see cref="EntityRef"/> IS a network id): GUID → ref
    /// (<see cref="EntityGuid"/>), key → ref (<see cref="RefKey"/>), and the reverse maps. ⛔ A duplicate key is NOT thrown here (a load refuses it;
    /// an editor must be able to SHOW it): <see cref="HasDuplicate"/> reports it, <see cref="TryResolve"/> then answers the first entity found.</para>
    /// <para><b>Rebuild policy (chosen):</b> the world exposes no structural version, so the index is rebuilt lazily when the entity count or the highest
    /// issued index changed (a spawn / a destroy — immediately), or — for an edit of a key / GUID on an existing entity — when
    /// <see cref="EntityRepository.GlobalVersion"/> moved and at least <see cref="RebuildInterval"/> has passed since the last build.
    /// <see cref="Invalidate"/> forces the next query to rebuild.</para>
    /// </summary>
    public sealed class LiveRefScope : IRefScope
    {
        /// <summary>The least time between rebuilds caused by a mere version change (a key edited in place).</summary>
        public static readonly TimeSpan RebuildInterval = TimeSpan.FromMilliseconds(250);

        private readonly EntityRepository _world;
        private readonly Func<long> _nowTicks;
        private readonly Dictionary<Guid, EntityRef> _byGuid = new();
        private readonly Dictionary<string, EntityRef> _byKey = new(StringComparer.Ordinal);
        private readonly HashSet<string> _duplicateKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _keyCounts = new(StringComparer.Ordinal);
        private readonly TimeSpan _interval;
        private readonly Dictionary<int, Guid> _guidOf = new();
        private readonly Dictionary<int, string> _keyOf = new();
        private readonly Dictionary<int, string> _nameOf = new();
        private readonly Dictionary<int, WorldPoint> _positions = new();
        private readonly Func<EntityRepository, Entity, EntityKind?>? _kinds;
        private readonly Dictionary<int, EntityKind> _kindOf = new();
        private readonly Dictionary<int, string> _chipOf = new();

        private bool _built;
        private int _count, _maxIndex;
        private uint _version;
        private long _builtAt;
        private bool _invalid;

        /// <param name="world">The running world.</param>
        /// <param name="nowTicks">A monotonic clock in <see cref="Stopwatch"/> ticks (tests inject one); default: wall clock.</param>
        /// <param name="rebuildInterval">The least time between version-caused rebuilds (default <see cref="RebuildInterval"/>).</param>
        /// <param name="kinds">⭐ U-8 (DESIGN_Condition_Editor §8.2 ④) — what KIND of thing an entity is (<i>Objective</i>, <i>Tank</i>) and its glyph key; asked once
        /// per entity per rebuild, never per frame. The host that knows the classes supplies it (the editor's one scope, <c>AuthoredRules.ScopeOf</c>); null = no kinds.</param>
        public LiveRefScope(EntityRepository world, Func<long>? nowTicks = null, TimeSpan? rebuildInterval = null, Func<EntityRepository, Entity, EntityKind?>? kinds = null)
        {
            _kinds = kinds;
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _nowTicks = nowTicks ?? (() => Stopwatch.GetTimestamp());
            _interval = rebuildInterval ?? RebuildInterval;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, LiveRefScope> _simScopes = new();

        /// <summary>
        /// ⭐ THE live-world scope for SIM code (a step runner, a rule's link reader, the macro expander) — ONE cached instance per world.
        /// ⛔ Sim must not depend on wall time (a replay of the same ticks must resolve the same targets), so this one has NO wall-clock window:
        /// it rebuilds on a spawn / destroy (count, highest index) AND whenever the world's <see cref="EntityRepository.GlobalVersion"/> (the frame
        /// clock — a tick, a restore) moved. It is stale only WITHIN one tick, identically in a live run and its replay. The editor builds its own
        /// (<c>new LiveRefScope(repo)</c>, the 250 ms wall-clock policy) — that one never decides a simulation outcome.
        /// </summary>
        public static LiveRefScope Of(EntityRepository world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return _simScopes.GetValue(world, w => new LiveRefScope(w, () => 0, TimeSpan.Zero));
        }

        /// <inheritdoc/>
        public string Describe => "this world";

        /// <summary>How many times the index was (re)built — a probe for tests.</summary>
        public int Rebuilds { get; private set; }

        /// <summary>Forces the next query to rebuild the index.</summary>
        public void Invalidate() => _invalid = true;

        private void Refresh()
        {
            bool structural = !_built || _invalid || _world.EntityCount != _count || _world.MaxEntityIndex != _maxIndex;
            if (!structural && _world.GlobalVersion != _version)
            {
                double elapsedMs = (_nowTicks() - _builtAt) * 1000.0 / Stopwatch.Frequency;
                structural = elapsedMs >= _interval.TotalMilliseconds;
            }
            if (structural) Rebuild();
        }

        private void Rebuild()
        {
            _byGuid.Clear(); _byKey.Clear(); _duplicateKeys.Clear(); _keyCounts.Clear();
            _guidOf.Clear(); _keyOf.Clear(); _nameOf.Clear(); _positions.Clear(); _kindOf.Clear(); _chipOf.Clear();
            Rebuilds++;

            if (_world.IsComponentTypeRegistered<NetworkIdentity>())
            {
                bool hasGuid = _world.IsComponentTypeRegistered<EntityGuid>();
                bool hasKey = _world.IsComponentTypeRegistered<RefKey>();
                bool hasInfo = _world.IsComponentTypeRegistered<EntityInfo>();
                bool hasPos = _world.IsComponentTypeRegistered<SimTransform>();
                foreach (var e in _world.Query().With<NetworkIdentity>().Build())
                {
                    int id = _world.GetComponent<NetworkIdentity>(e).Value;
                    if (id <= 0) continue;
                    var r = new EntityRef(id);
                    if (hasGuid && _world.HasComponent<EntityGuid>(e) && _world.GetComponent<EntityGuid>(e).Value is { } g && g != Guid.Empty)
                    { _byGuid[g] = r; _guidOf[id] = g; }
                    if (hasKey && _world.HasComponent<RefKey>(e) && _world.GetComponent<RefKey>(e).Value.ToString() is { Length: > 0 } k)
                    {
                        _keyCounts[k] = _keyCounts.TryGetValue(k, out var c) ? c + 1 : 1;
                        if (_byKey.ContainsKey(k)) _duplicateKeys.Add(k); else _byKey[k] = r;
                        _keyOf[id] = k;
                    }
                    if (hasInfo && _world.HasComponent<EntityInfo>(e) && _world.GetComponent<EntityInfo>(e).Name.ToString() is { Length: > 0 } n)
                        _nameOf[id] = n;
                    if (hasPos && _world.HasComponent<SimTransform>(e))
                        _positions[id] = new WorldPoint(_world.GetComponent<SimTransform>(e).Position);
                    if (_kinds?.Invoke(_world, e) is { } kind)
                    {
                        _kindOf[id] = kind;
                        if (kind.Name.Length > 0) _chipOf[id] = _nameOf.TryGetValue(id, out var nm) ? $"{kind.Name}: {nm}" : $"{kind.Name} #{id}";
                    }
                }
            }

            _built = true; _invalid = false;
            _count = _world.EntityCount; _maxIndex = _world.MaxEntityIndex; _version = _world.GlobalVersion;
            _builtAt = _nowTicks();
        }

        /// <inheritdoc/>
        public bool TryResolve(in RefSpec spec, out EntityRef entity)
        {
            Refresh();
            entity = EntityRef.None;
            return spec.Form switch
            {
                RefForm.Key   => _byKey.TryGetValue(spec.KeyText, out entity),
                RefForm.Exact => _byGuid.TryGetValue(spec.Exact, out entity),
                _             => false,
            };
        }

        /// <inheritdoc/>
        public bool TryPositionOf(EntityRef entity, out WorldPoint position)
        {
            Refresh();
            return _positions.TryGetValue(entity.NetworkId, out position);
        }

        /// <summary>The entity's stable GUID; <c>false</c> when it has none (a bullet, an unsaved helper).</summary>
        public bool TryGuidOf(EntityRef entity, out Guid guid)
        {
            Refresh();
            return _guidOf.TryGetValue(entity.NetworkId, out guid);
        }

        /// <summary>The entity's <see cref="RefKey"/> text (no <c>@</c>); <c>false</c> when it has none.</summary>
        public bool TryKeyOf(EntityRef entity, out string key)
        {
            Refresh();
            return _keyOf.TryGetValue(entity.NetworkId, out key!);
        }

        /// <summary>Every key in the world, sorted — the Key form's completion list.</summary>
        public IReadOnlyList<string> Keys()
        {
            Refresh();
            return _keyOf.Values.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>More than one entity carries <paramref name="key"/> — the UI shows it red.</summary>
        public bool HasDuplicate(string key)
        {
            Refresh();
            return _duplicateKeys.Contains(key.StartsWith('@') ? key.Substring(1) : key);
        }

        /// <summary>⭐ SR-188 — keys two or more entities carry, with how many (a resolve answers the FIRST).</summary>
        public IReadOnlyDictionary<string, int> Ambiguous
        {
            get
            {
                Refresh();
                var d = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var k in _duplicateKeys) d[k] = _keyCounts[k];
                return d;
            }
        }

        /// <summary>⭐ U-8 — what kind of thing <paramref name="entity"/> is (name + glyph key); <c>false</c> when the scope has no kinds or this one has none.</summary>
        public bool TryKindOf(EntityRef entity, out EntityKind kind)
        {
            Refresh();
            return _kindOf.TryGetValue(entity.NetworkId, out kind);
        }

        /// <summary>⭐ U-8 (DESIGN_Condition_Editor §8.2 ④) — the entity-link chip's text: <c>Objective: Hold the bridge</c> — the kind, then the name; the name alone
        /// when no kind is known (<see cref="NameOf"/>). Built at the rebuild, so a frame reads it without allocating.</summary>
        public string ChipLabelOf(EntityRef entity)
        {
            if (entity.IsNone) return "(none)";
            Refresh();
            return _chipOf.TryGetValue(entity.NetworkId, out var c) ? c : NameOf(entity);
        }

        /// <summary>The entity's <see cref="EntityInfo.Name"/>, else <c>#id</c> (<c>(none)</c> for none).</summary>
        public string NameOf(EntityRef entity)
        {
            if (entity.IsNone) return "(none)";
            Refresh();
            return _nameOf.TryGetValue(entity.NetworkId, out var n) ? n : "#" + entity.NetworkId;
        }
    }

    /// <summary>⭐ U-8 — what kind of thing an entity is, for a person: <see cref="Name"/> (<i>Objective</i>, <i>Tank</i>) and <see cref="IconKey"/>, a key of the
    /// one glyph table (<c>NodeEditor.Core.Widgets.Icons</c>, <c>thing/*</c>); empty key = no glyph.</summary>
    public readonly record struct EntityKind(string Name, string IconKey);
}
