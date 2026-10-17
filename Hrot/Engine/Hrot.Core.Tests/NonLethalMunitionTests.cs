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

        /// <summary>
        /// ⭐ <c>CE-3235</c> (B-13, §7.10 PU-2/PU-3) — on the BUILT-IN catalogue the hand items are the thrown rounds and the canister; each
        /// has a LOOK (so a lying one replicates to the brain's node); only the stone lies a minute. ✅ Red-proof: the HandItems attach dropped
        /// from <c>Register</c> ⇒ red.
        /// </summary>
        [Fact]
        public void CE3235_TheHandItems_AreSeen_AndAStoneLiesAMinute()
        {
            var db = BuiltIn();
            foreach (var ammo in new long[] { MunitionTkbCatalog.TkbStone, MunitionTkbCatalog.TkbM67Grenade, MunitionTkbCatalog.TkbStunGrenade,
                                              MunitionTkbCatalog.TkbTearGasGrenade, MunitionTkbCatalog.TkbTearGasCanister, MunitionTkbCatalog.TkbSmokeGrenade })
            {
                Assert.True(db.TryGetByType(ammo, out var t), $"{ammo}");
                Assert.True(t!.GetDescriptor<HandItemDto>() != null, $"{t.Name}: a hand may pick it up");
                Assert.True(t.GetDescriptor<MunitionVisualDto>() != null, $"{t.Name}: it must be SEEN to be picked up on another node");
            }
            Assert.Equal(60f, db.GetByType(MunitionTkbCatalog.TkbStone)!.GetDescriptor<HandItemDto>()!.LiesSeconds);
            Assert.Null(db.GetByType(MunitionTkbCatalog.TkbBatonRound)!.GetDescriptor<HandItemDto>());   // a fired baton round is not picked up
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3235</c> (B-13, AW-8, §7.10 PU-7) — a man standing in a STONE SOURCE (the built-in area type 8812) is topped up to 5 stones;
        /// outside it, nothing; a second refill takes nothing more. ✅ Red-proof: the point-in-area test inverted ⇒ red.
        /// </summary>
        [Fact]
        public void CE3235_AManInAStoneSource_IsToppedUpToFive_AndOutsideItGetsNone()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<Hrot.IG.Components.EditablePolyline>();
            world.RegisterComponent<TkbIdentity>();
            world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();
            world.RegisterComponent<Fdp.Toolkit.Combat.Components.AmmoLoadout>();
            world.RegisterComponent<PartMetadata>();
            var db = BuiltIn();
            world.SetSingletonManaged<ITkbDatabase>(db);
            var source = world.CreateEntity();
            world.AddComponent(source, new SimTransform { Position = new System.Numerics.Vector3(100, 100, 0), Rotation = System.Numerics.Quaternion.Identity });
            world.AddComponent(source, new TkbIdentity { TkbType = Hrot.Map.Common.TkbEntityTypes.StoneSource });
            world.SetManagedComponent(source, new Hrot.IG.Components.EditablePolyline
                { Points = new() { new(-5, -5), new(5, -5), new(5, 5), new(-5, 5) } });

            Entity Man(float x, float y)
            {
                var e = world.CreateEntity();
                world.AddComponent(e, new SimTransform { Position = new System.Numerics.Vector3(x, y, 0), Rotation = System.Numerics.Quaternion.Identity });
                world.AddComponent(e, new TkbIdentity { TkbType = CivilianTkbCatalog.TkbCivilianAdultMale });
                new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator().Inject(world, e, db.GetByType(CivilianTkbCatalog.TkbCivilianAdultMale)!);
                return e;
            }
            var inside = Man(102, 99);
            var outside = Man(120, 100);
            Assert.Equal(5, Fdp.Toolkit.Combat.ItemSources.Refill(world, inside));
            Assert.Equal(5, Fdp.Toolkit.Combat.PersonHands.Rounds(world, inside, MunitionTkbCatalog.TkbStone));
            Assert.Equal(0, Fdp.Toolkit.Combat.ItemSources.Refill(world, inside));   // already full
            Assert.Equal(0, Fdp.Toolkit.Combat.ItemSources.Refill(world, outside));
            Assert.Equal(0, Fdp.Toolkit.Combat.PersonHands.Rounds(world, outside, MunitionTkbCatalog.TkbStone));
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
        /// <summary>
        /// ⭐⭐ <c>CE-3239</c> (docs/DESIGN_Civilians_And_Crowd_Control.md §7.3c) — every built-in cloud round FLIES as an area round, so the
        /// flight bursts it on the ground (the rail below publishes the burst by hand; this is the half it never saw).
        /// </summary>
        [Fact]
        public void CE3239_EveryBuiltInCloudRound_FliesAsAnAreaRound()
        {
            var db = BuiltIn();
            int clouds = 0;
            foreach (var t in db.GetAll())
            {
                if (t.GetDescriptor<WarheadDto>() is not { HasCloud: true } w) continue;
                clouds++;
                Assert.True((Fdp.Toolkit.Combat.Components.WarheadRound.BitsOf(w, out _) & Fdp.Toolkit.Combat.Components.WarheadRound.Area) != 0, t.Name);
            }
            Assert.True(clouds >= 3, $"tear-gas grenade, canister and smoke: {clouds}");
        }

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
