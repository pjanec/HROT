using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>One file of an archived exercise as its manifest lists it: the name inside the exercise folder and its size in bytes.</summary>
    public readonly record struct ArchivedFile(string Name, long Size);

    /// <summary>
    /// ⭐ CE-4330 ① — what a completed archive pull hands on (the payload of the archive request's success): every file it put in the exercise folder,
    /// with its size, so the manifest written LAST lists them (<see cref="ArchiveManifest"/>).
    /// </summary>
    public sealed record ArchivePullResult(IReadOnlyList<ArchivedFile> Files);

    /// <summary>
    /// ⭐⭐ CE-4330 ① (📄 docs/DESIGN_Recording_Storage.md §3 P4) — the ONE place that says whether an exercise on the NAS is COMPLETE. The manifest is the
    /// exercise's sidecar (<c>Orchestrator.json</c>): written LAST, after every recording and the scenario copy, it lists every file with its size
    /// (<see cref="FilesProperty"/>); files land as <c>*.part</c> and are renamed when whole (<see cref="PartSuffix"/>). ⛔ No second copy of the rule.
    /// </summary>
    /// <remarks>
    /// ⚠ An archive written before CE-4330 has a sidecar with no file list: it was written only after a full pull, so it counts as complete (<see cref="Status.Legacy"/>).
    /// </remarks>
    public static class ArchiveManifest
    {
        /// <summary>The exercise's sidecar — the manifest.</summary>
        public const string SidecarFileName = "Orchestrator.json";

        /// <summary>The sidecar's list of the archive's files: <c>[{ "name": "node_1.fdp", "size": 1234 }, …]</c>.</summary>
        public const string FilesProperty = "archiveFiles";

        /// <summary>A file still being copied; renamed to its name when whole.</summary>
        public const string PartSuffix = ".part";

        /// <summary>
        /// Where an exercise on the NAS stands: <see cref="Complete"/> when its manifest is there and every listed file matches; otherwise INCOMPLETE —
        /// <see cref="FilesPresent"/> of <see cref="FilesTotal"/> (0 of 0 = no manifest yet).
        /// </summary>
        public readonly record struct Status(bool HasManifest, bool Legacy, int FilesTotal, int FilesPresent)
        {
            public bool Complete => HasManifest && (Legacy || FilesPresent == FilesTotal);
        }

        /// <summary>Reads the manifest of <paramref name="exerciseDirectory"/> and checks every file it lists (exists, same size).</summary>
        public static Status Read(string exerciseDirectory)
        {
            var sidecar = Path.Combine(exerciseDirectory, SidecarFileName);
            if (!File.Exists(sidecar)) return default;
            JsonNode? dom;
            try { dom = JsonNode.Parse(File.ReadAllText(sidecar)); }
            catch (System.Text.Json.JsonException) { return default; }   // a torn manifest is no manifest
            catch (IOException) { return default; }
            if (dom?[FilesProperty] is not JsonArray files) return new Status(true, true, 0, 0);
            int present = 0;
            foreach (var f in files)
            {
                var name = Path.GetFileName(f?["name"]?.GetValue<string>() ?? "");
                long size = f?["size"]?.GetValue<long>() ?? -1;
                if (name.Length == 0) continue;
                var path = Path.Combine(exerciseDirectory, name);
                if (File.Exists(path) && new FileInfo(path).Length == size) present++;
            }
            return new Status(true, false, files.Count, present);
        }

        /// <summary>Puts <paramref name="files"/> on <paramref name="sidecar"/> as its file list.</summary>
        public static void SetFiles(JsonObject sidecar, IReadOnlyList<ArchivedFile> files)
        {
            if (sidecar == null) throw new ArgumentNullException(nameof(sidecar));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var list = new JsonArray();
            for (int i = 0; i < files.Count; i++)
                list.Add(new JsonObject { ["name"] = files[i].Name, ["size"] = files[i].Size });
            sidecar[FilesProperty] = list;
        }

        /// <summary>Writes <paramref name="text"/> to <paramref name="path"/> as a <c>.part</c> renamed when whole — a reader never sees half a manifest.</summary>
        public static void WriteWhole(string path, string text)
        {
            var part = path + PartSuffix;
            File.WriteAllText(part, text);
            File.Move(part, path, overwrite: true);
        }
    }
}
