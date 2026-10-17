using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3223</c> (civilians B-5, CV-9, R-3013; docs/DESIGN_Civilians_And_Crowd_Control.md §7.5) — the non-lethal rounds of the
    /// built-in catalogue, through the real resolver and the real fire path: each produces its <see cref="HitEffect"/> (CE-3219), and
    /// the security forces load them.
    /// </summary>
    public class NonLethalMunitionTests
    {
        private static TkbDatabase BuiltIn()
        {
            var db = new TkbDatabase();
            HrotEnvironment.RegisterBuiltInTkb(db);
            return db;
        }

        /// <summary>⭐ <c>CE-3230</c> (B-8, H-1 / H-6 / H-7; DESIGN_Civilians_And_Crowd_Control §7.9) — on the BUILT-IN catalogue every person type
        /// ends with exactly one HANDS mount, at an index the replicated per-mount ammo carries (≤ 3), and no other type has one; the stone
        /// and the stun grenade are thrown on the ARC. ✅ Red-proof: the <c>EnsureOnPersons</c> call dropped from <c>RegisterBuiltInTkb</c> ⇒ red.</summary>
        [Fact]
        public void CE3230_EveryBuiltInPerson_EndsWithItsHands_AndStonesAndStunGrenadesArc()
        {
            var db = BuiltIn();
            int persons = 0;
            foreach (var t in db.GetAll())
            {
                var mounts = t.GetDescriptor<WeaponSuiteDto>()?.Mounts;
                int hands = 0;
                if (mounts != null) foreach (var m in mounts) if (m.Hands) hands++;
                bool person = t.GetDescriptor<VehicleParametersDto>()?.VehicleClass == global::CarKinem.Core.VehicleClass.Pedestrian;
                if (!person) { Assert.True(hands == 0, $"{t.Name}: not a person, yet it has hands"); continue; }
                persons++;
                Assert.True(hands == 1 && mounts![^1].Hands, $"{t.Name}: a person ends with exactly one hands mount");
                Assert.True(mounts!.Count - 1 < Fdp.Toolkit.Combat.Components.WeaponAmmo.MaxMounts,
                    $"{t.Name}: the hands at index {mounts.Count - 1} — dtWeaponAmmo carries mounts 0…{Fdp.Toolkit.Combat.Components.WeaponAmmo.MaxMounts - 1} only");
            }
            Assert.True(persons > 10, $"only {persons} person types found");
            Assert.True(ParameterResolver.Warhead(db, MunitionTkbCatalog.TkbStone).Warhead!.Indirect);
            Assert.True(ParameterResolver.Warhead(db, MunitionTkbCatalog.TkbStunGrenade).Warhead!.Indirect);
        }

        [Fact]
        public void CE3223_EachNonLethalRound_ResolvesToItsEffect_AndNoneCarriesFragments()
        {
            var db = BuiltIn();
            foreach (var (ammo, kind) in new[]
            {
                ((long)MunitionTkbCatalog.TkbBatonRound, WarheadKind.Blunt),
                ((long)MunitionTkbCatalog.TkbStunGrenade, WarheadKind.Stun),
                ((long)MunitionTkbCatalog.TkbStone, WarheadKind.Blunt),
            })
            {
                var w = ParameterResolver.Warhead(db, ammo).Warhead;
                Assert.NotNull(w);
                Assert.Equal(kind, w!.Kind);
                Assert.True(w.EffectSeconds > 0f, $"{ammo}: a non-lethal round needs a duration");
                Assert.Equal(0f, w.FragmentRadiusM);
            }
        }

        [Fact]
        public void CE3223_ABatonRoundHit_KnocksDown_AStunGrenade_Stuns()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            world.RegisterEvent<DetonationNotification>();
            world.RegisterEvent<DamageAssessedEvent>();
            world.RegisterEvent<Fdp.Toolkit.Terrain.DoorCommandEvent>();
            world.SetSingletonManaged<ITkbDatabase>(BuiltIn());

            var man = world.CreateEntity();
            world.AddComponent(man, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            world.AddComponent(man, new SimTransform { Position = new System.Numerics.Vector3(2, 0, 0), Rotation = System.Numerics.Quaternion.Identity });

            world.Bus.Publish(new DetonationNotification { Target = man, Damage = 5f, Ammo = MunitionTkbCatalog.TkbBatonRound });
            world.Bus.SwapBuffers();
            new DamageCalculationSystem().Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Contains(world.Bus.Read<DamageAssessedEvent>().ToArray(), d => d.HitEntity == man && d.Effect == HitEffect.Blunt);

            world.Bus.Publish(new DetonationNotification { HitX = 0, HitY = 0, HitZ = 0.1f, Ammo = MunitionTkbCatalog.TkbStunGrenade });
            world.Bus.SwapBuffers();
            new AreaEffectSystem().Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            Assert.Contains(world.Bus.Read<DamageAssessedEvent>().ToArray(), d => d.HitEntity == man && d.Effect == HitEffect.Stun && d.TotalDamage == 0f);
        }

        [Fact]
        public void CE3223_TheSecurityForces_LoadTheNonLethalRounds()
        {
            var db = BuiltIn();
            ulong Ammo(int type, int mount) => db.GetByType(type)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[mount].AmmoGuid;
            Assert.Equal((ulong)MunitionTkbCatalog.TkbStunGrenade, Ammo(CivilianTkbCatalog.TkbSecurityShield, 0));
            Assert.Equal((ulong)MunitionTkbCatalog.TkbStunGrenade, Ammo(CivilianTkbCatalog.TkbSecurityBaton, 0));
            Assert.Equal((ulong)MunitionTkbCatalog.TkbBatonRound, Ammo(CivilianTkbCatalog.TkbSecurityLauncher, 0));
            Assert.Equal((ulong)MunitionTkbCatalog.TkbStunGrenade, Ammo(CivilianTkbCatalog.TkbSecurityLauncher, 1));
            Assert.Equal((ulong)MunitionTkbCatalog.TkbTearGasGrenade, Ammo(CivilianTkbCatalog.TkbSecurityLauncher, 2));   // ⭐ CE-3219 part 3
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3219</c> part 3 (B-5's gas grenade, docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — each cloud round, bursting through
        /// the real <see cref="AreaEffectSystem"/> over the built-in TKB, asks for a cloud of its kind and hurts nobody on the spot.
        /// </summary>
        [Fact]
        public void CE3219_EachCloudRound_AsksForItsCloud_AndHurtsNobodyOnTheSpot()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            world.RegisterComponent<SimTransform>();
            world.RegisterEvent<DetonationNotification>();
            world.RegisterEvent<DamageAssessedEvent>();
            world.SetSingletonManaged<ITkbDatabase>(BuiltIn());
            var man = world.CreateEntity();
            world.AddComponent(man, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            world.AddComponent(man, new SimTransform { Position = new System.Numerics.Vector3(1, 0, 0), Rotation = System.Numerics.Quaternion.Identity });

            foreach (var (ammo, kind) in new[]
            {
                ((long)MunitionTkbCatalog.TkbTearGasGrenade, Fdp.Toolkit.Combat.CloudKind.TearGas),
                ((long)MunitionTkbCatalog.TkbTearGasCanister, Fdp.Toolkit.Combat.CloudKind.TearGas),
                ((long)MunitionTkbCatalog.TkbSmokeGrenade, Fdp.Toolkit.Combat.CloudKind.Smoke),
            })
            {
                world.Bus.Publish(new DetonationNotification { HitX = 0, HitY = 0, HitZ = 0.1f, Ammo = ammo });
                world.Bus.SwapBuffers();
                new AreaEffectSystem().Execute(world, 0.016f);
                world.Bus.SwapBuffers();
                var request = Assert.Single(world.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>().ToArray());
                Assert.Equal(Hrot.Core.Tkb.AreaEffectCloudTkb.Type, request.TkbType);   // the built-in TKB knows the type it asks for
                var cloud = Assert.Single(request.InitialComponents!.OfType<Fdp.Toolkit.Combat.AreaEffectCloud>());
                Assert.Equal(kind, cloud.Kind);
                Assert.True(cloud.Radius > 0f && cloud.Seconds > 0f, $"{ammo}: a cloud needs a size and a life");
                Assert.Empty(world.Bus.Read<DamageAssessedEvent>().ToArray());
            }
        }
    }
}
