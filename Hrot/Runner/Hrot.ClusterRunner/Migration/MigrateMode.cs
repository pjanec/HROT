using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Serialization;

namespace Hrot.ClusterRunner.Migration;

/// <summary>
/// Implements the <c>--mode migrate</c> batch migration subcommand.
/// Enumerates all *.json files in <see cref="_inputDirectory"/>, and for each
/// file that has a <c>$meta</c> envelope, migrates it to <see cref="_targetVersion"/>
/// (or the current registered version when targetVersion is -1).
/// Progress is reported to <see cref="_out"/> line-by-line.
/// </summary>
internal sealed class MigrateMode
{
    private readonly MigrationServices _services;
    private readonly string _inputDirectory;
    private readonly int _targetVersion;
    private readonly bool _dryRun;
    private readonly TextWriter _out;

    internal MigrateMode(
        MigrationServices services,
        string inputDirectory,
        int targetVersion,
        bool dryRun,
        TextWriter? output = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _inputDirectory = inputDirectory;
        _targetVersion = targetVersion;
        _dryRun = dryRun;
        _out = output ?? Console.Out;
    }

    /// <summary>
    /// Runs the batch migration. Returns 0 on success (no failures), 1 when
    /// one or more files could not be migrated.
    /// </summary>
    internal async Task<int> RunAsync(CancellationToken ct = default)
    {
        // 1. Resolve input directory.
        string inputDir = string.IsNullOrWhiteSpace(_inputDirectory)
            ? Directory.GetCurrentDirectory()
            : _inputDirectory;

        if (!Directory.Exists(inputDir))
        {
            Console.Error.WriteLine($"[migrate] Input directory does not exist: {inputDir}");
            return 1;
        }

        // 2. Enumerate JSON files.
        string[] files = Directory.GetFiles(inputDir, "*.json", SearchOption.AllDirectories);
        int total = files.Length;
        int migrated = 0, skipped = 0, failed = 0;

        // 3. Process each file.
        for (int i = 0; i < files.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string path = files[i];
            string label = $"{i + 1}/{total}: {Path.GetFileName(path)}";

            try
            {
                FileMigrateResult result = await MigrateFileAsync(path, ct).ConfigureAwait(false);
                if (result.Skipped)
                {
                    _out.WriteLine($"{label} -- SKIPPED ({result.Reason})");
                    skipped++;
                }
                else
                {
                    string dryTag = _dryRun ? " [dry-run]" : "";
                    _out.WriteLine($"{label} -- OK (v{result.FromVersion} -> v{result.ToVersion}){dryTag}");
                    migrated++;
                }
            }
            catch (Exception ex)
            {
                _out.WriteLine($"{label} -- FAILED: {ex.Message}");
                failed++;
            }
        }

        // 4. Summary line.
        _out.WriteLine($"[migrate] Completed: {migrated} migrated, {skipped} skipped, {failed} failed.");

        // 5. Non-zero exit when any file failed.
        return failed > 0 ? 1 : 0;
    }

    // -----------------------------------------------------------------------
    // File-level migration
    // -----------------------------------------------------------------------

    private async Task<FileMigrateResult> MigrateFileAsync(string path, CancellationToken ct)
    {
        // 1. Read raw text.
        string rawText = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        byte[] utf8 = Encoding.UTF8.GetBytes(rawText);

        // 2/3. Peek $meta; skip files with no envelope.
        DocumentMeta diskMeta;
        try
        {
            diskMeta = JsonEnvelope.Peek(utf8.AsSpan());
        }
        catch (Exception)
        {
            return FileMigrateResult.Skip("no $meta envelope");
        }

        // 4. Resolve effective target version.
        int effectiveTarget;
        if (_targetVersion < 0)
        {
            try
            {
                effectiveTarget = _services.Registry.GetCurrentVersion(diskMeta.DocType);
            }
            catch (MigrationException)
            {
                return FileMigrateResult.Skip("unknown docType");
            }
        }
        else
        {
            effectiveTarget = _targetVersion;
        }

        // 5. Already at target — nothing to do.
        if (diskMeta.SchemaVersion == effectiveTarget)
            return FileMigrateResult.Skip("already at target");

        // 6. Target version -1 -> use PersistentMigrationAdapter (full sidecar support).
        if (_targetVersion < 0)
        {
            var loadResult = await _services.Persistent.LoadAndMigrateAsync(path, ct)
                .ConfigureAwait(false);

            if (!loadResult.WasMigrated)
                return FileMigrateResult.Skip("no migration required");

            if (!_dryRun)
            {
                await _services.Persistent.SaveAsync(path, loadResult.Dom, loadResult, ct)
                    .ConfigureAwait(false);
            }

            return FileMigrateResult.Success(
                loadResult.OriginalMeta.SchemaVersion,
                loadResult.CurrentMeta.SchemaVersion);
        }

        // 7. Explicit target version -> use Pipeline directly (no sidecar).
        var dom = JsonNode.Parse(rawText)!.AsObject();
        _services.Pipeline.MigrateTo(dom, effectiveTarget, path);

        if (!_dryRun)
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = dom.ToJsonString(opts);
            json = JsonAestheticFormatter.FlattenNumericArrays(json);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct).ConfigureAwait(false);
        }

        var newMeta = JsonEnvelope.Read(dom);
        return FileMigrateResult.Success(diskMeta.SchemaVersion, newMeta.SchemaVersion);
    }

    // -----------------------------------------------------------------------
    // Result type
    // -----------------------------------------------------------------------

    private readonly record struct FileMigrateResult(bool Skipped, string? Reason, int FromVersion, int ToVersion)
    {
        public static FileMigrateResult Skip(string reason) => new(true, reason, 0, 0);
        public static FileMigrateResult Success(int from, int to) => new(false, null, from, to);
    }
}
