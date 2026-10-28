using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-4093</c> (AQ108 D20) — <b>"are there unsaved changes?" = the WHOLE world compared, on demand.</b> The node captures its world
    /// as a binary keyframe (<see cref="WorldKeyframes.Capture(EntityRepository, CaptureOptions, long)"/> — the flight recorder's own writer, the
    /// capture the dry run uses), keeps only the 64-bit HASH of its content, and compares a later capture with the hash it took at its
    /// BASELINE: when a load into editing commits, after a completed save round, at a late joiner's catch-up. Nothing periodic, no indicator.
    /// <para>⭐ <b>The hash covers the world's CONTENT only — a keyframe carries three things that are not content, and exactly those are skipped</b>
    /// (📐 each measured on <c>uc-urban-battle</c>, <c>WorldChangeCheckTests</c> and <c>TheWorldChangeCheckOnTheEditorTests</c>):
    /// ① the repository <b>tick</b> (bytes 0–7) and ② the <b>wall clock</b> (bytes 9–16) of the frame header — two captures of one world at
    /// different moments differ there and nowhere else; ③ every entity's <see cref="EntityMetadataCold.LastChangeTick"/> in the entity index's cold
    /// chunks — "the tick this entity was last modified": ⚠ <b>touching ANY component, a runtime-only one included, stamps it</b>
    /// (selecting a unit adds its <c>SelectionState</c> and read as a changed world until this was skipped). The type byte, the destroy count, the
    /// masks, the generation, flags, authority and lifecycle stay in.</para>
    /// <para>⭐ <b>Which component types count is decided by the types' OWN flags</b> (<see cref="DataPolicy"/>), through
    /// <see cref="CaptureOptions"/> — never by a list here. ⚠ It errs one way only: the keyframe follows the storage layout, so a unit deleted
    /// and re-added may read as a change (a needless "save first?"), and an unchanged world never reads as changed.</para>
    /// <para>⛔ <b>The state lives in the node's host composition, NOT in the ECS world</b> — a baseline stored in the world would itself change it.
    /// One instance per node, built where the handlers are composed (<c>EcsHostClusterHandlers.Compose</c>).</para>
    /// 📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md D20.
    /// </summary>
    public sealed class WorldChangeCheck
    {
        // The keyframe layout RecorderSystem.RecordKeyframe writes (one owner: that method). A frame this walker cannot parse THROWS — a layout
        // change must redden WorldChangeCheckTests, never produce a confident wrong answer.
        private const int TypeByteAt = 8, WallClockAt = 9, DestroyCountAt = 17, EventsAt = 21;

        private static readonly int ColdSize = Unsafe.SizeOf<EntityMetadataCold>();
        private static readonly int LastChangeTickAt = OffsetOfLastChangeTick();

        private readonly object _gate = new();
        private ulong _baseline;
        private bool _hasBaseline;
        private bool _settling;
        private ulong _settleHash;
        private int _settleFrames;

        /// <summary>How many frames a world may keep moving after its load before the baseline is taken regardless (it is logged, never silent).</summary>
        public const int DefaultMaxSettleFrames = 20;

        /// <param name="options">The type set that counts as world content. Default: <see cref="DefaultOptions"/>.</param>
        public WorldChangeCheck(CaptureOptions? options = null) => Options = options ?? DefaultOptions;

        /// <summary>
        /// What counts as the world: every type the flight recorder records (<see cref="KeyframeTypes.Recorded"/> — a type's own <c>NoReplay</c> /
        /// <c>Transient</c> flag takes it out), the singleton tables OFF.
        /// <para>📐 <b>Measured on <c>uc-urban-battle</c>, 2026-10-28</b> — both choices are forced by data, not taste: ① the SNAPSHOTABLE set
        /// (<see cref="CaptureOptions.NodeDryRun"/>'s) leaves out the managed authoring types — <c>RoutePlan</c>, <c>ActiveMissionPlan</c>,
        /// <c>SimCombatDef</c>, <c>TkbCompositionDef</c>, <c>SensorConfigPayload</c> (<c>NoPreview</c>: not shared with background threads) — so a user's route or
        /// mission edit would read "unchanged"; ② the singleton tables hold the clock (<c>GlobalTime</c> moves every frame, paused or not) and runtime machinery
        /// (<c>RaycastBatchData</c>, <c>PathfindingBatchData</c>, <c>EqsResultPool</c>, <c>RunTokenMark</c>, <c>NavigationHandleMark</c>) — none is authored state.</para>
        /// </summary>
        public static readonly CaptureOptions DefaultOptions = new(KeyframeTypes.Recorded, Singletons: false);

        public CaptureOptions Options { get; }

        /// <summary>True once <see cref="TakeBaseline"/> ran.</summary>
        public bool HasBaseline { get { lock (_gate) return _hasBaseline; } }

        /// <summary>The baseline's hash (0 while <see cref="HasBaseline"/> is false) — for diagnostics and rails.</summary>
        public ulong BaselineHash { get { lock (_gate) return _baseline; } }

        /// <summary>The content hash of <paramref name="repo"/> now: a keyframe capture, minus what is not content (see the type's remarks).</summary>
        public ulong Hash(EntityRepository repo) => HashOf(WorldKeyframes.Capture(repo ?? throw new ArgumentNullException(nameof(repo)), Options));

        /// <summary>The content hash of a keyframe's bytes: the tick, the wall clock and every entity's <c>LastChangeTick</c> skipped.</summary>
        /// <exception cref="InvalidDataException">The bytes are not a keyframe this walker understands (events in it, a truncated chunk).</exception>
        public static ulong HashOf(WorldKeyframe keyframe)
        {
            if (keyframe is null) throw new ArgumentNullException(nameof(keyframe));
            var f = keyframe.Bytes.AsSpan();
            if (f.Length < EventsAt + 12) throw new InvalidDataException("a keyframe starts with its 21-byte header, the event counts, the singleton count and the chunk count");

            ulong h = StableHash.Basis64;
            h = StableHash.Fnv1a64(f.Slice(TypeByteAt, 1), h);                                  // the frame type
            h = StableHash.Fnv1a64(f.Slice(DestroyCountAt, EventsAt - DestroyCountAt), h);     // the destroy count (a keyframe writes 0)
            int pos = EventsAt;

            // events: a keyframe captured by WorldKeyframes carries none (the stream counts are 0, 0)
            if (BinaryPrimitives.ReadInt32LittleEndian(f.Slice(pos)) != 0 || BinaryPrimitives.ReadInt32LittleEndian(f.Slice(pos + 4)) != 0)
                throw new InvalidDataException("a keyframe with events in it is not a world capture");
            h = StableHash.Fnv1a64(f.Slice(pos, 8), h);
            pos += 8;

            // singletons: count, then (type id, length, bytes) each
            int singletons = ReadInt(f, ref pos, ref h);
            for (int i = 0; i < singletons; i++)
            {
                h = StableHash.Fnv1a64(f.Slice(pos, 4), h);
                pos += 4;
                int len = ReadInt(f, ref pos, ref h);
                h = StableHash.Fnv1a64(Take(f, pos, len), h);
                pos += len;
            }

            // chunks: count, then (chunk id, 1, type id, byte length, bytes) each
            int chunks = ReadInt(f, ref pos, ref h);
            for (int i = 0; i < chunks; i++)
            {
                h = StableHash.Fnv1a64(Take(f, pos, 8), h);   // chunk id, the "1"
                pos += 8;
                int typeId = ReadInt(f, ref pos, ref h, allowNegative: true);   // the entity index's chunks are -1 / -2
                int len = ReadInt(f, ref pos, ref h);
                var data = Take(f, pos, len);
                if (typeId == RecorderSystem.ENTITY_INDEX_COLD_TYPE_ID)
                {
                    if (len % ColdSize != 0) throw new InvalidDataException($"an entity index cold chunk of {len} bytes is not a whole number of {ColdSize}-byte entries");
                    for (int e = 0; e < len; e += ColdSize)
                    {
                        h = StableHash.Fnv1a64(data.Slice(e, LastChangeTickAt), h);
                        int after = LastChangeTickAt + sizeof(ulong);
                        h = StableHash.Fnv1a64(data.Slice(e + after, ColdSize - after), h);
                    }
                }
                else
                    h = StableHash.Fnv1a64(data, h);
                pos += len;
            }

            if (pos != f.Length) throw new InvalidDataException($"{f.Length - pos} bytes follow the last chunk of the keyframe");
            return h;
        }

        private static int ReadInt(ReadOnlySpan<byte> f, ref int pos, ref ulong h, bool allowNegative = false)
        {
            var four = Take(f, pos, 4);
            h = StableHash.Fnv1a64(four, h);
            pos += 4;
            int v = BinaryPrimitives.ReadInt32LittleEndian(four);
            if (v < 0 && !allowNegative) throw new InvalidDataException("a negative count or length in a keyframe");
            return v;
        }

        private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> f, int pos, int len)
        {
            if (len < 0 || pos < 0 || pos + len > f.Length) throw new InvalidDataException("a keyframe record runs past the end of the keyframe");
            return f.Slice(pos, len);
        }

        private static int OffsetOfLastChangeTick()
        {
            var m = new EntityMetadataCold();
            return (int)Unsafe.ByteOffset(ref Unsafe.As<EntityMetadataCold, byte>(ref m), ref Unsafe.As<ulong, byte>(ref m.LastChangeTick));
        }

        /// <summary>Takes <paramref name="repo"/>'s content hash as THE baseline (replacing any earlier one).</summary>
        public void TakeBaseline(EntityRepository repo)
        {
            ulong h = Hash(repo);
            lock (_gate) { _baseline = h; _hasBaseline = true; }
        }

        /// <summary>Forgets the baseline (the node left the state it was taken in) and any settling in progress; <see cref="HasChanged"/> then answers true.</summary>
        public void ClearBaseline()
        {
            lock (_gate) { _baseline = 0; _hasBaseline = false; _settling = false; }
        }

        /// <summary>True between <see cref="BeginSettling"/> and the frame <see cref="SettleStep"/> takes the baseline.</summary>
        public bool IsSettling { get { lock (_gate) return _settling; } }

        /// <summary>How many <see cref="SettleStep"/> frames the last settling took (0 before one ran).</summary>
        public int SettledAfterFrames { get { lock (_gate) return _settleFrames; } }

        /// <summary>True when the last settling gave up waiting for a still world (<see cref="DefaultMaxSettleFrames"/>) and took the baseline anyway.</summary>
        public bool GaveUpSettling { get { lock (_gate) return _gaveUp; } }
        private bool _gaveUp;

        /// <summary>
        /// ⭐ <b>A world that has just been LOADED is not yet the world nobody has touched.</b> 📐 Measured on <c>uc-urban-battle</c> in the real editor:
        /// the frame AFTER the load chain resolves, the simulation modules initialise the loaded units once — their poses, blueprint blackboards, behaviour
        /// and SOP state, the brain journal (9 component types, 682 units) — and then nothing moves again. A baseline taken at the resolve itself read
        /// "changed" from the first frame on. ⇒ a load starts a SETTLING: the old baseline is dropped (<see cref="HasBaseline"/> is false, the node answers
        /// "no-baseline", which a master counts as changed) and <see cref="SettleStep"/>, once per frame, takes the baseline at the first frame whose
        /// content equals the previous frame's. ⛔ Not a list of types to ignore — those writes are world content, and an edit of them must count.
        /// </summary>
        public void BeginSettling()
        {
            lock (_gate) { _baseline = 0; _hasBaseline = false; _settling = true; _settleHash = 0; _settleFrames = 0; _gaveUp = false; }
        }

        /// <summary>
        /// One frame of a settling (call it once per frame, on the frame thread): hashes the world; when it equals the previous frame's it becomes the baseline.
        /// After <paramref name="maxFrames"/> frames the baseline is taken anyway (<see cref="GaveUpSettling"/>). A no-op when nothing is settling.
        /// </summary>
        /// <returns>True in the frame the baseline was taken.</returns>
        public bool SettleStep(EntityRepository repo, int maxFrames = DefaultMaxSettleFrames)
        {
            lock (_gate) { if (!_settling) return false; }
            ulong h = Hash(repo);
            lock (_gate)
            {
                if (!_settling) return false;
                _settleFrames++;
                bool stable = _settleFrames > 1 && h == _settleHash;
                if (!stable && _settleFrames < maxFrames) { _settleHash = h; return false; }
                _gaveUp = !stable;
                _baseline = h; _hasBaseline = true; _settling = false;
                return true;
            }
        }

        /// <summary>
        /// True when <paramref name="repo"/>'s content differs from the baseline — ⚠ and true when there is NONE (nothing says the world is
        /// saved); ask <see cref="HasBaseline"/> to tell the two apart.
        /// </summary>
        public bool HasChanged(EntityRepository repo)
        {
            ulong baseline; bool has;
            lock (_gate) { baseline = _baseline; has = _hasBaseline; }
            return !has || Hash(repo) != baseline;
        }
    }
}
