using System;
using System.Threading.Tasks;
using CycloneDDS.Runtime;
using Hrot.DDS.DataModel.Runner;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    /// <summary>
    /// DDS pub/sub integration tests for the <see cref="SubsystemStatusAnnounce"/> topic.
    /// Each test uses a dedicated DDS domain to prevent cross-test interference.
    /// </summary>
    public class SubsystemStatusAnnounceTests
    {
        // Dedicated domains per test to prevent concurrent-test interference
        private const uint DomainRoundTrip  = 120;
        private const uint DomainLateJoiner = 121;

        // ── Pub/sub round-trip ────────────────────────────────────────────────

        [Fact]
        public async Task SubsystemStatusAnnounce_PubSub_RoundTrip()
        {
            using var participant = new DdsParticipant(DomainRoundTrip);
            using var writer      = new DdsWriter<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
            using var reader      = new DdsReader<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");

            // Allow discovery
            await Task.Delay(500);

            const int expectedNodeId = 100;
            var sample = new SubsystemStatusAnnounce
            {
                NodeId        = expectedNodeId,
                SubsystemName = "SimHost",
                DomainId      = (int)DomainRoundTrip,
                Ready         = true,
                Timestamp     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            writer.Write(sample);
            await Task.Delay(500);

            var loan = reader.Take();
            try
            {
                // Find our specific sample by NodeId (there may be cache entries from other sources)
                bool found = false;
                for (int i = 0; i < loan.Count; i++)
                {
                    if (loan[i].NodeId == expectedNodeId)
                    {
                        Assert.Equal("SimHost", loan[i].SubsystemName);
                        Assert.True(loan[i].Ready);
                        found = true;
                        break;
                    }
                }
                Assert.True(found, $"Should have received sample with NodeId={expectedNodeId}");
            }
            finally
            {
                loan.Dispose();
            }
        }

        // ── TransientLocal durability: late joiner ────────────────────────────

        [Fact]
        public async Task SubsystemStatusAnnounce_TransientLocal_LateJoinerReceivesAnnouncement()
        {
            // A writer publishes BEFORE the reader is created
            using var writerParticipant = new DdsParticipant(DomainLateJoiner);
            using var earlyWriter       = new DdsWriter<SubsystemStatusAnnounce>(writerParticipant, "SubsystemStatusAnnounce");

            const int earlyNodeId = 200;
            earlyWriter.Write(new SubsystemStatusAnnounce
            {
                NodeId        = earlyNodeId,
                SubsystemName = "IG",
                DomainId      = (int)DomainLateJoiner,
                Ready         = false,
                Timestamp     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Allow message to propagate to DDS cache
            await Task.Delay(500);

            // Late-joining reader subscribes AFTER write
            using var readerParticipant = new DdsParticipant(DomainLateJoiner);
            using var lateReader        = new DdsReader<SubsystemStatusAnnounce>(readerParticipant, "SubsystemStatusAnnounce");

            // TransientLocal: late reader should receive the cached sample
            await Task.Delay(500);

            var loan = lateReader.Take();
            try
            {
                bool found = false;
                for (int i = 0; i < loan.Count; i++)
                {
                    if (loan[i].NodeId == earlyNodeId)
                    {
                        Assert.Equal("IG", loan[i].SubsystemName);
                        found = true;
                        break;
                    }
                }
                Assert.True(found,
                    $"Late-joining reader should receive the TransientLocal cached announcement with NodeId={earlyNodeId}");
            }
            finally
            {
                loan.Dispose();
            }
        }
    }
}

