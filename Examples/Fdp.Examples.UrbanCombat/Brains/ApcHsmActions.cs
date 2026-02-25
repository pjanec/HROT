using Fhsm.Kernel.Data;

namespace Fdp.Examples.UrbanCombat.Brains
{
    /// <summary>
    /// HSM action delegates for the "ConvoyEscort_HSM" APC state machine (BCS-P7-T6).
    ///
    /// <para>
    /// The FastHSM kernel invokes actions via the <c>HsmActionDispatcher</c>, passing the
    /// instance pointer, an unmanaged context pointer, and a command writer.  In the FDP
    /// pipeline, the context is <c>HsmKernelBridge</c> (containing only <c>Entity Self</c>).
    /// Full <c>EntityRepository</c> access for HSM actions is deferred to a future wiring
    /// step (DEBT-007 partial — context struct exists, kernel path not yet threaded).
    /// </para>
    ///
    /// <para>
    /// These methods are referenced by name in <see cref="ApcHsmSetup.Build"/>
    /// (<c>builder.RegisterAction("Activity_Cruise")</c> etc.).  At runtime they would be
    /// bound via <c>HsmActionDispatcher.RegisterAction</c> (manually or via the
    /// <c>Fhsm.SourceGen</c> source generator if added to the project).
    /// </para>
    ///
    /// <para>
    /// <b>Delegate signature:</b> <c>unsafe void Method(void* instance, void* context, HsmCommandWriter* writer)</c>
    /// — this is the actual HSM action signature required by <c>HsmActionDispatcher</c>,
    /// not the <c>FdpHsmContext ctx</c> signature shown in the batch specification.
    /// </para>
    /// </summary>
    public static unsafe class ApcHsmActions
    {
        /// <summary>
        /// Activity action for the <c>Cruising</c> state.
        /// Intended behaviour: writes convoy-escort locomotion intent to
        /// <c>LocomotionChannel</c> each tick.
        ///
        /// <para>
        /// Note: full ECS write (via <c>EntityRepository</c>) is deferred until DEBT-007
        /// kernel wiring is completed.  The stub below compiles and runs safely — the
        /// HsmActionDispatcher will call it each tick while the APC is in Cruising state.
        /// </para>
        /// </summary>
        public static void Activity_Cruise(void* instance, void* context, HsmCommandWriter* writer)
        {
            // Stub — convoy-escort locomotion intent will be written here in BCS-P7-T7
            // when the ScenarioDirector wires up the full action context.
            // ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self).ActiveAction = NavigationConstants.ActionIdFollowRoute;
        }

        /// <summary>
        /// OnEntry action for the <c>Disabled</c> state.
        /// Intended behaviour: clears <c>LocomotionChannel</c> and writes
        /// <c>EjectPassengers</c> to <c>InteractionChannel</c>.
        ///
        /// <para>See <see cref="Activity_Cruise"/> note on DEBT-007 deferred wiring.</para>
        /// </summary>
        public static void OnEnter_Disabled(void* instance, void* context, HsmCommandWriter* writer)
        {
            // Stub — channel writes deferred until DEBT-007 kernel path is threaded.
            // ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self).ActiveAction = 0;
            // ctx.World.GetComponentRW<InteractionChannel>(ctx.Self).ActiveAction = InteractionConstants.EjectPassengers;
        }
    }
}
