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
    /// scenario (<c>"RefKey": {"Value": "BlueHQ"}</c>). ⛔ Unique per scope — a duplicate is a LOUD load error, never "the first one wins".
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

        public ScenarioRefScope(string describe) => Describe = describe;

        public string Describe { get; }

        /// <summary>Number of keyed entities (a probe for tests and the load log).</summary>
        public int KeyCount => _byKey.Count;

        /// <summary>Adds one entity of the scope. ⛔ A key already taken is a LOUD error naming both.</summary>
        public void Add(EntityRef entity, string? key, Guid guid, WorldPoint? position)
        {
            if (!string.IsNullOrEmpty(key))
            {
                if (_byKey.TryGetValue(key, out var other) && other != entity)
                    throw new InvalidOperationException($"two entities carry the key '{key}' ({other} and {entity}) in {Describe} — a key is unique per scope");
                _byKey[key] = entity;
            }
            if (guid != Guid.Empty) _byGuid[guid] = entity;
            if (position is { } p) _positions[entity.NetworkId] = p;
        }

        public bool TryResolve(in RefSpec spec, out EntityRef entity)
        {
            entity = EntityRef.None;
            return spec.Form switch
            {
                RefForm.Key   => _byKey.TryGetValue(spec.KeyText, out entity),
                RefForm.Exact => _byGuid.TryGetValue(spec.Exact, out entity),
                _             => false,
            };
        }

        public bool TryPositionOf(EntityRef entity, out WorldPoint position) => _positions.TryGetValue(entity.NetworkId, out position);
    }
}
