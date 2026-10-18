using System.Numerics;
using Xunit;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Perception.Look;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Look;
using Hrot.MuscleCharacter.Animation.Stance;
using Hrot.MuscleCharacter.Animation.Systems;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3252</c> (LD-1, LD-F, LD-G, LD-E) — the brain's <see cref="LookRequest"/> reaches the muscle's <see cref="LookState"/>
    /// through the EXISTING look-at channel and dispatcher, in one world (the editor's shape; the wire is its own rail). Before this,
    /// the look-at set was registered on no host, a person had no <c>CanAim</c>, and the executors produced nothing a body could use.
    /// 📄 docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.2a, §3.2b.
    /// </summary>
    public sealed class LookPipelineTests
    {
        private static (EntityRepository World, Entity Man, LookAtDispatcherSystem Dispatcher) World()
        {
            var w = new EntityRepository();
            StanceComponentRegistry.RegisterAll(w);   // ⭐ G1 — the look-at set comes with the stance set
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<ActorCapabilityState>();
            w.RegisterEvent<DestructionOrder>();
            var man = w.CreateEntity();
            w.AddComponent(man, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(man, new ActorCapabilityState { Capabilities = ActorCapabilities.CanAim });
            w.AddComponent(man, new LookAtChannel());
            w.AddComponent(man, new LookAtChannelStatus());
            w.AddComponent(man, new LookAtExecutorState());
            return (w, man, new LookAtDispatcherSystem(new FakeAnimationBackend()));
        }

        /// <summary>✅ Red-proof: drop <c>LookOf.Want</c> from <c>LookAtPointExecutor.OnEnter</c> ⇒ no wanted look.</summary>
        [Fact]
        public void CE3252_ALookRequest_ReachesTheMusclesLookState_AndReleaseClearsIt()
        {
            var (w, man, dispatcher) = World();
            Assert.True(StanceComponentRegistryHasLook(w));

            Assert.True(LookRequest.AtPoint(w, man, new Vector3(5f, 5f, 0f)));
            dispatcher.Execute(w, 0.1f);
            var look = w.GetComponentRO<LookState>(man);
            Assert.Equal((byte)1, look.HasTarget);
            Assert.Equal(new Vector3(5f, 5f, 0f), look.Target);

            // the same point again changes nothing (a behaviour may call it every tick)
            uint instance = w.GetComponentRO<LookAtChannel>(man).ActionInstanceId;
            LookRequest.AtPoint(w, man, new Vector3(5.01f, 5f, 0f));
            Assert.Equal(instance, w.GetComponentRO<LookAtChannel>(man).ActionInstanceId);

            Assert.True(LookRequest.Release(w, man));
            dispatcher.Execute(w, 0.1f);
            Assert.Equal((byte)0, w.GetComponentRO<LookState>(man).HasTarget);
            w.Dispose();
        }

        /// <summary>An entity look names the entity; the muscle follows it.</summary>
        [Fact]
        public void CE3252_ALookAtAnEntity_NamesIt()
        {
            var (w, man, dispatcher) = World();
            var other = w.CreateEntity();
            w.AddComponent(other, new SimTransform { Position = new Vector3(0f, 9f, 0f), Rotation = Quaternion.Identity });
            Assert.True(LookRequest.AtEntity(w, man, other));
            dispatcher.Execute(w, 0.1f);
            var look = w.GetComponentRO<LookState>(man);
            Assert.Equal(other, look.TargetEntity);
            Assert.Equal(new Vector3(0f, 9f, 0f), look.Target);
            w.Dispose();
        }

        /// <summary>⛔ Without <c>CanAim</c> the dispatcher still refuses — so the TKB grant (G3) is what makes it work.</summary>
        [Fact]
        public void CE3252_WithoutCanAim_TheLookIsRefused()
        {
            var (w, man, dispatcher) = World();
            w.GetComponentRW<ActorCapabilityState>(man).Capabilities = ActorCapabilities.None;
            LookRequest.AtPoint(w, man, new Vector3(5f, 0f, 0f));
            dispatcher.Execute(w, 0.1f);
            Assert.False(w.HasComponent<LookState>(man) && w.GetComponentRO<LookState>(man).HasTarget != 0);
            w.Dispose();
        }

        private static bool StanceComponentRegistryHasLook(EntityRepository w)
            => w.IsComponentTypeRegistered<LookAtChannel>() && w.IsComponentTypeRegistered<LookAtChannelStatus>()
               && w.IsComponentTypeRegistered<LookAtExecutorState>() && w.IsComponentTypeRegistered<LookState>();
    }
}
