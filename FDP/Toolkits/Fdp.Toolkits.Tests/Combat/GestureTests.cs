using System.Reflection;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3224</c> (civilians B-6, CV-13; docs/DESIGN_Civilians_And_Crowd_Control.md §7.7) — a person's gesture: the behaviour sets
    /// it, every reader gets None for a unit that never gestured, and it is recorded (it reaches the replay).
    /// </summary>
    public class GestureTests
    {
        [Fact]
        public void CE3224_SetAndOf_NoneCostsNothing_AndTheLastGestureHolds()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<Gesture>();
            var man = world.CreateEntity();

            Assert.Equal(GestureKind.None, Gestures.Of(world, man));
            Assert.True(Gestures.Set(world, man, GestureKind.None));
            Assert.False(world.HasComponent<Gesture>(man));   // None on a unit that never gestured adds nothing

            Assert.True(Gestures.Set(world, man, GestureKind.HandsUp));
            Assert.Equal(GestureKind.HandsUp, Gestures.Of(world, man));
            Assert.True(Gestures.Set(world, man, GestureKind.FistRaised));
            Assert.Equal(GestureKind.FistRaised, Gestures.Of(world, man));
            Assert.True(Gestures.Set(world, man, GestureKind.None));
            Assert.Equal(GestureKind.None, Gestures.Of(world, man));

            using var bare = new EntityRepository();   // the type not registered on this node
            var other = bare.CreateEntity();
            Assert.False(Gestures.Set(bare, other, GestureKind.Waving));
            Assert.Equal(GestureKind.None, Gestures.Of(bare, other));
        }

        [Fact]
        public void CE3224_TheGesture_IsRecorded_NotAScenarioValue()
        {
            var policy = typeof(Gesture).GetCustomAttribute<DataPolicyAttribute>()!.Policy;
            Assert.False(policy.HasFlag(DataPolicy.NoReplay), "a gesture must reach the replay");
            Assert.True(policy.HasFlag(DataPolicy.NoScenario), "a gesture is runtime state, never authored in a scenario");
        }
    }
}
