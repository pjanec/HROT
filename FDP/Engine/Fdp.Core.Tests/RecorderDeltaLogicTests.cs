using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    public class RecorderDeltaLogicTests
    {
        // Define some Component structs for testing
        [ComponentId(220)]
        public struct Position
        {
            public float X, Y, Z;
        }

        [ComponentId(221)]
        public struct Velocity
        {
            public float VX, VY, VZ;
        }

        [Fact]
        public void DirtyScan_DetectsChangedChunks_ViaVersions()
        {
            // Scenario: 
            // 1. Fill Chunk 0 completely.
            // 2. Add one entity to Chunk 1.
            // 3. Update ONLY the entity in Chunk 1.
            // 4. Verify Delta only records Chunk 1 for that component.

            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            int capacity = FdpConfig.GetChunkCapacity<Position>();
            
            // Fill Chunk 0
            for (int i = 0; i < capacity; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i });
            }

            // Start Chunk 1
            var eChunk1 = repo.CreateEntity();
            repo.AddComponent(eChunk1, new Position { X = 999 });

            repo.Tick(); // Advance tick. All chunks are "dirty" relative to 0.
            uint baselineTick = (uint)repo.GlobalVersion; // Tick = 1

            // Modify Chunk 1 only
            repo.Tick(); // Tick = 2
            ref var pos = ref repo.GetComponentRW<Position>(eChunk1);
            pos.X = 888; 
            // This updates Component Chunk 1 version to 2.
            // Component Chunk 0 version should stay at 1 (creation time).

            // Record Delta relative to baselineTick (1)
            recorder.RecordDeltaFrame(repo, baselineTick, writer, 0L);

            // Verify
            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            ReadFrameMetadata(reader); // Read headers

            // Verify Chunks
            int chunkCount = reader.ReadInt32(); // Placeholder was patched
            
            // We expect:
            // - Possibly EntityIndex Chunk 0 (if valid), EntityIndex Chunk 1 (modified).
            // - Position Component Chunk 1 (modified).
            // - Position Component Chunk 0 (should use version check and be skipped).
            
            // Note: EntityIndex headers might be updated for eChunk1 (LastChangeTick).
            // EntityIndex Chunk 0 headers: LastChangeTick was at creation (tick 0 or 1). 
            // If they weren't touched, they shouldn't be recorded if baseline is 1.
            
            bool foundPosChunk0 = false;
            bool foundPosChunk1 = false;

            for (int i = 0; i < chunkCount; i++)
            {
                int cId = reader.ReadInt32();
                int typeCount = reader.ReadInt32();
                int typeId = reader.ReadInt32();
                int dataLen = reader.ReadInt32();
                byte[] data = reader.ReadBytes(dataLen);

                // Check for Position Component (which has a positive TypeID, EntityIndex is -1)
                if (typeId != -1)
                {
                    // Check if it's our Position component (assume it's the only one registered)
                    if (cId == 0) foundPosChunk0 = true;
                    if (cId == 1) foundPosChunk1 = true;
                }
            }

            Assert.False(foundPosChunk0, "Chunk 0 should not be recorded as it was not modified after baseline.");
            Assert.True(foundPosChunk1, "Chunk 1 should be recorded as it was modified.");
        }

        [Fact]
        public void DestructionLog_CapturesDeletions()
        {
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            repo.Tick();
            uint baseline = (uint)repo.GlobalVersion;

            repo.DestroyEntity(e1);

            recorder.RecordDeltaFrame(repo, baseline, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            reader.ReadUInt64(); // Ver
            reader.ReadByte();   // Type
            reader.ReadInt64();  // WallClockTicks (FORMAT_VERSION 3+)
            int dCount = reader.ReadInt32();
            
            Assert.Equal(1, dCount);
            int idx = reader.ReadInt32();
            ushort gen = reader.ReadUInt16();
            
            Assert.Equal(e1.Index, idx);
            Assert.Equal(e1.Generation, gen);
        }

        [Fact]
        public void Delta_OnlyRecordsModifiedComponents()
        {
            // Scenario:
            // Entity has Component A and B.
            // Modify A.
            // Delta should record chunk for A, but NOT for B.

            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1 });
            repo.AddComponent(e, new Velocity { VX = 1 });

            repo.Tick();
            uint baseline = (uint)repo.GlobalVersion;

            repo.Tick(); // Advance tick so modification is > baseline

            // Modify Position only
            ref var pos = ref repo.GetComponentRW<Position>(e);
            pos.X = 2;

            // No need for another tick here strictly, but usually we record at end of frame
            
            recorder.RecordDeltaFrame(repo, baseline, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            ReadFrameMetadata(reader);

            int chunkCount = reader.ReadInt32();
            
            int componentChunkCount = 0;

            for(int i=0; i<chunkCount; i++)
            {
                 int cId = reader.ReadInt32();
                 int tCount = reader.ReadInt32();
                 int tId = reader.ReadInt32();
                 int len = reader.ReadInt32();
                 reader.ReadBytes(len);

                 if (tId != -1)
                 {
                     componentChunkCount++;
                 }
            }
            
            // Expected: 1 component chunk (Position). Velocity should be skipped.
            Assert.Equal(1, componentChunkCount);
        }
        
        [Fact]
        public void Keyframe_RecordsAllChunks_RegardlessOfVersion()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1 });

            repo.Tick(); // Tick 1
            repo.Tick(); // Tick 2
            
            // No changes in Tick 2. 
            // Delta would be empty.
            // Keyframe should include everything.

            recorder.RecordKeyframe(repo, writer, 0L);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            ulong v = reader.ReadUInt64();
            byte type = reader.ReadByte(); // 1 = Keyframe
            Assert.Equal(1, type);
            reader.ReadInt64(); // WallClockTicks (FORMAT_VERSION 3+)
            reader.ReadInt32(); // DestroyCount = 0
            reader.ReadInt32(); // Unmanaged Events
            reader.ReadInt32(); // Managed Events
            reader.ReadInt32(); // Singleton Count (0)

            int chunkCount = reader.ReadInt32();
            Assert.True(chunkCount > 0, "Keyframe must contain chunks even if stable");
        }

        /// <summary>
        /// Known limitation (D004 / P2): Cold-only field modifications that bypass
        /// LastChangeTick stamping are not captured in delta frames.
        /// This test documents the current behavior -- NOT a correctness regression.
        /// The proper fix (per-cold-chunk version tracking) is deferred as a P2 item in
        /// DEBT-TRACKER D004.
        /// </summary>
        [Fact]
        public void Delta_ColdOnlyDirectMutation_KnownLimitation_NotCaptured()
        {
            using var repo = new EntityRepository();
            var recorder = new RecorderSystem();
            using var kfStream = new MemoryStream();
            using var kfWriter = new BinaryWriter(kfStream);

            var e = repo.CreateEntity();
            repo.Tick();
            uint baselineTick = (uint)repo.GlobalVersion;

            repo.Tick();

            // Direct cold metadata modification without LastChangeTick update.
            // In production code all lifecycle changes go through SetLifecycleState which
            // does stamp LastChangeTick. This direct write simulates a P2 edge case.
            ref var meta = ref repo.GetEntityIndex().GetMetadata(e.Index);
            EntityLifecycle originalState = meta.LifecycleState;
            meta.LifecycleState = EntityLifecycle.TearDown; // no LastChangeTick stamp

            using var deltaStream = new MemoryStream();
            using var deltaWriter = new BinaryWriter(deltaStream);
            recorder.RecordDeltaFrame(repo, baselineTick, deltaWriter, 0L);

            deltaStream.Position = 0;
            using var deltaReader = new BinaryReader(deltaStream);
            ReadFrameMetadata(deltaReader);

            int chunkCount = deltaReader.ReadInt32();

            // Known limitation: no entity index chunk should appear because LastChangeTick
            // was NOT updated. The cold-only change is silently dropped.
            bool foundEntityIndexChunk = false;
            for (int i = 0; i < chunkCount; i++)
            {
                deltaReader.ReadInt32(); // chunkId
                deltaReader.ReadInt32(); // typeCount
                int typeId = deltaReader.ReadInt32();
                int len = deltaReader.ReadInt32();
                deltaReader.ReadBytes(len);
                if (typeId == -1 || typeId == -2) foundEntityIndexChunk = true;
            }

            // This assertion documents the KNOWN LIMITATION: the change is not captured.
            Assert.False(foundEntityIndexChunk,
                "D004 known limitation: cold-only mutation without LastChangeTick stamp is not captured in delta frame");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4327</c> (D004, 📄 docs/UX/DESIGN_Review_Perspective.md §6.2 M3) — an ownership HANDOVER is in the recording from its frame: a keyframe
        /// with the authority held, then <c>SetAuthority(false)</c> and a DELTA — replayed into a fresh repo, the replica holds the authority after the
        /// keyframe and NOT after the delta. And a <c>SetAuthority</c> that changes nothing stamps nothing (the delta stays empty of index chunks).
        /// 🔴 Red-proof (compiles): <c>StampAuthorityChange</c>'s body emptied ⇒ the delta carries no index chunk ⇒ the replay keeps the old owner ⇒ red.
        /// </summary>
        [Fact]
        public void CE4327_AnAuthorityHandover_IsInTheDelta_AndReplaysWithTheNewOwner()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Tiny1>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Tiny1 { B = 1 });
            repo.SetAuthority<Tiny1>(e, true);
            int typeId = ComponentType<Tiny1>.ID;
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            repo.Tick();
            recorder.RecordKeyframe(repo, writer, 0L);
            uint baseline = repo.GlobalVersion;
            repo.Tick();
            repo.SetAuthority<Tiny1>(e, true);                                   // no change ⇒ no stamp
            Assert.True(repo.GetEntityIndex().GetMetadata(e.Index).LastChangeTick <= baseline);
            repo.SetAuthority<Tiny1>(e, false);                                  // ⭐ the handover
            recorder.RecordDeltaFrame(repo, baseline, writer, 0L);
            writer.Flush();

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            using var replay = new EntityRepository();
            replay.RegisterComponent<Tiny1>();
            var playback = new PlaybackSystem();
            playback.ApplyFrame(replay, reader);
            Assert.True(replay.HasAuthority(e, typeId));                         // the keyframe: held
            playback.ApplyFrame(replay, reader);
            Assert.False(replay.HasAuthority(e, typeId));                        // ⭐ the delta: handed over, from this frame
        }

        // Helper
        // ⭐ CE-3243 — one-byte components: a table chunk holds 65 536 slots, the worst case for a per-slot dirty check.
        //   Ids 486–491: free in every *Ids*.cs and every literal under FDP/ and Hrot/ (census 2026-10-17).
        [ComponentId(486)] private struct Tiny1 { public byte B; }
        [ComponentId(487)] private struct Tiny2 { public byte B; }
        [ComponentId(488)] private struct Tiny3 { public byte B; }
        [ComponentId(489)] private struct Tiny4 { public byte B; }
        [ComponentId(490)] private struct Tiny5 { public byte B; }
        [ComponentId(491)] private struct Tiny6 { public byte B; }

        /// <summary>
        /// ⭐ <c>CE-3243</c> — a delta frame over a world where nothing changed costs next to nothing, however large a table's
        /// chunk is. The dirty check read every slot of every chunk of every table (65 536 interface calls per one-byte table
        /// per frame, changed or not): 📐 ~25 ms a frame on every cluster node. A chunk's version is per chunk, so one read
        /// answers it. Bound is ~3× under the old cost at 1 ns a call, and ~25× over the new.
        /// </summary>
        [Fact]
        public void CE3243_AnIdleWorldsDeltaFrame_DoesNotWalkEveryChunkSlot()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Tiny1>(); repo.RegisterComponent<Tiny2>(); repo.RegisterComponent<Tiny3>();
            repo.RegisterComponent<Tiny4>(); repo.RegisterComponent<Tiny5>(); repo.RegisterComponent<Tiny6>();
            for (int i = 0; i < 10; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Tiny1()); repo.AddComponent(e, new Tiny2()); repo.AddComponent(e, new Tiny3());
                repo.AddComponent(e, new Tiny4()); repo.AddComponent(e, new Tiny5()); repo.AddComponent(e, new Tiny6());
            }
            repo.Tick();
            var recorder = new RecorderSystem();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            uint since = repo.GlobalVersion;
            recorder.RecordDeltaFrame(repo, since, writer, 0L);   // warm

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < 4000; f++)
            {
                stream.Position = 0;
                recorder.RecordDeltaFrame(repo, since, writer, 0L);
            }
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 500,
                $"4 000 idle delta frames took {sw.ElapsedMilliseconds} ms — the dirty check is walking chunk slots again");
        }

        /// <summary>
        /// ⭐ CE-2392 (DESIGN_Evaluation_Cost_And_Cadence §5a) — a delta writes a changed chunk's USED PREFIX only (the slots up to the highest
        /// issued entity), not the whole 64 KB chunk; a keyframe plus deltas still plays back every value — as the prefix grows with spawns,
        /// after a death (its slot zeroed), and for a chunk nothing changed in.
        /// ✅ Red-proof: the delta writing the full chunk again ⇒ the length assertion goes red.
        /// </summary>
        [Fact]
        public void CE2392_ADelta_WritesOnlyTheUsedPrefix_AndPlaysBackIdentically()
        {
            using var src = new EntityRepository();
            src.RegisterComponent<Position>();
            var recorder = new RecorderSystem();
            var frames = new List<byte[]>();
            var ents = new List<Entity>();
            int typeId = ComponentType<Position>.ID;
            int size = System.Runtime.CompilerServices.Unsafe.SizeOf<Position>();

            for (int i = 0; i < 5; i++) { var e = src.CreateEntity(); src.AddComponent(e, new Position { X = i + 1, Y = 7 }); ents.Add(e); }
            src.Tick();
            frames.Add(Capture(w => recorder.RecordKeyframe(src, w, 0L)));
            uint prev = src.GlobalVersion;

            // delta 1: one moves, three spawn (the prefix grows)
            src.Tick();
            src.GetComponentRW<Position>(ents[2]).X = 30;
            for (int i = 0; i < 3; i++) { var e = src.CreateEntity(); src.AddComponent(e, new Position { X = 100 + i }); ents.Add(e); }
            frames.Add(Capture(w => recorder.RecordDeltaFrame(src, prev, w, 0L)));
            int used = (src.GetEntityIndex().MaxIssuedIndex + 1) * size;
            Assert.Equal(new[] { used }, ChunkLengths(frames[^1], typeId));              // the prefix, not FdpConfig.CHUNK_SIZE_BYTES
            Assert.True(used < FdpConfig.CHUNK_SIZE_BYTES);
            prev = src.GlobalVersion;

            // delta 2: a death and a move
            src.Tick();
            src.DestroyEntity(ents[1]);
            src.GetComponentRW<Position>(ents[6]).Y = -4;
            frames.Add(Capture(w => recorder.RecordDeltaFrame(src, prev, w, 0L)));

            using var dst = new EntityRepository();
            dst.RegisterComponent<Position>();
            var playback = new PlaybackSystem();
            foreach (var f in frames)
            {
                using var r = new BinaryReader(new MemoryStream(f));
                playback.ApplyFrame(dst, r);
            }

            Assert.False(dst.IsAlive(ents[1]));
            foreach (var e in ents)
            {
                if (!src.IsAlive(e)) continue;
                Assert.True(dst.IsAlive(e));
                Assert.Equal(src.GetComponentRO<Position>(e), dst.GetComponentRO<Position>(e));
            }
        }

        private static byte[] Capture(Action<BinaryWriter> record)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) { record(w); w.Flush(); }
            return ms.ToArray();
        }

        private List<int> ChunkLengths(byte[] frame, int typeId)
        {
            using var reader = new BinaryReader(new MemoryStream(frame));
            ReadFrameMetadata(reader);
            int chunkCount = reader.ReadInt32();
            var lengths = new List<int>();
            for (int i = 0; i < chunkCount; i++)
            {
                reader.ReadInt32(); reader.ReadInt32();
                int tId = reader.ReadInt32();
                int len = reader.ReadInt32();
                reader.ReadBytes(len);
                if (tId == typeId) lengths.Add(len);
            }
            return lengths;
        }

        private void ReadFrameMetadata(BinaryReader reader)
        {
            reader.ReadUInt64(); // Version
            reader.ReadByte();   // Type
            reader.ReadInt64();  // WallClockTicks (FORMAT_VERSION 3+)
            int dCount = reader.ReadInt32(); // Destructions
            for(int i=0; i<dCount; i++) { reader.ReadInt32(); reader.ReadUInt16(); }
            
            // Skip Events (Unmanaged + Managed)
            // Assuming 0 events for these logic tests
            reader.ReadInt32(); // Unmanaged Count
            reader.ReadInt32(); // Managed Count
            
            // Skip Singletons
            int sCount = reader.ReadInt32();
            for(int i=0; i<sCount; i++) 
            {
                reader.ReadInt32(); // Type
                int len = reader.ReadInt32(); 
                reader.BaseStream.Seek(len, SeekOrigin.Current); // Skip Data
            }
        }
    }
}

