using System;
using System.Collections.Generic;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ S0 item 3 (<c>docs/DESIGN_Scenario_Rules.md</c> SR-63…SR-66, §9.8.3 SR-126) — an entity's AUTHORED NAME, the one an authored
    /// reference uses: <c>"@BlueHQ"</c> = <i>whatever entity carries <c>RefKey "BlueHQ"</c> here</i>. A person gives it; it is saved with the
    /// scenario (<c>"RefKey": {"Value": "BlueHQ"}</c>). ⭐ SR-188 (2026-11-02, amends SR-66): a duplicate is ALLOWED — the first match wins
    /// and the ambiguity is a warning (<see cref="ScenarioRefScope.Ambiguous"/>).
    /// ≤ 31 UTF-8 bytes, no <c>@</c> (<see cref="RefSpec.ByKey"/> refuses a longer one).
    /// </summary>
    [ComponentId(GlobalComponentIds.RefKey)]
    [DataPolicy(DataPolicy.Default)]   // authored data: saved, recorded, snapshotted
    public struct RefKey
    {
        public FixedString32 Value;

        public RefKey(string key) => Value = RefSpec.ByKey(key).Key;   // the one length check

        public override string ToString() => "@" + Value.ToString();
    }

    /// <summary>
    /// ⭐ The SCENARIO scope a load resolves authored references in (S0 item 3): every entity's <see cref="RefKey"/> and stable
    /// <see cref="EntityGuid"/>, each mapped to the network id the entity will have and to its position. Built once per load by the
    /// extractor; ⛔ no macro scope (<c>@Outer/Inner</c> across instances is S3's).
    /// </summary>
    public sealed class ScenarioRefScope : IRefScope
    {
        private readonly Dictionary<string, EntityRef> _byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, EntityRef> _byGuid = new();
        private readonly Dictionary<long, WorldPoint> _positions = new();
        private readonly Dictionary<string, int> _ambiguous = new(StringComparer.Ordinal);

        /// <summary>⭐ SR-188 — each key more than one entity carries, with how many (a probe for the load log and tests).</summary>
        public IReadOnlyDictionary<string, int> Ambiguous => _ambiguous;

        public ScenarioRefScope(string describe) => Describe = describe;

        public string Describe { get; }

        /// <summary>Number of keyed entities (a probe for tests and the load log).</summary>
        public int KeyCount => _byKey.Count;

        /// <summary>Adds one entity of the scope. ⛔ A key already taken is a LOUD error naming both.</summary>
        public void Add(EntityRef entity, string? key, Guid guid, WorldPoint? position)
        {
            if (!string.IsNullOrEmpty(key))
            {
                // ⭐ SR-188 (DESIGN_Scenario_Macros §14.3, amends SR-65 / SR-66) — duplicate keys are ALLOWED (two instances of one macro give
                //   two "@SL"s): the FIRST match wins, in file order, and the ambiguity is a WARNING. ⛔ A MISSING key still fails loudly.
                if (_byKey.TryGetValue(key, out var first) && first != entity)
                    _ambiguous[key] = (_ambiguous.TryGetValue(key, out var n) ? n : 1) + 1;
                else
                    _byKey[key] = entity;
            }
            if (guid != Guid.Empty) _byGuid[guid] = entity;
            if (position is { } p) _positions[entity.NetworkId] = p;
        }

        /// <summary>⭐ A5 / copy-paste (📄 docs/UX/DESIGN_Entity_Copy_Paste.md §1) — where a reference this scope does not hold resolves: the world OUTSIDE the
        /// slice (a pasted squad's commander that was not copied, a macro child's reference to the host scenario). Null = this scope only (a whole-scenario load).</summary>
        public IRefScope? Fallback { get; set; }

        public bool TryResolve(in RefSpec spec, out EntityRef entity)
        {
            entity = EntityRef.None;
            bool found = spec.Form switch
            {
                RefForm.Key   => TryKey(spec.KeyText, out entity),
                RefForm.Exact => _byGuid.TryGetValue(spec.Exact, out entity),
                _             => false,
            };
            return found || (Fallback != null && Fallback.TryResolve(spec, out entity));
        }

        public bool TryPositionOf(EntityRef entity, out WorldPoint position)
            => _positions.TryGetValue(entity.NetworkId, out position) || (Fallback != null && Fallback.TryPositionOf(entity, out position));

        private bool TryKey(string key, out EntityRef entity)
        {
            if (!_byKey.TryGetValue(key, out entity)) return false;
            if (_ambiguous.TryGetValue(key, out int n))
                Fdp.Core.Logging.FdpLog<ScenarioRefScope>.Warn($"@{key} matches {n} entities in {Describe}; using the first ({entity}) — SR-188");
            return true;
        }
    }
}
