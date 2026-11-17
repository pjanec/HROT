using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common.Replication.Egress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-10000</c> (air lane, the contract — docs/blueprints/batches/NOTE_Air_To_Ui_Air_Combat_Contract.md) — the ids ui and every lane key on
    /// are the ones the note states, mode 8 crosses the wire both ways (RV-6: an unknown mode used to become NAV_NONE and the fighter flew straight),
    /// and the air-combat samples carry what the brain and the looks read.
    /// </summary>
    public sealed class AirCombatContractTests
    {
        [Fact]
        public void TheIds_AreTheContract()
        {
            Assert.Equal(277, GlobalComponentIds.AirCombatSteer);
            Assert.Equal(278, GlobalComponentIds.FighterPilotState);
            Assert.Equal(279, GlobalComponentIds.AirCombatState);
            Assert.Equal(128, GlobalComponentIds.MissileDefenceState);
            Assert.Equal(160, GlobalComponentIds.AirArena);
            Assert.Equal(216L, (long)EDescriptorType.dtAirCombatSteer);
            Assert.Equal(217L, (long)EDescriptorType.dtFighterPilotState);
            Assert.Equal(218L, (long)EDescriptorType.dtAirArena);
            Assert.Equal(8, (int)NavigationMode.AirCombat);
            Assert.Equal(8, (int)ENavigationMode.NAV_AIR_COMBAT);
            Assert.Equal(409L, Hrot.Map.Common.TkbEntityTypes.Jet_MiG29);
            Assert.Equal(13, (int)WeaponCategory.AirToAir);
            Assert.Equal(new[] { 6060, 6061, 6062, 6063, 6064, 6065 }, new[]
            {
                Hrot.Core.Tkb.MunitionTkbCatalog.TkbAim9x, Hrot.Core.Tkb.MunitionTkbCatalog.TkbAim120, Hrot.Core.Tkb.MunitionTkbCatalog.TkbR73,
                Hrot.Core.Tkb.MunitionTkbCatalog.TkbR77, Hrot.Core.Tkb.MunitionTkbCatalog.TkbM61Hei, Hrot.Core.Tkb.MunitionTkbCatalog.TkbGsh30Hei,
            });
        }

        [Fact]
        public void ModeEight_RoundTripsTheWire()
        {
            Assert.Equal(ENavigationMode.NAV_AIR_COMBAT, Hrot.Map.Common.Replication.Egress.NavigationIntentEgressTranslator.MapMode(NavigationMode.AirCombat));
            Assert.Equal(NavigationMode.AirCombat, Hrot.Map.Common.Replication.Ingress.NavigationIntentIngressTranslator.MapMode(ENavigationMode.NAV_AIR_COMBAT));
        }

        [Fact]
        public void ThePilotReport_IsRounded_SoASteadyTurnSendsFewSamples()
        {
            var a = new FighterPilotState { GLoad = 6.42f, Throttle = 0.97f, Inbound = 2, ThreatTtiS = 3.04f, SpeedMps = 220.4f, ThreatSeeker = GuidanceKind.InfraRed };
            var b = a; b.GLoad = 6.38f; b.SpeedMps = 220.2f;
            var sa = EntityFighterPilotStateEgressTranslator.ToSample(7, in a);
            var sb = EntityFighterPilotStateEgressTranslator.ToSample(7, in b);
            Assert.Equal(sa, sb);   // a jitter under the rounding sends nothing
            Assert.Equal(6.4f, sa.GLoad, 3);
            Assert.Equal(2, sa.Inbound);
            Assert.Equal((byte)GuidanceKind.InfraRed, sa.ThreatSeeker);
        }

        [Fact]
        public void TheMissiles_CarryAMotor_AndTheGuns_ARound()
        {
            var tkb = new Fdp.Toolkit.Tkb.TkbDatabase();
            Hrot.Core.Tkb.MunitionTkbCatalog.RegisterAll(tkb);
            foreach (var id in new long[] { 6060, 6061, 6062, 6063 })
            {
                Assert.True(tkb.TryGetByType(id, out var t));
                var m = t!.GetDescriptor<MissileDto>();
                Assert.NotNull(m);
                Assert.Equal(TargetDomain.Air, m!.TargetDomain);
                Assert.True(m.BurnSeconds > 0f && m.MaxG > 0f && m.MinSpeedMps > 0f && m.SteerLagS > 0f);
                Assert.NotNull(t.GetDescriptor<MunitionVisualDto>());   // a placeholder look until ui's
            }
            foreach (var id in new long[] { 6064, 6065 })
            {
                Assert.True(tkb.TryGetByType(id, out var t));
                Assert.NotNull(t!.GetDescriptor<MunitionVisualDto>());
            }
        }
    }
}
