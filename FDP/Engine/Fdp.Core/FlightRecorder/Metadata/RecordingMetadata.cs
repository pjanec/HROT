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
    }
}
