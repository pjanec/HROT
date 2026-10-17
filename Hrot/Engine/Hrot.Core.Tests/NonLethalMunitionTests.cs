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
        }
    }
}
