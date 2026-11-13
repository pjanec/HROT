using System;
using System.Reflection;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Providers;
using Xunit;

namespace Fdp.ModuleHost.Tests
{
    /// <summary>
    /// ⭐ M-4 (CE-4483) — the module snapshots are RECYCLED, not cleared (<see cref="EntityRepository.RecycleForSync"/>): the memory is kept and
    /// the next sync copies every chunk. 📐 Measured (Release, 1 000 men, 4 threads): the clear on release and the recommit on acquire cost
    /// ≈ 18 ms a tick on the sim thread. These rails hold the world a recycled snapshot shows to the world a cleared one showed.
    /// </summary>
    public class SnapshotRecycleTests
    {
        [ComponentId(487)] struct RPos { public int X; }
        [ComponentId(488)] struct RVel { public int X; }

        private static (EntityRepository live, Entity e, BitMask512 mask) World()
        {
            var live = new EntityRepository();
            live.RegisterComponent<RPos>();
            live.RegisterComponent<RVel>();
            var e = live.CreateEntity();
            live.AddComponent(e, new RPos { X = 1 });
            live.AddComponent(e, new RVel { X = 2 });
            var mask = new BitMask512();
            mask.SetBit(ComponentType<RPos>.ID);
            return (live, e, mask);
        }

        /// <summary>
        /// A background module that wrote into its view: the write carries the SAME version as the live chunk (the snapshot takes the live
        /// version at sync), so a version-gated copy would skip that chunk and hand the module's write to the next run. The recycle forces it.
        /// </summary>
        [Fact]
        public void M4_AModulesWriteIntoItsViewNeverSurvivesTheRecycle()
        {
            var (live, e, mask) = World();
            using var _ = live;
            using var provider = new OnDemandProvider(live, new EventAccumulator(), mask, null, 1);

            var view = (EntityRepository)provider.AcquireView();
            Assert.Equal(1, view.GetComponentRO<RPos>(e).X);
            view.GetComponentRW<RPos>(e).X = 99;   // the module writes its view
            provider.ReleaseView(view);

            var again = (EntityRepository)provider.AcquireView();
            Assert.Same(view, again);              // the same pooled world …
            Assert.Equal(1, again.GetComponentRO<RPos>(e).X);   // … showing the live row, not the module's write
            provider.ReleaseView(again);
        }

        /// <summary>The recycle keeps the snapshot's committed memory (the point of it), and a live change made meanwhile still arrives.</summary>
        [Fact]
        public void M4_TheRecycledSnapshotKeepsItsMemory_AndShowsTheLiveChange()
        {
            var (live, e, mask) = World();
            using var _ = live;
            using var provider = new OnDemandProvider(live, new EventAccumulator(), mask, null, 1);

            var view = (EntityRepository)provider.AcquireView();
            long before = view.MemoryFootprint().Native;
            provider.ReleaseView(view);
            Assert.Equal(before, view.MemoryFootprint().Native);   // nothing decommitted

            live.Tick();
            live.GetComponentRW<RPos>(e).X = 5;
            var again = (EntityRepository)provider.AcquireView();
            Assert.Equal(5, again.GetComponentRO<RPos>(e).X);
            provider.ReleaseView(again);
        }

        /// <summary>A table the module filled but the mask does not copy is EMPTIED on the next sync (as the clear did), and an entity the
        /// live world destroyed is gone.</summary>
        [Fact]
        public void M4_ATableOutsideTheMaskIsEmptied_AndADestroyedEntityIsGone()
        {
            var (live, e, mask) = World();
            using var _ = live;
            using var provider = new OnDemandProvider(live, new EventAccumulator(), mask, null, 1);

            var view = (EntityRepository)provider.AcquireView();
            view.RegisterComponent<RVel>();
            view.AddComponent(e, new RVel { X = 7 });   // the module adds a row of a type outside its mask
            Assert.True(view.MemoryFootprint().Native > 0);
            provider.ReleaseView(view);

            live.DestroyEntity(e);
            var again = (EntityRepository)provider.AcquireView();
            Assert.False(again.IsAlive(e));
            Assert.Equal(0, again.EntityCount);
            Assert.False(again.HasComponent<RVel>(e));
            provider.ReleaseView(again);
        }

