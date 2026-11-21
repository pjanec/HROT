using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2416</c> — the recorder computes every entity's liveness ONCE per recorded frame and slices it per chunk (it recomputed it for
    /// every changed chunk of every table — ~60 % of the recorder on the CE-2408 stress world). The recording must be BYTE-IDENTICAL to the
    /// per-chunk recompute: a keyframe and deltas over frames that create, change and destroy entities, with a minimum recordable id and an
    /// entity filter. 🔴 Red-proof: the slice taken one entity off ⇒ the bytes differ.
    /// </summary>
    public class RecorderLivenessOnceTests
    {
        // the recorder suite's own components (RecorderDeltaLogicTests: [ComponentId] 220 / 221)

        private static List<byte[]> Record(bool once)
        {
            bool was = RecorderSystem.LivenessOnce;
            RecorderSystem.LivenessOnce = once;
            try
            {
                using var repo = new EntityRepository();
                repo.RegisterComponent<RecorderDeltaLogicTests.Position>();
                repo.RegisterComponent<RecorderDeltaLogicTests.Velocity>();
                var recorder = new RecorderSystem { MinRecordableId = 10, EntityFilter = e => e.Index % 7 != 0 };
                var rng = new Random(2416);
                var live = new List<Entity>();
                for (int i = 0; i < 400; i++)
                {
                    var e = repo.CreateEntity();
                    repo.AddComponent(e, new RecorderDeltaLogicTests.Position { X = i, Y = i * 0.5f });
                    if (i % 3 == 0) repo.AddComponent(e, new RecorderDeltaLogicTests.Velocity { VX = i });
                    live.Add(e);
                }
                var frames = new List<byte[]>();
                uint prev = 0;
                for (int f = 0; f < 12; f++)
                {
                    repo.Tick();
                    for (int k = 0; k < 30; k++) repo.GetComponentRW<RecorderDeltaLogicTests.Position>(live[rng.Next(live.Count)]).X += 1;
                    for (int k = 0; k < 5 && live.Count > 0; k++) { int j = rng.Next(live.Count); repo.DestroyEntity(live[j]); live.RemoveAt(j); }
                    for (int k = 0; k < 4; k++) { var e = repo.CreateEntity(); repo.AddComponent(e, new RecorderDeltaLogicTests.Position { X = -k }); live.Add(e); }
                    using var ms = new MemoryStream();
                    using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                    {
                        if (f == 0) recorder.RecordKeyframe(repo, w, 0L);
                        else recorder.RecordDeltaFrame(repo, prev, w, 0L);
                        w.Flush();
                    }
                    prev = repo.GlobalVersion;
                    frames.Add(ms.ToArray());
                }
                return frames;
            }
            finally { RecorderSystem.LivenessOnce = was; }
        }

        [Fact]
        public void CE2416_LivenessOncePerFrame_RecordsTheSameBytes()
        {
            var once = Record(true);
            var perChunk = Record(false);
            Assert.Equal(perChunk.Count, once.Count);
            for (int f = 0; f < once.Count; f++)
                Assert.True(perChunk[f].AsSpan().SequenceEqual(once[f]), $"frame {f}: {once[f].Length} vs {perChunk[f].Length} bytes");
            Assert.True(once[0].Length > 1000, "the premise: a real keyframe");
        }
    }
}
