using CarKinem.Core;
using Fdp.Kernel;

namespace FDP.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Bridges <see cref="NavigationIntent"/> (CQRS command written by the Brain tier via
    /// <see cref="Executors.MoveToExecutor"/>) into <see cref="NavState"/> (physics input
    /// consumed by <see cref="CarKinem.Systems.CarKinematicsSystem"/>).
    ///
    /// <para>
    /// This system is the "nervous system" adapter for the CQRS navigation contract
    /// (MOD1-P1T1/P1T2). It must run <b>after</b> <c>LocomotionDispatcherSystem</c>
    /// (so executors have already written <see cref="NavigationIntent"/>) and
    /// <b>before</b> <c>CarKinematicsSystem</c> (so the updated <see cref="NavState"/>
    /// is visible to the physics layer in the same tick).
    /// </para>
    ///
    /// <para>
    /// <b>Mapping rules:</b>
    /// <list type="bullet">
    ///   <item>If <see cref="NavigationIntent.Mode"/> is <see cref="NavigationMode.None"/> →
    ///     skip (no active intent; physics layer retains its current <see cref="NavState"/>).</item>
    ///   <item>Otherwise → copy <see cref="NavigationIntent.FinalDestination"/>,
    ///     <see cref="NavigationIntent.TargetSpeed"/>, <see cref="NavigationIntent.ArrivalRadius"/>
    ///     into <see cref="NavState"/> and set <see cref="NavState.Mode"/> to
    ///     <see cref="KinematicsMode.Direct"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// When the intent is cleared (<c>Mode → None</c> by <see cref="Executors.MoveToExecutor.OnExit"/>),
    /// this system stops overwriting <see cref="NavState"/>, allowing the vehicle to
    /// decelerate naturally or be controlled by a subsequent intent.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class NavigationIntentBridgeSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            var query = World.Query()
                .With<NavigationIntent>()
                .With<NavState>()
                .Build();

            foreach (var entity in query)
            {
                var intent = World.GetComponent<NavigationIntent>(entity);

                // Skip inactive intents — let NavState retain its current value.
                if (intent.Mode == NavigationMode.None)
                    continue;

                var nav = World.GetComponent<NavState>(entity);
                nav.Mode             = KinematicsMode.Direct;
                nav.FinalDestination = intent.FinalDestination;
                nav.TargetSpeed      = intent.TargetSpeed;
                nav.ArrivalRadius    = intent.ArrivalRadius;
                nav.HasArrived       = 0;
                World.SetComponent(entity, nav);
            }
        }
    }
}