        /// <summary>
        /// The source's version numbers REPEAT with other content — its clock set back (a playback frame, a rewind) and the same chunk rewritten
        /// at a version the snapshot already holds for it. A version-gated sync would skip it; the source's <see cref="EntityRepository.ContentEpoch"/>
        /// moved, so the recycled snapshot copies whole. ✅ Red-proof: the epoch check dropped ⇒ the snapshot keeps the old 1.
        /// </summary>
        [Fact]
        public void M4_ASourceWhoseVersionsRepeat_IsCopiedWhole()
        {
            var (live, e, mask) = World();
            using var _ = live;
            uint v1 = live.GlobalVersion;                         // RPos written at v1
            live.Tick();
            var other = live.CreateEntity();
            live.AddComponent(other, new RVel { X = 3 });        // something else moves the clock on
            using var provider = new OnDemandProvider(live, new EventAccumulator(), mask, null, 1);

            var view = (EntityRepository)provider.AcquireView();
            Assert.Equal(1, view.GetComponentRO<RPos>(e).X);
            provider.ReleaseView(view);

            live.ResetGlobalVersion(v1);                          // the clock set back …
            live.GetComponentRW<RPos>(e).X = 42;                  // … and the chunk rewritten at the SAME version the snapshot holds
            live.ResetGlobalVersion(v1 + 5);
            var again = (EntityRepository)provider.AcquireView();
            Assert.Equal(42, again.GetComponentRO<RPos>(e).X);
            provider.ReleaseView(again);
        }

        /// <summary>The point of the recycle: a source that only ADVANCED is synced incrementally — a chunk nobody changed is not copied again
        /// (read here through a byte the snapshot holds and the source does not: proof the chunk was skipped, not re-copied).</summary>
        [Fact]
        public void M4_AnAdvancedSource_IsSyncedIncrementally()
        {
            var (live, e, mask) = World();
            using var _ = live;
            live.Tick();                                          // RPos was written BEFORE the version the snapshot syncs at
            using var provider = new OnDemandProvider(live, new EventAccumulator(), mask, null, 1);
            var view = (EntityRepository)provider.AcquireView();
            provider.ReleaseView(view);
            // behind the version stamp's back (a test seam: no writer does this) — a skipped chunk keeps it, a copied one loses it
            unsafe { ((RPos*)view.GetTable<RPos>(false).GetRawPointer(e.Index))->X = 77; }
            live.Tick();
            var again = (EntityRepository)provider.AcquireView();
            Assert.Equal(77, again.GetComponentRO<RPos>(e).X);
            provider.ReleaseView(again);
        }

        /// <summary>
        /// ⭐ M-4 — <see cref="ComponentTypeRegistry.NeedsClone"/> is read WITHOUT the registry's lock: every managed table's sync asks it, from the
        /// sim thread, the staging publish and the UI's pull at once. 📐 Measured: 3.1 s in 20 s spent in it under contention.
        /// </summary>
        [Fact]
        public void M4_NeedsCloneAnswersWhileTheRegistryIsLocked()
        {
            int id = ComponentType<RPos>.ID;
            var lockObj = typeof(ComponentTypeRegistry).GetField("_lock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            using var held = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            var holder = new Thread(() => { lock (lockObj) { held.Set(); release.Wait(5000); } }) { IsBackground = true };
            holder.Start();
            try
            {
                Assert.True(held.Wait(5000));
                bool answered = false;
                var asker = new Thread(() => { ComponentTypeRegistry.NeedsClone(id); answered = true; }) { IsBackground = true };
                asker.Start();
                Assert.True(asker.Join(2000), "NeedsClone waited for the registry's lock");
                Assert.True(answered);
            }
            finally { release.Set(); holder.Join(5000); }
        }
    }
}
