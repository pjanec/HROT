using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Hrot.Network.NED.SimHost;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    /// <summary>
    /// ⭐ CE-2106 — every sound one acoustic solve hears crosses the wire. docs/DESIGN_Thermal_And_Acoustic_Sensing.md §8.1.
    /// </summary>
    public class AudioTargetDetectedWireTests
    {
        private const int TestDomain = 213;

        /// <summary>
        /// ⛔ Before the fix the topic was KeepLast 1 with no key — ONE instance — so a solve that heard sounds for two
        /// listeners in the same frame delivered only the last sample written. Measured live (tt-heard-shot): the rifleman's
        /// shot never reached the Brain; only the decoy's did.
        /// </summary>
        [Fact(Timeout = 15_000)]
        public void SameFrameBurst_ForTwoListeners_AllReachTheReader_CE2106()
        {
            using var participant = new DdsParticipant(TestDomain);
            using var writer = new DdsWriter<AudioTargetDetected>(participant);
            using var reader = new DdsReader<AudioTargetDetected>(participant);
            Thread.Sleep(400); // DDS discovery

            var sent = new[]
            {
                (Listener: 1000L, Kind: (byte)2),   // the rifleman hears the shot …
                (Listener: 1000L, Kind: (byte)1),   // … and the shooter's footsteps
                (Listener: 1002L, Kind: (byte)2),   // the decoy hears the same shot, written last
            };
            foreach (var (listener, kind) in sent)
                writer.Write(new AudioTargetDetected { ListenerEntityId = listener, Kind = kind, Radius = 5f, SourceClass = 4 });

            Thread.Sleep(300);
            var seen = new HashSet<(long, byte)>();
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (seen.Count < sent.Length && DateTime.UtcNow < deadline)
            {
                using (var l = reader.Take())
                    foreach (var s in l)
                        if (s.IsValid) seen.Add((s.Data.ListenerEntityId, s.Data.Kind));
                Thread.Sleep(50);
            }

            Assert.True(sent.All(x => seen.Contains((x.Listener, x.Kind))), $"only {seen.Count} of {sent.Length} heard sounds arrived");
        }

        // ───────────────────────── ⭐ CE-3329 W5 — an ingress translator can DISCARD what is pending ─────────────────────────

        /// <summary>
        /// ⭐⭐ <c>CE-3329 W5</c> (DESIGN_Replay_Time_Axis_And_Review §12u) — at a replay's exit (and a join) the node calls
        /// <see cref="IDiscardableIngress.DiscardIngress"/>: every pending sample of the translator's reader is taken and DROPPED, so a
        /// stale replay-era sample never lands on the world. A single-reader translator (<c>AudioTargetDetected</c>, keyed per listener).
        /// 🔴 Red-proved by <c>DiscardIngress() =&gt; 0</c>: the three samples then reach <c>PollIngress</c> (<c>ReceivedSampleCount</c> 3).
        /// </summary>
        [Fact(Timeout = 30_000)]
        public void CE3329_W5_AudioIngress_DiscardDropsEveryPendingSample_ThenPollIngressSeesNothing()
        {
            using var participant = new DdsParticipant(TestDomain + 1);
            var translator = new AudioTargetDetectedIngressTranslator(participant, new NetworkEntityMap());
            using var probe  = new DdsReader<AudioTargetDetected>(participant, translator.TopicName);   // proves the samples have ARRIVED
            using var writer = new DdsWriter<AudioTargetDetected>(participant, translator.TopicName);
            Thread.Sleep(400); // DDS discovery

            for (long listener = 1; listener <= 3; listener++)
                writer.Write(new AudioTargetDetected { ListenerEntityId = listener, Kind = 2, Radius = 5f });
            WaitUntilArrived(probe, 3);

            Assert.Equal(3, ((IDiscardableIngress)translator).DiscardIngress());   // ⭐ it reports what it dropped
            translator.PollIngress(null!, null!);
            Assert.Equal(0, translator.ReceivedSampleCount);                        // ⭐ and none of it reaches the world

            writer.Write(new AudioTargetDetected { ListenerEntityId = 9, Kind = 2, Radius = 5f });   // what arrives AFTER is taken in as usual
            WaitUntilArrived(probe, 1);
            translator.PollIngress(null!, null!);
            Assert.Equal(1, translator.ReceivedSampleCount);
            Assert.Equal(0, ((IDiscardableIngress)translator).DiscardIngress());
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3329 W5</c> — the same drain for a second translator on another topic (<c>NearMiss</c>, no key: ONE instance that
        /// holds the burst as history) in a second domain: the discard is per translator and per reader, nothing is shared.
        /// </summary>
        [Fact(Timeout = 30_000)]
        public void CE3329_W5_NearMissIngress_DiscardDropsTheWholeBurst_ThenPollIngressSeesNothing()
        {
            using var participant = new DdsParticipant(TestDomain + 2);
            var translator = new NearMissIngressTranslator(participant, new NetworkEntityMap());
            using var probe  = new DdsReader<NearMiss>(participant, translator.TopicName);
            using var writer = new DdsWriter<NearMiss>(participant, translator.TopicName);
            Thread.Sleep(400); // DDS discovery

            for (int i = 0; i < 3; i++) writer.Write(new NearMiss { UnitEntityId = 7, X = i });
            WaitUntilArrived(probe, 3);

            Assert.Equal(3, ((IDiscardableIngress)translator).DiscardIngress());
            translator.PollIngress(null!, null!);
            Assert.Equal(0, translator.ReceivedSampleCount);
        }

        /// <summary>Waits until the PROBE reader (a second reader on the same topic — it does not consume the translator's samples) has
        /// taken <paramref name="count"/> more samples: the written samples have crossed DDS and reached every reader in the participant.</summary>
        private static void WaitUntilArrived<T>(DdsReader<T> probe, int count) where T : new()
        {
            int seen = 0;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                using (var l = probe.Take()) seen += l.Count;
                if (seen >= count) { Thread.Sleep(100); return; }
                Thread.Sleep(20);
            }
            throw new TimeoutException("DDS loopback: the written samples never arrived");
        }
    }
}
