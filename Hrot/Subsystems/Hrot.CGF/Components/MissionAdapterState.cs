using Fdp.Core;

namespace Hrot.CGF.Components
{
    /// <summary>
    /// A transient cognitive shadow buffer used by the MissionAdapterSystem to detect 
    /// state changes and phase transitions within an entity's high-level mission plan.
    ///
    /// <para>
    /// <b>Clean Architecture & Reactive Design:</b>
    /// This component represents pure execution state. The MissionAdapterSystem acts as a 
    /// reactive change-detector, comparing the active MissionPlanQueue against this struct's 
    /// <see cref="LastPhase"/> and <see cref="LastPlanVersion"/> [1, 2]. 
    /// When a mismatch or phase exhaustion is detected, it extracts the behavior parameters 
    /// and publishes an AssignDoctrineEvent, bridging the mission into the cognitive tier 
    /// statelessly and safely without direct ECS mutation [2-4].
    /// </para>
    ///
    /// <para>
    /// <b>Serialization Policy:</b>
    /// This struct should never be persisted to scenario disk. By remaining strictly transient,
    /// we guarantee that newly 
    /// deserialized entities natively lack this component on the first tick. The system will 
    /// then automatically bootstrap it with a dummy state and seamlessly trigger the mission's 
    /// initial doctrine activation.
    /// </para>
    /// </summary>
    [ComponentId(129)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct MissionAdapterState
    {
        /// <summary>
        /// The index of the mission phase that was last evaluated and dispatched.
        /// </summary>
        public byte LastPhase;

        /// <summary>
        /// A hash combining the phase's DoctrineId and its JSON BehaviorParams. 
        /// Used to detect when a mission plan is explicitly restarted or re-committed 
        /// at the exact same phase [2].
        /// </summary>
        public uint LastPlanVersion;
    }
}
