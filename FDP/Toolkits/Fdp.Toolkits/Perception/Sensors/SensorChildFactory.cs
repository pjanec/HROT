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
            repo.AddComponent(child, default(EqsCognitiveBuffer));
            repo.AddComponent(child, new SensorTag { Kind = entry.Kind, TkbIndex = (byte)index, FromTkb = 1 });
            SetCapability(repo, child, entry, entry);
            DerivedParts.MarkNotSaved(repo, child);   // CE-3045
            return child;
        }

        /// <summary>The live sensor child of <paramref name="parent"/> with part id <paramref name="partId"/>, or null.</summary>
        public static Entity Find(ISimulationView view, Entity parent, int partId)
        {
            foreach (var e in view.Query().With<PartMetadata>().With<EqsSensor>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(e);
                if (meta.InstanceId == partId && meta.ParentEntity.Equals(parent)) return e;
            }
            return Entity.Null;
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
