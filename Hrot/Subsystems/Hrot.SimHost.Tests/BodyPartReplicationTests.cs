using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3210</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-6, HL-8) — a body's per-part damage (written by
    /// the Health owner) and its hit marks (written by the muscle) reach every other node whole: the egress wire form applied by the
    /// ingress on a second world gives back the same component.
    /// </summary>
    public sealed class BodyPartReplicationTests
    {
        private static (EntityRepository World, Entity Unit, NetworkEntityMap Map) Node(int netId)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<PartDamage>();
            w.RegisterComponent<HitMarks>();
            w.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            w.RegisterComponent<Fdp.Toolkit.Creatures.Demeanour>();
            w.RegisterComponent<Fdp.Toolkit.Perception.Radar.RadarEmission>();   // ⭐ CE-4617
            w.RegisterComponent<Fdp.Toolkit.Civilians.HostileAct>();   // ⭐ CE-3238
            w.RegisterComponent<Fdp.Toolkit.Combat.AreaEffectCloud>();
            w.RegisterComponent<Fdp.Toolkit.Perception.Look.LookState>();   // ⭐ CE-3252
            w.RegisterComponent<Fdp.Toolkit.Civilians.Custody>();   // ⭐ CE-3256
            w.RegisterComponent<Fdp.Toolkit.Combat.Lean>();   // ⭐ CE-3257
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.CombatProficiency>();   // ⭐ CE-3259
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.EngagementArea>();   // ⭐ CE-3261
            var unit = w.CreateEntity();
            w.AddComponent(unit, new NetworkIdentity { Value = netId });
            var map = new NetworkEntityMap();
            map.Register(netId, unit);
            return (w, unit, map);
        }

        [Fact]
        public unsafe void CE3210_PartDamage_TheOwnersLevels_ReachTheOtherNode()
        {
            var damage = new PartDamage { Plan = BodyPlan.Person };
            damage.Add(PersonPart.Head, 200);
            damage.Add(PersonPart.ShinL, 64);
            damage.Add(15, 7);
            var sample = EntityPartDamageEgressTranslator.ToSample(41, damage);

            var (node, unit, map) = Node(41);
            using (node)
            {
                var ingress = new EntityPartDamageIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<PartDamage>(unit);
                Assert.Equal(BodyPlan.Person, got.Plan);
                for (byte p = 0; p < PartDamage.MaxParts; p++) Assert.Equal(damage.LevelOf(p), got.LevelOf(p));
            }
        }

        [Fact]
        public void CE3210_HitMarks_TheRingCrosses_InSequence_EvenAfterItWrapped()
        {
            var marks = new HitMarks();
            for (int i = 0; i < HitMarks.Capacity + 5; i++)
                marks.Add((byte)(i % 10), new Vector3(i / 100f, -0.25f, 0.5f), (byte)(i % 4), i % 2 == 0 ? HitMarkKind.Bullet : HitMarkKind.Fragment);
            var sample = EntityHitMarksEgressTranslator.ToSample(42, marks);
            Assert.Equal(HitMarks.Capacity, sample.Marks.Count);
            Assert.Equal(5, sample.Marks[0].Seq);   // the oldest still in the ring

            var (node, unit, map) = Node(42);
            using (node)
            {
                var ingress = new EntityHitMarksIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<HitMarks>(unit);
                Assert.Equal(marks.NextSeq, got.NextSeq);
                for (int seq = 0; seq < marks.NextSeq; seq++)
                {
                    bool had = marks.TryGet(seq, out var p0, out var l0, out var f0, out var k0);
                    Assert.Equal(had, got.TryGet(seq, out var p1, out var l1, out var f1, out var k1));
                    if (!had) continue;
                    Assert.Equal((p0, f0, k0), (p1, f1, k1));
                    Assert.Equal(l0, l1);
                }
            }
        }

        /// <summary>⭐ <c>CE-3219</c> + ui finding F-3 (civilians design §7.6) — a person's effects, the behaviour's cower mode included,
        /// reach the other node whole; the owner publishes again when only the mode changes.</summary>
        [Fact]
        public void CE3219_F3_EffectState_WithCowering_ReachesTheOtherNode()
        {
            var effects = new Fdp.Toolkit.Combat.EffectState { StunnedUntil = (SimInstant)12.5, IrritatedUntil = (SimInstant)3, KnockedDownUntil = (SimInstant)7.25, Cowering = 1 };
            var sample = EntityEffectStateEgressTranslator.ToSample(44, effects);

            var (node, unit, map) = Node(44);
            using (node)
            {
                var ingress = new EntityEffectStateIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(effects, node.GetComponentRO<Fdp.Toolkit.Combat.EffectState>(unit));
            }

            Assert.NotEqual(sample, EntityEffectStateEgressTranslator.ToSample(44, effects with { Cowering = 0 }));
        }

        /// <summary>⭐ <c>CE-4617</c> (B-1, RS-10) — a radar's emission reaches the other node with its tracked target as THAT node's entity (by network
        /// id), its on-since clock and its range; switching off is a change. 🔴 Red-proof: the ingress dropping the tracked target.</summary>
        [Fact]
        public void CE4617_RadarEmission_ReachesTheOtherNode_WithTheTrackedTargetAsThatNodesEntity()
        {
            using var sender = new EntityRepository();
            sender.RegisterComponent<NetworkIdentity>();
            var heliHere = sender.CreateEntity();
            sender.AddComponent(heliHere, new NetworkIdentity { Value = 77 });
            var tracking = new Fdp.Toolkit.Perception.Radar.RadarEmission
            {
                Mode = Fdp.Toolkit.Perception.Radar.RadarMode.Track, TrackedTarget = heliHere,
                EmitterClass = Fdp.Toolkit.Perception.Radar.RadarEmitterClass.AirDefence, RangeM = 8000f, OnSince = (Fdp.Core.SimInstant)12.5,
            };
            var sample = Hrot.Map.Common.Replication.Egress.EntityRadarEmissionEgressTranslator.ToSample(sender, 46, tracking);
            Assert.Equal(77, sample.TrackedTarget);

            var (node, unit, map) = Node(46);
            using (node)
            {
                var heliThere = node.CreateEntity();
                map.Register(77, heliThere);
                var ingress = new Hrot.Map.Common.Replication.Ingress.EntityRadarEmissionIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<Fdp.Toolkit.Perception.Radar.RadarEmission>(unit);
                Assert.True(Fdp.Toolkit.Perception.Radar.RadarEmissions.IsOn(node, unit));
                Assert.Equal(Fdp.Toolkit.Perception.Radar.RadarMode.Track, got.Mode);
                Assert.Equal(heliThere, got.TrackedTarget);
                Assert.Equal(8000f, got.RangeM);
                Assert.Equal(12.5, (double)got.OnSince);
            }
            Assert.NotEqual(sample, Hrot.Map.Common.Replication.Egress.EntityRadarEmissionEgressTranslator.ToSample(sender, 46, default));
        }

        /// <summary>
        /// ⭐ <c>CE-3224</c> / <c>CE-4628</c> §10c rail (2) — the ONE demeanour record reaches the other node whole: the gesture (<c>Kind</c>) AND the mood (Mode, Fear,
        /// Anger) cross the wire (egress → ingress), and a change of the gesture alone — back to None included — is a different sample (a protester lowering his fist
        /// is a change). 🔴 Red-proof: the egress or the ingress dropping <c>Kind</c> ⇒ red.
        /// </summary>
        [Fact]
        public void CE4628_TheDemeanour_ReachesTheOtherNode_KindModeFearAndAnger()
        {
            var sent = new Fdp.Toolkit.Creatures.Demeanour { Kind = Fdp.Toolkit.Combat.GestureKind.FistRaised, Mode = Fdp.Toolkit.Civilians.CivilianMode.Riot, Fear = 2, Anger = 3 };
            var sample = Hrot.Map.Common.Replication.Egress.EntityDemeanourEgressTranslator.ToSample(45, sent);

            var (node, unit, map) = Node(45);
            using (node)
            {
                var ingress = new Hrot.Map.Common.Replication.Ingress.EntityDemeanourIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(Fdp.Toolkit.Combat.GestureKind.FistRaised, Fdp.Toolkit.Combat.Gestures.Of(node, unit));
                Assert.True(Fdp.Toolkit.Creatures.Demeanours.Of(node, unit, out var got));
                Assert.Equal((sent.Kind, sent.Mode, sent.Fear, sent.Anger), (got.Kind, got.Mode, got.Fear, got.Anger));
            }

            var lowered = sent; lowered.Kind = Fdp.Toolkit.Combat.GestureKind.None;
            Assert.NotEqual(sample, Hrot.Map.Common.Replication.Egress.EntityDemeanourEgressTranslator.ToSample(45, lowered));
        }

        /// <summary>⭐ <c>CE-3238</c> (civilians B-16, §7.16 HA-1) — a man's hostile act reaches the other node with its kind and its time, and
        /// an UPGRADE (non-lethal → lethal) is a change the egress publishes. ✅ Red-proof: the ingress dropping the kind ⇒ None ⇒ red.</summary>
        [Fact]
        public void CE3238_HostileAct_ReachesTheOtherNode_AndAnUpgradeIsAChange()
        {
            var shot = new Fdp.Toolkit.Civilians.HostileAct { SinceSimTime = (SimInstant)12.5, Kind = Fdp.Toolkit.Civilians.HostileActKind.Lethal };
            var sample = EntityHostileActEgressTranslator.ToSample(46, shot);

            var (node, unit, map) = Node(46);
            using (node)
            {
                var ingress = new EntityHostileActIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(Fdp.Toolkit.Civilians.HostileActKind.Lethal, Fdp.Toolkit.Civilians.HostileActs.Of(node, unit));
                Assert.Equal(12.5, node.GetComponentRO<Fdp.Toolkit.Civilians.HostileAct>(unit).SinceSimTime);
                Assert.True(Fdp.Toolkit.Civilians.HostileActs.Committed(node, unit));
            }

            Assert.NotEqual(sample, EntityHostileActEgressTranslator.ToSample(46, shot with { Kind = Fdp.Toolkit.Civilians.HostileActKind.NonLethal }));
        }

        /// <summary>⭐ <c>CE-3256</c> (BR-4, §7.6 CU-5) — a man's custody reaches the other node (detained, restrained, searched, being searched),
        /// so every node reads him out of the fight; a release is a change the egress publishes; the FDP-side ordinal is the NED one.
        /// ✅ Red-proof: the ingress dropping <c>Restrained</c> ⇒ the replica's fire is not held ⇒ red.</summary>
        [Fact]
        public void CE3256_Custody_ReachesTheOtherNode_AndAReleaseIsAChange()
        {
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtCustody, Fdp.Toolkit.Civilians.CustodyRules.CustodyDescriptorOrdinal);
            var held = new Fdp.Toolkit.Civilians.Custody { Detained = 1, Restrained = 1, Searched = 1, Searching = 0 };
            var sample = EntityCustodyEgressTranslator.ToSample(47, held);

            var (node, unit, map) = Node(47);
            using (node)
            {
                var ingress = new EntityCustodyIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<Fdp.Toolkit.Civilians.Custody>(unit);
                Assert.Equal(((byte)1, (byte)1, (byte)1, (byte)0), (got.Detained, got.Restrained, got.Searched, got.Searching));
                Assert.True(Fdp.Toolkit.Civilians.CustodyRules.IsRestrained(node, unit));
                Assert.True(Fdp.Toolkit.Civilians.CustodyRules.InCustody(node, unit));
            }

            Assert.NotEqual(sample, EntityCustodyEgressTranslator.ToSample(47, held with { Detained = 0, Restrained = 0 }));
        }

        /// <summary>⭐ <c>CE-3257</c> (BR-5, hit location §13 LN-1/LN-8) — a man's lean reaches the other node (sight, fire and ui read it there),
        /// and straightening up is a change the egress publishes. ✅ Red-proof: the ingress dropping the side ⇒ LeanNone ⇒ red. ⭐ AQ104 G — the depth crosses too (dropped ⇒ the default 0.4 m ⇒ red).</summary>
        [Fact]
        public void CE3257_Lean_ReachesTheOtherNode_AndStraighteningUpIsAChange()
        {
            var lean = new Fdp.Toolkit.Combat.Lean { Side = Fdp.Toolkit.Combat.LeanSide.LeanRight, Depth = 100 };   // ⭐ AQ104 G — and his depth
            var sample = EntityLeanEgressTranslator.ToSample(48, lean);

            var (node, unit, map) = Node(48);
            using (node)
            {
                var ingress = new EntityLeanIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanRight, Fdp.Toolkit.Combat.Leans.Of(node, unit));
                Assert.Equal(100 / 255f * Fdp.Toolkit.Combat.Leans.MaxShift, Fdp.Toolkit.Combat.Leans.ShiftOf(node, unit), 4);   // ⭐ AQ104 G
            }

            Assert.NotEqual(sample, EntityLeanEgressTranslator.ToSample(48, default));
            Assert.NotEqual(sample, EntityLeanEgressTranslator.ToSample(48, lean with { Depth = 0 }));   // ⭐ AQ104 G — a deeper lean is a change
        }

        /// <summary>⭐ <c>CE-3259</c> (BR-6) — a soldier's proficiency reaches the node that resolves his shots with every source and set bit, so
        /// the muscle's spread reads the AUTHORED value (not only the type default it projected at spawn). ✅ Red-proof: the ingress dropping
        /// <c>Authored</c> ⇒ 0 behind its set bit ⇒ 2.5, not Militia's 1.6 ⇒ red.</summary>
        [Fact]
        public void CE3259_CombatProficiency_ReachesTheMuscle_TheAuthoredValueWins()
        {
            var p = new Fdp.Toolkit.Behavior.Components.CombatProficiency
            {
                Authored = Fdp.Toolkit.Behavior.Components.ProficiencyOf.Militia, TypeDefault = Fdp.Toolkit.Behavior.Components.ProficiencyOf.Regular,
                Set = Fdp.Toolkit.Behavior.Components.CombatProficiency.AuthoredSet | Fdp.Toolkit.Behavior.Components.CombatProficiency.TypeDefaultSet,
            };
            var sample = EntityCombatProficiencyEgressTranslator.ToSample(49, p);

            var (node, unit, map) = Node(49);
            using (node)
            {
                var ingress = new EntityCombatProficiencyIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(1.6f, Fdp.Toolkit.Behavior.Components.ProficiencyOf.SpreadFactor(node, unit));   // Militia — ⚠ not Untrained: a
                                                                                                           // dropped Authored reads 0 = Untrained
            }

            Assert.NotEqual(sample, EntityCombatProficiencyEgressTranslator.ToSample(49, p with { Authored = Fdp.Toolkit.Behavior.Components.ProficiencyOf.Elite }));
        }

        /// <summary>⭐ <c>CE-3261</c> (behaviors' sectors note ②) — a commander's sector reaches the other node with every field, so a man
        /// whose brain runs there reads his commander's NoFire through the chain. ✅ Red-proof: the ingress dropping the NoFire ids ⇒ red.</summary>
        [Fact]
        public void CE3261_ASector_ReachesTheOtherNode_WithItsNoFireAreas()
        {
            var a = new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, ArcOrigin = new Vector3(1f, 2f, 3f), LeftBearing = 300f, RightBearing = 60f, MaxRange = 400f, AllowArea = 4711,
                WatchBearing = 15f, WatchSet = 1, SetBy = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior,
            };
            a.NoFire.Items[0] = 4712; a.NoFire.Items[1] = 4713; a.NoFire.Count = 2;
            var sample = EntityEngagementAreaEgressTranslator.ToSample(50, a);

            var (node, unit, map) = Node(50);
            using (node)
            {
                var ingress = new EntityEngagementAreaIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<Fdp.Toolkit.Behavior.Components.EngagementArea>(unit);
                Assert.Equal((a.ArcSet, a.ArcOrigin, a.MaxRange, a.AllowArea, a.SetBy), (got.ArcSet, got.ArcOrigin, got.MaxRange, got.AllowArea, got.SetBy));
                // ⭐ AQ107 I — the ECS keeps compass DEGREES; they cross the wire as radians, so they come back to within a float step
                Assert.Equal(a.LeftBearing, got.LeftBearing, 1e-3f);
                Assert.Equal(a.RightBearing, got.RightBearing, 1e-3f);
                Assert.Equal(a.WatchBearing, got.WatchBearing, 1e-3f);
                Assert.Equal((2, 4712L, 4713L), (got.NoFire.Count, got.NoFire.Items[0], got.NoFire.Items[1]));
            }
            var withdrawn = EntityEngagementAreaEgressTranslator.ToSample(50, default);
            Assert.NotEqual(sample, withdrawn);
        }

        /// <summary>
        /// ⭐ <c>AQ107 I</c> — a sector's bearings are RADIANS on the wire (the ECS <c>EngagementArea</c> keeps compass degrees): 300° leaves
        /// the egress as 5π/3, 60° as π/3, a watch of 15° as π/12, and a negative bearing is NOT normalised (−45° ⇒ −π/4); the ingress gives
        /// the same degrees back. ✅ Red-proof: the old degrees copied straight into the sample (the <c>DegToRad</c> dropped) ⇒ 300 on the wire ⇒ red.
        /// </summary>
        [Fact]
        public void AQ107I_ASectorsBearings_AreRadiansOnTheWire_AndDegreesInTheEcs()
        {
            var a = new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, LeftBearing = 300f, RightBearing = 60f, MaxRange = 400f, WatchBearing = 15f, WatchSet = 1,
            };
            var sample = EntityEngagementAreaEgressTranslator.ToSample(50, a);
            Assert.Equal(5f * System.MathF.PI / 3f, sample.LeftBearing, 1e-5f);
            Assert.Equal(System.MathF.PI / 3f, sample.RightBearing, 1e-5f);
            Assert.Equal(System.MathF.PI / 12f, sample.WatchBearing, 1e-5f);
            Assert.Equal(400f, sample.MaxRange);   // a range is metres — untouched

            var west = EntityEngagementAreaEgressTranslator.ToSample(50, new Fdp.Toolkit.Behavior.Components.EngagementArea { ArcSet = 1, LeftBearing = -45f });
            Assert.Equal(-System.MathF.PI / 4f, west.LeftBearing, 1e-5f);

            var back = EntityEngagementAreaIngressTranslator.FromSample(in sample);
            Assert.Equal((300f, 60f, 15f), (Round2(back.LeftBearing), Round2(back.RightBearing), Round2(back.WatchBearing)));
        }

        private static float Round2(float degrees) => System.MathF.Round(degrees, 2);

        /// <summary>
        /// ⭐ <c>AQ107 I</c> — weather is SI on the wire (the ECS <c>WeatherConditions</c> keeps °C / K per km / hPa / mm per h / compass
        /// degrees): the ISA day (15 °C, 6.5 K/km, 1013.25 hPa) is 288.15 K, 0.0065 K/m, 101 325 Pa; 36 mm/h is 1e-5 m/s; a wind from 90°
        /// is π/2; and the ingress gives the ECS values back. ✅ Red-proof: leaving the egress unit conversions off ⇒ 15 K on the wire ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void AQ107I_Weather_IsSIOnTheWire_AndTheEcsKeepsItsUnits()
        {
            var w = Fdp.Toolkit.Weather.WeatherConditions.Standard;   // 15 °C, 6.5 K/km, 1013.25 hPa
            w.PrecipitationMmPerHour = 36f;
            w.WindCount = 2;
            w.WindFromDeg[0] = 90f; w.WindSpeedMps[0] = 5f;
            w.WindFromDeg[1] = 270f; w.WindSpeedMps[1] = 8f;

            var s = EntityWeatherEgressTranslator.ToSample(60, w);
            Assert.Equal(288.15f, s.GroundTemperature, 1e-3f);
            Assert.Equal(0.0065f, s.LapseRate, 1e-7f);
            Assert.Equal(101325f, s.SeaLevelPressure, 0.05f);
            Assert.Equal(1e-5f, s.PrecipitationRate, 1e-9f);
            Assert.Equal(System.MathF.PI / 2f, s.W0From, 1e-5f);
            Assert.Equal(3f * System.MathF.PI / 2f, s.W1From, 1e-5f);
            Assert.Equal((5f, 8f), (s.W0Speed, s.W1Speed));   // m/s already
            Assert.Equal(w.RelativeHumidity, s.RelativeHumidity);   // a 0…1 fraction already

            var back = EntityWeatherEgressTranslator.FromSample(in s);
            Assert.Equal(15f, back.GroundTemperatureC, 1e-3f);
            Assert.Equal(6.5f, back.LapseRateKPerKm, 1e-4f);
            Assert.Equal(1013.25f, back.SeaLevelPressureHpa, 1e-3f);
            Assert.Equal(36f, back.PrecipitationMmPerHour, 1e-3f);
            Assert.Equal(90f, back.WindFromDeg[0], 1e-3f);
            Assert.Equal(270f, back.WindFromDeg[1], 1e-3f);
        }

        /// <summary>
        /// ⭐ <c>AQ107 I</c> — the scenario clock's display offset is SECONDS on the wire (an <c>int</c>; the ECS keeps whole minutes in a
        /// <c>short</c>): +180 min (UTC+3) is 10 800 s, −300 min is −18 000 s, +14 h (the largest zone, 840 min) is 50 400 s — which a
        /// <c>short</c> could not carry — and every one comes back as the same minutes. ✅ Red-proof: the egress copying minutes ⇒ 180 ⇒ red.
        /// </summary>
        [Theory]
        [InlineData((short)180, 10_800)]
        [InlineData((short)-300, -18_000)]
        [InlineData((short)840, 50_400)]
        [InlineData((short)0, 0)]
        public void AQ107I_ScenarioClockOffset_IsSecondsOnTheWire_AndMinutesInTheEcs(short minutes, int expectedSeconds)
        {
            var clock = new Fdp.Toolkit.Weather.ScenarioClock { StartUtcTicks = 637_000_000_000_000_000L, DisplayUtcOffsetMinutes = minutes };
            var sample = EntityScenarioClockEgressTranslator.ToSample(61, clock);
            Assert.Equal(expectedSeconds, sample.DisplayUtcOffsetSeconds);
            Assert.Equal(clock.StartUtcTicks, sample.StartUtcTicks);

            var back = EntityScenarioClockIngressTranslator.FromSample(in sample);
            Assert.Equal((clock.StartUtcTicks, minutes), (back.StartUtcTicks, back.DisplayUtcOffsetMinutes));
        }

        /// <summary>
        /// ⭐ <c>CE-4659</c> (thermal drivers TD-5, §3d) — a mount's LAST BURST crosses the wire field for field (rounds, start, last shot, heat at it, cooling),
        /// so another node computes the same barrel heat at any later time; the retired sampled heat goes as 0. (Was ⭐ AQ107 I: the sampled heat as a 0…1
        /// fraction — retired with the sample.) 🔴 Red-proof: drop the <c>LastShotAt2</c> copy in the ingress ⇒ mount 2 reads cold ⇒ red.
        /// </summary>
        [Fact]
        public void CE4659_ALastBurst_CrossesTheWire_AndTheHeatIsTheSameEverywhere()
        {
            var ammo = Fdp.Toolkit.Combat.Components.WeaponAmmo.Empty();
            ammo.Count = 4; ammo.Hot = 0b10;
            ammo.SetBurst(0, 12, 100.0, 101.5, 0.6f, 0.05f);
            ammo.SetBurst(2, 3, 90.0, 90.4, 0.9f, 0.1f);
            var sample = EntityWeaponAmmoEgressTranslator.ToSample(62, ammo);
            Assert.Equal((0f, 0f, 0f, 0f), (sample.Heat0, sample.Heat1, sample.Heat2, sample.Heat3));   // the retired sample
            Assert.Equal((byte)0b10, sample.Hot);

            var (node, unit, map) = Node(62);
            using (node)
            {
                node.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>();
                var ingress = new EntityWeaponAmmoIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponAmmo>(unit);
                Assert.True(got.Equals(ammo));
                foreach (double now in new[] { 91.0, 101.5, 105.0, 120.0 })
                    for (int m = 0; m < 4; m++) Assert.Equal(ammo.HeatAt(m, now), got.HeatAt(m, now));
                Assert.Equal(0.6f - 0.05f * 3.5f, got.HeatAt(0, 105.0), 4);
                Assert.True(double.IsNegativeInfinity(got.LastShotAtOf(1)));   // never fired
            }
        }

        /// <summary>⭐ <c>CE-3219</c> part 3 (§7.3b) — a cloud's every field reaches the other node (its kind, size, sight, irritation and
        /// the born/seconds a renderer fades on), and a thinner cloud is a new sample.</summary>
        [Fact]
        public void CE3219_ACloud_ReachesTheOtherNode_WithEveryField()
        {
            var cloud = new Fdp.Toolkit.Combat.AreaEffectCloud
            {
                Kind = Fdp.Toolkit.Combat.CloudKind.TearGas, Radius = 6f, Height = 3f, SightTransmittance = 0.4f, IrritantSeconds = 2f,
                BornAtSimSeconds = (SimInstant)12.5, Seconds = 60f, SourceNetworkId = 4711,   // ⭐ CE-3236 — the canister it sits on
                CreatorNetworkId = 4712, OriginalCreatorNetworkId = 4713,   // ⭐ CE-4155 (SR-82) — who threw it, and who threw it first
            };
            var sample = EntityAreaEffectCloudEgressTranslator.ToSample(46, cloud);

            var (node, unit, map) = Node(46);
            using (node)
            {
                var ingress = new EntityAreaEffectCloudIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(cloud, node.GetComponentRO<Fdp.Toolkit.Combat.AreaEffectCloud>(unit));
            }

            Assert.NotEqual(sample, EntityAreaEffectCloudEgressTranslator.ToSample(46, cloud with { SightTransmittance = 0.6f }));
        }

        /// <summary>
        /// ⭐ <c>CE-4662</c> (thermal drivers TD-12) — an IR-blocking smoke cloud SAYS SO on every node: <c>BlocksInfrared</c> rides
        /// <c>dtAreaEffectCloud</c> both ways, and flipping it is a new sample. ✅ Red-proof: the egress not copying it ⇒ 0 on the other node ⇒ red.
        /// </summary>
        [Fact]
        public void CE4662_AnInfraredBlockingSmokeCloud_SaysSoOnEveryNode()
        {
            var cloud = new Fdp.Toolkit.Combat.AreaEffectCloud
            {
                Kind = Fdp.Toolkit.Combat.CloudKind.Smoke, Radius = 20f, Height = 8f, SightTransmittance = 0.1f,
                BornAtSimSeconds = (SimInstant)5.0, Seconds = 90f, BlocksInfrared = 1,
            };
            var sample = EntityAreaEffectCloudEgressTranslator.ToSample(47, cloud);
            Assert.Equal((byte)1, sample.BlocksInfrared);

            var (node, unit, map) = Node(47);
            using (node)
            {
                var ingress = new EntityAreaEffectCloudIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal((byte)1, node.GetComponentRO<Fdp.Toolkit.Combat.AreaEffectCloud>(unit).BlocksInfrared);
            }
            Assert.NotEqual(sample, EntityAreaEffectCloudEgressTranslator.ToSample(47, cloud with { BlocksInfrared = 0 }));
        }

        /// <summary>
        /// ⭐ <c>CE-4662</c> (thermal drivers TD-8) — a LATE JOINER reads how long it has rained: the owner stamps <c>Since</c> when the rain
        /// starts (t = 100 s), a re-sent identical weather keeps it, and the sample a late joiner gets (TransientLocal) carries it ⇒ at t = 400 s it
        /// has rained 300 s. ✅ Red-proof: the ingress dropping <c>Since</c> ⇒ 0 ⇒ "400 s" ⇒ red.
        /// </summary>
        [Fact]
        public void CE4662_ALateJoiner_ReadsHowLongItHasRained()
        {
            var dry = Fdp.Toolkit.Weather.WeatherConditions.Standard;
            var rain = dry;
            rain.Precipitation = 1; rain.PrecipitationMmPerHour = 4f;
            rain.Restamp(dry, (SimInstant)100.0);                 // the owner: the rain began at 100 s
            var again = rain;
            again.Restamp(rain, (SimInstant)250.0);               // the same weather re-applied: it did not begin again
            Assert.Equal(100.0, (double)again.Since);

            var joined = EntityWeatherEgressTranslator.FromSample(EntityWeatherEgressTranslator.ToSample(61, again));
            Assert.Equal(300.0, 400.0 - joined.Since, 1e-9);
        }

        [Fact]
        public void CE3210_AnUnknownBody_IsDropped()
        {
            var (node, _, map) = Node(43);
            using (node)
            {
                var ingress = new EntityHitMarksIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                var sample = EntityHitMarksEgressTranslator.ToSample(999, new HitMarks());
                ingress.Apply(in sample, cmd, node);
                Assert.Equal(1, ingress.DroppedSampleCount);
            }
        }
    
        /// <summary>⭐ <c>CE-3252</c> (LD-E) — where a person looks reaches the other node (target, source, head turn); the owner sends
        /// again only on news (≥ 1° of head, ≥ 0.25 m of target). ✅ Red-proof: drop HeadYaw from the wire ⇒ the far node's head is 0.</summary>
        [Fact]
        public void CE3252_LookState_TheMusclesLook_ReachesTheOtherNode_OnNewsOnly()
        {
            var look = new Fdp.Toolkit.Perception.Look.LookState
            {
                Target = new Vector3(4f, 5f, 1.5f), HasTarget = 1, Source = Fdp.Toolkit.Perception.Look.LookSource.LookGlance, HeadYaw = (Fdp.Core.Angle)(0.6f),
                GlanceSince = (SimInstant)12.0, RestUntil = (SimInstant)3.0,   // the muscle's own timers — not on the wire
            };
            var sample = Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator.ToSample(46, look);

            var (node, unit, map) = Node(46);
            using (node)
            {
                var ingress = new Hrot.Map.Common.Replication.Ingress.EntityLookStateIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(in sample, cmd, node);
                cmd.Playback(node);
                var got = node.GetComponentRO<Fdp.Toolkit.Perception.Look.LookState>(unit);
                Assert.Equal((look.Target, look.HasTarget, look.Source, look.HeadYaw), (got.Target, got.HasTarget, got.Source, got.HeadYaw));
                Assert.Equal(0.0, got.GlanceSince);
            }

            var tiny = sample; tiny.HeadYaw += 0.01f;            // < 1°
            Assert.True(Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator.Same(sample, tiny));
            var turned = sample; turned.HeadYaw += 0.05f;        // ≈ 3°
            Assert.False(Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator.Same(sample, turned));
            var released = sample; released.HasTarget = 0;
            Assert.False(Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator.Same(sample, released));
        }
    }
}
