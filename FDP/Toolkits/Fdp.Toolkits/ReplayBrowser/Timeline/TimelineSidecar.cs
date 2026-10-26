using System;
using System.IO;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9 TL-6) — the index kept beside its recording as
    /// <c>&lt;recording&gt;.timeline</c>, so the second open costs a file read, not a pass. A header names the recording it was built from
    /// (its byte size and last-write time) and the schema; any mismatch ⇒ <see cref="TryRead"/> fails and the caller rebuilds.
    /// Only a COMPLETE index is written.
    /// </summary>
    public static class TimelineSidecar
    {
        private const uint Magic = 0x584C5448;   // "HTLX"
        /// <summary>Bumped whenever an extractor or the event layout changes meaning — an old sidecar then rebuilds.</summary>
        public const int SchemaVersion = 1;

        public static string PathFor(string fdpPath) => fdpPath + ".timeline";

        /// <summary>Writes a complete index; false (and nothing left behind) when it cannot — a read-only folder is not an error.</summary>
        public static bool Write(string fdpPath, TimelineIndex index)
        {
            if (!index.IsComplete) return false;
            string path = PathFor(fdpPath), tmp = path + ".tmp";
            try
            {
                var rec = new FileInfo(fdpPath);
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    w.Write(Magic);
                    w.Write(SchemaVersion);
                    w.Write(rec.Length);
                    w.Write(rec.LastWriteTimeUtc.Ticks);
                    w.Write(index.TotalFrames);
                    w.Write(index.FrameTicks.Count);
                    for (int i = 0; i < index.FrameTicks.Count; i++) w.Write(index.FrameTicks[i]);
                    for (int lane = 0; lane < TimelineLanes.Count; lane++)
                    {
                        var events = index.Lane((TimelineLane)lane);
                        w.Write(events.Count);
                        foreach (var e in events)
                        {
                            w.Write(e.Frame); w.Write(e.Subject); w.Write(e.Other);
                            w.Write(e.X); w.Write(e.Y); w.Write(e.Value);
                        }
                    }
                }
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { File.Delete(tmp); } catch { /* best effort */ }
                return false;
            }
        }

        /// <summary>The sidecar's index when it exists and was built from THIS recording with THIS schema; else null.</summary>
        public static TimelineIndex? TryRead(string fdpPath)
        {
            string path = PathFor(fdpPath);
            try
            {
                if (!File.Exists(path) || !File.Exists(fdpPath)) return null;
                var rec = new FileInfo(fdpPath);
                using var r = new BinaryReader(File.OpenRead(path));
                if (r.ReadUInt32() != Magic || r.ReadInt32() != SchemaVersion) return null;
                if (r.ReadInt64() != rec.Length || r.ReadInt64() != rec.LastWriteTimeUtc.Ticks) return null;
                int total = r.ReadInt32();
                var ticks = new long[r.ReadInt32()];
                for (int i = 0; i < ticks.Length; i++) ticks[i] = r.ReadInt64();
                var lanes = TimelineIndex.NewLanes();
                for (int lane = 0; lane < TimelineLanes.Count; lane++)
                {
                    var events = new TimelineEvent[r.ReadInt32()];
                    for (int i = 0; i < events.Length; i++)
                        events[i] = new TimelineEvent(r.ReadInt32(), (TimelineLane)lane, r.ReadInt64(), r.ReadInt64(),
                            r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    lanes[lane] = events;
                }
                return new TimelineIndex(lanes, ticks, ticks.Length, total);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or OverflowException or OutOfMemoryException)
            {
                return null;
            }
        }
    }
}
