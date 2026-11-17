using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8013</c> (ST-5 production wiring; SV-6, X-4) — a split's children HELD by a lifecycle participant until the handover (only
    /// them: the requirement is per ENTITY; unbounded; a cancelled child's wait abandoned), and a slot's ammunition crossing both ways.
    /// </summary>
    public sealed class UnitResolutionWiringRails
    {
        private static EntityRepository Repo()
        {
            var r = new EntityRepository();
            r.RegisterComponent<AggregateChild>();
            r.RegisterEvent<ConstructionOrder>(); r.RegisterEvent<ConstructionAck>();
            r.RegisterEvent<DestructionOrder>(); r.RegisterEvent<DestructionAck>();
            return r;
        }

        private static void Tick(EntityRepository repo, IEcsModuleSystem sys)
        {
            sys.Execute(repo, 0f);
            ((EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer()).Playback(repo);
            repo.Bus.SwapBuffers();
        }

        private static bool Acked(EntityRepository repo, Entity e)
        {
            foreach (var a in ((ISimulationView)repo).ReadEvents<ConstructionAck>())
                if (a.Entity == e && a.ModuleId == HeldChildParticipant.LifecycleModuleId && a.Success) return true;
            return false;
        }

        /// <summary>⭐ SV-6 — the held child waits (unbounded) and is acked only at the handover; a plain entity never waits on the participant.
        /// ✅ Red-proof: the participant acking at once (ignoring the tag) ⇒ the held child is acked before the handover ⇒ red.</summary>
        [Fact]
        public void AHeldChild_WaitsForTheHandover_OnlyIt_AndUnbounded()
        {
            using var repo = Repo();
            var elm = new EntityLifecycleModule(null!, Array.Empty<int>(), timeoutFrames: 5);
            var held = new HeldChildParticipant(elm, repo);
            var child = repo.CreateEntity(); repo.AddComponent(child, new AggregateChild { Held = 1 });
            var plain = repo.CreateEntity();
            var cmd = ((ISimulationView)repo).GetCommandBuffer();
            elm.BeginConstruction(child, 0, 1, cmd);
            elm.BeginConstruction(plain, 0, 1, cmd);
            Assert.True(elm.IsWaitingFor(child, HeldChildParticipant.LifecycleModuleId));
            Assert.False(elm.IsWaitingFor(plain, HeldChildParticipant.LifecycleModuleId));   // per entity: nobody else waits
            ((EntityCommandBuffer)cmd).Playback(repo); repo.Bus.SwapBuffers();               // the orders

            for (int i = 0; i < 4; i++) { Tick(repo, held); Assert.False(Acked(repo, child)); }
            Assert.Equal(1, held.PendingCount);
            elm.CheckTimeouts(1000, cmd);                                                   // far past the 5-frame budget
            Assert.True(elm.IsWaitingFor(child, HeldChildParticipant.LifecycleModuleId));    // unbounded: never destroyed by the timeout

            repo.GetComponentRW<AggregateChild>(child).Held = 0;                              // the handover
            Tick(repo, held);
            Assert.True(Acked(repo, child));
            Assert.Equal(0, held.PendingCount);
        }

        /// <summary>⭐ SV-7 — a held child destroyed (a cancelled split) leaves no wait behind. ✅ Red-proof: no abandon ⇒ the ELM still waits ⇒ red.</summary>
        [Fact]
        public void ACancelledChild_AbandonsItsWait()
        {
            using var repo = Repo();
            var elm = new EntityLifecycleModule(null!, Array.Empty<int>());
            var held = new HeldChildParticipant(elm, repo);
            var child = repo.CreateEntity(); repo.AddComponent(child, new AggregateChild { Held = 1 });
            var cmd = ((ISimulationView)repo).GetCommandBuffer();
            elm.BeginConstruction(child, 0, 1, cmd);
            ((EntityCommandBuffer)cmd).Playback(repo); repo.Bus.SwapBuffers();
            Tick(repo, held);
            repo.DestroyEntity(child);
            Tick(repo, held);
            Assert.False(elm.IsWaitingFor(child, HeldChildParticipant.LifecycleModuleId));
            Assert.Equal(0, held.PendingCount);
        }

        /// <summary>⭐ X-4 — a slot at 20 of the soldier's 40 rounds hands over a man at half on EVERY weapon (exactly 20 in all), and a merge
        /// reads his rounds back. ✅ Red-proof: the handover not scaling the mount child ⇒ 30 + 5 ≠ 20 ⇒ red.</summary>
        [Fact]
        public void TheSlotsRounds_CrossBothWays()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<WeaponState>(); repo.RegisterComponent<PartMetadata>();
            var man = repo.CreateEntity();
            repo.AddComponent(man, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            var launcher = repo.CreateEntity();
            repo.AddComponent(launcher, new WeaponState { Ammo = 10, MaxAmmo = 10 });
            repo.AddComponent(launcher, new PartMetadata { ParentEntity = man });
            Assert.Equal(40, UnitAmmunition.ToSlot(repo, man));

            UnitAmmunition.ToSoldier(repo, man, 20);
            Assert.Equal(20, repo.GetComponentRO<WeaponState>(man).Ammo + repo.GetComponentRO<WeaponState>(launcher).Ammo);
            Assert.InRange(repo.GetComponentRO<WeaponState>(launcher).Ammo, 5, 6);            // half of what it carried (the remainder goes last)
            Assert.Equal(20, UnitAmmunition.ToSlot(repo, man));

            UnitAmmunition.ToSoldier(repo, man, 500);                                       // never more than he carries
            Assert.Equal(20, UnitAmmunition.ToSlot(repo, man));
            var unarmed = repo.CreateEntity();
            Assert.Equal(-1, UnitAmmunition.ToSlot(repo, unarmed));                         // no weapon state: the slot keeps its count
        }
    }
}
