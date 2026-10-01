using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>What kind of wire key an EQS sensor has (<see cref="EqsSensorKey.Resolve"/>).</summary>
    public enum EqsSensorKeyKind
    {
        /// <summary>A child sensor whose parent is dead or has no network id — it has no key right now.</summary>
        None,
        /// <summary>A child sensor: <c>(parent's network id, PartMetadata.InstanceId)</c>.</summary>
        Child,
        /// <summary>A legacy sensor on a networked entity itself: <c>(its network id, 0)</c>.</summary>
        Legacy,
        /// <summary>A sensor with no network identity anywhere (offline / editor).</summary>
        LocalOnly,
    }

    /// <summary>
    /// ⭐⭐ <b>THE EQS sensor wire key, both directions, ONCE</b> — <c>(ParentNetworkId, LocalChildIndex)</c>, the compound
    /// key of <c>EqsSensorConfig</c> and <c>EqsResult</c> (EQS 1.3 H5). 📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D5.
    /// </summary>
    /// <remarks>
    /// <para>🔴 <b>Why it exists.</b> The entity → key direction was written out in the config egress and the solver
    /// (each commented "3-branch compound identity resolution"); the key → entity direction in the result ingress and in
    /// <c>EqsResultUpdateSystem</c>. Four copies of one rule — and <c>CE-487</c> had to change the match (part ids are now
    /// REUSED, so "is this entity still that sensor" must be asked on every cache hit). ⇒ one owner.</para>
    /// <para>⚠ The behaviour-side <c>EqsChildSensor.Find</c> matches a child by its parent ENTITY and site; this type
    /// answers the WIRE question (by the parent's network id). Different question, so a different seam.</para>
    /// </remarks>
    public static class EqsSensorKey
    {
        /// <summary>
        /// The wire key of <paramref name="sensor"/>. <paramref name="parent"/> is the child's parent entity (null for a
        /// legacy or local-only sensor).
        /// </summary>
        public static EqsSensorKeyKind Resolve(
            ISimulationView view, Entity sensor,
            out long parentNetworkId, out int localChildIndex, out Entity parent)
        {
            parentNetworkId = 0;
            localChildIndex = 0;
            parent          = Entity.Null;

            if (view.HasComponent<PartMetadata>(sensor))
            {
                var meta = view.GetComponentRO<PartMetadata>(sensor);
                parent = meta.ParentEntity;
                if (!view.IsAlive(parent) || !view.HasComponent<NetworkIdentity>(parent))
                    return EqsSensorKeyKind.None;
                parentNetworkId = view.GetComponentRO<NetworkIdentity>(parent).Value;
                localChildIndex = meta.InstanceId;
                return EqsSensorKeyKind.Child;
            }

            if (view.HasComponent<NetworkIdentity>(sensor))
            {
                parentNetworkId = view.GetComponentRO<NetworkIdentity>(sensor).Value;
                return EqsSensorKeyKind.Legacy;
            }

            return EqsSensorKeyKind.LocalOnly;
        }

        /// <summary>
        /// ⭐ <c>CE-487</c> — <paramref name="e"/> is, right now, the CHILD sensor for <c>(parent, part id)</c>: alive, an
        /// <see cref="EqsSensor"/>, and its <c>PartMetadata</c> names a live parent with that network id and that part
        /// id. ⚠ Requiring the sensor also keeps a non-sensor part (animation, weapon) that shares the instance id out.
        /// </summary>
        public static bool IsChildSensor(ISimulationView view, Entity e, long parentNetworkId, int localChildIndex)
        {
            if (e.IsNull || !view.IsAlive(e) || !view.HasComponent<EqsSensor>(e)) return false;
            return Resolve(view, e, out long net, out int index, out _) == EqsSensorKeyKind.Child
                && net   == parentNetworkId
                && index == localChildIndex;
        }
    }
}
