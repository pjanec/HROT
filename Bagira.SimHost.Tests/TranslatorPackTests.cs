using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bagira.SimHost.Network;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the static translator-pack factories introduced by MOD1-P3T1.
    /// Verifies that each pack yields the correct number and types of translators.
    /// </summary>
    public class TranslatorPackTests
    {
        // ── Minimal geo-transform stub ────────────────────────────────────────

        private sealed class IdentityGeoTransform : IGeographicTransform
        {
            public void SetOrigin(double lat, double lon, double alt) { }

            public Vector3 ToCartesian(double lat, double lon, double alt)
                => new Vector3((float)lon, (float)lat, (float)alt);

            public (double lat, double lon, double alt) ToGeodetic(Vector3 pos)
                => (pos.Y, pos.X, pos.Z);
        }

        // ── KinematicTranslatorPack ───────────────────────────────────────────

        [Fact]
        public void KinematicTranslatorPack_Create_ReturnsThreeTranslators()
        {
            const uint domainId = 210u;
            using var participant = new DdsParticipant(domainId);
            var entityMap  = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = KinematicTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Equal(3, translators.Count);
        }

        [Fact]
        public void KinematicTranslatorPack_Create_ContainsNavigationStatusEgressTranslator()
        {
            const uint domainId = 210u;
            using var participant = new DdsParticipant(domainId);
            var entityMap  = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = KinematicTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Contains(translators, t => t is NavigationStatusEgressTranslator);
        }

        [Fact]
        public void KinematicTranslatorPack_Create_ContainsNavigationIntentIngressTranslator()
        {
            const uint domainId = 210u;
            using var participant = new DdsParticipant(domainId);
            var entityMap  = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = KinematicTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Contains(translators, t => t is NavigationIntentIngressTranslator);
        }

        // ── CognitiveTranslatorPack ───────────────────────────────────────────

        [Fact]
        public void CognitiveTranslatorPack_Create_ReturnsFourTranslators()
        {
            const uint domainId = 211u;
            using var participant = new DdsParticipant(domainId);
            var entityMap       = new NetworkEntityMap();
            var geoTransform    = new IdentityGeoTransform();
            var ghostCreation   = new GhostCreationSystem(entityMap);

            var translators = CognitiveTranslatorPack.Create(
                participant, entityMap, geoTransform,
                doctrineRegistry: null,
                ghostCreationSystem: ghostCreation).ToList();

            Assert.Equal(4, translators.Count);
        }

        [Fact]
        public void CognitiveTranslatorPack_Create_ContainsNavigationIntentEgressTranslator()
        {
            const uint domainId = 211u;
            using var participant = new DdsParticipant(domainId);
            var entityMap     = new NetworkEntityMap();
            var geoTransform  = new IdentityGeoTransform();
            var ghostCreation = new GhostCreationSystem(entityMap);

            var translators = CognitiveTranslatorPack.Create(
                participant, entityMap, geoTransform,
                doctrineRegistry: null,
                ghostCreationSystem: ghostCreation).ToList();

            Assert.Contains(translators, t => t is NavigationIntentEgressTranslator);
        }

        [Fact]
        public void CognitiveTranslatorPack_Create_ContainsNavigationStatusIngressTranslator()
        {
            const uint domainId = 211u;
            using var participant = new DdsParticipant(domainId);
            var entityMap     = new NetworkEntityMap();
            var geoTransform  = new IdentityGeoTransform();
            var ghostCreation = new GhostCreationSystem(entityMap);

            var translators = CognitiveTranslatorPack.Create(
                participant, entityMap, geoTransform,
                doctrineRegistry: null,
                ghostCreationSystem: ghostCreation).ToList();

            Assert.Contains(translators, t => t is NavigationStatusIngressTranslator);
        }
    }
}
