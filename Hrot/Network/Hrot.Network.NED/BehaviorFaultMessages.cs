using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// ⭐ <b><c>CE-484</c> — a behaviour faulted: the operator notification on the wire.</b>
    /// 📄 <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c> §4c W4.
    ///
    /// <para>Event-shaped, on the <c>WeaponFire</c> precedent: published once by the node whose behaviour faulted
    /// (<c>BehaviorFaultEgressTranslator</c>, from the local <c>BehaviorFaultNotification</c>) and turned into a row of the
    /// Message Log's "Behaviour faults" tab on every node (<c>BehaviorFaultIngressTranslator</c>). Topic <c>"BehaviorFault"</c>,
    /// ordinal <see cref="Hrot.NED.Descriptors.EDescriptorType.dtBehaviorFault"/> = 97.</para>
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-behavior-fault")]
    [DdsManaged]
    public partial struct BehaviorFaultReport
    {
        /// <summary>Network entity id of the entity whose behaviour faulted.</summary>
        public long EntityId;

        /// <summary>The node that raised the fault (lets a node skip its own sample).</summary>
        public long OriginNodeId;

        /// <summary>The behaviour's registered name — its hash as hex text when the registry has no name.</summary>
        public string BehaviorName;

        /// <summary>The behaviour's hash (<c>BehaviorState.ActiveBehaviorHash</c>).</summary>
        public int BehaviorHash;

        /// <summary>The faulted run (<c>BehaviorState.InstanceId</c> on the origin node).</summary>
        public uint InstanceId;

        /// <summary>The fault code (<c>BehaviorFaultCode</c>, or a behaviour-specific code ≥ 1000).</summary>
        public int Code;

        /// <summary>The reason the behaviour gave.</summary>
        public string Message;

        /// <summary>Simulation time of the fault on the origin node.</summary>
        public double SimTime;
    }
}
