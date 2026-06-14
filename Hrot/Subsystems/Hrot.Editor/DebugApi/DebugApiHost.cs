using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// Lightweight <see cref="HttpListener"/>-based HTTP host for the AI Debug API.
    /// No ASP.NET Core / generic host dependency.
    /// </summary>
    public sealed class DebugApiHost : IDisposable
    {
        private readonly int _port;
        private readonly MainThreadJobQueue _jobQueue;
        private readonly Action _shutdownCallback;
        private readonly HttpListener _listener = new HttpListener();
        private bool _disposed;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public DebugApiHost(int port, MainThreadJobQueue jobQueue, Action shutdownCallback)
        {
            _port = port;
            _jobQueue = jobQueue;
            _shutdownCallback = shutdownCallback;
        }

        /// <summary>Starts the HTTP listener and the background accept loop.</summary>
        public void Start()
        {
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped — exit cleanly.
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleRequestAsync(ctx));
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                var method = ctx.Request.HttpMethod.ToUpperInvariant();
                var path   = ctx.Request.Url?.AbsolutePath ?? "/";

                if (method == "GET" && path == "/status")
                {
                    await WriteJsonResponseAsync(ctx, 200, new ApiResponse(true)).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path == "/shutdown")
                {
                    await WriteJsonResponseAsync(ctx, 200, new ApiResponse(true)).ConfigureAwait(false);
                    _shutdownCallback?.Invoke();
                    return;
                }

                await WriteJsonResponseAsync(ctx, 404, new ApiResponse(false, Error: "Not found")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    await WriteJsonResponseAsync(ctx, 500, new ApiResponse(false, Error: ex.Message)).ConfigureAwait(false);
                }
                catch
                {
                    // ignore — response write failed after headers sent
                }
            }
        }

        private static async Task WriteJsonResponseAsync(HttpListenerContext ctx, int statusCode, object obj)
        {
            var json  = JsonSerializer.Serialize(obj, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            ctx.Response.Close();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>Standard API response envelope.</summary>
    public record ApiResponse(bool Ok, object? Data = null, string? Error = null, bool? Awaited = null);
}
