using System;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.Internal;
using Xunit;

namespace Fdp.Core.Tests
{
    /// <summary>
    /// POC 2 / P-1 — a read is a read. The pure read path (<see cref="ThreadedReads.PureReads"/>, the DEFAULT since CE-4445; <c>HROT_MANAGED_READ_STAMP=1</c> restores the stamping read) stamps no chunk version
    /// and allocates nothing; the legacy path stamps. The rails call the pure methods DIRECTLY (a <c>static readonly</c> switch cannot flip inside
    /// one test process); the counter-proofs show the legacy path really does stamp, so "unchanged" means something.
    /// 📄 docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Threaded_POC_2.md
    /// </summary>
    public class PureReadsTests
    {
        [ComponentId(481)]
        public sealed class PureManaged { public int Value; }

        [ComponentId(482)]
        public struct PureUnmanaged { public int Value; }

        [ComponentId(483)]
        public struct PureSingleton { public int Value; }

        /// <summary>A world with one managed + one unmanaged component, advanced two ticks so a stamp with the current version is distinguishable.</summary>
        private static (EntityRepository Repo, Entity E, uint Baseline) Make()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<PureManaged>();
            repo.RegisterComponent<PureUnmanaged>();
            var e = repo.CreateEntity();
            repo.SetManagedComponent(e, new PureManaged { Value = 7 });
            repo.AddComponent(e, new PureUnmanaged { Value = 9 });
            repo.SetSingletonUnmanaged(new PureSingleton { Value = 5 });
            repo.Tick();
            repo.Tick();
            return (repo, e, repo.GlobalVersion - 1);   // a stamp with the CURRENT GlobalVersion is > Baseline; nothing earlier is
        }

        private static uint SingletonVersion(EntityRepository repo)
        {
            var arr = (Array)typeof(EntityRepository).GetField("_singletons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(repo)!;
            var table = (ComponentTable<PureSingleton>)arr.GetValue(ComponentType<PureSingleton>.ID)!;
            return table.GetVersionForEntity(0);
        }

        [Fact]
        public void PureManagedRead_StampsNoChunkVersion()
        {
            var (repo, e, baseline) = Make();
            using (repo)
            {
                for (int i = 0; i < 1000; i++)
                {
                    Assert.Equal(7, UnsafeShim.GetManagedROPure<PureManaged>(repo, e).Value);
                }
                Assert.False(repo.HasComponentChanged(typeof(PureManaged), baseline));
            }
        }

        [Fact]
        public void LegacyManagedRead_DoesStamp_CounterProof()
        {
            var (repo, e, baseline) = Make();
            using (repo)
            {
                Assert.False(repo.HasComponentChanged(typeof(PureManaged), baseline));
                _ = repo.GetComponentRW<PureManaged>(e).Value;   // the path GetComponent takes when the switch is OFF
                Assert.True(repo.HasComponentChanged(typeof(PureManaged), baseline));
            }
        }

        [Fact]
        public void LegacyGetComponent_WhenSwitchOff_Stamps()
        {
            if (ThreadedReads.PureReads) return;   // switch ON: GetComponent IS the pure path (the legacy rails run under the diagnostic HROT_MANAGED_READ_STAMP=1)
            var (repo, e, baseline) = Make();
            using (repo)
            {
                _ = repo.GetComponent<PureManaged>(e);
                Assert.True(repo.HasComponentChanged(typeof(PureManaged), baseline));
            }
        }

        [Fact]
        public void GetComponent_WhenSwitchOn_StampsNothing()
        {
            if (!ThreadedReads.PureReads) return;
            var (repo, e, baseline) = Make();
            using (repo)
            {
                _ = repo.GetComponent<PureManaged>(e);
                _ = repo.GetComponentRO<PureManaged>(e);
                repo.TryGetComponent<PureManaged>(e, out _);
                repo.TryGetComponent<PureUnmanaged>(e, out _);
                Assert.False(repo.HasComponentChanged(typeof(PureManaged), baseline));
                Assert.False(repo.HasComponentChanged(typeof(PureUnmanaged), baseline));
            }
        }

        [Fact]
        public void PureTryGetComponent_ManagedAndUnmanaged_StampNothing_AndReturnTheValue()
        {
            var (repo, e, baseline) = Make();
            using (repo)
            {
                for (int i = 0; i < 1000; i++)
                {
                    Assert.True(repo.TryGetComponentPure<PureManaged>(e, out var m));
                    Assert.Equal(7, m.Value);
                    Assert.True(repo.TryGetComponentPure<PureUnmanaged>(e, out var u));
                    Assert.Equal(9, u.Value);
                }
                Assert.False(repo.HasComponentChanged(typeof(PureManaged), baseline));
                Assert.False(repo.HasComponentChanged(typeof(PureUnmanaged), baseline));
                Assert.False(repo.TryGetComponentPure<PureManaged>(repo.CreateEntity(), out _));
            }
        }

        [Fact]
        public void LegacyTryGetComponent_DoesStamp_CounterProof()
        {
            if (ThreadedReads.PureReads) return;
            var (repo, e, baseline) = Make();
            using (repo)
            {
                repo.TryGetComponent<PureUnmanaged>(e, out _);
                Assert.True(repo.HasComponentChanged(typeof(PureUnmanaged), baseline));
            }
        }

        [Fact]
        public void SingletonRO_StampsNothing_AndReadsTheValue()
        {
            var (repo, _, _) = Make();
            using (repo)
            {
                uint before = SingletonVersion(repo);
                for (int i = 0; i < 1000; i++)
                    Assert.Equal(5, repo.GetSingletonUnmanagedRO<PureSingleton>().Value);
                Assert.Equal(before, SingletonVersion(repo));
            }
        }

        [Fact]
        public void PureManagedRead_OfAnUnallocatedChunk_AllocatesNothing_AndYieldsNull()
        {
            using var repo = new EntityRepository();
            repo.RegisterManagedComponent<PureManaged>();
            var e = repo.CreateEntity();   // no managed component ever set: the chunk is not allocated
            Assert.Null(UnsafeShim.GetManagedROPure<PureManaged>(repo, e));
        }

        [Fact]
        public void PureReads_AllocateNothing_InASteadyLoop()
        {
            var (repo, e, _) = Make();
            using (repo)
            {
                long sink = 0;
                void Loop()
                {
                    for (int i = 0; i < 10_000; i++)
                    {
                        sink += UnsafeShim.GetManagedROPure<PureManaged>(repo, e).Value;
                        repo.TryGetComponentPure<PureUnmanaged>(e, out var u); sink += u.Value;
                        sink += repo.GetSingletonUnmanagedRO<PureSingleton>().Value;
                    }
                }
                Loop();   // warm: JIT, delegate binding
                long a = GC.GetAllocatedBytesForCurrentThread();
                Loop();
                long b = GC.GetAllocatedBytesForCurrentThread();
                Assert.Equal(0, b - a);
                Assert.True(sink > 0);
            }
        }
    }
}
