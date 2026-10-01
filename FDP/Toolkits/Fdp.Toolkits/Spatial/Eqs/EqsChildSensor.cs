using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ The ONE Brain-side lifecycle of a child EQS sensor — find, ensure, refresh, destroy.
    /// 📄 <c>docs/blueprints/DESIGN_Hill_Attack_Eqs_Migration.md</c> §3.1, §4 D1–D3; recipe: EQS design §17.6.
    ///
    /// <para>A child sensor is an entity carrying <see cref="PartMetadata"/> (parent + <c>InstanceId</c>),
    /// <see cref="EqsSensor"/> and <see cref="EqsCognitiveBuffer"/>. Its identity is <c>(parent, InstanceId)</c> — that is
    /// also its DDS key — so it is always FOUND by that pair, never remembered as the handle an ECB returned: an ECB
    /// <c>CreateEntity()</c> handle is a negative-index placeholder, valid only inside its own playback
    /// (<c>EntityCommandBuffer.CreateEntity</c>).</para>
    ///
    /// <para>Used by the C# hill-attack commander, the BTree <c>EqsLifecycleNodes</c> and the code the blueprint compiler emits
    /// for <c>SpawnEqsSensor</c> / the <c>RefreshEqsSensor</c> and <c>DestroyEqsSensor</c> built-ins.</para>
    /// </summary>
    public static class EqsChildSensor
    {
        /// <summary>The live child sensor of <paramref name="parent"/> with <paramref name="instanceId"/>, or <see cref="Entity.Null"/>.</summary>
        public static Entity Find(ISimulationView view, Entity parent, int instanceId)
        {
            // A fresh query each call: an EntityQuery caches component-array pointers that a structural change can move.
            foreach (var candidate in view.Query().With<PartMetadata>().With<EqsSensor>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(candidate);
                if (meta.ParentEntity.Equals(parent) && meta.InstanceId == instanceId)
                    return candidate;
            }
            return Entity.Null;
        }

        /// <summary>
        /// The child sensor, created on first call. ⚠ Returns <see cref="Entity.Null"/> on the call that CREATES it — the entity
        /// exists only after the command buffer plays back, so the caller waits a tick and asks again.
        /// </summary>
        public static Entity Ensure(ISimulationView view, Entity parent, int instanceId, in EqsSensor sensor)
        {
            var existing = Find(view, parent, instanceId);
            if (!existing.IsNull) return existing;

            var cmd   = view.GetCommandBuffer();
            var child = cmd.CreateEntity();
            cmd.AddComponent(child, new PartMetadata { ParentEntity = parent, InstanceId = instanceId, DescriptorOrdinal = 0 });
            cmd.AddComponent(child, sensor);
            cmd.AddComponent(child, default(EqsCognitiveBuffer));
            return Entity.Null;
        }

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
            sensor.Epoch++;
            if (view is EntityRepository repo)
            {
                repo.GetComponentRW<EqsSensor>(child) = sensor;
                if (repo.HasComponent<EqsCognitiveBuffer>(child))
                {
                    ref var buffer = ref repo.GetComponentRW<EqsCognitiveBuffer>(child);
                    buffer.Count = 0;
                    buffer.LastUpdateTick = 0;
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
