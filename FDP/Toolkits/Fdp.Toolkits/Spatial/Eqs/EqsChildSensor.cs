using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ The ONE Brain-side lifecycle of a child EQS sensor — find, ensure, refresh, destroy.
    /// 📄 <c>docs/blueprints/DESIGN_Hill_Attack_Eqs_Migration.md</c> §3.1, §4 D1–D3; recipe: EQS design §17.6.
    ///
    /// <para>A child sensor is an entity carrying <see cref="PartMetadata"/> (parent + part id), <see cref="EqsSensor"/>,
    /// <see cref="EqsCognitiveBuffer"/> and — ⭐ CE-485 — a <see cref="BehaviorOwnedPart"/> stamp (owning run, site, key).
    /// The behaviour FINDS its sensor by the stamp; the part id is only the network address (<c>(parent, part id)</c> is the
    /// DDS key), allocated and reused. 📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D4/D5.</para>
    ///
    /// <para>Used by the C# hill-attack commander, the BTree <c>EqsLifecycleNodes</c> and the code the blueprint compiler emits
    /// for <c>SpawnEqsSensor</c> / the <c>RefreshEqsSensor</c> and <c>DestroyEqsSensor</c> built-ins.</para>
    /// </summary>
    public static class EqsChildSensor
    {
        /// <summary>
        /// ⭐ <b>CE-485</b> — the live child sensor that the parent's CURRENT behaviour run created at <paramref name="siteId"/>
        /// (and <paramref name="key"/>), or <see cref="Entity.Null"/>. 📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D5.
        /// <para>⭐ Matched on the brain-local <see cref="BehaviorOwnedPart"/> stamp, never on the part id: the part id is an
        /// allocated, reused network address, and a sensor of an earlier run is never this run's sensor.</para>
        /// </summary>
        public static Entity Find(ISimulationView view, Entity parent, int siteId, long key = 0)
        {
            uint owner = BehaviorOwnedParts.OwnerOf(view, parent);
            // A fresh query each call: an EntityQuery caches component-array pointers that a structural change can move.
            foreach (var candidate in view.Query().With<PartMetadata>().With<EqsSensor>().With<BehaviorOwnedPart>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(candidate);
                if (!meta.ParentEntity.Equals(parent)) continue;
                ref readonly var stamp = ref view.GetComponentRO<BehaviorOwnedPart>(candidate);
                if (stamp.SiteId == siteId && stamp.Key == key && stamp.OwnerInstanceId == owner)
                    return candidate;
            }
            return Entity.Null;
        }

        /// <summary>
        /// The child sensor of the current run at <paramref name="siteId"/>/<paramref name="key"/>, created on first call.
        /// <list type="bullet">
        ///   <item>⭐ The part id (<see cref="PartMetadata.InstanceId"/>, the DDS key) is ALLOCATED: the lowest id ≥ 1 not held by
        ///     a live sensor child of the parent — the children ARE the table (§1 D5 ①). Ids are reused; the descriptor
        ///     instance is never disposed while the parent lives (BDC/NED descriptor rules).</item>
        ///   <item>⭐ The epoch's high 16 bits carry the owning run (§1 D5 ②), so an answer computed for an earlier run's
        ///     sensor on a reused part id fails <c>EqsResultUpdateSystem</c>'s epoch check.</item>
        ///   <item>⭐ On the live world (<see cref="EntityRepository"/> — what every behaviour tick is handed) the child is
        ///     created IMMEDIATELY and returned, so a second creation in the same frame sees it and takes the next id.
        ///     ⚠ On any other view it goes through the command buffer and <see cref="Entity.Null"/> is returned; only one
        ///     creation per parent per frame is then safe (unit-test views only — measured, no production caller).</item>
        /// </list>
        /// </summary>
        public static Entity Ensure(ISimulationView view, Entity parent, int siteId, in EqsSensor sensor, long key = 0)
            => Ensure<EqsCognitiveBuffer>(view, parent, siteId, sensor, key);

        /// <summary>
        /// ⭐ <c>CE-3072</c> B0 — <see cref="Ensure(ISimulationView, Entity, int, in EqsSensor, long)"/> for a sensor whose
        /// answer is of another result family: the child gets an empty <typeparamref name="TResult"/> (e.g.
        /// <c>DangerAreaCognitiveBuffer</c>) INSTEAD of <see cref="EqsCognitiveBuffer"/>. ⭐ One creation body for every family
        /// — the part-id allocation, owner stamp and epoch rules above are the same.
        /// </summary>
        public static Entity Ensure<TResult>(ISimulationView view, Entity parent, int siteId, in EqsSensor sensor, long key = 0)
            where TResult : unmanaged
        {
            var existing = Find(view, parent, siteId, key);
            if (!existing.IsNull) return existing;

            uint owner  = BehaviorOwnedParts.OwnerOf(view, parent);
            int partId  = AllocatePartId(view, parent);
            var config  = sensor;
            config.Epoch = StampOwner(config.Epoch, owner);
            var meta    = new PartMetadata { ParentEntity = parent, InstanceId = partId };
            var stamp   = new BehaviorOwnedPart { OwnerInstanceId = owner, SiteId = siteId, Key = key };

            if (view is EntityRepository repo)
            {
                var child = repo.CreateEntity();
                repo.AddComponent(child, meta);
                repo.AddComponent(child, config);
                repo.AddComponent(child, default(TResult));
                repo.AddComponent(child, stamp);
                Fdp.Toolkit.Scenario.DerivedParts.MarkNotSaved(repo, child);   // CE-3045 — never saved
                return child;
            }

            var cmd     = view.GetCommandBuffer();
            var pending = cmd.CreateEntity();
            cmd.AddComponent(pending, meta);
            cmd.AddComponent(pending, config);
            cmd.AddComponent(pending, default(TResult));
            cmd.AddComponent(pending, stamp);
            Fdp.Toolkit.Scenario.DerivedParts.MarkNotSaved(cmd, pending, view);   // CE-3045
            return Entity.Null;
        }

        /// <summary>The lowest part id ≥ 1 not held by a live EQS sensor child of <paramref name="parent"/> (0 is the legacy
        /// "sensor on the entity itself").</summary>
        public static int AllocatePartId(ISimulationView view, Entity parent)
        {
            ulong low = 0;                       // ids 1..64 as a bitset — the common case
            System.Collections.Generic.HashSet<int>? high = null;
            foreach (var candidate in view.Query().With<PartMetadata>().With<EqsSensor>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(candidate);
                if (!meta.ParentEntity.Equals(parent)) continue;
                int id = meta.InstanceId;
                if (id >= 1 && id <= 64) low |= 1ul << (id - 1);
                else if (id > 64) (high ??= new System.Collections.Generic.HashSet<int>()).Add(id);
            }
            for (int id = 1; id <= 64; id++)
                if ((low & (1ul << (id - 1))) == 0) return id;
            int next = 65;
            while (high != null && high.Contains(next)) next++;
            // ⭐ CE-3036 — ids from 1000 are the unit's TKB sensors (Perception.Sensors.SensorChildFactory.FirstTkbPartId).
            if (next >= 1000)
                throw new System.InvalidOperationException(
                    $"Entity {parent} already has 999 behaviour-made sensors; ids from 1000 belong to its TKB sensors.");
            return next;
        }

        /// <summary>The epoch with its high 16 bits set to the owning run (low 16 bits = the refresh count).
        /// ⭐ CE-3049 — the owner's 32 bits are FOLDED into 16 (low half XOR high half), not truncated: a token from
        /// another slot (a doctrine's runs carry the high bit — DESIGN_Sensors_And_Doctrine §6) no longer stamps the same
        /// as the behaviour run with the same low bits. Behaviour tokens below 65 536 stamp exactly as before.</summary>
        public static uint StampOwner(uint epoch, uint ownerInstanceId)
            => ((((ownerInstanceId & 0xFFFFu) ^ (ownerInstanceId >> 16)) & 0xFFFFu) << 16) | (epoch & 0xFFFFu);

        /// <summary>
        /// ⭐ CE-3049 — the next epoch of a sensor: bumps the refresh count (low 16 bits) and NEVER the owner stamp (high
        /// 16). 🔴 A plain <c>Epoch++</c> carried a refresh overflow into the owner bits. Every refresh goes through here.
        /// </summary>
        public static uint NextEpoch(uint epoch) => (epoch & 0xFFFF0000u) | ((epoch + 1u) & 0xFFFFu);

        /// <summary>
        /// Ask again: bump the sensor's <c>Epoch</c> and clear its buffer, so <see cref="EqsCognitiveBuffer.IsReady"/> turns true
        /// only on an answer computed for the NEW epoch — the Brain drops every older one (<c>EqsResultUpdateSystem</c>).
        /// Returns false when <paramref name="child"/> is not a live sensor.
        /// </summary>
        public static bool Refresh(ISimulationView view, Entity child)
        {
            if (child.IsNull || child.Index < 0 || !view.IsAlive(child) || !view.HasComponent<EqsSensor>(child)) return false;
            var sensor = view.GetComponentRO<EqsSensor>(child);
            return Apply(view, child, sensor);
        }

        /// <summary>
        /// <see cref="Refresh(ISimulationView, Entity)"/>, re-asking with <paramref name="config"/> (every field but
        /// <c>Epoch</c>, which is bumped from the live sensor's). ⭐ A sensor re-found after an aborted run keeps the area it
        /// had; this is how the next run points it at its own.
        /// </summary>
        public static bool Refresh(ISimulationView view, Entity child, in EqsSensor config)
        {
            if (child.IsNull || child.Index < 0 || !view.IsAlive(child) || !view.HasComponent<EqsSensor>(child)) return false;
            var sensor = config;
            sensor.Epoch = view.GetComponentRO<EqsSensor>(child).Epoch;
            return Apply(view, child, sensor);
        }

        private static bool Apply(ISimulationView view, Entity child, EqsSensor sensor)
        {
            // ⭐ CE-485: only the low 16 bits count refreshes; the high 16 carry the owning run and never change.
            sensor.Epoch = NextEpoch(sensor.Epoch);
            if (view is EntityRepository repo)
            {
                repo.GetComponentRW<EqsSensor>(child) = sensor;
                if (repo.HasComponent<EqsCognitiveBuffer>(child))
                {
                    ref var buffer = ref repo.GetComponentRW<EqsCognitiveBuffer>(child);
                    buffer.Count = 0;
                    buffer.LastUpdateTick = 0;
                }
                else if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>()
                         && repo.HasComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>(child))
                {
                    // ⭐ CE-3072 B0 — an area-family sensor clears ITS answer; ⛔ never gets a ranked buffer added
                    ref var area = ref repo.GetComponentRW<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>(child);
                    area.Count = 0;
                    area.LastUpdateTick = 0;
                }
                else
                {
                    repo.AddComponent(child, default(EqsCognitiveBuffer));
                }
            }
            else
            {
                var cmd = view.GetCommandBuffer();
                cmd.SetComponent(child, sensor);
                cmd.SetComponent(child, default(EqsCognitiveBuffer));
            }
            return true;
        }

        /// <summary>Destroy the child sensor (its config is disposed; the Muscle carrier goes with it — EQS §17.6). Null / dead ⇒ no-op.</summary>
        public static void Destroy(ISimulationView view, Entity child)
        {
            if (child.IsNull || child.Index < 0 || !view.IsAlive(child)) return;
            view.GetCommandBuffer().DestroyEntity(child);
        }
    }
}
