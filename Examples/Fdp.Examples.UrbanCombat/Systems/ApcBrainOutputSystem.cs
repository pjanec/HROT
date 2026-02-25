using Fdp.Kernel;
using Fdp.Examples.UrbanCombat.Brains;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;

namespace Fdp.Examples.UrbanCombat.Systems
{
    /// <summary>
    /// Demo-application system that writes <c>EjectPassengers</c> (action ID 3) into the
    /// <see cref="InteractionChannel"/> of the APC entity once it enters the HSM
    /// <c>Disabled</c> state (<see cref="ApcHsmSetup.DisabledStateIndex"/>).
    ///
    /// <para>
    /// <b>Purpose:</b> Bridges the gap between the APC's HSM state machine reaching
    /// <c>Disabled</c> and the <c>EjectPassengersExecutor</c> which needs
    /// <c>InteractionChannel.ActiveAction == 3</c> to execute. In the full design, this
    /// write would be performed by <c>ApcHsmActions.OnEnter_Disabled</c> once DEBT-007
    /// (HSM context threading) is resolved. This system is a temporary stand-in.
    /// </para>
    ///
    /// <para>
    /// <b>Execution order:</b> Runs in <see cref="SimulationSystemGroup"/> after
    /// <see cref="HsmTickSystem{T}"/> so the state index reflects the transition that
    /// occurred in the current frame.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(HsmTickSystem<BrainHsm128>))]
    public class ApcBrainOutputSystem : ComponentSystem
    {
        protected override unsafe void OnUpdate()
        {
            var q = World.Query()
                .With<BrainHsm128>()
                .With<InteractionChannel>()
                .Build();

            foreach (var entity in q)
            {
                var brain = World.GetComponent<BrainHsm128>(entity);

                // Only act when APC is in the Disabled state.
                if (brain.State.ActiveLeafIds[0] != ApcHsmSetup.DisabledStateIndex)
                    continue;

                ref var channel = ref World.GetComponentRW<InteractionChannel>(entity);

                // Write EjectPassengers action (kind = 3) if not already set.
                if (channel.ActiveAction == 3)
                    continue;

                channel.ActiveAction = 3;   // EjectPassengers
            }
        }
    }
}
