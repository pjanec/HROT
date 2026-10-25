using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// ⭐⭐ <c>CE-1329</c> (docs/DESIGN_Entity_Brain_View.md §12r, BV-65) — THE ONE STORE of every per-unit debug switch, keyed by the unit's
    /// NETWORK ID: which map-gizmo families are pinned on it, whether its behaviour trace is switched on, and the unit the 3-D camera follows.
    /// The Brain tab, the map's right-click menu and the HTTP API write it; the map, the behaviour runners and the follow camera act on it.
    /// <para>⭐ A world SINGLETON, so a preview's rewind does not touch it (<c>WorldKeyframes.EditorDryRun</c> captures no singleton): a tick set
    /// during a preview survives its Stop. A host persists it per scenario (<see cref="ToJson"/> / <see cref="Load"/>).</para>
    /// <para>⭐ The behaviour runners still read <see cref="DebugState"/> each tick (the behaviors lane's per-entity read, unchanged): it is a
    /// COPY of this store, rewritten every frame by <see cref="Apply"/> — nothing else writes the bits the store owns, so the copy cannot drift.
    /// A unit with no network id (a test world, a local-only entity) keeps the old direct <see cref="PatchDebugStateCommand"/> path.</para>
    /// <para>Tracing has three requesters, and is on while ANY asks: what someone switched on (<see cref="SetTrace"/>, remembered), a Brain tab
    /// showing the unit (<see cref="SetViewed"/>, temporary) and an arming by the API or a breakpoint (<see cref="SetArmed"/>, temporary).</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.UnitDebugSettings)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class UnitDebugSettings
    {
        /// <summary>One unit's REMEMBERED switches.</summary>
        public readonly record struct Entry(AiOverlayFlags Pins, bool Trace)
        {
            public bool IsEmpty => Pins == AiOverlayFlags.None && !Trace;
        }

        private readonly Dictionary<long, Entry> _remembered = new();
        private readonly HashSet<long> _viewed = new();
        private readonly HashSet<long> _armed = new();
        private readonly HashSet<long> _managed = new();   // every unit whose DebugState bits the store has written (so a reset clears them)

        /// <summary>Bumped on every change to what is REMEMBERED (a host saves when it moves).</summary>
        public int Version { get; private set; }

        /// <summary>The network id of the unit the 3-D camera follows (remembered), or null.</summary>
        public long? Followed { get; private set; }

        /// <summary>The store of <paramref name="world"/> (created on first use).</summary>
        public static UnitDebugSettings Of(EntityRepository world)
        {
            if (world.HasSingletonManaged<UnitDebugSettings>() && world.GetSingletonManaged<UnitDebugSettings>() is { } s) return s;
            var created = new UnitDebugSettings();
            world.SetSingletonManaged(created);
            return created;
        }

        /// <summary>The store of <paramref name="world"/>, or null when nothing was ever set there (a READ never creates it: menus and gizmos may
        /// read off the main thread).</summary>
        public static UnitDebugSettings? Peek(EntityRepository world)
            => world.HasSingletonManaged<UnitDebugSettings>() ? world.GetSingletonManaged<UnitDebugSettings>() : null;

        /// <summary>The unit's network id, or 0 when it has none (then the store cannot hold it).</summary>
        public static long NetIdOf(EntityRepository world, Entity unit)
            => unit != Entity.Null && world.IsAlive(unit) && world.IsComponentTypeRegistered<NetworkIdentity>() && world.HasComponent<NetworkIdentity>(unit)
                ? world.GetComponentRO<NetworkIdentity>(unit).Value : 0;

        public Entry Get(long netId) => _remembered.TryGetValue(netId, out var e) ? e : default;
        public IReadOnlyDictionary<long, Entry> Remembered => _remembered;

        public bool IsPinned(long netId, AiOverlayFlags family) => (Get(netId).Pins & family) != 0;

        public void SetPin(long netId, AiOverlayFlags family, bool on)
        {
            var e = Get(netId);
            Put(netId, e with { Pins = on ? e.Pins | family : e.Pins & ~family });
        }

        /// <summary>Whether someone switched the unit's trace on (remembered) — not counting a tab or an arming.</summary>
        public bool IsTraceSet(long netId) => Get(netId).Trace;

        public void SetTrace(long netId, bool on) => Put(netId, Get(netId) with { Trace = on });

        /// <summary>A Brain tab shows the unit (temporary, never saved).</summary>
        public void SetViewed(long netId, bool on) => Toggle(_viewed, netId, on);

        /// <summary>The API or a breakpoint armed the unit's trace (temporary, never saved).</summary>
        public void SetArmed(long netId, bool on) => Toggle(_armed, netId, on);

        public bool IsArmed(long netId) => _armed.Contains(netId);

        /// <summary>Whether the unit's trace runs: switched on, shown by a tab, or armed.</summary>
        public bool TraceOn(long netId) => Get(netId).Trace || _viewed.Contains(netId) || _armed.Contains(netId);

        public void SetFollowed(long? netId)
        {
            if (Followed == netId) return;
            Followed = netId;
            Version++;
        }

        /// <summary>Forgets everything remembered for one unit (the Brain tab's reset).</summary>
        public void ResetUnit(long netId)
        {
            if (_remembered.Remove(netId)) Version++;
            if (Followed == netId) SetFollowed(null);
        }

        /// <summary>Forgets everything remembered (the Settings menu's reset). Temporary requests stay.</summary>
        public void ResetAll()
        {
            _remembered.Clear();
            Followed = null;
            Version++;
        }

        private void Put(long netId, Entry e)
        {
            if (e.IsEmpty) { if (!_remembered.Remove(netId)) return; }
            else if (_remembered.TryGetValue(netId, out var old) && old == e) return;
            else _remembered[netId] = e;
            Version++;
        }

        private static void Toggle(HashSet<long> set, long netId, bool on)
        {
            if (on) set.Add(netId); else set.Remove(netId);
        }

        /// <summary>
        /// Writes the store's bits into every unit's <see cref="DebugState"/>: the pins exactly, and the trace switch
        /// (<see cref="BehaviorDebugFlags.EnableTraceBuffer"/>) on while any requester asks. Only units the store holds or has held are
        /// touched; any other bit of <see cref="DebugState"/> is left as it is. Run each frame (by <see cref="DebugStatePatchSystem"/>), so a
        /// preview's rewind, a respawn or a scenario load is followed on the next frame.
        /// </summary>
        public void Apply(EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<NetworkIdentity>() || !world.IsComponentTypeRegistered<DebugState>()) return;
            if (_remembered.Count == 0 && _viewed.Count == 0 && _armed.Count == 0 && _managed.Count == 0) return;
            foreach (var e in world.Query().With<NetworkIdentity>().Build())
            {
                long id = world.GetComponentRO<NetworkIdentity>(e).Value;
                bool wanted = _remembered.ContainsKey(id) || _viewed.Contains(id) || _armed.Contains(id);
                if (!wanted && !_managed.Contains(id)) continue;
                var pins = Get(id).Pins;
                bool trace = TraceOn(id);
                if (!world.HasComponent<DebugState>(e))
                {
                    if (!wanted) continue;
                    world.AddComponent(e, new DebugState());
                }
                ref var state = ref world.GetComponentRW<DebugState>(e);
                var behavior = trace ? state.Behavior | BehaviorDebugFlags.EnableTraceBuffer : state.Behavior & ~BehaviorDebugFlags.EnableTraceBuffer;
                if (state.Ai != pins || state.Behavior != behavior) { state.Ai = pins; state.Behavior = behavior; }
                if (wanted) _managed.Add(id); else _managed.Remove(id);   // released: its bits cleared once, then left alone
            }
        }

        // ── persistence (per scenario, by the host) ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>What is remembered, as JSON: <c>{ "units": { "1004": { "pins": ["Perception"], "trace": true } }, "followed": 1004 }</c>.</summary>
        public JsonObject ToJson()
        {
            var units = new JsonObject();
            foreach (var (id, e) in _remembered)
            {
                var pins = new JsonArray();
                foreach (AiOverlayFlags f in Enum.GetValues<AiOverlayFlags>())
                    if (f != AiOverlayFlags.None && (e.Pins & f) == f && IsSingleBit(f)) pins.Add(f.ToString());
                units[id.ToString()] = new JsonObject { ["pins"] = pins, ["trace"] = e.Trace };
            }
            return new JsonObject { ["units"] = units, ["followed"] = Followed };
        }

        /// <summary>Replaces what is remembered with <paramref name="json"/> (null or bad = nothing); the temporary requests stay.</summary>
        public void Load(JsonNode? json)
        {
            _remembered.Clear();
            Followed = null;
            if (json is JsonObject o)
            {
                if (o["units"] is JsonObject units)
                    foreach (var (key, node) in units)
                    {
                        if (!long.TryParse(key, out var id) || node is not JsonObject u) continue;
                        var pins = AiOverlayFlags.None;
                        if (u["pins"] is JsonArray arr)
                            foreach (var p in arr)
                                if (p?.GetValue<string>() is { } name && Enum.TryParse<AiOverlayFlags>(name, out var f)) pins |= f;
                        bool trace = u["trace"] is JsonValue t && t.TryGetValue<bool>(out var b) && b;
                        var entry = new Entry(pins, trace);
                        if (!entry.IsEmpty) _remembered[id] = entry;
                    }
                if (o["followed"] is JsonValue fv && fv.TryGetValue<long>(out var followed)) Followed = followed;
            }
            Version++;
        }

        private static bool IsSingleBit(AiOverlayFlags f) => ((ushort)f & ((ushort)f - 1)) == 0;
    }
}
