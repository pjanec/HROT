using System;
using System.Collections.Generic;
using Fdp.Core.FlightRecorder;

namespace Fdp.Core.FlightRecorder.Metadata
{
    [Serializable]
    public class RecordingMetadata
    {
        public int ProtocolVersion { get; set; } = 1;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string AppVersion { get; set; } = "1.0.0";
        public string Description { get; set; } = "";
        public int TotalFrames { get; set; } = 0;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public Dictionary<string, string> CustomTags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Schema manifest captured at record time.
        /// Key: component ID; Value: structural layout info (size, FNV hash, type name).
        /// <para>
        /// <c>null</c> for recordings produced before schema manifest support was added
        /// (legacy recordings).  <see cref="FlightRecorder.SchemaValidator"/> treats a
        /// <c>null</c> manifest as a backwards-compatible warning rather than an error.
        /// </para>
        /// </summary>
        public Dictionary<int, ComponentSchemaInfo>? SchemaManifest { get; set; }
        
        /// <summary>
        /// Event schema manifest captured at record time.
        /// Key: event ID; Value: structural layout info (size, FNV hash, type name).
        /// </summary>
        public Dictionary<int, ComponentSchemaInfo>? EventManifest { get; set; }

        /// <summary>
        /// The highest network (DIS) entity ID observed during the recording session.
        /// Written by <see cref="Fdp.Core.FlightRecorder.AsyncRecorder"/> when
        /// <c>Dispose()</c> finalizes the recording.
        ///
        /// <para>
        /// Used by <c>ReplayLoadClusterStateHandler</c> to report the cluster-wide maximum entity
        /// ID to the orchestrator so the <c>DdsIdAllocatorServer</c> can reset its
        /// counter above the replay's ID space, preventing collisions when new entities
        /// are created during a Live-from-Replay branch (CGF1-S0304).
        /// A value of <c>0</c> means the recording pre-dates this feature or the recorder
        /// was not supplied with a network entity map.
        /// </para>
        /// </summary>
        public long MaxNetworkId { get; set; } = 0;

        /// <summary>
        /// Distributed exercise identifier shared by all nodes participating in one exercise session.
        /// Defaults to <see cref="Guid.Empty"/> for legacy recordings that pre-date federation support.
        /// </summary>
        public Guid ExerciseId { get; set; } = Guid.Empty;

        /// <summary>
        /// Identifier of the distributed node that produced this recording.
        /// Defaults to <c>0</c> for legacy recordings that pre-date federation support.
        /// </summary>
        public int NodeId { get; set; } = 0;

        /// <summary>
        /// ⭐ <c>CE-3126</c> (R-229) — the terrain resident while recording (the name the scenario resolved it by), so a
        /// replay can load it again. Empty when no terrain was loaded, and for recordings that pre-date this field.
        /// 📄 docs/DESIGN_Geo_Origin.md §2 E.
        /// </summary>
        public string TerrainName { get; set; } = "";

        /// <summary>
        /// ⭐ <c>CE-3126</c> (R-229) — the geodetic origin the recording node converted with. 🔒 User: <i>"Origin can be saved in
        /// recording metadata in case terrain with remembered name no longer exists"</i> — so a replay places the recording
        /// where it was recorded even when the terrain is gone or its origin was edited since. <c>null</c> for recordings that
        /// pre-date this field. 📄 docs/DESIGN_Geo_Origin.md §2 E/F.
        /// </summary>
        public GeoOriginRecord? GeoOrigin { get; set; }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B3 (R-3044) — the CHECKPOINTS in this recording: each a keyframe the recorder wrote on request and
        /// named. A restore seeks straight to one by its label. Empty for recordings without any (and for those that pre-date it).
        /// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1g.
        /// </summary>
        public List<CheckpointMark> Checkpoints { get; set; } = new List<CheckpointMark>();

        /// <summary>
        /// ⭐ <c>CE-3299</c> B4 / <c>D18</c> — the exercise this recording BRANCHED from (a checkpoint restore, or a replay going live);
        /// <c>Guid.Empty</c> for a recording that began at a load. Its first frames are a copy of that exercise's up to
        /// <see cref="BranchWallTicks"/>.
        /// </summary>
        public Guid ParentExerciseId { get; set; } = Guid.Empty;

        /// <summary>⭐ <c>D18</c> — the wall time of the branch point (0 when not a branch).</summary>
        public long BranchWallTicks { get; set; }

        /// <summary>⭐ CE-3299 D17 (R-3045) — 0: a FULL recording (every frame). N &gt; 0: a CHECKPOINTING recording — a keyframe every N s of sim time
        /// (and at each named checkpoint), nothing between: restore points, no smooth replay.</summary>
        public double CheckpointingSeconds { get; set; }
    }

    /// <summary>⭐ <c>CE-3299</c> B3 — one named keyframe of a recording: its label, its frame (0-based, in file order) and its wall time.</summary>
    [Serializable]
    public sealed class CheckpointMark
    {
        /// <summary>The checkpoint's name — unique within the recording.</summary>
        public string Label { get; set; } = "";

        /// <summary>The keyframe's position among the recording's frames (0 = the first written).</summary>
        public int Frame { get; set; }

        /// <summary>The keyframe's frame-locked wall time (<c>GlobalTime.TotalWallTicks</c>).</summary>
        public long WallTicks { get; set; }

        /// <summary>⭐ <c>CE-3299</c> B4 (D5) — the simulation clock at the keyframe (<c>GlobalTime.TotalTime</c>): a restore moves the
        /// cluster's clock back to it.</summary>
        public double TotalTime { get; set; }

        /// <summary>⭐ B4 — <c>GlobalTime.UnscaledTotalTime</c> at the keyframe.</summary>
        public double UnscaledTotalTime { get; set; }

        /// <summary>⭐ B4 — <c>GlobalTime.FrameNumber</c> at the keyframe.</summary>
        public long FrameNumber { get; set; }
    }

    /// <summary>A geodetic point — latitude and longitude in degrees, altitude in metres — as written into a recording.</summary>
    [Serializable]
    public sealed class GeoOriginRecord
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double Alt { get; set; }
    }
}
