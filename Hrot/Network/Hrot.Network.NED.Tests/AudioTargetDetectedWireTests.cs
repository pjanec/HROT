using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CycloneDDS.Runtime;
using Hrot.NED.Descriptors;
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
    }
}
