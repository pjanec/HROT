using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> (B4; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7) — the aircraft's order nodes: <c>FlyTo</c> moves through
    /// the AIR — the point at a height above the ground, never on the surface.
    /// </summary>
    public class AirOrderNodesTests
    {
        private readonly EntityRepository _w = new();

        public AirOrderNodesTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
        }

        private unsafe MoveToParams MoveOf(Entity e)
        {
            var ch = _w.GetComponentRO<LocomotionChannel>(e);
            return *(MoveToParams*)ch.Params;
        }

        /// <summary>⭐ FlyTo issues ONE MoveTo to (X, Y) at the height above the ground (120 m by default, the asked height otherwise), with a
        /// stated Z (not on the surface), and then reports the move's own status. ✅ Red-proofs: Z on the ground ⇒ red; re-issuing every
        /// tick ⇒ a new action instance each tick ⇒ red.</summary>
        [Fact]
        public void B4_FlyTo_MovesThroughTheAir_AtTheHeightAboveTheGround_AndReportsTheMove()
        {
            var heli = _w.CreateEntity();
            _w.AddComponent(heli, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _w.AddComponent(heli, new LocomotionChannel());
            var p = new FlyToParams { X = 300f, Y = -40f };

            Assert.Equal(NodeStatus.Running, AirOrderNodes.FlyTo(ref p, heli, _w));
            var move = MoveOf(heli);
            Assert.Equal(new Vector3(300f, -40f, AirOrderNodes.DefaultCruiseHeightAgl), move.Destination);   // flat world: ground 0
            Assert.Equal(0, move.Flags & NavigationConstants.FlagDestinationOnSurface);                      // a stated height
            Assert.Equal(AirOrderNodes.DefaultArrivalRadius, move.ArrivalRadius);
            uint instance = _w.GetComponentRO<LocomotionChannel>(heli).ActionInstanceId;

            Assert.Equal(NodeStatus.Running, AirOrderNodes.FlyTo(ref p, heli, _w));
            Assert.Equal(instance, _w.GetComponentRO<LocomotionChannel>(heli).ActionInstanceId);              // issued once
            _w.GetComponentRW<LocomotionChannel>(heli).Status = NodeStatus.Success;
            Assert.Equal(NodeStatus.Success, AirOrderNodes.FlyTo(ref p, heli, _w));                          // the move arrived

            var low = new FlyToParams { X = 0f, Y = 0f, HeightAgl = 30f };
            Assert.Equal(NodeStatus.Running, AirOrderNodes.FlyTo(ref low, heli, _w));
            Assert.Equal(30f, MoveOf(heli).Destination.Z);
        }
    }
}
