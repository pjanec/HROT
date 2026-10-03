using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ The ONE answer to "who am I, where am I, where is the target" for every EQS generator and test
    /// (docs/designs/eqs-2/EQS_Design_v1.3_final.md §19.5).
    /// <para>🔴 Why it exists: on the Muscle a CHILD sensor lives on a carrier entity that has <see cref="PartMetadata"/>,
    /// <see cref="EqsSensor"/> and a buffer — no <see cref="SimTransform"/>. Every block used to read the OBSERVER's transform,
    /// so a child sensor generated nothing and every test skipped (§19.1 H3).</para>
    /// </summary>
    public static class EqsContext
    {
        /// <summary>The entity the query is FOR: context slot 0 when it has a position, else the observer when it has one,
        /// else the carrier's parent. <see cref="Entity.Null"/> when none of them is placed.</summary>
        public static Entity Self(ISimulationView view, Entity observer, in EqsSensor sensor)
        {
            if (Placed(view, sensor.ContextSlot0)) return sensor.ContextSlot0;
            if (Placed(view, observer)) return observer;
            if (view.IsAlive(observer) && view.HasComponent<PartMetadata>(observer))
            {
                var parent = view.GetComponentRO<PartMetadata>(observer).ParentEntity;
                if (Placed(view, parent)) return parent;
            }
            return Entity.Null;
        }

        /// <summary>The position of <see cref="Self"/>; false when the query has no placed self.</summary>
        public static bool SelfPosition(ISimulationView view, Entity observer, in EqsSensor sensor, out Vector3 position)
        {
            var self = Self(view, observer, sensor);
            position = self.IsNull ? default : view.GetComponentRO<SimTransform>(self).Position;
            return !self.IsNull;
        }

        /// <summary>The entity in context slot <paramref name="slot"/> (0..2).</summary>
        public static Entity Slot(in EqsSensor sensor, byte slot) => slot switch
        {
            0 => sensor.ContextSlot0,
            2 => sensor.ContextSlot2,
            _ => sensor.ContextSlot1,
        };

        /// <summary>The entity a test or generator is anchored on: slot 0 resolves through <see cref="Self"/> (a child sensor's
        /// slot 0 is often empty); slots 1–2 are taken as written. Null when it is not placed.</summary>
        public static Entity Anchor(ISimulationView view, Entity observer, in EqsSensor sensor, byte slot)
        {
            if (slot == 0) return Self(view, observer, sensor);
            var e = Slot(sensor, slot);
            return Placed(view, e) ? e : Entity.Null;
        }

        /// <summary>The position of <see cref="Anchor"/>.</summary>
        public static bool AnchorPosition(ISimulationView view, Entity observer, in EqsSensor sensor, byte slot, out Vector3 position)
        {
            var e = Anchor(view, observer, sensor, slot);
            position = e.IsNull ? default : view.GetComponentRO<SimTransform>(e).Position;
            return !e.IsNull;
        }

        /// <summary>The navmesh layer the self plans on (<c>CE-3025</c>'s rule — a vehicle's points must come from the vehicle
        /// mesh). All layers when there is no self.</summary>
        public static uint SelfLayer(EntityRepository repo, Entity observer, in EqsSensor sensor)
        {
            var self = Self(repo, observer, sensor);
            return self.IsNull ? 0xFFFFFFFFu : (uint)NavLayerSelection.For(repo, self, 0);
        }

        /// <summary>Whose <c>TargetMemory</c> gates the threat-score threshold: the self's, else the observer's (a sensor placed
        /// directly on a Brain entity); <see cref="Entity.Null"/> when neither has one — then the gate does not apply.</summary>
        public static Entity ThreatMemoryOwner(ISimulationView view, Entity observer, Entity self)
        {
            if (!self.IsNull && view.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(self)) return self;
            if (view.IsAlive(observer) && view.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(observer)) return observer;
            return Entity.Null;
        }

        private static bool Placed(ISimulationView view, Entity e)
            => !e.IsNull && view.IsAlive(e) && view.HasComponent<SimTransform>(e);
    }
}
