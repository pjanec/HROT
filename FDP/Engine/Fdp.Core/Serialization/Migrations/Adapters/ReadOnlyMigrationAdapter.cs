using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Adapters;

/// <summary>
/// Fast-path migration adapter for read-only consumers (e.g. cluster nodes).
/// Never writes sidecar files. On the fast path (document already at current
/// version) the raw file bytes are returned as a string without DOM allocation.
/// On the slow path the document is migrated in-memory and the resulting DOM
/// is returned.
/// </summary>
public sealed class ReadOnlyMigrationAdapter
{
    private readonly MigrationPipeline _pipeline;

    /// <summary>Creates an adapter backed by the given pipeline.</summary>
    public ReadOnlyMigrationAdapter(MigrationPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    // ---------------------------------------------------------------
    // File path overload
    // ---------------------------------------------------------------

    /// <summary>
    /// Loads the document at <paramref name="path"/> and migrates it if
    /// its schema version is behind the current registered version.
    /// </summary>
    /// <param name="path">Absolute path to the JSON document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ReadOnlyLoadOutcome"/> describing the result.</returns>
    /// <exception cref="MigrationException">
    /// File not found, envelope missing or malformed, unregistered doc type,
    /// or migration failure.
    /// </exception>
    public async Task<ReadOnlyLoadOutcome> LoadAndMigrateAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new MigrationException($"File not found: {path}");

        byte[] utf8;
        try
        {
            utf8 = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new MigrationException(
                $"IO error reading '{path}': {ex.Message}", ex);
        }

        return ProcessBytes(utf8, path);
    }

    // ---------------------------------------------------------------
    // Stream overload
    // ---------------------------------------------------------------

    /// <summary>
    /// Loads a document from <paramref name="stream"/> and migrates it if
    /// its schema version is behind the current registered version.
    /// Non-seekable streams are buffered internally.
    /// </summary>
    /// <param name="stream">Readable stream positioned at the document start.</param>
    /// <param name="sourceId">Identifier used in exception messages (e.g. a logical path).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ReadOnlyLoadOutcome> LoadAndMigrateAsync(
        Stream stream,
        string sourceId,
        CancellationToken ct = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        byte[] utf8;

        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            utf8 = ms.ToArray();
        }
        else
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            utf8 = ms.ToArray();
        }

        return ProcessBytes(utf8, sourceId);
    }

    // ---------------------------------------------------------------
    // Shared processing
    // ---------------------------------------------------------------

    private ReadOnlyLoadOutcome ProcessBytes(byte[] utf8, string sourceId)
    {
        // Streaming peek — no DOM allocation.
        DocumentMeta meta = JsonEnvelope.Peek(utf8.AsSpan());

        int currentVersion = _pipeline.GetCurrentVersion(meta.DocType);

        if (meta.SchemaVersion == currentVersion)
        {
            // Fast path: document is already at the current version.
            return new ReadOnlyLoadOutcome
            {
                Meta = meta,
                WasMigrated = false,
                RawContent = Encoding.UTF8.GetString(utf8),
                MigratedDom = null,
                Report = null
            };
        }

        // Slow path: parse DOM and migrate.
        JsonObject dom;
        try
        {
            dom = JsonNode.Parse(utf8)!.AsObject();
        }
        catch (JsonException ex)
        {
            throw new MigrationException(
                $"Failed to parse JSON from '{sourceId}': {ex.Message}", ex);
        }

        var report = _pipeline.MigrateToCurrent(dom, sourceId);

        // Read updated meta from DOM (schemaVersion was advanced by the pipeline).
        var updatedMeta = JsonEnvelope.Read(dom);

        return new ReadOnlyLoadOutcome
        {
            Meta = updatedMeta,
            WasMigrated = true,
            RawContent = null,
            MigratedDom = dom,
            Report = report
        };
    }
}
