using System;
using System.Collections.Generic;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Kernel.FlightRecorder.Metadata
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
    }
}
