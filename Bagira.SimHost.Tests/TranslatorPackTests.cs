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
    [Collection("SimHostDds")]
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

        // ── Phase 6 translator packs (MOD1-P6T8) ─────────────────────────────

        [Fact]
        public void BrainPerceptionTranslatorPack_Create_ReturnsFourTranslators()
        {
            const uint domainId = 220u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = BrainPerceptionTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Equal(4, translators.Count);
        }

        [Fact]
        public void BrainPerceptionTranslatorPack_Create_ContainsSensorConfigEgressTranslator()
        {
            const uint domainId = 220u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = BrainPerceptionTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Contains(translators, t => t is SensorConfigEgressTranslator);
        }

        [Fact]
        public void SimPerceptionTranslatorPack_Create_ReturnsFourTranslators()
        {
            const uint domainId = 221u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = SimPerceptionTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Equal(4, translators.Count);
        }

        [Fact]
        public void SimPerceptionTranslatorPack_Create_ContainsSensorConfigIngressTranslator()
        {
            const uint domainId = 221u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = SimPerceptionTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Contains(translators, t => t is SensorConfigIngressTranslator);
        }

        [Fact]
        public void BrainPathfindingTranslatorPack_Create_ReturnsTwoTranslators()
        {
            const uint domainId = 222u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = BrainPathfindingTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Equal(2, translators.Count);
        }

        [Fact]
        public void BrainPathfindingTranslatorPack_Create_ContainsPathRequestBrainEgressTranslator()
        {
            const uint domainId = 222u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = BrainPathfindingTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Contains(translators, t => t is PathRequestBrainEgressTranslator);
        }

        [Fact]
        public void SimPathfindingTranslatorPack_Create_ReturnsTwoTranslators()
        {
            const uint domainId = 223u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = SimPathfindingTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Equal(2, translators.Count);
        }

        [Fact]
        public void SimPathfindingTranslatorPack_Create_ContainsPathRequestSolverIngressTranslator()
        {
            const uint domainId = 223u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            var translators = SimPathfindingTranslatorPack.Create(participant, entityMap, geoTransform).ToList();

            Assert.Contains(translators, t => t is PathRequestSolverIngressTranslator);
        }

        // ── NodeBootstrapper role → translator correlation (MOD1-P6T8) ────────────

        [Fact]
        public void NodeBootstrapper_AllInOne_RegistersAllTranslatorPacks()
        {
            const uint domainId = 224u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();
            var eventBus     = new FdpEventBus();
            var ghostSystem  = new GhostCreationSystem(entityMap);

            var bootstrapper = new NodeBootstrapper();
            var translators  = bootstrapper.BuildTranslators(
                NodeRole.AllInOne, participant, entityMap, geoTransform,
                eventBus, ghostSystem, doctrineRegistry: null, localNodeId: 1);

            // Brain perception pack
            Assert.Contains(translators, t => t is SensorConfigEgressTranslator);
            // Sim perception pack
            Assert.Contains(translators, t => t is SensorConfigIngressTranslator);
            // Brain pathfinding pack
            Assert.Contains(translators, t => t is PathRequestBrainEgressTranslator);
            // Sim pathfinding pack
            Assert.Contains(translators, t => t is PathRequestSolverIngressTranslator);
        }

        [Fact]
        public void NodeBootstrapper_Brain_DoesNotRegisterSimPerceptionPack()
        {
            const uint domainId = 225u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();
            var eventBus     = new FdpEventBus();
            var ghostSystem  = new GhostCreationSystem(entityMap);

            var bootstrapper = new NodeBootstrapper();
            var translators  = bootstrapper.BuildTranslators(
                NodeRole.Brain, participant, entityMap, geoTransform,
                eventBus, ghostSystem, doctrineRegistry: null, localNodeId: 1);

            // Brain should have its own perception and pathfinding packs
            Assert.Contains(translators, t => t is SensorConfigEgressTranslator);
            Assert.Contains(translators, t => t is PathRequestBrainEgressTranslator);
            // Brain must NOT contain the solver-side ingress translators
            Assert.DoesNotContain(translators, t => t is SensorConfigIngressTranslator);
            Assert.DoesNotContain(translators, t => t is PathRequestSolverIngressTranslator);
        }

        [Fact]
        public void NodeBootstrapper_Perception_RegistersSimPerceptionPack()
        {
            const uint domainId = 226u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();
            var eventBus     = new FdpEventBus();
            var ghostSystem  = new GhostCreationSystem(entityMap);

            var bootstrapper = new NodeBootstrapper();
            var translators  = bootstrapper.BuildTranslators(
                NodeRole.Perception, participant, entityMap, geoTransform,
                eventBus, ghostSystem, doctrineRegistry: null, localNodeId: 1);

            Assert.Contains(translators, t => t is SensorConfigIngressTranslator);
            // Perception must NOT publish brain-side sensor config
            Assert.DoesNotContain(translators, t => t is SensorConfigEgressTranslator);
        }

        [Fact]
        public void NodeBootstrapper_NavigationSolver_RegistersSimPathfindingPack()
        {
            const uint domainId = 227u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();
            var eventBus     = new FdpEventBus();
            var ghostSystem  = new GhostCreationSystem(entityMap);

            var bootstrapper = new NodeBootstrapper();
            var translators  = bootstrapper.BuildTranslators(
                NodeRole.NavigationSolver, participant, entityMap, geoTransform,
                eventBus, ghostSystem, doctrineRegistry: null, localNodeId: 1);

            Assert.Contains(translators, t => t is PathRequestSolverIngressTranslator);
            // NavigationSolver must NOT contain the brain-side path request egress translator
            Assert.DoesNotContain(translators, t => t is PathRequestBrainEgressTranslator);
        }
    }
}
