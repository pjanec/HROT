using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hrot.Editor.DebugApi;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐ <c>CE-3350</c> — the ONE place the editor's debug-API tests get a port and drive the main-thread queue. 📐 It replaces six private copies of
/// <c>FreePort()</c> (bind 0, read, release) and seven copies of the <c>Send(queue, request)</c> drain loop.
///
/// <para>⚠ <b>Why the port allocator never repeats.</b> <c>DebugApiHost</c> binds <c>http://localhost:{port}/</c> with the managed <c>HttpListener</c>. A port
/// freed by an earlier host in the same long test process and handed out again can be answered by a listener nobody drains — the request is accepted and
/// never answered (the <c>HttpClient.Timeout of 10 s</c> shape measured in whole-project runs). ⇒ a process-wide set of ISSUED ports; the allocator retries
/// until the OS gives a port this process has never issued.</para>
/// </summary>
public static class DebugApiTestHost
{
    private static readonly HashSet<int> Issued = new();

    /// <summary>A free loopback port this process has never handed out before.</summary>
    public static int FreePort()
    {
        lock (Issued)
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                int port = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                if (Issued.Add(port)) return port;
            }
            throw new InvalidOperationException("no never-issued free port after 1000 attempts — the process has issued " + Issued.Count + " ports.");
        }
    }

    /// <summary>Waits for the request, draining the main-thread queue meanwhile (the editor's frame does this), and returns the status and JSON body.</summary>
    public static async Task<(HttpStatusCode Status, JsonNode Body)> Send(MainThreadJobQueue queue, Task<HttpResponseMessage> request)
    {
        // ⭐ CE-3350 — ConfigureAwait(false): the drain must not resume on xUnit's max-concurrency sync context. In a whole-project run that
        //   context's few threads are busy with other tests, the loop stalls, the host's queued job is never drained, and the request is
        //   accepted but never answered (HttpClient's 10 s timeout). The drainer is the only one, so no thread affinity is lost.
        for (int i = 0; i < 4000 && !request.IsCompleted; i++) { queue.DrainAll(); await Task.Delay(5).ConfigureAwait(false); }
        var response = await request.ConfigureAwait(false);
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!);
    }
}
