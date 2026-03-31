using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;
using CycloneDDS.Runtime;

namespace Hrot.ClusterRunner.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WaitingRoomCoordinator"/>.
    /// Uses a 1 000 ms timeout constant to keep test runs short.
    /// </summary>
    [Collection("DDS-WaitingRoom")]
    public class WaitingRoomCoordinatorTests
    {
        // Short timeout for all coordinator tests so they fail fast
        private const int TestTimeoutMs = 1_000;

        // DDS domain used by all waiting room tests (isolated from domain 0)
        private const uint TestDomain = 11;

        // ── Peer discovery ────────────────────────────────────────────────────

        [Fact]
        public void WaitForPeers_SinglePeerAnnounces_ReturnsSuccessfully()
        {
            // Coordinator: node 10, waiting for "ig"
            using var participantA = new DdsParticipant(TestDomain);
            using var coordinator  = new WaitingRoomCoordinator(
                participantA, localNodeId: 10, subsystemName: "simhost",
                requiredPeers: new HashSet<string> { "ig" },
                timeoutMs: TestTimeoutMs * 5); // Give enough time for this test

            // Peer "ig": node 20, separate participant
            using var participantB = new DdsParticipant(TestDomain);
            using var peerWriter   = new DdsWriter<SubsystemStatusAnnounce>(
                participantB, "SubsystemStatusAnnounce");

            // Give discovery time, then peer announces
            Thread.Sleep(200);
            peerWriter.Write(new SubsystemStatusAnnounce
            {
                NodeId        = 20,
                SubsystemName = "IG",
                DomainId      = (int)TestDomain,
                Ready         = false,
                Timestamp     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Should return without exception
            coordinator.WaitForPeers();
        }

        [Fact]
        public void WaitForPeers_AllPeersPresent_ReturnsSuccessfully()
        {
            using var participantA = new DdsParticipant(TestDomain);
            using var coordinator  = new WaitingRoomCoordinator(
                participantA, localNodeId: 100, subsystemName: "simhost",
                requiredPeers: new HashSet<string> { "ig", "ios" },
                timeoutMs: TestTimeoutMs * 5);

            using var participantB  = new DdsParticipant(TestDomain);
            using var writerIg      = new DdsWriter<SubsystemStatusAnnounce>(participantB, "SubsystemStatusAnnounce");
            using var participantC  = new DdsParticipant(TestDomain);
            using var writerIos     = new DdsWriter<SubsystemStatusAnnounce>(participantC, "SubsystemStatusAnnounce");

            Thread.Sleep(200);

            writerIg.Write(new SubsystemStatusAnnounce { NodeId = 200, SubsystemName = "IG",  DomainId = (int)TestDomain, Ready = false, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            writerIos.Write(new SubsystemStatusAnnounce { NodeId = 300, SubsystemName = "ExCon", DomainId = (int)TestDomain, Ready = false, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

            coordinator.WaitForPeers();
        }

        // ── Timeout ───────────────────────────────────────────────────────────

        [Fact]
        public void WaitForPeers_Timeout_ThrowsTimeoutException()
        {
            using var participant = new DdsParticipant(TestDomain);
            using var coordinator = new WaitingRoomCoordinator(
                participant, localNodeId: 400, subsystemName: "simhost",
                requiredPeers: new HashSet<string> { "ig", "ios" },
                timeoutMs: TestTimeoutMs);

            var ex = Assert.Throws<TimeoutException>(() => coordinator.WaitForPeers());
            Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void WaitForPeers_Timeout_MessageContainsExpectedPeers()
        {
            using var participant = new DdsParticipant(TestDomain);
            using var coordinator = new WaitingRoomCoordinator(
                participant, localNodeId: 500, subsystemName: "ios",
                requiredPeers: new HashSet<string> { "simhost" },
                timeoutMs: TestTimeoutMs);

            var ex = Assert.Throws<TimeoutException>(() => coordinator.WaitForPeers());
            Assert.Contains("simhost", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── Self-ignore ───────────────────────────────────────────────────────

        [Fact]
        public void WaitForPeers_SelfAnnouncement_IsIgnored()
        {
            // Coordinator is nodeId=600, waiting for "simhost".
            // A writer using the same participant also announces as "simhost" but
            // with nodeId=600 (same as coordinator) → should be ignored → timeout.
            using var participant = new DdsParticipant(TestDomain);
            using var coordinator = new WaitingRoomCoordinator(
                participant, localNodeId: 600, subsystemName: "ig",
                requiredPeers: new HashSet<string> { "simhost" },
                timeoutMs: TestTimeoutMs);

            // Write from same nodeId=600 as "simhost"
            using var selfWriter = new DdsWriter<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
            Thread.Sleep(100);
            selfWriter.Write(new SubsystemStatusAnnounce
            {
                NodeId        = 600, // Same as coordinator → must be ignored
                SubsystemName = "simhost",
                DomainId      = (int)TestDomain,
                Ready         = false,
                Timestamp     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Because self-announcements are ignored, no valid "simhost" peer is found → timeout
            Assert.Throws<TimeoutException>(() => coordinator.WaitForPeers());
        }

        // ── TransientLocal late joiner ────────────────────────────────────────

        [Fact]
        public void WaitForPeers_TransientLocal_LateJoinerSeesEarlierAnnouncement()
        {
            // Peer announces BEFORE coordinator creates its reader
            using var participantPeer = new DdsParticipant(TestDomain);
            using var earlyWriter     = new DdsWriter<SubsystemStatusAnnounce>(participantPeer, "SubsystemStatusAnnounce");

            earlyWriter.Write(new SubsystemStatusAnnounce
            {
                NodeId        = 700,
                SubsystemName = "IG",
                DomainId      = (int)TestDomain,
                Ready         = false,
                Timestamp     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Small delay to ensure the message is in the DDS cache
            Thread.Sleep(300);

            // Coordinator subscribes AFTER peer wrote
            using var participantLate = new DdsParticipant(TestDomain);
            using var coordinator     = new WaitingRoomCoordinator(
                participantLate, localNodeId: 800, subsystemName: "simhost",
                requiredPeers: new HashSet<string> { "ig" },
                timeoutMs: TestTimeoutMs * 5);

            // TransientLocal QoS delivers cached announcement to late-joining reader
            coordinator.WaitForPeers();
        }

        // ── Empty required peers ──────────────────────────────────────────────

        [Fact]
        public void WaitForPeers_EmptyRequiredPeers_ReturnsImmediately()
        {
            using var participant = new DdsParticipant(TestDomain);
            using var coordinator = new WaitingRoomCoordinator(
                participant, localNodeId: 900, subsystemName: "all",
                requiredPeers: new HashSet<string>(), // nothing to wait for
                timeoutMs: TestTimeoutMs);

            // Should complete immediately without timeout
            coordinator.WaitForPeers();
        }
    }
}
