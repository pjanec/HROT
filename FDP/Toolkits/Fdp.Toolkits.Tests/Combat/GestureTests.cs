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
            world.RegisterComponent<Fdp.Toolkit.Creatures.Demeanour>();
            var man = world.CreateEntity();

            Assert.Equal(GestureKind.None, Gestures.Of(world, man));
            Assert.True(Gestures.Set(world, man, GestureKind.None));
            Assert.False(world.HasComponent<Fdp.Toolkit.Creatures.Demeanour>(man));   // None on a unit that never gestured adds nothing

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

        /// <summary>
        /// ⭐ <c>CE-4628</c> §10c rail (1) — the gesture half and the mood half of the ONE <c>Demeanour</c> never clobber each other: set the gesture, set the mood, set
        /// the gesture again ⇒ both kept; either writer ADDS the record when missing (final value, the other half zero). 🔴 Red-proof: <c>SetMood</c> writing the whole record (Kind reset) ⇒ red; <c>Gestures.Set</c> writing the whole record ⇒ red.
        /// </summary>
        [Fact]
        public void CE4628_GesturesAndMoods_NeverClobberEachOthersFields()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<Fdp.Toolkit.Creatures.Demeanour>();
            var man = world.CreateEntity();
            Assert.True(Fdp.Toolkit.Creatures.Demeanours.SetMood(world, man, Fdp.Toolkit.Civilians.CivilianMode.Riot, 0.5f, 1f));   // mood first: adds the record, Kind None
            Assert.Equal(GestureKind.None, Gestures.Of(world, man));
            Assert.True(Gestures.Set(world, man, GestureKind.FistRaised));
            Assert.True(Fdp.Toolkit.Creatures.Demeanours.SetMood(world, man, Fdp.Toolkit.Civilians.CivilianMode.Protest, 0.1f, 0.5f));
            Assert.Equal(GestureKind.FistRaised, Gestures.Of(world, man));                                                       // the mood write kept the gesture
            Assert.True(Gestures.Set(world, man, GestureKind.Waving));
            Assert.True(Fdp.Toolkit.Creatures.Demeanours.Of(world, man, out var d));
            Assert.Equal((GestureKind.Waving, Fdp.Toolkit.Civilians.CivilianMode.Protest, (byte)0, (byte)2), (d.Kind, d.Mode, d.Fear, d.Anger));   // the gesture write kept the mood

            var other = world.CreateEntity();
            Assert.True(Gestures.Set(world, other, GestureKind.HandsUp));                                                          // gesture first: adds the record with a zero mood
            Assert.True(Fdp.Toolkit.Creatures.Demeanours.Of(world, other, out var g));
            Assert.Equal((GestureKind.HandsUp, Fdp.Toolkit.Civilians.CivilianMode.None, (byte)0, (byte)0), (g.Kind, g.Mode, g.Fear, g.Anger));
        }

        [Fact]
        public void CE3224_TheGesture_IsRecorded_NotAScenarioValue()
        {
            var policy = typeof(Fdp.Toolkit.Creatures.Demeanour).GetCustomAttribute<DataPolicyAttribute>()!.Policy;
            Assert.False(policy.HasFlag(DataPolicy.NoReplay), "a gesture must reach the replay");
            Assert.True(policy.HasFlag(DataPolicy.NoScenario), "a gesture is runtime state, never authored in a scenario");
        }
    }
}
