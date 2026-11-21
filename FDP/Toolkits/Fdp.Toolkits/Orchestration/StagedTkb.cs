using System;
using System.IO;
using System.Text.Json;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>R-4018</c> — the record the orchestrator's TKB sync leaves in a node's TKB staging directory, naming the
    /// archive it copied there. ⛔ The ONE definition of the file both sides use: the orchestrator WRITES it after the
    /// copy, the node's knowledge-base step READS it.
    ///
    /// <para>⭐ <b>Why a record beside the archive and not a field on the load command:</b> the TKB is not tied to a
    /// scenario (one TKB per deployment), so its name travels with the FILE SYNC that delivered it — 🔒 user,
    /// <c>2026-10-15</c>: <i>"Do you mean the file synchronization message? In that case, of course, the name is
    /// fine."</i> The record survives a node restart, which the join sync (<c>CE-4003</c> step 2) relies on.</para>
    ///
    /// <para>⚠ <b>No record = no master copy</b> (the default deployment today: <c>{nas}/tkb/</c> holds nothing). The
    /// node then keeps its built-in catalogue — legal, never a failure. 🔒 User, <c>2026-10-15</c>: <i>"go ahead with the
    /// fallback"</i>. 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §0.</para>
    /// </summary>
    public static class StagedTkb
    {
        /// <summary>The record's file name inside a node's TKB staging directory.</summary>
        public const string FileName = "StagedTkb.json";

        /// <summary>Writes the record naming <paramref name="tkbName"/> into <paramref name="tkbStagingDirectory"/>.</summary>
        public static void Write(string tkbStagingDirectory, string tkbName)
        {
            if (string.IsNullOrWhiteSpace(tkbName)) throw new ArgumentException("A TKB name is required.", nameof(tkbName));
            Directory.CreateDirectory(tkbStagingDirectory);
            File.WriteAllText(Path.Combine(tkbStagingDirectory, FileName),
                "{\"TkbName\":" + JsonSerializer.Serialize(tkbName, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) + "}");
        }

        /// <summary>Removes the record — the master copy is gone, so the node must not load a stale archive.</summary>
        public static void Clear(string tkbStagingDirectory)
        {
            var path = Path.Combine(tkbStagingDirectory, FileName);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// The staged TKB's name, or <see langword="null"/> when nothing is staged. ⚠ A corrupt record throws — loud, never a silent fallback.
        /// </summary>
        public static string? Read(string tkbStagingDirectory)
        {
            var path = Path.Combine(tkbStagingDirectory, FileName);
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.TryGetProperty("TkbName", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
    }
}
