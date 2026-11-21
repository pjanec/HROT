using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Behavior
{
    /// <summary>
    /// ⭐ B-5 S5 backend (docs/UX/DESIGN_Initial_State.md §3 "Damage") — the damage per body part as attributes (85 <c>Damage.Part.&lt;part&gt;</c>, 86 <c>Damage.MobilityKill</c>),
    /// applied on the node that applies the unit's Health; the capabilities follow the write at once (<c>CombatLife</c>), raising or lowering.
    /// </summary>
    public sealed class DamageAttributesTests : IDisposable
    {
        private const long Soldier = 1, Tank = 6;
        private readonly EntityRepository _repo = new();

        public DamageAttributesTests()
        {
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<ActorCapabilityState>();
            _repo.RegisterComponent<NetworkAuthority>();
            _repo.RegisterComponent<TkbIdentity>();
            _repo.RegisterComponent<PartDamage>();
            _repo.RegisterComponent<IsEmbarkedTag>();
            _repo.RegisterComponent<IsAssistingTag>();
            var db = new TkbDatabase();
            db.Register(new TkbTemplate($"T{Soldier}", Soldier) { DisType = new DISEntityType { Kind = 3, Domain = 1, Category = 1 } });
            var tank = new TkbTemplate($"T{Tank}", Tank) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 1 } };
            tank.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 1000f, ArmorFront = 500f, ArmorSide = 80f, ArmorRear = 40f, ArmorTop = 30f });
            db.Register(tank);
            _repo.SetSingletonManaged<ITkbDatabase>(db);
        }

        public void Dispose() => _repo.Dispose();

        private Entity Unit(long type, bool withPartDamage = false)
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)type });
            _repo.AddComponent(e, new Health { Current = 100f, Max = 100f });
            _repo.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            if (withPartDamage)
            {
                _repo.AddComponent(e, new PartDamage { Plan = BodyZones.Of(_repo, e)!.Plan });
                _repo.SetAuthority<PartDamage>(e, true);
            }
            return e;
        }

        /// <summary>Applies one record through the binary interpreter; <c>true</c> when it was applied (not refused).</summary>
        private bool Apply(Entity e, ushort id, double v, short part = 0)
        {
            var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter();
            var patch = EcsPatchContext.Create(_repo, e);
            var ctx = interpreter.CreateContext(patch);
            ctx.Repo = _repo; ctx.Entity = e;
            interpreter.Apply(ctx, new[] { new EntityAttributeChange { AttributeId = id, SubIndex1 = part, Value = AttributeValue.FromDouble(v) } });
            return patch.HasAppliedAny;
        }

        private ActorCapabilities Caps(Entity e) => _repo.GetComponentRO<ActorCapabilityState>(e).Capabilities;

        /// <summary>A level is set, rounded and clamped to 0..255 on its own part only; a part id out of range is refused and changes nothing. ✅ Red-proof: the clamp removed ⇒ 300 wraps ⇒ red.</summary>
        [Fact]
        public void S5_APartLevel_IsSetRoundedAndClamped_AndAnOutOfRangePartIsRefused()
        {
            var man = Unit(Soldier, withPartDamage: true);
            Assert.True(Apply(man, AttributeIds.DamagePart, 100.4, PersonPart.Torso));
            Assert.Equal(100, _repo.GetComponentRO<PartDamage>(man).LevelOf(PersonPart.Torso));
            Assert.True(Apply(man, AttributeIds.DamagePart, 300, PersonPart.Head));
            Assert.Equal(255, _repo.GetComponentRO<PartDamage>(man).LevelOf(PersonPart.Head));
            Assert.True(Apply(man, AttributeIds.DamagePart, -5, PersonPart.Head));
            Assert.Equal((0, 100), (_repo.GetComponentRO<PartDamage>(man).LevelOf(PersonPart.Head), _repo.GetComponentRO<PartDamage>(man).LevelOf(PersonPart.Torso)));

            Assert.False(Apply(man, AttributeIds.DamagePart, 50, PartDamage.MaxParts));
            Assert.False(Apply(man, AttributeIds.DamagePart, 50, -1));
            Assert.Equal(100, _repo.GetComponentRO<PartDamage>(man).LevelOf(PersonPart.Torso));
        }

        /// <summary>A unit with no PartDamage gets one carrying the body's plan; a node that does not apply the unit's Health adds nothing. ✅ Red-proof: the plan left at None ⇒ red.</summary>
        [Fact]
        public void S5_TheComponentIsAddedWhenAbsent_WithTheBodysPlan_OnlyByTheHealthOwner()
        {
            var tank = Unit(Tank);
            Assert.True(Apply(tank, AttributeIds.DamagePart, 40, VehiclePart.Gun));
            var d = _repo.GetComponentRO<PartDamage>(tank);
            Assert.Equal((BodyPlan.TrackedVehicle, (byte)40), (d.Plan, d.LevelOf(VehiclePart.Gun)));

            var replica = Unit(Tank);
            _repo.AddComponent(replica, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));   // another node owns its Health
            Assert.False(Apply(replica, AttributeIds.DamagePart, 40, VehiclePart.Gun));
            Assert.False(_repo.HasComponent<PartDamage>(replica));
        }

        /// <summary>
        /// Mobility kill set ⇒ every Mobility part is over its threshold and CanMove is off AT ONCE; clear ⇒ the parts are 0 and CanMove is back (CanShoot untouched).
        /// ✅ Red-proof: the Restore call removed ⇒ CanMove never returns ⇒ red.
        /// </summary>
        [Fact]
        public void S5_MobilityKill_StripsCanMoveAtOnce_AndClearGivesItBack()
        {
            var tank = Unit(Tank);
            Assert.True(Apply(tank, AttributeIds.DamageMobilityKill, 1));
            Assert.True(Wounds.Has(_repo, tank, WoundEffect.Mobility));
            Assert.Equal(ActorCapabilities.CanShoot, Caps(tank));

            Assert.True(Apply(tank, AttributeIds.DamageMobilityKill, 0));
            Assert.False(Wounds.Has(_repo, tank, WoundEffect.Mobility));
            Assert.Equal(ActorCapabilities.CanMove | ActorCapabilities.CanShoot, Caps(tank));
        }

        /// <summary>A unit that is aboard, assisting, or down does not get CanMove back when the damage is lowered. ✅ Red-proof: the IsEmbarkedTag test removed ⇒ the aboard unit moves ⇒ red.</summary>
        [Fact]
        public void S5_LoweringTheDamage_DoesNotGiveCanMoveToAUnitAboardAssistingOrDown()
        {
            var aboard = Unit(Tank);
            Apply(aboard, AttributeIds.DamageMobilityKill, 1);
            _repo.AddComponent(aboard, new IsEmbarkedTag { VehicleEntity = Unit(Tank) });
            Apply(aboard, AttributeIds.DamageMobilityKill, 0);
            Assert.Equal(ActorCapabilities.CanShoot, Caps(aboard));   // boarding stripped CanMove too: the seat keeps it off

            var assisting = Unit(Tank);
            Apply(assisting, AttributeIds.DamageMobilityKill, 1);
            _repo.AddComponent(assisting, new IsAssistingTag { Carrier = aboard });
            Apply(assisting, AttributeIds.DamageMobilityKill, 0);
            Assert.False(Caps(assisting).HasFlag(ActorCapabilities.CanMove));

            var dead = Unit(Tank);
            Apply(dead, AttributeIds.DamageMobilityKill, 1);
            _repo.GetComponentRW<Health>(dead).Current = 0f;
            Apply(dead, AttributeIds.DamageMobilityKill, 0);
            Assert.False(Caps(dead).HasFlag(ActorCapabilities.CanMove));
        }

        /// <summary>A lowered part that leaves ANOTHER Mobility part over its threshold keeps CanMove off (the same Wounds test strips and restores). ✅ Red-proof: Restore ignoring Wounds ⇒ red.</summary>
        [Fact]
        public void S5_LoweringOnePart_WhileAnotherMobilityPartIsStillWounded_KeepsCanMoveOff()
        {
            var tank = Unit(Tank);
            Apply(tank, AttributeIds.DamagePart, 255, VehiclePart.RunningGearL);
            Apply(tank, AttributeIds.DamagePart, 255, VehiclePart.RunningGearR);
            Assert.False(Caps(tank).HasFlag(ActorCapabilities.CanMove));
            Apply(tank, AttributeIds.DamagePart, 0, VehiclePart.RunningGearL);
            Assert.False(Caps(tank).HasFlag(ActorCapabilities.CanMove));
            Apply(tank, AttributeIds.DamagePart, 0, VehiclePart.RunningGearR);
            Assert.True(Caps(tank).HasFlag(ActorCapabilities.CanMove));
        }

        /// <summary>A type with no Mobility zone (a man) refuses the mobility switch and gets no component. ✅ Red-proof: the HasMobilityZone refusal removed ⇒ red.</summary>
        [Fact]
        public void S5_MobilityKill_IsRefusedForATypeWithNoMobilityZone()
        {
            var man = Unit(Soldier);
            Assert.False(Apply(man, AttributeIds.DamageMobilityKill, 1));
            Assert.False(_repo.HasComponent<PartDamage>(man));
            Assert.Equal(ActorCapabilities.CanMove | ActorCapabilities.CanShoot, Caps(man));
        }

        /// <summary>The JSON route and the vocabulary carry the same two paths.</summary>
        [Fact]
        public void S5_TheJsonSetter_WritesTheLevel_AndTheVocabularyNamesBothPaths()
        {
            var vocab = System.Linq.Enumerable.ToList(AttributeVocabulary.All);
            foreach (var path in new[] { "Damage.Part.*", "Damage.MobilityKill" })
                Assert.Contains(vocab, d => d.JsonPath == path);
            var c = default(PartDamage);
            Assert.False(FormAttributes.SetPartLevel(ref c, 3, 128.4));
            Assert.Equal(128, c.LevelOf(3));
            Assert.True(FormAttributes.SetPartLevel(ref c, 3, 0));
        }
    }
}
