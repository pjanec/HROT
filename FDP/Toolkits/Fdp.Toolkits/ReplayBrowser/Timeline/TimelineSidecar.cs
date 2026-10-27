using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fdp.Core.Serialization;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9 TL-6, §9c ④) — the index kept beside its recording as
    /// <c>&lt;recording&gt;.timeline</c>. A header names the recording it was built from (its byte size and last-write time) and the schema;
    /// a mismatch ⇒ nothing is reused. Then, per lane, its id and its DEFINITION HASH ⇒ a lane added or changed since is the only one a
    /// later open has to scan. Only a complete index is written.
    /// </summary>
    public static class TimelineSidecar
    {
        private const uint Magic = 0x584C5448;   // "HTLX"
        /// <summary>Bumped whenever the layout or what an event means changes — an old sidecar then rebuilds.</summary>
        public const int SchemaVersion = 3;   // ⭐ CE-4059 — 3 adds the position track after the lanes

        public static string PathFor(string fdpPath) => fdpPath + ".timeline";

        /// <summary>A lane's identity in the cache: its id, its predicate and its projection (polymorphic JSON, hashed). A definition that
        /// cannot be serialised hashes to a fresh value — it is rebuilt every open rather than reused wrongly.</summary>
        public static string HashOf(IndexedLaneDefinition lane)
        {
            string json;
            try
            {
                // ⭐ CE-3187 — the one shared options instance (OneSharedJsonOptionsTests). ⚠ A changed hash only rebuilds a lane once.
                json = lane.Lane.Id + "|" + JsonSerializer.Serialize<SearchPredicateDto>(lane.When, FdpJsonOptionsRegistry.DefaultRelaxed) + "|" +
                       (lane.Payload == null ? "" : JsonSerializer.Serialize(lane.Payload, FdpJsonOptionsRegistry.DefaultRelaxed));
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
            {
                json = Guid.NewGuid().ToString();
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
        }

        /// <summary>Writes a complete index and each lane's hash; false (and nothing left behind) when it cannot.</summary>
        public static bool Write(string fdpPath, TimelineIndex index, IReadOnlyDictionary<string, string> laneHashes)
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
                    var written = new List<string>();
                    foreach (var id in index.LaneIds) if (laneHashes.ContainsKey(id) && !index.LaneErrors.ContainsKey(id)) written.Add(id);
                    w.Write(written.Count);
                    foreach (var id in written)
                    {
                        w.Write(id);
                        w.Write(laneHashes[id]);
                        var events = index.Raw(id);
                        w.Write(events.Count);
                        foreach (var e in events)
                        {
                            w.Write(e.Frame); w.Write(e.Subject); w.Write(e.Other);
                            w.Write(e.X); w.Write(e.Y); w.Write(e.Value);
                        }
                    }
                    // ⭐ CE-4059 — the position track: per unit (ascending id) its sample count and samples
                    var units = new List<long>(index.Positions.UnitIds);
                    units.Sort();
                    w.Write(units.Count);
                    foreach (var unit in units)
                    {
                        var samples = index.Positions.Samples(unit);
                        w.Write(unit);
                        w.Write(samples.Count);
                        foreach (var s in samples) { w.Write(s.Frame); w.Write(s.X); w.Write(s.Y); w.Write(s.Z); w.Write(s.Heading); }
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

        /// <summary>The cached index and each cached lane's hash, when the sidecar exists and was built from THIS recording with THIS schema;
        /// else null.</summary>
        public static (TimelineIndex Index, IReadOnlyDictionary<string, string> Hashes)? TryRead(string fdpPath)
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
                int laneCount = r.ReadInt32();
                var lanes = new Dictionary<string, TimelineEvent[]>(StringComparer.Ordinal);
                var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int l = 0; l < laneCount; l++)
                {
                    string id = string.Intern(r.ReadString());
                    hashes[id] = r.ReadString();
                    var events = new TimelineEvent[r.ReadInt32()];
                    for (int i = 0; i < events.Length; i++)
                        events[i] = new TimelineEvent(r.ReadInt32(), id, r.ReadInt64(), r.ReadInt64(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    lanes[id] = events;
                }
                int unitCount = r.ReadInt32();
                var units = new Dictionary<long, PositionSample[]>(unitCount);
                for (int u = 0; u < unitCount; u++)
                {
                    long unit = r.ReadInt64();
                    var samples = new PositionSample[r.ReadInt32()];
                    for (int i = 0; i < samples.Length; i++)
                        samples[i] = new PositionSample(r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    units[unit] = samples;
                }
                return (new TimelineIndex(lanes, ticks, ticks.Length, total, null, new PositionTrack(units)), hashes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or OverflowException or OutOfMemoryException)
            {
                return null;
            }
        }
    }
}
