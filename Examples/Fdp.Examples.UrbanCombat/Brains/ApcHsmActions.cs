using System.Runtime.InteropServices;
using Fdp.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Navigation;

namespace Fdp.Examples.UrbanCombat.Brains
{
    /// <summary>
    /// HSM action delegates for the "ConvoyEscort_HSM" APC state machine (BCS-P7-T6).
    ///
    /// <para>
    /// The FastHSM kernel invokes actions via the <c>HsmActionDispatcher</c>, passing the
    /// instance pointer, an unmanaged context pointer, and a command writer.  In the FDP
    /// pipeline, the context is <c>HsmKernelBridge</c> (containing <c>Entity Self</c> and
    /// <c>IntPtr WorldHandle</c>).  Action delegates recover the <see cref="EntityRepository"/>
    /// from the long-lived <c>GCHandle</c> stored in <c>WorldHandle</c> — zero per-frame
    /// allocation, no managed references in the unmanaged bridge.
    /// See DEBT-007-HSM-ANALYSIS.md for full explanation.
    /// </para>
    ///
    /// <para>
    /// These methods are referenced by name in <see cref="ApcHsmSetup.Build"/>
    /// (<c>builder.RegisterAction("Activity_Cruise")</c> etc.).  At runtime they are
    /// bound via <c>HsmActionDispatcher.RegisterAction</c> (manually or via the
    /// <c>Fhsm.SourceGen</c> source generator if added to the project).
    /// </para>
    ///
    /// <para>
    /// <b>Delegate signature:</b> <c>unsafe void Method(void* instance, void* context, HsmCommandWriter* writer)</c>
    /// — this is the actual HSM action signature required by <c>HsmActionDispatcher</c>.
    /// </para>
    /// </summary>
    public static unsafe class ApcHsmActions
    {
        /// <summary>
        /// Activity action for the <c>Cruising</c> state.
        /// Runs every tick while the APC is Cruising.
        /// Writes <see cref="NavigationConstants.ActionIdFollowRoute"/> to
        /// <see cref="LocomotionChannel"/> so the vehicle follows its road-graph route northward.
        /// </summary>
        [HsmAction]
        public static void Activity_Cruise(void* instance, void* context, HsmCommandWriter* writer)
        {
            var bridge = (HsmKernelBridge*)context;
            var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

            ref var loco    = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
            var     doctrine = repo.GetComponent<DoctrineState>(bridge->Self);

            loco.ActiveAction       = NavigationConstants.ActionIdFollowRoute;
            loco.DoctrineInstanceId = doctrine.InstanceId;
        }

        /// <summary>
        /// OnEntry action for the <c>Disabled</c> state.
        /// Fires exactly once when the HSM transitions into Disabled (on <c>MobilityLost</c> event).
        /// Clears <see cref="LocomotionChannel"/> and writes
        /// <see cref="BehaviorConstants.ActionIdEjectPassengers"/> to <see cref="InteractionChannel"/>.
        /// </summary>
        [HsmAction]
        public static void OnEnter_Disabled(void* instance, void* context, HsmCommandWriter* writer)
        {
            var bridge = (HsmKernelBridge*)context;
            var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

            var doctrine = repo.GetComponent<DoctrineState>(bridge->Self);

            // Stop movement (guard: minimal test worlds may not register LocomotionChannel)
            if (repo.HasComponent<LocomotionChannel>(bridge->Self))
            {
                ref var loco = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
                loco.ActiveAction = 0;
            }

            // Trigger passenger eject — fires exactly once on OnEntry
            if (repo.HasComponent<InteractionChannel>(bridge->Self))
            {
                ref var interact = ref repo.GetComponentRW<InteractionChannel>(bridge->Self);
                interact.ActiveAction       = BehaviorConstants.ActionIdEjectPassengers;
                interact.DoctrineInstanceId = doctrine.InstanceId;
                unchecked { interact.ActionInstanceId++; }
            }
        }
    }
}
