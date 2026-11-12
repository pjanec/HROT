using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using MessagePack;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ CE-4445 M-4 — a recording made with non-stamping managed reads (the default) replays to EXACTLY the live world, frame by frame, as one
    /// made with the old stamping reads (<c>HROT_MANAGED_READ_STAMP=1</c>) does. A deterministic world: 64 entities, a managed RECORD read every
    /// frame through every read path (GetComponent / GetComponentRO / TryGetComponent) and replaced whole every 7th frame for a third of them,
    /// plus an unmanaged component written every frame. Every frame is recorded (keyframe, then deltas) and replayed into a fresh world, which must
    /// match the live world after each frame.
    /// <para>Cross-mode: with <c>HROT_REPLAY_EQUIV_OUT=&lt;dir&gt;</c> it writes <c>replay-&lt;mode&gt;.txt</c> — the hash of every frame's replayed
    /// state and the recorded delta bytes — so a run in each mode can be compared: the hashes must be equal; the bytes show what the stamps cost.</para>
    /// 🔴 Red-proof: drop the replace-whole <c>SetManagedComponent</c> and mutate the record's list in place instead ⇒ with non-stamping reads the
    /// replay misses the change and the per-frame comparison fails.
    /// </summary>
    public class ManagedReadsReplayEquivalenceTests
    {
        [MessagePackObject]
        [ComponentId(476)]
        public sealed record EqSquad
        {
            [Key(0)] public string? Name { get; init; }
            [Key(1)] public int Count { get; init; }
        }

        [ComponentId(477)] public struct EqPos { public float X; }

        private const int N = 64, Frames = 120;

        [Fact]
        public void A_recording_replays_to_the_live_world_every_frame_and_the_reads_do_not_change_it()
        {
            using var live = new EntityRepository();
            live.RegisterComponent<EqSquad>();
            live.RegisterComponent<EqPos>();
            var ents = new Entity[N];
            for (int i = 0; i < N; i++)
            {
                ents[i] = live.CreateEntity();
                live.AddManagedComponent(ents[i], new EqSquad { Name = "s" + i, Count = 0 });
                live.AddComponent(ents[i], new EqPos { X = i });
            }

            var recorder = new RecorderSystem();
            var ms = new MemoryStream(1 << 20);
            var w = new BinaryWriter(ms);
            byte[] Take() { w.Flush(); var b = ms.ToArray(); ms.SetLength(0); ms.Position = 0; return b; }

            var frames = new List<byte[]>();
            var expected = new List<string>();
            long deltaBytes = 0;

            live.Tick();
            recorder.RecordKeyframe(live, w, 0L);
            frames.Add(Take());
            expected.Add(State(live, ents));

            for (int f = 1; f < Frames; f++)
            {
                uint prev = live.GlobalVersion;
                live.Tick();
                long sum = 0;
                for (int i = 0; i < N; i++)
                {
                    // ⭐ every read path, every frame — none may change what is recorded
                    var a = live.GetComponent<EqSquad>(ents[i]);
                    var b = live.GetComponentRO<EqSquad>(ents[i]);
                    live.TryGetComponent<EqSquad>(ents[i], out var c);
                    sum += a.Count + b.Count + (c?.Count ?? 0);
                    if (i % 3 == 0 && f % 7 == 0)
                        live.SetManagedComponent(ents[i], a with { Count = a.Count + 1, Name = "s" + i + "@" + f });   // replaced whole
                    live.GetComponentRW<EqPos>(ents[i]).X = f * 1000 + i;
                }
                Assert.True(sum >= 0);
                recorder.RecordDeltaFrame(live, prev, w, 0L);
                var bytes = Take();
                deltaBytes += bytes.Length;
                frames.Add(bytes);
                expected.Add(State(live, ents));
            }

            using var replay = new EntityRepository();
            replay.RegisterComponent<EqSquad>();
            replay.RegisterComponent<EqPos>();
            var playback = new PlaybackSystem();
            var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int f = 0; f < frames.Count; f++)
            {
                using (var r = new BinaryReader(new MemoryStream(frames[f]))) playback.ApplyFrame(replay, r);
                string got = State(replay, ents);
                Assert.Equal(expected[f], got);
                hash.AppendData(Encoding.UTF8.GetBytes(got));
            }

            if (Environment.GetEnvironmentVariable("HROT_REPLAY_EQUIV_OUT") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                string mode = ThreadedReads.PureReads ? "pure" : "stamping";
                File.WriteAllText(Path.Combine(dir, $"replay-{mode}.txt"),
                    $"mode {mode}\nstate-hash {Convert.ToHexString(hash.GetHashAndReset())}\ndelta-bytes {deltaBytes}\nframes {frames.Count}\n");
            }
        }

        private static string State(EntityRepository repo, Entity[] ents)
        {
            var sb = new StringBuilder();
            foreach (var e in ents)
            {
                var s = repo.GetComponentRO<EqSquad>(e);
                sb.Append(s.Name).Append(':').Append(s.Count).Append(':').Append(repo.GetComponentRO<EqPos>(e).X).Append(';');
            }
            return sb.ToString();
        }
    }
}
