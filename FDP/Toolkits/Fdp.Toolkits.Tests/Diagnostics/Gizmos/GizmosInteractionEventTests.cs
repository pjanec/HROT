using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    public class GizmosInteractionEventTests
    {
        // SC-GZ009-1: All four structs satisfy where T : unmanaged.
        // Verified by calling RegisterEvent<T>() which has an unmanaged constraint.
        // If any struct were not unmanaged, the call would not compile.
        [Fact]
        public void SC_GZ009_1_AllFourStructsSatisfyUnmanagedConstraint()
        {
            var repo = GizmoTestRepo.Create();

            // Each RegisterEvent call exercises the `where T : unmanaged` constraint at compile-time.
            var ex1 = Record.Exception(() => repo.RegisterEvent<GizmoInteractionStartedEvent>());
            var ex2 = Record.Exception(() => repo.RegisterEvent<GizmoDragUpdateEvent>());
            var ex3 = Record.Exception(() => repo.RegisterEvent<GizmoInteractionCommitEvent>());
            var ex4 = Record.Exception(() => repo.RegisterEvent<GizmoInteractionCancelEvent>());

            Assert.Null(ex1);
            Assert.Null(ex2);
            Assert.Null(ex3);
            Assert.Null(ex4);
        }

        // SC-GZ009-2: Publish GizmoDragUpdateEvent, swap buffers, read back — Token and WorldPos round-trip.
        [Fact]
        public void SC_GZ009_2_GizmoDragUpdateEvent_RoundTripsTokenAndWorldPos()
        {
            var repo = GizmoTestRepo.Create();
            repo.RegisterEvent<GizmoDragUpdateEvent>();

            var entity = repo.CreateEntity();
            var token = new PickToken { Target = entity, SubElementId = 7u };
            var worldPos = new Vector3(1.5f, 2.5f, 3.5f);

            repo.Bus.Publish(new GizmoDragUpdateEvent { Token = token, WorldPos = worldPos });
            repo.Bus.SwapBuffers();

            var events = repo.Bus.Read<GizmoDragUpdateEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(entity, events[0].Token.Target);
            Assert.Equal(7u, events[0].Token.SubElementId);
            Assert.Equal(worldPos, events[0].WorldPos);
        }
    }
}
