using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Tests.World
{
    /// <summary>
    /// ⭐ CE-1480 G1 — the ONE <see cref="ChangeGate"/>: every branch (Unchanged · Delta · Full and each Full reason), a structural change,
    /// a seek / restore (clock backwards), the switch, and 0 B in steady state. 📄 docs/DESIGN_Change_Gated_Systems.md §2–§3.
    /// </summary>
    public sealed class ChangeGateTests
    {

        private static EntityRepository NewWorld()
        {
            var w = new EntityRepository();
            w.RegisterComponent<Health>();
            w.RegisterComponent<MobilityKill>();
            w.RegisterManagedComponent<AirPath>();
            return w;
        }

        private static ChangeGate NewGate() => new ChangeGate().Watch(typeof(Health), typeof(AirPath));

        /// <summary>One frame: the clock ticks (as the kernel does at the start of a frame), then the system asks.</summary>
        private static ChangeResult Frame(EntityRepository w, ChangeGate g) { w.Tick(); return g.Next(w); }

        /// <summary>Runs frames until the gate settles to Unchanged (a few frames: the look-back re-sees the last write once).</summary>
        private static void Settle(EntityRepository w, ChangeGate g)
        {
            for (int i = 0; i < 6; i++) if (Frame(w, g).Kind == ChangeKind.Unchanged) return;
            throw new Exception("the gate never settled to Unchanged");
        }

        private static Entity Spawn(EntityRepository w)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new Health { Current = 1, Max = 2 });
            w.AddComponent(e, new MobilityKill { BelowFraction = 1 });
            return e;
        }

        [Fact]
        public void Undeclared_IsFullEveryTime_LikeNoGate()
        {
            var w = NewWorld(); Spawn(w);
            var g = new ChangeGate();
            for (int i = 0; i < 3; i++) Assert.Equal(ChangeKind.Full, Frame(w, g).Kind);
            Assert.Equal(FullReason.Undeclared, g.LastFullReason);
        }

        [Fact]
        public void FirstRun_IsFull_ThenSettlesToUnchanged()
        {
            var w = NewWorld(); Spawn(w);
            var g = NewGate();
            Assert.Equal(ChangeKind.Full, Frame(w, g).Kind);
            Assert.Equal(FullReason.FirstRun, g.LastFullReason);
            Settle(w, g);
            for (int i = 0; i < 5; i++) Assert.Equal(ChangeKind.Unchanged, Frame(w, g).Kind);
        }

        [Fact]
        public void AWatchedValueWrite_IsADelta_ThatNamesTheEntity_ThenSettles()
        {
            var w = NewWorld(); var e = Spawn(w); Spawn(w);
            var g = NewGate(); Settle(w, g);
            var q = w.Query().With<Health>().Build();

            w.Tick(); w.SetComponent(e, new Health { Current = 9, Max = 10 });   // an earlier system wrote this frame
            var r = g.Next(w);
            Assert.Equal(ChangeKind.Delta, r.Kind);
            var seen = new List<Entity>();
            foreach (var x in r.Entities(w, q)) seen.Add(x);
            Assert.Contains(e, seen);
            Settle(w, g);
        }

        [Fact]
        public void AWriteAfterTheRun_InTheSameFrame_IsSeenNextFrame()
        {
            var w = NewWorld(); var e = Spawn(w);
            var g = NewGate(); Settle(w, g);

            w.Tick(); Assert.Equal(ChangeKind.Unchanged, g.Next(w).Kind);
            w.SetComponent(e, new Health { Current = 5, Max = 10 });             // a LATER system, same version
            Assert.Equal(ChangeKind.Delta, Frame(w, g).Kind);
        }

        [Fact]
        public void AWriteToAnUnwatchedComponent_StaysUnchanged()
        {
            var w = NewWorld(); var e = Spawn(w);
            var g = NewGate(); Settle(w, g);
            w.Tick(); w.SetComponent(e, new MobilityKill { BelowFraction = 0.5f });
            Assert.Equal(ChangeKind.Unchanged, g.Next(w).Kind);
        }

        [Fact]
        public void AManagedComponentSetWhole_IsADelta()
        {
            var w = NewWorld(); var e = Spawn(w);
            w.AddComponent(e, new AirPath { RouteVersion = 1 });
            var g = NewGate(); Settle(w, g);
            w.Tick(); w.SetManagedComponent(e, new AirPath { RouteVersion = 2 });
            Assert.Equal(ChangeKind.Delta, g.Next(w).Kind);
        }

        [Fact]
        public void AStructuralChange_LandsInFull()
        {
            var w = NewWorld(); var e = Spawn(w);
            var g = NewGate(); Settle(w, g);

            w.Tick(); w.RemoveComponent<MobilityKill>(e);            // a REMOVAL moves no chunk version — only the structural clock
            var r = g.Next(w);
            Assert.Equal(ChangeKind.Full, r.Kind); Assert.Equal(FullReason.Structural, g.LastFullReason);
            Settle(w, g);

            w.Tick(); w.DestroyEntity(e);
            Assert.Equal(ChangeKind.Full, g.Next(w).Kind); Assert.Equal(FullReason.Structural, g.LastFullReason);
            Settle(w, g);

            w.Tick(); Spawn(w);
            Assert.Equal(ChangeKind.Full, g.Next(w).Kind);
        }

        [Fact]
        public void AnExternalVersion_ThatMoves_IsFull_AndThatStaysIsNot()
        {
            var w = NewWorld(); Spawn(w);
            long ext = 1;
            var g = NewGate().WatchExternal(() => ext); Settle(w, g);
            Assert.Equal(ChangeKind.Unchanged, Frame(w, g).Kind);
            ext = 2;
            Assert.Equal(ChangeKind.Full, Frame(w, g).Kind); Assert.Equal(FullReason.External, g.LastFullReason);
            Settle(w, g);
        }

        [Fact]
        public void AnotherWorld_IsFull()
        {
            var a = NewWorld(); Spawn(a);
            var g = NewGate(); Settle(a, g);
            var b = NewWorld(); Spawn(b);
            Assert.Equal(ChangeKind.Full, Frame(b, g).Kind); Assert.Equal(FullReason.WorldReplaced, g.LastFullReason);
        }

        [Fact]
        public void AWorldReplacedInPlace_ClearGeneration_IsFull()
        {
            var w = NewWorld(); Spawn(w);
            var g = NewGate(); Settle(w, g);
            w.SoftClear();                                      // a keyframe apply / checkpoint restore goes through Clear()
            Assert.Equal(ChangeKind.Full, Frame(w, g).Kind); Assert.Equal(FullReason.Cleared, g.LastFullReason);
        }

        [Fact]
        public void TheClockGoingBackwards_ASeek_IsFull()
        {
            var w = NewWorld(); Spawn(w);
            for (int i = 0; i < 10; i++) w.Tick();
            var g = NewGate(); Settle(w, g);
            w.ResetGlobalVersion(2);                            // PlaybackSystem.SetGlobalVersion(an earlier tick)
            Assert.Equal(ChangeKind.Full, g.Next(w).Kind); Assert.Equal(FullReason.ClockBackwards, g.LastFullReason);
        }

        [Fact]
        public void TheSwitch_ForcesFullEverywhere()
        {
            var w = NewWorld(); Spawn(w);
            var g = NewGate(); Settle(w, g);
            ChangeGate.Enabled = false;
            try
            {
                Assert.Equal(ChangeKind.Full, Frame(w, g).Kind); Assert.Equal(FullReason.Disabled, g.LastFullReason);
            }
            finally { ChangeGate.Enabled = true; }
            Settle(w, g);
        }

        [Fact]
        public void AWorldWhoseVersionDoesNotAdvance_IsNeverSkipped_SoAWriteAfterTheRunIsNeverMissed()
        {
            var w = NewWorld(); var e = Spawn(w);
            var g = NewGate();
            Assert.Equal(ChangeKind.Full, g.Next(w).Kind);
            for (int i = 0; i < 3; i++)
            {
                w.SetComponent(e, new Health { Current = i, Max = 10 });   // same version as the last run: no chunk version can tell
                var r = g.Next(w);
                Assert.Equal(ChangeKind.Full, r.Kind); Assert.Equal(FullReason.StaticClock, g.LastFullReason);
            }
        }

        [Fact]
        public void SteadyState_AllocatesNothing()
        {
            var w = NewWorld(); for (int i = 0; i < 50; i++) Spawn(w);
            var g = NewGate().WatchExternal(() => 3); Settle(w, g);
            for (int i = 0; i < 10; i++) Frame(w, g);           // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { w.Tick(); g.Next(w); }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(0, after - before);
        }
    }
}
