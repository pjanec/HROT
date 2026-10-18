using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Brains;
using Hrot.IG.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2282</c> DA-L (DA-20, DA-24, DA-26…DA-30; 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §13c) —
    /// SECTORS: what a man may CHOOSE to shoot (Allow, by tier, with self-defence) and where a round may NEVER end (NoFire, through his
    /// chain, always); the ROE-twin event that sets them; the fire picks that keep him inside his sector while he hides from every threat.
    /// </summary>
    public sealed class EngagementAreaTests : System.IDisposable
    {
        private readonly EntityRepository _w = new();
        private double _time = 100;

        public EngagementAreaTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
            if (!_w.IsComponentTypeRegistered<NetworkIdentity>()) _w.RegisterComponent<NetworkIdentity>();
            if (!_w.TryGetTable(typeof(EditablePolyline), out _)) _w.RegisterManagedComponent<EditablePolyline>();
            Tick();
        }

        public void Dispose() => _w.Dispose();

        private void Tick() => _w.SetSingleton(new GlobalTime { TotalTime = _time += 0.016, DeltaTime = 0.016f, TimeScale = 1f });

        private Entity At(float x, float y, Entity commander = default)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
            if (!commander.IsNull) _w.AddComponent(e, new UnitSubordinate { Commander = commander });
            return e;
        }

        private void Proficiency(Entity e, float value)
            => _w.AddComponent(e, new CombatProficiency { Authored = value, Set = CombatProficiency.AuthoredSet });

        private bool Set(Entity e, float left, float right, BehaviorOrigin origin = BehaviorOrigin.Superior, long noFire = 0)
        {
            var evt = new SetEngagementAreaEvent { Entity = e, Origin = origin, ArcSet = 1, ArcOrigin = Pos(e), LeftBearing = left, RightBearing = right };
            if (noFire != 0) { evt.NoFire.Count = 1; evt.NoFire.Items[0] = noFire; }
            return EngagementAreaSystem.Apply(_w, evt);
        }

        private Vector3 Pos(Entity e) => _w.HasComponent<SimTransform>(e) ? _w.GetComponentRO<SimTransform>(e).Position : default;

        /// <summary>An area entity: a 20 m square around <paramref name="centre"/>, network id <paramref name="netId"/>.</summary>
        private void Area(long netId, Vector3 centre)
        {
            var a = At(centre.X, centre.Y);
            _w.AddComponent(a, new NetworkIdentity(netId));
            _w.SetManagedComponent(a, new EditablePolyline { Points = new List<Vector2> { new(-10, -10), new(10, -10), new(10, 10), new(-10, 10) } });
        }

        [Fact]
        public void CE2282_TheArc_CompassBearings_ClockwiseWrap_AndRanges()
        {
            var o = Vector3.Zero;
            Assert.True(EngagementAreas.InArc(o, -45, 45, 0, 0, new Vector3(0, 100, 0)));     // north
            Assert.False(EngagementAreas.InArc(o, -45, 45, 0, 0, new Vector3(100, 0, 0)));    // east
            Assert.True(EngagementAreas.InArc(o, 45, 135, 0, 0, new Vector3(100, 0, 0)));     // east, 90°
            Assert.True(EngagementAreas.InArc(o, 350, 10, 0, 0, new Vector3(1, 100, 0)));     // wraps through north
            Assert.False(EngagementAreas.InArc(o, 350, 10, 0, 0, new Vector3(0, -100, 0)));
            Assert.False(EngagementAreas.InArc(o, -45, 45, 0, 50, new Vector3(0, 100, 0)));   // beyond max range
            Assert.False(EngagementAreas.InArc(o, -45, 45, 150, 0, new Vector3(0, 100, 0)));  // inside min range
            Assert.True(EngagementAreas.InArc(o, 0, 360, 0, 0, new Vector3(0, -100, 0)));     // a full circle
        }

        /// <summary>DA-29 — a man's own arc counts from Regular up; his commander's (the squad level) from Militia up; Untrained none;
        /// no sector, everything. ✅ Red-proof: the tier gate removed ⇒ a Militia man keeps to his own arc.</summary>
        [Fact]
        public void CE2282_Allow_ByTier_OwnFromRegular_CommandersFromMilitia_UntrainedNone()
        {
            var squad = At(0, 0);
            var man = At(0, 0, squad);
            var north = new Vector3(0, 100, 0);
            var east = new Vector3(100, 0, 0);
            Assert.True(EngagementAreas.Allows(_w, man, east));                        // no sector

            Assert.True(Set(man, -45, 45));
            Assert.True(EngagementAreas.Allows(_w, man, north));
            Assert.False(EngagementAreas.Allows(_w, man, east));                       // Regular: his own arc

            Proficiency(man, ProficiencyOf.Militia);
            Assert.True(EngagementAreas.Allows(_w, man, east));                        // Militia: his own arc does not bind him
            Assert.True(Set(squad, -45, 45));
            Assert.False(EngagementAreas.Allows(_w, man, east));                       // …the squad's does
            Assert.False(EngagementAreas.AllowsUnit(_w, squad, east));

            _w.GetComponentRW<CombatProficiency>(man).Authored = ProficiencyOf.Untrained;
            Assert.True(EngagementAreas.Allows(_w, man, east));                        // Untrained: none
        }

        /// <summary>DA-28 — self-defence: hit or near-missed within 5 s, a contact within 50 m is allowed outside his sector; farther, not.
        /// ✅ Red-proof: the self-defence branch removed ⇒ the near contact stays refused.</summary>
        [Fact]
        public void CE2282_SelfDefence_ANearContact_WhileUnderFire()
        {
            var man = At(0, 0);
            Assert.True(Set(man, -45, 45));
            var near = new Vector3(30, 0, 0);
            Assert.False(EngagementAreas.Allows(_w, man, near));
            var senses = new RecentSenses();
            senses.Record(SensorChange.NearMiss, _time);
            _w.AddComponent(man, senses);
            Assert.True(EngagementAreas.Allows(_w, man, near));
            Assert.False(EngagementAreas.Allows(_w, man, new Vector3(100, 0, 0)));     // beyond 50 m
            _time += 6; Tick();
            Assert.False(EngagementAreas.Allows(_w, man, near));                       // 6 s later
        }

        /// <summary>DA-26 — the ROE twin: a lower origin than the setter's is refused, the event replaces the set, all empty withdraws.</summary>
        [Fact]
        public void CE2282_TheEvent_OriginGated_Replaces_AndWithdraws()
        {
            var man = At(0, 0);
            Assert.True(Set(man, -45, 45, BehaviorOrigin.Superior));
            Assert.False(Set(man, 45, 135, BehaviorOrigin.Sop));                        // lower than the setter
            Assert.Equal(-45f, _w.GetComponentRO<EngagementArea>(man).LeftBearing);
            Assert.True(Set(man, 45, 135, BehaviorOrigin.Superior));                    // replaced
            Assert.Equal(45f, _w.GetComponentRO<EngagementArea>(man).LeftBearing);
            Assert.True(EngagementAreaSystem.Apply(_w, new SetEngagementAreaEvent { Entity = man, Origin = BehaviorOrigin.Superior }));
            Assert.Equal(BehaviorOrigin.Unmarked, _w.GetComponentRO<EngagementArea>(man).SetBy);
            Assert.True(EngagementAreas.Allows(_w, man, new Vector3(-100, 0, 0)));      // withdrawn ⇒ everything
            Assert.True(Set(man, -45, 45, BehaviorOrigin.Sop));                         // and anyone may set it again
        }

        /// <summary>⭐ NoFire through the chain: an area owned by the PLATOON stops the round of a man two levels down, at the gate
        /// (<c>HoldOf</c>, both the aimed and the turret path) — whatever his sector and his tier, and self-defence does not lift it.
        /// ✅ Red-proof: the gate's NoFire check removed ⇒ no hold.</summary>
        [Fact]
        public void CE2282_NoFire_OwnedByThePlatoon_HoldsAManTwoLevelsDown_AtTheGate()
        {
            Area(777, new Vector3(0, 100, 0));
            var platoon = At(0, -50);
            var squad = At(0, -10, platoon);
            var man = At(0, 0, squad);
            Proficiency(man, ProficiencyOf.Untrained);
            var inside = At(0, 105);
            var outside = At(50, 100);
            Assert.Null(AimAndFireExecutor.HoldOf(_w, man, inside, null, 0));

            Assert.True(EngagementAreaSystem.Apply(_w, new SetEngagementAreaEvent
            {
                Entity = platoon, Origin = BehaviorOrigin.Superior, NoFire = NoFire(777),
            }));
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldNoFireArea, AimAndFireExecutor.HoldOf(_w, man, inside, null, 0));
            Assert.Null(AimAndFireExecutor.HoldOf(_w, man, outside, null, 0));
            Assert.True(EngagementAreas.IsNoFire(_w, man, new Vector3(5, 95, 0)));
            Assert.False(EngagementAreas.IsNoFire(_w, At(0, 0), new Vector3(5, 95, 0)));   // not in that chain

            var senses = new RecentSenses();
            senses.Record(SensorChange.Hit, _time);
            _w.AddComponent(man, senses);
            Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldNoFireArea, AimAndFireExecutor.HoldOf(_w, man, inside, null, 0));
        }

        /// <summary>⭐ DA-27 — the fire pick: the threat he hides from lies outside his sector ⇒ he shoots the first remembered contact INSIDE
        /// it (<c>LockTarget</c>, the posture step's fallback); none inside ⇒ nothing. ✅ Red-proof: LockTarget without the Allow check ⇒
        /// the out-of-sector threat is locked.</summary>
        [Fact]
        public void CE2282_TheFirePick_TakesTheFirstContactInsideTheSector()
        {
            var man = At(0, 0);
            Assert.True(Set(man, -45, 45));
            var east = At(80, 0);
            var north = At(0, 80);
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, (long)east.PackedValue, 80, 0, 5f, 1);
            TargetMemory.AddOrUpdateTarget(ref mem, (long)north.PackedValue, 0, 80, 1f, 1);
            _w.AddComponent(man, mem);

            Assert.Equal(north, EqsTacticsNodes.FirstAllowedRemembered(_w, man));
            Assert.Equal(north, PeekAndFireNodes.LockTarget(_w, man, new ThreatAim(east, 0, default)));
            Assert.Equal(north, PeekAndFireNodes.LockTarget(_w, man, new ThreatAim(north, 0, default)));

            Assert.True(Set(man, 170, 190));                                           // a sector with nobody in it
            Assert.True(EqsTacticsNodes.FirstAllowedRemembered(_w, man).IsNull);
            Assert.True(PeekAndFireNodes.LockTarget(_w, man, new ThreatAim(east, 0, default)).IsNull);
        }

        private static NoFireAreaList NoFire(long id)
        {
            var l = new NoFireAreaList { Count = 1 };
            l.Items[0] = id;
            return l;
        }
    }
}
