using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐⭐ <b>The ONE builder of a sensor child</b> (docs/DESIGN_Sensors_And_Doctrine.md §4, §5.1). A TKB sensor and a
    /// behaviour-made sensor come out of the same shape: <see cref="PartMetadata"/> (parent + part id — the wire key),
    /// <see cref="EqsSensor"/>, <see cref="EqsCognitiveBuffer"/>, <see cref="SensorTag"/>, <see cref="SensorCapability"/>.
    /// <para>⭐ Every node that spawns the unit builds its TKB sensors here (R-185 K): both sides derive the SAME part id
    /// (<see cref="FirstTkbPartId"/> + index) from the TKB, so the existing result key matches with no config on the wire.</para>
    /// <para>⭐ CE-3045 — every child is a DERIVED part and is tagged <see cref="ScenarioIgnoreTag"/>: it is rebuilt from the
    /// TKB / by its behaviour on load, so saving it too duplicated it.</para>
    /// </summary>
    public static class SensorChildFactory
    {
        /// <summary>Part ids at and above this are TKB sensors (<c>1000 + index</c>); behaviour-made sensors are allocated below it.</summary>
        public const int FirstTkbPartId = 1000;

        /// <summary>The EQS sensor a TKB entry stands for. Epoch 1; OFF when the entry is <see cref="SensorEntryDto.Disabled"/> (R-187).</summary>
        public static EqsSensor SensorFor(SensorEntryDto entry) => new()
        {
            BlueprintId   = entry.Template == Guid.Empty ? 0u : EqsTemplateRegistry.BlueprintIdOf(entry.Template),
            Epoch         = 1u,
            SearchRadius  = entry.SearchRadius > 0f ? entry.SearchRadius : entry.Range,
            PublishPolicy = (byte)EqsPublishPolicy.TopChanged,
            Priority      = (byte)EqsPriorityBand.Normal,
            Suspended     = entry.Disabled,
        };

        /// <summary>
        /// Builds the unit's TKB sensor child for <paramref name="entry"/> at list position <paramref name="index"/>, unless it
        /// already exists (a translator can run twice for one entity — spawn and ghost promotion). Returns the child, or
        /// <see cref="Entity.Null"/> when this node does not host sensors (the EQS types are not registered — e.g. an IG) or
        /// the entry is not buildable (no template yet, or malformed).
        /// </summary>
        public static Entity EnsureTkbChild(EntityRepository repo, Entity parent, int index, SensorEntryDto entry)
        {
            if (!HostsSensors(repo)) return Entity.Null;
            if (entry.Template == Guid.Empty || !entry.IsWellFormed) return Entity.Null;

            int partId = FirstTkbPartId + index;
            var existing = Find(repo, parent, partId);
            if (!existing.IsNull) return existing;

            var child = repo.CreateEntity();
            repo.AddComponent(child, new PartMetadata { ParentEntity = parent, InstanceId = partId });
            repo.AddComponent(child, SensorFor(entry));
            // ⭐ CE-3072 B0 — the child carries the result component of ITS KIND'S FAMILY (R-213: no narrowing cast)
            if (SensorKindRegistry.FamilyOf(entry.Kind) == SensorResultFamily.Area
                && repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>()
                && repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>())
            {
                repo.AddComponent(child, default(Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer));
                var settings = Fdp.Toolkit.Squad.DangerArea.DangerAreaSettings.Default;
                repo.AddComponent(child, new Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor
                {
                    BlueprintId = Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.TemplateId,
                    RefreshIntervalSeconds = settings.RefreshSeconds,
                    Settings = settings,
                });
            }
            else
            {
                repo.AddComponent(child, default(EqsCognitiveBuffer));
            }
            repo.AddComponent(child, new SensorTag { Kind = entry.Kind, TkbIndex = (byte)index, FromTkb = 1 });
            SetCapability(repo, child, entry, entry);
            DerivedParts.MarkNotSaved(repo, child);   // CE-3045
            ChildIndex.Of(repo).Added(repo, parent, partId, child);   // CE-3242
            return child;
        }

        /// <summary>The live sensor child of <paramref name="parent"/> with part id <paramref name="partId"/>, or null.</summary>
        public static Entity Find(ISimulationView view, Entity parent, int partId)
        {
            if (view is EntityRepository repo) return ChildIndex.Of(repo).Find(repo, parent, partId);
            foreach (var e in view.Query().With<PartMetadata>().With<EqsSensor>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(e);
                if (meta.InstanceId == partId && meta.ParentEntity.Equals(parent)) return e;
            }
            return Entity.Null;
        }

        /// <summary>
        /// ⭐ <c>CE-3242</c> — the sensor children by (parent, part id), built by ONE scan and reused while nothing else changed the
        /// world. Was a scan of every sensor child per <see cref="Find"/>, and every unit's promotion asks it once per sensor:
        /// O(units × sensors) — 📐 1.1 ms a call at 1 500 entities, a 500-civilian load trickled in for 93 s.
        /// <para>⭐ Rebuilt when the frame moves on or the entity count is not what this factory's own creations left it at — so a
        /// child made elsewhere (the EQS config ingress, a restore) is seen; a hit is still checked live, so a stale entry is never
        /// returned. First match in index order wins, as the scan's did.</para>
        /// </summary>
        private sealed class ChildIndex
        {
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, ChildIndex> PerWorld = new();
            private readonly System.Collections.Generic.Dictionary<(Entity Parent, int PartId), Entity> _children = new();
            private uint _tick = uint.MaxValue;
            private int _count = -1;

            public static ChildIndex Of(EntityRepository repo) => PerWorld.GetValue(repo, _ => new ChildIndex());

            public Entity Find(EntityRepository repo, Entity parent, int partId)
            {
                if (_tick != repo.SimulationTick || _count != repo.EntityCount) Rebuild(repo);
                if (!_children.TryGetValue((parent, partId), out var child)) return Entity.Null;
                if (repo.IsAlive(child) && repo.HasComponent<EqsSensor>(child) && repo.HasComponent<PartMetadata>(child))
                {
                    ref readonly var meta = ref repo.GetComponentRO<PartMetadata>(child);
                    if (meta.InstanceId == partId && meta.ParentEntity.Equals(parent)) return child;
                }
                Rebuild(repo);   // the entry went stale without the count moving (a destroy and a create) — ask the world
                return _children.TryGetValue((parent, partId), out child) ? child : Entity.Null;
            }

            /// <summary>Records a child this factory just made, so the next lookup needs no rescan.</summary>
            public void Added(EntityRepository repo, Entity parent, int partId, Entity child)
            {
                if (_tick != repo.SimulationTick || _count != repo.EntityCount - 1) { _count = -1; return; }   // stale anyway: rebuild next time
                _children.TryAdd((parent, partId), child);
                _count = repo.EntityCount;
            }

            private void Rebuild(EntityRepository repo)
            {
                _children.Clear();
                foreach (var e in repo.Query().With<PartMetadata>().With<EqsSensor>().Build())
                {
                    ref readonly var meta = ref repo.GetComponentRO<PartMetadata>(e);
                    _children.TryAdd((meta.ParentEntity, meta.InstanceId), e);
                }
                _tick = repo.SimulationTick;
                _count = repo.EntityCount;
            }
        }

        /// <summary>Sets the child's capability (registering the managed type on first use, as other managed components do).</summary>
        public static void SetCapability(EntityRepository repo, Entity child, SensorEntryDto defaultEntry, SensorEntryDto current)
        {
            repo.RegisterManagedComponent<SensorCapability>();   // idempotent — registered on first use, as BehaviorStartRecord is
            repo.SetManagedComponent(child, new SensorCapability { Default = defaultEntry, Current = current });
        }

        /// <summary>True when this node registers the sensor-child component types.</summary>
        public static bool HostsSensors(EntityRepository repo)
            => repo.IsComponentTypeRegistered<PartMetadata>()
            && repo.IsComponentTypeRegistered<EqsSensor>()
            && repo.IsComponentTypeRegistered<EqsCognitiveBuffer>()
            && repo.IsComponentTypeRegistered<SensorTag>();
    }
}
