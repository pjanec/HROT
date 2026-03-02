using System;
using System.Runtime.InteropServices;
using Fdp.Examples.UrbanCombat;
using Fdp.Examples.UrbanCombat.Brains;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Navigation;
using Xunit;

namespace Fdp.Examples.UrbanCombat.Tests
{
    /// <summary>
    /// BATCH-17 DEBT-007 unit tests.
    ///
    /// <list type="table">
    ///   <item><term>T1</term><description><c>UnmanagedHandle</c> GCHandle round-trip — proves <c>EntityRepository</c> can be recovered from its <c>IntPtr</c>.</description></item>
    ///   <item><term>T2</term><description><c>Activity_Cruise</c> writes <c>ActionIdFollowRoute</c> to <c>LocomotionChannel</c>.</description></item>
    ///   <item><term>T3</term><description><c>OnEnter_Disabled</c> clears locomotion and writes <c>ActionIdEjectPassengers</c> to <c>InteractionChannel</c>.</description></item>
    /// </list>
    /// </summary>
    [Collection("SerialTests")]
    public unsafe class ApcBrainTests : IDisposable
    {
        private readonly HeadlessDemoApp _app;

        public ApcBrainTests()
        {
            _app = new HeadlessDemoApp();
            _app.Initialize();
        }

        public void Dispose() => _app.Dispose();

        // ── T1 ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void UnmanagedHandle_RecoveredTarget_IsSameInstance()
        {
            // Act
            IntPtr handle    = _app.World.UnmanagedHandle;
            var    recovered = (EntityRepository)GCHandle.FromIntPtr(handle).Target!;

            // Assert — proves the GCHandle round-trip is correct
            Assert.True(object.ReferenceEquals(_app.World, recovered));
        }

        // ── T2 ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void HsmAction_ActivityCruise_WritesFollowRoute_ToLocomotionChannel()
        {
            // Arrange
            var entity = _app.World.CreateEntity();
            _app.World.AddComponent(entity, new DoctrineState { InstanceId = 1, ActiveDoctrineHash = 1 });
            _app.World.AddComponent(entity, new LocomotionChannel());

            var bridge = new HsmKernelBridge
            {
                Self        = entity,
                WorldHandle = _app.World.UnmanagedHandle,
            };

            // Act
            ApcHsmActions.Activity_Cruise(null, &bridge, null);

            // Assert
            var loco = _app.World.GetComponent<LocomotionChannel>(entity);
            Assert.Equal(NavigationConstants.ActionIdFollowRoute, loco.ActiveAction);
        }

        // ── T3 ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void HsmAction_OnEnterDisabled_ClearsLocomotion_AndWritesEject()
        {
            // Arrange
            var entity = _app.World.CreateEntity();
            _app.World.AddComponent(entity, new DoctrineState { InstanceId = 2, ActiveDoctrineHash = 1 });
            _app.World.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction = NavigationConstants.ActionIdFollowRoute,
            });
            _app.World.AddComponent(entity, new InteractionChannel());

            var bridge = new HsmKernelBridge
            {
                Self        = entity,
                WorldHandle = _app.World.UnmanagedHandle,
            };

            // Act
            ApcHsmActions.OnEnter_Disabled(null, &bridge, null);

            // Assert
            var loco     = _app.World.GetComponent<LocomotionChannel>(entity);
            var interact = _app.World.GetComponent<InteractionChannel>(entity);
            Assert.Equal((ushort)0, loco.ActiveAction);
            Assert.Equal(BehaviorConstants.ActionIdEjectPassengers, interact.ActiveAction);
        }
    }
}
