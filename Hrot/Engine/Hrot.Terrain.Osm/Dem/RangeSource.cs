namespace Hrot.Terrain.Osm.Dem;

/// <summary>Bytes of a remote or local file by range — a cloud-optimised GeoTIFF is read a piece at a time (Terrain From OSM §14.5 E-6).</summary>
public interface IRangeSource
{
    /// <summary>The bytes [offset, offset + length); fewer at the end of the file. Null when the file does not exist (e.g. a sea tile).</summary>
    Task<byte[]?> ReadAsync(string name, long offset, int length, CancellationToken cancellation);
}

/// <summary>Files in a folder (tests, the CLI's <c>--dem-dir</c>).</summary>
public sealed class FolderRangeSource : IRangeSource
{
    private readonly string _folder;
    public FolderRangeSource(string folder) => _folder = folder;

    public async Task<byte[]?> ReadAsync(string name, long offset, int length, CancellationToken cancellation)
    {
        string path = Path.Combine(_folder, name);
        if (!File.Exists(path)) return null;
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        if (offset >= fs.Length) return Array.Empty<byte>();
        fs.Seek(offset, SeekOrigin.Begin);
        var buf = new byte[(int)Math.Min(length, fs.Length - offset)];
        await fs.ReadExactlyAsync(buf, cancellation);
        return buf;
    }
}

/// <summary>HTTP range requests against a base URL; each piece cached on disk so a second preview / import does not download again.</summary>
public sealed class HttpRangeSource : IRangeSource
{
    private readonly HttpClient _http;
    private readonly Func<string, string> _url;
    private readonly string? _cache;

    /// <param name="url">the file's URL from its name</param>
    /// <param name="cacheFolder">null = no disk cache</param>
    public HttpRangeSource(HttpClient http, Func<string, string> url, string? cacheFolder)
    {
        _http = http; _url = url; _cache = cacheFolder;
    }

    public async Task<byte[]?> ReadAsync(string name, long offset, int length, CancellationToken cancellation)
    {
        string? cached = _cache == null ? null : Path.Combine(_cache, $"{name}.{offset}.{length}.bin");
        if (cached != null && File.Exists(cached)) return await File.ReadAllBytesAsync(cached, cancellation);
        if (cached != null && File.Exists(cached + ".missing")) return null;

        for (int attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _url(name));
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + length - 1);
            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellation); }
            catch (HttpRequestException) when (attempt < 3) { await Task.Delay(1000 * attempt * attempt, cancellation); continue; }
            catch (TaskCanceledException) when (!cancellation.IsCancellationRequested && attempt < 3) { await Task.Delay(1000 * attempt * attempt, cancellation); continue; }
            using (resp)
            {
                // a missing tile is open sea (Copernicus publishes no all-sea tiles); S3 answers 403 or 404 for a missing key
                if (resp.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
                {
                    if (cached != null) { Directory.CreateDirectory(_cache!); await File.WriteAllTextAsync(cached + ".missing", "", cancellation); }
                    return null;
                }
                if (((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500) && attempt < 3) { await Task.Delay(1000 * attempt * attempt, cancellation); continue; }
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"elevation service: HTTP {(int)resp.StatusCode} for {name} ({_url(name)})");
                var bytes = await resp.Content.ReadAsByteArrayAsync(cancellation);
                if (resp.StatusCode == System.Net.HttpStatusCode.OK && bytes.Length > length)   // a server that ignored the range
                    bytes = bytes.AsSpan((int)Math.Min(offset, bytes.Length), (int)Math.Min(length, Math.Max(0, bytes.Length - offset))).ToArray();
                if (cached != null) { Directory.CreateDirectory(_cache!); await File.WriteAllBytesAsync(cached, bytes, cancellation); }
                return bytes;
            }
        }
    }
}
