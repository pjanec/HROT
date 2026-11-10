using System.Net;

namespace Hrot.Terrain.Osm;

/// <summary>A failed call to an outside service; the message names the service (§7).</summary>
public sealed class OsmServiceException : IOException
{
    public string Service { get; }
    public OsmServiceException(string service, string message, Exception? inner = null) : base(message, inner) => Service = service;
}

/// <summary>⭐ E-10: the one HTTP client and its retry policy — 3 retries with back-off on 429 / 5xx / timeouts, errors name the service.</summary>
public sealed class HttpRetry
{
    public const string RepoUrl = "https://github.com/pjanec/HROT";
    public static string UserAgent => $"HROT-terrain/{typeof(HttpRetry).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"} (+{RepoUrl})";

    /// <summary>One client per pipeline: the User-Agent, a 60 s timeout, the system proxy (HttpClient's default).</summary>
    public static HttpClient CreateClient(TimeSpan? timeout = null, HttpMessageHandler? handler = null)
    {
        var http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = timeout ?? TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    private readonly HttpClient _http;
    private readonly string _service;
    private readonly int _retries;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">the wait between attempts; tests pass a no-op</param>
    public HttpRetry(HttpClient http, string service, int retries = 3, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http; _service = service; _retries = retries;
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
    }

    /// <summary>Sends the request (made afresh for every attempt); returns a response whose headers are read.</summary>
    public async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken cancellation)
    {
        string last = "";
        for (int attempt = 0; attempt <= _retries; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            TimeSpan wait = TimeSpan.FromSeconds(2 << attempt);   // 2, 4, 8, 16 s
            try
            {
                var resp = await _http.SendAsync(make(), HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return resp;
                var code = resp.StatusCode;
                if (resp.Headers.RetryAfter?.Delta is TimeSpan ra) wait = ra > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : ra;
                resp.Dispose();
                last = $"answered {(int)code} {code}";
                if (code != HttpStatusCode.TooManyRequests && (int)code < 500)
                    throw new OsmServiceException(_service, $"{_service} {last} — the request was refused");
            }
            catch (HttpRequestException e) { last = "could not be reached: " + e.Message; }
            catch (TaskCanceledException) when (!cancellation.IsCancellationRequested) { last = "timed out"; }
            if (attempt < _retries) await _delay(wait, cancellation).ConfigureAwait(false);
            else throw new OsmServiceException(_service, $"{_service} {last} after {_retries + 1} attempts — try again in a minute or two");
        }
        throw new InvalidOperationException("unreachable");
    }
}
